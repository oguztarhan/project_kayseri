using NUnit.Framework;
using Game.Core;
using Game.Systems;

namespace Game.Tests
{
    public class GoalsTests
    {
        // ---- the invariant the daily selection rests on -------------------------------------------

        [Test]
        public void DailyTasksAreAlwaysDistinct()
        {
            // DailyIndex walks the pool in a fixed stride from a scrambled start, which only yields
            // distinct slots because the pool length is prime. This is the test that catches someone
            // growing the pool to a composite length — see Goals.DailyIndex.
            for (int day = -4000; day < 20000; day++)
            {
                int a = Goals.DailyIndex(day, 0);
                int b = Goals.DailyIndex(day, 1);
                int c = Goals.DailyIndex(day, 2);
                Assert.That(a, Is.Not.EqualTo(b), "day " + day);
                Assert.That(a, Is.Not.EqualTo(c), "day " + day);
                Assert.That(b, Is.Not.EqualTo(c), "day " + day);
            }
        }

        [Test]
        public void DailyPoolLengthIsPrime()
        {
            int n = Goals.DailyPool.Length;
            Assert.That(n, Is.GreaterThanOrEqualTo(Goals.DailySlots));
            for (int d = 2; d * d <= n; d++)
                Assert.That(n % d, Is.Not.Zero, "pool length " + n + " is divisible by " + d
                                                + " — DailyIndex can now repeat a task within a day");
        }

        [Test]
        public void DailyIndex_IsAlwaysInRange()
        {
            for (int day = -1000; day < 5000; day++)
                for (int slot = 0; slot < Goals.DailySlots; slot++)
                    Assert.That(Goals.DailyIndex(day, slot),
                                Is.InRange(0, Goals.DailyPool.Length - 1));
        }

        [Test]
        public void DailyTasks_AreTheSameForTheSameDay()
        {
            // Nothing about the selection is saved, so this is what makes the day stable across a
            // restart — and identical on two devices on the same date.
            for (int slot = 0; slot < Goals.DailySlots; slot++)
            {
                Goals.Task first = Goals.DailyTask(20345, slot);
                Goals.Task again = Goals.DailyTask(20345, slot);
                Assert.That(again.Metric, Is.EqualTo(first.Metric));
                Assert.That(again.Target, Is.EqualTo(first.Target));
            }
        }

        [Test]
        public void DailyTasks_ChangeFromDayToDay()
        {
            int same = 0;
            for (int day = 0; day < 200; day++)
                if (Goals.DailyIndex(day, 0) == Goals.DailyIndex(day + 1, 0)) same++;
            Assert.That(same, Is.LessThan(80), "the first slot barely moves between days");
        }

        [Test]
        public void DailyTasks_NeverUseAnInflatingMetric()
        {
            // Cash and bars multiply by 3.2x per ore tier. A fixed daily target in either is a wall on
            // coal and free on diamond, which is why the pool is count-based only.
            for (int i = 0; i < Goals.DailyPool.Length; i++)
                Assert.That(Goals.DailyPool[i].Metric, Is.Not.EqualTo(Goals.BarsSold),
                            "pool entry " + i + " counts bars");
        }

        [Test]
        public void EveryDailyTask_PaysSomething()
        {
            for (int i = 0; i < Goals.DailyPool.Length; i++)
            {
                Goals.Task t = Goals.DailyPool[i];
                Assert.That(t.Target, Is.GreaterThan(0L), "entry " + i);
                Assert.That(t.Gems + t.Cards, Is.GreaterThan(0L), "entry " + i + " pays nothing");
            }
        }

        // ---- the achievement ladder ----------------------------------------------------------------

        [Test]
        public void LadderTiers_Ascend()
        {
            for (int i = 0; i < Goals.Ladder.Length; i++)
            {
                long[] tiers = Goals.Ladder[i].Tiers;
                Assert.That(tiers, Is.Not.Null.And.Not.Empty, "ladder " + i);
                for (int t = 1; t < tiers.Length; t++)
                    Assert.That(tiers[t], Is.GreaterThan(tiers[t - 1]), "ladder " + i + " tier " + t);
            }
        }

