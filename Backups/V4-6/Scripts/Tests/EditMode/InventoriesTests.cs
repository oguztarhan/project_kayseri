using NUnit.Framework;
using Game.Core;

namespace Game.Tests
{
    public class InventoriesTests
    {
        [Test]
        public void Transfer_MovesUpToMax_AcrossKeys()
        {
            var src = new Inventory<string>(new BigDouble(1000d));
            var dst = new Inventory<string>(new BigDouble(1000d));
            src.Add("coal", new BigDouble(30d));
            src.Add("iron", new BigDouble(30d));

            var moved = Inventories.Transfer(src, dst, new BigDouble(40d));

            Assert.That(moved.ToDouble(), Is.EqualTo(40d).Within(1e-6));
            Assert.That(dst.Total.ToDouble(), Is.EqualTo(40d).Within(1e-6));
            Assert.That(src.Total.ToDouble(), Is.EqualTo(20d).Within(1e-6));
        }

        [Test]
        public void Transfer_LimitedByDestSpace()
        {
            var src = new Inventory<string>(new BigDouble(1000d));
            var dst = new Inventory<string>(new BigDouble(10d));
            src.Add("coal", new BigDouble(100d));

            var moved = Inventories.Transfer(src, dst, new BigDouble(50d));

            Assert.That(moved.ToDouble(), Is.EqualTo(10d).Within(1e-6));
            Assert.IsTrue(dst.IsFull);
        }

        [Test]
        public void Transfer_EmptySource_MovesNothing()
        {
            var src = new Inventory<string>(new BigDouble(100d));
            var dst = new Inventory<string>(new BigDouble(100d));
            var moved = Inventories.Transfer(src, dst, new BigDouble(50d));
            Assert.That(moved.ToDouble(), Is.EqualTo(0d).Within(1e-9));
        }
    }
}
