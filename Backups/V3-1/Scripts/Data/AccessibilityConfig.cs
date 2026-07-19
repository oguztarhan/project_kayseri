using UnityEngine;

namespace Game.Data
{
    public enum ColorblindMode { None, Deuteranopia, Protanopia, Tritanopia }

    /// <summary>
    /// Accessibility settings (GDD §13.5). Colorblind mode, text scaling, and reduce-motion. Ore/product
    /// readability must never rely on hue alone — icons/labels carry meaning too.
    /// </summary>
    [CreateAssetMenu(fileName = "AccessibilityConfig", menuName = "Ore Empire/Accessibility Config", order = 15)]
    public sealed class AccessibilityConfig : ScriptableObject
    {
        [SerializeField] private ColorblindMode colorblind = ColorblindMode.None;
        [SerializeField, Range(0.75f, 1.5f)] private float textScale = 1f;
        [SerializeField] private bool reduceMotion = false;

        public ColorblindMode Colorblind => colorblind;
        public float TextScale => textScale;
        public bool ReduceMotion => reduceMotion;
    }
}
