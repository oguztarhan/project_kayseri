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
            /// A whole Production Sprint's milestone ladder pays 210 gems and 6 cards, and the master
            /// set costs about 14,400 gems. A season here tops out at 150 gems, so a player who wins
            /// every three-day season for a month collects roughly 1,500 — about a tenth of the set,
            /// which puts the league beside the sprint rather than ahead of it.
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
                    new Reward { Gems = 150L, Cards = 3 },   // 1st
                    new Reward { Gems = 100L, Cards = 2 },   // 2nd
                    new Reward { Gems =  75L, Cards = 2 },   // 3rd
                    new Reward { Gems =  40L, Cards = 1 },   // 4-10
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
    }
}
