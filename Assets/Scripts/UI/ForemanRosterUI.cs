using Game.Core;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The foreman roster screen: eight cards, one per station, each showing who is hired, how far
    /// they are levelled, and what the next step costs.
    ///
    /// BUILT IN CODE rather than authored as a prefab, the same way <see cref="UiBuild"/>'s other
    /// screens are. That is a deliberate trade: an authored sheet would look better, but the roster
    /// is exactly eight identical cards driven off <see cref="Foremen"/>'s own tables, and a prefab
    /// would mean eight hand-wired copies that fall out of step the moment a slot's rarity changes.
    /// The card layout here reads the tables, so it cannot disagree with the maths.
    ///
    /// The art is the only thing wired in the Inspector — the white card, the blue title ribbon, the
    /// eight portraits. Everything else is fractions of the parent, so the sheet fits any aspect.
    ///
    /// Refreshed on open and on <see cref="ForemanService.RosterChanged"/> — never per frame. Nothing
    /// on this screen animates, and the wallet only moves when the player presses something on it.
    /// </summary>
    public sealed class ForemanRosterUI : MonoBehaviour
    {
        [Header("Yerleşim")]
        [Tooltip("Kart ızgarası: yatayda kaç sütun. Sekiz slot 4x2 olarak oturur.")]
        [SerializeField] private int columns = 4;
        [SerializeField] private int sortingOrder = 105;

        [Header("Görseller")]
        [Tooltip("Kart gövdesi — MaviSet/panel_beyaz.")]
        [SerializeField] private Sprite cardPanel;
        [Tooltip("Başlık şeridi — MaviSet/serit_mavi.")]
        [SerializeField] private Sprite ribbon;
        [Tooltip("İşe al / seviye atla düğmesi — MaviSet/btn_hap_mavi.")]
        [SerializeField] private Sprite actionButton;
        [Tooltip("Kapat düğmesi — MaviSet/btn_kapat_yeni.")]
        [SerializeField] private Sprite closeIcon;
        [Tooltip("Kart çubuğunun yatağı ve dolgusu — Gostergeler/slider_yatak, bar_dolgu.")]
        [SerializeField] private Sprite barTrack;
        [SerializeField] private Sprite barFill;
        [Tooltip("Üstteki iki gösterge — MaviSet/gosterge_grafit. HUD'un para ve elmas hapıyla aynı parça.")]
        [SerializeField] private Sprite chipPill;
        [Tooltip("Bedelin solundaki elmas ikonu — Ikonlar/ikon_elmas.")]
        [SerializeField] private Sprite gemIcon;
        [Tooltip("Kesenin solundaki elmas — HUD'un kullandığı diamond_128x128, artı rozetiyle birlikte.")]
        [SerializeField] private Sprite purseGem;
        [Tooltip("Sekiz portre, istasyon sırasıyla: maden, tren, depo, cevher kamyonu, fabrika, yük kamyonu, pazar, enerji santrali.")]
        [SerializeField] private Sprite[] portraits;

        [Header("Renkler")]
        [SerializeField] private Color scrim = new Color(0.04f, 0.05f, 0.08f, 0.86f);
        [SerializeField] private Color cardHired = new Color(0.16f, 0.19f, 0.27f, 1f);
        [SerializeField] private Color cardLocked = new Color(0.11f, 0.12f, 0.17f, 1f);
        [Tooltip("Sıradanlık renkleri: Common, Rare, Epic.")]
        [SerializeField] private Color commonTint = new Color(0.62f, 0.68f, 0.78f, 1f);
        [SerializeField] private Color rareTint = new Color(0.33f, 0.62f, 0.92f, 1f);
        [SerializeField] private Color epicTint = new Color(0.72f, 0.45f, 0.95f, 1f);

        /// <summary>The rail button's icon, loaded at runtime — this screen has no Inspector to wire.</summary>
        private const string OpenerIconResource = "UI/Buttons/ustabasi";

        // The card is white art now, so every label on it has to be ink rather than paper.
        private static readonly Color Ink = new Color(0.09f, 0.14f, 0.24f, 1f);
        private static readonly Color InkSoft = new Color(0.36f, 0.42f, 0.52f, 1f);
        private static readonly Color InkFaint = new Color(0.58f, 0.63f, 0.71f, 1f);
        /// <summary>The two header chips are graphite, so their numbers go the other way.</summary>
        private static readonly Color Paper = new Color(0.96f, 0.97f, 1f, 1f);

        /// <summary>
        /// Where the ribbon's flat band sits, measured on the sprite: its middle is 0.677 up from the
        /// bottom, because the tails hang below it. A label centred in the rect lands on the tails.
        /// </summary>
        private const float RibbonBand = 0.677f;

        /// <summary>
        /// The decimal separator is the game's, not the handset's. Left to the current culture, a
        /// Turkish phone draws "×1,50" here while the wallet an inch away draws "1.5K" out of
        /// <see cref="Game.Core.NumberFormatter"/> — two number languages on one screen. The
        /// language of this game is the one the player picked, not the one the device was sold in,
        /// so the formatting has no business coming from the device either.
        /// </summary>
        private static readonly System.Globalization.CultureInfo Culture =
            System.Globalization.CultureInfo.InvariantCulture;

        private ForemanService _foremen;
        private WalletService _wallet;
        private RectTransform _root;
        private Text _multiplier;
        private Text _balance;
        private TMP_Text _openerCount;

        // One entry per slot, built once. No allocation after Build().
        private readonly Text[] _name = new Text[Foremen.Count];
        private readonly Text[] _level = new Text[Foremen.Count];
        private readonly Text[] _effect = new Text[Foremen.Count];
        private readonly Text[] _cards = new Text[Foremen.Count];
        private readonly Text[] _cost = new Text[Foremen.Count];
        private readonly Button[] _action = new Button[Foremen.Count];
        private readonly Text[] _actionText = new Text[Foremen.Count];
        private readonly Image[] _card = new Image[Foremen.Count];
        private readonly Image[] _portrait = new Image[Foremen.Count];
        private readonly Image[] _fill = new Image[Foremen.Count];
        private readonly Image[] _gem = new Image[Foremen.Count];

        private void Awake()
        {
            _foremen = ServiceLocator.Get<ForemanService>();
            _wallet = ServiceLocator.Get<WalletService>();
            Build();
            BuildOpener();
            if (_foremen != null) _foremen.RosterChanged += OnRosterChanged;
            Hide();
            RefreshOpener();
        }

        private void OnDestroy()
        {
            if (_foremen != null) _foremen.RosterChanged -= OnRosterChanged;
        }

        private void OnRosterChanged(int station) { Refresh(); RefreshOpener(); }

        /// <summary>
        /// The opener sits in the HUD's bottom row, next to the goals opener - see
        /// <see cref="HudUI.AttachBottomButton"/> for why a code-built screen borrows a row button's
        /// rect instead of anchoring itself to a fraction of the screen.
        ///
        /// How many slots are hired rides under it in the row's own counter chip, so the button says
        /// whether there is anything to come back for.
        /// </summary>
        private void BuildOpener()
        {
            HudUI hud = FindAnyObjectByType<HudUI>(FindObjectsInactive.Include);
            if (hud == null) return;

            Button open = hud.AttachBottomButton(1, "BtnUstabasi", Resources.Load<Sprite>(OpenerIconResource), Show);
            if (open == null) return;

            GameObject chip = hud.AttachCounterChip(open);
            if (chip != null) _openerCount = chip.GetComponentInChildren<TMP_Text>(true);
        }

        private void RefreshOpener()
        {
            if (_openerCount == null || _foremen == null) return;
            _openerCount.text = string.Format("{0}/{1}", _foremen.HiredCount, Foremen.Count);
        }

        public void Show() { if (_root != null) _root.gameObject.SetActive(true); Refresh(); }
        public void Hide() { if (_root != null) _root.gameObject.SetActive(false); }
        public void Toggle()
        {
            if (_root == null) return;
            if (_root.gameObject.activeSelf) Hide(); else Show();
        }

        // ------------------------------------------------------------------ build
        private void Build()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "UstabasiKanvas", sortingOrder);
            _root = UiBuild.Flat(canvas, "Karartma", scrim, Vector2.zero, Vector2.one);

            BuildHeader();

            int rows = (Foremen.Count + columns - 1) / columns;
            const float left = 0.035f, right = 0.965f, top = 0.845f, bottom = 0.035f;
            float cellW = (right - left) / columns, cellH = (top - bottom) / rows;
            const float padX = 0.007f, padY = 0.014f;

            for (int s = 0; s < Foremen.Count; s++)
            {
                int col = s % columns, row = s / columns;
                var aMin = new Vector2(left + col * cellW + padX, top - (row + 1) * cellH + padY);
                var aMax = new Vector2(left + (col + 1) * cellW - padX, top - row * cellH - padY);
                BuildCard(s, aMin, aMax);
            }
        }

        /// <summary>The blue ribbon across the top, the income multiplier left of it, the purse right.</summary>
        private void BuildHeader()
        {
            RectTransform band = Art(_root, "Serit", ribbon, new Vector2(0.355f, 0.855f), new Vector2(0.645f, 0.995f));
            UiBuild.Label(Slot(band, "Yazi", new Vector2(0.13f, RibbonBand - 0.13f),
                                        new Vector2(0.87f, RibbonBand + 0.13f)),
                                   "Text", Loc.T("ustabasi.baslik"), 38, TextAnchor.MiddleCenter);

            // Both chips are the HUD's own graphite pill. They sit on the same line as the HUD's
            // money and gem counters and used to be white, so the top of the screen read as two
            // different games stacked on each other.
            RectTransform sol = Chip(_root, "Carpan", new Vector2(0.035f, 0.885f), new Vector2(0.175f, 0.968f));
            _multiplier = UiBuild.Label(Slot(sol, "Yazi", new Vector2(0.08f, 0f), new Vector2(0.92f, 1f)),
                                        "Text", string.Empty, 32, TextAnchor.MiddleCenter);
            _multiplier.color = Paper;

            RectTransform sag = Chip(_root, "Kese", new Vector2(0.672f, 0.885f), new Vector2(0.845f, 0.968f));
            // The gem overhangs the pill's left cap, the way it does on the HUD — inside the capsule
            // it would be a diamond in a dark box, and the plus badge would lose its edge.
            Icon(sag, "Elmas", purseGem != null ? purseGem : gemIcon,
                 new Vector2(-0.06f, 0.02f), new Vector2(0.26f, 1.02f));
            _balance = UiBuild.Label(Slot(sag, "Yazi", new Vector2(0.30f, 0f), new Vector2(0.92f, 1f)),
                                     "Text", string.Empty, 32, TextAnchor.MiddleCenter);
            _balance.color = Paper;

            Button close = UiBuild.Btn(_root, "Kapat", string.Empty, closeIcon != null ? closeIcon : UiSkin.ButtonGrey,
                                       cardLocked, 34, Hide);
            var closeImage = close.GetComponent<Image>();
            closeImage.type = Image.Type.Simple;
            closeImage.preserveAspect = true;
            // Sağ kenarda değil: HUD'un ayarlar dişlisi 120 sıralı kanvasta, bu ekranın üstünde
            // çiziliyor ve tam köşeye konan kapat düğmesinin üstüne biniyor.
            UiBuild.Anchor((RectTransform)close.transform, new Vector2(0.878f, 0.878f), new Vector2(0.938f, 0.975f));
        }

        private void BuildCard(int station, Vector2 aMin, Vector2 aMax)
        {
            RectTransform card = Art(_root, "Kart_" + station, cardPanel, aMin, aMax);
            _card[station] = card.GetComponent<Image>();
            if (cardPanel == null) _card[station].color = cardHired;

            // The rarity mark. Fixed per slot, so it is set once here and never refreshed. It is a
            // rule under the name rather than a tab on the card's edge: the white panel's rim carries
            // its own soft glow, and anything laid across it reads as a stray rectangle.
            UiBuild.Flat(card, "Sirad", TintFor(station),
                         new Vector2(0.575f, 0.752f), new Vector2(0.820f, 0.768f));

            _portrait[station] = Icon(card, "Portre", Portrait(station),
                                      new Vector2(0.01f, 0.245f), new Vector2(0.44f, 0.900f));

            _name[station] = UiBuild.Label(
                Slot(card, "Ad", new Vector2(0.44f, 0.775f), new Vector2(0.930f, 0.905f)),
                "Text", string.Empty, 28, TextAnchor.MiddleCenter);
            // "CEVHER KAMYONLARI" tek satirda karta sigmiyor; en uzun ad ne kadar kuculmesi
            // gerekiyorsa o kadar kuculuyor, tasip komsu karta girmiyor.
            Fit(_name[station], 15, 28);

            _level[station] = UiBuild.Label(
                Slot(card, "Seviye", new Vector2(0.44f, 0.635f), new Vector2(0.955f, 0.740f)),
                "Text", string.Empty, 26, TextAnchor.MiddleCenter);

            _effect[station] = UiBuild.Label(
                Slot(card, "Etki", new Vector2(0.44f, 0.520f), new Vector2(0.955f, 0.628f)),
                "Text", string.Empty, 32, TextAnchor.MiddleCenter);

            // Cards-toward-next-level. The bar is the collection made visible: gems can be bought,
            // duplicates cannot, so this is the line that actually paces the roster.
            _fill[station] = Bar(card, new Vector2(0.455f, 0.435f), new Vector2(0.955f, 0.510f));

            _cards[station] = UiBuild.Label(
                Slot(card, "Kartlar", new Vector2(0.44f, 0.320f), new Vector2(0.955f, 0.425f)),
                "Text", string.Empty, 22, TextAnchor.MiddleCenter);

            // Price, with the gem in front of the number rather than the word after it — a 150 with
            // no icon on a white card reads as a level, not a cost.
            RectTransform bedel = Slot(card, "Bedel", new Vector2(0.44f, 0.185f), new Vector2(0.955f, 0.310f));
            _gem[station] = Icon(bedel, "Elmas", gemIcon, new Vector2(0.16f, 0.06f), new Vector2(0.42f, 0.94f));
            _cost[station] = UiBuild.Label(
                Slot(bedel, "Yazi", new Vector2(0.45f, 0f), new Vector2(0.96f, 1f)),
                "Text", string.Empty, 28, TextAnchor.MiddleLeft);

            int captured = station;
            _action[station] = UiBuild.Btn(card, "Dugme", string.Empty,
                                           actionButton != null ? actionButton : UiSkin.ButtonGreen,
                                           new Color(0.24f, 0.68f, 0.36f, 1f), 26, () => OnPressed(captured));
            // Dar ve alcak: hap sanatinin kendi orani 4:1 ve uclari yatayda dilimleniyor. Kartin
            // tam genisligine yayilinca sekli bozulmadan da olsa cok iri duruyordu; kenarlardan
            // biraz iceri cekildi.
            UiBuild.Anchor((RectTransform)_action[station].transform,
                           new Vector2(0.105f, 0.040f), new Vector2(0.895f, 0.170f));
            PillFit.Wrap(_action[station].GetComponent<Image>());
            _actionText[station] = _action[station].GetComponentInChildren<Text>();
        }

        // ------------------------------------------------------------------ pieces
        /// <summary>A sliced art panel, falling back to the flat skin when nothing is wired.</summary>
        private static RectTransform Art(RectTransform parent, string name, Sprite sprite,
                                         Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite != null ? sprite : UiSkin.Panel;
            img.type = sprite != null && sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            img.preserveAspect = img.type == Image.Type.Simple;
            img.color = Color.white;
            img.raycastTarget = false;
            return UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
        }

        /// <summary>A graphite capsule for the multiplier and the purse — the HUD's counter pill.</summary>
        private RectTransform Chip(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            Sprite art = chipPill != null ? chipPill : cardPanel;
            RectTransform rt = Art(parent, name, art, aMin, aMax);
            var img = rt.GetComponent<Image>();
            if (art != null) { img.type = Image.Type.Sliced; img.preserveAspect = false; PillFit.Wrap(img); }
            return rt;
        }

        /// <summary>A non-stretching icon. Returns the image so the caller can dim or hide it.</summary>
        private static Image Icon(RectTransform parent, string name, Sprite sprite, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.enabled = sprite != null;
            UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
            return img;
        }

        /// <summary>
        /// The capsule bar: a track, and inside it a fill whose WIDTH is driven — see
        /// <see cref="Progress"/>. The fill used to be an <see cref="Image.Type.Filled"/> draw, which
        /// crops a stretched sprite rather than slicing it, so the round left cap arrived as a wedge
        /// and the right end as a straight cut. Sliced art plus <see cref="PillFit"/> gives a capsule
        /// that is a capsule at any length.
        /// </summary>
        private Image Bar(RectTransform parent, Vector2 aMin, Vector2 aMax)
        {
            RectTransform bed = Art(parent, "Cubuk", barTrack, aMin, aMax);
            var bedImage = bed.GetComponent<Image>();
            bedImage.type = Image.Type.Sliced;
            bedImage.preserveAspect = false;
            PillFit.Wrap(bedImage);
            if (barTrack == null) bedImage.color = cardLocked;

            // Inset by the track's own rim. Flush with the edges the fill covers the rim entirely
            // and a full bar stops looking like a bar at all — it becomes one solid blue capsule.
            RectTransform alan = Slot(bed, "DolguAlani", Vector2.zero, Vector2.one);
            alan.offsetMin = new Vector2(3f, 3f);
            alan.offsetMax = new Vector2(-3f, -3f);

            var go = new GameObject("Dolgu", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(alan, false);
            var img = go.GetComponent<Image>();
            img.sprite = barFill;
            img.type = Image.Type.Sliced;
            img.preserveAspect = false;
            img.raycastTarget = false;
            if (barFill == null) img.color = rareTint;
            UiBuild.Anchor((RectTransform)go.transform, Vector2.zero, new Vector2(0f, 1f));
            PillFit.Wrap(img);
            return img;
        }

        /// <summary>Drives a bar built by <see cref="Bar"/>: the fill's right anchor is the progress.</summary>
        private static void Progress(Image fill, float t)
        {
            ((RectTransform)fill.transform).anchorMax = new Vector2(Mathf.Clamp01(t), 1f);
        }

        /// <summary>Shrinks a label until it fits its box, so a long station name cannot run off the card.</summary>
        private static void Fit(Text label, int min, int max)
        {
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = min;
            label.resizeTextMaxSize = max;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private Sprite Portrait(int station)
            => portraits != null && station >= 0 && station < portraits.Length ? portraits[station] : null;

        /// <summary>A child rect anchored inside the card — the shape UiBuild's helpers want.</summary>
        private static RectTransform Slot(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
        }

        /// <summary>A rarity tint deep enough to read as text on white, alpha untouched.</summary>
        private static Color Darken(Color c) => new Color(c.r * 0.68f, c.g * 0.68f, c.b * 0.68f, 1f);

        private Color TintFor(int station)
        {
            switch (Foremen.Slot(station))
            {
                case Foremen.Rarity.Epic: return epicTint;
                case Foremen.Rarity.Rare: return rareTint;
                default:                  return commonTint;
            }
        }

        // ------------------------------------------------------------------ press
        private void OnPressed(int station)
        {
            if (_foremen == null) return;
            bool done = _foremen.IsHired(station) ? _foremen.TryLevelUp(station) : _foremen.TryHire(station);
            if (done) ServiceLocator.Get<HapticService>()?.Light();
            Refresh();   // RosterChanged already refreshes on success; this covers the refusal too
        }

        // ---------------------------------------------------------------- refresh
        private void Refresh()
        {
            if (_foremen == null || _root == null || !_root.gameObject.activeSelf) return;

            _multiplier.text = string.Format(Culture, "×{0:0.00}", _foremen.IncomeMultiplier);
            _balance.text = (_wallet != null ? _wallet.Gems : 0L).ToString();

            for (int s = 0; s < Foremen.Count; s++) RefreshCard(s);
        }

        private void RefreshCard(int s)
        {
            bool hired = _foremen.IsHired(s);
            bool maxed = _foremen.IsMaxed(s);
            int level = _foremen.LevelOf(s);

            if (cardPanel == null) _card[s].color = hired ? cardHired : cardLocked;
            // A locked foreman keeps his own portrait, greyed — the card is white, so blanking him
            // leaves a hole where the only thing worth looking at should be.
            _portrait[s].color = hired ? Color.white : new Color(0.62f, 0.66f, 0.73f, 0.85f);

            _name[s].text = Loc.Id("istasyon", IslandEconomy.Stations[s]);
            _name[s].color = hired ? Ink : InkSoft;

            _level[s].text = hired
                ? string.Format(Loc.T("yukseltme.seviye"), level)
                : Loc.T("ustabasi.kiralikdegil");
            _level[s].color = InkSoft;

            // What this foreman is worth right now, as a percentage — the same number on the station
            // and on the empire, because it is literally the same term. See Game.Core.Foremen.
            double perLevel = Foremen.PerLevel(s, _foremen.Tuning);
            _effect[s].text = hired
                ? string.Format(Culture, "+{0:0.#}%", perLevel * level * 100d)
                : string.Format(Culture, "+{0:0.#}%", perLevel * 100d);
            _effect[s].color = hired ? Darken(TintFor(s)) : InkFaint;

            int have = _foremen.DuplicatesOf(s);
            int need = hired && !maxed ? _foremen.DuplicatesToLevel(s) : 0;
            float t = need > 0 ? Mathf.Clamp01(have / (float)need) : (maxed ? 1f : 0f);
            Progress(_fill[s], t);

            _cards[s].color = InkFaint;

            if (maxed)
            {
                _cards[s].text = string.Format("{0} / {0} {1}", need > 0 ? need : have, Loc.T("ustabasi.kart"));
                _gem[s].enabled = false;
                _cost[s].text = string.Empty;
                _actionText[s].text = Loc.T("ustabasi.azami");
                _action[s].interactable = false;
            }
            else if (hired)
            {
                _cards[s].text = string.Format("{0} / {1} {2}", have, need, Loc.T("ustabasi.kart"));
                _gem[s].enabled = gemIcon != null;
                _cost[s].text = _foremen.GemsToLevel(s).ToString();
                _actionText[s].text = Loc.T("ustabasi.seviyeatla");
                _action[s].interactable = _foremen.CanLevel(s);
            }
            else
            {
                _cards[s].text = string.Empty;
                _gem[s].enabled = gemIcon != null;
                _cost[s].text = _foremen.HireGems(s).ToString();
                _actionText[s].text = Loc.T("ustabasi.isealt");
                _action[s].interactable = _foremen.CanHire(s);
            }

            _cost[s].color = _action[s].interactable ? Ink : InkFaint;
            // The blue button is one pre-coloured sprite, so an unaffordable slot is greyed by tint
            // rather than by swapping to a second piece of art the kit does not have.
            _action[s].GetComponent<Image>().color =
                _action[s].interactable ? Color.white : new Color(0.72f, 0.75f, 0.80f, 1f);
        }
    }
}
