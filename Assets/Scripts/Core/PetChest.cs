using System;

namespace Game.Core
{
    /// <summary>
    /// The pet chest: what one opening comes out as, and what stops a run of bad luck lasting
    /// forever. The same shape <see cref="CaptainCrate"/> and <see cref="CardCollectionPack"/> already
    /// use, narrowed to this feature's one difference from both — every rarity is always reachable,
    /// because every rarity carries all six species rather than a named subset of them.
    ///
    /// THE ROLL IS AN ARGUMENT, NOT A CALL, for the reason every chest in this project gives: nothing
    /// here touches a random number generator and nothing reads a save, so the whole distribution can
    /// be asserted over two hundred thousand opens exactly rather than sampled and hoped.
    ///
    /// NO POOL ARGUMENT, UNLIKE CardCollectionPack. That file's <c>WeightOf</c> zeroes a rarity its
    /// catalogue carries no card at; this one never needs to, because <see cref="Pets.Roster"/> is
    /// fixed at six members and every one of them exists at every rarity (a pet's rarity is how it
    /// fused, not which species it is). A rarity with real weight here is always a rarity the chest
    /// can actually hand over.
    ///
    /// TWO PITIES, NOT ONE — the same reasoning as the two reference systems: a short one keeps Epic
    /// common enough that opening always feels like it moves, a long one keeps Legendary rare enough
    /// that it stays an event, with a soft ramp in front of the long one so most dry runs end on a
    /// real roll rather than the guarantee.
    /// </summary>
    public static class PetChest
    {
        /// <summary>Pets one chest hands over.</summary>
        public const int PetsPerChest = 1;

        public struct Tuning
        {
            /// <summary>Base weights. Need not sum to one — every rarity here is always reachable, so
            /// unlike the two reference chests there is never a normalising empty row to worry about.</summary>
            public double CommonWeight, RareWeight, EpicWeight, LegendaryWeight, MythicWeight;

            /// <summary>Chests without an Epic-or-better before the next one is guaranteed to be one.
            /// 0 turns the guarantee off.</summary>
            public int EpicPity;

            /// <summary>Chests without a Legendary-or-better before the next one is guaranteed.</summary>
            public int LegendaryPity;

            /// <summary>Where the Legendary weight starts climbing, counted the same way.</summary>
            public int SoftPityStart;

            /// <summary>Weight added to Legendary for each chest past <see cref="SoftPityStart"/>.</summary>
            public double SoftPityStep;

            /// <summary>Pearls for one chest.</summary>
            public long PearlCost;

            /// <summary>How many chests the bulk button opens at once, and what that batch costs —
            /// cheaper per chest than buying the same count one at a time, the same hook
            /// <see cref="CaptainCrate"/>'s bulk open uses to make itself the button most players
            /// press.</summary>
            public int BulkCount;
            public long BulkPearlCost;

            public static Tuning Default => new Tuning
            {
                // 60 / 26 / 10.5 / 3 / 0.5. Against a roster where every rarity carries all six
                // species, that is 10% for a named Common and 0.083% for a named Mythic — see
                // Docs/PLAN_PETS.md for the simulation this was chosen against.
                CommonWeight    = 0.600d,
                RareWeight      = 0.260d,
                EpicWeight      = 0.105d,
                LegendaryWeight = 0.030d,
                MythicWeight    = 0.005d,

                // Epic-or-better lands on a median of four chests, so ten is a long way past unlucky
                // rather than a schedule.
                EpicPity = 10,

                // A fresh Legendary weight of 3% puts a median around twenty-three; seventy is the
                // outer bound with the soft ramp below meaning most dry runs end before it fires.
                LegendaryPity = 70,
                SoftPityStart = 45,
                SoftPityStep  = 0.010d,

                PearlCost     = 100L,
                BulkCount     = 10,
                BulkPearlCost = 900L,
            };
        }

        // ------------------------------------------------------------------ weights
        private static double BaseWeight(RosterCardState.Rarity rarity, in Tuning t)
        {
            switch (rarity)
            {
                case RosterCardState.Rarity.Common:    return t.CommonWeight;
                case RosterCardState.Rarity.Rare:      return t.RareWeight;
                case RosterCardState.Rarity.Epic:      return t.EpicWeight;
                case RosterCardState.Rarity.Legendary: return t.LegendaryWeight;
                case RosterCardState.Rarity.Mythic:    return t.MythicWeight;
                default:                               return 0d;
            }
        }

