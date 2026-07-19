using Game.Core;

namespace Game.Systems
{
    /// <summary>Carries the offline-earnings result from bootstrap to the HUD welcome-back popup (GDD §7).</summary>
    public sealed class OfflineReport
    {
        public BigDouble Amount;
        public bool Pending;
    }
}
