namespace Game.Core
{
    /// <summary>
    /// What a finishing position in the three-day league is worth. The rules and the numbers only —
    /// the ranking itself is <see cref="Leaderboards"/>, and who hands the reward over is
    /// <c>Game.Systems.LadderService</c>.
    ///
    /// WHY THIS IS A SEPARATE FILE FROM <see cref="Leaderboards"/>. That one deliberately knows
    /// nothing about what a bracket pays — the same split <see cref="LiveEvents"/> keeps from the
    /// modules above it. A ranking is arithmetic and is the same in every build; a payout is content
    /// and is tuned. Keeping them apart is what lets the reward table move without a single ranking
    /// test being re-read.
    ///
    /// NO CASH, EVER. Docs/VOYAGES.md R1: <c>MarketService</c> is the only faucet, and a second one
    /// competes with it — whichever pays less becomes pointless. Gems and master cards are the two
    /// currencies the meta already runs on, and both are ceiling lifts rather than rates (R2).
    /// </summary>
    public static class Ladder
    {
        /// <summary>What one bracket hands over. Deliberately the same two fields
        /// <c>Chapters</c> pays in, so the ladder is not a third reward vocabulary.</summary>
        public struct Reward
        {
            public long Gems;
            public int Cards;
        }

        /// <summary>
        /// The payout table, one entry per bracket in <see cref="Leaderboards.DefaultBracketEnds"/>:
        /// 1st, 2nd, 3rd, 4-10, 11-20, 21-30.
        /// </summary>
        public struct Tuning
        {
            public Reward[] Brackets;

            /// <summary>
            /// Sized against what the rest of the economy already pays, rather than against the
            /// reference game's numbers, which are not ours to copy.
            ///
            /// Retuned 2026-09-17 against the free gem budget (RewardBudgetTests): a season tops out at
            /// 100 gems, and the budget counts a typical 4th-20th finish at about 25 a season — the
            /// league is a small, steady share of the week, not a second festival.
            ///
            /// The tail pays on purpose. 21st-30th is still 10 gems, so a season the player was never
            /// going to win is a reason to come back rather than a reason to stop looking. Cards stop
            /// at 10th because a card is the scarcer of the two and the podium has to keep something
            /// the tail does not get.
            /// </summary>
            public static Tuning Default => new Tuning
            {
                Brackets = new[]
                {
                    new Reward { Gems = 100L, Cards = 3 },   // 1st
                    new Reward { Gems =  70L, Cards = 2 },   // 2nd
                    new Reward { Gems =  50L, Cards = 2 },   // 3rd
                    new Reward { Gems =  35L, Cards = 1 },   // 4-10
                    new Reward { Gems =  20L, Cards = 0 },   // 11-20
                    new Reward { Gems =  10L, Cards = 0 },   // 21-30
                },
            };
        }

        /// <summary>How many brackets a well-formed table has: one per entry in
        /// <see cref="Leaderboards.DefaultBracketEnds"/>.</summary>
        public static int BracketCount => Leaderboards.DefaultBracketEnds.Length;

        /// <summary>
        /// Whether a table can be used at all. Checked rather than trusted, because a tuning that
        /// arrives short would otherwise pay nothing for the ranks past its end — a silent failure
        /// that looks exactly like a player finishing outside the brackets.
        /// </summary>
        public static bool IsWellFormed(in Tuning tuning)
        {
            if (tuning.Brackets == null || tuning.Brackets.Length != BracketCount) return false;
            for (int i = 0; i < tuning.Brackets.Length; i++)
                if (tuning.Brackets[i].Gems < 0L || tuning.Brackets[i].Cards < 0) return false;
            return true;
        }

        /// <summary>
        /// What a bracket index pays. Anything outside the table pays nothing — including -1, which is
        /// what <see cref="Leaderboards.RewardTier"/> answers for a player who was not on the board at
        /// all, and which must never be turned into a reward by an unchecked array read.
        /// </summary>
        public static Reward RewardFor(int tier, in Tuning tuning)
        {
            if (tier < 0 || !IsWellFormed(tuning) || tier >= tuning.Brackets.Length) return default;
            return tuning.Brackets[tier];
        }

        /// <summary>
        /// Whether a settled season owes the player anything. A settlement with no payout still
        /// produces an inbox row — the player is told where they finished either way — so the screen
        /// needs this to decide whether to draw a claim button or a plain result line.
        /// </summary>
        public static bool Pays(int tier, in Tuning tuning)
        {
            Reward reward = RewardFor(tier, tuning);
            return reward.Gems > 0L || reward.Cards > 0;
        }

        // ----------------------------------------------------------------------------- points
        /// <summary>One counted action: which goal metric, what it scores, and the most of it a
        /// season will count.</summary>
        public struct ScoringRule
        {
            public int Metric;
            public int PointsPerAction;
            public long SeasonCap;
        }

        /// <summary>
        /// What a season is ranked on (decided 2026-09-17, replacing raw bars sold).
        ///
        /// COUNTS, NEVER OUTPUT. Bars and cash inflate x3.2 per ore tier, so a bar-based score ranked
        /// how far along a player was rather than how much they played — and the two-island band
        /// still spans a whole tier. An upgrade, a contract, a repair and a foreman level mean the same
        /// thing on coal and on diamond, which is the reason the daily goals count them too.
        ///
        /// CAPPED PER SEASON, so no one metric can be ground without limit and a season has a known
        /// ceiling (<see cref="MaxSeasonPoints"/>) the generated cohort is sized against. Upgrades pay
        /// least per action because they are by far the most frequent.
        /// </summary>
        public static readonly ScoringRule[] Scoring =
        {
            new ScoringRule { Metric = Goals.Upgrades,      PointsPerAction = 1,  SeasonCap = 250L },
            new ScoringRule { Metric = Goals.Contracts,     PointsPerAction = 10, SeasonCap = 30L },
            new ScoringRule { Metric = Goals.Repairs,       PointsPerAction = 6,  SeasonCap = 30L },
            new ScoringRule { Metric = Goals.ForemanLevels, PointsPerAction = 15, SeasonCap = 10L },
        };

        /// <summary>What a rule scores for <paramref name="actions"/> taken this season: capped, and
        /// never negative — a counter that went backwards is no actions, not a debt.</summary>
        public static long PointsFor(in ScoringRule rule, long actions)
        {
            if (actions <= 0L || rule.PointsPerAction <= 0) return 0L;
            if (rule.SeasonCap >= 0L && actions > rule.SeasonCap) actions = rule.SeasonCap;
            return actions * rule.PointsPerAction;
        }

        /// <summary>The most a season can score: every rule at its cap.</summary>
        public static long MaxSeasonPoints
        {
            get
            {
                long total = 0L;
                for (int i = 0; i < Scoring.Length; i++) total += PointsFor(Scoring[i], Scoring[i].SeasonCap);
                return total;
            }
        }
    }
}
