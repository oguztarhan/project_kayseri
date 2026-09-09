using System;

namespace Game.Core
{
    /// <summary>
    /// The card collection as pure maths: what a level costs, what a card is worth, and what a copy
    /// you can no longer use turns into. The sixth of these files after <see cref="IslandEconomy"/>,
    /// <see cref="MarketFlow"/>, <see cref="Foremen"/>, <see cref="Voyages"/> and
    /// <see cref="Captains"/>, and it exists for the same reason as all five: a number the player is
    /// asked to invest months in should be readable in one file rather than discovered by playing.
    ///
    /// WHY A THIRD COLLECTION. Docs/PLAN_05 ruled one out, and the reversal is recorded in
    /// Docs/PLAN_14 rather than here. The short of it: Plan #05's objection was to a bonus you had to
    /// ASSEMBLE — the wrong five cards costing you a multiplier. Nothing here is ever selected. A
    /// card works from the moment it is drawn, owning more is monotonically better, and owning less
    /// is never a mistake. There is no composition to get wrong because there is no composition.
    ///
    /// NO RARITY ENUM OF ITS OWN. <see cref="RosterCardState.Rarity"/> is already the five-rung
    /// ladder every collectible in this project speaks — the captains carry it, the masters map onto
    /// it, <see cref="MiningGear"/> and <see cref="SeaCombat"/> grade their drops on it, and
    /// <c>kaptan.derece.*</c> already prints its five words in eleven languages. A fourth copy of the
    /// same five names would buy nothing and would eventually disagree with the other three.
    ///
    /// A RARER CARD NEEDS FEWER COPIES. This is <see cref="Captains.Tuning.DupScaleCommon"/>'s
    /// inversion, kept for the same measured reason. On one flat curve the three Legendaries would
    /// take about four times as long to max as the nine Commons purely because they drop a third as
    /// often, and an unreachable ceiling makes the whole ladder beneath it read as pointless. What is
    /// different here is that the curve is a TABLE rather than a base-and-scale formula: five
    /// rarities times four rungs is small enough to write out, and a designer moving one rung should
    /// not have to work out what it does to the other nineteen.
    ///
    /// EVERY EFFECT IS CAPPED. Not because the launch values are near a ceiling — they are not — but
    /// because the launch values are the only ones anybody has solved. A second content set added
    /// later compounds into the same aggregate, and <see cref="CapOf"/> is what stops that arriving
    /// as a surprise. <see cref="EffectKind.CraftPointDropChance"/> carries a second, harder clamp
    /// for a different reason: it is a probability rather than a multiplier, and a drop chance that
    /// can reach 1.0 has stopped being a drop chance.
    /// </summary>
    public static class CardCollection
    {
        /// <summary>Level 0 is a card that has never been drawn. There is no level-0 card owned.</summary>
        public const int NotOwned = 0;

        /// <summary>How far a card can be taken. Five, the same ceiling <see cref="Captains.MaxLevel"/>
        /// and <see cref="Foremen.MaxStars"/> use — three rosters with three ladders is three things
        /// to learn.</summary>
        public const int MaxLevel = 5;

        /// <summary>Rungs a card climbs, and therefore the width of one rarity's cost row.</summary>
        public const int LevelSteps = MaxLevel - 1;

        /// <summary>How many rungs <see cref="RosterCardState.Rarity"/> has. Named here so the tables
        /// below can be sized off it without the reader having to go and count the enum.</summary>
        public const int RarityCount = 5;

        /// <summary>
        /// The v1 effect surface, deliberately small and deliberately explicit. Each one names a
        /// number ONE existing system already owns, and the consumer reads the aggregate rather than
        /// ever seeing a card — see Docs/PLAN_14 for which service owns which.
        ///
        /// OfflineIncomeMultiplier was drafted and cut before implementation: GameBootstrap clamps
        /// offline efficiency to 1.0 and a maxed master roster already contributes the whole +1.00 on
        /// its own, so the effect would have been invisible to exactly the players who own a
        /// collection — and would have silently cancelled the offline perk the store SELLS.
        /// </summary>
        public enum EffectKind
        {
            IncomeMultiplier = 0,
            CraftXpMultiplier = 1,
            CraftPointDropChance = 2,
            SeaSalvageMultiplier = 3,
            SeaChartMultiplier = 4,
        }

        public const int EffectKindCount = 5;

