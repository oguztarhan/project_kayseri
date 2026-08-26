using System.Collections.Generic;
using Game.Systems;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class BoostAndIapSafetyTests
    {
        [Test]
        public void RewardedAd_OnEmptyTimer_GrantsExactlyFiveMinutes()
        {
            var data = new SaveData();
            var boost = new BoostService(data, new TimeService());

            boost.AddRewardedAdBoost(2d);

            Assert.That(boost.ActiveMultiplier, Is.EqualTo(2d));
            Assert.That(boost.SecondsLeft, Is.InRange(299f, 300f));
        }

        [Test]
        public void RewardedAd_OnRunningStarterTimer_ExtendsByExactlyFiveMinutes()
        {
            var data = new SaveData();
            var time = new TimeService();
            long originalEnd = time.NowUnix() + 345600L;
            data.boostMultiplier = 2d;
            data.boostEndUnix = originalEnd;
            var boost = new BoostService(data, time);

            boost.AddRewardedAdBoost(2d);

            Assert.That(data.boostEndUnix, Is.EqualTo(originalEnd + 300L));
        }

        [Test]
        public void SameIapTransaction_CanOnlyBeRecordedOnce()
        {
            var data = new SaveData();

            Assert.That(IapTransactionJournal.Record(data, "transaction-42"), Is.True);
            Assert.That(IapTransactionJournal.Record(data, "transaction-42"), Is.False);
            Assert.That(data.processedIapTransactions.Count, Is.EqualTo(1));
            Assert.That(IapTransactionJournal.Contains(data, "transaction-42"), Is.True);
        }

        [Test]
        public void SaveReset_PreservesProcessedTransactions()
        {
            var old = new SaveData();
            IapTransactionJournal.Record(old, "paid-order");

            SaveData fresh = SaveMigration.Reset(old);

            Assert.That(IapTransactionJournal.Contains(fresh, "paid-order"), Is.True);
        }

        [Test]
        public void StarterOffer_EveryIslandGetsItsOwnFullFortyEightHourWindow()
        {
            var data = new SaveData { starterOffersMigrated = true };
            StarterOfferState.EnsureStarted(data, "coal", 1000L);

            long afterCoalExpired = 1000L + StarterOfferState.WindowSeconds + 10L;
            Assert.That(StarterOfferState.SecondsLeft(data, "coal", afterCoalExpired), Is.Zero);
            Assert.That(StarterOfferState.EnsureStarted(data, "copper", afterCoalExpired), Is.True);
            Assert.That(StarterOfferState.SecondsLeft(data, "copper", afterCoalExpired),
                        Is.EqualTo(StarterOfferState.WindowSeconds));
        }

        [Test]
        public void StarterOffer_BuyingOneIslandDoesNotCloseTheNextIsland()
        {
            var data = new SaveData { starterOffersMigrated = true };
            StarterOfferState.EnsureStarted(data, "coal", 1000L);
            StarterOfferState.EnsureStarted(data, "copper", 2000L);

            StarterOfferState.MarkBought(data, "coal");

            Assert.That(StarterOfferState.Bought(data, "coal"), Is.True);
            Assert.That(StarterOfferState.SecondsLeft(data, "coal", 2000L), Is.Zero);
            Assert.That(StarterOfferState.Bought(data, "copper"), Is.False);
            Assert.That(StarterOfferState.SecondsLeft(data, "copper", 2000L),
                        Is.EqualTo(StarterOfferState.WindowSeconds));
        }

        [Test]
        public void StarterOffer_LegacyGlobalBuyerKeepsAlreadyOwnedIslandsOnly()
        {
            var data = new SaveData();
            data.purchasedOffers.Add(StarterOfferState.Sku);

            StarterOfferState.MigrateLegacy(
                data, "iron", new List<string> { "coal", "copper", "iron" });

            Assert.That(StarterOfferState.Bought(data, "coal"), Is.True);
            Assert.That(StarterOfferState.Bought(data, "copper"), Is.True);
            Assert.That(StarterOfferState.Bought(data, "iron"), Is.True);
            Assert.That(StarterOfferState.Bought(data, "gold"), Is.False);
        }

        [Test]
        public void StarterOffer_LegacyUnboughtCountdownContinuesOnActiveIsland()
        {
            var data = new SaveData { starterOfferSeenUnix = 12345L };

            StarterOfferState.MigrateLegacy(
                data, "iron", new List<string> { "coal", "iron" });

            Assert.That(StarterOfferState.StartedUnix(data, "iron"), Is.EqualTo(12345L));
            Assert.That(StarterOfferState.StartedUnix(data, "coal"), Is.Zero);
        }

        [Test]
        public void StarterOffer_LegacyMigrationDoesNotBlockFutureIslands()
        {
            var data = new SaveData();
            data.purchasedOffers.Add(StarterOfferState.LegacyV2Sku);
            StarterOfferState.MigrateLegacy(data, "coal", new List<string> { "coal" });

            Assert.That(StarterOfferState.MigrateLegacy(
                data, "copper", new List<string> { "coal", "copper" }), Is.False);
            Assert.That(StarterOfferState.Bought(data, "copper"), Is.False);
        }
    }
}
