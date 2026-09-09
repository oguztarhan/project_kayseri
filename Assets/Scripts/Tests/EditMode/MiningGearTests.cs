using NUnit.Framework;
using Game.Core;

namespace Game.Tests
{
    /// <summary>
    /// The mining loadout's pure maths. <see cref="MiningGear.RollSlot"/> and
    /// <see cref="MiningGear.RollGrade"/> take their roll as an argument, so every rung of both
    /// ladders is walked exactly over the unit interval rather than sampled and hoped over.
    /// </summary>
    public class MiningGearTests
    {
        private static MiningGear.Tuning T => MiningGear.Tuning.Default;

        // ---- rolls ---------------------------------------------------------------------------

        [Test]
        public void RollSlotCoversAllFourSlotsExactlyOnce()
        {
            for (int i = 0; i < MiningGear.SlotCount; i++)
            {
                double roll = (i + 0.5d) / MiningGear.SlotCount;
                Assert.That(MiningGear.RollSlot(roll), Is.EqualTo(i));
            }
        }

        [Test]
        public void RollSlotClampsOutOfRangeRollsRatherThanThrowing()
        {
            Assert.DoesNotThrow(() => MiningGear.RollSlot(-5d));
            Assert.DoesNotThrow(() => MiningGear.RollSlot(5d));
            Assert.That(MiningGear.RollSlot(-5d), Is.EqualTo(0));
            Assert.That(MiningGear.RollSlot(5d), Is.EqualTo(MiningGear.SlotCount - 1));
        }

        [Test]
        public void RollGradeIsAlwaysARealGrade()
        {
            for (int i = 0; i <= 200; i++)
                Assert.That(MiningGear.RollGrade(i / 200d, T), Is.InRange(0, Captains.GradeCount - 1));
        }

        [Test]
        public void RollGradeClampsOutOfRangeRollsRatherThanThrowing()
        {
            Assert.DoesNotThrow(() => MiningGear.RollGrade(-5d, T));
            Assert.DoesNotThrow(() => MiningGear.RollGrade(1d, T));
            Assert.That(MiningGear.RollGrade(-5d, T), Is.EqualTo(0));
        }

        [Test]
        public void RollGradeReachesEveryGradeWithDefaultWeights()
        {
            var seen = new bool[Captains.GradeCount];
            for (int i = 0; i < 4000; i++)
                seen[MiningGear.RollGrade((i + 0.5d) / 4000d, T)] = true;
            for (int g = 0; g < seen.Length; g++)
                Assert.That(seen[g], Is.True, $"grade {g} never rolled");
        }

        [Test]
        public void RollGradeWithNoWeightAtAllFallsBackToCommon()
        {
            var zero = new MiningGear.Tuning();
            Assert.That(MiningGear.RollGrade(0.5d, zero), Is.EqualTo(0));
        }

        // ---- bonus and income ------------------------------------------------------------------

        [Test]
        public void BonusForIsStrictlyIncreasingByGrade()
        {
            double last = -1d;
            for (int g = 0; g < Captains.GradeCount; g++)
            {
                double bonus = MiningGear.BonusFor(g, T);
                Assert.That(bonus, Is.GreaterThan(last));
                last = bonus;
            }
        }

        [Test]
        public void BonusForAnEmptySlotIsZero()
        {
            Assert.That(MiningGear.BonusFor(MiningGear.NoGrade, T), Is.EqualTo(0d));
        }

        [Test]
        public void IncomeMultiplierWithNothingWornIsOne()
        {
            Assert.That(MiningGear.IncomeMultiplier(null, T), Is.EqualTo(1d));

            var empty = new[] { MiningGear.NoGrade, MiningGear.NoGrade, MiningGear.NoGrade, MiningGear.NoGrade };
            Assert.That(MiningGear.IncomeMultiplier(empty, T), Is.EqualTo(1d));
        }

        [Test]
        public void IncomeMultiplierSumsEachWornSlotsBonus()
        {
            var mixed = new[] { 0, 1, 2, MiningGear.NoGrade };   // Common, Rare, Epic, empty
            double expected = 1d + T.CommonBonus + T.RareBonus + T.EpicBonus;
            Assert.That(MiningGear.IncomeMultiplier(mixed, T), Is.EqualTo(expected).Within(1e-9));
        }

        [Test]
        public void IncomeMultiplierFullMythicSetIsFourMythicBonuses()
        {
            var mythic = new[] { 4, 4, 4, 4 };
            double expected = 1d + 4d * T.MythicBonus;
            Assert.That(MiningGear.IncomeMultiplier(mythic, T), Is.EqualTo(expected).Within(1e-9));
        }

        [Test]
        public void IncomeMultiplierReadsAtMostFourSlotsFromALongerArray()
        {
            var tooLong = new[] { 4, 4, 4, 4, 4, 4 };
            var exact = new[] { 4, 4, 4, 4 };
            Assert.That(MiningGear.IncomeMultiplier(tooLong, T),
                        Is.EqualTo(MiningGear.IncomeMultiplier(exact, T)));
        }

        // ---- upgrades and scrap -----------------------------------------------------------------

        [Test]
        public void AnEmptySlotIsAnUpgradeForAnything()
        {
            Assert.That(MiningGear.IsUpgrade(0, MiningGear.NoGrade), Is.True);
        }

        [Test]
        public void AnEmptyCraftIsNeverAnUpgrade()
        {
            Assert.That(MiningGear.IsUpgrade(MiningGear.NoGrade, 0), Is.False);
            Assert.That(MiningGear.IsUpgrade(MiningGear.NoGrade, MiningGear.NoGrade), Is.False);
        }

        [Test]
        public void AHigherGradeIsAnUpgradeAndATieOrLowerIsNot()
        {
            Assert.That(MiningGear.IsUpgrade(2, 1), Is.True);
            Assert.That(MiningGear.IsUpgrade(1, 1), Is.False);
            Assert.That(MiningGear.IsUpgrade(1, 2), Is.False);
        }

        [Test]
        public void ScrapForAnEmptySlotIsZeroAndGradesClampIntoTheTable()
        {
            Assert.That(MiningGear.ScrapFor(MiningGear.NoGrade), Is.EqualTo(0L));
            Assert.That(MiningGear.ScrapFor(99), Is.EqualTo(MiningGear.ScrapFor(Captains.GradeCount - 1)));
        }

        // ---- points --------------------------------------------------------------------------

        [Test]
        public void PointsForElapsedIsZeroBelowOneTick()
        {
            Assert.That(MiningGear.PointsForElapsed(0L, T), Is.EqualTo(0L));
            Assert.That(MiningGear.PointsForElapsed((long)T.TickSeconds - 1L, T), Is.EqualTo(0L));
        }

        [Test]
        public void PointsForElapsedTruncatesToWholeTicks()
        {
            long oneTick = (long)T.TickSeconds;
            Assert.That(MiningGear.PointsForElapsed(oneTick, T), Is.EqualTo(T.PointsPerTick));
            Assert.That(MiningGear.PointsForElapsed(oneTick * 3 + 1, T), Is.EqualTo(T.PointsPerTick * 3));
        }

        [Test]
        public void PointsForElapsedNeverThrowsOnNegativeInput()
        {
            Assert.DoesNotThrow(() => MiningGear.PointsForElapsed(-100L, T));
            Assert.That(MiningGear.PointsForElapsed(-100L, T), Is.EqualTo(0L));
        }
    }
}
