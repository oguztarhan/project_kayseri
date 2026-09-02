using System;

namespace Game.Core
{
    /// <summary>
    /// The foreman roster as pure maths: who you have hired, how far you have levelled them, and what
    /// that is worth. The market's counterpart is <see cref="MarketFlow"/> and the island's is
    /// <see cref="IslandEconomy"/>; this is the third, and it exists for the same reason as those two —
    /// a number the player is asked to invest in should be readable in one file rather than discovered
    /// by playing.
    ///
    /// WHY THIS AND NOT MANAGERS. The save used to carry a hiredManagers list and the economy config a
    /// flat x2 "managerBonus", which is the pay-once-to-automate-a-station idea. That is redundant
    /// here: the market yard is already a hire system, three jobs by five levels, and its whole promise
    /// is that a maxed yard runs itself forever without the player. A second automate-this-station
    /// layer on the island would restate it. So the roster is the other thing that model is good for —
    /// something to COLLECT, which the game had none of, and somewhere for gems to go, which the game
    /// also had none of: gems were earned from contracts, dailies and every rewarded ad, and could only
    /// ever be spent back inside the store.
    ///
    /// ONE FOREMAN PER STATION, ACCOUNT-WIDE. The eight slots are the eight stations in
    /// <see cref="IslandEconomy.Stations"/>, in that order, and a foreman works on every island at once
    /// rather than on the one you are standing on. Per-island would go stale the moment you sailed;
    /// account-wide means the mine foreman you levelled on coal is still the mine foreman on diamond,
    /// which is what makes the roster worth keeping rather than rebuying.
    ///
    /// TWO EFFECTS, DELIBERATELY. A foreman speeds their own station's throughput AND lifts the empire
    /// income ceiling. Throughput alone would be swallowed whole: every island's income is capped in
    /// MarketService, so a player near their cap — exactly the player who has been playing long enough
    /// to own foremen — would see a bonus do nothing at all. Lifting the ceiling is the half that pays;
    /// the throughput is the half you can watch.
    ///
    /// RARITY IS EARNED, NOT ROLLED. The slots used to carry a fixed rarity apiece — the mine foreman
    /// was Epic because the mine is important — which made rarity a fact about the station rather than
    /// anything the player did. It is now a fact about the MASTER: stars 1-10 promote him up five tiers
    /// (see <see cref="TierOf"/>), so a Legendary mine master is one you took there. That is what makes
    /// a chest worth opening after the roster is complete, and what the card frames, the plinth under
    /// his feet and his size on the island are all reading from. It also means a card is never dead:
    /// every master is reachable from the first chest and every card moves somebody up.
    /// </summary>
    public static class Foremen
    {
        /// <summary>One slot per station. Saves address foremen by this index, so it must never be
        /// reordered — it is <see cref="IslandEconomy.Stations"/>'s order and stays tied to it.</summary>
        public const int Count = 8;

        /// <summary>Nobody there. The first card for an empty slot puts a master at one star; there is
        /// no zero-star master. Kept as a name rather than a literal because saves are full of it.</summary>
        public const int NotHired = 0;

        /// <summary>How far a master can be taken. Reaching it is meant to take months of cards, not a
        /// weekend of gems.</summary>
        public const int MaxLevel = 10;

        /// <summary>How far along a master is, as the word the player sees. Two stars per tier.</summary>
        public enum Tier { Common = 0, Rare = 1, Epic = 2, Legendary = 3, Mythic = 4 }

        public const int TierCount = 5;

        /// <summary>The first star of each tier. Two stars apiece, so the promotion lands every other
        /// star-up and a card is never more than one star away from changing how a master looks.</summary>
        private static readonly int[] TierFloorStar = { 1, 3, 5, 7, 9 };

        /// <summary>
        /// Which tier a master at this many stars is in. An empty slot reads as Common — it is the
        /// colour a locked card is drawn in, not a claim that somebody is standing there; ask
        /// <see cref="IsHired"/> for that.
        /// </summary>
        public static Tier TierOf(int stars)
        {
            if (stars > MaxLevel) stars = MaxLevel;
            for (int tier = TierCount - 1; tier > 0; tier--)
                if (stars >= TierFloorStar[tier]) return (Tier)tier;
            return Tier.Common;
        }

        // A master's NAME is their station's name — the loc table already carries all eight in
        // eleven languages under istasyon.*, so the UI asks for those rather than duplicating them.

        // ------------------------------------------------------------------ tuning
        /// <summary>Everything a designer can move, so the numbers below are defaults rather than
        /// decisions. Mirrors the shape of <see cref="IslandEconomy.Tuning"/>.</summary>
        public struct Tuning
        {
            /// <summary>What a master at each star is worth to his own station's throughput, as a
            /// fraction added to 1.0. Two per tier, written out rather than derived, because the jump
            /// between tiers is the thing being tuned and a formula would hide it.</summary>
            public double CommonBoost1, CommonBoost2;
            public double RareBoost1, RareBoost2;
            public double EpicBoost1, EpicBoost2;
            public double LegendaryBoost1, LegendaryBoost2;
            public double MythicBoost1, MythicBoost2;

            /// <summary>How much of a master's boost also lifts the empire income ceiling. The whole
            /// boost would be enormous across eight masters; a tenth of it is the half that pays.</summary>
            public double IncomeShare;

