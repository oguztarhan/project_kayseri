using System;

namespace Game.Core
{
    /// <summary>
    /// The pet roster as pure maths: who exists, what one is worth at a given rarity and star, and
    /// what three copies turn into. The seventh of these files after <see cref="Captains"/> and
    /// <see cref="CardCollection"/>, and it borrows a rule from each rather than inventing a third
    /// shape for the same five rarities.
    ///
    /// NO RARITY ENUM OF ITS OWN, for the reason <see cref="CardCollection"/> gives:
    /// <see cref="RosterCardState.Rarity"/> is already the five-rung ladder every collectible in this
    /// project speaks. A pet's rarity is a captain's grade is a card's tier.
    ///
    /// STARS ARE NOT LEVELS. <see cref="CardCollection"/> banks duplicates and spends them to raise a
    /// level while the card itself stays one copy forever; a pet instead FUSES — three copies at one
    /// rung become one copy at the next, and the two consumed are gone. That is <see cref="TryFuse"/>,
    /// and it is the one idea in this file CardCollection's leveling curve does not already cover.
    ///
    /// SIX SPECIES, SIX STATS, NO OVERLAP. Every species on <see cref="Roster"/> owns exactly one
    /// <see cref="EffectKind"/> and no two share one, so equipping three pets can never double up on
    /// the same secondary — the aggregate cap a second content set would need is already enforced by
    /// there being nowhere for a second contributor to come from. If a future species ever repeats a
    /// kind, <see cref="Pets"/> itself still only emits one number per kind per pet; the ceiling would
    /// then have to move into <see cref="ApplyCombatBonus"/>, which is exactly why that method takes
    /// an aggregate per kind rather than a list of pets.
    ///
    /// THE CEILING IS THE TABLE, NOT A SEPARATE CAP. Each species' Mythic-5★ value was authored to sit
    /// at its intended ceiling directly (see <see cref="DefaultEffectPerRarity"/>), so there is no
    /// second "most a pet may add" constant to keep in sync with the first. What DOES still clamp is
    /// <see cref="SeaCombat"/>'s own absolute caps (DodgeCap, StunCap, SalvoCap, StealCap) — gear,
    /// captain and pet all add into the same number, and <see cref="ApplyCombatBonus"/> is the one
    /// place a pet's share of that number is held to them.
    /// </summary>
    public static class Pets
    {
        /// <summary>How many rungs <see cref="RosterCardState.Rarity"/> has. Named here so the tables
        /// below can be sized off it without the reader having to go and count the enum — the same
        /// constant <see cref="CardCollection.RarityCount"/> carries for the same reason.</summary>
        public const int RarityCount = 5;

        /// <summary>The top of a pet's star ladder. One rarity's worth of fusion headroom before the
        /// next three copies jump it to the next rarity instead.</summary>
        public const int MaxStars = 5;

        /// <summary>Copies one fusion consumes. Three in, one out, at every rung — the single rule the
        /// whole ladder runs on.</summary>
        public const int FuseGroupSize = 3;

        /// <summary>Equip slots the pet system has at all, once every one is unlocked. How many of
        /// them the player can actually use right now is progression — see
        /// <c>PetService.SlotsUnlocked</c> — and is deliberately not a fact this file knows.</summary>
        public const int SlotCount = 3;

        /// <summary>
        /// The v1 effect surface: one secondary stat per species, each one a number
        /// <see cref="SeaCombat"/> already owns. A pet never invents a new stat — it only ever adds to
        /// one the ship already has, which is what keeps "what does this pet do" answerable from the
        /// fight screen alone.
        /// </summary>
        public enum EffectKind
        {
            Dodge = 0,
            Salvo = 1,
            Stun = 2,
            Steal = 3,
            Def = 4,
            Hull = 5,
        }

        public const int EffectKindCount = 6;

        /// <summary>One entry on the roster: a save-stable id (also its loc key) and the single stat
        /// it feeds.</summary>
        public struct Species
        {
            public string Id;
            public EffectKind Effect;
        }