        [Test]
        public void TiersReached_CountsWhatIsPassed()
        {
            var a = Goals.Ladder[0];
            Assert.That(Goals.TiersReached(a, 0L), Is.Zero);
            Assert.That(Goals.TiersReached(a, a.Tiers[0]), Is.EqualTo(1));
            Assert.That(Goals.TiersReached(a, a.Tiers[a.Tiers.Length - 1]), Is.EqualTo(a.Tiers.Length));
            Assert.That(Goals.TiersReached(a, long.MaxValue), Is.EqualTo(a.Tiers.Length));
        }

        [Test]
        public void NextTier_IsZeroOnceFinished()
        {
            var a = Goals.Ladder[0];
            Assert.That(Goals.NextTier(a, 0L), Is.EqualTo(a.Tiers[0]));
            Assert.That(Goals.NextTier(a, long.MaxValue), Is.Zero);
        }

        [Test]
        public void LaterTiers_PayMore()
        {
            var a = Goals.Ladder[0];
            Assert.That(Goals.TierGems(a, 2), Is.GreaterThan(Goals.TierGems(a, 1)));
            Assert.That(Goals.TierGems(a, 0), Is.Zero);
        }

        [Test]
        public void EveryLadderMetric_IsReal()
        {
            for (int i = 0; i < Goals.Ladder.Length; i++)
                Assert.That(Goals.Ladder[i].Metric, Is.InRange(0, Goals.MetricCount - 1));
        }

        // ---- progress -------------------------------------------------------------------------------

