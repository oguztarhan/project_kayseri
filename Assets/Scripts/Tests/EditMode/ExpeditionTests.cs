using NUnit.Framework;
using Game.Core;

namespace Game.Tests
{
    /// <summary>
    /// Where a voyage is. The test that matters most is the last group: the sea scene may not change
    /// a voyage, and the way that rule is held is that position is a pure function of the clock — so
    /// these assert that reading it twice, or not at all, cannot move anything.
    /// </summary>
    public class ExpeditionTests
    {
        private const long Sailed = 1_000_000L;
        private const long Returns = 1_003_600L;   // an hour at sea

        // ---- the clock ---------------------------------------------------------------------------

        [Test]
        public void ProgressRunsZeroToOneAcrossTheCrossing()
        {
            Assert.That(Expedition.Progress(Sailed, Returns, Sailed), Is.EqualTo(0d).Within(1e-9));
            Assert.That(Expedition.Progress(Sailed, Returns, Sailed + 1800L), Is.EqualTo(0.5d).Within(1e-9));
            Assert.That(Expedition.Progress(Sailed, Returns, Returns), Is.EqualTo(1d).Within(1e-9));
        }

        [Test]
        public void ProgressIsClampedAtBothEnds()
        {
            Assert.That(Expedition.Progress(Sailed, Returns, Sailed - 9999L), Is.Zero);
            Assert.That(Expedition.Progress(Sailed, Returns, Returns + 9999L), Is.EqualTo(1d));
        }

        [Test]
        public void AShipStillAtTheDockHasNotStarted()
        {
            // sailedUnix 0 is the dock's own "still loading" marker — see VoyageState.
            Assert.That(Expedition.Progress(0L, Returns, Sailed + 100L), Is.Zero);
        }

        [Test]
        public void AVoyageWithNoDurationReadsAsFinished()
        {
            // Not as zero. A bar that cannot fill must never sit empty forever — the same answer
            // Goals.Progress gives a zero target.
            Assert.That(Expedition.Progress(Sailed, Sailed, Sailed), Is.EqualTo(1d));
            Assert.That(Expedition.Progress(Sailed, Sailed - 50L, Sailed), Is.EqualTo(1d));
        }

        [Test]
        public void SecondsLeftNeverGoesNegative()
        {
            Assert.That(Expedition.SecondsLeft(Returns, Sailed), Is.EqualTo(3600d).Within(1e-9));
            Assert.That(Expedition.SecondsLeft(Returns, Returns + 500L), Is.Zero);
        }

        // ---- out and back ------------------------------------------------------------------------

        [Test]
        public void TheLaneIsRunForwardsThenBackwards()
        {
            Assert.That(Expedition.LanePosition(0d), Is.EqualTo(0d).Within(1e-9));
            Assert.That(Expedition.LanePosition(0.25d), Is.EqualTo(0.5d).Within(1e-9));
            Assert.That(Expedition.LanePosition(0.5d), Is.EqualTo(1d).Within(1e-9), "she turns at halfway");
            Assert.That(Expedition.LanePosition(0.75d), Is.EqualTo(0.5d).Within(1e-9));
            Assert.That(Expedition.LanePosition(1d), Is.EqualTo(0d).Within(1e-9), "and comes home");
        }

        [Test]
        public void LanePositionStaysInRangeForAnyProgress()
        {
            for (int i = -50; i <= 150; i++)
                Assert.That(Expedition.LanePosition(i / 100d), Is.InRange(0d, 1d), "progress " + (i / 100d));
        }

        [Test]
        public void OutboundFlipsExactlyAtTheTurn()
        {
            Assert.That(Expedition.Outbound(0.499d), Is.True);
            Assert.That(Expedition.Outbound(0.5d), Is.False);
            Assert.That(Expedition.Outbound(1d), Is.False);
        }

        // ---- the lane's shape --------------------------------------------------------------------

        [Test]
        public void BothPortsSitOnTheAxis()
        {
            Expedition.PointOnLane(0d, 900d, 110d, out double x0, out double z0);
            Expedition.PointOnLane(1d, 900d, 110d, out double x1, out double z1);
            Assert.That(x0, Is.EqualTo(0d).Within(1e-9));
            Assert.That(z0, Is.EqualTo(0d).Within(1e-9));
            Assert.That(x1, Is.EqualTo(900d).Within(1e-9));
            Assert.That(z1, Is.EqualTo(0d).Within(1e-6), "the far port must not drift off the axis");
        }

