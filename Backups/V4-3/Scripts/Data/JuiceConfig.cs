using UnityEngine;

namespace Game.Data
{
    /// <summary>Game-feel toggles (GDD §13.5). Quality- and accessibility-gated so juice never breaks the budget.</summary>
    [CreateAssetMenu(fileName = "JuiceConfig", menuName = "Ore Empire/Juice Config", order = 17)]
    public sealed class JuiceConfig : ScriptableObject
    {
        [SerializeField] private bool haptics = true;
        [SerializeField] private bool screenShake = true;
        [SerializeField] private float numberPunchScale = 1.2f;

        public bool Haptics => haptics;
        public bool ScreenShake => screenShake;
        public float NumberPunchScale => numberPunchScale;
    }
}
