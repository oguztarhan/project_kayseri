using Game.Core;
using Game.Gameplay;
using Game.Systems;
using TMPro;
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
    ///
    /// IT WEARS THE GAME'S OWN INTERFACE, and getting there took three things rather than a restyle.
    /// The cards and the button ask <see cref="UiSkin"/> for the kit's panel and button art, which they
    /// always did — but the skin is authored in Main and this scene runs with Main unloaded, so until
    /// UiSkin started holding its art statically every one of them fell back to a flat grey rectangle.
    /// The text is TMP, because TMP's project default font is Baloo2, the font every authored screen is
    /// set in; the legacy Text this used before could only ever be Arial. And the content sits inside a
    /// <see cref="SafeArea"/>, like every authored screen's does.
    ///
    /// That last one was not cosmetic. The player settings render this game into the display cutout, and
    /// in landscape a cutout is on the LEFT — exactly where the way out of the market was pinned. On a
    /// phone with a notch, part of that button was under glass that does not exist and could not be
    /// pressed. It worked on every phone without one, which is why it was reported as working sometimes.
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

        /// <summary>
        /// What a card is painted when there is no skin to ask. Only ever used then: the kit art is
        /// pre-coloured and tinting it just muddies it, which is why <see cref="UiBuild.Box"/> ignores this
        /// the moment a real panel sprite exists.
        /// </summary>
        private static readonly Color Chrome = new Color(0.07f, 0.08f, 0.11f, 0.86f);

        private TMP_Text _stockText, _incomeText, _padText, _padSubText, _carryText, _yardText, _cashText;
        private RectTransform _stockFill, _carryFill, _padPanel, _accent;
        private Image _stockFillImage;
        private CarryStack _carry;
        private WalletService _wallet;
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
            _wallet = ServiceLocator.Get<WalletService>();

            RectTransform canvas = UiBuild.Canvas(transform, "MarketHudKanvas", sortingOrder);

            // ---- the stick's zone, underneath everything, and everything above it is deaf to touch ----
            // Full bleed, and the one thing here that stays OUTSIDE the safe area: this is a surface you
            // drag on, not something you have to see or hit precisely, and a drag lane that stopped short
            // of the cutout would just be a smaller lane. See Untappable for the other half of that deal.
            var zoneGo = new GameObject("KolAlani", typeof(RectTransform));
            var zone = (RectTransform)zoneGo.transform;
            zone.SetParent(canvas, false);
            UiBuild.Anchor(zone, Vector2.zero, new Vector2(1f, stickZoneHeight));
            MarketJoystick.DragSurface(zone);

            RectTransform ring = Ring(zone, "KolTabani", 300f, new Color(1f, 1f, 1f, 0.16f));
            RectTransform knob = Ring(zone, "KolBasi", 130f, new Color(1f, 1f, 1f, 0.42f));

            _stick = zoneGo.AddComponent<MarketJoystick>();
            _stick.Bind(ring, knob);

            // ---- everything readable or tappable hangs off here ----
            // The screen inside the notches and the gesture bar, which is what the authored screens all
            // do and what this one was missing. See the class note: on a landscape phone with a cutout,
            // the way out used to be partly underneath it.
            var safeGo = new GameObject("GuvenliAlan", typeof(RectTransform));
            var safe = (RectTransform)safeGo.transform;
            safe.SetParent(canvas, false);
            UiBuild.Anchor(safe, Vector2.zero, Vector2.one);
            safeGo.AddComponent<SafeArea>();

            // ---- the resting pad: what tells a first-time player there is anything to drive ----
            // In the safe area because it has to be SEEN, unlike the drag lane it sits over, which is full
            // bleed. Bottom right by choice: the left corner is the carry readout's, and this is the thumb
            // that is not already holding the phone up.
            //
            // Deaf to touch, all of it, like every other piece of decoration on this screen. The drag
            // surface underneath is what answers — so a thumb landing on the pad starts the real stick
            // exactly where the pad is, and the hint behaves like the fixed control it looks like.
            var padGo = new GameObject("YuruyusPedi", typeof(RectTransform));
            var pad = (RectTransform)padGo.transform;
            pad.SetParent(safe, false);
            pad.anchorMin = pad.anchorMax = new Vector2(0.875f, 0.21f);
            pad.pivot = new Vector2(0.5f, 0.5f);
            pad.sizeDelta = new Vector2(320f, 320f);
            // The ripple first, so the base and the head draw over it as it passes them.
            RectTransform ripple = Ring(pad, "Dalga", 300f, new Color(1f, 1f, 1f, 0.22f));
            Ring(pad, "Taban", 300f, new Color(1f, 1f, 1f, 0.10f));
            Ring(pad, "Bas", 130f, new Color(1f, 1f, 1f, 0.30f));
            _stick.BindRest(pad, ripple);

            // ---- top bar: the way out, the wallet, and what this yard is ----
            // Twice the height it was. It is the only way out of a scene with no other exit, it is the
            // control a player reaches for while their other thumb is on the stick, and it was a strip
            // less than six per cent of the screen tall. Kit yellow, the same art as MARKETE GİR — the
            // door in and the door out wearing one colour is the cheapest wayfinding there is.
            Button exit = Chip(safe, "CikisDugmesi", "‹  " + Loc.T("market.cik"), UiSkin.ButtonYellow,
                               44f, onExit);
            UiBuild.Anchor((RectTransform)exit.transform,
                           new Vector2(0.022f, 0.855f), new Vector2(0.235f, 0.975f));

            // What is in the wallet, in the kit's counter capsule. Every pad in the yard is priced in
            // cash and this screen used to be the one place in the game that would not tell you how much
            // you had — you bought upgrades by walking onto them and hoping.
            RectTransform purse = UiBuild.Box(safe, "ParaKarti", Chrome,
                                              new Vector2(0.255f, 0.855f), new Vector2(0.475f, 0.975f));
            var purseImage = purse.GetComponent<Image>();
            if (purseImage != null) purseImage.sprite = UiSkin.Pill;
            Icon(purse, UiSkin.Coin);
            _cashText = Line(purse, "ParaYazisi", 46f, TextAlignmentOptions.Left, 0.06f, 0.94f);
            var cashRect = (RectTransform)_cashText.transform;
            cashRect.anchorMin = new Vector2(0.30f, 0.06f);
            cashRect.anchorMax = new Vector2(0.94f, 0.94f);

            RectTransform card = UiBuild.Box(safe, "AvluKarti", Chrome,
                                             new Vector2(0.495f, 0.80f), new Vector2(0.978f, 0.975f));

            // The ore's own colour down the leading edge, so which yard you are in is answerable
            // without reading anything — the same cue the world map uses for the same islands.
            _accent = UiBuild.Flat(card, "Cizgi", FillColour, new Vector2(0.02f, 0.12f),
                                   new Vector2(0.036f, 0.88f));

            _yardText = Line(card, "AvluAdi", 34f, TextAlignmentOptions.TopLeft, 0.62f, 0.96f);
            _yardText.color = new Color(1f, 1f, 1f, 0.66f);
            _incomeText = Line(card, "GelirYazisi", 50f, TextAlignmentOptions.TopLeft, 0.33f, 0.66f);
            _stockText = Line(card, "StokYazisi", 32f, TextAlignmentOptions.TopLeft, 0.16f, 0.35f);

            UiBuild.Bar(card, "StokCubugu", new Color(1f, 1f, 1f, 0.12f), FillColour,
                        new Vector2(0.06f, 0.07f), new Vector2(0.95f, 0.13f), out _stockFill);
            _stockFillImage = _stockFill.GetComponent<Image>();

            // ---- bottom left: what is on your back ----
            RectTransform load = UiBuild.Box(safe, "SirtKarti", Chrome,
                                             new Vector2(0.022f, 0.035f), new Vector2(0.235f, 0.155f));
            _carryText = Line(load, "SirtYazisi", 48f, TextAlignmentOptions.Left, 0.34f, 0.92f);
            UiBuild.Bar(load, "SirtCubugu", new Color(1f, 1f, 1f, 0.12f), new Color(0.44f, 0.72f, 0.95f, 0.95f),
                        new Vector2(0.09f, 0.16f), new Vector2(0.93f, 0.26f), out _carryFill);

            // ---- what the pad underfoot is selling. Hidden until they stand on one ----
            _padPanel = UiBuild.Box(safe, "PedBilgisi", new Color(0.09f, 0.10f, 0.14f, 0.94f),
                                    new Vector2(0.315f, 0.035f), new Vector2(0.685f, 0.175f));
            _padText = Line(_padPanel, "PedYazisi", 42f, TextAlignmentOptions.Top, 0.48f, 0.92f);
            _padSubText = Line(_padPanel, "PedAltYazisi", 36f, TextAlignmentOptions.Top, 0.10f, 0.48f);
            _padSubText.color = new Color(1f, 1f, 1f, 0.66f);
            _padPanel.gameObject.SetActive(false);

            Untappable(purse);
            Untappable(card);
            Untappable(load);
            Untappable(_padPanel);

            Refresh();
        }

        /// <summary>
        /// Makes a card, and everything drawn inside it, invisible to the pointer.
        ///
        /// The stick's drag surface lies UNDER these, so a card that answers a touch is a patch of screen
        /// where the joystick does not work. Both readouts sit in the bottom corners — which is precisely
        /// where a thumb lands — and neither of them has anything on it to press.
        /// </summary>
        private static void Untappable(RectTransform card)
        {
            Graphic[] parts = card.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < parts.Length; i++) parts[i].raycastTarget = false;
        }

        /// <summary>
        /// A line of text occupying a horizontal band of its card.
        ///
        /// TMP rather than <see cref="UiBuild.Label"/>'s legacy Text, and the reason is the font: TMP's
        /// project default font asset IS Baloo2, the one every authored screen in the game is set in, so a
        /// label built with nothing wired comes out in the game's own type. UiBuild.Label can only ever be
        /// Arial, which is what made this screen look like a different game's debug overlay.
        /// </summary>
        private static TMP_Text Line(Transform parent, string name, float size,
                                     TextAlignmentOptions align, float bottom, float top)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.alignment = align;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.color = Color.white;
            text.raycastTarget = false;      // never eat a tap meant for whatever is under it
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.06f, bottom);
            rect.anchorMax = new Vector2(0.96f, top);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return text;
        }

        /// <summary>
        /// A button in the kit's art with a TMP label on it. <see cref="UiBuild.Btn"/> would do the same
        /// job with a legacy Text child, and one Arial word on a screen of Baloo2 is the one that shows.
        /// </summary>
        private static Button Chip(Transform parent, string name, string label, Sprite art, float size,
                                   UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = art != null ? art : UiSkin.Flat;
            image.type = Image.Type.Sliced;
            image.color = UiSkin.HasArt ? Color.white : Chrome;
            TMP_Text text = Line(go.transform, "Yazi", size, TextAlignmentOptions.Center, 0f, 1f);
            text.text = label;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            if (onClick != null) button.onClick.AddListener(onClick);
            return button;
        }

        /// <summary>The coin on the purse capsule. Skipped entirely when the kit has no icon wired.</summary>
        private static void Icon(Transform parent, Sprite sprite)
        {
            if (sprite == null) return;
            var go = new GameObject("Ikon", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            UiBuild.Anchor((RectTransform)go.transform, new Vector2(0.05f, 0.14f), new Vector2(0.28f, 0.86f));
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

        /// <summary>
        /// A soft round blob. The kit has no joystick art yet, so the greybox draws its own.
        ///
        /// Anchored to the CENTRE of its parent, and that is a fix rather than a preference. The stick
        /// drives these by writing the thumb's position straight into anchoredPosition, and that position
        /// comes out of ScreenPointToLocalPointInRectangle, which measures from the zone's pivot — its
        /// middle. Anchored to the corner, as these were, the two disagreed by half a screen: put a thumb
        /// in the middle of the zone and the ring drew itself down in the bottom-left corner. Which is a
        /// large part of why nobody could tell there was a stick at all.
        /// </summary>
        private static RectTransform Ring(Transform parent, string name, float size, Color c)
        {
            RectTransform rect = UiBuild.Flat(parent, name, c, Vector2.zero, Vector2.zero);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
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
            // Off the same once-a-second beat as the rest. The wallet has an event, but a pad being stood
            // on fires it several times a second and a counter that repainted on every one of them would
            // be the only thing in this HUD doing per-frame text work.
            if (_cashText != null && _wallet != null)
                _cashText.text = "$" + NumberFormatter.Format(_wallet.Cash);

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
