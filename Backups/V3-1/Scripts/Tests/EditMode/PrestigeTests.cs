using NUnit.Framework;
using Game.Core;

namespace Game.Tests
{
    public class PrestigeTests
    {
        [Test]
        public void Investors_IsKTimesSqrt()
        {
            var inv = Prestige.Investors(new BigDouble(10000d), 1d); // sqrt(10000) = 100
            Assert.That(inv.ToDouble(), Is.EqualTo(100d).Within(1e-3));
        }

        [Test]
        public void Investors_ScaledByK()
        {
            var inv = Prestige.Investors(new BigDouble(10000d), 2d); // 2 * 100
            Assert.That(inv.ToDouble(), Is.EqualTo(200d).Within(1e-3));
        }

        [Test]
        public void Investors_ZeroWhenNoLifetime()
        {
            Assert.IsTrue(Prestige.Investors(BigDouble.Zero, 1d).IsZero);
        }

        [Test]
        public void IncomeMultiplier_Scales()
        {
            Assert.That(Prestige.IncomeMultiplier(50d, 0.02d), Is.EqualTo(2d).Within(1e-9)); // 1 + 50*0.02
            Assert.That(Prestige.IncomeMultiplier(0d, 0.02d), Is.EqualTo(1d).Within(1e-9));
        }
    }
}