        /// <summary>Where a pack came from. Saved only as a statistic and read by analytics; the pack
        /// itself is identical whichever source handed it over, because two odds tables would be two
        /// balance surfaces and one odds sheet that had to apologise for both.</summary>
        public enum PackSource
        {
            Daily = 0,
            GoalMilestone = 1,
            AchievementMilestone = 2,
            EventReserved = 3,
        }

        /// <summary>What a completed set pays, once. Every one of these is a currency some existing
        /// service already owns — nothing here mints a new one.</summary>
        public enum SetRewardKind
        {
            Gems = 0,
            ForemanCards = 1,
            CraftPoints = 2,
            Charts = 3,
            Salvage = 4,
            Pack = 5,
        }

        // ------------------------------------------------------------------ tuning
        /// <summary>
        /// Everything a designer can move. Mirrors the shape of <see cref="Captains.Tuning"/> and
        /// <see cref="Foremen.Tuning"/>; <c>Data/CardCollectionConfig</c> makes it Inspector-editable.
        /// </summary>
        public struct Tuning
        {
            /// <summary>
            /// Copies for every rung of every rarity, flattened: rarity-major, four rungs each, so
            /// index <c>(int)rarity * LevelSteps + (level - 1)</c> is the cost of going from
            /// <c>level</c> to <c>level + 1</c>. Flat rather than jagged because a ScriptableObject
            /// can serialise this and cannot serialise <c>int[][]</c>.
            ///
            /// A null or short array falls back to <see cref="DefaultDuplicateCurve"/> rather than
            /// throwing: a half-filled config asset must degrade to the shipped balance, not to a
            /// crash on the first pack.
            /// </summary>
            public int[] DuplicateCurve;

            /// <summary>
            /// What one level of one card is worth, flattened: effect-major, five rarities each, so
            /// index <c>(int)kind * RarityCount + (int)rarity</c> is the per-level value of a card
            /// of that kind and rarity.
            ///
            /// A card's worth was never per-card — it is a function of what it does and how rare it
            /// is, which is what makes a Legendary worth finding. Twenty-four authored constants
            /// would be twenty-four chances to mistype one and no way to see the shape.
            /// </summary>
            public double[] EffectPerLevel;

            /// <summary>Gems a copy of an already-maxed card turns into, by rarity. Indexed by
            /// <see cref="RosterCardState.Rarity"/>. Overflow is small on purpose — it exists to stop
            /// a draw being worth nothing, not to become a gem faucet.</summary>
            public long[] OverflowGems;

            /// <summary>
            /// The most the whole collection may add to each effect, indexed by
            /// <see cref="EffectKind"/>. For the four multipliers this is a fraction on top of 1.0;
            /// for <see cref="EffectKind.CraftPointDropChance"/> it is absolute probability.
            /// </summary>
            public double[] EffectCaps;

            /// <summary>The most a craft point drop chance may EVER read, collection and base
            /// together. Separate from the cap above because that one bounds this feature's
            /// contribution and this one bounds the number the dice are actually rolled against.</summary>
            public double CraftPointChanceCeiling;

            public static Tuning Default => new Tuning
            {
                DuplicateCurve = (int[])DefaultDuplicateCurve.Clone(),
                EffectPerLevel = (double[])DefaultEffectPerLevel.Clone(),

                // 5 / 10 / 20 / 40 / 75 against a 60-gem master chest. Overflow only starts once a
                // card is maxed — about pack 500 for the first Commons — so this is worth a few
                // hundred gems across the months after that, beside the ~14,400 an all-bought master
                // set costs. A rounding error, deliberately.
                OverflowGems = new[] { 5L, 10L, 20L, 40L, 75L },

                // Income is the smallest because it is the number every other system in the game is
                // already competing to raise; the two sea effects are the largest because salvage and
                // charts are closed loops that never touch the main economy.
                // Every one of these sits ABOVE what the launch catalogue can reach — the fully
                // completed collection is +24% income against a 0.30 cap, +50% craft XP against
                // 0.75, +0.16 drop chance against 0.20, +34% salvage against 0.50 and +28% charts
                // against 0.40. A cap that clipped the shipped values would be the thing doing the
                // balancing, which is the one job a cap must not have: it is here to bound what a
                // CONTENT SET ADDED LATER can compound into, and CardCollectionTests asserts the
                // headroom so nobody can quietly close it.
                EffectCaps = new[]
                {
                    0.30d,   // IncomeMultiplier      — +30% on top of foreman 3.0x and gear 1.8x
                    0.75d,   // CraftXpMultiplier     — the bench's real brake is its wall-clock gates
                    0.20d,   // CraftPointDropChance  — absolute, added to the 0.20 base
                    0.50d,   // SeaSalvageMultiplier  — closed loop: sailing pays for sailing
                    0.40d,   // SeaChartMultiplier    — closed loop: sailing pays for crates
                },

                CraftPointChanceCeiling = 0.40d,
            };
        }

