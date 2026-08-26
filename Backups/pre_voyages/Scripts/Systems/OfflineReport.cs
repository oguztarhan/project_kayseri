using Game.Core;

namespace Game.Systems
{
    /// <summary>Carries the offline-earnings result from bootstrap to the HUD welcome-back popup (GDD §7).</summary>
    public sealed class OfflineReport
    {
        public BigDouble Amount;
        public bool Pending;
        /// <summary>Real time away, uncapped — the screen reports how long you were gone, not what was paid.</summary>
        public long AwaySeconds;
        /// <summary>
        /// Time actually paid for, after <see cref="Game.Data.OfflineConfig"/>'s cap. Less than
        /// <see cref="AwaySeconds"/> means the cap bit, and the screen has to say so — a player who
        /// was away nine hours and is paid for two will work it out, and silence reads as theft.
        /// </summary>
        public long CreditedSeconds;
        /// <summary>Rate multiplier applied to the credited time (OfflineConfig efficiency), 0..1.</summary>
        public double Efficiency = 1d;
    }
}
