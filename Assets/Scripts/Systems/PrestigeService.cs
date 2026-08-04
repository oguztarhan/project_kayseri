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
        private readonly double _k, _bonus, _reference, _tierStep, _readyFraction;
        private readonly int _minIslands;

        public PrestigeService(SaveData data, double k, double bonusPerInvestor,
                               double referenceLifetime, double tierStep,
                               int minIslandsOwned, double readyFraction)
        {
            _data = data; _k = k; _bonus = bonusPerInvestor;
            _reference = referenceLifetime; _tierStep = tierStep;
            _minIslands = minIslandsOwned; _readyFraction = readyFraction;
        }

        /// <summary>
        /// What a full run is worth at the tier the player has reached. Every island earns
        /// <c>tierStep</c> more than the last, so without scaling by it a reset would be worth
        /// exponentially more the further up the ladder it was taken — which is exactly how
        /// prestige used to hand out a 70× multiplier for finishing the first island.
        /// </summary>
        private BigDouble Reference =>
            new BigDouble(_reference * System.Math.Pow(_tierStep, IslandsOwned));

        /// <summary>Islands bought beyond the starter one, which is always owned.</summary>
        private int IslandsOwned => _data.unlockedIslands != null ? _data.unlockedIslands.Count : 0;

        public double Investors => _data.wallet.investors;
        public double IncomeMultiplier => Prestige.IncomeMultiplier(_data.wallet.investors, _bonus);
        public BigDouble PendingInvestors() => Prestige.Investors(_data.wallet.lifetimeCash, _k, Reference);

        /// <summary>
        /// Gated on ISLANDS OWNED, not on a cash figure. The old gate was 1,000 lifetime cash —
        /// roughly two minutes of play — so the run-ending button was live before the player had
        /// seen a second island.
        /// </summary>
        public bool CanPrestige() =>
            IslandsOwned + 1 >= _minIslands &&
            _data.wallet.lifetimeCash.ToDouble() >= Threshold &&
            PendingInvestors().Mantissa > 0d;

        /// <summary>Lifetime cash needed before prestige unlocks — the prestige screen draws a bar toward it.</summary>
        public double Threshold => Reference.ToDouble() * _readyFraction;
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
