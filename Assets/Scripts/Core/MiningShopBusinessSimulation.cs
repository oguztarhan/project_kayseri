using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// Deterministic four-product business model. Tables own their product stock; one carrier and one seller are
    /// shared between them. Each bench has one mastery level (<see cref="BenchMastery"/>) that makes its items both
    /// faster and dearer.
    ///
    /// THE LOGISTICS FOLLOW THE BENCHES. A carrier that took one item every nine seconds used to cap the whole shop,
    /// so every speed level past the first was sold for nothing. Carrier load, rack and shelf room, and how many items
    /// one customer takes home are now sized from what the built benches make (<see cref="Resize"/>), always with
    /// headroom, so a bench's own level is the only thing that sets its income — and <see cref="SteadyStateRate"/>
    /// can be the plain sum of the benches rather than a guess at a queue.
    ///
    /// CUSTOMERS FOLLOW STOCK. An arriving customer goes to a bench with goods on its shelf when there is one, and no
    /// bench may hold more than its share of the queue, so a slow bench can never fill the queue with people waiting
    /// for it and starve the others.
    ///
    /// THE CONTRACT CUSTOMER IS A CUSTOMER. An accepted contract (<see cref="StartContract"/>) is served by the same
    /// seller, taking turns with the normal customers of its bench, and never takes a place in the queue. Each turn
    /// hands over a bundle for no cash; the receipt of the last item says so, and the contract's terms stay in the
    /// save until the caller has paid them (<see cref="ClearCompletedContract"/>).
    /// </summary>
    public sealed class MiningShopBusinessSimulation
    {
        public struct ProductTuning
        {
            public string ProductId;
            public double CraftSeconds;
            public double UnitPrice;
            public double TableCost;
            /// <summary>What this bench's level 1 → 2 costs; every later level grows from it.</summary>
            public double FirstLevelCost;
        }

        public struct Tuning
        {
            public ProductTuning[] Products;
            // The four capacities below are floors: the benches size the real ones upward (see Resize).
            public int OutputCapacity;
            public int ShelfCapacityPerProduct;
            public int CarrierLoad;
            public int QueueCapacity;
            public double TravelSeconds;
            public double HandlingSeconds;
            public double ArrivalSeconds;
            public double ServiceSeconds;
            /// <summary>The level the previous bench must reach before the next one can be built.</summary>
            public int BuildRequiresLevel;
            public BenchMastery.Tuning Mastery;

            public static Tuning Default => new Tuning
            {
                Products = new[]
                {
                    new ProductTuning { ProductId = MiningShopCampaign.ProductIdAt(0), CraftSeconds = 10d, UnitPrice = 20d, TableCost = 0d, FirstLevelCost = 40d },
                    new ProductTuning { ProductId = MiningShopCampaign.ProductIdAt(1), CraftSeconds = 20d, UnitPrice = 240d, TableCost = 3000d, FirstLevelCost = 400d },
                    new ProductTuning { ProductId = MiningShopCampaign.ProductIdAt(2), CraftSeconds = 40d, UnitPrice = 3200d, TableCost = 150000d, FirstLevelCost = 4000d },
                    new ProductTuning { ProductId = MiningShopCampaign.ProductIdAt(3), CraftSeconds = 60d, UnitPrice = 30000d, TableCost = 6000000d, FirstLevelCost = 40000d }
                },
                OutputCapacity = 4,
                ShelfCapacityPerProduct = 4,
                CarrierLoad = 1,
                QueueCapacity = 6,
                TravelSeconds = 4d,
                HandlingSeconds = 0.5d,
                ArrivalSeconds = 10d,
                ServiceSeconds = 2d,
                BuildRequiresLevel = 25,
                Mastery = BenchMastery.Tuning.Default
            };

            public void Validate()
            {
                if (Products == null || Products.Length != MiningShopCampaign.ProductCount || OutputCapacity < 1 ||
                    ShelfCapacityPerProduct < 1 || CarrierLoad < 1 || QueueCapacity < 1 ||
                    !MiningShopBusinessSimulation.Positive(TravelSeconds) || !MiningShopBusinessSimulation.Positive(HandlingSeconds) || !MiningShopBusinessSimulation.Positive(ArrivalSeconds) ||
                    !MiningShopBusinessSimulation.Positive(ServiceSeconds) ||
                    BuildRequiresLevel < BenchMastery.MinLevel || BuildRequiresLevel > BenchMastery.MaxLevel)
                    throw new ArgumentException("Mining-shop business tuning requires finite positive tables, timings and capacities.");
                Mastery.Validate();

                for (int i = 0; i < Products.Length; i++)
                {
                    ProductTuning product = Products[i];
                    if (product.ProductId != MiningShopCampaign.ProductIdAt(i) || !MiningShopBusinessSimulation.Positive(product.CraftSeconds) ||
                        !MiningShopBusinessSimulation.Positive(product.UnitPrice) || !MiningShopBusinessSimulation.NonNegative(product.TableCost) ||
                        !MiningShopBusinessSimulation.Positive(product.FirstLevelCost))
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
            /// <summary>Items the customer took home in this one purchase.</summary>
            public readonly int Units;
            /// <summary>A perfect sale: <see cref="Cash"/> already includes its multiplier.</summary>
            public readonly bool Perfect;
            /// <summary>A delivery to the contract customer: <see cref="Cash"/> is 0, the contract pays on completion.</summary>
            public readonly bool Contract;
            /// <summary>This delivery was the contract's last item.</summary>
            public readonly bool CompletesContract;

            internal Sale(string businessId, string productId, long sequence, double cash, int units, bool perfect,
                bool contract = false, bool completesContract = false)
            {
                BusinessId = businessId;
                ProductId = productId;
                Sequence = sequence;
                Cash = cash;
                Units = units;
                Perfect = perfect;
                Contract = contract;
                CompletesContract = completesContract;
            }

            /// <summary>The same receipt at what the wallet was actually paid, once the payer's multipliers are in.</summary>
            public Sale WithCash(double cash) =>
                new Sale(BusinessId, ProductId, Sequence, cash, Units, Perfect, Contract, CompletesContract);
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
            public int Level => _line.Level;
            public int Stars => BenchMastery.StarsAt(_line.Level);
            public int StarsPaid => _line.StarsPaid;
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
            public int ServiceUnits => _business.ServiceUnits;
            public double ServiceRemaining => _business.ServiceRemaining;
            public double ServiceDuration => _business.ServiceDuration;
            public double PendingSeconds => _business.PendingSeconds;
            public double ElapsedSeconds => _business.ElapsedSeconds;
            public long ReceiptSequence => _business.ReceiptSequence;
            /// <summary>The customer being served is the contract customer.</summary>
            public bool ServingContract => _business.ServiceContract;
            public bool ContractActive => _business.Contract.Active;
            public long ContractSlot => _business.Contract.Slot;
            public int ContractProductIndex => _business.Contract.ProductIndex;
            public int ContractSizeIndex => _business.Contract.SizeIndex;
            public int ContractQuantity => _business.Contract.Quantity;
            public int ContractDelivered => _business.Contract.Delivered;
            public double ContractCash => _business.Contract.Cash;
            public long ContractGems => _business.Contract.Gems;
            public int ContractForemanCards => _business.Contract.ForemanCards;
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
        /// <summary>Every logistics capacity is sized to carry this much more than the benches make.</summary>
        private const double LogisticsHeadroom = 1.2d;
        private readonly MiningShopState _owner;
        private readonly MiningShopBusinessState _state;
        private readonly Tuning _tuning;
        private readonly Action<Sale> _settle;
        private bool _advancing;
        // Derived from the built benches by Resize, never saved: a reload recomputes them from the levels.
        private int _carrierLoad, _outputCapacity, _shelfCapacity, _bundle, _lineQueue;
        // What each bench's worker multiplies its price by. Set by the service from the roster, never saved.
        private readonly double[] _workerMultiplier = { 1d, 1d, 1d, 1d };

        /// <summary>
        /// Opens one authored business. <paramref name="availableProductCount"/> is its campaign allowance,
        /// not a grant of built tables; a saved business whose allowance has since grown is widened to it, never
        /// narrowed. A legacy pickaxe record is copied once into the first product line.
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
            if (availableProductCount > _state.AvailableProductCount) _state.AvailableProductCount = availableProductCount;
            // A save from before bundles was serving exactly one item.
            if (_state.Serving && _state.ServiceUnits < 1) _state.ServiceUnits = 1;
            // A save from before contracts has no contract customer.
            if (_state.Contract == null) _state.Contract = new ShopContractState();
            ValidateState();
            // After validation, so a save the shop refuses is left exactly as it was on disk.
            if (_state.LevelSchema < MiningShopLevelMigration.Schema) MigrateLevels();
            Resize();
            ScheduleJobs();
        }

        /// <summary>
        /// Cash owed to the player by the level migration that ran when this business opened, 0 when none ran. The
        /// service pays it in the same save that records the migration.
        /// </summary>
        public double LevelMigrationRefund { get; private set; }

        public Snapshot View => new Snapshot(_owner.BusinessId, _state, _tuning);
        /// <summary>Seconds one item takes on this bench at its level, held at the mastery floor.</summary>
        public double CraftSeconds(int productIndex) =>
            BenchMastery.CycleSeconds(_tuning.Products[productIndex].CraftSeconds, Line(productIndex).Level, _tuning.Mastery);
        /// <summary>What one item off this bench sells for at its level with its worker, before perfect sales and the wallet.</summary>
        public double UnitPrice(int productIndex) => BenchMastery.ItemValue(_tuning.Products[productIndex].UnitPrice,
            _tuning.Products[productIndex].CraftSeconds, Line(productIndex).Level, _tuning.Mastery) *
            _workerMultiplier[productIndex];
        public double TableCost(int productIndex) => _tuning.Products[productIndex].TableCost;
        /// <summary>The next level's price, or 0 at the top.</summary>
        public double LevelCost(int productIndex) =>
            BenchMastery.LevelCost(_tuning.Products[productIndex].FirstLevelCost, Line(productIndex).Level, _tuning.Mastery);
        /// <summary>The next <paramref name="count"/> levels' price, stopping at the top.</summary>
        public double CostOfLevels(int productIndex, int count) => BenchMastery.CostOfLevels(
            _tuning.Products[productIndex].FirstLevelCost, Line(productIndex).Level, count, _tuning.Mastery);
        /// <summary>How many of the next levels, up to <paramref name="limit"/>, this much cash pays for.</summary>
        public int AffordableLevels(int productIndex, double cash, int limit) => BenchMastery.AffordableLevels(
            _tuning.Products[productIndex].FirstLevelCost, Line(productIndex).Level, cash, limit, _tuning.Mastery);
        public int BuildRequiresLevel => _tuning.BuildRequiresLevel;
        /// <summary>What each star multiplies its axis by.</summary>
        public double StarMultiplier => _tuning.Mastery.StarMultiplier;

        /// <summary>Gems the star at this position pays once.</summary>
        public long StarGems(int star) => BenchMastery.StarGems(star, _tuning.Mastery);

        /// <summary>Stars this bench has reached but not been paid for, one bit per star.</summary>
        public int UnpaidStars(int productIndex)
        {
            MiningShopProductLineState line = Line(productIndex);
            return line.TableBuilt ? BenchMastery.UnpaidStars(line.Level, line.StarsPaid) : 0;
        }

        /// <summary>Pure bookkeeping. Its caller pays the gems and saves both together.</summary>
        public void MarkStarsPaid(int productIndex, int stars) => Line(productIndex).StarsPaid |= stars;

        /// <summary>The master saved at this bench, or -1. Unchecked: the roster is the service's to validate.</summary>
        public int WorkerAt(int productIndex) => Line(productIndex).Worker;

        /// <summary>Pure bookkeeping. Its caller validates the master and saves.</summary>
        public void SetWorker(int productIndex, int master) => Line(productIndex).Worker = master;

        /// <summary>The mastery rules this business runs on.</summary>
        public BenchMastery.Tuning Mastery => _tuning.Mastery;

        /// <summary>What this bench's worker multiplies its price by; 1 for the apprentice.</summary>
        public double WorkerMultiplier(int productIndex) => _workerMultiplier[productIndex];

        /// <summary>
        /// Sets what this bench's worker is worth. Items priced from now on carry it; a customer already being served
        /// pays the price they were quoted.
        /// </summary>
        public void SetWorkerMultiplier(int productIndex, double multiplier)
        {
            if (productIndex < 0 || productIndex >= _workerMultiplier.Length || !Positive(multiplier))
                throw new ArgumentOutOfRangeException(nameof(productIndex));
            _workerMultiplier[productIndex] = multiplier;
        }

        /// <summary>Whether this bench is offered, unbuilt, and the bench before it has reached the required level.</summary>
        public bool BuildRequirementMet(int productIndex)
        {
            if (productIndex <= 0 || productIndex >= _state.AvailableProductCount || Line(productIndex).TableBuilt ||
                !PreviousTablesBuilt(productIndex)) return false;
            return _state.Lines[productIndex - 1].Level >= _tuning.BuildRequiresLevel;
        }

        /// <summary>
        /// Cash per second once the shop has settled, before any wallet multiplier: each built bench's income at its
        /// level and with its worker, with the average that perfect sales add. The logistics are sized never to hold a bench back, so the
        /// sum is the whole answer. Offline earnings and income-minute rewards are paid from this rather than from a
        /// measured window, so it is right the moment the shop opens.
        /// </summary>
        public double SteadyStateRate()
        {
            double rate = 0d;
            for (int i = 0; i < _state.AvailableProductCount; i++)
            {
                MiningShopProductLineState line = _state.Lines[i];
                if (!line.TableBuilt) continue;
                ProductTuning product = _tuning.Products[i];
                rate += BenchMastery.IncomePerSecond(product.UnitPrice, product.CraftSeconds, line.Level, _tuning.Mastery) *
                        BenchMastery.PerfectAverage(BenchMastery.StarsAt(line.Level), _tuning.Mastery) * _workerMultiplier[i];
            }
            return rate;
        }

        /// <summary>Pure build mutation. Its caller validates and spends <see cref="TableCost"/> atomically.</summary>
        public bool BuildTable(int productIndex)
        {
            if (!BuildRequirementMet(productIndex)) return false;
            Line(productIndex).TableBuilt = true;
            Resize();
            ScheduleJobs();
            return true;
        }

        /// <summary>
        /// Pure level mutation: raises a built bench by up to <paramref name="count"/> levels, stopping at the top, and
        /// returns how many it took. Its caller validates and spends <see cref="CostOfLevels"/> atomically. An item
        /// already on the bench keeps the share of its craft it had left.
        /// </summary>
        public int BuyLevels(int productIndex, int count)
        {
            MiningShopProductLineState line = Line(productIndex);
            if (!line.TableBuilt || count < 1) return 0;
            int bought = Math.Min(count, BenchMastery.MaxLevel - line.Level);
            if (bought <= 0) return 0;
            double fractionLeft = line.Crafting ? line.CraftRemaining / line.CraftDuration : 0d;
            line.Level += bought;
            if (line.Crafting)
            {
                line.CraftDuration = CraftSeconds(productIndex);
                line.CraftRemaining = fractionLeft * line.CraftDuration;
            }
            Resize();
            return bought;
        }

        /// <summary>
        /// Pure contract mutation: the contract customer joins the shop for <paramref name="productIndex"/> with the
        /// accepted terms. Refused while another contract runs, for a bench that is not built, or for empty terms. Its
        /// caller saves. Nothing is paid here — the contract pays on completion.
        /// </summary>
        public bool StartContract(long slot, int productIndex, int sizeIndex, in ShopContract.Terms terms)
        {
            ShopContractState contract = _state.Contract;
            // Not while a cancelled contract's last bundle is still being handed over: it would count toward this one.
            if (_advancing || contract.Active || _state.ServiceContract ||
                productIndex < 0 || productIndex >= _state.AvailableProductCount ||
                !Line(productIndex).TableBuilt || sizeIndex < 0 || sizeIndex >= ShopContract.SizeCount ||
                terms.Quantity < 1 || !NonNegative(terms.Cash) || terms.Gems < 0L || terms.ForemanCards < 0) return false;

            contract.Active = true;
            contract.Slot = slot;
            contract.ProductIndex = productIndex;
            contract.SizeIndex = sizeIndex;
            contract.Quantity = terms.Quantity;
            contract.Delivered = 0;
            contract.Cash = terms.Cash;
            contract.Gems = terms.Gems;
            contract.ForemanCards = terms.ForemanCards;
            contract.ContractTurn = true;
            ScheduleJobs();
            return true;
        }

        /// <summary>
        /// Pure contract mutation: the contract customer leaves unpaid and nothing is owed. Items already handed over
        /// stay sold; a bundle being handed over right now is still handed over, and counts for nothing. Its caller saves.
        /// </summary>
        public bool CancelContract()
        {
            if (_advancing || !_state.Contract.Active) return false;
            _state.Contract.Clear();
            return true;
        }

        /// <summary>
        /// Forgets a completed contract's terms once its caller has paid them. Refused while one runs. Allowed from
        /// inside the completing receipt, since nothing after it reads the contract.
        /// </summary>
        public bool ClearCompletedContract()
        {
            ShopContractState contract = _state.Contract;
            if (contract.Active) return false;
            contract.Clear();
            return true;
        }

        /// <summary>Items the contract customer still wants beyond any bundle already being handed over.</summary>
        private int ContractWants(int productIndex)
        {
            ShopContractState contract = _state.Contract;
            if (!contract.Active || contract.ProductIndex != productIndex) return 0;
            int inHand = _state.Serving && _state.ServiceContract ? _state.ServiceUnits : 0;
            return Math.Max(0, contract.Quantity - contract.Delivered - inHand);
        }

        /// <summary>
        /// Turns each bench's old speed and value levels into one level, once (see <see cref="MiningShopLevelMigration"/>).
        /// A level already set is never lowered. An item on the bench keeps the share of its craft it had left.
        /// </summary>
        private void MigrateLevels()
        {
            double refund = 0d;
            for (int i = 0; i < _state.Lines.Count; i++)
            {
                MiningShopProductLineState line = _state.Lines[i];
                ProductTuning product = _tuning.Products[i];
                int level = MiningShopLevelMigration.Level(i, line.SpeedLevel, line.ValueLevel, product.FirstLevelCost,
                    product.UnitPrice, product.CraftSeconds, _tuning.Mastery, out double owed);
                if (line.Level > level && line.Level <= BenchMastery.MaxLevel) level = line.Level;
                else refund += owed;
                double fractionLeft = line.Crafting ? line.CraftRemaining / line.CraftDuration : 0d;
                line.Level = level;
                if (line.Crafting)
                {
                    line.CraftDuration = CraftSeconds(i);
                    line.CraftRemaining = fractionLeft * line.CraftDuration;
                }
            }
            _state.LevelSchema = MiningShopLevelMigration.Schema;
            LevelMigrationRefund = refund;
        }

        /// <summary>
        /// Sizes the shared logistics from what the built benches make, with <see cref="LogisticsHeadroom"/> to spare.
        /// The carrier and the customers both reach the benches in turn, so a trip and a bundle must each hold what the
        /// fastest bench makes while the turn goes round all of them — a carrier trip, or one customer arrival per bench.
        /// Racks and shelves hold two of either so a bench never waits on the walk.
        /// </summary>
        private void Resize()
        {
            int built = 0;
            double fastest = 0d;
            for (int i = 0; i < _state.AvailableProductCount; i++)
            {
                if (!_state.Lines[i].TableBuilt) continue;
                double rate = 1d / CraftSeconds(i);
                built++;
                if (rate > fastest) fastest = rate;
            }
            double trip = 2d * (_tuning.HandlingSeconds + _tuning.TravelSeconds);
            _carrierLoad = Math.Max(_tuning.CarrierLoad, (int)Math.Ceiling(LogisticsHeadroom * fastest * built * trip));
            _bundle = Math.Max(1, (int)Math.Ceiling(LogisticsHeadroom * fastest * built * _tuning.ArrivalSeconds));
            _outputCapacity = Math.Max(_tuning.OutputCapacity, 2 * _carrierLoad);
            _shelfCapacity = Math.Max(_tuning.ShelfCapacityPerProduct, 2 * Math.Max(_carrierLoad, _bundle));
            _lineQueue = Math.Max(1, _tuning.QueueCapacity / Math.Max(1, built));
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
                if (line.TableBuilt && !line.Crafting && line.OutputStock + line.PickupReserved < _outputCapacity)
                {
                    line.Crafting = true;
                    line.CraftDuration = CraftSeconds(i);
                    line.CraftRemaining = line.CraftDuration;
                }
            }

            if (!_state.Serving)
            {
                int sell = FindSellableLine(out bool contract);
                if (sell >= 0)
                {
                    MiningShopProductLineState line = _state.Lines[sell];
                    if (contract)
                    {
                        // Handed over, not sold: no price and no perfect roll — the contract pays on completion.
                        int units = Math.Min(_bundle, Math.Min(line.ShelfStock, ContractWants(sell)));
                        line.ShelfStock -= units;
                        _state.Contract.ContractTurn = false;
                        _state.ServiceUnits = units;
                        _state.ServicePerfect = false;
                        _state.ServicePrice = 0d;
                    }
                    else
                    {
                        int units = Math.Min(_bundle, line.ShelfStock);
                        line.WaitingCustomers--;
                        line.ShelfStock -= units;
                        bool perfect = NextRoll() < BenchMastery.PerfectChance(BenchMastery.StarsAt(line.Level), _tuning.Mastery);
                        if (_state.Contract.Active && _state.Contract.ProductIndex == sell) _state.Contract.ContractTurn = true;
                        _state.ServiceUnits = units;
                        _state.ServicePerfect = perfect;
                        _state.ServicePrice = units * UnitPrice(sell) * (perfect ? _tuning.Mastery.PerfectMultiplier : 1d);
                    }
                    _state.Serving = true;
                    _state.ServiceContract = contract;
                    _state.ServiceProductIndex = sell;
                    _state.ServiceDuration = _tuning.ServiceSeconds;
                    _state.ServiceRemaining = _tuning.ServiceSeconds;
                }
            }

            if (_state.Carrier == MiningShopSimulation.CarrierPhase.Idle)
            {
                int pickup = FindCarrierLine();
                if (pickup >= 0)
                {
                    MiningShopProductLineState line = _state.Lines[pickup];
                    int room = Math.Max(0, _shelfCapacity - line.ShelfStock - line.DestinationReserved);
                    int take = Math.Min(line.OutputStock, Math.Min(room, _carrierLoad));
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
                if (line.ShelfStock < _shelfCapacity) SetCarrier(MiningShopSimulation.CarrierPhase.Unloading, _tuning.HandlingSeconds);
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
                        int accepted = Math.Min(_state.CarrierCount, Math.Max(0, _shelfCapacity - line.ShelfStock));
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
                    int demand = NextDemandLine();
                    if (demand >= 0)
                    {
                        _state.Lines[demand].WaitingCustomers++;
                        _state.DemandCursor = (demand + 1) % _state.AvailableProductCount;
                    }
                }
            }

            if (_state.Serving && _state.ServiceRemaining <= Epsilon)
            {
                int product = _state.ServiceProductIndex;
                MiningShopProductLineState line = _state.Lines[product];
                double price = _state.ServicePrice;
                int units = _state.ServiceUnits;
                bool perfect = _state.ServicePerfect;
                bool contract = _state.ServiceContract;
                _state.Serving = false;
                _state.ServiceContract = false;
                _state.ServiceProductIndex = -1;
                _state.ServiceUnits = 0;
                _state.ServicePerfect = false;
                _state.ServiceRemaining = 0d;
                _state.ServicePrice = 0d;
                line.Sold += units;
                line.Earned += price;

                // A bundle for a contract cancelled mid-hand-over still leaves the shop; it just counts for nothing.
                bool completes = false;
                ShopContractState order = _state.Contract;
                if (contract && order.Active && order.ProductIndex == product)
                {
                    order.Delivered += units;
                    if (order.Delivered >= order.Quantity)
                    {
                        order.Delivered = order.Quantity;
                        order.Active = false;
                        order.ContractTurn = false;
                        completes = true;
                    }
                }
                _state.ReceiptSequence++;
                _settle(new Sale(_owner.BusinessId, line.ProductId, _state.ReceiptSequence, price, units, perfect,
                    contract, completes));
            }
        }

        private int FindCarrierLine()
        {
            for (int pass = 0; pass < 2; pass++)
                for (int offset = 0; offset < _state.AvailableProductCount; offset++)
                {
                    int index = (_state.CarrierCursor + offset) % _state.AvailableProductCount;
                    MiningShopProductLineState line = _state.Lines[index];
                    int shelfRoom = _shelfCapacity - line.ShelfStock - line.DestinationReserved;
                    bool nearlyFull = line.OutputStock + line.PickupReserved >= _outputCapacity - 1;
                    if (line.TableBuilt && line.OutputStock > 0 && shelfRoom > 0 && (pass == 1 || nearlyFull))
                    {
                        _state.CarrierCursor = (index + 1) % _state.AvailableProductCount;
                        return index;
                    }
                }
            return -1;
        }

        /// <summary>
        /// The next bench the seller serves, in turn from the seller cursor, and whether it is the contract customer's
        /// turn there. On the contract's bench the contract customer and the normal customers take turns; when only
        /// one of them wants goods, that one is served so the seller never stands idle.
        /// </summary>
        private int FindSellableLine(out bool contract)
        {
            contract = false;
            for (int offset = 0; offset < _state.AvailableProductCount; offset++)
            {
                int index = (_state.SellerCursor + offset) % _state.AvailableProductCount;
                MiningShopProductLineState line = _state.Lines[index];
                if (!line.TableBuilt || line.ShelfStock <= 0) continue;
                bool normal = line.WaitingCustomers > 0;
                bool wants = ContractWants(index) > 0;
                if (!normal && !wants) continue;
                _state.SellerCursor = (index + 1) % _state.AvailableProductCount;
                contract = wants && (!normal || _state.Contract.ContractTurn);
                return index;
            }
            return -1;
        }

        /// <summary>
        /// Which bench a new customer queues for, in turn from the demand cursor: first a bench with goods on its shelf
        /// for more people than already wait there, then any bench still under its share of the queue, then nobody.
        /// </summary>
        private int NextDemandLine()
        {
            for (int pass = 0; pass < 2; pass++)
                for (int offset = 0; offset < _state.AvailableProductCount; offset++)
                {
                    int index = (_state.DemandCursor + offset) % _state.AvailableProductCount;
                    MiningShopProductLineState line = _state.Lines[index];
                    if (line.TableBuilt && line.WaitingCustomers < _lineQueue &&
                        (pass == 1 || line.ShelfStock > line.WaitingCustomers)) return index;
                }
            return -1;
        }

        /// <summary>
        /// The next perfect-sale roll in [0,1). The generator's state is saved with the business, so a reload can
        /// neither re-roll a sale nor replay the same luck; a business that has never rolled seeds from its own ID.
        /// </summary>
        private double NextRoll()
        {
            uint x = unchecked((uint)_state.PerfectSeed);
            if (x == 0u) x = Seed(_owner.BusinessId);
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state.PerfectSeed = unchecked((int)x);
            return (x >> 8) * (1d / 16777216d);
        }

        private static uint Seed(string id)
        {
            uint hash = 2166136261u;
            for (int i = 0; i < id.Length; i++) hash = unchecked((hash ^ id[i]) * 16777619u);
            return hash == 0u ? 1u : hash;
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
                    _state.ServiceUnits < 1 || !Positive(_state.ServiceDuration) ||
                    _state.ServiceRemaining > _state.ServiceDuration ||
                    // A contract hand-over carries no price and no perfect roll; a sale always has a price.
                    (_state.ServiceContract ? _state.ServicePrice != 0d || _state.ServicePerfect : !Positive(_state.ServicePrice)))) ||
                (!_state.Serving && (_state.ServiceProductIndex != -1 || _state.ServiceUnits != 0 || _state.ServiceContract)) ||
                _state.LevelSchema < 0 || _state.LevelSchema > MiningShopLevelMigration.Schema)
                throw new ArgumentException("Mining-shop business save has invalid shared jobs; refusing to reset it.");

            // Before the migration a bench's level is the two old tracks; after it, the single level.
            bool levelsMigrated = _state.LevelSchema >= MiningShopLevelMigration.Schema;
            int totalReserved = 0;
            for (int i = 0; i < _state.Lines.Count; i++)
            {
                MiningShopProductLineState line = _state.Lines[i];
                if (line == null || line.ProductId != _tuning.Products[i].ProductId || (i >= _state.AvailableProductCount && line.TableBuilt) ||
                    (i == 0 && !line.TableBuilt) ||
                    (levelsMigrated ? line.Level < BenchMastery.MinLevel || line.Level > BenchMastery.MaxLevel
                        : !MiningShopLevelMigration.LegacyLevelsValid(line.SpeedLevel, line.ValueLevel)) ||
                    line.OutputStock < 0 || line.PickupReserved < 0 || line.DestinationReserved < 0 || line.ShelfStock < 0 ||
                    line.WaitingCustomers < 0 || line.Produced < 0 || line.Sold < 0 || !NonNegative(line.Earned) ||
                    !NonNegative(line.CraftRemaining) || !NonNegative(line.CraftDuration) ||
                    (line.Crafting && (!Positive(line.CraftDuration) || line.CraftRemaining > line.CraftDuration)) ||
                    (!line.TableBuilt && (line.Crafting || line.OutputStock > 0 || line.PickupReserved > 0 ||
                        line.DestinationReserved > 0 || line.ShelfStock > 0 || line.WaitingCustomers > 0 || line.Produced > 0 || line.Sold > 0)) ||
                    line.Produced - line.Sold != (long)line.OutputStock + line.PickupReserved + line.ShelfStock +
                        (_state.CarrierProductIndex == i ? _state.CarrierCount : 0) +
                        (_state.Serving && _state.ServiceProductIndex == i ? _state.ServiceUnits : 0L))
                    throw new ArgumentException("Mining-shop product-line save has invalid inventory; refusing to reset it.");
                totalReserved += line.PickupReserved + (_state.CarrierProductIndex == i ? _state.CarrierCount : 0);
                if (line.DestinationReserved != (line.PickupReserved + (_state.CarrierProductIndex == i ? _state.CarrierCount : 0)))
                    throw new ArgumentException("Mining-shop product-line reservation does not match its cargo.");
            }

            if (totalReserved < 0) throw new ArgumentException("Mining-shop reservation total overflowed.");

            ShopContractState contract = _state.Contract;
            bool contractInvalid = contract.Quantity < 0 || contract.Delivered < 0 || contract.Delivered > contract.Quantity ||
                !NonNegative(contract.Cash) || contract.Gems < 0L || contract.ForemanCards < 0;
            if (contract.Active)
                contractInvalid |= contract.ProductIndex < 0 || contract.ProductIndex >= _state.AvailableProductCount ||
                    !_state.Lines[contract.ProductIndex].TableBuilt || contract.SizeIndex < 0 ||
                    contract.SizeIndex >= ShopContract.SizeCount || contract.Quantity < 1 ||
                    contract.Delivered >= contract.Quantity ||
                    (_state.ServiceContract && _state.ServiceProductIndex == contract.ProductIndex &&
                        contract.Delivered + _state.ServiceUnits > contract.Quantity);
            if (contractInvalid)
                throw new ArgumentException("Mining-shop contract save is inconsistent; refusing to reset it.");
        }

        private static bool Positive(double value) => value > 0d && !double.IsInfinity(value) && !double.IsNaN(value);
        private static bool NonNegative(double value) => value >= 0d && !double.IsInfinity(value) && !double.IsNaN(value);
    }
}
