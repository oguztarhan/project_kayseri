using UnityEngine;

namespace Game.Systems
{
    /// <summary>Haptics facade (GDD §13.5). Light vibration on rewards; no-op in the editor and when disabled.
    /// <see cref="Enabled"/> is mutable so the settings screen can flip it at runtime.</summary>
    public sealed class HapticService
    {
        public bool Enabled { get; set; }

        public HapticService(bool enabled) { Enabled = enabled; }

        public void Light()
        {
            if (Enabled) Handheld.Vibrate();
        }
    }
}
