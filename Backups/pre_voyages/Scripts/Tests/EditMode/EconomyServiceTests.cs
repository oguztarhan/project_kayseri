using NUnit.Framework;
using Game.Systems;

namespace Game.Tests
{
    public class EconomyServiceTests
    {
        [Test]
        public void UpgradeCost_GrowsGeometrically()
        {
            var e = new EconomyService(costGrowth: 1.1d, tierValueMultiplier: 3d);
            Assert.That(e.UpgradeCost(10d, 0).ToDouble(), Is.EqualTo(10d).Within(1e-6));
            Assert.That(e.UpgradeCost(10d, 1).ToDouble(), Is.EqualTo(11d).Within(1e-6));
            Assert.That(e.UpgradeCost(10d, 2).ToDouble(), Is.EqualTo(12.1d).Within(1e-4));
        }

        [Test]
        public void TierValue_Scales()
        {
            var e = new EconomyService(1.1d, 3d);
            Assert.That(e.TierValue(0), Is.EqualTo(1d).Within(1e-9));
            Assert.That(e.TierValue(2), Is.EqualTo(9d).Within(1e-9));
        }
    }
}
