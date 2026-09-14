using System;
using Game.Core;

namespace Game.Systems
{
    /// <summary>Read/upgrade facade. MarketService alone constructs and advances it; views never tick or pay.</summary>
    public sealed class MiningShopService
    {
        private readonly MiningShopSimulation _simulation;
        private readonly WalletService _wallet;
        private readonly SaveService _save;
        private readonly SaveData _data;
        private bool _busy;

        public event Action Changed;

        internal MiningShopService(MiningShopState state, MiningShopSimulation.Tuning tuning,
            WalletService wallet, SaveService save, SaveData data, Action<MiningShopSimulation.Sale> settle)
        {
            _wallet = wallet;
            _save = save;
            _data = data;
            _simulation = new MiningShopSimulation(state, tuning, settle);
        }

        public MiningShopSimulation.Snapshot View => _simulation.View;
        public double CraftSeconds => _simulation.CraftSeconds;
        public double UnitPrice => _simulation.UnitPrice;
        public double UpgradeCost(bool speed) => _simulation.UpgradeCost(speed);

        public bool TryBuyUpgrade(bool speed)
        {
            if (_busy || _simulation.View.PendingSeconds > 0d) return false;
            double cost = UpgradeCost(speed);
            if (cost <= 0d || !_wallet.CanAfford(new BigDouble(cost))) return false;
            _busy = true;
            try
            {
                // Set the new level before wallet notifications: observers/save hooks see the whole purchase.
                if (!_simulation.Upgrade(speed)) return false;
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
