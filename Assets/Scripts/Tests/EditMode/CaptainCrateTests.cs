using NUnit.Framework;
using Game.Core;

namespace Game.Tests
{
    /// <summary>
    /// The crate. Because <see cref="CaptainCrate"/> takes its roll as an argument, the whole
    /// distribution can be walked exactly rather than sampled and hoped over — every test here is
    /// deterministic, and the two pity tests simulate tens of thousands of pulls in milliseconds.
    /// </summary>
    public class CaptainCrateTests
    {
        private static CaptainCrate.Tuning T => CaptainCrate.Tuning.Default;

        /// <summary>Walks a whole batch of pulls, advancing the counters exactly as the service does.</summary>
        private static Captains.Grade[] Pulls(int n, System.Func<int, double> roll, CaptainCrate.Tuning t)
        {
            var got = new Captains.Grade[n];
            int e = 0, l = 0;
            for (int i = 0; i < n; i++)
            {
                got[i] = CaptainCrate.RollGrade(roll(i), e, l, t);
                CaptainCrate.Advance(got[i], ref e, ref l);
            }
            return got;
        }

        // ---- the weight table --------------------------------------------------------------------

        [Test]
        public void RollGradeIsAlwaysARealGrade()
        {
            for (int i = 0; i <= 1000; i++)
            {
                var g = CaptainCrate.RollGrade(i / 1000d, 0, 0, T);
                Assert.That((int)g, Is.InRange(0, Captains.GradeCount - 1));
                Assert.That(Captains.CountOfGrade(g), Is.GreaterThan(0),
                            "rolled a grade nobody in the roster carries");
            }
        }

        [Test]
        public void RollsOutsideZeroToOneAreClampedRatherThanThrowing()
        {
            Assert.DoesNotThrow(() => CaptainCrate.RollGrade(-5d, 0, 0, T));
            Assert.DoesNotThrow(() => CaptainCrate.RollGrade(1d, 0, 0, T));
            Assert.DoesNotThrow(() => CaptainCrate.RollGrade(double.NaN, 0, 0, T));
            Assert.That(CaptainCrate.RollGrade(-5d, 0, 0, T), Is.EqualTo(Captains.Grade.Common));
        }

        [Test]
        public void TheDistributionMatchesTheWeights()
        {
            // Sweep the unit interval instead of sampling: with a fresh pair of counters every time,
            // the share of the interval landing on a grade IS its probability.
            const int n = 200000;
            var count = new int[Captains.GradeCount];
            for (int i = 0; i < n; i++) count[(int)CaptainCrate.RollGrade((i + 0.5d) / n, 0, 0, T)]++;

            double total = T.CommonWeight + T.RareWeight + T.EpicWeight + T.LegendaryWeight + T.MythicWeight;
            AssertShare(count[(int)Captains.Grade.Common],    n, T.CommonWeight / total);
            AssertShare(count[(int)Captains.Grade.Rare],      n, T.RareWeight / total);
            AssertShare(count[(int)Captains.Grade.Epic],      n, T.EpicWeight / total);
            AssertShare(count[(int)Captains.Grade.Legendary], n, T.LegendaryWeight / total);
            AssertShare(count[(int)Captains.Grade.Mythic],    n, T.MythicWeight / total);
        }

        private static void AssertShare(int got, int n, double expected)
            => Assert.That(got / (double)n, Is.EqualTo(expected).Within(0.002d));

        [Test]
        public void AnEmptyGradeCarriesNoWeight()
        {
            for (int g = 0; g < Captains.GradeCount; g++)
                if (Captains.CountOfGrade((Captains.Grade)g) <= 0)
                    Assert.That(CaptainCrate.WeightOf(g, 0, T), Is.Zero, "grade " + g);

            Assert.That(CaptainCrate.WeightOf(-1, 0, T), Is.Zero);
            Assert.That(CaptainCrate.WeightOf(Captains.GradeCount, 0, T), Is.Zero);
        }

        // ---- pity --------------------------------------------------------------------------------

