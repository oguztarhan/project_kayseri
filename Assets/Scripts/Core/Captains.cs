using System;

namespace Game.Core
{
    /// <summary>
    /// The captain roster as pure maths: who can be found, what they are worth aboard, and what a
    /// level costs. The fifth of these files after <see cref="IslandEconomy"/>,
    /// <see cref="MarketFlow"/>, <see cref="Foremen"/> and <see cref="Voyages"/>, and it exists for
    /// the same reason as all four.
    ///
    /// WHY A SECOND ROSTER. <see cref="Foremen"/> is eight fixed slots bolted to the eight stations:
    /// you know from the first day exactly who exists, and the whole point of its comment is that "a
    /// roster you cannot plan for is a roster you cannot save toward". That is the right shape for an
    /// economy bonus and the wrong shape for a collection, which needs the opposite — someone you did
    /// not expect, who you did not know you wanted. So the foremen keep the island and the captains
    /// take the sea, and the two never touch the same number.
    ///
    /// WHY "CAPTAIN" AND NOT "CREW". The plan called these crew. The word was already taken twice
    /// over: <see cref="Voyages.Crew"/> is ship upgrade track 2, and <c>sefer.murettebat</c> is the
    /// shipyard tab that buys it. Those are the anonymous hands that make a hull carry more; a captain
    /// is a named person who commands one voyage. Renaming afterwards would have cost a save
    /// migration, so it was done here.
    ///
    /// THE RULE THAT SHAPED EVERY EFFECT BELOW. Docs/VOYAGES.md §21 records that the first voyage
    /// defaults were wrong by about 2.5x, and names the cause: "a multiplicative stack — tier payout x
    /// hold x crew — where each factor was defensible alone and the product was not." Those numbers
    /// were then re-solved against four constraints at once. So NOTHING here multiplies
    /// <see cref="Voyages.Cards"/>. A captain moves charts, salvage, risk, the repair window and where
    /// cards land — five knobs, none of them in that solve.
    /// </summary>
    public static class Captains
    {
        /// <summary>How many captains exist. Saves address them by index, so this may grow but must
        /// never shrink or be reordered — a new captain is APPENDED.</summary>
        public const int Count = 10;

        /// <summary>Level 0 is a captain you have never pulled. There is no level-0 captain aboard.</summary>
        public const int NotOwned = 0;

        /// <summary>How far a captain can be levelled. The same ceiling <see cref="Foremen.MaxLevel"/>
        /// uses, deliberately: two rosters with two different ladders is two things to learn.</summary>
        public const int MaxLevel = 10;

        /// <summary>
        /// Grades, rarest last. Five of them because that is what the collection needs to feel like a
        /// ladder; <see cref="Foremen"/>'s three are enough for eight fixed slots and would not be
        /// here. Saves store a captain's INDEX, not their grade, so this enum is free to gain a sixth.
        /// </summary>
        public enum Grade { Common = 0, Rare = 1, Epic = 2, Legendary = 3, Mythic = 4 }
        public const int GradeCount = 5;

        // Role indices. A captain's role is authored below rather than saved, so these are addressed
        // by the loc table and the UI but never by a save file.
        public const int Quartermaster = 0, Gunner = 1, Bosun = 2, Purser = 3;
        public const int RoleCount = 4;

        /// <summary>One captain: who they are, what they do, and how hard they are to find.</summary>
        public struct Card
        {
            /// <summary>Loc id. Stays lower-case ASCII because it is also the key into the table.</summary>
            public string Id;
            public int Role;
            public Grade Rank;
        }

