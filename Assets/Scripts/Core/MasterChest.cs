using System;

namespace Game.Core
{
    /// <summary>
    /// The master chest: what gems buy, and how often the free one comes round.
    ///
    /// TWO ROLLS, NOT ONE. A card's rarity is a fact about the master it names
    /// (<see cref="Foremen.Rarity"/>), so the chest has to decide it: rarity first, off the weights in
    /// <see cref="Tuning"/>, then a flat station roll inside that rarity. It used to roll a slot flat
    /// over eight, because rarity was earned rather than drawn and every master was reachable from the
    /// first chest. Fifteen cards at three fixed rarities is the opposite shape, and a flat roll over
    /// it would hand out Legendaries at the same rate as Commons.
    ///
    /// STILL NO PITY. The honest fix for a dry run is the directed card:
    /// <see cref="Tuning.DirectedPerChest"/> of every chest is aimed at the master furthest behind
    /// rather than rolled. It bounds the worst case better than a pity counter would — a dry run
    /// cannot last longer than one chest — and it costs the balance nothing, because the card count is
    /// unchanged. The service does the aiming; this file only says how many. What a pity counter would
    /// buy on top is a guaranteed Legendary on a schedule, and a Legendary you can predict the arrival
    /// of is the one card in the set that stops being worth opening a chest for.
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
            /// rest are rolled — rarity, then station.</summary>
            public int DirectedPerChest;

            /// <summary>How often each rarity comes up. Relative rather than normalised, so a designer
            /// can raise one without having to fix the other two to keep a total.</summary>
            public double WeightCommon, WeightRare, WeightLegendary;

            /// <summary>Seconds between free chests.</summary>
            public long FreeIntervalSeconds;

            /// <summary>Cards the free chest pays. Smaller than a bought one — it is a drip, not a
            /// reason to stop buying.</summary>
            public int FreeCards;

            public static Tuning Default => new Tuning
            {
                // Four cards a chest against a 50-100 card road per master. It was three against a
                // 90-card road across eight masters; fifteen masters is half again as much collection,
                // so the chest grew with it rather than letting the same chest pace a longer game.
                CardsPerChest = 4,

                // Gems used to buy a hire outright (150-900) and hires are gone, so this is where that
                // sink moved. 60 is inside a single rewarded-ad day; ten at 540 is the 10% bulk
                // discount the store already trains players to expect.
                GemCost     = 60L,
                BulkCount   = 10,
                BulkGemCost = 540L,

                // One in three. Enough that no master can be starved for more than a chest, not so much
                // that the roll stops mattering.
                DirectedPerChest = 1,

                // 70 / 25 / 5. A Legendary is one card in twenty and there are five of them, so the
                // first one lands somewhere in the first hundred cards — about two weeks of free
                // chests — and the set of five is a months-long tail rather than a wall.
                WeightCommon    = 70d,
                WeightRare      = 25d,
                WeightLegendary = 5d,

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
        /// Which rarity a rolled card carries. <paramref name="roll"/> is in [0,1). Weights are
        /// relative; all-zero weights fall back to Common rather than dividing by nothing.
        /// </summary>
        public static Foremen.Rarity RollRarity(double roll, in Tuning t)
        {
            double common = t.WeightCommon > 0d ? t.WeightCommon : 0d;
            double rare = t.WeightRare > 0d ? t.WeightRare : 0d;
            double legendary = t.WeightLegendary > 0d ? t.WeightLegendary : 0d;
            double total = common + rare + legendary;
            if (total <= 0d) return Foremen.Rarity.Common;

            roll = Unit(roll) * total;
            if (roll < common) return Foremen.Rarity.Common;
            if (roll < common + rare) return Foremen.Rarity.Rare;
            return Foremen.Rarity.Legendary;
        }

        /// <summary>
        /// Which master a rolled card belongs to: <paramref name="rarityRoll"/> picks the rarity and
        /// <paramref name="stationRoll"/> picks flat among the five stations carrying it. Both are in
        /// [0,1) and are the only source of chance in the whole system.
        /// </summary>
        public static int RollMaster(double rarityRoll, double stationRoll, in Tuning t)
        {
            Foremen.Rarity rank = RollRarity(rarityRoll, t);
            int station = (int)(Unit(stationRoll) * Foremen.StationCount);
            if (station < 0) station = 0;
            if (station >= Foremen.StationCount) station = Foremen.StationCount - 1;
            return Foremen.IndexOf(station, rank);
        }

        /// <summary>
        /// A roll clamped into [0,1). NaN fails every comparison, so it survives a clamp written as
        /// two ifs and then casts to an out-of-range int. Catch it by name rather than by luck.
        /// </summary>
        private static double Unit(double roll)
        {
            if (double.IsNaN(roll) || roll < 0d) return 0d;
            return roll >= 1d ? 0.9999999999d : roll;
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
