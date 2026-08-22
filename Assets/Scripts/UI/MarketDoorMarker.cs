using Game.Core;
using Game.Gameplay;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The way in. A button floating over the island's market building that opens the yard.
    ///
    /// Modelled on <see cref="UpgradeReadyMarkers"/>, and for its reasons: one screen-space canvas so
    /// the badge stays a constant size through the whole zoom range, and a slow re-bind rather than a
    /// cached operation, because travelling to another island swaps which
    /// <see cref="CoalOperation"/> is live and the market building moves with it.
    ///
    /// Deliberately separate from the MARKET upgrade panel rather than a button inside it. Walking into
    /// the yard and buying a price upgrade are different intentions, and burying the first one two taps
    /// deep inside the second is how a whole mode goes unnoticed.
    ///
    /// It STANDS STILL. It used to glide up and down and breathe in and out, which is the treatment every
    /// other floating badge in the game gets and the wrong one here: this is the largest thing on the
    /// island, it sits over the one building the player is trying to look at, and a thing that size
    /// moving on its own does not read as "tap me", it reads as the building shifting. Static, and one
    /// size smaller, it is a sign over a door.
    ///
    /// It also has the space above the market to itself now — <see cref="UpgradeReadyMarkers"/> skips
    /// that station. Two badges were stacked over one roof, and between them the roof was gone.
    /// </summary>
    public sealed class MarketDoorMarker : MonoBehaviour
    {
        private const string MarketButtonResource = "UI/Buttons/market_enter_yellow";

        [Header("Hedef")]
        [Tooltip("Açılacak sahne. Build Settings'te ekli olmalı, yoksa dokunuş bir şey yapmaz.")]
        [SerializeField] private string marketSceneName = "Market";

        [Header("Görsel")]
        [Tooltip("Düğmenin genişliği ve yüksekliği, referans çözünürlükte piksel. Binayı kapatmayacak " +
                 "kadar küçük, başparmakla ıskalanmayacak kadar büyük olmalı.")]
        [SerializeField] private Vector2 buttonSize = new Vector2(250f, 84f);

        [Tooltip("Yazı boyu. Düğme genişliğiyle birlikte düşün: yazı taşarsa Fransızca ve Rusça " +
                 "düğmenin dışına çıkar.")]
        [SerializeField, Min(10)] private int labelSize = 30;

        [Tooltip("Binanın tepesinden ne kadar yukarıda durduğu, dünya birimi. MARKET'in kaldırılan " +
                 "yükseltme rozetiyle aynı yükseklik.")]
        [SerializeField] private float buildingLift = 10f;

        [Tooltip("Dünya noktasından sonra uygulanan ekran kayması, piksel. Negatif Y aşağı iter.\n\n" +
                 "Rozetin durduğu yerin biraz altına indirir; binanın üstünde durur, binanın önünde " +
                 "durmaz.")]
        [SerializeField] private Vector2 buildingOffset = new Vector2(0f, -56f);
        [Tooltip("HUD 100, satış yazıları 95, yükseltme rozetleri 92. Kapı rozetlerle aynı katta.")]
        [SerializeField] private int sortingOrder = 92;

        // MARKET can sit low in frame, and the button is tall enough to land under the bottom HUD
        // row when it does. Same band the building signs keep to.
        [Header("HUD payı")]
        [SerializeField] private float hudTopFraction = 0.10f;
        [SerializeField] private float hudBottomFraction = 0.20f;
        [SerializeField] private float hudSideFraction = 0.09f;
        [SerializeField] private Color tint = new Color(0.16f, 0.18f, 0.24f, 0.94f);

        // MARKET's index in IslandEconomy.Stations. Named here rather than reached for through the
        // simulation because this assembly has no business knowing the station order beyond this one.
        private const int MarketStation = 6;

        private Camera _cam;
        private CoalOperation _op;
        private RectTransform _canvasRect, _rect;
        private GameObject _root;
        private float _rebindIn;
        private bool _opening;

        private void Awake()
        {
            _cam = Camera.main;

            var go = new GameObject("MarketKapisiKanvas", typeof(Canvas), typeof(CanvasScaler),
                                    typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the HUD in the raycast stack, whatever the sorting order says. The button floats over
            // the island and the HUD is drawn over the whole screen, so a transparent edge of a HUD
            // button can sit on top of this one and quietly eat the tap.
            canvas.sortingOrder = Mathf.Max(sortingOrder, 102);
            var sc = go.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1080f, 1920f);
            sc.matchWidthOrHeight = 0.5f;
            _canvasRect = (RectTransform)go.transform;

            // The kit's primary-action button, the same art every other "do the thing" button in the
            // game wears. It used to pass null, which fell through to UiSkin's plain white quad tinted
            // dark grey — a flat rectangle sitting on a hand-drawn island, which is exactly as bad as
            // it sounds. UiBuild leaves pre-coloured kit art untinted, so the tint below is only ever
            // used on a project with no skin wired.
            Sprite marketArt = Resources.Load<Sprite>(MarketButtonResource);
            Button button = UiBuild.Btn(_canvasRect, "MarketeGir", Loc.T("market.gir"),
                                        marketArt != null ? marketArt : UiSkin.ButtonYellow,
                                        tint, labelSize, Open);
            Image buttonImage = button.GetComponent<Image>();
            if (buttonImage != null && marketArt != null)
            {
                // This is a complete glossy button painting rather than a 9-slice kit part.
                buttonImage.type = Image.Type.Simple;
                buttonImage.preserveAspect = false;
                buttonImage.color = Color.white;
            }
            _rect = (RectTransform)button.transform;
            _rect.anchorMin = _rect.anchorMax = new Vector2(0.5f, 0.5f);
            _rect.pivot = new Vector2(0.5f, 0.5f);
            _rect.sizeDelta = buttonSize;
            _root = button.gameObject;
            _root.SetActive(false);
        }

        private void Open()
        {
            // Main is parked rather than destroyed while the market is open. The same marker instance
            // therefore wakes on return, and an old true value must not block every later visit.
            if (_opening && SceneCurtain.Busy) return;
            _opening = false;

            if (!Application.CanStreamedLevelBeLoaded(marketSceneName))
            {
                Debug.LogError("Market sahnesi Build Settings içinde yüklenebilir değil: " + marketSceneName);
                return;
            }
            // Behind a loading screen, like every other scene the game reads. The async load on its own
            // was better than the synchronous one it replaced — the island kept animating instead of the
            // frame freezing — but it also meant a tap with no answer for as long as the read took, on
            // the one button in the game whose whole job is to promise a change of place.
            //
            // The curtain owns the double tap too, so the guard above is belt and braces.
            string key = ServiceLocator.Get<MarketService>()?.ActiveIsland;
            if (string.IsNullOrEmpty(key)) key = "coal";
            _opening = SceneCurtain.Cover(marketSceneName, WorldIslands.OreColorFor(key),
                                          Loc.Id("ada", key));
            if (_opening && _root != null) _root.SetActive(false);
        }

        private void OnEnable()
        {
            // Called again when the parked island roots are restored after leaving the market.
            _opening = false;
        }

        /// <summary>Travelling enables a different operation, so which one is live is re-checked on a timer.</summary>
        private void Rebind()
        {
            if (_cam == null) _cam = Camera.main;
            if (_op != null && _op.enabled) return;

            var all = FindObjectsByType<CoalOperation>(FindObjectsInactive.Exclude);
            for (int i = 0; i < all.Length; i++)
                if (all[i].enabled) { _op = all[i]; return; }
        }

        private void Update()
        {
            if (_opening && !SceneCurtain.Busy) _opening = false;
            float dt = Time.unscaledDeltaTime;
            _rebindIn -= dt;
            if (_rebindIn <= 0f) { _rebindIn = 1f; Rebind(); }
            if (_op == null || _cam == null) { Hide(); return; }

            Vector3 world;
            if (!_op.StationAnchor(MarketStation, out world)) { Hide(); return; }

            world.y += buildingLift;
            Vector3 screen = _cam.WorldToScreenPoint(world);
            if (screen.z <= 0f) { Hide(); return; }      // behind the camera

            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect, new Vector2(screen.x, screen.y), null, out local);

            // Straight onto the point, with no bob and no breath added to it. See the class summary:
            // the movement was what made this thing hard to look past.
            float w = _canvasRect.rect.width, h = _canvasRect.rect.height;
            float halfW = buttonSize.x * 0.5f, halfH = buttonSize.y * 0.5f;
            float x = local.x + buildingOffset.x, y = local.y + buildingOffset.y;

            float side = w * hudSideFraction;
            float loX = -w * 0.5f + side + halfW, hiX = w * 0.5f - side - halfW;
            float loY = -h * 0.5f + h * hudBottomFraction + halfH;
            float hiY = h * 0.5f - h * hudTopFraction - halfH;
            if (loX < hiX) x = x < loX ? loX : (x > hiX ? hiX : x);
            if (loY < hiY) y = y < loY ? loY : (y > hiY ? hiY : y);

            _rect.anchoredPosition = new Vector2(x, y);

            if (!_root.activeSelf) _root.SetActive(true);
        }

        private void Hide()
        {
            if (_root != null && _root.activeSelf) _root.SetActive(false);
        }
    }
}