        /// <summary>
        /// Everyone who can be found, in save order.
        ///
        /// Every role appears at two grades or more, so no role is a trap you can only draw badly, and
        /// every grade has someone in it, so the crate's whole weight table is reachable. The four
        /// Commons are one of each role on purpose: whatever a new player pulls first, it does
        /// something they can point at.
        /// </summary>
        public static readonly Card[] Roster =
        {
            new Card { Id = "kemal",  Role = Quartermaster, Rank = Grade.Common    },
            new Card { Id = "selim",  Role = Gunner,        Rank = Grade.Common    },
            new Card { Id = "musa",   Role = Bosun,         Rank = Grade.Common    },
            new Card { Id = "derya",  Role = Purser,        Rank = Grade.Common    },
            new Card { Id = "zehra",  Role = Quartermaster, Rank = Grade.Rare      },
            new Card { Id = "baran",  Role = Purser,        Rank = Grade.Rare      },
            new Card { Id = "orhan",  Role = Bosun,         Rank = Grade.Epic      },
            new Card { Id = "nihal",  Role = Quartermaster, Rank = Grade.Epic      },
            new Card { Id = "husrev", Role = Gunner,        Rank = Grade.Legendary },
            new Card { Id = "ates",   Role = Bosun,         Rank = Grade.Mythic    },
        };

        // ------------------------------------------------------------------ tuning
        /// <summary>
        /// Everything a designer can move. Mirrors the shape of <see cref="Foremen.Tuning"/> and
        /// <see cref="Voyages.Tuning"/>; <c>Data/CaptainConfig</c> makes it Inspector-editable.
        /// </summary>
        public struct Tuning
        {
            /// <summary>What one level is worth, per grade. Drives the quartermaster, the gunner, the
            /// bosun's repair cut and the purser's share alike — one number per grade, so a captain's
            /// rank means the same thing whatever they do.</summary>
            public double CommonPerLevel, RarePerLevel, EpicPerLevel, LegendaryPerLevel, MythicPerLevel;

            /// <summary>
            /// Risk points a bosun takes off per level, per grade. On its OWN scale rather than derived
            /// from the numbers above, because risk is measured in absolute percentage points and
            /// everything else is a multiplier — deriving one from the other made a Mythic bosun erase
            /// the far reach outright.
            /// </summary>
            public double BosunRiskCommon, BosunRiskRare, BosunRiskEpic, BosunRiskLegendary, BosunRiskMythic;

            /// <summary>The least of a repair window a bosun can leave. A repair that can be cut to
            /// nothing is a failure with no cost, and the berth is where a failure is supposed to
            /// hurt (Docs/VOYAGES.md §18).</summary>
            public double MinRepairFraction;

            /// <summary>Duplicates to go from level L to L+1: Base + Step x (L - 1). No gem cost on
            /// top, unlike the foremen — charts already paid for the crate, and charging twice for one
            /// card would put the two rosters back in competition for the same wallet.</summary>
            public int DuplicateBase, DuplicateStep;

            /// <summary>
            /// The curve above, scaled per grade. A RARER CAPTAIN NEEDS FEWER COPIES, which is the
            /// opposite of the obvious answer and the only one that works.
            ///
            /// Measured before it was chosen. At one flat curve of 90 duplicates the roster paced like
            /// this: own all ten in 4 days, max a Common in 15 — and max the single Mythic in 370,
            /// because 0.66% of pulls carry them and ninety of those is fourteen thousand crates.
            /// Docs/VOYAGES.md §21 sizes one foreman at "four to six weeks of ordinary play"; a year
            /// for one captain is not a long tail, it is an unreachable one, and an unreachable ceiling
            /// makes the whole ladder beneath it read as pointless.
            ///
            /// Scaling by grade lands the ladder at roughly 16 / 17 / 22 / 21 / 58 days — ordinary
            /// captains in a fortnight or so, the Mythic still the trophy, none of them out of reach.
            /// </summary>
            public double DupScaleCommon, DupScaleRare, DupScaleEpic, DupScaleLegendary, DupScaleMythic;

