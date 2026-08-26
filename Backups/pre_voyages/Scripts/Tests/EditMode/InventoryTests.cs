using NUnit.Framework;
using Game.Core;

namespace Game.Tests
{
    public class InventoryTests
    {
        [Test]
        public void AddAndGet_PerKey()
        {
            var inv = new Inventory<string>(new BigDouble(100d));
            inv.Add("coal", new BigDouble(30d));
            inv.Add("iron", new BigDouble(20d));
            Assert.That(inv.Get("coal").ToDouble(), Is.EqualTo(30d).Within(1e-6));
            Assert.That(inv.Get("iron").ToDouble(), Is.EqualTo(20d).Within(1e-6));
            Assert.That(inv.Total.ToDouble(), Is.EqualTo(50d).Within(1e-6));
        }

        [Test]
        public void Capacity_ClampsTotalAcrossKeys()
        {
            var inv = new Inventory<string>(new BigDouble(50d));
            var a = inv.Add("coal", new BigDouble(40d));
            var b = inv.Add("iron", new BigDouble(30d)); // only 10 space left
            Assert.That(a.ToDouble(), Is.EqualTo(40d).Within(1e-6));
            Assert.That(b.ToDouble(), Is.EqualTo(10d).Within(1e-6));
            Assert.IsTrue(inv.IsFull);
        }

        [Test]
        public void Remove_ClampsToHeld()
        {
            var inv = new Inventory<string>(new BigDouble(100d));
            inv.Add("coal", new BigDouble(15d));
            var r = inv.Remove("coal", new BigDouble(50d));
            Assert.That(r.ToDouble(), Is.EqualTo(15d).Within(1e-6));
            Assert.That(inv.Get("coal").ToDouble(), Is.EqualTo(0d).Within(1e-6));
        }
    }
}