        [Test]
        public void TheShortPityAlwaysLandsWithinItsWindow()
        {
            // The worst possible luck: every roll asks for the commonest thing available.
            var got = Pulls(5000, _ => 0d, T);
            int dry = 0;
            for (int i = 0; i < got.Length; i++)
            {
                if (got[i] >= Captains.Grade.Epic) { dry = 0; continue; }
                dry++;
                Assert.That(dry, Is.LessThan(T.EpicPity),
                            "went " + dry + " pulls without an Epic — the short pity is " + T.EpicPity);
            }
        }

        [Test]
        public void ABulkOpenAlwaysContainsAnEpic()
        {
            // This is the whole reason the bulk open exists, and why it is the button most players
            // will press. If EpicPity is ever raised past BulkCount, this is what notices.
            var got = Pulls(T.BulkCount, _ => 0d, T);
            bool any = false;
            for (int i = 0; i < got.Length; i++) if (got[i] >= Captains.Grade.Epic) any = true;
            Assert.That(any, Is.True);
        }

        [Test]
        public void TheLongPityAlwaysLandsWithinItsWindow()
        {
            var got = Pulls(20000, _ => 0d, T);
            int dry = 0;
            for (int i = 0; i < got.Length; i++)
            {
                if (got[i] >= Captains.Grade.Legendary) { dry = 0; continue; }
                dry++;
                Assert.That(dry, Is.LessThan(T.LegendaryPity),
                            "went " + dry + " pulls without a Legendary — the long pity is " + T.LegendaryPity);
            }
        }

        [Test]
        public void SoftPityRampsOnlyAfterItsStart()
        {
            Assert.That(CaptainCrate.SoftPityBonus(0, T), Is.Zero);
            Assert.That(CaptainCrate.SoftPityBonus(T.SoftPityStart - 2, T), Is.Zero);

            double a = CaptainCrate.SoftPityBonus(T.SoftPityStart, T);
            double b = CaptainCrate.SoftPityBonus(T.SoftPityStart + 10, T);
            Assert.That(a, Is.GreaterThan(0d));
            Assert.That(b, Is.GreaterThan(a), "the ramp must actually climb");
        }

        [Test]
        public void SoftPityMakesALegendaryStrictlyMoreLikely()
        {
            double fresh = CaptainCrate.WeightOf((int)Captains.Grade.Legendary, 0, T);
            double dry = CaptainCrate.WeightOf((int)Captains.Grade.Legendary, T.SoftPityStart + 15, T);
            Assert.That(dry, Is.GreaterThan(fresh));
        }

        [Test]
        public void TheFloorRisesOnlyWhenAPityIsDue()
        {
            Assert.That(CaptainCrate.Floor(0, 0, T), Is.EqualTo(Captains.Grade.Common));
            Assert.That(CaptainCrate.Floor(T.EpicPity - 2, 0, T), Is.EqualTo(Captains.Grade.Common));
            Assert.That(CaptainCrate.Floor(T.EpicPity - 1, 0, T), Is.EqualTo(Captains.Grade.Epic));
            Assert.That(CaptainCrate.Floor(0, T.LegendaryPity - 1, T), Is.EqualTo(Captains.Grade.Legendary));

            // The long pity outranks the short one when both are due.
            Assert.That(CaptainCrate.Floor(T.EpicPity, T.LegendaryPity, T), Is.EqualTo(Captains.Grade.Legendary));
        }

        [Test]
        public void APityDuePullNeverComesBackBelowItsFloor()
        {
            for (int i = 0; i <= 100; i++)
            {
                Assert.That(CaptainCrate.RollGrade(i / 100d, T.EpicPity - 1, 0, T),
                            Is.GreaterThanOrEqualTo(Captains.Grade.Epic));
                Assert.That(CaptainCrate.RollGrade(i / 100d, 0, T.LegendaryPity - 1, T),
                            Is.GreaterThanOrEqualTo(Captains.Grade.Legendary));
            }
        }

        [Test]
        public void PityCanBeSwitchedOff()
        {
            var t = T;
            t.EpicPity = 0;
            t.LegendaryPity = 0;
            Assert.That(CaptainCrate.Floor(9999, 9999, t), Is.EqualTo(Captains.Grade.Common));
            Assert.That(CaptainCrate.RollGrade(0d, 9999, 9999, t), Is.EqualTo(Captains.Grade.Common));
        }

