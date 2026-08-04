using NUnit.Framework;
using Game.Core;

namespace Game.Tests
{
    public class PrestigeTests
    {
        [Test]
        public void Investors_IsKTimesSqrtOfRunsAgainstReference()
        {
            // Four references' worth banked -> sqrt(4) = 2 runs' worth of investors.
            var inv = Prestige.Investors(new BigDouble(40000d), 10d, new BigDouble(10000d));
            Assert.That(inv.ToDouble(), Is.EqualTo(20d).Within(1e-3));
        }

        [Test]
        public void Investors_ScaledByK()
        {
            var inv = Prestige.Investors(new BigDouble(10000d), 2d, new BigDouble(10000d));
            Assert.That(inv.ToDouble(), Is.EqualTo(2d).Within(1e-3));
        }

        [Test]
        public void Investors_ZeroWhenNoLifetime()
        {
            Assert.IsTrue(Prestige.Investors(BigDouble.Zero, 1d, new BigDouble(10000d)).IsZero);
        }

        /// <summary>
        /// The point of the reference: the same run measured against a tier ten times larger
        /// pays out the same, so climbing the ladder does not inflate prestige with it.
        /// </summary>
        [Test]
        public void Investors_AreScaleFreeAcrossTiers()
        {
            var early = Prestige.Investors(new BigDouble(1.1e6d), 10d, new BigDouble(1.1e6d));
            var late = Prestige.Investors(new BigDouble(1.1e6d * 3.2e7d), 10d, new BigDouble(1.1e6d * 3.2e7d));
            Assert.That(late.ToDouble(), Is.EqualTo(early.ToDouble()).Within(1e-6));
            Assert.That(early.ToDouble(), Is.EqualTo(10d).Within(1e-6));
        }

        /// <summary>A well-timed first reset is worth ×2 — not the ×70 the old curve paid.</summary>
        [Test]
        public void FirstResetIsWorthAboutDouble()
        {
            var inv = Prestige.Investors(new BigDouble(1.1e6d), 10d, new BigDouble(1.1e6d));
            Assert.That(Prestige.IncomeMultiplier(inv.ToDouble(), 0.10d), Is.EqualTo(2d).Within(1e-6));
        }

        [Test]
        public void IncomeMultiplier_Scales()
        {
            Assert.That(Prestige.IncomeMultiplier(10d, 0.10d), Is.EqualTo(2d).Within(1e-9));
            Assert.That(Prestige.IncomeMultiplier(0d, 0.10d), Is.EqualTo(1d).Within(1e-9));
        }
    }
}