        /// <summary>
        /// The launch six, ocean/pirate themed and each carrying a stat nobody else on the roster
        /// touches. Order is the save's index — appending is safe, reordering is not, the same rule
        /// <see cref="Captains.Roster"/> keeps.
        /// </summary>
        public static readonly Species[] Roster =
        {
            new Species { Id = "papagan",     Effect = EffectKind.Dodge },  // parrot — spots the broadside coming
            new Species { Id = "maymun",      Effect = EffectKind.Salvo },  // monkey — quick hands on the second gun
            new Species { Id = "ahtapot",     Effect = EffectKind.Stun  },  // octopus — tangles their wheel
            new Species { Id = "gemi_faresi", Effect = EffectKind.Steal },  // ship's rat — into their hold mid-fight
            new Species { Id = "yengec",      Effect = EffectKind.Def   },  // crab — armoured, sits on the hull
            new Species { Id = "kaplumbaga",  Effect = EffectKind.Hull  },  // turtle — slow, and carries more of it
        };

        public const int SpeciesCount = 6;

        // ------------------------------------------------------------------ tuning
        /// <summary>
        /// Everything a designer can move. Mirrors the shape of <see cref="CardCollection.Tuning"/>;
        /// <c>Data/PetConfig</c> makes it Inspector-editable.
        /// </summary>
        public struct Tuning
        {
            /// <summary>
            /// What one star of one species is worth, flattened: effect-major, five rarities each, so
            /// index <c>(int)kind * RarityCount + (int)rarity</c> is the per-star value of a pet of
            /// that kind and rarity. Indexed by <see cref="EffectKind"/> rather than by species so a
            /// sixth Dodge-pet, if one is ever authored, reads the same row the parrot does.
            /// </summary>
            public double[] EffectPerRarity;

            public static Tuning Default => new Tuning
            {
                EffectPerRarity = (double[])DefaultEffectPerRarity.Clone(),
            };
        }

        /// <summary>
        /// What one star is worth, effect-major and rarity-ascending. Reading across a row shows a
        /// rarer pet being worth more of the same thing at the same star; reading down a column shows
        /// the six effects on their own scales.
        ///
        ///                Common    Rare     Epic     Leg      Mythic
        ///   Dodge/Salvo   .0011    .0023    .0034    .0056    .0090
        ///   Stun/Steal    .0009    .0018    .0026    .0044    .0070
        ///   Def           .20      .40      .60      1.00     1.60
        ///   Hull          1.0      2.0      3.0      5.0      8.0
        ///
        /// Each row is the Mythic★5 ceiling (the ONLY ceiling — see the class note) split across the
        /// rarities on a 1:2:3:5:8 shape and across the five stars by simple multiplication, so a
        /// Rare★5 pet and a fresh Epic★1 pet are close but never equal: Rare's ceiling is 5×.0023=
        /// .0115, Epic's floor is .0034 — the rarer pet is never behind the one beneath it at any
        /// star, which is what makes fusing up always strictly better.
        ///
        /// DODGE AND SALVO SHARE A ROW, and so do STUN AND STEAL: all four are secondary-stat
        /// percentages already capped at the sea's own ceiling (0.50/0.50/0.40/0.40 — see
        /// <see cref="SeaCombat.DodgeCap"/> and neighbours), so one pet at Mythic★5 (+0.045 at its
        /// cap's widest) is a meaningful slice without being able to fill the whole bar by itself.
        /// DEF AND HULL ARE FLAT, uncapped by the sea, so their Mythic★5 ceiling is pinned instead to
        /// "about what one Rare-grade gear roll of the matching slot is worth" — a pet is a second
        /// source of the same kind of number gear already gives, not a bigger one.
        /// </summary>
        public static readonly double[] DefaultEffectPerRarity =
        {
            0.0011d, 0.0023d, 0.0034d, 0.0056d, 0.0090d,   // Dodge (papağan)
            0.0011d, 0.0023d, 0.0034d, 0.0056d, 0.0090d,   // Salvo (maymun)
            0.0009d, 0.0018d, 0.0026d, 0.0044d, 0.0070d,   // Stun (ahtapot)
            0.0009d, 0.0018d, 0.0026d, 0.0044d, 0.0070d,   // Steal (gemi faresi)
            0.2000d, 0.4000d, 0.6000d, 1.0000d, 1.6000d,   // Def (yengeç)
            1.0000d, 2.0000d, 3.0000d, 5.0000d, 8.0000d,   // Hull (kaplumbağa)
        };

