using System;

namespace Game.Core
{
    /// <summary>
    /// The pack: what one card comes out as, and what stops a run of bad luck from lasting forever.
    ///
    /// THE ROLL IS AN ARGUMENT, NOT A CALL. Nothing in this file touches a random number generator
    /// and nothing in it reads a save. <see cref="RollRarity"/> takes a value in [0,1) and is a pure
    /// lookup — the same contract <see cref="CaptainCrate"/>, <see cref="Crafting"/> and
    /// <see cref="SeaCombat"/> keep, and the reason the tests can assert the whole distribution over
    /// two hundred thousand pulls exactly rather than sampling it and hoping. The service supplies
    /// the dice and owns the counters.
    ///
    /// ONE TABLE FOR EVERY PACK. The daily pack and the milestone packs roll identically. A second
    /// table would be a second balance surface and an odds sheet that had to apologise for both, and
    /// buys nothing a player can feel: a pack is a pack.
    ///
    /// THE POOL IS AN ARGUMENT TOO. How many cards carry each rarity comes from the authored
    /// catalogue, which lives in a config asset rather than in code, so it is passed in. That is also
    /// what makes <see cref="WeightOf"/> able to zero a rarity nobody carries — the same rule
    /// <see cref="CaptainCrate.WeightOf"/> applies to a grade with no captain behind it. Mythic ships
    /// with a weight and no cards, and stays unreachable until one is authored; the alternative is a
    /// pack that can roll a rank it cannot then hand over, and a silent re-roll nobody can see in the
    /// published odds.
    ///
    /// TWO PITIES, NOT ONE — <see cref="CaptainCrate"/>'s reasoning, unchanged. A single counter set
    /// short stops Legendaries being rare; set long it lets a new player draw thirty Commons and
    /// conclude the pack is broken. The short one guarantees an Epic often enough that a fortnight
    /// always shows progress; the long one guarantees a Legendary rarely enough that it stays an
    /// event, with a soft ramp in front of it so the guarantee is usually beaten by a real roll.
    ///
    /// WHY THE NUMBERS ARE WHAT THEY ARE. Simulated before they were chosen, and recorded in
    /// Docs/PLAN_14: the shipped table realises 51.6 / 27.7 / 14.5 / 6.2 percent over ten thousand
    /// opens, puts a first Epic at a median of four packs and a first Legendary at thirteen, and
    /// lands a player's first completed set around day 18 and their third around day 49.
    /// </summary>
    public static class CardCollectionPack
    {
        /// <summary>Cards one pack hands over. One, in v1 — a reveal is a ceremony, and a ceremony
        /// with four cards in it is a list.</summary>
        public const int CardsPerPack = 1;

        public struct Tuning
        {
            /// <summary>Base weights. They need not sum to one — everything is normalised against the
            /// rarities actually reachable, which is what keeps the table honest when the catalogue
            /// has nobody at a rarity yet.</summary>
            public double CommonWeight, RareWeight, EpicWeight, LegendaryWeight, MythicWeight;

            /// <summary>Packs without an Epic-or-better before the next one is guaranteed to be one.
            /// 0 turns the guarantee off.</summary>
            public int EpicPity;

            /// <summary>Packs without a Legendary-or-better before the next one is guaranteed.</summary>
            public int LegendaryPity;

            /// <summary>Where the Legendary weight starts climbing, counted the same way.</summary>
            public int SoftPityStart;

            /// <summary>Weight added to Legendary for each pack past <see cref="SoftPityStart"/>.</summary>
            public double SoftPityStep;

