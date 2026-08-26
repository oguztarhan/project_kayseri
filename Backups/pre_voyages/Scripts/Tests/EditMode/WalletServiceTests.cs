using NUnit.Framework;
using Game.Core;
using Game.Systems;

namespace Game.Tests
{
    public class WalletServiceTests
    {
        [Test]
        public void AddAndSpendCash()
        {
            var w = new WalletService(new WalletData());
            w.AddCash(new BigDouble(100d));
            Assert.IsTrue(w.TrySpendCash(new BigDouble(40d)));
            Assert.That(w.Cash.ToDouble(), Is.EqualTo(60d).Within(1e-6));
        }

        [Test]
        public void CannotOverspend()
        {
            var w = new WalletService(new WalletData());
            w.AddCash(new BigDouble(10d));
            Assert.IsFalse(w.TrySpendCash(new BigDouble(11d)));
            Assert.That(w.Cash.ToDouble(), Is.EqualTo(10d).Within(1e-6));
        }

        [Test]
        public void Gems()
        {
            var w = new WalletService(new WalletData());
            w.AddGems(5);
            Assert.IsFalse(w.TrySpendGems(6));
            Assert.IsTrue(w.TrySpendGems(5));
            Assert.AreEqual(0L, w.Gems);
        }
    }
}
