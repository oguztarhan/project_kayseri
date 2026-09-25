using System;
using Game.Core;

namespace Game.Systems
{
    /// <summary>Wallet and save facade for the four-product model. MarketService remains its only clock and payer.</summary>
    public sealed class MiningShopBusinessService
    {
        private readonly MiningShopBusinessSimulation _simulation;
        private readonly WalletService _wallet;
        private readonly SaveService _save;
        private readonly SaveData _data;
        private readonly GoalService _goals;
        private readonly ForemanService _foremen;
        // Scratch for posting: who is on which bench, so a master is never offered to a second one.
        private readonly int[] _posted = new int[MiningShopCampaign.ProductCount];
        private bool _busy;
        // A held upgrade button buys many levels a second; those purchases skip the save until the hold ends.
        private bool _saveOwed;

        public event Action Changed;

        /// <summary>
        /// A star's gems were just paid: product, star position (0 = the level-10 star) and gems. Raised after the
        /// save that holds them, once per star. Stars caught up on open are paid silently — nobody is watching yet.
        /// </summary>
        public event Action<int, int, long> StarPaid;

        /// <summary>A bench's worker, or what one is worth, may have moved: a swap, a build, or a roster change.</summary>
        public event Action WorkersChanged;

        internal MiningShopBusinessService(MiningShopState state, int availableProductCount,
            MiningShopBusinessSimulation.Tuning tuning, WalletService wallet, SaveService save, SaveData data,
            Action<MiningShopBusinessSimulation.Sale> settle, GoalService goals = null, ForemanService foremen = null)
        {
            _wallet = wallet;
            _save = save;
            _data = data;
            _goals = goals;
            _foremen = foremen;
            _simulation = new MiningShopBusinessSimulation(state, availableProductCount, tuning, settle);
            // Before anything is priced. Saved by the market's open save, like the migration below.
            PostWorkers();
            if (_foremen != null) _foremen.RosterChanged += OnRosterChanged;
            // An old save's speed and value upgrades just became one level; the cash they cost beyond that level
            // comes back. The market saves once the shop is open, so the refund and the migration land together.
            if (_simulation.LevelMigrationRefund > 0d) _wallet.AddCash(new BigDouble(_simulation.LevelMigrationRefund));
            // Stars a save already stands past — migrated there, or reached before stars paid anything — are owed
            // too. The same open save carries them.
            for (int p = 0; p < MiningShopCampaign.ProductCount; p++) PayStars(p);
        }

        public MiningShopBusinessSimulation.Snapshot View => _simulation.View;
        public double TableCost(int productIndex) => _simulation.TableCost(productIndex);
        public double CraftSeconds(int productIndex) => _simulation.CraftSeconds(productIndex);
        public double UnitPrice(int productIndex) => _simulation.UnitPrice(productIndex);
        public double LevelCost(int productIndex) => _simulation.LevelCost(productIndex);
        public double CostOfLevels(int productIndex, int count) => _simulation.CostOfLevels(productIndex, count);
        /// <summary>How many of the next <paramref name="limit"/> levels the wallet pays for right now.</summary>
        public int AffordableLevels(int productIndex, int limit) =>
            _simulation.AffordableLevels(productIndex, _wallet.Cash.ToDouble(), limit);
        public bool BuildRequirementMet(int productIndex) => _simulation.BuildRequirementMet(productIndex);
        public int BuildRequiresLevel => _simulation.BuildRequiresLevel;
        public double StarMultiplier => _simulation.StarMultiplier;
        /// <summary>What a perfect sale multiplies its price by.</summary>
        public double PerfectMultiplier => _simulation.Mastery.PerfectMultiplier;
        public long StarGems(int star) => _simulation.StarGems(star);
        public double SteadyStateRate() => _simulation.SteadyStateRate();
        /// <summary>The mastery rules this business runs on.</summary>
        public BenchMastery.Tuning Mastery => _simulation.Mastery;

        /// <summary>The contract customer joins the shop. No cash moves and nothing is saved: ShopContractService does both.</summary>
        internal bool StartContract(long slot, int productIndex, int sizeIndex, in ShopContract.Terms terms)
            => !_busy && _simulation.View.PendingSeconds <= 0d &&
               _simulation.StartContract(slot, productIndex, sizeIndex, terms);