            /// <summary>Cards needed to go from star L to L+1: Base + Step * (L - 1).</summary>
            public int DuplicateBase, DuplicateStep;

            public static Tuning Default => new Tuning
            {
                // Legendary's top star is +300% exactly — that is the number the feature was asked for
                // and the one the card advertises. The curve accelerates rather than stepping evenly,
                // so a promotion is always felt: the second star of a tier is worth more than the
                // first, and the first star of the next tier is worth more again.
                CommonBoost1    = 0.10d, CommonBoost2    = 0.20d,
                RareBoost1      = 0.45d, RareBoost2      = 0.70d,
                EpicBoost1      = 1.10d, EpicBoost2      = 1.60d,
                LegendaryBoost1 = 2.30d, LegendaryBoost2 = 3.00d,
                MythicBoost1    = 4.00d, MythicBoost2    = 5.00d,

                // Eight Legendary masters land the empire at 1 + 8(3.0 x 0.10) = 3.4x, which is exactly
                // where the old maxed roster landed and where the ladder was solved. Mythic stretches
                // the tail to 5.0x rather than moving the floor. Prestige used to hand out 70x at coal
                // and the economy pass measured that as the thing breaking the ladder; this stays an
                // order of magnitude below it and is earned over months.
                IncomeShare = 0.10d,

                DuplicateBase = 2, DuplicateStep = 2,     // 2,4,6,… = 90 cards to max one master
            };
        }

        // ------------------------------------------------------------------ worth
        /// <summary>
        /// What a master at <paramref name="stars"/> is worth, as a fraction added to 1.0. Zero for an
        /// empty slot, so an empty roster changes nothing anywhere and the whole feature can be
        /// switched off by leaving it empty. Clamped at both ends — a hand-edited save must not be able
        /// to buy itself a bigger multiplier than the top tier.
        /// </summary>
        public static double Boost(int stars, in Tuning t)
        {
            if (stars <= NotHired) return 0d;
            if (stars > MaxLevel) stars = MaxLevel;
            switch (stars)
            {
                case 1:  return t.CommonBoost1;
                case 2:  return t.CommonBoost2;
                case 3:  return t.RareBoost1;
                case 4:  return t.RareBoost2;
                case 5:  return t.EpicBoost1;
                case 6:  return t.EpicBoost2;
                case 7:  return t.LegendaryBoost1;
                case 8:  return t.LegendaryBoost2;
                case 9:  return t.MythicBoost1;
                default: return t.MythicBoost2;
            }
        }

        /// <summary>
        /// One station's throughput multiplier. 1.0 when the slot is empty.
        /// </summary>
        public static double StationMultiplier(int[] levels, int station, in Tuning t)
            => 1d + Boost(LevelOf(levels, station), t);

        /// <summary>
        /// What the whole roster is worth to income, as one multiplier on the empire. This is the half
        /// that survives the per-island income cap — see the class summary.
        /// </summary>
        public static double IncomeMultiplier(int[] levels, in Tuning t)
        {
            if (levels == null) return 1d;
            double sum = 0d;
            int n = levels.Length < Count ? levels.Length : Count;
            for (int s = 0; s < n; s++) sum += Boost(levels[s], t);
            return 1d + sum * t.IncomeShare;
        }

        // ------------------------------------------------------------------ price
        /// <summary>Cards to take a master from <paramref name="level"/> to the next star.</summary>
        public static int DuplicatesToLevel(int level, in Tuning t)
        {
            if (level < 1 || level >= MaxLevel) return 0;
            return t.DuplicateBase + t.DuplicateStep * (level - 1);
        }

        /// <summary>Every card a master will ever need, for a progress readout that does not lie about
        /// how long the road is.</summary>
        public static int DuplicatesToMax(in Tuning t)
        {
            int total = 0;
            for (int level = 1; level < MaxLevel; level++) total += DuplicatesToLevel(level, t);
            return total;
        }

        // ------------------------------------------------------------------ state
        /// <summary>Which tier the master in this slot is at. The whole of a card's colour, his plinth
        /// and his size on the island read from here.</summary>
        public static Tier TierOfStation(int[] levels, int station) => TierOf(LevelOf(levels, station));

        public static int LevelOf(int[] levels, int station)
        {
            if (levels == null || station < 0 || station >= levels.Length) return NotHired;
            int level = levels[station];
            if (level < NotHired) return NotHired;
            return level > MaxLevel ? MaxLevel : level;
        }

        public static bool IsHired(int[] levels, int station) => LevelOf(levels, station) > NotHired;

        public static bool IsMaxed(int[] levels, int station) => LevelOf(levels, station) >= MaxLevel;

        /// <summary>A fresh, empty roster.</summary>
        public static int[] NewLevels() => new int[Count];

        /// <summary>True once every slot is filled and starred out — the end of the collection.</summary>
        public static bool RosterComplete(int[] levels)
        {
            if (levels == null || levels.Length < Count) return false;
            for (int s = 0; s < Count; s++)
                if (levels[s] < MaxLevel) return false;
            return true;
        }

        /// <summary>How many masters you have at all. The number worth putting on a collection screen.</summary>
        public static int HiredCount(int[] levels)
        {
            if (levels == null) return 0;
            int n = 0, len = levels.Length < Count ? levels.Length : Count;
            for (int s = 0; s < len; s++) if (levels[s] > NotHired) n++;
            return n;
        }
    }
}
