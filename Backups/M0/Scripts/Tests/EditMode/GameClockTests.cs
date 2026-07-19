using NUnit.Framework;
using Game.Core;

namespace Game.Tests
{
    public class GameClockTests
    {
        [Test]
        public void FiresExpectedTicks()
        {
            var clock = new GameClock(10f); // 0.1s per tick
            int ticks = 0;
            clock.OnTick += () => ticks++;

            clock.Advance(0.35f); // 3 whole ticks, ~0.05 remainder
            Assert.AreEqual(3, ticks);

            clock.Advance(0.06f); // remainder ~0.11 -> 1 more tick
            Assert.AreEqual(4, ticks);
        }

        [Test]
        public void Paused_DoesNotTick()
        {
            var clock = new GameClock(10f);
            int ticks = 0;
            clock.OnTick += () => ticks++;
            clock.Paused = true;

            clock.Advance(1f);

            Assert.AreEqual(0, ticks);
        }

        [Test]
        public void ClampsBacklogToMax()
        {
            var clock = new GameClock(10f);
            int ticks = 0;
            clock.OnTick += () => ticks++;

            clock.Advance(100f); // would be 1000 ticks; clamped to 8

            Assert.AreEqual(8, ticks);
        }

        [Test]
        public void TickCount_Accumulates()
        {
            var clock = new GameClock(4f); // 0.25s per tick
            clock.Advance(0.75f);
            Assert.AreEqual(3L, clock.TickCount);
        }
    }
}