            public static Tuning Default => new Tuning
            {
                // A level-10 Common is +40%, a level-10 Mythic +180%. The spread is what makes a
                // Mythic worth chasing; the floor is what keeps a Common worth levelling.
                CommonPerLevel    = 0.040d,
                RarePerLevel      = 0.060d,
                EpicPerLevel      = 0.090d,
                LegendaryPerLevel = 0.130d,
                MythicPerLevel    = 0.180d,

                // At level 10 these are 2, 3, 4, 5 and 6 risk points. A maxed Mythic bosun beside a
                // maxed foreman takes 26 points off the far reach, which still leaves 4 — deliberately.
                // Docs/VOYAGES.md §10 refuses to SELL guaranteed success; this refuses to hand it over
                // for a collection either, because a gamble with no downside is not a decision.
                BosunRiskCommon    = 0.0020d,
                BosunRiskRare      = 0.0030d,
                BosunRiskEpic      = 0.0040d,
                BosunRiskLegendary = 0.0050d,
                BosunRiskMythic    = 0.0060d,

                MinRepairFraction = 0.25d,

                // 2,4,6,… = 90 duplicates to max one captain, the same shape and the same total as
                // Foremen.Tuning. "Months of duplicates, not a weekend" is that file's phrase and the
                // reason is unchanged — this is the tail, not a sprint.
                // 2,4,6,… = 90 duplicates at Common, the same total Foremen.Tuning uses.
                DuplicateBase = 2, DuplicateStep = 2,

                // Totals these land on: 90 / 80 / 55 / 35 / 16.
                DupScaleCommon    = 1.00d,
                DupScaleRare      = 0.89d,
                DupScaleEpic      = 0.61d,
                DupScaleLegendary = 0.39d,
                DupScaleMythic    = 0.17d,
            };
        }

        // -------------------------------------------------------------------- read
        public static bool Exists(int captain) => captain >= 0 && captain < Roster.Length;

        public static Grade RankOf(int captain) => Exists(captain) ? Roster[captain].Rank : Grade.Common;

        public static int RoleOf(int captain) => Exists(captain) ? Roster[captain].Role : Quartermaster;

        /// <summary>Loc id, or "" for an index off the roster.</summary>
        public static string IdOf(int captain) => Exists(captain) ? Roster[captain].Id : string.Empty;

        /// <summary>How many captains carry a grade — what the crate divides its weight among.</summary>
        public static int CountOfGrade(Grade grade)
        {
            int n = 0;
            for (int i = 0; i < Roster.Length; i++) if (Roster[i].Rank == grade) n++;
            return n;
        }

        /// <summary>The <paramref name="nth"/> captain of a grade, or -1 when that grade is empty.</summary>
        public static int OfGrade(Grade grade, int nth)
        {
            if (nth < 0) return -1;
            for (int i = 0; i < Roster.Length; i++)
            {
                if (Roster[i].Rank != grade) continue;
                if (nth == 0) return i;
                nth--;
            }
            return -1;
        }

        /// <summary>What one level of this captain is worth.</summary>
        public static double PerLevel(int captain, in Tuning t)
        {
            switch (RankOf(captain))
            {
                case Grade.Mythic:    return t.MythicPerLevel;
                case Grade.Legendary: return t.LegendaryPerLevel;
                case Grade.Epic:      return t.EpicPerLevel;
                case Grade.Rare:      return t.RarePerLevel;
                default:              return t.CommonPerLevel;
            }
        }

        private static double BosunRiskPerLevel(int captain, in Tuning t)
        {
            switch (RankOf(captain))
            {
                case Grade.Mythic:    return t.BosunRiskMythic;
                case Grade.Legendary: return t.BosunRiskLegendary;
                case Grade.Epic:      return t.BosunRiskEpic;
                case Grade.Rare:      return t.BosunRiskRare;
                default:              return t.BosunRiskCommon;
            }
        }

        /// <summary>How much of the duplicate curve this captain's grade actually pays.</summary>
        public static double DuplicateScale(int captain, in Tuning t)
        {
            switch (RankOf(captain))
            {
                case Grade.Mythic:    return t.DupScaleMythic;
                case Grade.Legendary: return t.DupScaleLegendary;
                case Grade.Epic:      return t.DupScaleEpic;
                case Grade.Rare:      return t.DupScaleRare;
                default:              return t.DupScaleCommon;
            }
        }

        // ----------------------------------------------------------------- levels
        /// <summary>
        /// Duplicates to take <paramref name="captain"/> from <paramref name="level"/> to the next.
        /// 0 at the ceiling, and never less than one — a level that costs nothing is not a level.
        /// </summary>
        public static int DuplicatesToLevel(int captain, int level, in Tuning t)
        {
            if (level < 1 || level >= MaxLevel) return 0;
            double curve = Math.Max(1, t.DuplicateBase) + Math.Max(0, t.DuplicateStep) * (level - 1);
            double scale = DuplicateScale(captain, t);
            if (scale <= 0d) scale = 1d;
            int n = (int)Math.Round(curve * scale, MidpointRounding.AwayFromZero);
            return n < 1 ? 1 : n;
        }

