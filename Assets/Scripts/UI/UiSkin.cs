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

        private static Sprite _flat;

        // The art, held statically as well as on the component.
        //
        // THIS IS WHAT LETS THE MARKET LOOK LIKE THE GAME. The skin is authored once, in Main, and the
        // market yard runs in its own scene with Main unloaded — so the lookup came back empty in there
        // and every panel and button in the yard fell through to the plain white quad. Two different
        // interfaces, one game. A static reference outlives the scene that owned it, and holding one is
        // also what stops UnloadUnusedAssets dropping the atlas during the swap.
        //
        // Only ever filled from a wired slot, so a second skin with empty slots cannot blank it.
        private static Sprite _panel, _pill, _green, _grey, _blue, _yellow, _coin, _gem;
        private static bool _looked;

        private void Awake() => Cache();

        private void Cache()
        {
            _looked = true;
            if (panel != null) _panel = panel;
            if (pill != null) _pill = pill;
            if (buttonGreen != null) _green = buttonGreen;
            if (buttonGrey != null) _grey = buttonGrey;
            if (buttonBlue != null) _blue = buttonBlue;
            if (buttonYellow != null) _yellow = buttonYellow;
            if (coin != null) _coin = coin;
            if (gem != null) _gem = gem;
        }

        /// <summary>
        /// Finds the skin the first time anything asks. Once. A scene with no skin in it must not pay for
        /// a search every time a label is built, and a scene that HAS one has already cached itself in
        /// Awake before any screen gets built.
        /// </summary>
        private static void Ensure()
        {
            if (_looked) return;
            _looked = true;
            UiSkin found = FindAnyObjectByType<UiSkin>();
            if (found != null) found.Cache();
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

        public static Sprite Panel { get { Ensure(); return Pick(_panel); } }
        public static Sprite Pill { get { Ensure(); return Pick(_pill); } }
        public static Sprite ButtonGreen { get { Ensure(); return Pick(_green); } }
        public static Sprite ButtonGrey { get { Ensure(); return Pick(_grey); } }
        public static Sprite ButtonBlue { get { Ensure(); return Pick(_blue); } }
        public static Sprite ButtonYellow { get { Ensure(); return Pick(_yellow); } }
        public static Sprite Coin { get { Ensure(); return _coin; } }
        public static Sprite Gem { get { Ensure(); return _gem; } }

        /// <summary>True when a real skin is wired — callers tint only when falling back to flat art.</summary>
        public static bool HasArt { get { Ensure(); return _green != null; } }
    }
}