        /// <summary>
        /// The shipped curve, rarity-major. Reading down a column shows the rungs getting dearer;
        /// reading across a row shows a rarer card asking for less at every rung.
        ///
        ///   Common    2  4  8 14   = 28 copies to max
        ///   Rare      2  4  7 12   = 25
        ///   Epic      1  3  5  9   = 18
        ///   Legendary 1  2  4  7   = 14
        ///   Mythic    1  2  3  5   = 11   (authored for a future card; nothing carries it at launch)
        ///
        /// Measured against the pack odds in <see cref="CardCollectionPack"/>, these land the four
        /// launch rarities within about 55% of one another on time-to-max — roughly 500 packs for a
        /// Common and 780 for an Epic — while keeping Commons the fastest thing on the board.
        /// </summary>
        public static readonly int[] DefaultDuplicateCurve =
        {
            2, 4, 8, 14,
            2, 4, 7, 12,
            1, 3, 5,  9,
            1, 2, 4,  7,
            1, 2, 3,  5,
        };

        /// <summary>
        /// What one level is worth, effect-major and rarity-ascending. Reading across a row shows a
        /// rarer card being worth more of the same thing; reading down a column shows the five
        /// effects on their own scales, because they are.
        ///
        ///                        Common   Rare    Epic    Leg    Mythic
        ///   IncomeMultiplier      .002    .004    .006    .010    .016
        ///   CraftXpMultiplier     .010    .020    .030    .045    .065
        ///   CraftPointDropChance  .002    .004    .006    .010    .015
        ///   SeaSalvageMultiplier  .006    .010    .016    .024    .036
        ///   SeaChartMultiplier    .006    .010    .016    .024    .036
        ///
        /// INCOME IS THE SMALLEST because it is the number every other system in the game already
        /// competes to raise: the launch catalogue's seven income cards are worth +16% maxed, against
        /// an existing foreman 3.0x times mining-gear 1.8x. THE TWO SEA EFFECTS ARE THE LARGEST
        /// because salvage and charts are closed loops — sailing pays for sailing, and for crates —
        /// so a generous number there never reaches the economy the islands run on.
        ///
        /// The Legendary and Mythic cells of the craft-XP row and the Mythic cells generally are
        /// authored for cards that do not exist yet. They cost nothing to carry and stop the next
        /// content set inventing its own scale.
        /// </summary>
        public static readonly double[] DefaultEffectPerLevel =
        {
            0.002d, 0.004d, 0.006d, 0.010d, 0.016d,   // IncomeMultiplier
            0.010d, 0.020d, 0.030d, 0.045d, 0.065d,   // CraftXpMultiplier
            0.002d, 0.004d, 0.006d, 0.010d, 0.015d,   // CraftPointDropChance
            0.006d, 0.010d, 0.016d, 0.024d, 0.036d,   // SeaSalvageMultiplier
            0.006d, 0.010d, 0.016d, 0.024d, 0.036d,   // SeaChartMultiplier
        };

        /// <summary>
        /// What one level of a card of this kind and rarity is worth. Falls back to the shipped
        /// table for a config asset saved half-filled, on the same rule the duplicate curve keeps: a
        /// mis-set Inspector field must cost the player balance, never a session.
        /// </summary>
        public static double PerLevel(EffectKind kind, RosterCardState.Rarity rarity, in Tuning t)
        {
            if (!IsEffectKind(kind) || !IsRarity(rarity)) return 0d;

            int index = (int)kind * RarityCount + (int)rarity;
            if (index < 0 || index >= DefaultEffectPerLevel.Length) return 0d;

            double[] table = t.EffectPerLevel;
            double value = table == null || index >= table.Length
                ? DefaultEffectPerLevel[index]
                : table[index];

            return value < 0d ? 0d : value;
        }