        /// <summary>Extra Legendary weight earned by a dry run. Zero until the ramp starts.</summary>
        public static double SoftPityBonus(int sinceLegendary, in Tuning t)
        {
            if (t.SoftPityStart <= 0 || t.SoftPityStep <= 0d) return 0d;
            int past = sinceLegendary + 1 - t.SoftPityStart;
            return past <= 0 ? 0d : t.SoftPityStep * past;
        }

        /// <summary>A rarity's weight for this chest. Never zeroed by an empty pool — see the class
        /// note — so only a misconfigured negative weight can silence a rarity here.</summary>
        public static double WeightOf(RosterCardState.Rarity rarity, int sinceLegendary, in Tuning t)
        {
            if (!Pets.IsRarity(rarity)) return 0d;
            double w = BaseWeight(rarity, t);
            if (w < 0d) w = 0d;
            if (rarity == RosterCardState.Rarity.Legendary) w += SoftPityBonus(sinceLegendary, t);
            return w;
        }

        /// <summary>
        /// The lowest rarity this chest may come out as. Legendary is tested first and wins, the same
        /// resolution <see cref="CaptainCrate.Floor"/> and <see cref="CardCollectionPack.Floor"/> give
        /// two guarantees falling due together: a Legendary is also an Epic-or-better, so paying the
        /// higher one pays both.
        /// </summary>
        public static RosterCardState.Rarity Floor(int sinceEpic, int sinceLegendary, in Tuning t)
        {
            if (t.LegendaryPity > 0 && sinceLegendary + 1 >= t.LegendaryPity)
                return RosterCardState.Rarity.Legendary;
            if (t.EpicPity > 0 && sinceEpic + 1 >= t.EpicPity)
                return RosterCardState.Rarity.Epic;
            return RosterCardState.Rarity.Common;
        }

        // --------------------------------------------------------------------- roll
        /// <summary>What rarity a chest comes out as. <paramref name="roll"/> is in [0,1) and is the
        /// only source of chance in the whole feature.</summary>
        public static RosterCardState.Rarity RollRarity(double roll, int sinceEpic, int sinceLegendary,
                                                         in Tuning t)
        {
            roll = Unit(roll);
            int floor = (int)Floor(sinceEpic, sinceLegendary, t);

            double total = 0d;
            for (int r = floor; r < Pets.RarityCount; r++)
                total += WeightOf((RosterCardState.Rarity)r, sinceLegendary, t);

            if (total <= 0d) return RosterCardState.Rarity.Common;

            double target = roll * total;
            double acc = 0d;
            for (int r = floor; r < Pets.RarityCount; r++)
            {
                acc += WeightOf((RosterCardState.Rarity)r, sinceLegendary, t);
                if (target < acc) return (RosterCardState.Rarity)r;
            }
            return RosterCardState.Rarity.Mythic;
        }

        /// <summary>Which species, flat among all six — a chest's rarity and its species are two
        /// independent rolls, so authoring a seventh species changes who you might get without
        /// touching how often any rarity appears.</summary>
        public static int RollSpecies(double roll)
        {
            int n = (int)(Unit(roll) * Pets.SpeciesCount);
            if (n < 0) n = 0;
            return n >= Pets.SpeciesCount ? Pets.SpeciesCount - 1 : n;
        }

        /// <summary>Moves the two dry counters on by one chest. Legendary clears both, the same rule
        /// every pitied chest in this project keeps.</summary>
        public static void Advance(RosterCardState.Rarity got, ref int sinceEpic, ref int sinceLegendary)
        {
            if (got >= RosterCardState.Rarity.Legendary) { sinceEpic = 0; sinceLegendary = 0; return; }
            if (got >= RosterCardState.Rarity.Epic) { sinceEpic = 0; sinceLegendary++; return; }
            sinceEpic++;
            sinceLegendary++;
        }

        // -------------------------------------------------------------------- price
        /// <summary>Pearls for this many chests. The bulk count is the one price that is not simply
        /// per-chest times count — everything else is.</summary>
        public static long Cost(int chests, in Tuning t)
        {
            if (chests <= 0) return 0L;
            long single = t.PearlCost < 0L ? 0L : t.PearlCost;
            if (t.BulkCount > 0 && chests == t.BulkCount)
                return t.BulkPearlCost < 0L ? 0L : t.BulkPearlCost;
            return single * chests;
        }

        /// <summary>A roll clamped into [0,1). NaN fails every comparison, so it survives a clamp
        /// written as two ifs rather than by luck.</summary>
        private static double Unit(double roll)
        {
            if (double.IsNaN(roll) || roll < 0d) return 0d;
            return roll >= 1d ? 0.9999999999d : roll;
        }
    }
}
