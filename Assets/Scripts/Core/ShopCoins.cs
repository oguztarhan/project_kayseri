using System;

namespace Game.Core
{
    /// <summary>
    /// The collectible shop coins, as numbers: the 48-hour cycle every player shares, how many coins a cycle holds,
    /// how long the gaps between them are, and what a coin pays.
    ///
    /// The cycle is wall-clock UTC, <c>floor(unix / 172800)</c>, so it turns over at every second UTC midnight for
    /// everyone. The cap counts coins COLLECTED: a coin nobody taps goes back to the cycle and comes again after the
    /// normal gap.
    ///
    /// Every roll is seeded by the player, the cycle and an index, never by a live random source. The reward is keyed
    /// by the collected index, so the Nth coin a player collects in a cycle pays the same whatever they skipped or
    /// however often they restarted; the gaps are keyed by the spawn index for the same reason.
    /// </summary>
    public static class ShopCoins
    {
        public const long CycleSeconds = 172800L;
        public const long DaySeconds = 86400L;

        public enum RewardKind
        {
            SmallCash = 0,
            MediumCash = 1,
            Boost = 2,
            Gems = 3,
            Jackpot = 4
        }

        public const int RewardKindCount = 5;

        public struct Tuning
        {
            public int CoinsPerCycle;
            /// <summary>Most coins collected in a cycle's first 24 hours, so the second day always has some left.</summary>
            public int FirstDayCap;
            public double FirstSpawnMinSeconds;
            public double FirstSpawnMaxSeconds;
            public double GapMinSeconds;
            public double GapMaxSeconds;
            public double LifetimeSeconds;

            /// <summary>Cash rewards pay at least this, so a coin is never worth $0 to an empire that earns nothing yet.</summary>
            public double CashFloor;
            public double SmallCashWeight;
            public double SmallCashMinutes;
            public double MediumCashWeight;
            public double MediumCashMinutes;
            public double BoostWeight;
            public double BoostMultiplier;
            public double BoostSeconds;
            public double GemWeight;
            public long Gems;
            public double JackpotWeight;
            public double JackpotMinutes;

            public static Tuning Default => new Tuning
            {
                CoinsPerCycle = 12,
                FirstDayCap = 8,
                FirstSpawnMinSeconds = 45d,
                FirstSpawnMaxSeconds = 90d,
                GapMinSeconds = 300d,
                GapMaxSeconds = 540d,
                LifetimeSeconds = 20d,
                CashFloor = 100d,
                SmallCashWeight = 60d,
                SmallCashMinutes = 2d,
                MediumCashWeight = 20d,
                MediumCashMinutes = 5d,
                BoostWeight = 12d,
                BoostMultiplier = 2d,
                BoostSeconds = 90d,
                GemWeight = 5d,
                Gems = 5L,
                JackpotWeight = 3d,
                JackpotMinutes = 15d
            };

            public void Validate()
            {
                if (CoinsPerCycle < 1 || FirstDayCap < 1 ||
                    !NonNegative(FirstSpawnMinSeconds) || FirstSpawnMaxSeconds < FirstSpawnMinSeconds || !Finite(FirstSpawnMaxSeconds) ||
                    !NonNegative(GapMinSeconds) || GapMaxSeconds < GapMinSeconds || !Finite(GapMaxSeconds) ||
                    !Positive(LifetimeSeconds) || !NonNegative(CashFloor))
                    throw new ArgumentException("Shop coin tuning requires a positive cap, ordered delay ranges, a positive lifetime and a non-negative cash floor.");
                if (!NonNegative(SmallCashWeight) || !NonNegative(SmallCashMinutes) ||
                    !NonNegative(MediumCashWeight) || !NonNegative(MediumCashMinutes) ||
                    !NonNegative(BoostWeight) || !Finite(BoostMultiplier) || !NonNegative(BoostSeconds) ||
                    !NonNegative(GemWeight) || Gems < 0L ||
                    !NonNegative(JackpotWeight) || !NonNegative(JackpotMinutes))
                    throw new ArgumentException("Shop coin rewards require non-negative weights and amounts.");
                double total = 0d;
                for (int i = 0; i < RewardKindCount; i++) total += EffectiveWeight((RewardKind)i, this);
                if (!(total > 0d))
                    throw new ArgumentException("Shop coin rewards require at least one reward with a weight and an amount.");
            }
        }

        /// <summary>One coin's payout. Cash rewards carry minutes of income; the caller turns them into money.</summary>
        public struct Reward
        {
            public RewardKind Kind;
            public double CashMinutes;
            public long Gems;
            public double BoostMultiplier;
            public double BoostSeconds;

            public bool IsCash => Kind == RewardKind.SmallCash || Kind == RewardKind.MediumCash || Kind == RewardKind.Jackpot;
        }

        public static long CycleAt(long nowUnix) => FloorDiv(nowUnix, CycleSeconds);

        public static long CycleStartUnix(long cycle) => cycle * CycleSeconds;

        /// <summary>Seconds until the next cycle starts: the HUD's countdown and the notification's time.</summary>
        public static long SecondsToReset(long nowUnix) => CycleStartUnix(CycleAt(nowUnix) + 1L) - nowUnix;

        public static bool InFirstDay(long nowUnix) => nowUnix - CycleStartUnix(CycleAt(nowUnix)) < DaySeconds;

        /// <summary>How many coins may have been collected by now in this cycle: the first-day cap, then the full cap.</summary>
        public static int CapAt(long nowUnix, in Tuning t)
            => InFirstDay(nowUnix) && t.FirstDayCap < t.CoinsPerCycle ? t.FirstDayCap : t.CoinsPerCycle;

