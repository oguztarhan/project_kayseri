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
    }
}