        // ---- the counters ------------------------------------------------------------------------

        [Test]
        public void ALegendaryClearsBothCounters()
        {
            // Leaving the short counter running after a Legendary would owe the player a second
            // guarantee they have just been paid.
            int e = 7, l = 40;
            CaptainCrate.Advance(Captains.Grade.Legendary, ref e, ref l);
            Assert.That(e, Is.Zero);
            Assert.That(l, Is.Zero);

            e = 7; l = 40;
            CaptainCrate.Advance(Captains.Grade.Mythic, ref e, ref l);
            Assert.That(e, Is.Zero);
            Assert.That(l, Is.Zero);
        }

        [Test]
        public void AnEpicClearsTheShortCounterOnly()
        {
            int e = 7, l = 40;
            CaptainCrate.Advance(Captains.Grade.Epic, ref e, ref l);
            Assert.That(e, Is.Zero);
            Assert.That(l, Is.EqualTo(41));
        }

        [Test]
        public void AnythingLesserLengthensBoth()
        {
            int e = 7, l = 40;
            CaptainCrate.Advance(Captains.Grade.Common, ref e, ref l);
            Assert.That(e, Is.EqualTo(8));
            Assert.That(l, Is.EqualTo(41));

            CaptainCrate.Advance(Captains.Grade.Rare, ref e, ref l);
            Assert.That(e, Is.EqualTo(9));
            Assert.That(l, Is.EqualTo(42));
        }

        // ---- who comes out -----------------------------------------------------------------------

        [Test]
        public void RollCaptainAlwaysHandsOverSomebodyRealOfTheRolledGrade()
        {
            for (int a = 0; a <= 50; a++)
                for (int b = 0; b <= 50; b++)
                {
                    double gr = a / 50d, mr = b / 50d;
                    int c = CaptainCrate.RollCaptain(gr, mr, 0, 0, T);
                    Assert.That(Captains.Exists(c), Is.True, gr + "/" + mr);
                    Assert.That(Captains.RankOf(c), Is.EqualTo(CaptainCrate.RollGrade(gr, 0, 0, T)));
                }
        }

        [Test]
        public void EveryCaptainOnTheRosterCanActuallyBeDrawn()
        {
            // A captain nobody can pull is dead content the player is never told about.
            var seen = new System.Collections.Generic.HashSet<int>();
            for (int a = 0; a < 400; a++)
                for (int b = 0; b < 40; b++)
                    seen.Add(CaptainCrate.RollCaptain(a / 400d, b / 40d, 0, 0, T));

            for (int c = 0; c < Captains.Count; c++)
                Assert.That(seen.Contains(c), Is.True, "captain " + Captains.IdOf(c) + " is unreachable");
        }

        [Test]
        public void MembersOfAGradeComeOutEvenly()
        {
            const int n = 60000;
            var count = new int[Captains.Count];
            for (int i = 0; i < n; i++)
                count[CaptainCrate.RollCaptain(0d, (i + 0.5d) / n, 0, 0, T)]++;

            var grade = CaptainCrate.RollGrade(0d, 0, 0, T);
            int members = Captains.CountOfGrade(grade);
            for (int nth = 0; nth < members; nth++)
                Assert.That(count[Captains.OfGrade(grade, nth)] / (double)n,
                            Is.EqualTo(1d / members).Within(0.01d));
        }

        // ---- price -------------------------------------------------------------------------------

        [Test]
        public void BulkIsCheaperPerCrateAndNothingElseIs()
        {
            Assert.That(CaptainCrate.Cost(1, T), Is.EqualTo(T.ChartCost));
            Assert.That(CaptainCrate.Cost(T.BulkCount, T), Is.EqualTo(T.BulkChartCost));
            Assert.That(CaptainCrate.Cost(T.BulkCount, T),
                        Is.LessThan(T.ChartCost * T.BulkCount), "a bulk open nobody saves on is a button nobody presses");
            Assert.That(CaptainCrate.Cost(3, T), Is.EqualTo(T.ChartCost * 3));
            Assert.That(CaptainCrate.Cost(0, T), Is.Zero);
            Assert.That(CaptainCrate.Cost(-4, T), Is.Zero);
        }
    }
}
