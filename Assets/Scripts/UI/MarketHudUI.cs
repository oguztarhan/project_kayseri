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
        /// <summary>The stock rail, in the plate's own accent orange.</summary>
        private static readonly Color RailColour = new Color(1f, 0.58f, 0.10f, 1f);

        /// <summary>
        /// What a card is painted when there is no skin to ask. Only ever used then: the kit art is
        /// pre-coloured and tinting it just muddies it, which is why <see cref="UiBuild.Box"/> ignores this
        /// the moment a real panel sprite exists.
        /// </summary>
        private static readonly Color Chrome = new Color(0.07f, 0.08f, 0.11f, 0.86f);
        private const string GlassPath = "UI/MarketLiquid/";

        private TMP_Text _stockText, _incomeText, _modeText, _fillText;
        private TMP_Text _padText, _padSubText, _carryText, _yardText, _cashText;
        private RectTransform _stockFill, _carryFill, _padPanel;
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
            Sprite backArt = Glass("back_button");
            Sprite currencyArt = Glass("currency_panel");
            Sprite infoArt = Glass("island_info_panel");
            Sprite counterArt = Glass("objective_counter");
            Sprite joystickArt = Glass("joystick");
            Sprite thumbArt = Glass("joystick_thumb");

            // ---- the stick's zone, underneath everything, and everything above it is deaf to touch ----
            // Full bleed, and the one thing here that stays OUTSIDE the safe area: this is a surface you
            // drag on, not something you have to see or hit precisely, and a drag lane that stopped short
            // of the cutout would just be a smaller lane. See Untappable for the other half of that deal.
            var zoneGo = new GameObject("KolAlani", typeof(RectTransform));
            var zone = (RectTransform)zoneGo.transform;
            zone.SetParent(canvas, false);
            UiBuild.Anchor(zone, Vector2.zero, new Vector2(1f, stickZoneHeight));
            MarketJoystick.DragSurface(zone);

            // Setin kendi kol basligi var artik; onceki hâlde bu bir beyaz lekeydi (aşağıdaki
            // Ring'in notuna bak). Basligin capi tabanin ortasindaki delige oturuyor.
            RectTransform ring = Ring(zone, "KolTabani", 300f, new Color(1f, 1f, 1f, 0.92f), joystickArt);
            RectTransform knob = Ring(zone, "KolBasi", 126f, Color.white, thumbArt);

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
            Pin(pad, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-26f, 24f),
                new Vector2(300f, 300f));
            // The ripple first, so the base and the head draw over it as it passes them.
            RectTransform ripple = Ring(pad, "Dalga", 300f, new Color(1f, 1f, 1f, 0.20f), joystickArt);
            Ring(pad, "Taban", 300f, new Color(1f, 1f, 1f, 0.92f), joystickArt);
            // Taban ortasi delik bir halka; basligi olmadan duran kol bos bir cember gibi duruyor
            // ve basilacak bir sey oldugu okunmuyor. Ped surukleme baslayinca komple kapaniyor.
            Ring(pad, "PedBasligi", 126f, new Color(1f, 1f, 1f, 0.92f), thumbArt);
            _stick.BindRest(pad, ripple);

            // ---- top bar: the way out, the wallet, and what this yard is ----
            // Twice the height it was. It is the only way out of a scene with no other exit, it is the
            // control a player reaches for while their other thumb is on the stick, and it was a strip
            // less than six per cent of the screen tall. Kit yellow, the same art as MARKETE GİR — the
            // door in and the door out wearing one colour is the cheapest wayfinding there is.
            Button exit = Chip(safe, "CikisDugmesi", "‹  " + Loc.T("market.cik"), backArt,
                               40f, onExit);
            // Ölçüler setin kendi en-boy oranından: parçalar preserveAspect ile çiziliyor, rect
            // oranı tutmazsa sanat kutunun içinde küçülüp ortalanıyor ve üstüne koyduğumuz yazı
            // sanatın dışına düşüyor.
            Pin((RectTransform)exit.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(20f, -18f), new Vector2(360f, 132f));
            TMP_Text exitText = exit.GetComponentInChildren<TMP_Text>();
            if (exitText != null) Box(exitText, 0.14f, 0.26f, 0.86f, 0.76f);

            // What is in the wallet, in the kit's counter capsule. Every pad in the yard is priced in
            // cash and this screen used to be the one place in the game that would not tell you how much
            // you had — you bought upgrades by walking onto them and hoping.
            // Sikke artik hapin kendi sanatinda basili; ustune bir tane daha koymak iki sikke
            // demekti. Yazi yalnizca sagdaki koyu okuma penceresine giriyor.
            RectTransform purse = UiBuild.Box(safe, "ParaKarti", Chrome,
                                              new Vector2(0.255f, 0.855f), new Vector2(0.475f, 0.975f));
            Skin(purse, currencyArt);
            Pin(purse, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(400f, -18f), new Vector2(330f, 134f));
            _cashText = Line(purse, "ParaYazisi", 38f, TextAlignmentOptions.Center, 0.28f, 0.72f);
            Box(_cashText, 0.33f, 0.28f, 0.91f, 0.72f);

            // The card is the kit's slotted plate: a name across the top, four recessed windows in
            // two rows, a rail along the bottom. Every rect below is measured off that art rather
            // than spaced by eye — a label that misses its window reads as a bug in the panel.
            //
            // The kit ships the plate with Turkish burned into it. Those letters are painted out in
            // Tools/ui/market_seti.py and redrawn here through Loc, because the game runs in eleven
            // languages and baked type only ever speaks one.
            RectTransform card = UiBuild.Box(safe, "AvluKarti", Chrome,
                                             new Vector2(0.495f, 0.80f), new Vector2(0.978f, 0.975f));
            Skin(card, infoArt);
            Pin(card, Vector2.one, Vector2.one, new Vector2(-20f, -18f), new Vector2(520f, 395f));

            _yardText = Cell(card, "AvluAdi", 34f, TextAlignmentOptions.Left,
                             0.115f, 0.792f, 0.640f, 0.882f);
            _yardText.color = new Color(1f, 0.72f, 0.16f, 1f);   // setin turuncusu

            Caption(card, "GelirEtiketi", Loc.T("market.gelir"), 0.125f, 0.655f, 0.484f, 0.742f);
            _incomeText = Cell(card, "GelirYazisi", 30f, TextAlignmentOptions.Center,
                               0.135f, 0.528f, 0.474f, 0.650f);

            Caption(card, "ModEtiketi", Loc.T("market.mod"), 0.542f, 0.655f, 0.900f, 0.742f);
            _modeText = Cell(card, "ModYazisi", 30f, TextAlignmentOptions.Center,
                             0.552f, 0.528f, 0.875f, 0.650f);

            Caption(card, "StokEtiketi", Loc.T("market.stok"), 0.125f, 0.398f, 0.484f, 0.485f);
            _stockText = Cell(card, "StokYazisi", 30f, TextAlignmentOptions.Center,
                              0.135f, 0.272f, 0.474f, 0.393f);

            Caption(card, "DolulukEtiketi", Loc.T("market.doluluk"), 0.542f, 0.398f, 0.920f, 0.485f);
            _fillText = Cell(card, "DolulukYazisi", 30f, TextAlignmentOptions.Center,
                             0.552f, 0.272f, 0.875f, 0.393f);

            UiBuild.Bar(card, "StokCubugu", new Color(0f, 0f, 0f, 0f), FillColour,
                        new Vector2(0.152f, 0.152f), new Vector2(0.861f, 0.188f), out _stockFill);
            _stockFillImage = _stockFill.GetComponent<Image>();

            // ---- bottom left: what is on your back ----
            RectTransform load = UiBuild.Box(safe, "SirtKarti", Chrome,
                                             new Vector2(0.022f, 0.035f), new Vector2(0.235f, 0.155f));
            Skin(load, counterArt);
            Pin(load, Vector2.zero, Vector2.zero, new Vector2(20f, 18f), new Vector2(320f, 123f));
            // Isci ikonu sayacin kendi sanatinda; yazi ve cubuk sagdaki okuma penceresine giriyor.
            _carryText = Line(load, "SirtYazisi", 44f, TextAlignmentOptions.Center, 0.38f, 0.76f);
            Box(_carryText, 0.40f, 0.38f, 0.92f, 0.76f);
            UiBuild.Bar(load, "SirtCubugu", new Color(0f, 0f, 0f, 0f), new Color(0.44f, 0.72f, 0.95f, 0.95f),
                        new Vector2(0.42f, 0.27f), new Vector2(0.90f, 0.33f), out _carryFill);

            // ---- what the pad underfoot is selling. Hidden until they stand on one ----
            _padPanel = UiBuild.Box(safe, "PedBilgisi", new Color(0.09f, 0.10f, 0.14f, 0.94f),
                                    new Vector2(0.315f, 0.035f), new Vector2(0.685f, 0.175f));
            Skin(_padPanel, counterArt);
            Pin(_padPanel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 18f),
                new Vector2(470f, 181f));
            _padText = Cell(_padPanel, "PedYazisi", 32f, TextAlignmentOptions.Center,
                            0.38f, 0.50f, 0.93f, 0.76f);
            _padSubText = Cell(_padPanel, "PedAltYazisi", 28f, TextAlignmentOptions.Center,
                               0.38f, 0.25f, 0.93f, 0.50f);
            _padSubText.color = new Color(1f, 1f, 1f, 0.88f);
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

        private static Sprite Glass(string name) => Resources.Load<Sprite>(GlassPath + name);

        /// <summary>Applies one of the authored glass plates without slicing or recolouring its glow.</summary>
        private static void Skin(RectTransform rect, Sprite art)
        {
            if (rect == null || art == null) return;
            var image = rect.GetComponent<Image>();
            if (image == null) return;
            image.sprite = art;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = Color.white;
        }

        /// <summary>Positions a glass component at a corner/edge with its authored aspect ratio intact.</summary>
        private static void Pin(RectTransform rect, Vector2 anchor, Vector2 pivot,
                                Vector2 offset, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
        }

        /// <summary>Pins a label to an exact rectangle of its card, in fractions of that card.</summary>
        private static void Box(TMP_Text text, float left, float bottom, float right, float top)
        {
            var rect = (RectTransform)text.transform;
            rect.anchorMin = new Vector2(left, bottom);
            rect.anchorMax = new Vector2(right, top);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>A readout sitting in one of the plate's recessed windows.</summary>
        private static TMP_Text Cell(Transform parent, string name, float size, TextAlignmentOptions align,
                                     float left, float bottom, float right, float top)
        {
            TMP_Text text = Line(parent, name, size, align, bottom, top);
            Box(text, left, bottom, right, top);
            return text;
        }

        /// <summary>
        /// One of the plate's engraved slot headings, redrawn through <see cref="Loc"/>.
        ///
        /// The kit has these burned into the art in Turkish. They are painted out when the sprite is
        /// cut and written here instead, so the panel reads in whatever language the player picked.
        /// </summary>
        private static void Caption(Transform parent, string name, string label,
                                    float left, float bottom, float right, float top)
        {
            TMP_Text text = Cell(parent, name, 26f, TextAlignmentOptions.Left, left, bottom, right, top);
            text.text = label;
            text.color = new Color(0.93f, 0.94f, 0.96f, 1f);
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
            text.enableAutoSizing = true;
            text.fontSizeMin = Mathf.Max(18f, size * 0.58f);
            text.fontSizeMax = size;
            text.alignment = align;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.color = Color.white;
            text.fontStyle = FontStyles.Bold;
            text.outlineColor = new Color32(8, 22, 44, 225);
            text.outlineWidth = 0.18f;
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
            image.type = art != null ? Image.Type.Simple : Image.Type.Sliced;
            image.preserveAspect = art != null;
            image.color = art != null || UiSkin.HasArt ? Color.white : Chrome;
            TMP_Text text = Line(go.transform, "Yazi", size, TextAlignmentOptions.Center, 0f, 1f);
            text.text = label;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            if (onClick != null) button.onClick.AddListener(onClick);
            return button;
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
        /// The stick's base and its thumb. Both are kit art now — the note about a greybox blob is
        /// gone with it, but the anchoring below has not changed and still cannot.
        ///
        /// Anchored to the CENTRE of its parent, and that is a fix rather than a preference. The stick
        /// drives these by writing the thumb's position straight into anchoredPosition, and that position
        /// comes out of ScreenPointToLocalPointInRectangle, which measures from the zone's pivot — its
        /// middle. Anchored to the corner, as these were, the two disagreed by half a screen: put a thumb
        /// in the middle of the zone and the ring drew itself down in the bottom-left corner. Which is a
        /// large part of why nobody could tell there was a stick at all.
        /// </summary>
        private static RectTransform Ring(Transform parent, string name, float size, Color c, Sprite art)
        {
            RectTransform rect = UiBuild.Flat(parent, name, c, Vector2.zero, Vector2.zero);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(size, size);
            var img = rect.GetComponent<Image>();
            img.sprite = art != null ? art : UiSkin.Flat;
            img.type = art != null ? Image.Type.Simple : Image.Type.Sliced;
            img.preserveAspect = art != null;
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
            // The plate names each window, so the readouts are bare values — repeating the heading
            // inside its own slot is the one thing the engraved art already does.
            if (_stockText != null)
                _stockText.text = NumberFormatter.Format(new BigDouble(stock));
            if (_fillText != null)
                _fillText.text = spilling
                    ? Loc.T("market.dolu")
                    : Mathf.RoundToInt(fraction * 100f) + "%";
            if (_fillText != null)
                _fillText.color = spilling ? SpillingColour : Color.white;

            // The rail is kit orange rather than the yard's ore colour. Ore colour was the obvious
            // choice and it is wrong here: coal's brand is very nearly black, so a full rail on a
            // graphite plate read as an empty one. Orange is the plate's own accent and always reads.
            if (_stockFillImage != null)
                _stockFillImage.color = spilling ? SpillingColour : RailColour;

            if (_yardText != null)
                _yardText.text = Loc.Id("ada", _yardKey).ToUpperInvariant();

            if (_incomeText != null)
                _incomeText.text = "$" +
                                   NumberFormatter.Format(new BigDouble(_market.RatePerMin(_yardKey))) +
                                   " /dk";
            if (_modeText != null)
            {
                // The AUTO badge is the promise the whole feature is built on, so it replaces the rate
                // multiplier rather than sitting beside it — once a yard is staffed the number stops
                // being something the player has to act on.
                bool auto = _market.IsMaxed(_yardKey);
                _modeText.text = auto ? Loc.T("market.otomatik")
                                      : "x" + _market.ServiceRate(_yardKey).ToString("0.00");
                _modeText.color = auto ? new Color(0.42f, 0.92f, 0.52f, 1f) : Color.white;
            }
            if (_stockFill != null) _stockFill.anchorMax = new Vector2(fraction, 1f);
        }
    }
}
