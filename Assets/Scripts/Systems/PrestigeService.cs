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
        /// What a full run is worth — at the tier the player has reached, and against what they have
        /// already banked. Two factors, for two different runaways.
        ///
        /// TIER: every island earns <c>tierStep</c> more than the last, so without scaling by it a
        /// reset would be worth exponentially more the further up the ladder it was taken — which is
        /// exactly how prestige used to hand out a 70× multiplier for finishing the first island.
        ///
        /// INVESTORS ALREADY BANKED, squared: <see cref="Prestige.Investors"/> takes a square root, so
        /// squaring here divides the payout by the factor exactly. Without it every prestige paid the
        /// SAME amount — the reference cannot move while the island count does not move, and the
        /// lifetime cash needed to reach a given gate is fixed — so the fastest way to play was to stop
        /// advancing and farm the earliest legal reset forever, each loop cheaper than the last.
        /// Measured 2026-08-07: every loop at the iron gate returned the same 15.06 investors (at the
        /// 43e6 reference in force that day), and six of them took the whole ladder from 159.6
        /// income-hours down to 50.6. With this term there is an optimum instead — about four resets —
        /// and prestiging past it makes the player slower, which is the shape the mechanic wanted.
        /// Dividing by <c>_k</c> makes the term read as "reference runs already banked", which is why
        /// a player holding none is unaffected: their FIRST prestige is worth what it always was.
        /// </summary>
        private BigDouble Reference
        {
            get
            {
                double banked = _k > 0d ? 1d + _data.wallet.investors / _k : 1d;
                return new BigDouble(_reference * System.Math.Pow(_tierStep, IslandsOwned) * banked * banked);
            }
        }

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

        /// <summary>
        /// Islands still to buy before the gate opens; 0 once it has. The screen needs this because the
        /// two gates do not fall in the same order: the cash threshold is met around the third island
        /// while the island gate holds until the last, so a bar drawn on cash alone sits full against a
        /// locked button and the "how much more" line goes negative.
        /// </summary>
        public int IslandsStillNeeded
        {
            get
            {
                int need = _minIslands - (IslandsOwned + 1);
                return need > 0 ? need : 0;
            }
        }

        /// <summary>
        /// How close prestige is to unlocking, 0..1 — islands while those are what is shut, then
        /// lifetime cash. Tracking whichever gate is actually closed is the whole point; a bar that
        /// tracks the open one tells the player they are ready when they are not.
        /// </summary>
        public double UnlockProgress01
        {
            get
            {
                if (IslandsStillNeeded > 0) return (IslandsOwned + 1) / (double)_minIslands;
                double t = Threshold;
                if (t <= 0d) return 1d;
                double p = _data.wallet.lifetimeCash.ToDouble() / t;
                return p < 0d ? 0d : p > 1d ? 1d : p;
            }
        }
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
