using NUnit.Framework;
using Game.Core;

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
    }
}
