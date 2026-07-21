using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// Runs prestige (GDD §8): tracks Investors (a permanent global income multiplier) and resets the
    /// run when the player prestiges. Investors carry over; cash, station levels, and managers reset.
    /// </summary>
    public sealed class PrestigeService
    {
        private readonly SaveData _data;
        private readonly double _k, _bonus, _threshold;

        public PrestigeService(SaveData data, double k, double bonusPerInvestor, double threshold)
        {
            _data = data; _k = k; _bonus = bonusPerInvestor; _threshold = threshold;
        }

        public double Investors => _data.wallet.investors;
        public double IncomeMultiplier => Prestige.IncomeMultiplier(_data.wallet.investors, _bonus);
        public BigDouble PendingInvestors() => Prestige.Investors(_data.wallet.lifetimeCash, _k);
        public bool CanPrestige() => _data.wallet.lifetimeCash.ToDouble() >= _threshold && PendingInvestors().Mantissa > 0d;

        /// <summary>Award pending investors and reset the run. Returns investors gained. Caller resets in-scene stations.</summary>
        public double DoPrestige()
        {
            double gained = PendingInvestors().ToDouble();
            _data.wallet.investors += gained;
            _data.wallet.cash = BigDouble.Zero;
            _data.wallet.lifetimeCash = BigDouble.Zero;
            _data.stationLevels.Clear();
            _data.hiredManagers.Clear();
            _data.incomeRatePerSec = 0d;
            return gained;
        }
    }
}
