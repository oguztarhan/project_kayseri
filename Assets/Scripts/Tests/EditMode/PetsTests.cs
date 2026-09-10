using NUnit.Framework;
using Game.Core;

namespace Game.Tests
{
    /// <summary>
    /// The pet roster's maths: the table, the fusion ladder and the one place a pet's secondary stat
    /// is ever added to a fight. <see cref="Pets"/> takes no save and touches no random generator, so
    /// every test here is exact.
    /// </summary>
    public class PetsTests
    {
        private static Pets.Tuning T => Pets.Tuning.Default;

        // ---- the roster --------------------------------------------------------------------------

        [Test]
        public void RosterLengthMatchesSpeciesCount()
        {
            Assert.That(Pets.Roster.Length, Is.EqualTo(Pets.SpeciesCount));
        }

        [Test]
        public void EveryIdIsPresentLowercaseAndUnique()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < Pets.SpeciesCount; i++)
            {
                string id = Pets.IdOf(i);
                Assert.That(id, Is.Not.Empty, "species " + i);
                Assert.That(id, Is.EqualTo(id.ToLowerInvariant()), "id is also a loc key: " + id);
                Assert.That(seen.Add(id), Is.True, "duplicate id " + id);
            }
        }

        [Test]
        public void NoTwoSpeciesShareAnEffectKind()
        {
            // The whole reason three equipped pets can never double up on one stat: there is
            // nowhere for a second contributor of the same kind to come from.
            var seen = new System.Collections.Generic.HashSet<Pets.EffectKind>();
            for (int i = 0; i < Pets.SpeciesCount; i++)
                Assert.That(seen.Add(Pets.EffectKindOf(i)), Is.True,
                            "species " + Pets.IdOf(i) + " repeats an effect kind");
        }

        [Test]
        public void ExistsIsTrueOnlyInsideTheRoster()
        {
            Assert.That(Pets.Exists(-1), Is.False);
            Assert.That(Pets.Exists(0), Is.True);
            Assert.That(Pets.Exists(Pets.SpeciesCount - 1), Is.True);
            Assert.That(Pets.Exists(Pets.SpeciesCount), Is.False);
        }

        // ---- the table ----------------------------------------------------------------------------

        [Test]
        public void DefaultTableIsExactlyEffectKindsTimesRarities()
        {
            Assert.That(Pets.DefaultEffectPerRarity.Length,
                        Is.EqualTo(Pets.EffectKindCount * Pets.RarityCount));
        }

        [Test]
        public void EveryCellIsPositive()
        {
            foreach (double v in Pets.DefaultEffectPerRarity) Assert.That(v, Is.GreaterThan(0d));
        }

        [Test]
        public void ARarerPetIsNeverWorseAtAnyStar()
        {
            // Checked per star rather than only at the ceiling: a rarer pet must not fall behind a
            // commoner one at ANY point on the ladder, or fusing up would sometimes be a downgrade.
            for (int kind = 0; kind < Pets.EffectKindCount; kind++)
            {
                var k = (Pets.EffectKind)kind;
                for (int r = 1; r < Pets.RarityCount; r++)
                {
                    double lower = Pets.PerRarity(k, (RosterCardState.Rarity)(r - 1), T);
                    double higher = Pets.PerRarity(k, (RosterCardState.Rarity)r, T);
                    Assert.That(higher, Is.GreaterThan(lower), k + " rarity " + r);
                }
            }
        }

        [Test]
        public void BonusScalesLinearlyWithStarAndIsZeroBelowOne()
        {
            Assert.That(Pets.Bonus(Pets.EffectKind.Dodge, RosterCardState.Rarity.Common, 0, T), Is.Zero);
            Assert.That(Pets.Bonus(Pets.EffectKind.Dodge, RosterCardState.Rarity.Common, -3, T), Is.Zero);

            double one = Pets.Bonus(Pets.EffectKind.Dodge, RosterCardState.Rarity.Rare, 1, T);
            double three = Pets.Bonus(Pets.EffectKind.Dodge, RosterCardState.Rarity.Rare, 3, T);
            Assert.That(three, Is.EqualTo(one * 3d).Within(1e-12));

            // A star above the ceiling reads as the ceiling rather than extrapolating past it.
            double five = Pets.Bonus(Pets.EffectKind.Dodge, RosterCardState.Rarity.Rare, 5, T);
            double beyond = Pets.Bonus(Pets.EffectKind.Dodge, RosterCardState.Rarity.Rare, 99, T);
            Assert.That(beyond, Is.EqualTo(five));
        }

        [Test]
        public void AMissingConfigArrayFallsBackToTheShippedTable()
        {
            var empty = new Pets.Tuning { EffectPerRarity = null };
            Assert.That(Pets.PerRarity(Pets.EffectKind.Hull, RosterCardState.Rarity.Mythic, empty),
                        Is.EqualTo(8.0d));
        }

        [Test]
        public void AnOutOfRangeKindOrRarityIsZeroRatherThanThrown()
        {
            Assert.DoesNotThrow(() => Pets.PerRarity((Pets.EffectKind)99, RosterCardState.Rarity.Common, T));
            Assert.That(Pets.PerRarity((Pets.EffectKind)99, RosterCardState.Rarity.Common, T), Is.Zero);
            Assert.That(Pets.PerRarity(Pets.EffectKind.Dodge, (RosterCardState.Rarity)99, T), Is.Zero);
        }

        // ---- fusion ---------------------------------------------------------------------------------

        [Test]
        public void ThreeCommonsBecomeOneTwoStarCommon()
        {
            bool ok = Pets.TryFuse(RosterCardState.Rarity.Common, 1, out var r, out int s);
            Assert.That(ok, Is.True);
            Assert.That(r, Is.EqualTo(RosterCardState.Rarity.Common));
            Assert.That(s, Is.EqualTo(2));
        }

        [Test]
        public void ThreeFiveStarRaresBecomeOneOneStarEpic()
        {
            // The exact example the feature was specified with: Rare+Rare+Rare -> 2★ Rare is the
            // first rung above; this is the rung where the rarity itself turns over.
            bool ok = Pets.TryFuse(RosterCardState.Rarity.Rare, Pets.MaxStars, out var r, out int s);
            Assert.That(ok, Is.True);
            Assert.That(r, Is.EqualTo(RosterCardState.Rarity.Epic));
            Assert.That(s, Is.EqualTo(1));
        }

        [Test]
        public void TheWholeLadderClimbsWithoutEverSkippingOrRepeatingARung()
        {
            var rarity = RosterCardState.Rarity.Common;
            int star = 1;
            var seen = new System.Collections.Generic.HashSet<(RosterCardState.Rarity, int)>();
            seen.Add((rarity, star));

            int steps = 0;
            while (Pets.TryFuse(rarity, star, out var nextRarity, out int nextStar))
            {
                Assert.That(seen.Add((nextRarity, nextStar)), Is.True, "repeated a rung");
                rarity = nextRarity;
                star = nextStar;
                steps++;
                Assert.That(steps, Is.LessThan(100), "fusion never terminates");
            }

            Assert.That(rarity, Is.EqualTo(RosterCardState.Rarity.Mythic));
            Assert.That(star, Is.EqualTo(Pets.MaxStars));
            // Every (rarity, star) pair is one cell on a straight ladder of RarityCount * MaxStars
            // cells; climbing from the first to the last is exactly that many steps minus one.
            Assert.That(steps, Is.EqualTo(Pets.RarityCount * Pets.MaxStars - 1));
        }

        [Test]
        public void MythicFiveStarCannotFuseFurther()
        {
            Assert.That(Pets.IsMaxed(RosterCardState.Rarity.Mythic, Pets.MaxStars), Is.True);
            bool ok = Pets.TryFuse(RosterCardState.Rarity.Mythic, Pets.MaxStars, out var r, out int s);
            Assert.That(ok, Is.False);
            // Out params still name the cell itself rather than garbage, so a caller that forgets to
            // check the bool is left looking at the truth, not at noise.
            Assert.That(r, Is.EqualTo(RosterCardState.Rarity.Mythic));
            Assert.That(s, Is.EqualTo(Pets.MaxStars));
        }

        [Test]
        public void AnInvalidCellCannotFuse()
        {
            Assert.That(Pets.TryFuse(RosterCardState.Rarity.Common, 0, out _, out _), Is.False);
            Assert.That(Pets.TryFuse(RosterCardState.Rarity.Common, 6, out _, out _), Is.False);
            Assert.That(Pets.TryFuse((RosterCardState.Rarity)99, 1, out _, out _), Is.False);
        }

        // ---- the owned grid -------------------------------------------------------------------------

        [Test]
        public void CellIndexIsUniqueForEveryRealCellAndMinusOneOtherwise()
        {
            var seen = new System.Collections.Generic.HashSet<int>();
            for (int sp = 0; sp < Pets.SpeciesCount; sp++)
                for (int r = 0; r < Pets.RarityCount; r++)
                    for (int s = 1; s <= Pets.MaxStars; s++)
                    {
                        int idx = Pets.CellIndex(sp, (RosterCardState.Rarity)r, s);
                        Assert.That(idx, Is.InRange(0, Pets.CountsLength - 1));
                        Assert.That(seen.Add(idx), Is.True, "collided at " + sp + "/" + r + "/" + s);
                    }

            Assert.That(Pets.CellIndex(-1, RosterCardState.Rarity.Common, 1), Is.EqualTo(-1));
            Assert.That(Pets.CellIndex(0, RosterCardState.Rarity.Common, 0), Is.EqualTo(-1));
            Assert.That(Pets.CellIndex(0, RosterCardState.Rarity.Common, Pets.MaxStars + 1), Is.EqualTo(-1));
        }

        [Test]
        public void CountAtIsZeroForAMissingOrShortArray()
        {
            Assert.That(Pets.CountAt(null, 0, RosterCardState.Rarity.Common, 1), Is.Zero);
            Assert.That(Pets.CountAt(new int[3], 0, RosterCardState.Rarity.Mythic, Pets.MaxStars), Is.Zero);
        }

        [Test]
        public void BestOwnedPrefersRarestThenHighestStar()
        {
            var counts = new int[Pets.CountsLength];
            counts[Pets.CellIndex(2, RosterCardState.Rarity.Common, 3)] = 4;
            counts[Pets.CellIndex(2, RosterCardState.Rarity.Rare, 1)] = 1;
            counts[Pets.CellIndex(2, RosterCardState.Rarity.Rare, 4)] = 1;

            bool ok = Pets.TryBestOwned(counts, 2, out var r, out int s);
            Assert.That(ok, Is.True);
            Assert.That(r, Is.EqualTo(RosterCardState.Rarity.Rare));
            Assert.That(s, Is.EqualTo(4));
        }

        [Test]
        public void BestOwnedIsFalseForASpeciesNobodyHasDrawn()
        {
            var counts = new int[Pets.CountsLength];
            Assert.That(Pets.TryBestOwned(counts, 0, out _, out _), Is.False);
            Assert.That(Pets.TryBestOwned(counts, -1, out _, out _), Is.False);
        }

        // ---- combat -----------------------------------------------------------------------------

        [Test]
        public void ApplyCombatBonusAddsOnceAndClampsToTheSeaCaps()
        {
            var baseStats = new SeaCombat.Stats { Dodge = 0.10d, Def = 5d };
            var bonus = new double[Pets.EffectKindCount];
            bonus[(int)Pets.EffectKind.Dodge] = 0.50d;   // deliberately past the cap on its own
            bonus[(int)Pets.EffectKind.Def] = 2d;

            SeaCombat.Stats s = Pets.ApplyCombatBonus(baseStats, bonus);

            Assert.That(s.Dodge, Is.EqualTo(SeaCombat.DodgeCap));
            Assert.That(s.Def, Is.EqualTo(7d));
        }

        [Test]
        public void ANullBonusArrayChangesNothing()
        {
            var baseStats = new SeaCombat.Stats { Dodge = 0.12d, Hull = 40d };
            SeaCombat.Stats s = Pets.ApplyCombatBonus(baseStats, null);
            Assert.That(s.Dodge, Is.EqualTo(0.12d));
            Assert.That(s.Hull, Is.EqualTo(40d));
        }

        [Test]
        public void ANegativeOrOutOfRangeBonusCellIsIgnored()
        {
            var baseStats = new SeaCombat.Stats { Stun = 0.05d };
            var bonus = new double[2];   // shorter than EffectKindCount
            bonus[(int)Pets.EffectKind.Dodge] = -1d;
            SeaCombat.Stats s = Pets.ApplyCombatBonus(baseStats, bonus);
            Assert.That(s.Stun, Is.EqualTo(0.05d));
            Assert.That(s.Dodge, Is.Zero);
        }
    }
}
