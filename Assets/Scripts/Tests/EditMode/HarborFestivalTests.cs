using System;
using System.Collections.Generic;
using Game.Core;
using Game.Systems;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class HarborFestivalTests
    {
        private const long Day = 86400L;

        private static LiveEvents.Definition Definition(long start, long end, int version = 1)
            => new LiveEvents.Definition
            {
                Id = "harbor-2026",
                Kind = HarborFestival.Kind,
                StartUnix = start,
                EndUnix = end,
                ConfigVersion = version,
                Slots = HarborFestival.Slots,
                MinIslands = 0,
            };

        private sealed class Rig
        {
            public SaveData Data;
            public GoalService Goals;
            public HarborFestivalService Festival;
            public LiveEventService Events;
        }

        private static Rig Running(HarborFestival.Tuning? tuning = null)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var goals = new GoalService(data, wallet);
            var events = new LiveEventService(data,
                new List<LiveEvents.Definition> { Definition(now - Day, now + Day) });
            var festival = new HarborFestivalService(events, goals, wallet,
                tuning ?? HarborFestival.Tuning.Default, data: data);
            return new Rig { Data = data, Goals = goals, Events = events, Festival = festival };
        }

        [Test]
        public void SlotMapFitsLiveEventBound()
        {
            Assert.That(HarborFestival.Slots, Is.EqualTo(33));
            Assert.That(HarborFestival.Slots, Is.LessThanOrEqualTo(LiveEvents.MaxSlots));
            Assert.That(HarborFestival.ExpirySlot, Is.EqualTo(HarborFestival.Slots - 1));
        }

        [Test]
        public void ShippedTuningIsWellFormed()
        {
            HarborFestival.Tuning tuning = HarborFestival.Tuning.Default;
            Assert.That(HarborFestival.IsWellFormed(tuning), Is.True);
            Assert.That(HarborFestival.TotalTokens(tuning), Is.EqualTo(300));
        }

        [Test]
        public void FirstSyncSeedsBaselineWithoutRetroactiveProgress()
        {
            Rig rig = Running();
            rig.Goals.Record(Goals.Upgrades, 500);

            Assert.That(rig.Festival.TaskProgress(0), Is.Zero);
            rig.Goals.Record(Goals.Upgrades, 4);
            Assert.That(rig.Festival.TaskProgress(0), Is.EqualTo(4));
        }

        [Test]
        public void CompletedTaskEarnsTokensBeforeRewardIsClaimed()
        {
            Rig rig = Running();
            Assert.That(rig.Festival.TaskProgress(0), Is.Zero);
            rig.Goals.Record(Goals.Upgrades, 10);

            Assert.That(rig.Festival.TaskDone(0), Is.True);
            Assert.That(rig.Festival.TokensEarned, Is.EqualTo(30));
            Assert.That(rig.Festival.TaskClaimed(0), Is.False);
            Assert.That(rig.Festival.CanClaimFreeTier(0), Is.True);
        }

        [Test]
        public void TaskAndTierClaimsAreIdempotent()
        {
            Rig rig = Running();
            rig.Festival.TaskProgress(0);
            rig.Goals.Record(Goals.Upgrades, 10);
            long before = rig.Data.wallet.gems;

            Assert.That(rig.Festival.ClaimTask(0), Is.True);
            Assert.That(rig.Festival.ClaimTask(0), Is.False);
            Assert.That(rig.Festival.ClaimFreeTier(0), Is.True);
            Assert.That(rig.Festival.ClaimFreeTier(0), Is.False);
            Assert.That(rig.Data.wallet.gems - before, Is.EqualTo(45));
        }

        [Test]
        public void CatalogueCannotOverspendOrPayTwice()
        {
            Rig rig = Running();
            rig.Festival.TaskProgress(0);
            rig.Goals.Record(Goals.Upgrades, 10);

            Assert.That(rig.Festival.TokenBalance, Is.EqualTo(30));
            Assert.That(rig.Festival.Redeem(0), Is.False);

            rig.Goals.Record(Goals.Contracts, 3);
            Assert.That(rig.Festival.TokenBalance, Is.EqualTo(70));
            Assert.That(rig.Festival.Redeem(0), Is.True);
            Assert.That(rig.Festival.TokenBalance, Is.EqualTo(30));
            Assert.That(rig.Festival.Redeem(0), Is.False);
        }

        [Test]
        public void PremiumTierRequiresExistingEntitlement()
        {
            HarborFestival.Tuning tuning = HarborFestival.Tuning.Default;
            tuning.PremiumSku = "harbor_pass_2026";
            Rig rig = Running(tuning);
            rig.Festival.TaskProgress(0);
            rig.Goals.Record(Goals.Upgrades, 10);

            Assert.That(rig.Festival.CanClaimPremiumTier(0), Is.False);
            rig.Data.purchasedOffers.Add(tuning.PremiumSku);
            Assert.That(rig.Festival.CanClaimPremiumTier(0), Is.True);
            Assert.That(rig.Festival.ClaimPremiumTier(0), Is.True);
            Assert.That(rig.Festival.ClaimPremiumTier(0), Is.False);
        }

        [Test]
        public void ClosedEventConvertsOnlyUnspentTokensOnce()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var data = new SaveData();
            HarborFestival.Tuning tuning = HarborFestival.Tuning.Default;
            data.liveEvents.Add(new LiveEventState
            {
                id = "harbor-2026",
                configVersion = 1,
                progress = new long[HarborFestival.Slots],
                claimed = new bool[HarborFestival.Slots],
            });
            data.liveEvents[0].progress[HarborFestival.TaskSlot(0)] = tuning.Tasks[0].Target;
            var wallet = new WalletService(data.wallet);
            var events = new LiveEventService(data,
                new List<LiveEvents.Definition> { Definition(now - 2 * Day, now - Day) });
            var festival = new HarborFestivalService(events, null, wallet, tuning, data: data);

            Assert.That(festival.ExpiryGems, Is.EqualTo(3));
            Assert.That(festival.ClaimExpiryConversion(), Is.True);
            Assert.That(festival.ClaimExpiryConversion(), Is.False);
            Assert.That(data.wallet.gems, Is.EqualTo(3));
            Assert.That(festival.Redeem(0), Is.False);
        }

        [Test]
        public void UndersizedScheduleRowIsUnavailable()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            LiveEvents.Definition definition = Definition(now - Day, now + Day);
            definition.Slots = HarborFestival.Slots - 1;
            var data = new SaveData();
            var events = new LiveEventService(data, new List<LiveEvents.Definition> { definition });
            var festival = new HarborFestivalService(events, null, new WalletService(data.wallet),
                HarborFestival.Tuning.Default, data: data);

            Assert.That(festival.Available, Is.False);
            Assert.That(festival.PendingCount(), Is.Zero);
        }
    }
}
