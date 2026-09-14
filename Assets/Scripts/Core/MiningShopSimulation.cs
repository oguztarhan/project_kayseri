using System;

namespace Game.Core
{
    /// <summary>
    /// One pickaxe table, one carrier and one seller. Advances to job boundaries, independent of frames/views.
    /// MarketService owns the runtime instance and supplies the only sale settlement callback.
    /// </summary>
    public sealed class MiningShopSimulation
    {
        public enum CarrierPhase { Idle, Loading, ToMarket, Unloading, WaitingForSpace, Returning }

        public struct Tuning
        {
            public double CraftSeconds, UnitPrice;
            public int OutputCapacity, ShelfCapacity, CarrierLoad, QueueCapacity;
            public double TravelSeconds, HandlingSeconds, ArrivalSeconds, ServiceSeconds;
            public double SpeedPerLevel, ValuePerLevel, UpgradeBaseCost, UpgradeCostGrowth;
            public int MaxUpgradeLevel;

            public static Tuning Default => new Tuning
            {
                CraftSeconds = 10d, UnitPrice = 20d, OutputCapacity = 4, ShelfCapacity = 4,
                CarrierLoad = 2, QueueCapacity = 6, TravelSeconds = 4d, HandlingSeconds = 0.5d,
                ArrivalSeconds = 3d, ServiceSeconds = 2d, SpeedPerLevel = 0.1d,
                ValuePerLevel = 0.15d, UpgradeBaseCost = 40d, UpgradeCostGrowth = 1.18d,
                MaxUpgradeLevel = 21
            };

            public void Validate()
            {
                if (!Positive(CraftSeconds) || !Positive(UnitPrice) || !Positive(TravelSeconds) ||
                    !Positive(HandlingSeconds) || !Positive(ArrivalSeconds) || !Positive(ServiceSeconds) ||
                    !Positive(SpeedPerLevel) || !Positive(ValuePerLevel) || !Positive(UpgradeBaseCost) ||
                    !Positive(UpgradeCostGrowth) || UpgradeCostGrowth < 1d || OutputCapacity < 1 ||
                    ShelfCapacity < 1 || CarrierLoad < 1 || QueueCapacity < 1 || MaxUpgradeLevel < 1 ||
                    !Positive(1d + SpeedPerLevel * (MaxUpgradeLevel - 1)) ||
                    !Positive(UnitPrice * (1d + ValuePerLevel * (MaxUpgradeLevel - 1))) ||
                    !Positive(UpgradeBaseCost * Math.Pow(UpgradeCostGrowth, MaxUpgradeLevel - 1)))
                    throw new ArgumentException("Mining-shop tuning requires finite positive durations, capacities and prices.");
            }
        }

        public readonly struct Sale
        {
            public readonly string BusinessId;
            public readonly string ProductId;
            public readonly long Sequence;
            public readonly double Cash;

            internal Sale(string businessId, long sequence, double cash)
            {
                BusinessId = businessId;
                ProductId = MiningShopCampaign.ProductIdAt(0);
                Sequence = sequence;
                Cash = cash;
            }
        }

        public readonly struct Snapshot
        {
            public readonly string BusinessId;
            public readonly int OutputStock, PickupReserved, Cargo, ShelfStock, WaitingCustomers;
            public readonly bool Crafting, Serving;
            public readonly double CraftRemaining, CraftDuration, CarrierRemaining, CarrierDuration, ServiceRemaining, ServiceDuration;
            public readonly CarrierPhase Carrier;
            public readonly long Produced, Sold;
            public readonly double Earned, ElapsedSeconds, PendingSeconds;
            public readonly int SpeedLevel, ValueLevel;

            internal Snapshot(MiningShopState s)
            {
                BusinessId = s.BusinessId;
                OutputStock = s.OutputStock; PickupReserved = s.PickupReserved; Cargo = s.Cargo;
                ShelfStock = s.ShelfStock; WaitingCustomers = s.WaitingCustomers;
                Crafting = s.Crafting; Serving = s.Serving; CraftRemaining = s.CraftRemaining;
                CraftDuration = s.CraftDuration; CarrierRemaining = s.CarrierRemaining;
                CarrierDuration = s.CarrierDuration; ServiceRemaining = s.ServiceRemaining; Carrier = s.Carrier;
                ServiceDuration = s.ServiceDuration;
                Produced = s.Produced; Sold = s.Sold; Earned = s.Earned;
                ElapsedSeconds = s.ElapsedSeconds; PendingSeconds = s.PendingSeconds;
                SpeedLevel = s.SpeedLevel; ValueLevel = s.ValueLevel;
            }
        }

        private const int MaxBoundariesPerAdvance = 256;
        private const double Epsilon = 1e-9d;
        private readonly MiningShopState _state;
        private readonly Tuning _tuning;
        private readonly Action<Sale> _settle;
        private bool _advancing;

