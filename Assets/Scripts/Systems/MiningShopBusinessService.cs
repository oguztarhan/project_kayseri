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
        private bool _busy;
        // A held upgrade button buys many levels a second; those purchases skip the save until the hold ends.
        private bool _saveOwed;

        public event Action Changed;

        internal MiningShopBusinessService(MiningShopState state, int availableProductCount,
            MiningShopBusinessSimulation.Tuning tuning, WalletService wallet, SaveService save, SaveData data,
            Action<MiningShopBusinessSimulation.Sale> settle, GoalService goals = null)
        {
            _wallet = wallet;
            _save = save;
            _data = data;
            _goals = goals;
            _simulation = new MiningShopBusinessSimulation(state, availableProductCount, tuning, settle);
            // An old save's speed and value upgrades just became one level; the cash they cost beyond that level
            // comes back. The market saves once the shop is open, so the refund and the migration land together.
            if (_simulation.LevelMigrationRefund > 0d) _wallet.AddCash(new BigDouble(_simulation.LevelMigrationRefund));
        }

        public MiningShopBusinessSimulation.Snapshot View => _simulation.View;
        public double TableCost(int productIndex) => _simulation.TableCost(productIndex);
        public double CraftSeconds(int productIndex) => _simulation.CraftSeconds(productIndex);
        public double UnitPrice(int productIndex) => _simulation.UnitPrice(productIndex);
        public double LevelCost(int productIndex) => _simulation.LevelCost(productIndex);
        public double CostOfLevels(int productIndex, int count) => _simulation.CostOfLevels(productIndex, count);
        public bool BuildRequirementMet(int productIndex) => _simulation.BuildRequirementMet(productIndex);
        public int BuildRequiresLevel => _simulation.BuildRequiresLevel;
        public double SteadyStateRate() => _simulation.SteadyStateRate();

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
                // A build counts as an upgrade, as a station purchase does on the ore islands. Recorded before
                // the save so the count lands on disk with the purchase.
                _goals?.Record(Goals.Upgrades);
                Save();
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
                if (saveNow) Save();
                else _saveOwed = true;
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
