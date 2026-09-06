using System;

namespace Game.Core
{
    /// <summary>Numerical conversion rules shared by the new worker simulation and its UI.</summary>
    public static class IdleCrewRules
    {
        // Keep the existing third job's contribution exactly: only its name/payment behavior changes.
        public static double ServiceRate(int carry, int serve, int dispatch)
            => Math.Min(MarketFlow.JobRate(carry), Math.Min(MarketFlow.JobRate(serve), MarketFlow.JobRate(dispatch)));

        // Global marketCarryLevel retains its level, and improves NPC loads on every island.
        // Level 0 is neutral; the existing level-8 cap yields 1.8x. Preserve the shop's current cap.
        public static double PorterLoadMultiplier(int marketCarryLevel)
            => 1d + 0.1d * Math.Min(MarketPrices.MaxCarryLevel, Math.Max(0, marketCarryLevel));
    }
}
