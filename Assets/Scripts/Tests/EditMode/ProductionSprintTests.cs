using System;
using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Systems;
using NUnit.Framework;
using UnityEditor;

namespace Game.Tests
{
    public sealed class ProductionSprintTests
    {
        private const long Day = 86400L;

        private static LiveEvents.Definition Definition(long start, long end, int version = 1)
            => new LiveEvents.Definition
            {
                Id = "production-sprint-2026-01",
                Kind = ProductionSprint.Kind,
                StartUnix = start,
                EndUnix = end,
                ConfigVersion = version,
                Slots = ProductionSprint.Slots,
                MinIslands = 0,
            };

        private sealed class Rig
        {
            public SaveData Data;
            public GoalService Goals;
            public ProductionSprintService Sprint;
        }

        private static Rig Running(ProductionSprint.Tuning? tuning = null)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var goals = new GoalService(data, wallet);
            var events = new LiveEventService(data,
                new List<LiveEvents.Definition> { Definition(now - Day, now + Day) });
            var sprint = new ProductionSprintService(events, goals, wallet,
                tuning ?? ProductionSprint.Tuning.Default, data: data);
            return new Rig { Data = data, Goals = goals, Sprint = sprint };
        }

        [Test]
        public void ShippedTuningAndSlotMapAreValid()
        {
            ProductionSprint.Tuning tuning = ProductionSprint.Tuning.Default;
            Assert.That(ProductionSprint.IsWellFormed(tuning), Is.True);
            Assert.That(ProductionSprint.MaximumScore(tuning), Is.EqualTo(489L));
            Assert.That(ProductionSprint.Slots, Is.EqualTo(15));
            Assert.That(ProductionSprint.Slots, Is.LessThanOrEqualTo(LiveEvents.MaxSlots));

            Assert.That(tuning.Milestones[0].Score, Is.EqualTo(40L));
            Assert.That(tuning.Milestones[0].Reward.Gems, Is.EqualTo(10L));
            Assert.That(tuning.Milestones[0].Reward.CashMinutes, Is.EqualTo(5d));
            Assert.That(tuning.Milestones[1].Score, Is.EqualTo(100L));
            Assert.That(tuning.Milestones[1].Reward.Gems, Is.EqualTo(20L));
            Assert.That(tuning.Milestones[1].Reward.CashMinutes, Is.EqualTo(15d));
            Assert.That(tuning.Milestones[2].Reward.Gems, Is.EqualTo(30L));
            Assert.That(tuning.Milestones[2].Reward.Cards, Is.EqualTo(1));
            Assert.That(tuning.Milestones[3].Reward.Gems, Is.EqualTo(50L));
            Assert.That(tuning.Milestones[3].Reward.Cards, Is.EqualTo(2));
            Assert.That(tuning.Milestones[4].Reward.Gems, Is.EqualTo(100L));
            Assert.That(tuning.Milestones[4].Reward.Cards, Is.EqualTo(3));
        }

        [Test]
        public void ConstructorSeedsBaselineWithoutRetroactiveScore()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var goals = new GoalService(data, wallet);
            goals.Record(Goals.Upgrades, 500);
            var events = new LiveEventService(data,
                new List<LiveEvents.Definition> { Definition(now - Day, now + Day) });
            var sprint = new ProductionSprintService(events, goals, wallet,
                ProductionSprint.Tuning.Default, data: data);

