using System;

namespace Game.Core
{
    /// <summary>
    /// Milestone step-multipliers (GDD §5): every <see cref="Every"/> levels a track crosses, the station's
    /// output gains a step ×<see cref="StepMultiplier"/>. Configured once at bootstrap from EconomyConfig, then
    /// read by the stations' effect calc — so the thresholds stay designer-editable, not magic numbers in code.
    /// </summary>
    public static class Milestones
    {
        public static int Every = 25;
        public static double StepMultiplier = 2d;

        /// <summary>Output multiplier from all milestones crossed at <paramref name="level"/>.</summary>
        public static double Multiplier(int level)
        {
            if (Every <= 0 || level < Every) return 1d;
            return Math.Pow(StepMultiplier, level / Every);
        }

        /// <summary>Levels remaining until the next milestone (0 if the config is disabled).</summary>
        public static int ToNext(int level) => Every <= 0 ? 0 : Every - (level % Every);
    }
}
