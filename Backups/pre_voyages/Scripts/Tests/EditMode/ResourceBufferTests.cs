using NUnit.Framework;
using Game.Core;

namespace Game.Tests
{
    public class ResourceBufferTests
    {
        [Test]
        public void Add_ClampsToCapacity()
        {
            var b = new ResourceBuffer(new BigDouble(100d));
            var accepted = b.Add(new BigDouble(150d));
            Assert.That(accepted.ToDouble(), Is.EqualTo(100d).Within(1e-6));
            Assert.IsTrue(b.IsFull);
        }

        [Test]
        public void Remove_ClampsToAmount()
        {
            var b = new ResourceBuffer(new BigDouble(100d));
            b.Add(new BigDouble(30d));
            var removed = b.Remove(new BigDouble(50d));
            Assert.That(removed.ToDouble(), Is.EqualTo(30d).Within(1e-6));
            Assert.IsTrue(b.IsEmpty);
        }

        [Test]
        public void PartialAdd_ReturnsAcceptedSpace()
        {
            var b = new ResourceBuffer(new BigDouble(100d));
            b.Add(new BigDouble(80d));
            var accepted = b.Add(new BigDouble(50d));
            Assert.That(accepted.ToDouble(), Is.EqualTo(20d).Within(1e-6));
            Assert.That(b.Amount.ToDouble(), Is.EqualTo(100d).Within(1e-6));
        }
    }
}
