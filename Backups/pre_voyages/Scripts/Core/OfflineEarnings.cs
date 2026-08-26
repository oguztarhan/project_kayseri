namespace Game.Core
{
    /// <summary>
    /// Pure offline-earnings math (GDD §7). Awards the player's recent income rate for the time they
    /// were away, at a configurable efficiency, capped. Rollback-safe: non-positive elapsed earns nothing.
    /// </summary>
    public static class OfflineEarnings
    {
        public static BigDouble Compute(BigDouble ratePerSecond, long elapsedSeconds, double efficiency, long capSeconds)
        {
            if (elapsedSeconds <= 0L || efficiency <= 0d) return BigDouble.Zero;
            long capped = (capSeconds > 0L && elapsedSeconds > capSeconds) ? capSeconds : elapsedSeconds;
            return ratePerSecond * (capped * efficiency);
        }

        /// <summary>
        /// The whole grant including a boost that was still running when the player left.
        ///
        /// This exists because the number has to be quoted in two places that must never disagree: the
        /// welcome-back screen pays it, and the notification scheduled hours earlier predicts it. A
        /// notification that promises more than the screen delivers reads as the game cheating, and the
        /// only reliable way to keep them equal is for both to call this.
        ///
        /// <paramref name="boostSecondsFromLeaving"/> is how much of the boost was left AT THE MOMENT
        /// THE PLAYER LEFT — measured forward from that instant, not backward from now, because the
        /// credited window starts there. Only the extra above x1 is added; the first term already paid
        /// the whole window at the plain rate.
        /// </summary>
        public static BigDouble ComputeTotal(BigDouble ratePerSecond, long elapsedSeconds, double efficiency,
                                             long capSeconds, double boostMultiplier, long boostSecondsFromLeaving)
        {
            BigDouble earned = Compute(ratePerSecond, elapsedSeconds, efficiency, capSeconds);
            if (boostMultiplier <= 1d || boostSecondsFromLeaving <= 0L) return earned;

            long credited = (capSeconds > 0L && elapsedSeconds > capSeconds) ? capSeconds : elapsedSeconds;
            long boosted = boostSecondsFromLeaving > credited ? credited : boostSecondsFromLeaving;
            if (boosted <= 0L) return earned;
            return earned + Compute(ratePerSecond, boosted, efficiency * (boostMultiplier - 1d), 0L);
        }
    }
}
