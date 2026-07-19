using System;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// Owns the player's currencies (cash as BigDouble, gems as long), backed by the save's
    /// <see cref="WalletData"/>. Raises change events so the UI can refresh without polling.
    /// </summary>
    public sealed class WalletService
    {
        private readonly WalletData _data;

        public event Action CashChanged;
        public event Action GemsChanged;

        public WalletService(WalletData data)
        {
            _data = data ?? new WalletData();
        }

        public BigDouble Cash => _data.cash;
        public long Gems => _data.gems;

        public void AddCash(BigDouble amount)
        {
            if (amount.Mantissa <= 0d) return;
            _data.cash += amount;
            CashChanged?.Invoke();
        }

        public bool TrySpendCash(BigDouble amount)
        {
            if (amount > _data.cash) return false;
            _data.cash -= amount;
            CashChanged?.Invoke();
            return true;
        }

        public void AddGems(long amount)
        {
            if (amount == 0) return;
            _data.gems += amount;
            GemsChanged?.Invoke();
        }

        public bool TrySpendGems(long amount)
        {
            if (amount > _data.gems) return false;
            _data.gems -= amount;
            GemsChanged?.Invoke();
            return true;
        }
    }
}
