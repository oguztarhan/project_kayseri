using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// Shared sprite set for the runtime-built HUD. Before this, <see cref="CoalHud"/>,
    /// <see cref="IslandMapUI"/> and <see cref="StationBadges"/> each generated their own 4×4 white
    /// texture, which is why every panel and button rendered as a plain flat rectangle.
    ///
    /// The kit art is pre-coloured (a green button is a green PNG), so callers pick state by swapping
    /// the sprite rather than tinting a neutral one — tinting pre-coloured art just muddies it.
    /// References are wired in the Inspector (project convention); every getter falls back to a
    /// generated flat sprite when a slot is empty, so an unwired skin degrades to the old look.
    /// </summary>
    public sealed class UiSkin : MonoBehaviour
    {
        [Header("Panels (9-sliced)")]
        [SerializeField] private Sprite panel;        // window / card body
        [SerializeField] private Sprite pill;         // capsule for top-bar counters

        [Header("Buttons (9-sliced, pre-coloured)")]
        [SerializeField] private Sprite buttonGreen;  // affordable / buy
        [SerializeField] private Sprite buttonGrey;   // can't afford / disabled
        [SerializeField] private Sprite buttonBlue;   // done / maxed
        [SerializeField] private Sprite buttonYellow; // primary action

        [Header("Icons")]
        [SerializeField] private Sprite coin;
        [SerializeField] private Sprite gem;

        private static UiSkin _instance;
        private static Sprite _flat;

        public static UiSkin Instance
        {
            get
            {
                if (_instance == null) _instance = FindAnyObjectByType<UiSkin>();
                return _instance;
            }
        }

        /// <summary>Fallback sprite: a 1px-bordered white quad, tintable and stretchable.</summary>
        public static Sprite Flat
        {
            get
            {
                if (_flat != null) return _flat;
                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var px = new Color[16];
                for (int i = 0; i < 16; i++) px[i] = Color.white;
                tex.SetPixels(px); tex.Apply();
                _flat = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f, 0,
                                      SpriteMeshType.FullRect, new Vector4(1, 1, 1, 1));
                return _flat;
            }
        }

        private static Sprite Pick(Sprite s) => s != null ? s : Flat;

        public static Sprite Panel => Instance != null ? Pick(Instance.panel) : Flat;
        public static Sprite Pill => Instance != null ? Pick(Instance.pill) : Flat;
        public static Sprite ButtonGreen => Instance != null ? Pick(Instance.buttonGreen) : Flat;
        public static Sprite ButtonGrey => Instance != null ? Pick(Instance.buttonGrey) : Flat;
        public static Sprite ButtonBlue => Instance != null ? Pick(Instance.buttonBlue) : Flat;
        public static Sprite ButtonYellow => Instance != null ? Pick(Instance.buttonYellow) : Flat;
        public static Sprite Coin => Instance != null ? Instance.coin : null;
        public static Sprite Gem => Instance != null ? Instance.gem : null;

        /// <summary>True when a real skin is wired — callers tint only when falling back to flat art.</summary>
        public static bool HasArt => Instance != null && Instance.buttonGreen != null;
    }
}
