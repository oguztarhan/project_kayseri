using NUnit.Framework;
using UnityEngine;
using Game.Core;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>
    /// The collection's persisted block: that it survives a round trip through Unity's serialiser,
    /// that a save written before the feature existed loads as an empty collection rather than a
    /// crash, and that a hand-edited one cannot buy anything the game does not sell.
    ///
    /// The round trips go through <see cref="JsonUtility"/> rather than asserting on the object in
    /// memory, because that is what actually writes the file — a field the serialiser silently drops
    /// would pass any test that never left the heap.
    /// </summary>
    public class CardCollectionSaveTests
    {
        private static CardCollectionSaveData RoundTrip(CardCollectionSaveData block)
        {
            var host = new SaveData { cardCollection = block };
            string json = JsonUtility.ToJson(host);
            return JsonUtility.FromJson<SaveData>(json).cardCollection;
        }

        // ---- a fresh save -------------------------------------------------------------------------

        [Test]
        public void AFreshSaveIsAnEmptyValidCollection()
        {
            var data = new SaveData();

            Assert.That(data.cardCollection, Is.Not.Null);
            Assert.That(data.cardCollection.progress, Is.Empty);
            Assert.That(data.cardCollection.claimedSetRewardIds, Is.Empty);
            Assert.That(data.cardCollection.unopenedPacks, Is.Zero);
            Assert.That(data.cardCollection.packsOpened, Is.Zero);
            Assert.That(data.cardCollection.pullsSinceEpic, Is.Zero);
            Assert.That(data.cardCollection.pullsSinceLegendary, Is.Zero);
            Assert.That(data.cardCollection.Normalise(), Is.False, "a fresh block needs no repair");
        }

        [Test]
        public void TheDailyPackIsClaimableOnTheFirstEverLaunch()
        {
            // int.MinValue is "no day yet", and it must not accidentally equal a real UTC day number
            // or the very first daily pack would be lost.
            var data = new SaveData();
            Assert.That(data.cardCollection.dailyPackDay, Is.EqualTo(int.MinValue));
            Assert.That(data.cardCollection.dailyPackDay,
                        Is.Not.EqualTo(Goals.DayNumber(0L)));
        }

        // ---- backwards compatibility --------------------------------------------------------------

        [Test]
        public void ASaveWrittenBeforeTheCollectionExistedLoadsAsAnEmptyOne()
        {
            // Exactly what a pre-Plan-14 file looks like: the key is simply not there. This is the
            // whole reason the block could be added without bumping SaveMigration.CurrentVersion.
            const string legacy = "{\"version\":7,\"charts\":40,\"salvage\":120}";

            SaveData loaded = null;
            Assert.DoesNotThrow(() => loaded = JsonUtility.FromJson<SaveData>(legacy));

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.cardCollection, Is.Not.Null,
                        "a missing collection block must default, not arrive null");
            Assert.That(loaded.cardCollection.progress, Is.Empty);
            Assert.That(loaded.cardCollection.Normalise(), Is.False);
            Assert.That(loaded.charts, Is.EqualTo(40L), "the rest of the save must be untouched");
            Assert.That(loaded.salvage, Is.EqualTo(120L));
        }

        [Test]
        public void AddingTheCollectionDidNotBumpTheSaveVersion()
        {
            // Bumping it would wipe every tester's progress for a feature that adds an empty list.
            Assert.That(SaveMigration.CurrentVersion, Is.EqualTo(7));
        }

        [Test]
        public void ACollectionSurvivesTheResetThatKeepsOnlyPurchases()
        {
            // It must NOT: a wipe throws away progress, and a collection is progress. Asserted so
            // that if someone later adds it to the keep-list, they do it on purpose.
            var old = new SaveData();
            old.cardCollection.unopenedPacks = 9;
            old.cardCollection.FindOrAdd("coal_industry/ore_scale").level = 4;

            SaveData fresh = SaveMigration.Reset(old);

            Assert.That(fresh.cardCollection, Is.Not.Null);
            Assert.That(fresh.cardCollection.progress, Is.Empty);
            Assert.That(fresh.cardCollection.unopenedPacks, Is.Zero);
        }

        // ---- round trip ---------------------------------------------------------------------------

        [Test]
        public void EveryFieldSurvivesTheSerialiser()
        {
            var block = new CardCollectionSaveData
            {
                unopenedPacks = 3,
                dailyPackDay = 20363,
                packsOpened = 117,
                pullsSinceEpic = 6,
                pullsSinceLegendary = 41,
            };
            block.FindOrAdd("coal_industry/pit_charter").level = 3;
            block.Find("coal_industry/pit_charter").duplicates = 2;
            block.Find("coal_industry/pit_charter").seen = true;
            block.claimedSetRewardIds.Add("coal_industry");

            CardCollectionSaveData back = RoundTrip(block);

            Assert.That(back.unopenedPacks, Is.EqualTo(3));
            Assert.That(back.dailyPackDay, Is.EqualTo(20363));
            Assert.That(back.packsOpened, Is.EqualTo(117));
            Assert.That(back.pullsSinceEpic, Is.EqualTo(6));
            Assert.That(back.pullsSinceLegendary, Is.EqualTo(41));
            Assert.That(back.claimedSetRewardIds, Is.EqualTo(new[] { "coal_industry" }));

            var row = back.Find("coal_industry/pit_charter");
            Assert.That(row, Is.Not.Null);
            Assert.That(row.level, Is.EqualTo(3));
            Assert.That(row.duplicates, Is.EqualTo(2));
            Assert.That(row.seen, Is.True);
        }

        [Test]
        public void AWholeCollectionSurvivesTheSerialiser()
        {
            var block = new CardCollectionSaveData();
            for (int card = 0; card < CardCollectionCatalogue.Count; card++)
            {
                var row = block.FindOrAdd(CardCollectionCatalogue.IdOf(card));
                row.level = 1 + card % CardCollection.MaxLevel;
                row.duplicates = card;
            }

            CardCollectionSaveData back = RoundTrip(block);

            Assert.That(back.progress.Count, Is.EqualTo(CardCollectionCatalogue.Count));
            for (int card = 0; card < CardCollectionCatalogue.Count; card++)
            {
                var row = back.Find(CardCollectionCatalogue.IdOf(card));
                Assert.That(row, Is.Not.Null, CardCollectionCatalogue.IdOf(card));
                Assert.That(row.level, Is.EqualTo(1 + card % CardCollection.MaxLevel));
                Assert.That(row.duplicates, Is.EqualTo(card));
            }
        }

        // ---- normalisation ------------------------------------------------------------------------

        [Test]
        public void NullListsAreRepairedRatherThanLeftToThrow()
        {
            var block = new CardCollectionSaveData { progress = null, claimedSetRewardIds = null };

            Assert.That(block.Normalise(), Is.True);
            Assert.That(block.progress, Is.Not.Null.And.Empty);
            Assert.That(block.claimedSetRewardIds, Is.Not.Null.And.Empty);
            Assert.That(block.Find("anything"), Is.Null);
            Assert.That(block.HasClaimedSetReward("coal_industry"), Is.False);
        }

        [Test]
        public void AHandEditedLevelCannotExceedTheCeiling()
        {
            var block = new CardCollectionSaveData();
            block.FindOrAdd("coal_industry/ore_scale").level = 99;

            Assert.That(block.Normalise(), Is.True);
            Assert.That(block.Find("coal_industry/ore_scale").level,
                        Is.EqualTo(CardCollection.MaxLevel));
        }

        [Test]
        public void NegativeCountersAreFloored()
        {
            var block = new CardCollectionSaveData
            {
                unopenedPacks = -4,
                packsOpened = -1,
                pullsSinceEpic = -9,
                pullsSinceLegendary = -3,
            };
            block.FindOrAdd("coal_industry/ore_scale").duplicates = -7;
            block.FindOrAdd("coal_industry/lamp_oil").level = -2;

            Assert.That(block.Normalise(), Is.True);
            Assert.That(block.unopenedPacks, Is.Zero);
            Assert.That(block.packsOpened, Is.Zero);
            Assert.That(block.pullsSinceEpic, Is.Zero);
            Assert.That(block.pullsSinceLegendary, Is.Zero);
            Assert.That(block.Find("coal_industry/ore_scale").duplicates, Is.Zero);
            Assert.That(block.Find("coal_industry/lamp_oil").level, Is.EqualTo(CardCollection.NotOwned));
        }

        [Test]
        public void RowsThatNameNoCardAreDropped()
        {
            var block = new CardCollectionSaveData();
            block.progress.Add(new CardCollectionProgress { cardId = "", level = 3 });
            block.progress.Add(null);
            block.progress.Add(new CardCollectionProgress { cardId = "coal_industry/ore_scale", level = 2 });

            Assert.That(block.Normalise(), Is.True);
            Assert.That(block.progress.Count, Is.EqualTo(1));
            Assert.That(block.Find("coal_industry/ore_scale").level, Is.EqualTo(2));
        }

        [Test]
        public void ACardWithTwoRowsEndsUpWithOne()
        {
            var block = new CardCollectionSaveData();
            block.progress.Add(new CardCollectionProgress { cardId = "deep_waters/grapnel", level = 1, duplicates = 1 });
            block.progress.Add(new CardCollectionProgress { cardId = "deep_waters/grapnel", level = 4, duplicates = 6 });

            Assert.That(block.Normalise(), Is.True);
            Assert.That(block.progress.Count, Is.EqualTo(1));
            Assert.That(block.Find("deep_waters/grapnel").level, Is.EqualTo(4),
                        "the later-written row is the one kept");
        }

        [Test]
        public void ASetRewardClaimedTwiceIsRecordedOnce()
        {
            var block = new CardCollectionSaveData();
            block.claimedSetRewardIds.Add("coal_industry");
            block.claimedSetRewardIds.Add("deep_waters");
            block.claimedSetRewardIds.Add("coal_industry");
            block.claimedSetRewardIds.Add("");

            Assert.That(block.Normalise(), Is.True);
            Assert.That(block.claimedSetRewardIds, Is.EqualTo(new[] { "coal_industry", "deep_waters" }));
            Assert.That(block.HasClaimedSetReward("coal_industry"), Is.True);
            Assert.That(block.HasClaimedSetReward("workshop_guild"), Is.False);
        }

        [Test]
        public void ACardTheBuildNoLongerCarriesIsKeptRatherThanDestroyed()
        {
            // A downgrade, or a staged rollout the player is not in yet. Dropping the row would
            // silently wipe a collection that the next build would have shown again.
            var block = new CardCollectionSaveData();
            block.FindOrAdd("some_future_set/unknown_card").level = 5;
            block.FindOrAdd("coal_industry/ore_scale").level = 2;

            block.Normalise();

            Assert.That(block.Find("some_future_set/unknown_card"), Is.Not.Null);
            Assert.That(block.Find("some_future_set/unknown_card").level, Is.EqualTo(5));
            Assert.That(CardCollectionCatalogue.IndexOf("some_future_set/unknown_card"), Is.EqualTo(-1),
                        "and the catalogue still refuses to resolve it");
        }

        [Test]
        public void DuplicatesOnAnUnownedCardAreKeptButStillCannotUnlockIt()
        {
            var block = new CardCollectionSaveData();
            var row = block.FindOrAdd("coal_industry/pit_charter");
            row.level = CardCollection.NotOwned;
            row.duplicates = 500;

            Assert.That(block.Normalise(), Is.False, "there is nothing here to repair");
            Assert.That(row.duplicates, Is.EqualTo(500));
            Assert.That(CardCollection.CanLevel(RosterCardState.Rarity.Legendary,
                            row.level, row.duplicates, CardCollection.Tuning.Default),
                        Is.False, "only a pack draw may create level 1");
        }

        [Test]
        public void NormaliseIsIdempotent()
        {
            var block = new CardCollectionSaveData { unopenedPacks = -2, pullsSinceEpic = -5 };
            block.progress.Add(null);
            block.progress.Add(new CardCollectionProgress { cardId = "deep_waters/grapnel", level = 77 });
            block.claimedSetRewardIds.Add("deep_waters");
            block.claimedSetRewardIds.Add("deep_waters");

            Assert.That(block.Normalise(), Is.True, "the first pass has work to do");
            Assert.That(block.Normalise(), Is.False, "the second should find nothing left");
        }

        [Test]
        public void ARepairedSaveSurvivesBeingWrittenBackAndReloaded()
        {
            var block = new CardCollectionSaveData { unopenedPacks = -3 };
            block.progress.Add(new CardCollectionProgress { cardId = "deep_waters/tide_table", level = 42 });
            block.Normalise();

            CardCollectionSaveData back = RoundTrip(block);

            Assert.That(back.Normalise(), Is.False, "a repaired save should stay repaired");
            Assert.That(back.unopenedPacks, Is.Zero);
            Assert.That(back.Find("deep_waters/tide_table").level, Is.EqualTo(CardCollection.MaxLevel));
        }

        // ---- lookups ------------------------------------------------------------------------------

        [Test]
        public void FindOrAddCreatesOnceAndFindsThereafter()
        {
            var block = new CardCollectionSaveData();

            var first = block.FindOrAdd("workshop_guild/bellows");
            var again = block.FindOrAdd("workshop_guild/bellows");

            Assert.That(again, Is.SameAs(first));
            Assert.That(block.progress.Count, Is.EqualTo(1));
            Assert.That(first.level, Is.EqualTo(CardCollection.NotOwned),
                        "a new row is a card the player has met, not one they own");
        }

        [Test]
        public void FindRefusesNonsenseRatherThanGuessing()
        {
            var block = new CardCollectionSaveData();
            block.FindOrAdd("workshop_guild/bellows");

            Assert.That(block.Find(null), Is.Null);
            Assert.That(block.Find(""), Is.Null);
            Assert.That(block.Find("workshop_guild/Bellows"), Is.Null, "ids are case-sensitive save keys");
        }
    }
}
