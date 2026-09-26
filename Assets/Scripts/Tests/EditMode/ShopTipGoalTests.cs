using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Systems;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>The tips achievement: counted beside the goal metrics, never among them.</summary>
    public sealed class ShopTipGoalTests
    {
        private const string BusinessId = "mining-shop.island-01-04";

        private static int TipsIndex()
        {
            for (int i = 0; i < Goals.Ladder.Length; i++) if (Goals.Ladder[i].Metric == Goals.Tips) return i;
            return -1;
        }

        [Test]
        public void TipsAreTheLastAchievementWithTheApprovedTiersAndRewards()
        {
            int index = TipsIndex();
            Assert.That(index, Is.EqualTo(Goals.Ladder.Length - 1), "appended: tiersClaimed is saved by position");
            Goals.Achievement a = Goals.Ladder[index];
            CollectionAssert.AreEqual(new[] { 10L, 50L, 250L, 1000L, 3000L, 8000L }, a.Tiers);
            Assert.That(Goals.TierGems(a, 1), Is.EqualTo(5L));
            Assert.That(Goals.TierGems(a, 3), Is.EqualTo(15L));
            Assert.That(Goals.TierCards(a, 2), Is.EqualTo(1));
            Assert.That(Goals.TierPacks(a, 2), Is.Zero);
            Assert.That(Goals.TierPacks(a, 3), Is.EqualTo(2));
        }

        [Test]
        public void TheTipsMetricLeavesEverySlotLayoutSizedByTheCountedMetrics()
        {
            Assert.That(Goals.MetricCount, Is.EqualTo(6), "live events, the sprint, the pass and the league size saves by it");
            Assert.That(Goals.Tips, Is.GreaterThanOrEqualTo(Goals.MetricCount));

            // The authored event rows carry exact slot counts; each service refuses a row smaller than its layout.
            var config = AssetDatabase.LoadAssetAtPath<LiveEventConfig>("Assets/Data/LiveEventConfig.asset");
            Assert.That(config, Is.Not.Null);
            List<LiveEvents.Definition> definitions = config.Definitions();
            int checkedRows = 0;
            foreach (LiveEvents.Definition d in definitions)
            {
                int layout = d.Kind == FoundryFestival.Kind ? FoundryFestival.Slots
                           : d.Kind == HarborFestival.Kind ? HarborFestival.Slots
                           : d.Kind == ProductionSprint.Kind ? ProductionSprint.Slots
                           : d.Kind == SeasonalIndustryPass.Kind ? SeasonalIndustryPass.Slots
                           : -1;
                if (layout < 0) continue;
                Assert.That(d.Slots, Is.GreaterThanOrEqualTo(layout), d.Id);
                checkedRows++;
            }
            Assert.That(checkedRows, Is.EqualTo(4));

            var data = new SaveData();
            var goals = new GoalService(data, new WalletService(data.wallet), null, new TimeService());
            goals.Record(Goals.Tips, 7L);
            Assert.That(data.goals.lifetime.Length, Is.EqualTo(Goals.MetricCount));
            Assert.That(data.goals.dayBaseline.Length, Is.EqualTo(Goals.MetricCount));
            Assert.That(data.goals.weekBaseline.Length, Is.EqualTo(Goals.MetricCount));
            for (int m = 0; m < Goals.MetricCount; m++) Assert.That(goals.Lifetime(m), Is.Zero, "metric " + m);
            Assert.That(goals.Lifetime(Goals.Tips), Is.EqualTo(7L));
            Assert.That(data.goals.tipsLifetime, Is.EqualTo(7L));
            Assert.That(goals.TodayProgress(Goals.Tips), Is.Zero, "no daily reads tips");
        }

        [Test]
        public void EveryShopTipCountsOnceForTheAchievement()
        {
            var campaignConfig = ScriptableObject.CreateInstance<MiningShopCampaignConfig>();
            try
            {
                var data = new SaveData();
                var wallet = new WalletService(data.wallet);
                var goals = new GoalService(data, wallet, null, new TimeService());
                var market = new MarketService(data, wallet, null, goals: goals);
                MiningShopBusinessSimulation.Tuning tuning = MiningShopBusinessSimulation.Tuning.Default;
                tuning.Tips.BaseChance = 1d;
                tuning.Tips.MaxChance = 1d;
                market.OpenMiningShopBusiness(campaignConfig.CreateCampaign(), BusinessId, tuning);
                for (int i = 0; i < 600; i++) market.Tick(1f);

                long tips = data.miningShopBusinesses[0].Business.TipCount;
                Assert.That(tips, Is.GreaterThan(5L));
                Assert.That(goals.Lifetime(Goals.Tips), Is.EqualTo(tips));
            }
            finally { Object.DestroyImmediate(campaignConfig); }
        }

        [Test]
        public void ReachedTiersClaimOnceAndPay()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var goals = new GoalService(data, wallet, null, new TimeService());
            int index = TipsIndex();
            goals.Record(Goals.Tips, 9L);
            Assert.That(goals.UnclaimedTiers(index), Is.Zero);
            goals.Record(Goals.Tips, 41L);
            Assert.That(goals.UnclaimedTiers(index), Is.EqualTo(2));
            Assert.That(goals.PendingCount(), Is.GreaterThanOrEqualTo(1));

            long before = wallet.Gems;
            Assert.That(goals.ClaimAchievement(index, out GoalService.ClaimReceipt receipt), Is.True);
            Assert.That(receipt.Gems, Is.EqualTo(5L + 10L));
            Assert.That(receipt.Cards, Is.EqualTo(2));
            Assert.That(wallet.Gems - before, Is.EqualTo(15L));
            Assert.That(goals.ClaimAchievement(index), Is.False, "nothing twice");
        }

        [Test]
        public void ASaveFromBeforeTipsKeepsItsClaimsAndStartsTheTipsLadderAtZero()
        {
            var data = new SaveData();
            data.goals.tiersClaimed = new[] { 1, 2, 3, 4, 5, 6 };   // six achievements, as saved before tips
            var json = JsonUtility.ToJson(data);
            json = json.Replace(",\"tipsLifetime\":0", string.Empty);
            StringAssert.DoesNotContain("tipsLifetime", json);
            var old = JsonUtility.FromJson<SaveData>(json);

            var goals = new GoalService(old, new WalletService(old.wallet), null, new TimeService());
            Assert.That(goals.Lifetime(Goals.Tips), Is.Zero);
            Assert.That(old.goals.tiersClaimed.Length, Is.EqualTo(Goals.Ladder.Length));
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6, 0 }, old.goals.tiersClaimed);
        }
    }
}