        public MiningShopSimulation(MiningShopState state, Tuning tuning, Action<Sale> settle)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _settle = settle ?? throw new ArgumentNullException(nameof(settle));
            tuning.Validate();
            _tuning = tuning;
            ValidateState();
            ScheduleJobs();
        }

        public Snapshot View => new Snapshot(_state);
        public double CraftSeconds => _tuning.CraftSeconds / (1d + _tuning.SpeedPerLevel * (_state.SpeedLevel - 1));
        public double UnitPrice => _tuning.UnitPrice * (1d + _tuning.ValuePerLevel * (_state.ValueLevel - 1));
        public double UpgradeCost(bool speed)
        {
            int level = speed ? _state.SpeedLevel : _state.ValueLevel;
            return level >= _tuning.MaxUpgradeLevel ? 0d : Math.Ceiling(_tuning.UpgradeBaseCost * Math.Pow(_tuning.UpgradeCostGrowth, level - 1));
        }

        /// <summary>Caller pays once before exposing this mutation. Zero cost means the track is capped.</summary>
        public bool Upgrade(bool speed)
        {
            if (UpgradeCost(speed) <= 0d) return false;
            if (speed)
            {
                double fractionLeft = _state.Crafting ? _state.CraftRemaining / _state.CraftDuration : 0d;
                _state.SpeedLevel++;
                if (_state.Crafting)
                {
                    _state.CraftDuration = CraftSeconds;
                    _state.CraftRemaining = fractionLeft * _state.CraftDuration;
                }
            }
            else _state.ValueLevel++;
            return true;
        }

