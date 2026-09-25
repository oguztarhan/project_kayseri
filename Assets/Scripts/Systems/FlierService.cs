using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// The flier's state: its foreground timer, its daily charges (a <see cref="FreeRewardService"/> slot, so it
    /// resets at the same UTC midnight as every other ad), and the saved catch streak. The rules are in
    /// <see cref="Flier"/>; this holds the numbers, pays, and saves.
    /// </summary>
    public sealed class FlierService
    {
        public const string RewardId = "flier";

        public readonly struct Receipt
        {
            public readonly bool Paid;
            public readonly double Cash;
            /// <summary>The master whose card the streak paid, or -1.</summary>
            public readonly int CardMaster;
            /// <summary>The streak after this catch.</summary>
            public readonly int Streak;

            public Receipt(bool paid, double cash, int cardMaster, int streak)
            {
                Paid = paid;
                Cash = cash;
                CardMaster = cardMaster;
                Streak = streak;
            }
        }

        private readonly FreeRewardService _free;
        private readonly WalletService _wallet;
        private readonly ForemanService _foremen;
        private readonly SaveService _save;
        private readonly SaveData _data;
        private readonly Flier.Tuning _tuning;
        private double _gap;

        public FlierService(FreeRewardService free, WalletService wallet, ForemanService foremen, SaveService save,
                            SaveData data, Flier.Tuning tuning, double firstGapRoll)
        {
            tuning.Validate();
            _free = free;
            _wallet = wallet;
            _foremen = foremen;
            _save = save;
            _data = data;
            _tuning = tuning;
            _gap = Flier.Gap(firstGapRoll, tuning);
        }

        public Flier.Tuning Tuning => _tuning;
        public int ChargesLeft => _free != null ? _free.ChargesLeft(RewardId, _tuning.ChargesPerDay) : 0;
        public int Streak => _data != null && _data.flierStreak > 0 ? _data.flierStreak : 0;
        /// <summary>Foreground seconds before the next crossing is due; 0 once it is waiting to be allowed.</summary>
        public double SecondsToNext => _gap;

        /// <summary>
        /// Runs the foreground clock. Answers true once, when a due crossing is allowed to start; the next gap is
        /// drawn from <paramref name="roll"/> at that moment. A due crossing that is not allowed waits.
        /// </summary>
        public bool Tick(double seconds, in Flier.Conditions now, double roll)
        {
            if (seconds > 0d) _gap -= seconds;
            if (_gap > 0d) return false;
            _gap = 0d;
            if (!Flier.MaySpawn(now, _tuning)) return false;
            _gap = Flier.Gap(roll, _tuning);
            return true;
        }

        /// <summary>
        /// One catch, after its ad has paid (or at once with Remove Ads): the income-minutes in cash, one charge, and
        /// one step of the streak, which pays a random master card when it completes. Saved before it returns.
        /// </summary>
        public Receipt TryClaim(double incomePerMinute)
        {
            if (_free == null || ChargesLeft <= 0) return new Receipt(false, 0d, -1, Streak);
            double cash = Flier.Payout(incomePerMinute, _tuning);
            _wallet?.AddCash(new BigDouble(cash));
            _free.Consume(RewardId);
            _data.flierStreak = Flier.NextStreak(_data.flierStreak, _tuning, out bool card);
            int master = card && _foremen != null ? _foremen.GrantRandomDuplicates(1) : -1;
            _save?.Save(_data);
            return new Receipt(true, cash, master, _data.flierStreak);
        }
    }
}