        /// <summary>The contract customer leaves unpaid. Nothing is saved: ShopContractService does that.</summary>
        internal bool CancelContract() => !_busy && _simulation.CancelContract();

        /// <summary>Forgets a completed contract once it is paid. Called from inside its completing receipt.</summary>
        internal bool ClearCompletedContract() => _simulation.ClearCompletedContract();

        /// <summary>
        /// The goal card's bench: of the built benches with a star left, the one whose next star costs least to
        /// reach, ties to the earlier bench. -1 when every built bench has all its stars.
        /// </summary>
        public int NextStarBench()
        {
            MiningShopBusinessSimulation.Snapshot v = _simulation.View;
            int best = -1;
            double bestCost = 0d;
            for (int p = 0; p < v.AvailableProductCount; p++)
            {
                MiningShopBusinessSimulation.ProductSnapshot line = v.ProductAt(p);
                if (!line.TableBuilt) continue;
                int toStar = BenchMastery.LevelsToNextStar(line.Level);
                if (toStar <= 0) continue;
                double cost = _simulation.CostOfLevels(p, toStar);
                if (best < 0 || cost < bestCost)
                {
                    best = p;
                    bestCost = cost;
                }
            }
            return best;
        }

        /// <summary>The master working this bench, or -1 for the apprentice.</summary>
        public int WorkerAt(int productIndex) => _foremen != null ? _simulation.WorkerAt(productIndex) : -1;

        /// <summary>What this bench's worker multiplies its income by; 1 for the apprentice.</summary>
        public double WorkerMultiplier(int productIndex) => _simulation.WorkerMultiplier(productIndex);

        /// <summary>What this master would multiply a bench's income by at the stars he carries now; 1 without a roster.</summary>
        public double MasterMultiplier(int master) => _foremen != null
            ? BenchMastery.WorkerMultiplier(master, _foremen.LevelOf(master), _foremen.Tuning, _simulation.Mastery) : 1d;

        /// <summary>The bench this master works on this island, or -1.</summary>
        public int BenchOf(int master)
        {
            if (master < 0) return -1;
            for (int p = 0; p < MiningShopCampaign.ProductCount; p++)
                if (WorkerAt(p) == master) return p;
            return -1;
        }

        /// <summary>
        /// Puts an owned master on a built bench. A master already working another bench swaps with this bench's
        /// worker, so a swap never leaves either bench to the apprentice while somebody could stand there.
        /// </summary>
        public bool TrySetWorker(int productIndex, int master)
        {
            if (_busy || _foremen == null || !_foremen.IsHired(master)) return false;
            if (!_simulation.View.ProductAt(productIndex).TableBuilt) return false;
            int current = _simulation.WorkerAt(productIndex);
            if (current == master) return false;
            int other = BenchOf(master);
            if (other >= 0) _simulation.SetWorker(other, current);
            _simulation.SetWorker(productIndex, master);
            PostWorkers();
            Save();
            WorkersChanged?.Invoke();
            Changed?.Invoke();
            return true;
        }

        public bool TryBuildTable(int productIndex)
        {
            if (_busy || _simulation.View.PendingSeconds > 0d) return false;
            double cost = TableCost(productIndex);
            if (cost <= 0d || !_wallet.CanAfford(new BigDouble(cost))) return false;
            _busy = true;
            try
            {
                if (!_simulation.BuildTable(productIndex)) return false;
                _wallet.TrySpendCash(new BigDouble(cost));
                PostWorkers();
                // A build counts as an upgrade, as a station purchase does on the ore islands. Recorded before
                // the save so the count lands on disk with the purchase.
                _goals?.Record(Goals.Upgrades);
                Save();
                WorkersChanged?.Invoke();
                Changed?.Invoke();
                return true;
            }
            finally { _busy = false; }
        }