        /// <summary>
        /// The wait before spawn number <paramref name="spawnIndex"/> of a cycle, in seconds of eligible play. The
        /// first spawn of a cycle waits the short first-spawn range; every later one the gap range.
        /// </summary>
        public static double DelayBefore(string playerId, long cycle, int spawnIndex, in Tuning t)
        {
            double roll = Unit(Next(Seed(playerId, cycle, spawnIndex, SaltDelay)));
            return spawnIndex <= 0
                ? Lerp(t.FirstSpawnMinSeconds, t.FirstSpawnMaxSeconds, roll)
                : Lerp(t.GapMinSeconds, t.GapMaxSeconds, roll);
        }

        /// <summary>What the coin collected at <paramref name="collectedIndex"/> (0-based) in a cycle pays.</summary>
        public static Reward RewardFor(string playerId, long cycle, int collectedIndex, in Tuning t)
            => RewardAt(Unit(Next(Seed(playerId, cycle, collectedIndex, SaltReward))), t);

        /// <summary>
        /// The reward a roll in [0,1) lands on. A reward whose amount is zero has no weight, so setting the gems to 0
        /// removes gem coins and the others share the whole roll between them.
        /// </summary>
        public static Reward RewardAt(double roll, in Tuning t)
        {
            double total = 0d;
            for (int i = 0; i < RewardKindCount; i++) total += EffectiveWeight((RewardKind)i, t);
            double at = roll * total;
            RewardKind kind = RewardKind.SmallCash;
            for (int i = 0; i < RewardKindCount; i++)
            {
                double w = EffectiveWeight((RewardKind)i, t);
                if (w <= 0d) continue;
                kind = (RewardKind)i;
                at -= w;
                if (at < 0d) break;
            }
            return Build(kind, t);
        }

        /// <summary>A reward's share of the roll, 0 when it would pay nothing.</summary>
        public static double EffectiveWeight(RewardKind kind, in Tuning t)
        {
            switch (kind)
            {
                case RewardKind.SmallCash: return t.SmallCashMinutes > 0d ? t.SmallCashWeight : 0d;
                case RewardKind.MediumCash: return t.MediumCashMinutes > 0d ? t.MediumCashWeight : 0d;
                case RewardKind.Boost: return t.BoostMultiplier > 1d && t.BoostSeconds > 0d ? t.BoostWeight : 0d;
                case RewardKind.Gems: return t.Gems > 0L ? t.GemWeight : 0d;
                case RewardKind.Jackpot: return t.JackpotMinutes > 0d ? t.JackpotWeight : 0d;
                default: return 0d;
            }
        }

        /// <summary>Gems one collected coin pays on average — what the reward budget counts.</summary>
        public static double ExpectedGems(in Tuning t)
        {
            double total = 0d;
            for (int i = 0; i < RewardKindCount; i++) total += EffectiveWeight((RewardKind)i, t);
            return total > 0d ? EffectiveWeight(RewardKind.Gems, t) / total * t.Gems : 0d;
        }

        /// <summary>A cash reward's money: its minutes of income, never below the floor.</summary>
        public static double Cash(double minutes, double incomePerMinute, double floor)
        {
            double scaled = incomePerMinute > 0d && Finite(incomePerMinute) ? incomePerMinute * minutes : 0d;
            double min = floor > 0d ? floor : 0d;
            return scaled > min ? scaled : min;
        }

        private static Reward Build(RewardKind kind, in Tuning t)
        {
            switch (kind)
            {
                case RewardKind.MediumCash: return new Reward { Kind = kind, CashMinutes = t.MediumCashMinutes };
                case RewardKind.Boost: return new Reward { Kind = kind, BoostMultiplier = t.BoostMultiplier, BoostSeconds = t.BoostSeconds };
                case RewardKind.Gems: return new Reward { Kind = kind, Gems = t.Gems };
                case RewardKind.Jackpot: return new Reward { Kind = kind, CashMinutes = t.JackpotMinutes };
                default: return new Reward { Kind = RewardKind.SmallCash, CashMinutes = t.SmallCashMinutes };
            }
        }

        private const uint SaltDelay = 0x5D1A7u;
        private const uint SaltReward = 0xC01Bu;

        /// <summary>FNV-1a over the player ID, folded with the cycle, index and stream, so neighbours do not walk together.</summary>
        private static uint Seed(string playerId, long cycle, int index, uint salt)
        {
            uint hash = 2166136261u;
            if (playerId != null)
                for (int i = 0; i < playerId.Length; i++) hash = unchecked((hash ^ playerId[i]) * 16777619u);
            hash = unchecked((hash ^ (uint)cycle) * 16777619u);
            hash = unchecked((hash ^ (uint)(cycle >> 32)) * 16777619u);
            hash = unchecked((hash ^ (uint)index) * 16777619u);
            hash = unchecked((hash ^ salt) * 16777619u);
            hash = unchecked(hash * 2654435761u);
            hash ^= hash >> 16;
            return hash == 0u ? 1u : hash;
        }

        private static uint Next(uint x)
        {
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            return x;
        }

        private static double Unit(uint x) => (x >> 8) * (1d / 16777216d);

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        private static long FloorDiv(long a, long b)
        {
            long q = a / b;
            return (a % b != 0L && (a < 0L) != (b < 0L)) ? q - 1L : q;
        }

        private static bool Positive(double v) => v > 0d && Finite(v);
        private static bool NonNegative(double v) => v >= 0d && Finite(v);
        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
