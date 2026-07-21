using NUnit.Framework;
using Game.Core;

namespace Game.Tests
{
    public class ServiceLocatorTests
    {
        private interface IFoo { int V { get; } }
        private class Foo : IFoo { public int V => 42; }

        [SetUp]
        public void Reset() => ServiceLocator.Clear();

        [Test]
        public void RegisterAndGet()
        {
            ServiceLocator.Register<IFoo>(new Foo());
            Assert.AreEqual(42, ServiceLocator.Get<IFoo>().V);
        }

        [Test]
        public void Get_Missing_ReturnsNull()
        {
            Assert.IsNull(ServiceLocator.Get<IFoo>());
        }

        [Test]
        public void TryGet_Works()
        {
            Assert.IsFalse(ServiceLocator.TryGet<IFoo>(out _));
            ServiceLocator.Register<IFoo>(new Foo());
            Assert.IsTrue(ServiceLocator.TryGet<IFoo>(out var f));
            Assert.AreEqual(42, f.V);
        }
    }
}
