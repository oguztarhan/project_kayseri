using System;

namespace Game.Core
{
    /// <summary>
    /// The master chest: what gems buy, and how often the free one comes round.
    ///
    /// WHY THERE IS NO RARITY ROLL HERE, unlike <see cref="CaptainCrate"/>. A captain crate has to
    /// decide how rare the card it hands over is, because the captains are ten different people at five
    /// grades and a Mythic pull is the event the whole crate is built around. A master chest never makes
    /// that decision: there are exactly eight masters, one per station, every one of them reachable from
    /// the first chest, and rarity is not a property of the card — it is how far you have taken that
    /// master (see <see cref="Foremen.TierOf"/>). So the chest rolls a SLOT, flat, and the excitement
    /// lives in which master moves rather than in what dropped.
    ///
    /// That also means pity would be answering a question nobody asked. There is no grade to be starved
    /// of, only a slot that has not come up lately, and the honest fix for that is the directed card:
    /// <see cref="Tuning.DirectedPerChest"/> of every chest is aimed at the master furthest behind
    /// rather than rolled. It bounds the worst case better than a pity counter would — a dry run cannot
    /// last longer than one chest — and it costs the balance nothing, because the card count is
    /// unchanged. The service does the aiming; this file only says how many.
    ///
    /// THE ROLL IS AN ARGUMENT, NOT A CALL — the same split CaptainCrate uses, for the same reason: it
    /// is what lets the tests assert the distribution over ten thousand chests instead of hoping.
    ///
    /// THE FREE CHEST IS A DEADLINE, NOT A TIMER. It is stored as the unix second the last one was
    /// claimed, so it survives a quit, a clock change and a reinstall, and it cannot be farmed by
    /// leaving the app open. It banks at most one: a player who is away for a week comes back to one
    /// waiting chest, not seven. Nothing expires — an unclaimed chest simply waits — which is the rule
    /// every layer in Docs/FIVE_LAYERS.md is held to.
    /// </summary>
    public static class MasterChest
    {
        public struct Tuning
        {
            /// <summary>Cards one chest hands over.</summary>
            public int CardsPerChest;

            /// <summary>Gems for one chest.</summary>
            public long GemCost;

            /// <summary>How many a bulk open buys, and what it costs — cheaper per chest on purpose.</summary>
            public int BulkCount;
            public long BulkGemCost;

            /// <summary>Cards per chest aimed at the master furthest behind rather than rolled. The
            /// rest are flat over the eight slots.</summary>
            public int DirectedPerChest;

            /// <summary>Seconds between free chests.</summary>
            public long FreeIntervalSeconds;

            /// <summary>Cards the free chest pays. Smaller than a bought one — it is a drip, not a
            /// reason to stop buying.</summary>
            public int FreeCards;

            public static Tuning Default => new Tuning
            {
                // Three cards a chest against a 90-card road per master: a chest is visible progress on
                // one master rather than a rounding error across eight.
                CardsPerChest = 3,

                // Gems used to buy a hire outright (150-900) and hires are gone, so this is where that
                // sink moved. 60 is inside a single rewarded-ad day; ten at 540 is the 10% bulk
                // discount the store already trains players to expect.
                GemCost     = 60L,
                BulkCount   = 10,
                BulkGemCost = 540L,

                // One in three. Enough that no master can be starved for more than a chest, not so much
                // that the roll stops mattering.
                DirectedPerChest = 1,

                // Eight hours: twice a day for a player who opens the game morning and night, once for
                // everyone else, and never a reason to set an alarm.
                FreeIntervalSeconds = 28800L,
                FreeCards           = 2,
            };
        }

        // -------------------------------------------------------------------- cost
        /// <summary>Gems for <paramref name="chests"/> opened at once. The bulk count is the only
        /// discounted size; anything else is priced one at a time.</summary>
        public static long Cost(int chests, in Tuning t)
        {
            if (chests <= 0) return 0L;
            long single = Math.Max(0L, t.GemCost);
            if (t.BulkCount > 0 && chests == t.BulkCount) return Math.Max(0L, t.BulkGemCost);
            return single * chests;
        }

        /// <summary>Cards <paramref name="chests"/> hand over in total, directed ones included.</summary>
        public static int CardsFor(int chests, in Tuning t)
        {
            if (chests <= 0) return 0;
            int per = t.CardsPerChest < 0 ? 0 : t.CardsPerChest;
            return per * chests;
        }

        /// <summary>How many of one chest's cards are aimed rather than rolled. Never more than the
        /// chest holds, so a mis-set config cannot manufacture cards.</summary>
        public static int DirectedIn(in Tuning t)
        {
            int per = t.CardsPerChest < 0 ? 0 : t.CardsPerChest;
            int aimed = t.DirectedPerChest < 0 ? 0 : t.DirectedPerChest;
            return aimed > per ? per : aimed;
        }

        // -------------------------------------------------------------------- roll
        /// <summary>
        /// Which master a rolled card belongs to. <paramref name="roll"/> is in [0,1) and is the only
        /// source of chance in the whole system. Flat across the eight slots — see the class summary.
        /// </summary>
        public static int RollSlot(double roll)
        {
            // NaN fails every comparison, so it survives a clamp written as two ifs and then casts to
            // an out-of-range int. Catch it by name rather than by luck.
            if (double.IsNaN(roll) || roll < 0d) roll = 0d;
            if (roll >= 1d) roll = 0.9999999999d;
            int slot = (int)(roll * Foremen.Count);
            if (slot < 0) slot = 0;
            if (slot >= Foremen.Count) slot = Foremen.Count - 1;
            return slot;
        }

        // -------------------------------------------------------------------- free
        /// <summary>
        /// When the next free chest comes due. <paramref name="lastClaimUnix"/> of 0 means it has never
        /// been claimed, which reads as due now — a fresh save opens the game with a chest waiting,
        /// because the first thing a collection screen should do is hand you something.
        /// </summary>
        public static long FreeReadyAtUnix(long lastClaimUnix, in Tuning t)
        {
            if (lastClaimUnix <= 0L) return 0L;
            long interval = t.FreeIntervalSeconds < 0L ? 0L : t.FreeIntervalSeconds;
            return lastClaimUnix + interval;
        }

        /// <summary>True when the free chest can be claimed. A clock rolled backwards only ever delays
        /// it, never pays twice.</summary>
        public static bool FreeReady(long nowUnix, long lastClaimUnix, in Tuning t)
            => nowUnix >= FreeReadyAtUnix(lastClaimUnix, t);

        /// <summary>Seconds still to wait, for a countdown label. Zero once it is ready.</summary>
        public static long FreeSecondsLeft(long nowUnix, long lastClaimUnix, in Tuning t)
        {
            long due = FreeReadyAtUnix(lastClaimUnix, t);
            return nowUnix >= due ? 0L : due - nowUnix;
        }
    }
}
