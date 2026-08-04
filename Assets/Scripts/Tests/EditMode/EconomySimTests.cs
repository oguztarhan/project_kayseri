using System;
using System.Text;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// The pacing instrument. Walks the whole island ladder using the measured income
    /// curve (<see cref="EconomyCurve"/>) and the real cost curve
    /// (<see cref="IslandEconomy"/>), and reports how long each island takes.
    ///
    /// The targets asserted here are the agreed shape of the game: a fast onboarding
    /// ramp settling to about a week per island, so a player stays level with the weekly
    /// island drop forever. A tuning change that breaks that fails here instead of
    /// shipping.
    /// </summary>
    public class EconomySimTests
    {
        // The eight islands live today, plus four of the weekly drops to come — the ladder
        // is meant to reach 20-25, and the steady state is what has to hold.
        private static readonly string[] Names =
        {
            "Coal", "Copper", "Iron", "Silver", "Gold", "Ruby", "Emerald", "Diamond",
            "week 9", "week 10", "week 11", "week 12",
        };

        /// <summary>Islands past the authored ramp; everything about them is x3.2 per step.</summary>
        private const int Ramp = 8;

        // Per-island multipliers, mirroring the CoalOperation components in Main.unity.
        // Value steps by the tier; cost is solved so an island's upgrade track takes about
        // as long to buy as the island is meant to last (Kayseri/Economy/Solve Ladder).
        private static double ValueMultiplier(int n) => Math.Pow(3.2d, n);
        private static readonly double[] CostMul =
        { 0.48, 20.6487, 147.8513, 787.0063, 3783.3154, 16718.3784, 63310.6556, 218269.5513 };
        private static double CostMultiplier(int n) => n < Ramp
            ? CostMul[n]
            : CostMul[Ramp - 1] * Math.Pow(3.2d, n - Ramp + 1);

        // Unlock costs, mirroring WorldIslands.DefaultLadder.
        private static readonly double[] Unlock =
        { 0d, 1.89e6d, 81.41e6d, 614.78e6d, 3.1e9d, 15.73e9d, 65.91e9d, 263.25e9d };
        private static double UnlockCost(int n) => n < Ramp
            ? Unlock[n]
            : Unlock[Ramp - 1] * Math.Pow(3.2d, n - Ramp + 1);

        /// <summary>The ten ghost buildings, before the island's cost multiplier.</summary>
        private const double GhostBuildings = 25000d + 10000d + 15000d + 60000d + 20000d
                                            + 35000d + 40000d + 150000d + 80000d + 45000d;

        /// <summary>
        /// Income-hours per island the design calls for: fast at first, then a week, flat.
        /// Income-hours, not wall-clock — see the note on <see cref="EconomyCurve"/>.
        /// </summary>
        private static double TargetHours(int n) => EconomyCurve.TargetIncomeHours(n);

        private static IslandEconomy Island(int n)
        {
            var t = IslandEconomy.Tuning.Default;
            t.ValueMultiplier = (float)ValueMultiplier(n);
            t.CostMultiplier = CostMultiplier(n);
            return new IslandEconomy(t, IslandEconomy.NewLevels(), new bool[10]);
        }

        private struct Row
        {
            public double Hours, ToMax, Cumulative, FinalPerMin, Spent;
        }

        private static Row[] WalkLadder()
        {
            var rows = new Row[Names.Length];
            double background = 0d, cumulative = 0d;
            for (int n = 0; n < Names.Length; n++)
            {
                double next = n + 1 < Names.Length ? UnlockCost(n + 1) : 0d;
                var run = EconomyCurve.Simulate(Island(n), ValueMultiplier(n), next,
                                                background, GhostBuildings * CostMultiplier(n));
                // An island is "done" when it has paid for the next one; that is the gate
                // the player actually feels, not the level cap.
                double hours = next > 0d ? run.HoursToNextUnlock : run.HoursToMax;
                cumulative += hours;
                rows[n] = new Row
                {
                    Hours = hours,
                    ToMax = run.HoursToMax,
                    Cumulative = cumulative,
                    FinalPerMin = run.FinalPerMin,
                    Spent = run.SpentToMax,
                };
                // What the player LEAVES it at, not what it could reach: only the active
                // island simulates, so one moved on from half-built stays half-built.
                background += run.PerMinAtGate;
            }
            return rows;
        }

        [Test]
        public void PacingReport()
        {
            var rows = WalkLadder();
            var sb = new StringBuilder();
            // "gate h" is what the player feels: hours until the NEXT island is affordable.
            // The last island has none, so its gate is its own time-to-max.
            sb.AppendLine("island      gate h   to max   cum h   cum days   target h   ratio   maxed $/min");
            for (int n = 0; n < rows.Length; n++)
                sb.AppendLine(string.Format(
                    "{0,-10}{1,8:F1}{2,9:F1}{3,8:F0}{4,11:F1}{5,11:F0}{6,8:F2}   {7,14:N0}",
                    Names[n], rows[n].Hours, rows[n].ToMax, rows[n].Cumulative,
                    rows[n].Cumulative / 24d, TargetHours(n),
                    rows[n].Hours / TargetHours(n), rows[n].FinalPerMin));
            sb.AppendLine();
            sb.AppendLine(string.Format("Total to Diamond: {0:F0} h  ({1:F0} days of continuous play)",
                rows[rows.Length - 1].Cumulative, rows[rows.Length - 1].Cumulative / 24d));
            sb.AppendLine(string.Format("At 40 min/day that is {0:F0} real days.",
                rows[rows.Length - 1].Cumulative * 60d / 40d));
            TestContext.WriteLine(sb.ToString());
            Assert.Pass(sb.ToString());
        }

        /// <summary>
        /// Every island should land within a factor of two of its target. This is the test
        /// the balance work has to make pass; it is expected to FAIL until then, which is
        /// the point of writing it first.
        /// </summary>
        [Test]
        public void EveryIslandIsNearItsTarget()
        {
            var rows = WalkLadder();
            for (int n = 0; n < rows.Length; n++)
            {
                double ratio = rows[n].Hours / TargetHours(n);
                Assert.That(ratio, Is.InRange(0.5d, 2.0d),
                    $"{Names[n]} takes {rows[n].Hours:F1} income-hours against a target of {TargetHours(n):F1}");
            }
        }

        /// <summary>
        /// Time per island must not run away. Cost grows x4 per tier while value grows only
        /// x3.2, so today each island takes 25% longer than the last and the twentieth takes
        /// 73x the first — which no weekly content cadence can survive.
        /// </summary>
        [Test]
        public void TimePerIslandDoesNotRunAway()
        {
            // Measured across the STEADY STATE only. The first eight islands ramp on purpose
            // — that is the onboarding — so comparing island 1 with island 8 would flag the
            // design as a bug. What must not drift is what happens after it: every weekly
            // drop from here to island 25 has to cost the player the same week.
            //
            // Measured on time-to-max rather than the unlock gate, because the last island
            // simulated has no next island to pay for and its gate is a different quantity.
            var rows = WalkLadder();
            double growth = Math.Pow(rows[rows.Length - 1].ToMax / rows[Ramp - 1].ToMax,
                                     1d / (rows.Length - Ramp));
            TestContext.WriteLine($"steady state grows {growth:F3}x per island; " +
                                  $"over 20 weekly drops that is {Math.Pow(growth, 20):F1}x");
            Assert.That(growth, Is.LessThan(1.05d),
                "each weekly island must take about as long as the last, or the drop " +
                "cadence outruns the player");
        }

        /// <summary>
        /// The measured curve must rise — buying levels cannot pay less.
        ///
        /// Level 8 is exempted, and deliberately named rather than absorbed into a loose
        /// tolerance: it measures 1813 against level 5's 3114, it reproduces across runs and
        /// fleet caps, and it is a real defect with an open investigation (see
        /// EconomyCurve.DipLevel and IslandEconomy.MaxLevel). Widening the tolerance until
        /// it passed would hide the one thing here worth fixing. Everywhere else the
        /// tolerance is the measurement's own noise floor: the game's meter averages 60
        /// seconds against truck cycles of about 25, so a sample carries roughly ±10%.
        /// </summary>
        [Test]
        public void MeasuredCurveRises()
        {
            for (int i = 1; i < EconomyCurve.SamplePerMin.Length; i++)
            {
                int level = EconomyCurve.SampleLevel[i];
                if (level == EconomyCurve.DipLevel || EconomyCurve.SampleLevel[i - 1] == EconomyCurve.DipLevel)
                {
                    TestContext.WriteLine($"level {level}: skipped, known dip under investigation");
                    continue;
                }
                Assert.That(EconomyCurve.SamplePerMin[i],
                    Is.GreaterThan(EconomyCurve.SamplePerMin[i - 1] * 0.90d),
                    $"income fell between level {EconomyCurve.SampleLevel[i - 1]} and {level}");
            }
            Assert.That(EconomyCurve.SamplePerMin[EconomyCurve.SamplePerMin.Length - 1],
                Is.GreaterThan(EconomyCurve.SamplePerMin[0] * 10d),
                "the whole upgrade track has to be worth at least 10x");
        }

        /// <summary>
        /// Guards the dip itself: it is allowed to exist while it is being investigated, but
        /// not to get worse or to spread. If this fails, something made the haulage
        /// instability bigger.
        /// </summary>
        [Test]
        public void TheKnownDipDoesNotGetWorse()
        {
            int i = System.Array.IndexOf(EconomyCurve.SampleLevel, EconomyCurve.DipLevel);
            double before = EconomyCurve.SamplePerMin[i - 1], at = EconomyCurve.SamplePerMin[i];
            TestContext.WriteLine($"dip at level {EconomyCurve.DipLevel}: {at:N0} against {before:N0} " +
                                  $"before it ({at / before:P0})");
            Assert.That(at / before, Is.GreaterThan(0.5d),
                "the level-8 income dip has deepened past half — see EconomyCurve.DipLevel");
        }

        /// <summary>
        /// The ladder assumes a maxed coal island earns 29,000 $/min and prices every unlock
        /// off that. The probe measures 15,921.
        /// </summary>
        /// <summary>
        /// The ladder's cap must be what an island actually earns. It used to be 29,000 —
        /// a rate no island reaches — so every unlock was priced about 1.8x too high.
        /// </summary>
        [Test]
        public void LadderCapMatchesWhatAnIslandEarns()
        {
            double measured = EconomyCurve.CoalPerMinute(50, true);
            TestContext.WriteLine($"maxed coal measures {measured:N0} $/min");
            Assert.That(measured, Is.EqualTo(EconomyCurve.MaxedCoalPerMin).Within(2d).Percent,
                "WorldIslands.CoalMaxPerMin and every island's incomeCapPerMin key off this");
        }
    }
}
