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
    /// </summary>
    public static class Foremen
    {
        /// <summary>One slot per station. Saves address foremen by this index, so it must never be
        /// reordered — it is <see cref="IslandEconomy.Stations"/>'s order and stays tied to it.</summary>
        public const int Count = 8;

        /// <summary>Not hired. Level 1 is a hire; there is no level 0 foreman.</summary>
        public const int NotHired = 0;

        /// <summary>How far a foreman can be levelled. Reaching it is meant to take months of
        /// duplicates, not a weekend of gems.</summary>
        public const int MaxLevel = 10;

        /// <summary>How rare a slot's foreman is. Fixed per slot rather than rolled, because a roster
        /// you cannot plan for is a roster you cannot save toward.</summary>
        public enum Rarity { Common = 0, Rare = 1, Epic = 2 }

        /// <summary>
        /// Which rarity sits in which slot, indexed by station. The two ends of the chain are the
        /// expensive ones: MINE is where everything starts and MARKET is where the money is actually
        /// made, so those are the slots worth saving for. POWER PLANT is Epic because it multiplies
        /// both income and speed on the island and is the last thing a player unlocks.
        /// </summary>
        public static readonly Rarity[] Slots =
        {
            Rarity.Epic,    // MINE
            Rarity.Common,  // TRAIN
            Rarity.Common,  // STORAGE
            Rarity.Rare,    // ORE TRUCKS
            Rarity.Rare,    // SMELTER
            Rarity.Rare,    // CARGO TRUCKS
            Rarity.Epic,    // MARKET
            Rarity.Common,  // POWER PLANT
        };

        // A foreman's NAME is their station's name — the loc table already carries all eight in
        // eleven languages under istasyon.*, so the UI asks for those rather than duplicating them.

        // ------------------------------------------------------------------ tuning
        /// <summary>Everything a designer can move, so the numbers below are defaults rather than
        /// decisions. Mirrors the shape of <see cref="IslandEconomy.Tuning"/>.</summary>
        public struct Tuning
        {
            /// <summary>What one level of a foreman is worth, per rarity. Applied to that station's
            /// throughput and added into the empire income multiplier.</summary>
            public double CommonPerLevel, RarePerLevel, EpicPerLevel;

            /// <summary>Gems to hire, per rarity.</summary>
            public long CommonHireGems, RareHireGems, EpicHireGems;

            /// <summary>Duplicates needed to go from level L to L+1: Base + Step * (L - 1).</summary>
            public int DuplicateBase, DuplicateStep;

            /// <summary>Gems charged alongside the duplicates, growing the same way.</summary>
            public long LevelGemBase, LevelGemStep;

            public static Tuning Default => new Tuning
            {
                // A maxed roster of 3 Common, 3 Rare, 2 Epic lands at
                //   1 + 3(0.020x10) + 3(0.030x10) + 2(0.045x10) = 3.4x
                // which is the size the second gear is meant to be. Prestige used to hand out 70x at
                // coal, and the economy pass measured that as the thing breaking the ladder — this is
                // deliberately an order of magnitude smaller and earned over far longer.
                CommonPerLevel = 0.020d,
                RarePerLevel   = 0.030d,
                EpicPerLevel   = 0.045d,

                CommonHireGems = 150,
                RareHireGems   = 400,
                EpicHireGems   = 900,

                DuplicateBase = 2, DuplicateStep = 2,     // 2,4,6,… = 90 duplicates to max one foreman
                LevelGemBase  = 60, LevelGemStep = 45,
            };
        }

        // ------------------------------------------------------------------ worth
        /// <summary>What one level is worth in a given slot.</summary>
        public static double PerLevel(int station, in Tuning t)
        {
            switch (Slot(station))
            {
                case Rarity.Epic: return t.EpicPerLevel;
                case Rarity.Rare: return t.RarePerLevel;
                default:          return t.CommonPerLevel;
            }
        }

        /// <summary>
        /// One station's throughput multiplier. 1.0 when nobody is hired, so an empty roster changes
        /// nothing anywhere and the whole feature can be switched off by leaving it empty.
        /// </summary>
        public static double StationMultiplier(int[] levels, int station, in Tuning t)
        {
            int level = LevelOf(levels, station);
            if (level <= NotHired) return 1d;
            return 1d + PerLevel(station, t) * level;
        }

        /// <summary>
        /// What the whole roster is worth to income, as one multiplier on the empire. This is the half
        /// that survives the per-island income cap — see the class summary.
        /// </summary>
        public static double IncomeMultiplier(int[] levels, in Tuning t)
        {
            if (levels == null) return 1d;
            double sum = 0d;
            int n = levels.Length < Count ? levels.Length : Count;
            for (int s = 0; s < n; s++)
            {
                int level = levels[s];
                if (level <= NotHired) continue;
                if (level > MaxLevel) level = MaxLevel;
                sum += PerLevel(s, t) * level;
            }
            return 1d + sum;
        }

        // ------------------------------------------------------------------ price
        /// <summary>Gems to hire the foreman in this slot. Only meaningful while they are unhired.</summary>
        public static long HireGems(int station, in Tuning t)
        {
            switch (Slot(station))
            {
                case Rarity.Epic: return t.EpicHireGems;
                case Rarity.Rare: return t.RareHireGems;
                default:          return t.CommonHireGems;
            }
        }

        /// <summary>Duplicates to take a foreman from <paramref name="level"/> to the next one.</summary>
        public static int DuplicatesToLevel(int level, in Tuning t)
        {
            if (level < 1 || level >= MaxLevel) return 0;
            return t.DuplicateBase + t.DuplicateStep * (level - 1);
        }

        /// <summary>Gems charged alongside those duplicates.</summary>
        public static long GemsToLevel(int level, in Tuning t)
        {
            if (level < 1 || level >= MaxLevel) return 0;
            return t.LevelGemBase + t.LevelGemStep * (level - 1);
        }

        /// <summary>Every duplicate a foreman will ever need, for a progress readout that does not lie
        /// about how long the road is.</summary>
        public static int DuplicatesToMax(in Tuning t)
        {
            int total = 0;
            for (int level = 1; level < MaxLevel; level++) total += DuplicatesToLevel(level, t);
            return total;
        }

        // ------------------------------------------------------------------ state
        public static Rarity Slot(int station)
            => station >= 0 && station < Slots.Length ? Slots[station] : Rarity.Common;

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

        /// <summary>True once every slot is hired and levelled out — the end of the collection.</summary>
        public static bool RosterComplete(int[] levels)
        {
            if (levels == null || levels.Length < Count) return false;
            for (int s = 0; s < Count; s++)
                if (levels[s] < MaxLevel) return false;
            return true;
        }

        /// <summary>How many slots are hired at all. The number worth putting on a collection screen.</summary>
        public static int HiredCount(int[] levels)
        {
            if (levels == null) return 0;
            int n = 0, len = levels.Length < Count ? levels.Length : Count;
            for (int s = 0; s < len; s++) if (levels[s] > NotHired) n++;
            return n;
        }
    }
}
