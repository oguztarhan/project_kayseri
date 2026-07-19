using UnityEngine;

namespace Game.Systems
{
    /// <summary>Haptics facade (GDD §13.5). Light vibration on rewards; no-op in the editor and when disabled.</summary>
    public sealed class HapticService
    {
        private readonly bool _enabled;

        public HapticService(bool enabled) { _enabled = enabled; }

        public void Light()
        {
            if (_enabled) Handheld.Vibrate();
        }
    }
}
