using Game.Core;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The checklist screen: today's three tasks in the left column, the permanent ladder in the right.
    ///
    /// Built in code for the same reason <see cref="ForemanRosterUI"/> is — the rows are generated
    /// from <see cref="Goals"/>'s own tables, so an authored sheet would be a set of hand-wired copies
    /// that fall out of step the moment a task or a tier changes.
    ///
    /// TWO COLUMNS, not one list. Nine full-width rows stacked down a landscape screen come out around
    /// seventy pixels tall each — a letterbox with a bar in it. Splitting the daily tasks from the
    /// ladder gives every row twice the height and costs nothing, because the two lists never interact.
    ///
    /// Refreshed on open and on <see cref="GoalService.Changed"/>, never per frame. The opener carries
    /// the pending count, because a checklist nobody is told about is a checklist nobody opens.
    /// </summary>
    public sealed class GoalsUI : MonoBehaviour
    {
        [SerializeField] private int sortingOrder = 106;

        [Header("Görseller")]
        [Tooltip("Satır gövdesi — MaviSet/panel_beyaz.")]
        [SerializeField] private Sprite cardPanel;
        [Tooltip("Başlık şeridi — MaviSet/serit_mavi.")]
        [SerializeField] private Sprite ribbon;
        [Tooltip("Al düğmesi — MaviSet/btn_mavi.")]
        [SerializeField] private Sprite actionButton;
        [Tooltip("Kapat düğmesi — MaviSet/btn_kapat_yeni.")]
        [SerializeField] private Sprite closeIcon;
        [Tooltip("İlerleme çubuğunun yatağı ve dolgusu — Gostergeler/slider_yatak, slider_dolgu.")]
        [SerializeField] private Sprite barTrack;
        [SerializeField] private Sprite barFill;
        [Tooltip("Ödülün solundaki elmas ikonu — Ikonlar/ikon_elmas.")]
        [SerializeField] private Sprite gemIcon;

        [Header("Renkler")]
        [SerializeField] private Color scrim = new Color(0.04f, 0.05f, 0.08f, 0.86f);
        [SerializeField] private Color card = new Color(0.16f, 0.19f, 0.27f, 1f);
        [SerializeField] private Color track = new Color(0.10f, 0.11f, 0.16f, 1f);
        [SerializeField] private Color dailyFill = new Color(0.35f, 0.72f, 0.98f, 1f);
        [SerializeField] private Color ladderFill = new Color(0.98f, 0.74f, 0.24f, 1f);

        /// <summary>The rail button's icon, loaded at runtime — this screen has no Inspector to wire.</summary>
        private const string OpenerIconResource = "UI/Buttons/gorev";

        // The rows are white art now, so every label on them has to be ink rather than paper.
        private static readonly Color Ink = new Color(0.09f, 0.14f, 0.24f, 1f);
        private static readonly Color InkSoft = new Color(0.36f, 0.42f, 0.52f, 1f);
        private static readonly Color InkFaint = new Color(0.58f, 0.63f, 0.71f, 1f);

        /// <summary>
        /// Where the ribbon's flat band sits, measured on the sprite: its middle is 0.677 up from the
        /// bottom, because the tails hang below it. A label centred in the rect lands on the tails.
        /// </summary>
        private const float RibbonBand = 0.677f;

        private GoalService _goals;
        private RectTransform _root;
        private Text _pendingLabel;
        private RectTransform _pendingChip;
        private TMP_Text _openerCount;
        private GameObject _openerChip;

        private readonly Text[] _dailyText = new Text[Goals.DailySlots];
        private readonly Text[] _dailyReward = new Text[Goals.DailySlots];
        private readonly Image[] _dailyFillImage = new Image[Goals.DailySlots];
        private readonly Button[] _dailyBtn = new Button[Goals.DailySlots];
        private readonly Text[] _dailyBtnText = new Text[Goals.DailySlots];

        private Text[] _ladderText;
        private Image[] _ladderFillImage;
        private Button[] _ladderBtn;
        private Text[] _ladderBtnText;

        private void Awake()
        {
            _goals = ServiceLocator.Get<GoalService>();
            Build();
            BuildOpener();
            if (_goals != null) _goals.Changed += OnChanged;
            Hide();
            RefreshOpener();
        }

        private void OnDestroy() { if (_goals != null) _goals.Changed -= OnChanged; }

        private void OnChanged() { Refresh(); RefreshOpener(); }

        public void Show() { if (_root != null) _root.gameObject.SetActive(true); Refresh(); }
        public void Hide() { if (_root != null) _root.gameObject.SetActive(false); }

        // ------------------------------------------------------------------ build
        private void Build()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "GorevKanvas", sortingOrder);
            _root = UiBuild.Flat(canvas, "Karartma", scrim, Vector2.zero, Vector2.one);

            BuildHeader();

            const float top = 0.745f, bottom = 0.030f;

            // Sol sütun: günün üç görevi.
            Caption("GunlukBaslik", Loc.T("gorev.gunluk"), 0.035f, 0.475f);
            float dh = (top - bottom) / Goals.DailySlots;
            for (int i = 0; i < Goals.DailySlots; i++)
                BuildDaily(i, new Vector2(0.035f, top - (i + 1) * dh + 0.010f),
                              new Vector2(0.475f, top - i * dh - 0.010f));

            // Sağ sütun: kalıcı basamaklar.
            Caption("LadderBaslik", Loc.T("gorev.basarimlar"), 0.525f, 0.965f);
            int n = Goals.Ladder.Length;
            _ladderText = new Text[n];
            _ladderFillImage = new Image[n];
            _ladderBtn = new Button[n];
            _ladderBtnText = new Text[n];

            float lh = (top - bottom) / n;
            for (int i = 0; i < n; i++)
                BuildLadderRow(i, new Vector2(0.525f, top - (i + 1) * lh + 0.006f),
                                  new Vector2(0.965f, top - i * lh - 0.006f));
        }

        /// <summary>The blue ribbon across the top, the pending count left of it, close on the right.</summary>
        private void BuildHeader()
        {
            RectTransform band = Art(_root, "Serit", ribbon, new Vector2(0.360f, 0.850f), new Vector2(0.640f, 0.992f));
            UiBuild.Label(Slot(band, "Yazi", new Vector2(0.13f, RibbonBand - 0.13f),
                                        new Vector2(0.87f, RibbonBand + 0.13f)),
                                   "Text", Loc.T("gorev.baslik"), 38, TextAnchor.MiddleCenter);

            _pendingChip = Chip(_root, "Bekleyen", new Vector2(0.035f, 0.880f), new Vector2(0.185f, 0.963f));
            _pendingLabel = UiBuild.Label(Slot(_pendingChip, "Yazi", new Vector2(0.08f, 0f), new Vector2(0.92f, 1f)),
                                          "Text", string.Empty, 32, TextAnchor.MiddleCenter);
            _pendingLabel.color = Ink;

            Button close = UiBuild.Btn(_root, "Kapat", string.Empty, closeIcon != null ? closeIcon : UiSkin.ButtonGrey,
                                       track, 34, Hide);
            var closeImage = close.GetComponent<Image>();
            closeImage.type = Image.Type.Simple;
            closeImage.preserveAspect = true;
            // Sağ kenarda değil: HUD'un ayarlar dişlisi 120 sıralı kanvasta, bu ekranın üstünde
            // çiziliyor ve tam köşeye konan kapat düğmesinin üstüne biniyor.
            UiBuild.Anchor((RectTransform)close.transform, new Vector2(0.878f, 0.873f), new Vector2(0.938f, 0.970f));
        }

        /// <summary>A column caption, sitting just under the ribbon and above the first row.</summary>
        private void Caption(string name, string text, float left, float right)
        {
            Text t = UiBuild.Label(Slot(_root, name, new Vector2(left, 0.755f), new Vector2(right, 0.815f)),
                                   "Text", text, 28, TextAnchor.MiddleLeft);
            t.color = new Color(1f, 1f, 1f, 0.82f);
        }

        private void BuildDaily(int slot, Vector2 aMin, Vector2 aMax)
        {
            RectTransform c = Art(_root, "Gunluk_" + slot, cardPanel, aMin, aMax);
            if (cardPanel == null) c.GetComponent<Image>().color = card;

            _dailyText[slot] = UiBuild.Label(Slot(c, "Yazi", new Vector2(0.045f, 0.60f), new Vector2(0.70f, 0.93f)),
                                             "Text", string.Empty, 30, TextAnchor.MiddleLeft);
            _dailyText[slot].color = Ink;
            Fit(_dailyText[slot], 16, 30);

            _dailyFillImage[slot] = Bar(c, new Vector2(0.045f, 0.42f), new Vector2(0.70f, 0.56f), dailyFill);

            RectTransform odul = Slot(c, "Odul", new Vector2(0.045f, 0.10f), new Vector2(0.70f, 0.36f));
            Icon(odul, "Elmas", gemIcon, new Vector2(0f, 0.08f), new Vector2(0.10f, 0.92f));
            _dailyReward[slot] = UiBuild.Label(Slot(odul, "Yazi", new Vector2(0.12f, 0f), new Vector2(1f, 1f)),
                                               "Text", string.Empty, 26, TextAnchor.MiddleLeft);
            _dailyReward[slot].color = InkSoft;

            int captured = slot;
            _dailyBtn[slot] = UiBuild.Btn(c, "Al", string.Empty,
                                          actionButton != null ? actionButton : UiSkin.ButtonGreen,
                                          new Color(0.24f, 0.68f, 0.36f, 1f), 26,
                                          () => { if (_goals != null && _goals.ClaimDaily(captured)) Ping(); });
            UiBuild.Anchor((RectTransform)_dailyBtn[slot].transform,
                           new Vector2(0.735f, 0.30f), new Vector2(0.965f, 0.70f));
            _dailyBtnText[slot] = _dailyBtn[slot].GetComponentInChildren<Text>();
        }

        private void BuildLadderRow(int index, Vector2 aMin, Vector2 aMax)
        {
            RectTransform c = Art(_root, "Basarim_" + index, cardPanel, aMin, aMax);
            if (cardPanel == null) c.GetComponent<Image>().color = card;

            _ladderText[index] = UiBuild.Label(Slot(c, "Yazi", new Vector2(0.04f, 0.48f), new Vector2(0.74f, 0.94f)),
                                               "Text", string.Empty, 26, TextAnchor.MiddleLeft);
            _ladderText[index].color = Ink;
            Fit(_ladderText[index], 15, 26);

            _ladderFillImage[index] = Bar(c, new Vector2(0.04f, 0.16f), new Vector2(0.74f, 0.40f), ladderFill);

            int captured = index;
            _ladderBtn[index] = UiBuild.Btn(c, "Al", string.Empty,
                                            actionButton != null ? actionButton : UiSkin.ButtonGreen,
                                            new Color(0.24f, 0.68f, 0.36f, 1f), 26,
                                            () => { if (_goals != null && _goals.ClaimAchievement(captured)) Ping(); });
            UiBuild.Anchor((RectTransform)_ladderBtn[index].transform,
                           new Vector2(0.770f, 0.18f), new Vector2(0.965f, 0.82f));
            _ladderBtnText[index] = _ladderBtn[index].GetComponentInChildren<Text>();
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

        /// <summary>Shrinks a label until it fits its box, so a long task line cannot run off the row.</summary>
        private static void Fit(Text label, int min, int max)
        {
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = min;
            label.resizeTextMaxSize = max;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
        }

        /// <summary>A small white capsule for the pending counter.</summary>
        private RectTransform Chip(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            RectTransform rt = Art(parent, name, cardPanel, aMin, aMax);
            var img = rt.GetComponent<Image>();
            if (cardPanel != null) { img.type = Image.Type.Sliced; img.preserveAspect = false; }
            img.pixelsPerUnitMultiplier = 2.4f;   // the card's 44px corner is half a chip tall
            return rt;
        }

        /// <summary>A non-stretching icon.</summary>
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
        /// The capsule bar. The fill is <see cref="Image.Type.Filled"/> rather than a stretched child,
        /// so an almost-empty bar shows the sprite's own rounded left cap instead of a squashed
        /// capsule with both caps crushed into each other.
        /// </summary>
        private Image Bar(RectTransform parent, Vector2 aMin, Vector2 aMax, Color fallback)
        {
            RectTransform bed = Art(parent, "Cubuk", barTrack, aMin, aMax);
            var bedImage = bed.GetComponent<Image>();
            bedImage.type = Image.Type.Sliced;
            bedImage.preserveAspect = false;
            bedImage.pixelsPerUnitMultiplier = 3f;
            if (barTrack == null) bedImage.color = track;

            var go = new GameObject("Dolgu", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(bed, false);
            var img = go.GetComponent<Image>();
            img.sprite = barFill;
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = 0;
            img.raycastTarget = false;
            if (barFill == null) img.color = fallback;
            UiBuild.Anchor((RectTransform)go.transform, Vector2.zero, Vector2.one);
            return img;
        }

        private static void Ping() => ServiceLocator.Get<HapticService>()?.Medium();

        private static RectTransform Slot(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
        }

        // ---------------------------------------------------------------- opener
        /// <summary>
        /// The opener goes on the end of the HUD's left rail, under the ads and offer buttons, rather
        /// than floating at a screen fraction of its own — see <see cref="HudUI.AttachRailButton"/> for
        /// why a fraction of a landscape screen is not a place on a portrait-authored rail.
        ///
        /// The pending count rides under it in the rail's own counter chip, the same one the contract
        /// and offer buttons wear, and goes away when there is nothing to claim: a checklist nobody is
        /// told about is a checklist nobody opens.
        /// </summary>
        private void BuildOpener()
        {
            HudUI hud = FindAnyObjectByType<HudUI>(FindObjectsInactive.Include);
            if (hud == null) return;

            Button open = hud.AttachRailButton(false, "BtnGorev", Resources.Load<Sprite>(OpenerIconResource), Show);
            if (open == null) return;

            _openerChip = hud.AttachRailChip(open);
            if (_openerChip != null) _openerCount = _openerChip.GetComponentInChildren<TMP_Text>(true);
        }

        private void RefreshOpener()
        {
            if (_openerChip == null || _goals == null) return;
            int pending = _goals.PendingCount();
            _openerChip.SetActive(pending > 0);
            if (pending > 0) _openerCount.text = pending.ToString();
        }

        // --------------------------------------------------------------- refresh
        private void Refresh()
        {
            if (_goals == null || _root == null || !_root.gameObject.activeSelf) return;

            int pending = _goals.PendingCount();
            _pendingChip.gameObject.SetActive(pending > 0);
            if (pending > 0) _pendingLabel.text = string.Format("{0} ×{1}", Loc.T("gorev.al"), pending);

            for (int i = 0; i < Goals.DailySlots; i++) RefreshDaily(i);
            for (int i = 0; i < Goals.Ladder.Length; i++) RefreshLadder(i);
        }

        private void RefreshDaily(int slot)
        {
            Goals.Task t = _goals.DailyTask(slot);
            long have = _goals.DailyProgress(slot);
            bool claimed = _goals.DailyClaimed(slot);

            _dailyText[slot].text = string.Format("{0}   {1} / {2}", MetricName(t.Metric), have, t.Target);
            _dailyText[slot].color = claimed ? InkFaint : Ink;
            _dailyFillImage[slot].fillAmount = Goals.Progress(have, t.Target);
            _dailyReward[slot].text = RewardLine(t.Gems, t.Cards);

            _dailyBtnText[slot].text = claimed ? Loc.T("gorev.alindi") : Loc.T("gorev.al");
            Dress(_dailyBtn[slot], _goals.CanClaimDaily(slot));
        }

        private void RefreshLadder(int index)
        {
            Goals.Achievement a = Goals.Ladder[index];
            long total = _goals.Lifetime(a.Metric);
            int reached = Goals.TiersReached(a, total);
            long next = Goals.NextTier(a, total);
            int owed = _goals.UnclaimedTiers(index);

            _ladderText[index].text = next > 0L
                ? string.Format("{0}   ·   {1} {2}/{3}   ·   {4} / {5}",
                                MetricName(a.Metric), Loc.T("gorev.kademe"), reached, a.Tiers.Length, total, next)
                : string.Format("{0}   ·   {1}", MetricName(a.Metric), Loc.T("gorev.tamamlandi"));

            long from = reached > 0 ? a.Tiers[reached - 1] : 0L;
            _ladderFillImage[index].fillAmount =
                next > 0L ? Goals.Progress(total - from, next - from) : 1f;

            _ladderBtnText[index].text = owed > 0 ? string.Format("{0} ×{1}", Loc.T("gorev.al"), owed)
                                                  : Loc.T("gorev.al");
            Dress(_ladderBtn[index], owed > 0);
        }

        /// <summary>
        /// The blue button is one pre-coloured sprite, so a claim that is not ready is greyed by tint
        /// rather than by swapping to a second piece of art the kit does not have.
        /// </summary>
        private static void Dress(Button b, bool live)
        {
            b.interactable = live;
            b.GetComponent<Image>().color = live ? Color.white : new Color(0.72f, 0.75f, 0.80f, 1f);
        }

        private static string RewardLine(long gems, int cards)
            => cards > 0
                ? string.Format("{0}   +{1} {2}", gems, cards, Loc.T("ustabasi.kart"))
                : gems.ToString();

        /// <summary>
        /// A metric's name. Four of the six already have a word in the table — the stations and the
        /// foreman screen named them — so only the two that do not get their own key.
        /// </summary>
        private static string MetricName(int metric)
        {
            switch (metric)
            {
                case Goals.BarsSold:      return Loc.T("gorev.metrik.kulce");
                case Goals.Upgrades:      return Loc.T("gorev.metrik.yukseltme");
                case Goals.Contracts:     return Loc.T("gorev.metrik.kontrat");
                case Goals.Repairs:       return Loc.T("gorev.metrik.onarim");
                case Goals.Islands:       return Loc.T("gorev.metrik.ada");
                case Goals.ForemanLevels: return Loc.T("gorev.metrik.ustabasi");
                default:                  return string.Empty;
            }
        }
    }
}
