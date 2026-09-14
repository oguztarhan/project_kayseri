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
        private bool _busy;

        public event Action Changed;

        internal MiningShopBusinessService(MiningShopState state, int availableProductCount,
            MiningShopBusinessSimulation.Tuning tuning, WalletService wallet, SaveService save, SaveData data,
            Action<MiningShopBusinessSimulation.Sale> settle)
        {
            _wallet = wallet;
            _save = save;
            _data = data;
            _simulation = new MiningShopBusinessSimulation(state, availableProductCount, tuning, settle);
        }

        public MiningShopBusinessSimulation.Snapshot View => _simulation.View;
        public double TableCost(int productIndex) => _simulation.TableCost(productIndex);
        public double CraftSeconds(int productIndex) => _simulation.CraftSeconds(productIndex);
        public double UnitPrice(int productIndex) => _simulation.UnitPrice(productIndex);
        public double UpgradeCost(int productIndex, bool speed) => _simulation.UpgradeCost(productIndex, speed);

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
                _save?.Save(_data);
                Changed?.Invoke();
                return true;
            }
            finally { _busy = false; }
        }

        public bool TryBuyUpgrade(int productIndex, bool speed)
        {
            if (_busy || _simulation.View.PendingSeconds > 0d) return false;
            double cost = UpgradeCost(productIndex, speed);
            if (cost <= 0d || !_wallet.CanAfford(new BigDouble(cost))) return false;
            _busy = true;
            try
            {
                if (!_simulation.Upgrade(productIndex, speed)) return false;
                _wallet.TrySpendCash(new BigDouble(cost));
                _save?.Save(_data);
                Changed?.Invoke();
                return true;
            }
            finally { _busy = false; }
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