            Assert.That(sprint.Score, Is.Zero);
            goals.Record(Goals.Upgrades, 3);
            Assert.That(sprint.RuleProgress(0), Is.EqualTo(3));
            Assert.That(sprint.Score, Is.EqualTo(9));
        }

        [Test]
        public void ExplicitRulesWeightActionsAndStopAtTheirCaps()
        {
            Rig rig = Running();
            rig.Goals.Record(Goals.Upgrades, 80);
            rig.Goals.Record(Goals.Contracts, 2);

            Assert.That(rig.Sprint.RuleProgress(0), Is.EqualTo(40));
            Assert.That(rig.Sprint.RuleProgress(1), Is.EqualTo(2));
            Assert.That(rig.Sprint.Score, Is.EqualTo(156));
        }

        [Test]
        public void PersonalMilestoneClaimsOnceAndSurvivesClose()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var data = new SaveData();
            data.liveEvents.Add(new LiveEventState
            {
                id = "production-sprint-2026-01",
                configVersion = 1,
                progress = new long[ProductionSprint.Slots],
                claimed = new bool[ProductionSprint.Slots],
            });
            data.liveEvents[0].progress[ProductionSprint.RuleSlot(0)] = 14;
            data.incomeRatePerSec = 100d;
            var wallet = new WalletService(data.wallet);
            var events = new LiveEventService(data,
                new List<LiveEvents.Definition> { Definition(now - 2 * Day, now - Day) });
            var sprint = new ProductionSprintService(events, null, wallet,
                ProductionSprint.Tuning.Default, data: data);

            Assert.That(sprint.Score, Is.EqualTo(42));
            Assert.That(sprint.PendingCount(), Is.EqualTo(1));
            Assert.That(sprint.ClaimMilestone(0), Is.True);
            Assert.That(sprint.ClaimMilestone(0), Is.False);
            Assert.That(data.wallet.gems, Is.EqualTo(10));
            Assert.That(data.wallet.cash.ToDouble(), Is.EqualTo(30000d).Within(0.001d));
        }

        [Test]
        public void RankingStaysDisabledWithShippingStub()
        {
            Rig rig = Running();
            rig.Goals.Record(Goals.Upgrades, 30);

            Assert.That(rig.Sprint.RankingAvailable, Is.False);
            Assert.That(rig.Sprint.Score, Is.EqualTo(90));
        }

        [Test]
        public void UndersizedScheduleRowIsUnavailable()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            LiveEvents.Definition definition = Definition(now - Day, now + Day);
            definition.Slots = ProductionSprint.Slots - 1;
            var data = new SaveData();
            var events = new LiveEventService(data, new List<LiveEvents.Definition> { definition });
            var sprint = new ProductionSprintService(events, null, new WalletService(data.wallet),
                ProductionSprint.Tuning.Default, data: data);

            Assert.That(sprint.Available, Is.False);
            Assert.That(sprint.PendingCount(), Is.Zero);
        }

        [Test]
        public void MalformedTuningFallsBackToShippedBalance()
        {
            ProductionSprint.Tuning malformed = ProductionSprint.Tuning.Default;
            malformed.Milestones = new ProductionSprint.Milestone[0];
            Rig rig = Running(malformed);
            rig.Goals.Record(Goals.Upgrades, 25);

            Assert.That(rig.Sprint.Score, Is.EqualTo(75));
            Assert.That(rig.Sprint.CanClaimMilestone(0), Is.True);
        }

        [Test]
        public void AuthoredScheduleMatchesTheApprovedWindowAndChapterGate()
        {
            LiveEventConfig config = AssetDatabase.LoadAssetAtPath<LiveEventConfig>(
                "Assets/Data/LiveEventConfig.asset");
            Assert.That(config, Is.Not.Null);

            List<LiveEvents.Definition> definitions = config.Definitions();
            LiveEvents.Definition sprint = definitions.Find(d => d.Kind == ProductionSprint.Kind);
            long expectedStart = new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero)
                .ToUnixTimeSeconds();

            Assert.That(sprint.Id, Is.EqualTo("production_sprint_2026_09"));
            Assert.That(sprint.StartUnix, Is.EqualTo(expectedStart));
            Assert.That(sprint.EndUnix, Is.EqualTo(expectedStart + 3L * Day));
            Assert.That(sprint.MinCompletedChapters, Is.EqualTo(1));
            Assert.That(sprint.Slots, Is.GreaterThanOrEqualTo(ProductionSprint.Slots));
        }
    }
}