        /// <summary>
        /// Foreground elapsed time only. Work is bounded per call; unprocessed time remains in the save.
        /// Advance(0) drains that debt. No clock reads, replay on load, allocations or animation callbacks.
        /// </summary>
        public void Advance(double seconds)
        {
            if (!NonNegative(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (_advancing) return;
            double pending = _state.PendingSeconds + seconds;
            if (!NonNegative(pending)) throw new ArgumentOutOfRangeException(nameof(seconds));
            _advancing = true;
            try
            {
                _state.PendingSeconds = pending;
                ScheduleJobs();
                for (int boundary = 0; boundary < MaxBoundariesPerAdvance && _state.PendingSeconds > 0d; boundary++)
                {
                    double step = Math.Min(_state.PendingSeconds, _state.ArrivalRemaining);
                    if (_state.Crafting) step = Math.Min(step, _state.CraftRemaining);
                    if (CarrierTimed) step = Math.Min(step, _state.CarrierRemaining);
                    if (_state.Serving) step = Math.Min(step, _state.ServiceRemaining);
                    _state.PendingSeconds = Math.Max(0d, _state.PendingSeconds - step);
                    _state.ElapsedSeconds += step;
                    _state.ArrivalRemaining -= step;
                    if (_state.Crafting) _state.CraftRemaining -= step;
                    if (CarrierTimed) _state.CarrierRemaining -= step;
                    if (_state.Serving) _state.ServiceRemaining -= step;
                    CompleteJobs();
                    ScheduleJobs();
                }
            }
            finally { _advancing = false; }
        }

        private bool CarrierTimed => _state.Carrier != CarrierPhase.Idle && _state.Carrier != CarrierPhase.WaitingForSpace;

        private void ScheduleJobs()
        {
            if (_state.ArrivalRemaining <= 0d) _state.ArrivalRemaining = _tuning.ArrivalSeconds;
            if (!_state.Crafting && (long)_state.OutputStock + _state.PickupReserved < _tuning.OutputCapacity)
            {
                _state.Crafting = true;
                _state.CraftDuration = CraftSeconds;
                _state.CraftRemaining = _state.CraftDuration;
            }
            if (!_state.Serving && _state.WaitingCustomers > 0 && _state.ShelfStock > 0)
            {
                _state.WaitingCustomers--;
                _state.ShelfStock--;
                _state.Serving = true;
                _state.ServiceRemaining = _tuning.ServiceSeconds;
                _state.ServiceDuration = _tuning.ServiceSeconds;
                _state.ServicePrice = UnitPrice;
            }
            if (_state.Carrier == CarrierPhase.Idle && _state.OutputStock > 0)
            {
                int room = Math.Max(0, _tuning.ShelfCapacity - _state.ShelfStock);
                int take = Math.Min(_state.OutputStock, Math.Min(room, _tuning.CarrierLoad));
                if (take > 0)
                {
                    _state.OutputStock -= take;
                    _state.PickupReserved = take;
                    _state.DestinationReserved = take;
                    SetCarrier(CarrierPhase.Loading, _tuning.HandlingSeconds);
                }
            }
            else if (_state.Carrier == CarrierPhase.WaitingForSpace && _state.ShelfStock < _tuning.ShelfCapacity)
                SetCarrier(CarrierPhase.Unloading, _tuning.HandlingSeconds);

        }

        private void CompleteJobs()
        {
            if (_state.Crafting && _state.CraftRemaining <= Epsilon)
            {
                _state.CraftRemaining = 0d;
                _state.Crafting = false;
                _state.OutputStock++;
                _state.Produced++;
            }
            if (CarrierTimed && _state.CarrierRemaining <= Epsilon)
            {
                switch (_state.Carrier)
                {
                    case CarrierPhase.Loading:
                        _state.Cargo = _state.PickupReserved;
                        _state.PickupReserved = 0;
                        SetCarrier(CarrierPhase.ToMarket, _tuning.TravelSeconds);
                        break;
                    case CarrierPhase.ToMarket:
                        SetCarrier(CarrierPhase.Unloading, _tuning.HandlingSeconds);
                        break;
                    case CarrierPhase.Unloading:
                        int accepted = Math.Min(_state.Cargo, Math.Max(0, _tuning.ShelfCapacity - _state.ShelfStock));
                        _state.ShelfStock += accepted;
                        _state.Cargo -= accepted;
                        _state.DestinationReserved -= accepted;
                        SetCarrier(_state.Cargo == 0 ? CarrierPhase.Returning : CarrierPhase.WaitingForSpace,
                            _state.Cargo == 0 ? _tuning.TravelSeconds : 0d);
                        break;
                    case CarrierPhase.Returning:
                        SetCarrier(CarrierPhase.Idle, 0d);
                        break;
                }
            }
            if (_state.ArrivalRemaining <= Epsilon)
            {
                _state.ArrivalRemaining = _tuning.ArrivalSeconds;
                if (_state.WaitingCustomers < _tuning.QueueCapacity) _state.WaitingCustomers++;
            }
            if (_state.Serving && _state.ServiceRemaining <= Epsilon)
            {
                double price = _state.ServicePrice;
                _state.Serving = false;
                _state.ServiceRemaining = 0d;
                _state.ServicePrice = 0d;
                _state.Sold++;
                _state.Earned += price;
                _settle(new Sale(_state.BusinessId, _state.Sold, price));
            }
        }

        private void SetCarrier(CarrierPhase phase, double duration)
        {
            _state.Carrier = phase;
            _state.CarrierDuration = duration;
            _state.CarrierRemaining = duration;
        }

        private void ValidateState()
        {
            if (string.IsNullOrWhiteSpace(_state.BusinessId) || _state.SpeedLevel < 1 || _state.ValueLevel < 1 ||
                _state.SpeedLevel > _tuning.MaxUpgradeLevel || _state.ValueLevel > _tuning.MaxUpgradeLevel ||
                !NonNegative(_state.ElapsedSeconds) || !NonNegative(_state.PendingSeconds) ||
                !NonNegative(_state.CraftRemaining) || !NonNegative(_state.CraftDuration) ||
                !NonNegative(_state.CarrierRemaining) || !NonNegative(_state.CarrierDuration) ||
                !NonNegative(_state.ArrivalRemaining) || !NonNegative(_state.ServiceRemaining) ||
                !NonNegative(_state.ServiceDuration) ||
                !NonNegative(_state.ServicePrice) || !NonNegative(_state.Earned) ||
                _state.OutputStock < 0 || _state.PickupReserved < 0 || _state.Cargo < 0 ||
                _state.ShelfStock < 0 || _state.WaitingCustomers < 0 || _state.Produced < 0 || _state.Sold < 0 ||
                (int)_state.Carrier < (int)CarrierPhase.Idle || (int)_state.Carrier > (int)CarrierPhase.Returning ||
                (_state.Crafting && (!Positive(_state.CraftDuration) || _state.CraftRemaining > _state.CraftDuration)) ||
                (CarrierTimed && (!Positive(_state.CarrierDuration) || _state.CarrierRemaining > _state.CarrierDuration)) ||
                (_state.Serving && (!Positive(_state.ServicePrice) || !Positive(_state.ServiceDuration) ||
                    _state.ServiceRemaining > _state.ServiceDuration)) ||
                _state.DestinationReserved != (long)_state.PickupReserved + _state.Cargo ||
                (_state.PickupReserved > 0 && _state.Carrier != CarrierPhase.Loading) ||
                (_state.Carrier == CarrierPhase.Loading && (_state.PickupReserved == 0 || _state.Cargo != 0)) ||
                (_state.Cargo > 0 && (_state.Carrier == CarrierPhase.Idle || _state.Carrier == CarrierPhase.Returning)) ||
                ((_state.Carrier == CarrierPhase.ToMarket || _state.Carrier == CarrierPhase.Unloading ||
                    _state.Carrier == CarrierPhase.WaitingForSpace) && _state.Cargo == 0) ||
                _state.Produced - _state.Sold != (long)_state.OutputStock + _state.PickupReserved + _state.Cargo +
                    _state.ShelfStock + (_state.Serving ? 1L : 0L))
                throw new ArgumentException("Mining-shop save has invalid jobs or inventory; refusing to reset or discard it.");
        }

        private static bool Positive(double value) => value > 0d && !double.IsInfinity(value) && !double.IsNaN(value);
        private static bool NonNegative(double value) => value >= 0d && !double.IsInfinity(value) && !double.IsNaN(value);
    }
}
