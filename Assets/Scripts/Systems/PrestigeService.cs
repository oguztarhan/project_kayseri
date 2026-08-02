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

        /// <summary>Lifetime cash needed before prestige unlocks — the prestige screen draws a bar toward it.</summary>
        public double Threshold => _threshold;
        public BigDouble LifetimeCash => _data.wallet.lifetimeCash;

        /// <summary>What the multiplier becomes once the pending investors are cashed in.</summary>
        public double MultiplierAfterPrestige()
            => Prestige.IncomeMultiplier(_data.wallet.investors + PendingInvestors().ToDouble(), _bonus);

        /// <summary>
        /// Partial prestige, sold for gems: awards <paramref name="share"/> of the pending investors and
        /// keeps every station level. Returns the investors gained.
        ///
        /// The lifetime cash it burns is not a design dial — it is forced by the curve. Investors are
        /// <c>√lifetime</c>, so leaving <c>(1-share)</c> of the pending pool means leaving
        /// <c>(1-share)²</c> of the cash. Anything less and the bag would be farmable: buy it twice and
        /// the second purchase would draw on a pool the first one never paid for.
        ///
        /// It stays deliberately worse than prestige per unit of lifetime cash — half the investors for
        /// three quarters of the run — because what the player is buying is not efficiency, it is
        /// keeping the islands they built.
        /// </summary>
        public double TakeInvestorShare(double share)
        {
            if (share <= 0d || share > 1d) return 0d;
            double gained = PendingInvestors().ToDouble() * share;
            if (gained <= 0d) return 0d;
            _data.wallet.investors += gained;
            double keep = (1d - share) * (1d - share);
            _data.wallet.lifetimeCash = _data.wallet.lifetimeCash * keep;
            return gained;
        }

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
