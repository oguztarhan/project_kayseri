using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class HarbourLoadingTests
    {
        [Test]
        public void CargoWaitsUntilShipIsMoored()
        {
            var loading = new HarbourLoading(14);
            loading.Enqueue(); loading.Tick(100, false);
            Assert.AreEqual(1, loading.Waiting);
            Assert.AreEqual(0, loading.Loaded);
            Assert.IsFalse(loading.Active);
            loading.Tick(14, true);
            Assert.AreEqual(1, loading.Loaded);
        }
        [Test]
        public void DepartureReturnsUnfinishedParcelWithoutDuplication()
        {
            var loading = new HarbourLoading(14);
            loading.Enqueue(); loading.Tick(8, true);
            loading.Tick(1, false); loading.Tick(1, false);
            Assert.AreEqual(1, loading.Waiting);
            Assert.AreEqual(0, loading.Loaded);
            Assert.IsFalse(loading.Active);
            loading.Tick(6, true);
            Assert.AreEqual(0, loading.Loaded);
            loading.Tick(8, true);
            Assert.AreEqual(1, loading.Loaded);
        }
        [Test]
        public void QueueIncludesParcelAlreadyOnCrane()
        {
            var loading = new HarbourLoading(14);
            loading.Enqueue(); loading.Tick(1, true);
            for (int i = 0; i < 3; i++) Assert.IsTrue(loading.Enqueue());
            Assert.IsFalse(loading.Enqueue());
            loading.Tick(1, false);
            Assert.AreEqual(HarbourLoading.QueueCapacity, loading.Waiting);
        }
        [Test]
        public void FullDeckWaitsForNextVisit()
        {
            var loading = new HarbourLoading(14);
            for (int i = 0; i < 4; i++) loading.Enqueue();
            loading.Tick(1000, true);
            Assert.AreEqual(3, loading.Loaded);
            Assert.AreEqual(1, loading.Waiting);
            loading.NewVisit(); loading.Tick(14, true);
            Assert.AreEqual(1, loading.Loaded);
            Assert.AreEqual(0, loading.Waiting);
        }
        [Test]
        public void EmptyQuayCannotInventCargo()
        {
            var loading = new HarbourLoading(14);
            loading.Tick(1000, true);
            Assert.AreEqual(0, loading.Loaded);
            Assert.IsFalse(loading.Active);
        }
    }
}