        [Test]
        public void TheLaneActuallyBends()
        {
            // A straight line gives a hull that never turns, which reads as a sprite being dragged.
            double maxAway = 0d;
            for (int i = 0; i <= 100; i++)
            {
                Expedition.PointOnLane(i / 100d, 900d, 110d, out _, out double z);
                if (System.Math.Abs(z) > maxAway) maxAway = System.Math.Abs(z);
            }
            Assert.That(maxAway, Is.GreaterThan(50d));
        }

        [Test]
        public void HeadingIsAlwaysAUnitVector()
        {
            foreach (bool outbound in new[] { true, false })
                for (int i = 0; i <= 100; i++)
                {
                    Expedition.HeadingOnLane(i / 100d, 900d, 110d, outbound, out double dx, out double dz);
                    Assert.That(System.Math.Sqrt(dx * dx + dz * dz), Is.EqualTo(1d).Within(1e-9),
                                "u " + (i / 100d) + " outbound " + outbound);
                }
        }

        [Test]
        public void SheComesHomeFacingHome()
        {
            Expedition.HeadingOnLane(0.5d, 900d, 110d, true, out double ox, out _);
            Expedition.HeadingOnLane(0.5d, 900d, 110d, false, out double hx, out _);
            Assert.That(ox, Is.GreaterThan(0d), "outbound runs up the axis");
            Assert.That(hx, Is.LessThan(0d), "homeward runs back down it");
        }

        [Test]
        public void AZeroLengthLaneDoesNotProduceANaN()
        {
            Expedition.HeadingOnLane(0.5d, 0d, 0d, true, out double dx, out double dz);
            Assert.That(double.IsNaN(dx), Is.False);
            Assert.That(double.IsNaN(dz), Is.False);
            Assert.That(System.Math.Sqrt(dx * dx + dz * dz), Is.EqualTo(1d).Within(1e-9));
        }

        [Test]
        public void BesideTheLaneIsAlwaysTheSameDistanceFromIt()
        {
            // What S2 hangs encounters off. "Off the route" has to keep meaning the same thing
            // wherever the lane happens to be bending, or a threat placed on a curve drifts onto it.
            for (int i = 0; i <= 100; i++)
            {
                double u = i / 100d;
                Expedition.PointOnLane(u, 900d, 110d, out double cx, out double cz);
                Expedition.OffsetFromLane(u, 900d, 110d, 60d, out double ox, out double oz);
                double d = System.Math.Sqrt((ox - cx) * (ox - cx) + (oz - cz) * (oz - cz));
                Assert.That(d, Is.EqualTo(60d).Within(1e-6), "u " + u);
            }
        }

        [Test]
        public void BesideFlipsSideWithTheSign()
        {
            Expedition.PointOnLane(0.3d, 900d, 110d, out double cx, out double cz);
            Expedition.OffsetFromLane(0.3d, 900d, 110d, 60d, out double px, out double pz);
            Expedition.OffsetFromLane(0.3d, 900d, 110d, -60d, out double sx, out double sz);
            Assert.That(px - cx, Is.EqualTo(-(sx - cx)).Within(1e-6));
            Assert.That(pz - cz, Is.EqualTo(-(sz - cz)).Within(1e-6));
        }

        // ---- the rule the layer rests on ---------------------------------------------------------

        [Test]
        public void PositionIsAPureFunctionOfTheClock()
        {
            // Docs/FIVE_LAYERS.md §4: active sailing may only ever improve an outcome. The way that is
            // held is that nothing in this file has any state to change — reading it a thousand times
            // gives the same answer as reading it once, so a scene left open cannot drift out of step
            // with the save, and there is no path by which watching a voyage could alter it.
            double first = Expedition.LanePosition(Expedition.Progress(Sailed, Returns, Sailed + 900L));
            for (int i = 0; i < 1000; i++)
                Assert.That(Expedition.LanePosition(Expedition.Progress(Sailed, Returns, Sailed + 900L)),
                            Is.EqualTo(first).Within(1e-12));
        }

        [Test]
        public void ExpeditionExposesNoWayToWrite()
        {
            // The same rule, asserted structurally. If somebody adds a setter or a field here, this is
            // the conversation it has to have first.
            var t = typeof(Expedition);
            Assert.That(t.IsAbstract && t.IsSealed, Is.True, "Expedition must stay a static class");
            Assert.That(t.GetFields(System.Reflection.BindingFlags.Public
                                    | System.Reflection.BindingFlags.NonPublic
                                    | System.Reflection.BindingFlags.Static), Is.Empty,
                        "Expedition must hold no state — position is a function of the save, nothing else");
        }
    }
}
