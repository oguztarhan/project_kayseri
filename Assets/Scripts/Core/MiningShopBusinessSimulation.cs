using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// Deterministic four-product business model. Tables own their product stock; one carrier and one seller
    /// are intentionally shared, so opening helmet, lantern and bag lines cannot create extra logistics or
    /// sale capacity. This model is not yet selected by the live 1-1 service.
    /// </summary>
    public sealed class MiningShopBusinessSimulation
    {
        public struct ProductTuning
        {
            public string ProductId;
            public double CraftSeconds;
            public double UnitPrice;
            public double TableCost;
        }

        public struct Tuning
        {
            public ProductTuning[] Products;
            public int OutputCapacity;
            public int ShelfCapacityPerProduct;
            public int CarrierLoad;
            public int QueueCapacity;
            public double TravelSeconds;
            public double HandlingSeconds;
            public double ArrivalSeconds;
            public double ServiceSeconds;
            public double SpeedPerLevel;
            public double ValuePerLevel;
            public double UpgradeBaseCost;
            public double UpgradeCostGrowth;
            public int MaxUpgradeLevel;

            public static Tuning Default => new Tuning
            {
                Products = new[]
                {
                    new ProductTuning { ProductId = MiningShopCampaign.ProductIdAt(0), CraftSeconds = 10d, UnitPrice = 20d, TableCost = 0d },
                    new ProductTuning { ProductId = MiningShopCampaign.ProductIdAt(1), CraftSeconds = 20d, UnitPrice = 60d, TableCost = 300d },
                    new ProductTuning { ProductId = MiningShopCampaign.ProductIdAt(2), CraftSeconds = 40d, UnitPrice = 150d, TableCost = 1400d },
                    new ProductTuning { ProductId = MiningShopCampaign.ProductIdAt(3), CraftSeconds = 60d, UnitPrice = 360d, TableCost = 5000d }
                },
                OutputCapacity = 4,
                ShelfCapacityPerProduct = 4,
                CarrierLoad = 1,
                QueueCapacity = 6,
                TravelSeconds = 4d,
                HandlingSeconds = 0.5d,
                ArrivalSeconds = 10d,
                ServiceSeconds = 2d,
                SpeedPerLevel = 0.1d,
                ValuePerLevel = 0.15d,
                UpgradeBaseCost = 40d,
                UpgradeCostGrowth = 1.18d,
                MaxUpgradeLevel = 21
            };

            public void Validate()
            {
                if (Products == null || Products.Length != MiningShopCampaign.ProductCount || OutputCapacity < 1 ||
                    ShelfCapacityPerProduct < 1 || CarrierLoad < 1 || QueueCapacity < 1 || MaxUpgradeLevel < 1 ||
                    !MiningShopBusinessSimulation.Positive(TravelSeconds) || !MiningShopBusinessSimulation.Positive(HandlingSeconds) || !MiningShopBusinessSimulation.Positive(ArrivalSeconds) ||
                    !MiningShopBusinessSimulation.Positive(ServiceSeconds) || !MiningShopBusinessSimulation.Positive(SpeedPerLevel) || !MiningShopBusinessSimulation.Positive(ValuePerLevel) ||
                    !MiningShopBusinessSimulation.Positive(UpgradeBaseCost) || !MiningShopBusinessSimulation.Positive(UpgradeCostGrowth) || UpgradeCostGrowth < 1d)
                    throw new ArgumentException("Mining-shop business tuning requires finite positive tables, timings and capacities.");

                for (int i = 0; i < Products.Length; i++)
                {
                    ProductTuning product = Products[i];
                    if (product.ProductId != MiningShopCampaign.ProductIdAt(i) || !MiningShopBusinessSimulation.Positive(product.CraftSeconds) ||
                        !MiningShopBusinessSimulation.Positive(product.UnitPrice) || !MiningShopBusinessSimulation.NonNegative(product.TableCost))
                        throw new ArgumentException("Mining-shop product tuning must use the authored merchandise order and finite values.");
                }
            }
        }

        public readonly struct Sale
        {
            public readonly string BusinessId;
            public readonly string ProductId;
            public readonly long Sequence;
            public readonly double Cash;

            internal Sale(string businessId, string productId, long sequence, double cash)
            {
                BusinessId = businessId;
                ProductId = productId;
                Sequence = sequence;
                Cash = cash;
            }
        }

        public readonly struct ProductSnapshot
        {
            private readonly MiningShopProductLineState _line;
            private readonly ProductTuning _tuning;
            private readonly int _index;

            internal ProductSnapshot(MiningShopProductLineState line, ProductTuning tuning, int index)
            {
                _line = line;
                _tuning = tuning;
                _index = index;
            }

            public int Index => _index;
            public string ProductId => _line.ProductId;
            public bool TableBuilt => _line.TableBuilt;
            public int SpeedLevel => _line.SpeedLevel;
            public int ValueLevel => _line.ValueLevel;
            public bool Crafting => _line.Crafting;
            public double CraftRemaining => _line.CraftRemaining;
            public double CraftDuration => _line.CraftDuration;
            public int OutputStock => _line.OutputStock;
            public int PickupReserved => _line.PickupReserved;
            public int ShelfStock => _line.ShelfStock;
            public int WaitingCustomers => _line.WaitingCustomers;
            public long Produced => _line.Produced;
            public long Sold => _line.Sold;
            public double Earned => _line.Earned;
            public double TableCost => _tuning.TableCost;
        }

        public readonly struct Snapshot
        {
            private readonly MiningShopBusinessState _business;
            private readonly Tuning _tuning;
            private readonly string _businessId;

            internal Snapshot(string businessId, MiningShopBusinessState business, Tuning tuning)
            {
                _businessId = businessId;
                _business = business;
                _tuning = tuning;
            }

            public string BusinessId => _businessId;
            public int AvailableProductCount => _business.AvailableProductCount;
            public int BuiltTableCount
            {
                get
                {
                    int count = 0;
                    for (int i = 0; i < _business.Lines.Count; i++) if (_business.Lines[i].TableBuilt) count++;
                    return count;
                }
            }
            public MiningShopSimulation.CarrierPhase Carrier => _business.Carrier;
            public int CarrierProductIndex => _business.CarrierProductIndex;
            public int CarrierCount => _business.CarrierCount;
            public double CarrierRemaining => _business.CarrierRemaining;
            public double CarrierDuration => _business.CarrierDuration;
            public bool Serving => _business.Serving;
            public int ServiceProductIndex => _business.ServiceProductIndex;
            public double ServiceRemaining => _business.ServiceRemaining;
            public double ServiceDuration => _business.ServiceDuration;
            public double PendingSeconds => _business.PendingSeconds;
            public double ElapsedSeconds => _business.ElapsedSeconds;
            public long ReceiptSequence => _business.ReceiptSequence;
            public int WaitingCustomerCount
            {
                get
                {
                    int count = 0;
                    for (int i = 0; i < _business.Lines.Count; i++) count += _business.Lines[i].WaitingCustomers;
                    return count;
                }
            }

            public ProductSnapshot ProductAt(int index)
            {
                if (index < 0 || index >= _business.Lines.Count) throw new ArgumentOutOfRangeException(nameof(index));
                return new ProductSnapshot(_business.Lines[index], _tuning.Products[index], index);
            }
        }

        private const int MaxBoundariesPerAdvance = 256;
        private const double Epsilon = 1e-9d;
        private readonly MiningShopState _owner;
        private readonly MiningShopBusinessState _state;
        private readonly Tuning _tuning;
        private readonly Action<Sale> _settle;
        private bool _advancing;

        /// <summary>
        /// Opens one authored business. <paramref name="availableProductCount"/> is its campaign allowance,
        /// not a grant of built tables. A legacy pickaxe record is copied once into the first product line.
        /// </summary>
        public MiningShopBusinessSimulation(MiningShopState owner, int availableProductCount, Tuning tuning, Action<Sale> settle)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _settle = settle ?? throw new ArgumentNullException(nameof(settle));
            if (availableProductCount < 1 || availableProductCount > MiningShopCampaign.ProductCount)
                throw new ArgumentOutOfRangeException(nameof(availableProductCount));
            tuning.Validate();
            _tuning = tuning;
            _state = EnsureBusinessState(owner, availableProductCount);
            ValidateState();
            ScheduleJobs();
        }

        public Snapshot View => new Snapshot(_owner.BusinessId, _state, _tuning);
        public double CraftSeconds(int productIndex) => _tuning.Products[productIndex].CraftSeconds /
            (1d + _tuning.SpeedPerLevel * (Line(productIndex).SpeedLevel - 1));
        public double UnitPrice(int productIndex) => _tuning.Products[productIndex].UnitPrice *
            (1d + _tuning.ValuePerLevel * (Line(productIndex).ValueLevel - 1));
        public double TableCost(int productIndex) => _tuning.Products[productIndex].TableCost;

        public double UpgradeCost(int productIndex, bool speed)
        {
            MiningShopProductLineState line = Line(productIndex);
            int level = speed ? line.SpeedLevel : line.ValueLevel;
            return level >= _tuning.MaxUpgradeLevel ? 0d :
                Math.Ceiling(_tuning.UpgradeBaseCost * Math.Pow(_tuning.UpgradeCostGrowth, level - 1));
        }

        /// <summary>Pure build mutation. Its caller validates and spends <see cref="TableCost"/> atomically.</summary>
        public bool BuildTable(int productIndex)
        {
            MiningShopProductLineState line = Line(productIndex);
            if (productIndex == 0 || productIndex >= _state.AvailableProductCount || line.TableBuilt ||
                !PreviousTablesBuilt(productIndex)) return false;
            line.TableBuilt = true;
            ScheduleJobs();
            return true;
        }

        /// <summary>Pure upgrade mutation. Its caller validates and spends <see cref="UpgradeCost"/> atomically.</summary>
        public bool Upgrade(int productIndex, bool speed)
        {
            MiningShopProductLineState line = Line(productIndex);
            if (!line.TableBuilt || UpgradeCost(productIndex, speed) <= 0d) return false;
            if (speed)
            {
                double fractionLeft = line.Crafting ? line.CraftRemaining / line.CraftDuration : 0d;
                line.SpeedLevel++;
                if (line.Crafting)
                {
                    line.CraftDuration = CraftSeconds(productIndex);
                    line.CraftRemaining = fractionLeft * line.CraftDuration;
                }
            }
            else line.ValueLevel++;
            return true;
        }

        /// <summary>Foreground time only. Bounded event processing preserves outstanding work in the save.</summary>
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
                    double step = NextBoundary();
                    _state.PendingSeconds = Math.Max(0d, _state.PendingSeconds - step);
                    _state.ElapsedSeconds += step;
                    _state.ArrivalRemaining -= step;
                    for (int i = 0; i < _state.Lines.Count; i++)
                    {
                        MiningShopProductLineState line = _state.Lines[i];
                        if (line.Crafting) line.CraftRemaining -= step;
                    }
                    if (CarrierTimed) _state.CarrierRemaining -= step;
                    if (_state.Serving) _state.ServiceRemaining -= step;
                    CompleteJobs();
                    ScheduleJobs();
                }
            }
            finally { _advancing = false; }
        }

        private double NextBoundary()
        {
            double step = Math.Min(_state.PendingSeconds, _state.ArrivalRemaining);
            for (int i = 0; i < _state.Lines.Count; i++)
                if (_state.Lines[i].Crafting) step = Math.Min(step, _state.Lines[i].CraftRemaining);
            if (CarrierTimed) step = Math.Min(step, _state.CarrierRemaining);
            if (_state.Serving) step = Math.Min(step, _state.ServiceRemaining);
            return step;
        }

        private bool CarrierTimed => _state.Carrier != MiningShopSimulation.CarrierPhase.Idle &&
            _state.Carrier != MiningShopSimulation.CarrierPhase.WaitingForSpace;

        private void ScheduleJobs()
        {
            if (_state.ArrivalRemaining <= 0d) _state.ArrivalRemaining = _tuning.ArrivalSeconds;
            for (int i = 0; i < _state.AvailableProductCount; i++)
            {
                MiningShopProductLineState line = _state.Lines[i];
                if (line.TableBuilt && !line.Crafting && line.OutputStock + line.PickupReserved < _tuning.OutputCapacity)
                {
                    line.Crafting = true;
                    line.CraftDuration = CraftSeconds(i);
                    line.CraftRemaining = line.CraftDuration;
                }
            }

            if (!_state.Serving)
            {
                int sell = FindSellableLine();
                if (sell >= 0)
                {
                    MiningShopProductLineState line = _state.Lines[sell];
                    line.WaitingCustomers--;
                    line.ShelfStock--;
                    _state.Serving = true;
                    _state.ServiceProductIndex = sell;
                    _state.ServiceDuration = _tuning.ServiceSeconds;
                    _state.ServiceRemaining = _tuning.ServiceSeconds;
                    _state.ServicePrice = UnitPrice(sell);
                }
            }

            if (_state.Carrier == MiningShopSimulation.CarrierPhase.Idle)
            {
                int pickup = FindCarrierLine();
                if (pickup >= 0)
                {
                    MiningShopProductLineState line = _state.Lines[pickup];
                    int room = Math.Max(0, _tuning.ShelfCapacityPerProduct - line.ShelfStock - line.DestinationReserved);
                    int take = Math.Min(line.OutputStock, Math.Min(room, _tuning.CarrierLoad));
                    line.OutputStock -= take;
                    line.PickupReserved = take;
                    line.DestinationReserved = take;
                    _state.CarrierProductIndex = pickup;
                    SetCarrier(MiningShopSimulation.CarrierPhase.Loading, _tuning.HandlingSeconds);
                }
            }
            else if (_state.Carrier == MiningShopSimulation.CarrierPhase.WaitingForSpace)
            {
                MiningShopProductLineState line = CarrierLine();
                if (line.ShelfStock < _tuning.ShelfCapacityPerProduct) SetCarrier(MiningShopSimulation.CarrierPhase.Unloading, _tuning.HandlingSeconds);
            }
        }

        private void CompleteJobs()
        {
            for (int i = 0; i < _state.AvailableProductCount; i++)
            {
                MiningShopProductLineState line = _state.Lines[i];
                if (line.Crafting && line.CraftRemaining <= Epsilon)
                {
                    line.CraftRemaining = 0d;
                    line.Crafting = false;
                    line.OutputStock++;
                    line.Produced++;
                }
            }

            if (CarrierTimed && _state.CarrierRemaining <= Epsilon)
            {
                MiningShopProductLineState line = CarrierLine();
                switch (_state.Carrier)
                {
                    case MiningShopSimulation.CarrierPhase.Loading:
                        _state.CarrierCount = line.PickupReserved;
                        line.PickupReserved = 0;
                        SetCarrier(MiningShopSimulation.CarrierPhase.ToMarket, _tuning.TravelSeconds);
                        break;
                    case MiningShopSimulation.CarrierPhase.ToMarket:
                        SetCarrier(MiningShopSimulation.CarrierPhase.Unloading, _tuning.HandlingSeconds);
                        break;
                    case MiningShopSimulation.CarrierPhase.Unloading:
                        int accepted = Math.Min(_state.CarrierCount, Math.Max(0, _tuning.ShelfCapacityPerProduct - line.ShelfStock));
                        line.ShelfStock += accepted;
                        _state.CarrierCount -= accepted;
                        line.DestinationReserved -= accepted;
                        SetCarrier(_state.CarrierCount == 0 ? MiningShopSimulation.CarrierPhase.Returning :
                            MiningShopSimulation.CarrierPhase.WaitingForSpace,
                            _state.CarrierCount == 0 ? _tuning.TravelSeconds : 0d);
                        break;
                    case MiningShopSimulation.CarrierPhase.Returning:
                        _state.CarrierProductIndex = -1;
                        SetCarrier(MiningShopSimulation.CarrierPhase.Idle, 0d);
                        break;
                }
            }

            if (_state.ArrivalRemaining <= Epsilon)
            {
                _state.ArrivalRemaining = _tuning.ArrivalSeconds;
                if (WaitingCustomerCount() < _tuning.QueueCapacity)
                {
                    int demand = NextBuiltLine(_state.DemandCursor);
                    if (demand >= 0)
                    {
                        _state.Lines[demand].WaitingCustomers++;
                        _state.DemandCursor = (demand + 1) % _state.AvailableProductCount;
                    }
                }
            }

            if (_state.Serving && _state.ServiceRemaining <= Epsilon)
            {
                MiningShopProductLineState line = _state.Lines[_state.ServiceProductIndex];
                double price = _state.ServicePrice;
                _state.Serving = false;
                _state.ServiceProductIndex = -1;
                _state.ServiceRemaining = 0d;
                _state.ServicePrice = 0d;
                line.Sold++;
                line.Earned += price;
                _state.ReceiptSequence++;
                _settle(new Sale(_owner.BusinessId, line.ProductId, _state.ReceiptSequence, price));
            }
        }

        private int FindCarrierLine()
        {
            for (int pass = 0; pass < 2; pass++)
                for (int offset = 0; offset < _state.AvailableProductCount; offset++)
                {
                    int index = (_state.CarrierCursor + offset) % _state.AvailableProductCount;
                    MiningShopProductLineState line = _state.Lines[index];
                    int shelfRoom = _tuning.ShelfCapacityPerProduct - line.ShelfStock - line.DestinationReserved;
                    bool nearlyFull = line.OutputStock + line.PickupReserved >= _tuning.OutputCapacity - 1;
                    if (line.TableBuilt && line.OutputStock > 0 && shelfRoom > 0 && (pass == 1 || nearlyFull))
                    {
                        _state.CarrierCursor = (index + 1) % _state.AvailableProductCount;
                        return index;
                    }
                }
            return -1;
        }

        private int FindSellableLine()
        {
            for (int offset = 0; offset < _state.AvailableProductCount; offset++)
            {
                int index = (_state.SellerCursor + offset) % _state.AvailableProductCount;
                MiningShopProductLineState line = _state.Lines[index];
                if (line.TableBuilt && line.WaitingCustomers > 0 && line.ShelfStock > 0)
                {
                    _state.SellerCursor = (index + 1) % _state.AvailableProductCount;
                    return index;
                }
            }
            return -1;
        }

        private int NextBuiltLine(int start)
        {
            for (int offset = 0; offset < _state.AvailableProductCount; offset++)
            {
                int index = (start + offset) % _state.AvailableProductCount;
                if (_state.Lines[index].TableBuilt) return index;
            }
            return -1;
        }

        private int WaitingCustomerCount()
        {
            int count = 0;
            for (int i = 0; i < _state.Lines.Count; i++) count += _state.Lines[i].WaitingCustomers;
            return count;
        }

        private bool PreviousTablesBuilt(int productIndex)
        {
            for (int i = 0; i < productIndex; i++) if (!_state.Lines[i].TableBuilt) return false;
            return true;
        }

        private MiningShopProductLineState Line(int productIndex)
        {
            if (productIndex < 0 || productIndex >= MiningShopCampaign.ProductCount) throw new ArgumentOutOfRangeException(nameof(productIndex));
            return _state.Lines[productIndex];
        }

        private MiningShopProductLineState CarrierLine()
        {
            if (_state.CarrierProductIndex < 0 || _state.CarrierProductIndex >= _state.Lines.Count)
                throw new ArgumentException("Mining-shop carrier has no valid product assignment.");
            return _state.Lines[_state.CarrierProductIndex];
        }

        private void SetCarrier(MiningShopSimulation.CarrierPhase phase, double duration)
        {
            _state.Carrier = phase;
            _state.CarrierDuration = duration;
            _state.CarrierRemaining = duration;
        }

        private MiningShopBusinessState EnsureBusinessState(MiningShopState owner, int availableProductCount)
        {
            if (string.IsNullOrWhiteSpace(owner.BusinessId)) throw new ArgumentException("Mining-shop business requires an ID.");
            // JsonUtility materializes a serializable nested object even when a save was written
            // before the Business field existed. An object with no product lines is therefore the
            // old flat pickaxe record, not a valid initialized business.
            if (owner.Business != null && owner.Business.Lines != null && owner.Business.Lines.Count > 0)
                return owner.Business;

            var business = new MiningShopBusinessState { AvailableProductCount = availableProductCount };
            for (int i = 0; i < MiningShopCampaign.ProductCount; i++)
            {
                var line = new MiningShopProductLineState
                {
                    ProductId = _tuning.Products[i].ProductId,
                    TableBuilt = i == 0,
                    SpeedLevel = 1,
                    ValueLevel = 1
                };
                business.Lines.Add(line);
            }

            // The active pickaxe record is an additive predecessor. Copy, never reinterpret or clear it.
            MiningShopProductLineState pickaxe = business.Lines[0];
            pickaxe.SpeedLevel = owner.SpeedLevel;
            pickaxe.ValueLevel = owner.ValueLevel;
            pickaxe.Crafting = owner.Crafting;
            pickaxe.CraftRemaining = owner.CraftRemaining;
            pickaxe.CraftDuration = owner.CraftDuration;
            pickaxe.OutputStock = owner.OutputStock;
            pickaxe.PickupReserved = owner.PickupReserved;
            pickaxe.DestinationReserved = owner.DestinationReserved;
            pickaxe.ShelfStock = owner.ShelfStock;
            pickaxe.WaitingCustomers = owner.WaitingCustomers;
            pickaxe.Produced = owner.Produced;
            pickaxe.Sold = owner.Sold;
            pickaxe.Earned = owner.Earned;
            business.ElapsedSeconds = owner.ElapsedSeconds;
            business.PendingSeconds = owner.PendingSeconds;
            business.Carrier = owner.Carrier;
            business.CarrierProductIndex = owner.Carrier == MiningShopSimulation.CarrierPhase.Idle ? -1 : 0;
            business.CarrierCount = owner.Cargo;
            business.CarrierRemaining = owner.CarrierRemaining;
            business.CarrierDuration = owner.CarrierDuration;
            business.ArrivalRemaining = owner.ArrivalRemaining;
            business.Serving = owner.Serving;
            business.ServiceProductIndex = owner.Serving ? 0 : -1;
            business.ServiceRemaining = owner.ServiceRemaining;
            business.ServiceDuration = owner.ServiceDuration;
            business.ServicePrice = owner.ServicePrice;
            business.ReceiptSequence = owner.Sold;
            owner.Business = business;
            return business;
        }

        private void ValidateState()
        {
            if (_state.AvailableProductCount < 1 || _state.AvailableProductCount > MiningShopCampaign.ProductCount ||
                _state.Lines == null || _state.Lines.Count != MiningShopCampaign.ProductCount ||
                !NonNegative(_state.ElapsedSeconds) || !NonNegative(_state.PendingSeconds) ||
                !NonNegative(_state.ArrivalRemaining) || !NonNegative(_state.CarrierRemaining) ||
                !NonNegative(_state.CarrierDuration) || !NonNegative(_state.ServiceRemaining) ||
                !NonNegative(_state.ServiceDuration) || !NonNegative(_state.ServicePrice) || _state.CarrierCount < 0 ||
                _state.ReceiptSequence < 0 || _state.CarrierCursor < 0 || _state.CarrierCursor >= _state.AvailableProductCount ||
                _state.DemandCursor < 0 || _state.DemandCursor >= _state.AvailableProductCount ||
                _state.SellerCursor < 0 || _state.SellerCursor >= _state.AvailableProductCount ||
                (int)_state.Carrier < (int)MiningShopSimulation.CarrierPhase.Idle ||
                (int)_state.Carrier > (int)MiningShopSimulation.CarrierPhase.Returning ||
                (_state.Carrier == MiningShopSimulation.CarrierPhase.Idle &&
                    (_state.CarrierProductIndex != -1 || _state.CarrierCount != 0)) ||
                (_state.Carrier != MiningShopSimulation.CarrierPhase.Idle &&
                    (_state.CarrierProductIndex < 0 || _state.CarrierProductIndex >= _state.AvailableProductCount)) ||
                (_state.Serving && (_state.ServiceProductIndex < 0 || _state.ServiceProductIndex >= _state.AvailableProductCount ||
                    !Positive(_state.ServicePrice) || !Positive(_state.ServiceDuration) || _state.ServiceRemaining > _state.ServiceDuration)) ||
                (!_state.Serving && _state.ServiceProductIndex != -1))
                throw new ArgumentException("Mining-shop business save has invalid shared jobs; refusing to reset it.");

            int totalReserved = 0;
            for (int i = 0; i < _state.Lines.Count; i++)
            {
                MiningShopProductLineState line = _state.Lines[i];
                if (line == null || line.ProductId != _tuning.Products[i].ProductId || (i >= _state.AvailableProductCount && line.TableBuilt) ||
                    (i == 0 && !line.TableBuilt) || line.SpeedLevel < 1 || line.ValueLevel < 1 ||
                    line.SpeedLevel > _tuning.MaxUpgradeLevel || line.ValueLevel > _tuning.MaxUpgradeLevel ||
                    line.OutputStock < 0 || line.PickupReserved < 0 || line.DestinationReserved < 0 || line.ShelfStock < 0 ||
                    line.WaitingCustomers < 0 || line.Produced < 0 || line.Sold < 0 || !NonNegative(line.Earned) ||
                    !NonNegative(line.CraftRemaining) || !NonNegative(line.CraftDuration) ||
                    (line.Crafting && (!Positive(line.CraftDuration) || line.CraftRemaining > line.CraftDuration)) ||
                    (!line.TableBuilt && (line.Crafting || line.OutputStock > 0 || line.PickupReserved > 0 ||
                        line.DestinationReserved > 0 || line.ShelfStock > 0 || line.WaitingCustomers > 0 || line.Produced > 0 || line.Sold > 0)) ||
                    line.Produced - line.Sold != (long)line.OutputStock + line.PickupReserved + line.ShelfStock +
                        (_state.CarrierProductIndex == i ? _state.CarrierCount : 0) +
                        (_state.Serving && _state.ServiceProductIndex == i ? 1L : 0L))
                    throw new ArgumentException("Mining-shop product-line save has invalid inventory; refusing to reset it.");
                totalReserved += line.PickupReserved + (_state.CarrierProductIndex == i ? _state.CarrierCount : 0);
                if (line.DestinationReserved != (line.PickupReserved + (_state.CarrierProductIndex == i ? _state.CarrierCount : 0)))
                    throw new ArgumentException("Mining-shop product-line reservation does not match its cargo.");
            }

            if (totalReserved < 0) throw new ArgumentException("Mining-shop reservation total overflowed.");
        }

        private static bool Positive(double value) => value > 0d && !double.IsInfinity(value) && !double.IsNaN(value);
        private static bool NonNegative(double value) => value >= 0d && !double.IsInfinity(value) && !double.IsNaN(value);
    }
}