        // -------------------------------------------------------------------- read
        public static bool Exists(int species) => species >= 0 && species < Roster.Length;

        public static bool IsRarity(RosterCardState.Rarity rarity)
            => (int)rarity >= 0 && (int)rarity < RarityCount;

        public static bool IsEffectKind(EffectKind kind)
            => (int)kind >= 0 && (int)kind < EffectKindCount;

        public static EffectKind EffectKindOf(int species)
            => Exists(species) ? Roster[species].Effect : EffectKind.Dodge;

        public static string IdOf(int species) => Exists(species) ? Roster[species].Id : string.Empty;

        // ------------------------------------------------------------------- worth
        /// <summary>
        /// What one star of this kind and rarity is worth. Falls back to the shipped table for a
        /// config asset saved half-filled, the same degrade-not-crash rule every tuning table in this
        /// project keeps.
        /// </summary>
        public static double PerRarity(EffectKind kind, RosterCardState.Rarity rarity, in Tuning t)
        {
            if (!IsEffectKind(kind) || !IsRarity(rarity)) return 0d;

            int index = (int)kind * RarityCount + (int)rarity;
            if (index < 0 || index >= DefaultEffectPerRarity.Length) return 0d;

            double[] table = t.EffectPerRarity;
            double value = table == null || index >= table.Length
                ? DefaultEffectPerRarity[index]
                : table[index];

            return value < 0d ? 0d : value;
        }

        /// <summary>A pet's whole contribution right now: per-star value times the star it has
        /// reached. Zero below star 1, so an unowned or unfused cell changes nothing.</summary>
        public static double Bonus(EffectKind kind, RosterCardState.Rarity rarity, int star, in Tuning t)
        {
            if (star <= 0) return 0d;
            if (star > MaxStars) star = MaxStars;
            return PerRarity(kind, rarity, t) * star;
        }

        // ------------------------------------------------------------------ fusion
        /// <summary>True once a pet can climb no further — Mythic at its top star.</summary>
        public static bool IsMaxed(RosterCardState.Rarity rarity, int star)
            => rarity >= RosterCardState.Rarity.Mythic && star >= MaxStars;

        /// <summary>
        /// What three copies at (<paramref name="rarity"/>, <paramref name="star"/>) become. Below the
        /// star ceiling, the same rarity one star up. At the ceiling, the next rarity at star 1 — the
        /// Rare★5 → Epic★1 jump the class doc opens with. False at Mythic★5 or for an invalid cell:
        /// there is nothing past the top of the ladder, and fusing there would silently eat the three
        /// copies for nothing.
        /// </summary>
        public static bool TryFuse(RosterCardState.Rarity rarity, int star,
                                   out RosterCardState.Rarity resultRarity, out int resultStar)
        {
            resultRarity = rarity;
            resultStar = star;
            if (!IsRarity(rarity) || star < 1 || star > MaxStars) return false;
            if (IsMaxed(rarity, star)) return false;

            if (star < MaxStars)
            {
                resultStar = star + 1;
                return true;
            }

            resultRarity = rarity + 1;
            resultStar = 1;
            return true;
        }

        // ------------------------------------------------------------- owned grid
        /// <summary>One flattened counts array's whole length: species-major, rarity next, star last.
        /// Sized for a fixed roster against a fixed ladder, so the save can carry it as one plain
        /// array the way <c>SaveData.masterStars</c> carries the master roster.</summary>
        public static int CountsLength => SpeciesCount * RarityCount * MaxStars;

