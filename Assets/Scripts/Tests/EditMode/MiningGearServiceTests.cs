using NUnit.Framework;
using Game.Core;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>
    /// The half of the mining loadout that touches the save: padding, points, and that a craft
    /// always resolves to exactly one outcome. The odds and the bonus curve are covered in
    /// MiningGearTests; what is tested here is the spending, the equip-or-scrap compare, and the
    /// point pool's wall-clock accrual.
    /// </summary>
    public class MiningGearServiceTests
    {
        private static MiningGear.Tuning T => MiningGear.Tuning.Default;

        private static MiningGearService Make(SaveData data, int seed = 12345)
            => new MiningGearService(data, null, new TimeService(), T, new System.Random(seed));

        // ---- the save contract -------------------------------------------------------------------

        [Test]
        public void ASaveFromBeforeMiningGearExistedWorks()
        {
            var data = new SaveData();
            data.miningGearGrade = null;

            MiningGearService s = Make(data);
            Assert.That(data.miningGearGrade.Length, Is.EqualTo(MiningGear.SlotCount));
            Assert.That(s.Points, Is.Zero);
            for (int slot = 0; slot < MiningGear.SlotCount; slot++)
                Assert.That(s.WornGrade(slot), Is.EqualTo(MiningGear.NoGrade));
            Assert.That(s.IncomeMultiplier, Is.EqualTo(1d));
        }

        [Test]
        public void AShortArrayIsPaddedAndKeepsWhatItHad()
        {
            var data = new SaveData();
            data.miningGearGrade = new[] { 3, 5 };   // Epic in slot 0, Mythic in slot 1

            MiningGearService s = Make(data);
            Assert.That(data.miningGearGrade.Length, Is.EqualTo(MiningGear.SlotCount));
            Assert.That(s.WornGrade(0), Is.EqualTo(2));   // Epic
            Assert.That(s.WornGrade(1), Is.EqualTo(4));   // Mythic
            for (int slot = 2; slot < MiningGear.SlotCount; slot++)
                Assert.That(s.WornGrade(slot), Is.EqualTo(MiningGear.NoGrade));
        }

        [Test]
        public void AnOutOfRangeGradeCellIsResetToEmptyRatherThanTrusted()
        {
            var data = new SaveData();
            data.miningGearGrade = new[] { 99, -3, 0, 1 };

            MiningGearService s = Make(data);
            Assert.That(s.WornGrade(0), Is.EqualTo(MiningGear.NoGrade));
            Assert.That(s.WornGrade(1), Is.EqualTo(MiningGear.NoGrade));
            Assert.That(s.WornGrade(2), Is.EqualTo(MiningGear.NoGrade));
            Assert.That(s.WornGrade(3), Is.EqualTo(0));   // Common — a valid cell survives
        }

        [Test]
        public void ANullSaveIsSurvivable()
        {
            var s = new MiningGearService(null, null, new TimeService(), T);
            Assert.That(s.Points, Is.Zero);
            Assert.That(s.CanCraft, Is.False);
            Assert.That(s.IncomeMultiplier, Is.EqualTo(1d));
            Assert.DoesNotThrow(() => s.Poll());
            var result = s.TryCraft();
            Assert.That(result.Crafted, Is.False);
        }

        // ---- crafting ----------------------------------------------------------------------------

        [Test]
        public void ACraftRefusesRatherThanGoingNegativeWhenShortOfPoints()
        {
            var data = new SaveData { miningPoints = T.CraftCost - 1 };
            MiningGearService s = Make(data);

            MiningGearService.CraftResult result = s.TryCraft();
            Assert.That(result.Crafted, Is.False);
            Assert.That(s.Points, Is.EqualTo(T.CraftCost - 1));   // untouched
        }

        [Test]
        public void ACraftSpendsExactlyTheCraftCost()
        {
            var data = new SaveData { miningPoints = T.CraftCost * 3 };
            MiningGearService s = Make(data);

            MiningGearService.CraftResult result = s.TryCraftWithRolls(0.1d, 0.1d);
            Assert.That(result.Crafted, Is.True);
            Assert.That(s.Points, Is.EqualTo(T.CraftCost * 2));
        }

        [Test]
        public void AnEmptySlotAlwaysEquipsWhateverIsCrafted()
        {
            // Every slot starts empty, so IsUpgrade is true no matter what the dice say.
            var data = new SaveData { miningPoints = T.CraftCost };
            MiningGearService s = Make(data);

            MiningGearService.CraftResult result = s.TryCraftWithRolls(0.73d, 0.02d);
            Assert.That(result.Crafted, Is.True);
            Assert.That(result.Equipped, Is.True);
            Assert.That(s.WornGrade(result.Slot), Is.EqualTo(result.Grade));
        }

        [Test]
        public void ANewCraftNeverEquipsOverAFullMythicLoadout()
        {
            var data = new SaveData
            {
                miningPoints = T.CraftCost,
                miningGearGrade = new[] { 5, 5, 5, 5 },   // Mythic (Grade+1) in every slot
            };
            MiningGearService s = Make(data);

            MiningGearService.CraftResult result = s.TryCraftWithRolls(0.4d, 0.999d);
            Assert.That(result.Crafted, Is.True);
            Assert.That(result.Equipped, Is.False);
            for (int slot = 0; slot < MiningGear.SlotCount; slot++)
                Assert.That(s.WornGrade(slot), Is.EqualTo((int)Captains.Grade.Mythic));
        }

        [Test]
        public void EveryCraftPaysScrapWhicheverSideOfTheCompareItLandsOn()
        {
            var data = new SaveData { miningPoints = T.CraftCost };
            MiningGearService s = Make(data);

            MiningGearService.CraftResult result = s.TryCraftWithRolls(0.1d, 0.1d);
            Assert.That(result.ScrapEarned, Is.GreaterThanOrEqualTo(0L));
            Assert.That(s.Scrap, Is.EqualTo(result.ScrapEarned));
        }

        [Test]
        public void EquippingOverAWornItemScrapsTheDisplacedOneNotTheNewOne()
        {
            var data = new SaveData
            {
                miningPoints = T.CraftCost,
                miningGearGrade = new[] { 1, 0, 0, 0 },   // Common (grade 0) worn in slot 0
            };
            MiningGearService s = Make(data);

            // Slot 0, rolling Mythic — beats the worn Common.
            MiningGearService.CraftResult result = s.TryCraftWithRolls(0.05d, 0.999d);
            Assert.That(result.Slot, Is.EqualTo(0));
            Assert.That(result.Equipped, Is.True);
            Assert.That(s.WornGrade(0), Is.EqualTo((int)Captains.Grade.Mythic));
            Assert.That(result.ScrapEarned, Is.EqualTo(MiningGear.ScrapFor((int)Captains.Grade.Common)));
        }

        [Test]
        public void IncomeMultiplierMovesTheMomentAnEquipHappens()
        {
            var data = new SaveData { miningPoints = T.CraftCost };
            MiningGearService s = Make(data);
            Assert.That(s.IncomeMultiplier, Is.EqualTo(1d));

            s.TryCraftWithRolls(0.1d, 0.999d);   // empty slot, Mythic roll: always equips
            Assert.That(s.IncomeMultiplier, Is.EqualTo(1d + T.MythicBonus).Within(1e-9));
        }

        // ---- point accrual -----------------------------------------------------------------------

        [Test]
        public void PointsAccrueOverElapsedWallClockTimeOnConstruction()
        {
            long now = new TimeService().NowUnix();
            var data = new SaveData
            {
                miningPoints = 0L,
                miningPointsStampUnix = now - (long)T.TickSeconds * 3L,
            };

            MiningGearService s = Make(data);
            Assert.That(s.Points, Is.EqualTo(T.PointsPerTick * 3));
        }

        [Test]
        public void PointAccrualStopsAtThePointCap()
        {
            long now = new TimeService().NowUnix();
            var data = new SaveData
            {
                miningPoints = 0L,
                miningPointsStampUnix = now - (long)T.TickSeconds * (T.PointCap + 50L),
            };

            MiningGearService s = Make(data);
            Assert.That(s.Points, Is.EqualTo(T.PointCap));
        }

        [Test]
        public void APointStampThatHasNeverBeenSetStartsTheClockRatherThanBackdating()
        {
            var data = new SaveData { miningPoints = 0L, miningPointsStampUnix = 0L };
            MiningGearService s = Make(data);
            Assert.That(s.Points, Is.Zero);
            Assert.That(data.miningPointsStampUnix, Is.GreaterThan(0L));
        }
    }
}