        /// <summary>
        /// Buys as many of the next <paramref name="count"/> levels as the wallet covers, stopping at the top, and
        /// returns how many it bought. Each level counts as one upgrade for goals and the league. With
        /// <paramref name="saveNow"/> false the purchase is saved later, by <see cref="FlushSave"/> or any other save.
        /// </summary>
        public int TryBuyLevels(int productIndex, int count, bool saveNow = true)
        {
            if (_busy || _simulation.View.PendingSeconds > 0d || count < 1) return 0;
            if (!_simulation.View.ProductAt(productIndex).TableBuilt) return 0;
            int affordable = _simulation.AffordableLevels(productIndex, _wallet.Cash.ToDouble(), count);
            if (affordable < 1) return 0;
            double cost = _simulation.CostOfLevels(productIndex, affordable);
            _busy = true;
            try
            {
                int bought = _simulation.BuyLevels(productIndex, affordable);
                if (bought < 1) return 0;
                _wallet.TrySpendCash(new BigDouble(cost));
                _goals?.Record(Goals.Upgrades, bought);
                // A star pays gems, which never wait for a held button's release: the hold stops at the star anyway.
                int stars = PayStars(productIndex);
                if (saveNow || stars != 0) Save();
                else _saveOwed = true;
                for (int star = 0; star < BenchMastery.StarCount; star++)
                    if ((stars & (1 << star)) != 0) StarPaid?.Invoke(productIndex, star, _simulation.StarGems(star));
                Changed?.Invoke();
                return bought;
            }
            finally { _busy = false; }
        }

        /// <summary>Saves the purchases a held button deferred. Called when the hold ends; does nothing when none are owed.</summary>
        public void FlushSave()
        {
            if (_saveOwed) Save();
        }

        /// <summary>Pays every star the bench has reached and not been paid for; answers which ones it paid.</summary>
        private int PayStars(int productIndex)
        {
            int stars = _simulation.UnpaidStars(productIndex);
            if (stars == 0) return 0;
            long gems = 0L;
            for (int star = 0; star < BenchMastery.StarCount; star++)
                if ((stars & (1 << star)) != 0) gems += _simulation.StarGems(star);
            _simulation.MarkStarsPaid(productIndex, stars);
            _wallet.AddGems(gems);
            return stars;
        }

        /// <summary>
        /// Makes every bench's worker one it may have, then prices them. A built bench keeps a master who is owned and
        /// not already on an earlier bench; an empty one takes the best idle master (auto-posting only ever fills, as
        /// the station posts do); an unbuilt bench releases whoever the save had there. Without a roster nothing is
        /// touched and every bench works at ×1. Answers whether a posting moved.
        /// </summary>
        private bool PostWorkers()
        {
            if (_foremen == null) return false;
            bool moved = false;
            for (int p = 0; p < _posted.Length; p++) _posted[p] = -1;
            for (int p = 0; p < _posted.Length; p++)
            {
                int saved = _simulation.WorkerAt(p);
                int keep = _simulation.View.ProductAt(p).TableBuilt && _foremen.IsHired(saved) &&
                           Array.IndexOf(_posted, saved) < 0 ? saved : -1;
                _posted[p] = keep;
            }
            for (int p = 0; p < _posted.Length; p++)
            {
                if (_posted[p] < 0 && _simulation.View.ProductAt(p).TableBuilt) _posted[p] = _foremen.BestIdleForBench(_posted);
                if (_posted[p] != _simulation.WorkerAt(p))
                {
                    _simulation.SetWorker(p, _posted[p]);
                    moved = true;
                }
                _simulation.SetWorkerMultiplier(p, _posted[p] < 0 ? 1d : MasterMultiplier(_posted[p]));
            }
            return moved;
        }

        /// <summary>A card arrived or a star was bought: fill any empty bench and reprice every worker.</summary>
        private void OnRosterChanged(int master)
        {
            if (_busy) return;
            if (PostWorkers()) Save();
            WorkersChanged?.Invoke();
            Changed?.Invoke();
        }

        private void Save()
        {
            _saveOwed = false;
            _save?.Save(_data);
        }

        internal void Advance(double seconds)
        {
            if (_busy) return;
            _busy = true;
            try { _simulation.Advance(seconds); }
            finally { _busy = false; }
        }
    }
}