        // -------------------------------------------------------------------- read
        public static bool IsRarity(RosterCardState.Rarity rarity)
            => (int)rarity >= 0 && (int)rarity < RarityCount;

        public static bool IsEffectKind(EffectKind kind)
            => (int)kind >= 0 && (int)kind < EffectKindCount;

        /// <summary>True for the four effects that read as a fraction on top of 1.0. The odd one out
        /// is <see cref="EffectKind.CraftPointDropChance"/>, which is absolute probability — the UI
        /// has to know which sentence to print, and so does every test.</summary>
        public static bool IsMultiplier(EffectKind kind) => kind != EffectKind.CraftPointDropChance;

        // ------------------------------------------------------------------- price
        /// <summary>
        /// Copies to take a card from <paramref name="level"/> to the next one. 0 for a card nobody
        /// owns and 0 at the ceiling — those are not upgrades that cost nothing, they are upgrades
        /// that do not exist, and the caller must tell the two apart by asking the level.
        ///
        /// Never less than one for a real rung: a level that costs nothing is not a level.
        /// </summary>
        public static int DuplicatesToLevel(RosterCardState.Rarity rarity, int level, in Tuning t)
        {
            if (!IsRarity(rarity) || level < 1 || level >= MaxLevel) return 0;
            int n = CurveAt((int)rarity * LevelSteps + (level - 1), t);
            return n < 1 ? 1 : n;
        }

        /// <summary>Every copy a card of this rarity will ever need, for a progress readout that does
        /// not lie about how long the road is.</summary>
        public static int DuplicatesToMax(RosterCardState.Rarity rarity, in Tuning t)
        {
            if (!IsRarity(rarity)) return 0;
            int total = 0;
            for (int level = 1; level < MaxLevel; level++) total += DuplicatesToLevel(rarity, level, t);
            return total;
        }

        /// <summary>Copies still owed from here to the ceiling. What the card detail sheet prints
        /// under a card nobody has finished.</summary>
        public static int DuplicatesFrom(RosterCardState.Rarity rarity, int level, in Tuning t)
        {
            if (!IsRarity(rarity) || level < 1) return DuplicatesToMax(rarity, t);
            int total = 0;
            for (int l = level; l < MaxLevel; l++) total += DuplicatesToLevel(rarity, l, t);
            return total;
        }

        /// <summary>One cell of the curve, falling back to the shipped table for a config asset that
        /// was saved half-filled. Clamped rather than thrown for the reason every table in this
        /// project is: a mis-set Inspector field must cost the player balance, never a session.</summary>
        private static int CurveAt(int index, in Tuning t)
        {
            if (index < 0 || index >= DefaultDuplicateCurve.Length) return 1;
            int[] curve = t.DuplicateCurve;
            if (curve == null || index >= curve.Length) return DefaultDuplicateCurve[index];
            return curve[index];
        }

        // ------------------------------------------------------------------ worth
        /// <summary>
        /// What one card is worth at <paramref name="level"/>: its authored per-level value times the
        /// level it has reached. Zero for a card nobody owns, so an empty collection changes nothing
        /// anywhere and the whole feature can be switched off by shipping an empty catalogue.
        ///
        /// Linear on purpose, the same choice <see cref="Foremen"/> made for its stars: "three of
        /// five levels" then reads as three fifths of the card rather than as a curve to look up.
        /// </summary>
        public static double CardEffect(double perLevel, int level)
        {
            if (level <= NotOwned || perLevel <= 0d) return 0d;
            if (level > MaxLevel) level = MaxLevel;
            return perLevel * level;
        }

        /// <summary>The most the collection may contribute to one effect. A kind with no cap
        /// configured falls back to the shipped one rather than to infinity.</summary>
        public static double CapOf(EffectKind kind, in Tuning t)
        {
            if (!IsEffectKind(kind)) return 0d;
            double[] caps = t.EffectCaps;
            if (caps == null || (int)kind >= caps.Length)
                return Tuning.Default.EffectCaps[(int)kind];
            double cap = caps[(int)kind];
            return cap < 0d ? 0d : cap;
        }

        /// <summary>
        /// An aggregate held to its cap. The service calls this ONCE per effect kind as it builds the
        /// snapshot, so no consumer ever has to remember that a cap exists — which is the only way a
        /// cap survives the next person to add a content set.
        /// </summary>
        public static double Capped(EffectKind kind, double total, in Tuning t)
        {
            if (double.IsNaN(total) || total <= 0d) return 0d;
            double cap = CapOf(kind, t);
            return total > cap ? cap : total;
        }