            public static Tuning Default => new Tuning
            {
                // 52 / 28 / 14 / 5.5 / 0.5. Against a 9/6/6/3/0 catalogue that is 5.81% for a named
                // Common and 1.84% for a named Legendary — a spread of about three, which is what
                // makes the last card of a set the one you remember drawing.
                CommonWeight    = 0.520d,
                RareWeight      = 0.280d,
                EpicWeight      = 0.140d,
                LegendaryWeight = 0.055d,
                MythicWeight    = 0.005d,

                // Epic-or-better lands on a median of four packs, so fifteen is a long way past
                // unlucky rather than a schedule — and at roughly a pack a day it means no fortnight
                // ever passes without one.
                EpicPity = 15,

                // 5.5% a pull puts a Legendary at a median of thirteen packs; fifty is the outer
                // bound, and the soft ramp below means most dry runs end before it fires. It is also
                // about seven weeks of daily packs, which is deliberately longer than the median
                // first-set completion — a guarantee should be the floor of the experience, not the
                // shape of it.
                LegendaryPity = 50,
                SoftPityStart = 35,
                SoftPityStep  = 0.015d,
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

        /// <summary>How many cards the catalogue carries at this rarity. A null or short array reads
        /// as none, so a service that has not loaded a catalogue rolls nothing rather than rolling a
        /// card that does not exist.</summary>
        public static int PoolAt(int[] pool, RosterCardState.Rarity rarity)
        {
            int index = (int)rarity;
            if (pool == null || index < 0 || index >= pool.Length) return 0;
            int n = pool[index];
            return n < 0 ? 0 : n;
        }

        /// <summary>Extra Legendary weight earned by a dry run. Zero until the ramp starts.</summary>
        public static double SoftPityBonus(int sinceLegendary, in Tuning t)
        {
            if (t.SoftPityStart <= 0 || t.SoftPityStep <= 0d) return 0d;
            int past = sinceLegendary + 1 - t.SoftPityStart;
            return past <= 0 ? 0d : t.SoftPityStep * past;
        }

        /// <summary>
        /// A rarity's weight for this pack. Zero for a rarity the catalogue carries no card at, which
        /// is what stops the pack rolling a rank it cannot then hand over.
        /// </summary>
        public static double WeightOf(RosterCardState.Rarity rarity, int[] pool,
                                      int sinceLegendary, in Tuning t)
        {
            if (!CardCollection.IsRarity(rarity)) return 0d;
            if (PoolAt(pool, rarity) <= 0) return 0d;

            double w = BaseWeight(rarity, t);
            if (w < 0d) w = 0d;
            if (rarity == RosterCardState.Rarity.Legendary) w += SoftPityBonus(sinceLegendary, t);
            return w;
        }

        /// <summary>
        /// The lowest rarity this pack may come out as. Common normally; higher when a guarantee has
        /// come due.
        ///
        /// Legendary is tested FIRST and wins, which is how two guarantees falling due together are
        /// resolved: a Legendary is also an Epic-or-better, so paying the higher one pays both. The
        /// other order would owe the player a second guarantee they had just been given.
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
        /// <summary>
        /// What rarity a pack comes out as. <paramref name="roll"/> is in [0,1) and is the only
        /// source of chance in the whole feature.
        ///
        /// When every rarity at or above the floor is empty — a guarantee has come due for a rank
        /// nobody carries yet — it falls back DOWN the ladder to the rarest rarity that has cards,
        /// rather than returning one the pack cannot hand over.
        /// </summary>
        public static RosterCardState.Rarity RollRarity(double roll, int[] pool,
                                                        int sinceEpic, int sinceLegendary, in Tuning t)
        {
            roll = Unit(roll);
            int floor = (int)Floor(sinceEpic, sinceLegendary, t);

            double total = 0d;
            for (int r = floor; r < CardCollection.RarityCount; r++)
                total += WeightOf((RosterCardState.Rarity)r, pool, sinceLegendary, t);

            if (total <= 0d) return Rarest(pool);

            double target = roll * total;
            double acc = 0d;
            for (int r = floor; r < CardCollection.RarityCount; r++)
            {
                acc += WeightOf((RosterCardState.Rarity)r, pool, sinceLegendary, t);
                if (target < acc) return (RosterCardState.Rarity)r;
            }
            return Rarest(pool);
        }

        /// <summary>The rarest rarity the catalogue actually carries, or Common for an empty
        /// catalogue. The floor's escape hatch, and never reached in a configured game.</summary>
        public static RosterCardState.Rarity Rarest(int[] pool)
        {
            for (int r = CardCollection.RarityCount - 1; r >= 0; r--)
                if (PoolAt(pool, (RosterCardState.Rarity)r) > 0) return (RosterCardState.Rarity)r;
            return RosterCardState.Rarity.Common;
        }

        /// <summary>
        /// Which card of the rolled rarity, flat among everyone carrying it —
        /// two rolls rather than one weight per card, so authoring a new card changes who you might
        /// get without changing how often a Legendary appears. Returns -1 when nobody carries it.
        /// </summary>
        public static int RollIndexInRarity(double roll, int countAtRarity)
        {
            if (countAtRarity <= 0) return -1;
            int nth = (int)(Unit(roll) * countAtRarity);
            if (nth < 0) return 0;
            return nth >= countAtRarity ? countAtRarity - 1 : nth;
        }

        /// <summary>
        /// Moves the two dry counters on by one pack. A pack at or above a rarity clears that
        /// rarity's counter; anything else lengthens it. Legendary clears both, because a Legendary
        /// is also an Epic-or-better and leaving the short counter running would owe the player a
        /// second guarantee they have just been paid.
        /// </summary>
        public static void Advance(RosterCardState.Rarity got, ref int sinceEpic, ref int sinceLegendary)
        {
            if (got >= RosterCardState.Rarity.Legendary) { sinceEpic = 0; sinceLegendary = 0; return; }
            if (got >= RosterCardState.Rarity.Epic) { sinceEpic = 0; sinceLegendary++; return; }
            sinceEpic++;
            sinceLegendary++;
        }

        // --------------------------------------------------------------------- odds
        /// <summary>
        /// A rarity's share with NOTHING OWED — what the odds sheet prints. Base rather than
        /// realised, because pity bends the numbers and folding it in would publish a percentage that
        /// is true of no particular pack. 0 for a rarity nobody carries, and the sheet is expected to
        /// omit that row rather than print it: a rate being hidden and a rank that does not exist
        /// look identical to a player.
        /// </summary>
        public static double ChanceOf(RosterCardState.Rarity rarity, int[] pool, in Tuning t)
        {
            double mine = WeightOf(rarity, pool, 0, t);
            if (mine <= 0d) return 0d;

            double total = 0d;
            for (int r = 0; r < CardCollection.RarityCount; r++)
                total += WeightOf((RosterCardState.Rarity)r, pool, 0, t);

            return total <= 0d ? 0d : mine / total;
        }

        /// <summary>
        /// A roll clamped into [0,1). NaN fails every comparison, so it survives a clamp written as
        /// two ifs and then casts to an out-of-range int. Catch it by name rather than by luck —
        /// <see cref="MasterChest"/> learned this one the hard way.
        /// </summary>
        private static double Unit(double roll)
        {
            if (double.IsNaN(roll) || roll < 0d) return 0d;
            return roll >= 1d ? 0.9999999999d : roll;
        }
    }
}