        /// <summary>One cell's index into a counts array of <see cref="CountsLength"/>. -1 for
        /// anything outside the grid, so a caller can use it as both a lookup and a bounds check.</summary>
        public static int CellIndex(int species, RosterCardState.Rarity rarity, int star)
        {
            if (!Exists(species) || !IsRarity(rarity) || star < 1 || star > MaxStars) return -1;
            return ((species * RarityCount) + (int)rarity) * MaxStars + (star - 1);
        }

        /// <summary>Copies owned at one cell. 0 for an out-of-range cell or a short/missing array,
        /// never thrown — a half-grown counts array is a player who owns nothing yet at the cells it
        /// has not reached, not a corrupt save.</summary>
        public static int CountAt(int[] counts, int species, RosterCardState.Rarity rarity, int star)
        {
            if (counts == null) return 0;
            int index = CellIndex(species, rarity, star);
            if (index < 0 || index >= counts.Length) return 0;
            int n = counts[index];
            return n < 0 ? 0 : n;
        }

        /// <summary>
        /// The best cell a species is owned at right now — rarest first, then highest star — or false
        /// for a species nobody has drawn. What the collection screen shows by default and what
        /// equipping "the parrot" without naming a rarity means.
        /// </summary>
        public static bool TryBestOwned(int[] counts, int species,
                                        out RosterCardState.Rarity rarity, out int star)
        {
            rarity = RosterCardState.Rarity.Common;
            star = 0;
            if (!Exists(species)) return false;

            for (int r = RarityCount - 1; r >= 0; r--)
            {
                for (int s = MaxStars; s >= 1; s--)
                {
                    if (CountAt(counts, species, (RosterCardState.Rarity)r, s) > 0)
                    {
                        rarity = (RosterCardState.Rarity)r;
                        star = s;
                        return true;
                    }
                }
            }
            return false;
        }

        // ---------------------------------------------------------------- combat
        /// <summary>
        /// Gear's and the captain's stats, plus whatever the equipped pets add — held to
        /// <see cref="SeaCombat"/>'s own absolute caps exactly as gear already is. One call, after
        /// <see cref="SeaCombat.OurStats"/> and nowhere else, so a pet's secondary is never added
        /// twice and never skips the cap gear would have to respect.
        ///
        /// <paramref name="bonusByEffectKind"/> is indexed by <see cref="EffectKind"/> and is expected
        /// to already be the SUM of every equipped pet's <see cref="Bonus"/> for that kind — summing
        /// is the caller's job because only the caller (the save-backed service) knows who is
        /// equipped. A null array changes nothing.
        /// </summary>
        public static SeaCombat.Stats ApplyCombatBonus(in SeaCombat.Stats baseStats, double[] bonusByEffectKind)
        {
            SeaCombat.Stats s = baseStats;
            if (bonusByEffectKind == null) return s;

            s.Dodge = ClampAdd(s.Dodge, BonusAt(bonusByEffectKind, EffectKind.Dodge), SeaCombat.DodgeCap);
            s.Salvo = ClampAdd(s.Salvo, BonusAt(bonusByEffectKind, EffectKind.Salvo), SeaCombat.SalvoCap);
            s.Stun = ClampAdd(s.Stun, BonusAt(bonusByEffectKind, EffectKind.Stun), SeaCombat.StunCap);
            s.Steal = ClampAdd(s.Steal, BonusAt(bonusByEffectKind, EffectKind.Steal), SeaCombat.StealCap);
            s.Def += BonusAt(bonusByEffectKind, EffectKind.Def);
            s.Hull += BonusAt(bonusByEffectKind, EffectKind.Hull);
            return s;
        }

        private static double BonusAt(double[] arr, EffectKind kind)
        {
            int i = (int)kind;
            if (arr == null || i < 0 || i >= arr.Length) return 0d;
            double v = arr[i];
            return v > 0d ? v : 0d;
        }

        private static double ClampAdd(double current, double add, double cap)
        {
            if (add <= 0d) return current;
            double sum = current + add;
            return sum > cap ? cap : sum;
        }
    }
}
