using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class RosterCardStateTests
    {
        [Test]
        public void LockedCardHasNoProgressOrAttention()
        {
            var card = Card(level: 0, duplicates: 9, required: 2);

            Assert.That(card.CardStatus, Is.EqualTo(RosterCardState.Status.Locked));
            Assert.That(card.Progress, Is.Zero);
            Assert.That(card.CanUpgrade, Is.False);
            Assert.That(card.NeedsAttention, Is.False);
        }

        [Test]
        public void OwnedCardReportsClampedDuplicateProgress()
        {
            var halfway = Card(level: 2, duplicates: 3, required: 6);
            var overflowing = Card(level: 2, duplicates: 12, required: 6);

            Assert.That(halfway.CardStatus, Is.EqualTo(RosterCardState.Status.Owned));
            Assert.That(halfway.Progress, Is.EqualTo(0.5f));
            Assert.That(overflowing.Progress, Is.EqualTo(1f));
            Assert.That(overflowing.CanUpgrade, Is.True);
        }

        [Test]
        public void MaxedCardIsCompleteButNeverUpgradeReady()
        {
            var card = Card(level: 10, duplicates: 999, required: 0);

            Assert.That(card.CardStatus, Is.EqualTo(RosterCardState.Status.Maxed));
            Assert.That(card.Progress, Is.EqualTo(1f));
            Assert.That(card.CanUpgrade, Is.False);
        }

        [Test]
        public void ConstructorSanitisesSaveFacingValues()
        {
            var card = new RosterCardState(3, (RosterCardState.Rarity)99, 2, 99, 10,
                                           -4, -2, -1d, false);

            Assert.That(card.Tier, Is.EqualTo(RosterCardState.Rarity.Mythic));
            Assert.That(card.Level, Is.EqualTo(10));
            Assert.That(card.Duplicates, Is.Zero);
            Assert.That(card.DuplicatesRequired, Is.Zero);
            Assert.That(card.Effect, Is.Zero);
        }

        private static RosterCardState Card(int level, int duplicates, int required)
            => new RosterCardState(0, RosterCardState.Rarity.Rare, 1, level, 10,
                                   duplicates, required, 0.25d, false);
    }
}
