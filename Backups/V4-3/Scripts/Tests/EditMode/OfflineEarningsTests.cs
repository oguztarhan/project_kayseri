using NUnit.Framework;
using Game.Core;

namespace Game.Tests
{
    public class OfflineEarningsTests
    {
        [Test]
        public void Normal_RateTimesTimeTimesEfficiency()
        {
            var r = OfflineEarnings.Compute(new BigDouble(10d), 100L, 0.5d, 3600L);
            Assert.That(r.ToDouble(), Is.EqualTo(500d).Within(1e-6)); // 10 * 100 * 0.5
        }

        [Test]
        public void CappedAtCapSeconds()
        {
            var r = OfflineEarnings.Compute(new BigDouble(10d), 100000L, 0.5d, 7200L);
            Assert.That(r.ToDouble(), Is.EqualTo(36000d).Within(1e-3)); // 10 * 7200 * 0.5
        }

        [Test]
        public void Rollback_EarnsNothing()
        {
            var r = OfflineEarnings.Compute(new BigDouble(10d), -50L, 0.5d, 7200L);
            Assert.IsTrue(r.IsZero);
        }
    }
}
