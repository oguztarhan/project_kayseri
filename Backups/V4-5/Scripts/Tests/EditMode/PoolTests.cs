using NUnit.Framework;
using Game.Core;

namespace Game.Tests
{
    public class PoolTests
    {
        private class Box { }

        [Test]
        public void ReusesReturnedInstance()
        {
            int created = 0;
            var pool = new Pool<Box>(() => { created++; return new Box(); });
            var a = pool.Get();
            pool.Return(a);
            var b = pool.Get();
            Assert.AreSame(a, b);
            Assert.AreEqual(1, created);
        }

        [Test]
        public void CreatesWhenEmpty()
        {
            var pool = new Pool<Box>(() => new Box());
            Assert.AreNotSame(pool.Get(), pool.Get());
        }

        [Test]
        public void Prewarm_CreatesInstances()
        {
            var pool = new Pool<Box>(() => new Box(), prewarm: 3);
            Assert.AreEqual(3, pool.CountFree);
        }
    }
}