        [Test]
        public void Progress_IsClamped()
        {
            Assert.That(Goals.Progress(-5L, 10L), Is.EqualTo(0f));
            Assert.That(Goals.Progress(5L, 10L), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(Goals.Progress(50L, 10L), Is.EqualTo(1f));
        }

        [Test]
        public void Progress_TreatsAZeroTargetAsDone()
        {
            // A misconfigured task must read as finished rather than divide by nothing.
            Assert.That(Goals.Progress(0L, 0L), Is.EqualTo(1f));
        }

        [Test]
        public void DayNumber_RollsAtUtcMidnight()
        {
            Assert.That(Goals.DayNumber(0L), Is.Zero);
            Assert.That(Goals.DayNumber(86399L), Is.Zero);
            Assert.That(Goals.DayNumber(86400L), Is.EqualTo(1));
            // and it must not go wrong before 1970, because a device clock can be set backwards
            Assert.That(Goals.DayNumber(-1L), Is.EqualTo(-1));
        }

        [Test]
        public void WeekNumber_RollsAtMondayUtc()
        {
            // 1970-01-01 was Thursday; the first boundary after the epoch is Monday 1970-01-05.
            Assert.That(Goals.WeekNumber(0L), Is.Zero);
            Assert.That(Goals.WeekNumber(345599L), Is.Zero);
            Assert.That(Goals.WeekNumber(345600L), Is.EqualTo(1));
            Assert.That(Goals.WeekNumber(-259201L), Is.EqualTo(-1));
        }

        [Test]
        public void WeeklyDefinitions_HaveStableUniqueIdsAndAscendingMilestones()
        {
            var ids = new System.Collections.Generic.HashSet<string>();
            Assert.That(Goals.WeeklyTasks.Length, Is.EqualTo(Goals.WeeklySlots));
            for (int i = 0; i < Goals.WeeklyTasks.Length; i++)
            {
                Goals.WeeklyTask task = Goals.WeeklyTasks[i];
                Assert.That(task.Id, Is.Not.Null.And.Not.Empty);
                Assert.That(ids.Add(task.Id), Is.True, task.Id);
                Assert.That(task.Metric, Is.InRange(0, Goals.MetricCount - 1));
                Assert.That(task.Target, Is.GreaterThan(0L));
                Assert.That(task.Points, Is.GreaterThan(0));
            }

            int lastPoints = 0;
            for (int i = 0; i < Goals.WeeklyMilestones.Length; i++)
            {
                Goals.WeeklyMilestone milestone = Goals.WeeklyMilestones[i];
                Assert.That(milestone.Id, Is.Not.Null.And.Not.Empty);
                Assert.That(ids.Add(milestone.Id), Is.True, milestone.Id);
                Assert.That(milestone.Points, Is.GreaterThan(lastPoints));
                Assert.That(milestone.Gems + milestone.Cards, Is.GreaterThan(0L));
                lastPoints = milestone.Points;
            }
        }

        [Test]
        public void WeeklyProgress_UsesLifetimeDeltaAndOldSavesStartClean()
        {
            var data = new SaveData();
            data.goals.lifetime[Goals.Upgrades] = 900L;
            data.goals.weekBaseline = null;
            data.goals.weeklyMilestonesClaimed = null;

            var service = new GoalService(data, new WalletService(data.wallet), null, new TimeService());

            Assert.That(service.WeekProgress(Goals.Upgrades), Is.Zero);
            service.Record(Goals.Upgrades, 12L);
            Assert.That(service.WeekProgress(Goals.Upgrades), Is.EqualTo(12L));
            Assert.That(data.goals.weekBaseline.Length, Is.EqualTo(Goals.MetricCount));
            Assert.That(data.goals.weeklyMilestonesClaimed, Is.Not.Null);
        }

        [Test]
        public void WeeklyClaim_IsIdempotentAcrossRepeatedTaps()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var service = new GoalService(data, wallet, null, new TimeService());
            Goals.WeeklyTask task = Goals.WeeklyTasks[0];
            service.Record(task.Metric, task.Target);

            long before = wallet.Gems;
            Assert.That(service.ClaimWeeklyMilestone(0, out GoalService.ClaimReceipt receipt), Is.True);
            Assert.That(receipt.Items, Is.EqualTo(1));
            Assert.That(receipt.Gems, Is.EqualTo(Goals.WeeklyMilestones[0].Gems));
            Assert.That(receipt.Cards, Is.EqualTo(Goals.WeeklyMilestones[0].Cards));
            long afterFirst = wallet.Gems;
            Assert.That(afterFirst, Is.EqualTo(before + Goals.WeeklyMilestones[0].Gems));
            Assert.That(service.ClaimWeeklyMilestone(0), Is.False);
            Assert.That(wallet.Gems, Is.EqualTo(afterFirst));
        }

        [Test]
        public void WeeklyRollover_DropsPartialProgressAndClaims()
        {
            var data = new SaveData();
            data.goals.week = Goals.WeekNumber(System.DateTimeOffset.UtcNow.ToUnixTimeSeconds()) - 1;
            data.goals.lifetime[Goals.Upgrades] = 40L;
            data.goals.weekBaseline[Goals.Upgrades] = 10L;
            data.goals.weeklyMilestonesClaimed = new[] { Goals.WeeklyMilestones[0].Id };

            var service = new GoalService(data, new WalletService(data.wallet), null, new TimeService());

            Assert.That(service.WeekProgress(Goals.Upgrades), Is.Zero);
            Assert.That(service.WeeklyMilestoneClaimed(0), Is.False);
        }

        [Test]
        public void ClaimAll_TakesEveryReadyKindExactlyOnce()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var service = new GoalService(data, wallet, null, new TimeService());
            for (int metric = 0; metric < Goals.MetricCount; metric++)
                service.Record(metric, 30000000L);

            int expected = Goals.DailySlots + Goals.WeeklyMilestones.Length + Goals.Ladder.Length;
            Assert.That(service.ClaimAll(), Is.EqualTo(expected));
            long afterFirst = wallet.Gems;

            Assert.That(service.ClaimAll(), Is.Zero);
            Assert.That(wallet.Gems, Is.EqualTo(afterFirst));
            for (int i = 0; i < Goals.WeeklyMilestones.Length; i++)
                Assert.That(service.WeeklyMilestoneClaimed(i), Is.True);
        }
    }
}
