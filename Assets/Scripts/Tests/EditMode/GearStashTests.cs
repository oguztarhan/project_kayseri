using NUnit.Framework;
using Game.Core;

namespace Game.Tests
{
    /// <summary>
    /// The depo's arithmetic. Four small rules, and each of them is one the screen would otherwise
    /// get wrong in a way nobody notices until an item is gone: the id sequence that makes a tap
    /// safe, the capacity that decides whether a craft can be kept, the compare that draws the
    /// arrow, and the total the PARÇALA button prints before it is pressed.
    /// </summary>
    public class GearStashTests
    {
        private static SeaCombat.Tuning T => SeaCombat.Tuning.Default;

        private static SeaCombat.Item Item(int slot, int grade)
            => SeaCombat.ItemFor(slot, 0, grade, 0.5d, T);

        // ---- ids ---------------------------------------------------------------------------------

        [Test]
        public void IdsStartAtOneSoAZeroedRowAddressesNothing()
        {
            Assert.That(GearStash.NoId, Is.EqualTo(0L));
            Assert.That(GearStash.NextId(0L), Is.EqualTo(1L));
        }

        [Test]
        public void IdsOnlyEverGoUp()
        {
            long id = 0L;
            for (int i = 1; i <= 500; i++)
            {
                long next = GearStash.NextId(id);
                Assert.That(next, Is.GreaterThan(id));
                id = next;
            }
            Assert.That(id, Is.EqualTo(500L));
        }

        [Test]
        public void ADamagedSequenceStillYieldsARealId()
        {
            // A negative stored id is damage; the sequence must not answer 0 or another negative,
            // because both would address "nothing" and every action would be refused for ever.
            Assert.That(GearStash.NextId(-9L), Is.EqualTo(1L));
            Assert.That(GearStash.NextId(long.MinValue), Is.EqualTo(1L));
        }

        // ---- capacity ----------------------------------------------------------------------------

        [Test]
        public void RoomIsCountAgainstCapacity()
        {
            Assert.That(GearStash.HasRoom(0, 20), Is.True);
            Assert.That(GearStash.HasRoom(19, 20), Is.True);
            Assert.That(GearStash.HasRoom(20, 20), Is.False, "a full shelf takes nothing");
            Assert.That(GearStash.FreeSlots(20, 20), Is.Zero);
            Assert.That(GearStash.FreeSlots(6, 20), Is.EqualTo(14));
        }

        [Test]
        public void AnOverFullOrNonsenseShelfReportsNoRoomRatherThanNegativeSpace()
        {
            // Capacity is tuning and may be lowered under a shelf that is already fuller than
            // that. The answer has to be "no room", never a negative that some caller treats as
            // space and writes into.
            Assert.That(GearStash.FreeSlots(25, 20), Is.Zero);
            Assert.That(GearStash.HasRoom(25, 20), Is.False);
            Assert.That(GearStash.HasRoom(0, 0), Is.False, "a depo tuned to nothing holds nothing");
            Assert.That(GearStash.FreeSlots(-3, -3), Is.Zero);
        }

        [Test]
        public void TheDefaultShelfIsTheFiveByFourGrid()
        {
            Assert.That(GearStash.DefaultCapacity, Is.EqualTo(20));
            Assert.That(T.StashCapacity, Is.EqualTo(GearStash.DefaultCapacity),
                        "the sea's tuning must ship the same shelf the grid draws");
        }

        // ---- the upgrade arrow -------------------------------------------------------------------

        [Test]
        public void AnythingBeatsAnEmptySlot()
        {
            var empty = new SeaCombat.Item { Slot = SeaCombat.SlotCannon, Grade = -1 };
            Assert.That(GearStash.IsUpgrade(Item(SeaCombat.SlotCannon, 0), empty, T), Is.True);
        }

        [Test]
        public void NothingIsAnUpgradeForTheWrongSlotOrFromNoItem()
        {
            SeaCombat.Item cannon = Item(SeaCombat.SlotCannon, 4);
            SeaCombat.Item plating = Item(SeaCombat.SlotPlating, 0);
            Assert.That(GearStash.IsUpgrade(cannon, plating, T), Is.False,
                        "a Mythic cannon is not an upgrade for the plating slot");

            var nothing = new SeaCombat.Item { Slot = SeaCombat.SlotCannon, Grade = -1 };
            Assert.That(GearStash.IsUpgrade(nothing, plating, T), Is.False);
        }

        [Test]
        public void TheArrowIsScoredNotGraded()
        {
            // The whole reason IsUpgrade goes through ItemScore: a higher grade in the same slot
            // always scores higher, and an equal item is not an upgrade — an arrow on a sidegrade
            // is an arrow nobody trusts twice.
            for (int slot = 0; slot < SeaCombat.SlotCount; slot++)
            {
                SeaCombat.Item same = Item(slot, 2);
                Assert.That(GearStash.IsUpgrade(same, same, T), Is.False);
                Assert.That(GearStash.IsUpgrade(Item(slot, 3), same, T), Is.True);
                Assert.That(GearStash.IsUpgrade(Item(slot, 1), same, T), Is.False);
            }
        }

        // ---- the PARÇALA total -------------------------------------------------------------------

        [Test]
        public void ScrapTotalIsTheSumOfTheTwoLaddersOneItemAtATime()
        {
            var grades = new[] { 0, 4, 2 };
            long scrap = GearStash.ScrapTotal(grades, grades.Length, out long xp);

            Assert.That(scrap, Is.EqualTo(SeaCombat.ScrapFor(0) + SeaCombat.ScrapFor(4)
                                        + SeaCombat.ScrapFor(2)));
            Assert.That(xp, Is.EqualTo(Crafting.SalvageXpFor(0) + Crafting.SalvageXpFor(4)
                                     + Crafting.SalvageXpFor(2)));
        }

        [Test]
        public void ScrapTotalCountsOnlyTheFirstCountEntries()
        {
            // The service hands over a reusable buffer with stale grades past the end of the shelf.
            // Paying for those would pay for items that are not there.
            var buffer = new[] { 4, 4, 4, 4, 4 };
            long two = GearStash.ScrapTotal(buffer, 2, out long xpTwo);
            Assert.That(two, Is.EqualTo(SeaCombat.ScrapFor(4) * 2L));
            Assert.That(xpTwo, Is.EqualTo(Crafting.SalvageXpFor(4) * 2L));

            Assert.That(GearStash.ScrapTotal(buffer, 0, out long xpNone), Is.Zero);
            Assert.That(xpNone, Is.Zero);
        }

        [Test]
        public void ScrapTotalSurvivesNonsenseWithoutThrowing()
        {
            Assert.That(GearStash.ScrapTotal(null, 3, out long xpNull), Is.Zero);
            Assert.That(xpNull, Is.Zero);

            var grades = new[] { 1, 1 };
            Assert.DoesNotThrow(() => GearStash.ScrapTotal(grades, 99, out _));
            long capped = GearStash.ScrapTotal(grades, 99, out _);
            Assert.That(capped, Is.EqualTo(SeaCombat.ScrapFor(1) * 2L),
                        "a count past the buffer is clamped to the buffer");

            var damaged = new[] { -1, 3 };
            long paid = GearStash.ScrapTotal(damaged, 2, out long xp);
            Assert.That(paid, Is.EqualTo(SeaCombat.ScrapFor(3)), "a broken row pays nothing");
            Assert.That(xp, Is.EqualTo(Crafting.SalvageXpFor(3)));
        }
    }
}