        /// <summary>Every duplicate a captain will ever want — what "maxed" costs from level 1.</summary>
        public static int DuplicatesToMax(int captain, in Tuning t)
        {
            int total = 0;
            for (int l = 1; l < MaxLevel; l++) total += DuplicatesToLevel(captain, l, t);
            return total;
        }

        public static int[] NewLevels() => new int[Count];

        /// <summary>How many captains have been found at all.</summary>
        public static int OwnedCount(int[] levels)
        {
            if (levels == null) return 0;
            int n = 0;
            int len = levels.Length < Count ? levels.Length : Count;
            for (int i = 0; i < len; i++) if (levels[i] > NotOwned) n++;
            return n;
        }

        // ---------------------------------------------------------------- aboard
        // Each of these takes the ONE captain aboard a voyage. -1, an unowned captain and a captain of
        // the wrong role all read as "nobody is doing that job", so a caller never has to check first.

        private static bool Doing(int role, int captain, int level)
            => Exists(captain) && level > NotOwned && Roster[captain].Role == role;

        /// <summary>Multiplier on the charts a voyage brings home. 1 when no quartermaster is aboard.</summary>
        public static double ChartMultiplier(int captain, int level, in Tuning t)
            => Doing(Quartermaster, captain, level)
             ? 1d + PerLevel(captain, t) * Clamp(level, 0, MaxLevel)
             : 1d;

        /// <summary>Multiplier on the salvage a voyage brings home. 1 when no gunner is aboard.</summary>
        public static double SalvageMultiplier(int captain, int level, in Tuning t)
            => Doing(Gunner, captain, level)
             ? 1d + PerLevel(captain, t) * Clamp(level, 0, MaxLevel)
             : 1d;

        /// <summary>
        /// Risk points a bosun takes off the route, ON TOP of whatever the foreman aboard takes.
        ///
        /// They stack rather than taking the better of the two, because a rule that discards the
        /// smaller number makes one of the two officers pointless the moment the other is levelled —
        /// which is the trap Docs/VOYAGES.md R1 names for currencies, and it reads the same way for
        /// slots. What keeps that safe is the size of the numbers, not a special case: see
        /// <see cref="Tuning.BosunRiskMythic"/>.
        /// </summary>
        public static double RiskReduction(int captain, int level, in Tuning t)
            => Doing(Bosun, captain, level)
             ? Math.Max(0d, BosunRiskPerLevel(captain, t)) * Clamp(level, 0, MaxLevel)
             : 0d;

        /// <summary>
        /// What the repair window is multiplied by after a failure. A bosun shortens it; nobody
        /// lengthens it. Floored at <see cref="Tuning.MinRepairFraction"/> so the berth always costs
        /// something — that is where a failure is supposed to be felt.
        /// </summary>
        public static double RepairMultiplier(int captain, int level, in Tuning t)
        {
            if (!Doing(Bosun, captain, level)) return 1d;
            double cut = PerLevel(captain, t) * Clamp(level, 0, MaxLevel);
            double left = 1d - cut;
            double floor = Clamp01(t.MinRepairFraction);
            return left < floor ? floor : left;
        }

        /// <summary>
        /// The share of a voyage's cards a purser aims rather than leaving to the roll — 0 when nobody
        /// is doing that job, 1 when they place every card.
        ///
        /// This is the only effect that moves no number at all: the cards are the same cards, they
        /// just land on the foreman who is furthest from their next level instead of on whoever the
        /// dice picked. Ninety duplicates per foreman is a long enough road that WHERE a card lands is
        /// worth as much as how many arrive, and it costs the balance nothing.
        /// </summary>
        public static double DirectedShare(int captain, int level, in Tuning t)
        {
            if (!Doing(Purser, captain, level)) return 0d;
            return Clamp01(PerLevel(captain, t) * Clamp(level, 0, MaxLevel));
        }

        private static double Clamp01(double v) => v < 0d ? 0d : (v > 1d ? 1d : v);

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
