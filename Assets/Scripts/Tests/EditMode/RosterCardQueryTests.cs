using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class RosterCardQueryTests
    {
        private readonly RosterCardState[] _cards =
        {
            Card(0, RosterCardState.Rarity.Common, 0, 0, 2),
            Card(1, RosterCardState.Rarity.Mythic, 1, 1, 2),
            Card(2, RosterCardState.Rarity.Rare, 4, 6, 6),
            Card(3, RosterCardState.Rarity.Epic, 2, 1, 4),
        };

        [Test]
        public void AllFilterKeepsLockedCardsVisible()
        {
            var order = new int[4];
            int count = RosterCardQuery.Fill(_cards, _cards.Length, RosterSortMode.Default,
                                              RosterFilterMode.All, order);
            Assert.That(count, Is.EqualTo(4));
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, order);
        }

        [Test]
        public void UpgradeReadyFilterReturnsOnlyActionableCards()
        {
            var order = new int[4];
            int count = RosterCardQuery.Fill(_cards, _cards.Length, RosterSortMode.Default,
                                              RosterFilterMode.UpgradeReady, order);
            Assert.That(count, Is.EqualTo(1));
            Assert.That(order[0], Is.EqualTo(2));
        }

        [Test]
        public void RaritySortIncludesLockedLongTermGoals()
        {
            var order = new int[4];
            int count = RosterCardQuery.Fill(_cards, _cards.Length, RosterSortMode.Rarity,
                                              RosterFilterMode.All, order);
            Assert.That(count, Is.EqualTo(4));
            CollectionAssert.AreEqual(new[] { 1, 3, 2, 0 }, order);
        }

        [Test]
        public void UpgradeSortPutsReadyThenOwnedThenLocked()
        {
            var order = new int[4];
            RosterCardQuery.Fill(_cards, _cards.Length, RosterSortMode.UpgradeReady,
                                 RosterFilterMode.All, order);
            CollectionAssert.AreEqual(new[] { 2, 1, 3, 0 }, order);
        }

        private static RosterCardState Card(int index, RosterCardState.Rarity rarity, int level,
                                            int duplicates, int required)
            => new RosterCardState(index, rarity, 0, level, 10, duplicates, required, 0d, false);
    }
}