        /// <summary>
        /// The craft point drop chance the dice are actually rolled against, base and collection
        /// together, held under <see cref="Tuning.CraftPointChanceCeiling"/>.
        ///
        /// It lives here rather than in the workshop so there is exactly one place the ceiling is
        /// applied. A probability is the one effect on the surface that can be pushed to certainty,
        /// and a drop that always drops is not a drop.
        /// </summary>
        public static double CraftPointChance(double baseChance, double collectionBonus, in Tuning t)
        {
            if (double.IsNaN(baseChance) || baseChance < 0d) baseChance = 0d;
            if (double.IsNaN(collectionBonus) || collectionBonus < 0d) collectionBonus = 0d;

            double ceiling = t.CraftPointChanceCeiling;
            if (ceiling <= 0d) ceiling = Tuning.Default.CraftPointChanceCeiling;

            double chance = baseChance + Capped(EffectKind.CraftPointDropChance, collectionBonus, t);
            if (chance > ceiling) chance = ceiling;
            return chance > 1d ? 1d : chance;
        }

        /// <summary>What a multiplier effect reads as on the number it multiplies. The four
        /// multiplier kinds enter their consumer as this; the drop chance does not, which is what
        /// <see cref="IsMultiplier"/> is for.</summary>
        public static double AsMultiplier(double total) => total <= 0d ? 1d : 1d + total;

        /// <summary>
        /// A counted reward with a multiplier applied — craft XP, salvage, charts.
        ///
        /// One rounding rule for every whole-number reward the collection touches, because the
        /// alternative is three consumers each picking their own and a player discovering that the
        /// same +6% is worth something on charts and nothing on salvage. Away-from-zero is what the
        /// rest of the project rounds a scaled reward with — see <c>Foremen.CardsFor</c> and
        /// <c>SeaCombat.SalvageFor</c>.
        ///
        /// A multiplier at or below 1 hands the amount back untouched rather than shaving it: these
        /// are bonuses, and a bonus must never be able to subtract.
        /// </summary>
        public static long Scale(long amount, double multiplier)
        {
            if (amount <= 0L) return amount;
            if (double.IsNaN(multiplier) || multiplier <= 1d) return amount;
            double scaled = Math.Round(amount * multiplier, MidpointRounding.AwayFromZero);
            if (double.IsInfinity(scaled) || scaled >= long.MaxValue) return long.MaxValue;
            long paid = (long)scaled;
            return paid < amount ? amount : paid;
        }

        // --------------------------------------------------------------- overflow
        /// <summary>
        /// Gems a copy of an already-maxed card turns into. The deliberate answer to "what happens to
        /// a draw you cannot use": leaving it visible and inert is the one option that silently
        /// discards value, and taking maxed cards out of the roll pool is worse still — it would make
        /// the published odds untrue for exactly the players who have read them.
        /// </summary>
        public static long OverflowGems(RosterCardState.Rarity rarity, in Tuning t)
        {
            if (!IsRarity(rarity)) return 0L;
            long[] gems = t.OverflowGems;
            if (gems == null || (int)rarity >= gems.Length)
                return Tuning.Default.OverflowGems[(int)rarity];
            long n = gems[(int)rarity];
            return n < 0L ? 0L : n;
        }

        // ------------------------------------------------------------------ state
        /// <summary>A level clamped to something a card can actually be at.</summary>
        public static int ClampLevel(int level)
        {
            if (level < NotOwned) return NotOwned;
            return level > MaxLevel ? MaxLevel : level;
        }

        public static bool IsOwned(int level) => ClampLevel(level) > NotOwned;

        public static bool IsMaxed(int level) => ClampLevel(level) >= MaxLevel;

        /// <summary>
        /// True when this card can be levelled right now. Owned, below the ceiling, and holding
        /// enough copies — all three, because the UI is not allowed to work any of them out for
        /// itself and a save arriving with duplicates on an unowned card must not unlock it.
        /// </summary>
        public static bool CanLevel(RosterCardState.Rarity rarity, int level, int duplicates, in Tuning t)
        {
            if (!IsOwned(level) || IsMaxed(level)) return false;
            int need = DuplicatesToLevel(rarity, level, t);
            return need > 0 && duplicates >= need;
        }
    }
}
