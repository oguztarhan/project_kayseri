using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// The rolling delivery contract (GDD §9): earn a target amount of cash before the clock runs out,
    /// claim a bonus, and a slightly harder one starts immediately. This is the "there is always
    /// something to do" loop — an idle game with nothing on screen asking to be finished is just a
    /// number going up.
    ///
    /// Progress is measured as growth in <see cref="WalletService.LifetimeCash"/>, which the wallet
    /// already tracks for prestige. That deliberately avoids a hook in the selling code: any income
    /// counts, from any island, so travelling mid-contract does not silently void it.
    ///
    /// The target is derived from what the player currently earns per minute rather than from a fixed
    /// number, so one contract is roughly one focused minute whether the empire makes $500 or $500T.
    /// The configured target only acts as a floor for the opening minutes.
    /// </summary>
    public sealed class ContractService
    {
        private readonly WalletService _wallet;
        private readonly double _floorTarget;
        private readonly float _seconds;
        private readonly double _floorReward;
        private readonly long _rewardGems;

        // How much of a minute's income one contract asks for, against a window the config sets (90 s).
        // So passive play delivers 1.5 minutes of income into a 1.2-minute ask: a 25% margin at
        // difficulty 1, which the streak eats in two claims (x1.12 each) before the player has to find
        // the rest somewhere — a boost, a burst of upgrades — or drop a notch and start climbing again.
        //
        // It was 1.35 against a 60-second window, which asked for 35% MORE than the window could
        // passively produce. The comment here used to say the player should "buy an upgrade, unlock a
        // truck" to cover that, and on the first island's opening levels they could: at level 1 one axis
        // purchase is worth 6.8% and five of them clear it. By level 5 an axis is worth 0.34% and it
        // takes 89 purchases in sixty seconds. So from a few minutes in, the contract could only be won
        // inside a rewarded-ad boost window, which is not what a "there is always something to do" loop
        // is for. Measured off EconomyCurve's samples, 2026-08-07.
        private const double MinutesOfIncome = 1.2d;
        private const double RewardFraction = 0.45d;   // bonus paid, as a share of the target
        private const double StreakStep = 1.12d;       // each claim makes the next one this much harder
        private const double StreakCap = 4d;

        private BigDouble _startCash;
        private BigDouble _target;
        private double _difficulty = 1d;
        private double _lastIncomePerMinute;
        private float _left;
        private bool _claimable;
        private bool _seeded;

        public ContractService(WalletService wallet, double floorTarget, float seconds,
                               double floorReward, long rewardGems)
        {
            _wallet = wallet;
            _floorTarget = floorTarget > 0d ? floorTarget : 100d;
            _seconds = seconds > 5f ? seconds : 60f;
            _floorReward = floorReward;
            _rewardGems = rewardGems;
            Roll(0d);
        }

        public BigDouble Target => _target;
        public BigDouble Earned
        {
            get
            {
                BigDouble e = _wallet.LifetimeCash - _startCash;
                return e.Mantissa > 0d ? e : BigDouble.Zero;
            }
        }
        public bool Claimable => _claimable;
        public float SecondsLeft => _left;
        public int Streak { get; private set; }
        public BigDouble Reward => RewardFor(_target);
        public long RewardGems => _rewardGems;

        public double Progress01
        {
            get
            {
                if (_claimable) return 1d;
                double t = _target.ToDouble();
                if (t <= 0d) return 0d;
                double p = Earned.ToDouble() / t;
                return p < 0d ? 0d : p > 1d ? 1d : p;
            }
        }

        /// <summary>
        /// Sizes the opening contract from the rate the previous session persisted (the same number the
        /// offline grant trusts). Without it the first contract of a session is sized from the live
        /// income meter, which is a trailing 60-second average starting at zero: a grown empire got the
        /// $100 floor and cleared it before the player had looked at the screen. Call once, after the
        /// offline earnings are paid, or the money the player made while away counts as progress.
        /// </summary>
        public void Seed(double incomePerMinute)
        {
            if (_seeded || incomePerMinute <= 0d) return;
            _seeded = true;
            Roll(incomePerMinute);
        }

        /// <summary>
        /// Advances the clock. <paramref name="incomePerMinute"/> sizes the NEXT contract, so it is only
        /// read when one is rolled — a target that moved while the player was working toward it would be
        /// a treadmill they could never catch.
        /// </summary>
        public void Tick(float dt, double incomePerMinute)
        {
            _lastIncomePerMinute = incomePerMinute;

            // The first real target has to wait for the islands to report an income — the service is
            // built during bootstrap, before any of them exist. Without this, a returning player's
            // opening contract is the $100 floor, which their empire clears before they see it.
            // Keep waiting while the income reads zero: the first few ticks after a scene load run
            // before the operations have reported, and spending the seed on one of those leaves the
            // target stuck on the floor for the whole first contract.
            if (!_seeded && incomePerMinute > 0d)
            {
                _seeded = true;
                Roll(incomePerMinute);
            }

            if (_claimable) return;                       // the clock stops once it is won; claim at leisure

            if (Earned >= _target) { _claimable = true; return; }

            _left -= dt;
            if (_left > 0f) return;

            // Ran out. Ease off rather than punish — a wall the player cannot clear stops being a goal.
            _difficulty = _difficulty > 1d ? _difficulty / StreakStep : 1d;
            Streak = 0;
            Roll(incomePerMinute);
        }

        /// <summary>
        /// Pays the bonus and starts the next contract, sized from the income the ticker last reported.
        /// The contract screen has no view of the empire's income, and re-deriving it there would be a
        /// second, drifting copy of <see cref="HudUI"/>'s sum.
        /// </summary>
        public bool Claim() => Claim(_lastIncomePerMinute);

        /// <summary>Pays the bonus and starts the next contract. No-op unless the goal is met.</summary>
        public bool Claim(double incomePerMinute)
        {
            if (!_claimable) return false;
            _wallet.AddCash(RewardFor(_target));
            _wallet.AddGems(_rewardGems);
            Streak++;
            _difficulty *= StreakStep;
            if (_difficulty > StreakCap) _difficulty = StreakCap;
            Roll(incomePerMinute);
            return true;
        }

        private BigDouble RewardFor(BigDouble target)
        {
            BigDouble scaled = target * RewardFraction;
            return scaled.ToDouble() > _floorReward ? scaled : new BigDouble(_floorReward);
        }

        private void Roll(double incomePerMinute)
        {
            double want = incomePerMinute * MinutesOfIncome * _difficulty;
            if (want < _floorTarget) want = _floorTarget;
            _target = new BigDouble(want);
            _startCash = _wallet.LifetimeCash;
            _left = _seconds;
            _claimable = false;
        }
    }
}
