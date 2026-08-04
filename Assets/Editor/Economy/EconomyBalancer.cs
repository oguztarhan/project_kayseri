using System;
using System.Text;
using Game.Core;
using UnityEditor;
using UnityEngine;

namespace Kayseri.EconomyTools
{
    /// <summary>
    /// Solves the island ladder from the pacing targets instead of hand-picking it.
    ///
    /// Two numbers set every island's tempo: how much more it EARNS than the last
    /// (<c>valueMultiplier</c>) and how much more it COSTS to upgrade
    /// (<c>costMultiplier</c>). Shipped, those are x3.2 and x4 — so each island takes
    /// 25% longer than the one before, and by the twentieth that has compounded to 73x.
    /// No weekly content cadence survives that, so this sets them equal and time per
    /// island goes flat.
    ///
    /// With the tempo fixed, the unlock price is what is left to choose, and it has a
    /// clean definition: an island costs what you earn in the window it is meant to
    /// take. That is solved here per island against
    /// <see cref="EconomyCurve.TargetIncomeHours"/>, using the measured income curve.
    ///
    /// Run: Kayseri/Economy/Solve Ladder. It prints; it does not write.
    /// </summary>
    public static class EconomyBalancer
    {
        /// <summary>Tier step, applied to BOTH value and cost so pacing stays flat.</summary>
        public const double TierStep = 3.2d;

        /// <summary>What a maxed, fully-built coal island really earns — measured, not assumed.</summary>
        public const double MaxedCoalPerMin = EconomyCurve.MaxedCoalPerMin;

        private const double GhostBuildings = 480000d;   // the ten one-time buildings, coal prices

        public static double ValueMultiplier(int n) => Math.Pow(TierStep, n);

        [MenuItem("Kayseri/Economy/Solve Ladder", false, 20)]
        public static void SolveMenu() => Debug.Log(Solve(8));

        public static string Solve(int islands)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Ladder solved against the measured curve. Tier step x" + TierStep
                          + " on BOTH value and cost.");
            sb.AppendLine();
            // "next costs" is the price of island n+1, solved on island n's row - that is
            // the island whose earning window pays for it.
            sb.AppendLine("  n  island      target d   target ih   to max ih       next costs       cap $/min   left earning");

            string[] names = { "Coal", "Copper", "Iron", "Silver", "Gold", "Ruby", "Emerald", "Diamond" };
            double background = 0d;
            var unlocks = new double[islands];

            var costMuls = new double[islands];

            for (int n = 0; n < islands; n++)
            {
                double targetIh = EconomyCurve.TargetIncomeHours(n);

                // 1. How expensive should this island's upgrade track be? Solved, not
                //    derived: the islands already owned keep paying while this one is
                //    built, and that background is 13x a late island's own starting rate,
                //    so a closed form off value alone comes out 2.4x too fast.
                //    The track is sized to fill its own window - the player runs out of
                //    things to buy exactly as the next island opens, never before.
                double clo = 1e-4d, chi = 1e9d, costMul = 1d;
                for (int iter = 0; iter < 80; iter++)
                {
                    costMul = Math.Sqrt(clo * chi);
                    if (Run(n, costMul, double.MaxValue, background).HoursToMax > targetIh) chi = costMul;
                    else clo = costMul;
                }
                costMuls[n] = costMul;

                // 2. An island opens when cumulative earnings reach its price, and the sim
                //    spends everything as it earns - so "cumulative spend" and "cumulative
                //    earnings" are the same quantity. Bisect the price landing on the target.
                double lo = 1e2d, hi = 1e18d, solved = 0d;
                for (int iter = 0; iter < 80; iter++)
                {
                    solved = Math.Sqrt(lo * hi);          // geometric: the range spans decades
                    if (Run(n, costMul, solved, background).HoursToNextUnlock > targetIh) hi = solved;
                    else lo = solved;
                }

                var run = Run(n, costMul, solved, background);
                double capPerMin = MaxedCoalPerMin * ValueMultiplier(n);
                sb.AppendLine(string.Format("  {0}  {1,-10}{2,9:F1}{3,12:F1}{4,12:F1}{5,17}{6,16}{7,15}",
                    n, n < names.Length ? names[n] : "island" + n,
                    EconomyCurve.TargetElapsedDays(n), targetIh, run.HoursToMax,
                    Money(solved), Money(capPerMin), Money(run.PerMinAtGate)));

                if (n + 1 < islands) unlocks[n + 1] = solved;
                // What the player leaves behind, not what the island could reach.
                background += run.PerMinAtGate;
            }

            sb.AppendLine();
            sb.AppendLine("Unlock costs, in the order DefaultLadder wants them:");
            for (int n = 1; n < islands; n++) sb.Append(Money(unlocks[n])).Append(n + 1 < islands ? ", " : "");
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("Cost multipliers (Main.unity, per CoalOperation):");
            for (int n = 0; n < islands; n++)
                sb.Append(costMuls[n].ToString("0.####")).Append(n + 1 < islands ? ", " : "");
            sb.AppendLine();
            sb.AppendLine("  step between them (settles to the tier step once the ramp is done):");
            for (int n = 1; n < islands; n++)
                sb.Append((costMuls[n] / costMuls[n - 1]).ToString("0.00")).Append(n + 1 < islands ? ", " : "");
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("Beyond the eighth island every target is a flat week, so the unlock price,");
            sb.AppendLine("the cap and the cost multiplier are all simply x" + TierStep + " per island from there.");
            return sb.ToString();
        }

        private static EconomyCurve.Run Run(int n, double costMul, double unlockCost, double background)
        {
            var t = IslandEconomy.Tuning.Default;
            t.ValueMultiplier = (float)ValueMultiplier(n);
            t.CostMultiplier = costMul;
            var econ = new IslandEconomy(t, IslandEconomy.NewLevels(), new bool[10]);
            return EconomyCurve.Simulate(econ, ValueMultiplier(n), unlockCost, background,
                                         GhostBuildings * costMul);
        }

        /// <summary>Compact money, the way the ladder is written by hand: 12e6, 1.55e9.</summary>
        private static string Money(double v)
        {
            if (v >= 1e9d) return (v / 1e9d).ToString("0.##") + "e9d";
            if (v >= 1e6d) return (v / 1e6d).ToString("0.##") + "e6d";
            if (v >= 1e3d) return (v / 1e3d).ToString("0.##") + "e3d";
            return v.ToString("0") + "d";
        }
    }
}
