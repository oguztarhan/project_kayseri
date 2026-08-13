using Game.Core;
using Game.Gameplay;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Everything on screen while the player is in a market yard: the thumb stick, the way out, and a
    /// readout of what the yard is doing.
    ///
    /// Built at runtime rather than authored, the same way <see cref="IslandMapUI"/>'s siblings are —
    /// see <see cref="UiBuild"/>. A greybox screen that has to be wired by hand before it can be run is
    /// a screen nobody runs.
    ///
    /// It is also the bridge for input. <see cref="MarketPlayer"/> lives in <c>Game.Gameplay</c>, which
    /// sits below this assembly and is not allowed to know what a joystick is, so the push goes the
    /// only direction the dependency permits: down, from here, once a frame.
    /// </summary>
    public sealed class MarketHudUI : MonoBehaviour
    {
        [Tooltip("HUD 100, satış yazıları 95, rozetler 92. Avlu ekranı kendi sahnesinde tek başına, " +
                 "ama aynı ölçekte dursun diye HUD ile aynı sırada.")]
        [SerializeField] private int sortingOrder = 100;

        [Tooltip("Kolun çalıştığı alan, ekranın altından yukarı doğru oran. Üst şerit dışarıda " +
                 "kalmalı, yoksa çıkış düğmesine basmak yürümek sayılır.")]
        [SerializeField, Range(0.2f, 1f)] private float stickZoneHeight = 0.78f;

        private MarketJoystick _stick;
        private MarketPlayer _player;
        private MarketService _market;
        private string _yardKey;
        private UpgradePad[] _pads;

        private static readonly Color FillColour = new Color(0.85f, 0.72f, 0.25f, 0.95f);
        private static readonly Color SpillingColour = new Color(0.82f, 0.29f, 0.24f, 0.95f);
        private static readonly Color Chrome = new Color(0.07f, 0.08f, 0.11f, 0.86f);

        private Text _stockText, _incomeText, _padText, _padSubText, _carryText, _yardText;
        private RectTransform _stockFill, _carryFill, _padPanel, _accent;
        private Image _stockFillImage;
        private CarryStack _carry;
        private float _readoutIn;

        /// <summary>Builds the screen. <paramref name="onExit"/> is what the way-out button does.</summary>
        public void Build(MarketPlayer player, MarketService market, string yardKey,
                          UpgradePad[] pads, UnityEngine.Events.UnityAction onExit)
        {
            _player = player;
            _market = market;
            _yardKey = yardKey;
            _pads = pads;
            _carry = player != null ? player.GetComponent<CarryStack>() : null;

            RectTransform canvas = UiBuild.Canvas(transform, "MarketHudKanvas", sortingOrder);

            // ---- the stick's zone, under everything else so the readouts stay tappable ----
            var zoneGo = new GameObject("KolAlani", typeof(RectTransform));
            var zone = (RectTransform)zoneGo.transform;
            zone.SetParent(canvas, false);
            UiBuild.Anchor(zone, Vector2.zero, new Vector2(1f, stickZoneHeight));
            MarketJoystick.DragSurface(zone);

            RectTransform ring = Ring(zone, "KolTabani", 300f, new Color(1f, 1f, 1f, 0.16f));
            RectTransform knob = Ring(zone, "KolBasi", 130f, new Color(1f, 1f, 1f, 0.42f));

            _stick = zoneGo.AddComponent<MarketJoystick>();
            _stick.Bind(ring, knob);

            // ---- top bar: the way out, and what this yard is ----
            Button exit = UiBuild.Btn(canvas, "CikisDugmesi", "‹  " + Loc.T("market.cik"), null,
                                      Chrome, 44, onExit);
            UiBuild.Anchor((RectTransform)exit.transform,
                           new Vector2(0.035f, 0.925f), new Vector2(0.34f, 0.982f));

            RectTransform card = UiBuild.Flat(canvas, "AvluKarti", Chrome,
                                              new Vector2(0.37f, 0.885f), new Vector2(0.965f, 0.982f));

            // The ore's own colour down the leading edge, so which yard you are in is answerable
            // without reading anything — the same cue the world map uses for the same islands.
            _accent = UiBuild.Flat(card, "Cizgi", FillColour, new Vector2(0f, 0f), new Vector2(0.016f, 1f));

            _yardText = Line(card, "AvluAdi", 38, TextAnchor.UpperLeft, 0.60f, 0.98f);
            _yardText.color = new Color(1f, 1f, 1f, 0.62f);
            _incomeText = Line(card, "GelirYazisi", 54, TextAnchor.UpperLeft, 0.30f, 0.64f);
            _stockText = Line(card, "StokYazisi", 36, TextAnchor.UpperLeft, 0.12f, 0.32f);

            UiBuild.Bar(card, "StokCubugu", new Color(1f, 1f, 1f, 0.12f), FillColour,
                        new Vector2(0.05f, 0.04f), new Vector2(0.97f, 0.115f), out _stockFill);
            _stockFillImage = _stockFill.GetComponent<Image>();

            // ---- bottom left: what is on your back ----
            RectTransform load = UiBuild.Flat(canvas, "SirtKarti", Chrome,
                                              new Vector2(0.035f, 0.035f), new Vector2(0.30f, 0.115f));
            _carryText = Line(load, "SirtYazisi", 52, TextAnchor.UpperLeft, 0.36f, 0.95f);
            UiBuild.Bar(load, "SirtCubugu", new Color(1f, 1f, 1f, 0.12f), new Color(0.44f, 0.72f, 0.95f, 0.95f),
                        new Vector2(0.08f, 0.14f), new Vector2(0.94f, 0.3f), out _carryFill);

            // ---- what the pad underfoot is selling. Hidden until they stand on one ----
            _padPanel = UiBuild.Flat(canvas, "PedBilgisi", new Color(0.09f, 0.10f, 0.14f, 0.94f),
                                     new Vector2(0.26f, 0.135f), new Vector2(0.74f, 0.225f));
            _padText = Line(_padPanel, "PedYazisi", 46, TextAnchor.UpperCenter, 0.46f, 0.94f);
            _padSubText = Line(_padPanel, "PedAltYazisi", 40, TextAnchor.UpperCenter, 0.08f, 0.46f);
            _padSubText.color = new Color(1f, 1f, 1f, 0.66f);
            _padPanel.gameObject.SetActive(false);

            Refresh();
        }

        /// <summary>
        /// A line of text occupying a horizontal band of its card. <see cref="UiBuild.Label"/> stretches
        /// to fill, which stacks every line on top of the last; this is the band version.
        /// </summary>
        private static Text Line(Transform parent, string name, int size, TextAnchor anchor,
                                 float bottom, float top)
        {
            Text text = UiBuild.Label(parent, name, "", size, anchor);
            var rect = (RectTransform)text.transform;
            rect.anchorMin = new Vector2(0.05f, bottom);
            rect.anchorMax = new Vector2(0.97f, top);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return text;
        }

        /// <summary>
        /// Points the readouts at a different yard. The hall is one scene, so walking through a doorway
        /// changes whose stock and whose income the player is looking at without anything reloading.
        /// </summary>
        public void SetYard(string yardKey, UpgradePad[] pads)
        {
            _yardKey = yardKey;
            _pads = pads;
            Refresh();
        }

        /// <summary>A soft round blob. The kit has no joystick art yet, so the greybox draws its own.</summary>
        private static RectTransform Ring(Transform parent, string name, float size, Color c)
        {
            RectTransform rect = UiBuild.Flat(parent, name, c, Vector2.zero, Vector2.zero);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            var img = rect.GetComponent<Image>();
            img.sprite = UiSkin.Flat;
            // Never a raycast target: the zone underneath owns the drag, and a blob that ate pointers
            // would cancel the very gesture that spawned it.
            img.raycastTarget = false;
            return rect;
        }

        private void Update()
        {
            if (_player != null && _stick != null) _player.SetMoveInput(_stick.Value);

            // The pad strip follows the player's feet and the load rides their back — neither can wait
            // for the once-a-second refresh the ledger's numbers can.
            RefreshPad();
            RefreshCarry();

            // Once a second, not per frame — these are the ledger's numbers and it only settles that often.
            _readoutIn -= Time.deltaTime;
            if (_readoutIn > 0f) return;
            _readoutIn = 1f;
            Refresh();
        }

        private void RefreshPad()
        {
            if (_pads == null || _padPanel == null || _market == null) return;

            UpgradePad standing = null;
            for (int i = 0; i < _pads.Length; i++)
                if (_pads[i] != null && _pads[i].Occupied) { standing = _pads[i]; break; }

            if (standing == null)
            {
                if (_padPanel.gameObject.activeSelf) _padPanel.gameObject.SetActive(false);
                return;
            }
            if (!_padPanel.gameObject.activeSelf) _padPanel.gameObject.SetActive(true);

            YardUpgrade kind = standing.Kind;
            string yard = standing.YardKey;
            _padText.text = Loc.T(YardPadLabels.PadKey(kind));

            if (_market.IsTrackMaxed(yard, kind))
            {
                _padSubText.text = Loc.T("market.maks");
                return;
            }
            _padSubText.text = _market.Level(yard, kind) + " / " + MarketPrices.MaxLevel(kind) +
                               "      $" + NumberFormatter.Format(new BigDouble(_market.Cost(yard, kind)));
        }

        private void RefreshCarry()
        {
            if (_carry == null || _carryText == null) return;
            int held = _carry.Count, capacity = Mathf.Max(1, _carry.Capacity);
            _carryText.text = held + " / " + capacity;
            if (_carryFill != null)
                _carryFill.anchorMax = new Vector2(Mathf.Clamp01(held / (float)capacity), 1f);
        }

        private void Refresh()
        {
            if (_market == null || string.IsNullOrEmpty(_yardKey)) return;

            double stock = _market.Stock(_yardKey);
            float fraction = Mathf.Clamp01((float)_market.StockFraction(_yardKey));

            // A yard whose pads have been full is throwing away everything its island sends. That is
            // the one thing in here the player has to be told rather than left to infer, so it gets
            // words and a colour instead of a percentage they would have to notice had stopped moving.
            bool spilling = _market.OverflowSeconds(_yardKey) > 0d;
            if (_stockText != null)
                _stockText.text = spilling
                    ? Loc.T("market.stok") + "   " + Loc.T("market.dolu")
                    : Loc.T("market.stok") + "   " + NumberFormatter.Format(new BigDouble(stock)) +
                      "   " + Mathf.RoundToInt(fraction * 100f) + "%";
            if (_stockFillImage != null)
                _stockFillImage.color = spilling ? SpillingColour : FillColour;

            if (_yardText != null)
                _yardText.text = Loc.Id("ada", _yardKey).ToUpperInvariant();
            if (_accent != null)
            {
                var image = _accent.GetComponent<Image>();
                if (image != null) image.color = WorldIslands.OreColorFor(_yardKey);
            }

            if (_incomeText != null)
            {
                // The AUTO badge is the promise the whole feature is built on, so it replaces the rate
                // multiplier rather than sitting beside it — once a yard is staffed the number stops
                // being something the player has to act on.
                string staffing = _market.IsMaxed(_yardKey)
                    ? "   " + Loc.T("market.otomatik")
                    : "   x" + _market.ServiceRate(_yardKey).ToString("0.00");
                _incomeText.text = "$" +
                                   NumberFormatter.Format(new BigDouble(_market.RatePerMin(_yardKey))) +
                                   " /dk" + staffing;
            }
            if (_stockFill != null) _stockFill.anchorMax = new Vector2(fraction, 1f);
        }
    }
}
