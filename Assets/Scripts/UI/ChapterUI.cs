using Game.Core;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The chapter log: the eight islands down the left, the selected chapter's five beats down the
    /// right, and the chapter's opening line above them.
    ///
    /// Built in code for the same reason <see cref="GoalsUI"/> and <see cref="ForemanRosterUI"/> are —
    /// the rows come out of <see cref="Chapters"/>'s own tables, so an authored sheet would be a set
    /// of hand-wired copies that fall out of step the moment a beat is added. Adding the sixth beat
    /// this design is already sized for should cost one entry in that table and nothing here.
    ///
    /// TWO COLUMNS, like the goals screen and for the same reason: thirteen full-width rows stacked
    /// down a landscape screen come out as a letterbox with bars in it. The left column is also the
    /// selector, which is what lets the right one be five tall rows instead of forty short ones.
    ///
    /// THE ART IS THE DESIGN KIT, loaded through <see cref="AtolyeKit"/> and shared with
    /// <see cref="CraftingUI"/> and <see cref="GoalsUI"/>. The kit is pre-coloured, so state is picked
    /// by swapping the SPRITE, never by tinting: an island tab is gold when it is the one being read,
    /// plain silver when it is owned, and silver with a padlock when it is not — three sprites where
    /// this screen used to wash one panel in three colours.
    ///
    /// Refreshed on open and on <see cref="ChapterService.Changed"/>, never per frame.
    /// </summary>
    public sealed class ChapterUI : MonoBehaviour
    {
        [SerializeField] private int sortingOrder = 107;

        [Header("Renkler")]
        [SerializeField] private Color scrim = new Color(0.04f, 0.05f, 0.08f, 0.92f);
        [Tooltip("Kartların üstünde durduğu zemin. Panel sanatı bağlıysa onu boyar.")]
        [SerializeField] private Color backdrop = new Color(0.15f, 0.18f, 0.26f, 1f);
        [SerializeField] private Color track = new Color(0.10f, 0.11f, 0.16f, 1f);
        [SerializeField] private Color beatFill = new Color(0.98f, 0.74f, 0.24f, 1f);

        /// <summary>
        /// The kit art, fetched once in <see cref="Awake"/> — see <see cref="AtolyeKit"/> for why it
        /// comes from an atlas rather than from wired Inspector slots, and why a tab's state is a
        /// different SPRITE rather than a tint on one.
        /// </summary>
        private Sprite _panel, _ribbon, _btnLive, _btnDead, _closeIcon, _barTrack, _barFill, _chip,
                       _gemIcon, _tabPlain, _tabPicked, _tabLocked;

        /// <summary>The rail button's icon before the kit had one; still the fallback.</summary>
        private const string OpenerIconResource = "UI/Buttons/bolum";

        private static readonly Color Ink = new Color(0.09f, 0.14f, 0.24f, 1f);
        private static readonly Color InkSoft = new Color(0.36f, 0.42f, 0.52f, 1f);
        private static readonly Color InkFaint = new Color(0.58f, 0.63f, 0.71f, 1f);
        private static readonly Color Paper = new Color(0.96f, 0.97f, 1f, 1f);

        /// <summary>Where the ribbon's flat band sits on the sprite — its tails hang below the rect.</summary>
        private const float RibbonBand = 0.677f;

        private ChapterService _chapters;
        private LocalizationService _loc;
        private RectTransform _root;

        private Text _titleLabel, _pendingLabel, _storyTitle, _storyLine;
        private RectTransform _pendingChip;
        private Button _claimAll;
        private Text _claimAllText;
        private TMP_Text _openerCount;
        private GameObject _openerChip;

        private readonly Button[] _tabBtn = new Button[Chapters.Count];
        private readonly Text[] _tabName = new Text[Chapters.Count];
        private readonly Text[] _tabCount = new Text[Chapters.Count];
        private readonly Image[] _tabArt = new Image[Chapters.Count];

        private readonly Text[] _beatName = new Text[Chapters.BeatCount];
        private readonly Text[] _beatNote = new Text[Chapters.BeatCount];
        private readonly Text[] _beatReward = new Text[Chapters.BeatCount];
        private readonly Image[] _beatFillImage = new Image[Chapters.BeatCount];
        private readonly Button[] _beatBtn = new Button[Chapters.BeatCount];
        private readonly Text[] _beatBtnText = new Text[Chapters.BeatCount];

        /// <summary>Which chapter the right-hand column is showing.</summary>
        private int _shown;

        private void Awake()
        {
            _chapters = ServiceLocator.Get<ChapterService>();
            LoadKit();
            Build();
            BuildOpener();
            if (_chapters != null) _chapters.Changed += OnChanged;
            _loc = ServiceLocator.Get<LocalizationService>();
            if (_loc != null) _loc.Changed += OnLanguageChanged;
            Hide();
            RefreshOpener();
        }

        private void OnDestroy()
        {
            if (_chapters != null) _chapters.Changed -= OnChanged;
            if (_loc != null) _loc.Changed -= OnLanguageChanged;
        }

        private void OnLanguageChanged()
        {
            if (_titleLabel != null) _titleLabel.text = Loc.T("bolum.baslik");
            Refresh();
            RefreshOpener();
        }

        private void OnChanged() { Refresh(); RefreshOpener(); }

        /// <summary>
        /// Opens on the chapter the player is actually in rather than on chapter one. An eight-island
        /// empire opening on coal every time would put the screen's whole point — what am I working
        /// on now — behind seven taps.
        /// </summary>
        public void Show()
        {
            if (_root == null) return;
            if (_chapters != null)
            {
                _shown = _chapters.Current;
                _chapters.MarkIntroSeen(_shown);
            }
            _root.gameObject.SetActive(true);
            Refresh();
        }

        public void Hide() { if (_root != null) _root.gameObject.SetActive(false); }

        // ------------------------------------------------------------------ build
        /// <summary>Before <see cref="Build"/>, which reads every one of these.</summary>
        private void LoadKit()
        {
            _panel = AtolyeKit.Get("panel_kart");
            _ribbon = AtolyeKit.Get("serit_baslik");
            _btnLive = AtolyeKit.Get("btn_yesil");
            _btnDead = AtolyeKit.Get("btn_al");
            _closeIcon = AtolyeKit.Get("kapat");
            _barTrack = AtolyeKit.Get("cubuk_yatak");
            _barFill = AtolyeKit.Get("cubuk_altin");
            _chip = AtolyeKit.Get("hap_cip");
            _gemIcon = AtolyeKit.Get("elmas");
            _tabPlain = AtolyeKit.Get("sekme");
            _tabPicked = AtolyeKit.Get("sekme_secili");
            _tabLocked = AtolyeKit.Get("sekme_kilitli");
        }

        private void Build()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "BolumKanvas", sortingOrder);
            _root = UiBuild.Flat(canvas, "Karartma", UiBuild.Opaque(scrim), Vector2.zero, Vector2.one);
            var dismiss = _root.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(Hide);

            BuildBackdrop();

            BuildHeader();

            // Sol sütun: sekiz ada, hem liste hem seçici.
            const float top = 0.815f, bottom = 0.030f;
            float th = (top - bottom) / Chapters.Count;
            for (int c = 0; c < Chapters.Count; c++)
                BuildTab(c, new Vector2(0.035f, top - (c + 1) * th + 0.005f),
                            new Vector2(0.330f, top - c * th - 0.005f));

            BuildStory();

            // Sağ sütun: seçili bölümün beş aşaması.
            const float beatTop = 0.660f;
            float bh = (beatTop - bottom) / Chapters.BeatCount;
            for (int b = 0; b < Chapters.BeatCount; b++)
                BuildBeat(b, new Vector2(0.355f, beatTop - (b + 1) * bh + 0.008f),
                             new Vector2(0.965f, beatTop - b * bh - 0.008f));
            // Content into the safe area; the scrim above it keeps covering the notch.
            UiBuild.InsetContent(_root);
        }

        private void BuildHeader()
        {
            RectTransform band = Art(_root, "Serit", _ribbon, new Vector2(0.360f, 0.850f), new Vector2(0.640f, 0.992f));
            _titleLabel = UiBuild.Label(Slot(band, "Yazi", new Vector2(0.13f, RibbonBand - 0.13f),
                                        new Vector2(0.87f, RibbonBand + 0.13f)),
                                   "Text", Loc.T("bolum.baslik"), 38, TextAnchor.MiddleCenter);

            _pendingChip = Chip(_root, "Bekleyen", new Vector2(0.035f, 0.880f), new Vector2(0.185f, 0.963f));
            _pendingLabel = UiBuild.Label(Slot(_pendingChip, "Yazi", new Vector2(0.08f, 0f), new Vector2(0.92f, 1f)),
                                          "Text", string.Empty, 32, TextAnchor.MiddleCenter);
            _pendingLabel.color = Paper;

            Button close = UiBuild.Btn(_root, "Kapat", string.Empty,
                                       _closeIcon != null ? _closeIcon : UiSkin.ButtonGrey, Color.white, 34, Hide);
            var closeImage = close.GetComponent<Image>();
            closeImage.type = Image.Type.Simple;
            closeImage.preserveAspect = true;
            // Sağ köşede değil: HUD'un ayarlar dişlisi 120 sıralı kanvasta, bu ekranın üstünde çiziliyor.
            UiBuild.Anchor((RectTransform)close.transform, new Vector2(0.878f, 0.873f), new Vector2(0.938f, 0.970f));
        }

        /// <summary>The selected chapter's opening line, with the claim-all button beside it.</summary>
        private void BuildStory()
        {
            RectTransform c = Art(_root, "Hikaye", _panel, new Vector2(0.355f, 0.680f), new Vector2(0.965f, 0.815f));

            _storyTitle = UiBuild.Label(Slot(c, "Baslik", new Vector2(0.035f, 0.56f), new Vector2(0.700f, 0.92f)),
                                        "Text", string.Empty, 30, TextAnchor.MiddleLeft);
            _storyTitle.color = Ink;
            Fit(_storyTitle, 18, 30);

            _storyLine = UiBuild.Label(Slot(c, "Satir", new Vector2(0.035f, 0.10f), new Vector2(0.700f, 0.50f)),
                                       "Text", string.Empty, 24, TextAnchor.UpperLeft);
            _storyLine.color = InkSoft;
            Fit(_storyLine, 14, 24);

            _claimAll = UiBuild.Btn(c, "HepsiniAl", string.Empty,
                                    _btnLive != null ? _btnLive : UiSkin.ButtonGreen,
                                    Color.white, 26,
                                    () => { if (_chapters != null && _chapters.ClaimChapter(_shown) > 0) Ping(); });
            // Wide and low, near the capsule art's own 2:1 — a taller box spends its width on two
            // end caps and leaves the label nowhere to sit.
            UiBuild.Anchor((RectTransform)_claimAll.transform,
                           new Vector2(0.710f, 0.340f), new Vector2(0.980f, 0.660f));
            PillFit.Wrap(_claimAll.GetComponent<Image>());
            _claimAllText = AtolyeKit.Label(_claimAll, 11, 24);
        }

        /// <summary>
        /// One island: a name plate, and the chapter's number and progress underneath it.
        ///
        /// THE PLATE IS NOT THE ROW. The kit's tab is a 4:1 name plate with an ornament at each end —
        /// studs when it is the chapter being read, a padlock when the island is not owned — and a row
        /// tall enough for two lines is nearer 1.6:1. Drawn as the whole row, the two end caps met in
        /// the middle and the text ran across the padlock. So the plate takes the top band at close to
        /// its authored proportion and carries the name alone, and the caption sits below it on the
        /// backdrop, which is also where a caption belongs.
        ///
        /// The button itself is the whole row, transparent: the whole row should be tappable, not just
        /// the plate.
        /// </summary>
        private void BuildTab(int chapter, Vector2 aMin, Vector2 aMax)
        {
            int captured = chapter;
            _tabBtn[chapter] = UiBuild.Btn(_root, "Ada_" + chapter, string.Empty,
                                           UiSkin.Flat, Color.clear, 24, () => Select(captured));
            var hit = _tabBtn[chapter].GetComponent<Image>();
            hit.color = Color.clear;      // invisible, but still the row's raycast target
            RectTransform rt = UiBuild.Anchor((RectTransform)_tabBtn[chapter].transform, aMin, aMax);

            // The button's own auto-label is unused — two lines are wanted, not one centred string.
            Text made = _tabBtn[chapter].GetComponentInChildren<Text>();
            if (made != null) made.gameObject.SetActive(false);

            RectTransform plate = Art(rt, "Plaka", _tabPlain, new Vector2(0f, 0.46f), Vector2.one);
            var plateImage = plate.GetComponent<Image>();
            plateImage.type = Image.Type.Sliced;
            plateImage.preserveAspect = false;
            if (_tabPlain != null) PillFit.Wrap(plateImage);
            _tabArt[chapter] = plateImage;

            // Inside the plate's field, clear of the widest cap the three states use — the padlock's.
            _tabName[chapter] = UiBuild.Label(Slot(plate, "Ad", new Vector2(0.14f, 0.08f),
                                                   new Vector2(0.66f, 0.92f)),
                                              "Text", string.Empty, 22, TextAnchor.MiddleLeft);
            _tabName[chapter].color = Ink;
            Fit(_tabName[chapter], 11, 22);

            _tabCount[chapter] = UiBuild.Label(Slot(rt, "Sayac", new Vector2(0.06f, 0.02f),
                                                    new Vector2(0.98f, 0.42f)),
                                               "Text", string.Empty, 19, TextAnchor.MiddleLeft);
            _tabCount[chapter].color = Paper;
            Fit(_tabCount[chapter], 10, 19);
        }

        private void BuildBeat(int beat, Vector2 aMin, Vector2 aMax)
        {
            RectTransform c = Art(_root, "Asama_" + beat, _panel, aMin, aMax);

            _beatName[beat] = UiBuild.Label(Slot(c, "Ad", new Vector2(0.030f, 0.58f), new Vector2(0.545f, 0.94f)),
                                            "Text", string.Empty, 28, TextAnchor.MiddleLeft);
            _beatName[beat].color = Ink;
            Fit(_beatName[beat], 15, 28);

            _beatNote[beat] = UiBuild.Label(Slot(c, "Not", new Vector2(0.030f, 0.30f), new Vector2(0.545f, 0.56f)),
                                            "Text", string.Empty, 22, TextAnchor.MiddleLeft);
            _beatNote[beat].color = InkSoft;
            Fit(_beatNote[beat], 12, 22);

            _beatFillImage[beat] = Bar(c, new Vector2(0.030f, 0.10f), new Vector2(0.545f, 0.26f), beatFill);

            RectTransform odul = Slot(c, "Odul", new Vector2(0.570f, 0.30f), new Vector2(0.780f, 0.70f));
            Icon(odul, "Elmas", _gemIcon, new Vector2(0f, 0.10f), new Vector2(0.22f, 0.90f));
            _beatReward[beat] = UiBuild.Label(Slot(odul, "Yazi", new Vector2(0.26f, 0f), new Vector2(1f, 1f)),
                                              "Text", string.Empty, 24, TextAnchor.MiddleLeft);
            _beatReward[beat].color = InkSoft;
            Fit(_beatReward[beat], 12, 24);

            int captured = beat;
            _beatBtn[beat] = UiBuild.Btn(c, "Al", string.Empty,
                                         _btnLive != null ? _btnLive : UiSkin.ButtonGreen,
                                         Color.white, 26,
                                         () => { if (_chapters != null && _chapters.Claim(_shown, captured)) Ping(); });
            // Geniş ve alçak: hap sanatının kendi oranı 2,5:1 ve uçları yatayda dilimleniyor.
            UiBuild.Anchor((RectTransform)_beatBtn[beat].transform,
                           new Vector2(0.780f, 0.380f), new Vector2(0.980f, 0.620f));
            PillFit.Wrap(_beatBtn[beat].GetComponent<Image>());
            _beatBtnText[beat] = AtolyeKit.Label(_beatBtn[beat], 10, 22);
        }

        private void Select(int chapter)
        {
            if (chapter < 0 || chapter >= Chapters.Count || chapter == _shown) return;
            _shown = chapter;
            if (_chapters != null) _chapters.MarkIntroSeen(chapter);
            Ping();
            Refresh();
        }

        // --------------------------------------------------------------- refresh
        private void Refresh()
        {
            if (_chapters == null || _root == null || !_root.gameObject.activeSelf) return;

            int pending = _chapters.PendingCount();
            _pendingChip.gameObject.SetActive(pending > 0);
            if (pending > 0) _pendingLabel.text = string.Format("{0} ×{1}", Loc.T("gorev.al"), pending);

            for (int c = 0; c < Chapters.Count; c++) RefreshTab(c);
            RefreshStory();
            for (int b = 0; b < Chapters.BeatCount; b++) RefreshBeat(b);
        }

        private void RefreshTab(int chapter)
        {
            bool owned = _chapters.Owned(chapter);
            string island = Chapters.Island(chapter);

            // THE ISLAND'S NAME IS ALWAYS THE TITLE, owned or not. It used to read "you do not own
            // this island yet" on every locked row, which put that sentence on the screen seven times
            // and left "Chapter 4" as the only thing telling them apart — a list of seven identical
            // rows is not a list. The lock belongs in the subtitle, where the progress would be.
            _tabName[chapter].text = Loc.Id("ada", island);
            // The name sits on the plate's pale field, the caption on the dark backdrop below it.
            _tabName[chapter].color = owned ? Ink : InkSoft;

            if (owned)
            {
                int done = Chapters.BeatsSatisfied(_chapters.Progress(chapter), _chapters.Tuning);
                bool complete = done >= Chapters.BeatCount;
                _tabCount[chapter].text = complete
                    ? string.Format("{0} {1}   ·   {2}", Loc.T("bolum.bolum"), chapter + 1,
                                    Loc.T("bolum.tamamlandi"))
                    : string.Format("{0} {1}   ·   {2}/{3}",
                                    Loc.T("bolum.bolum"), chapter + 1, done, Chapters.BeatCount);
            }
            else
            {
                _tabCount[chapter].text = string.Format("{0} {1}   ·   {2}",
                                          Loc.T("bolum.bolum"), chapter + 1, Loc.T("bolum.kilitli"));
            }
            _tabCount[chapter].color = owned ? Paper : InkFaint;

            // THREE STATES, THREE SPRITES. The kit carries a gold tab for the one being read and a
            // silver one with a padlock for an island not owned yet; the plain silver between them is
            // the locked tab with its lock mirrored away (Tools/ui/atolye_tasarim_kiti.ps1). This row
            // used to tint one panel three ways, which is what pre-coloured art must never be asked
            // to do — the gold would have come out of a wash over white.
            // All three are the same height, so PillFit's multiplier holds across a swap.
            Sprite art = chapter == _shown ? _tabPicked : owned ? _tabPlain : _tabLocked;
            if (art != null) _tabArt[chapter].sprite = art;
        }

        private void RefreshStory()
        {
            string island = Chapters.Island(_shown);
            bool owned = _chapters.Owned(_shown);

            _storyTitle.text = string.Format("{0} {1}   ·   {2}",
                                             Loc.T("bolum.bolum"), _shown + 1, Loc.Id("ada", island));
            // A chapter the player has not reached keeps its line back — it is the reason to get there.
            _storyLine.text = owned ? Loc.T("bolum.hikaye." + island) : Loc.T("bolum.kilitli");

            int owed = 0;
            for (int b = 0; b < Chapters.BeatCount; b++) if (_chapters.CanClaim(_shown, b)) owed++;
            _claimAllText.text = owed > 0 ? string.Format("{0} ×{1}", Loc.T("bolum.hepsiniAl"), owed)
                                          : Loc.T("bolum.hepsiniAl");
            Dress(_claimAll, owed > 0);
        }

        private void RefreshBeat(int beat)
        {
            Chapters.Progress p = _chapters.Progress(_shown);
            Chapters.Tuning t = _chapters.Tuning;
            bool claimed = _chapters.Claimed(_shown, beat);

            _beatName[beat].text = Loc.T("bolum.asama." + beat);
            _beatName[beat].color = claimed ? InkFaint : Ink;
            _beatNote[beat].text = BeatNote(beat, t);
            Progress(_beatFillImage[beat], Chapters.BeatProgress(beat, p, t));

            _beatReward[beat].text = RewardLine(Chapters.BeatGems(_shown, beat, t),
                                                Chapters.BeatCards(_shown, beat, t));

            _beatBtnText[beat].text = claimed ? Loc.T("gorev.alindi") : Loc.T("gorev.al");
            Dress(_beatBtn[beat], _chapters.CanClaim(_shown, beat));
        }

        /// <summary>
        /// What a beat asks for. The numbers come from the tuning rather than the copy, so moving a
        /// threshold in the Inspector moves the sentence with it instead of making it a lie.
        /// </summary>
        private static string BeatNote(int beat, in Chapters.Tuning t)
        {
            string key = "bolum.asama." + beat + ".not";
            switch (beat)
            {
                case Chapters.FirstSmoke: return string.Format(Loc.T(key), t.FirstSmokeLevels);
                case Chapters.TheWorks:   return string.Format(Loc.T(key), t.WorksUnlocks);
                case Chapters.FullSteam:  return string.Format(Loc.T(key), t.FullSteamLevels, t.FullSteamUnlocks);
                default:                  return Loc.T(key);
            }
        }

        private static string RewardLine(long gems, int cards)
            => cards > 0
                ? string.Format("{0}   +{1} {2}", gems, cards, Loc.T("ustabasi.kart"))
                : gems.ToString();

        // ---------------------------------------------------------------- opener
        /// <summary>
        /// Sits in the HUD's bottom row beside the goals and roster openers — see
        /// <see cref="HudUI.AttachBottomButton"/> for why a code-built screen borrows a real row
        /// button's rect rather than anchoring at a fraction of a landscape screen. Order 2 puts it
        /// after those two and before the authored entries, which start at 10.
        /// </summary>
        private void BuildOpener()
        {
            HudUI hud = FindAnyObjectByType<HudUI>(FindObjectsInactive.Include);
            if (hud == null) return;

            Sprite icon = AtolyeKit.Get("bolum_ikon") ?? Resources.Load<Sprite>(OpenerIconResource);
            Button open = hud.AttachBottomButton(2, "BtnBolum",
                                                 icon != null ? icon : UiSkin.ButtonBlue, Show);
            if (open == null) return;

            _openerChip = hud.AttachCounterChip(open);
            if (_openerChip != null) _openerCount = _openerChip.GetComponentInChildren<TMP_Text>(true);
        }

        private void RefreshOpener()
        {
            if (_openerChip == null || _chapters == null) return;
            int pending = _chapters.PendingCount();
            _openerChip.SetActive(pending > 0);
            if (pending > 0 && _openerCount != null) _openerCount.text = pending.ToString();
        }

        /// <summary>
        /// The sheet everything else sits on.
        ///
        /// WHY THIS EXISTS. The screen used to be loose cards floating on a translucent scrim with the
        /// island still moving between them, which is legible in a mock-up and unreadable in motion —
        /// the eye has nothing to anchor on and every gap is a moving picture. One opaque sheet behind
        /// the content is what the dock panel already does (VoyageUI's SeferPaneli), and it is the
        /// difference between a window and a heads-up display.
        ///
        /// Built FIRST so sibling order puts it behind every card, and it eats its own taps so the
        /// scrim's dismiss cannot fire through it.
        /// </summary>
        private void BuildBackdrop()
        {
            RectTransform sheet = Art(_root, "Zemin", _panel,
                                      new Vector2(0.020f, 0.020f), new Vector2(0.980f, 0.842f));
            var image = sheet.GetComponent<Image>();
            image.color = backdrop;
            image.raycastTarget = true;
            var eat = sheet.gameObject.AddComponent<Button>();
            eat.transition = Selectable.Transition.None;
        }

        // ------------------------------------------------------------------ pieces
        // The same handful GoalsUI uses. Kept local rather than pulled up into UiBuild: that file's
        // own comment says it exists so the meta-layer screens stop growing private copies, and these
        // are the sliced-art wrappers around it, not new builders.

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

        private static void Fit(Text label, int min, int max)
        {
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = min;
            label.resizeTextMaxSize = max;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private RectTransform Chip(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            Sprite art = _chip != null ? _chip : _panel;
            RectTransform rt = Art(parent, name, art, aMin, aMax);
            var img = rt.GetComponent<Image>();
            if (art != null) { img.type = Image.Type.Sliced; img.preserveAspect = false; PillFit.Wrap(img); }
            return rt;
        }

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
        /// A capsule bar: a track, and inside it a fill whose WIDTH is driven. Sliced art rather than
        /// an <see cref="Image.Type.Filled"/> draw, which crops a stretched sprite instead of slicing
        /// it and turns the round left cap into a wedge — see GoalsUI.Bar.
        /// </summary>
        private Image Bar(RectTransform parent, Vector2 aMin, Vector2 aMax, Color fallback)
        {
            RectTransform bed = Art(parent, "Cubuk", _barTrack, aMin, aMax);
            var bedImage = bed.GetComponent<Image>();
            bedImage.type = Image.Type.Sliced;
            bedImage.preserveAspect = false;
            PillFit.Wrap(bedImage);
            if (_barTrack == null) bedImage.color = track;

            RectTransform alan = Slot(bed, "DolguAlani", Vector2.zero, Vector2.one);
            alan.offsetMin = new Vector2(3f, 3f);
            alan.offsetMax = new Vector2(-3f, -3f);

            var go = new GameObject("Dolgu", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(alan, false);
            var img = go.GetComponent<Image>();
            img.sprite = _barFill;
            img.type = Image.Type.Sliced;
            img.preserveAspect = false;
            img.raycastTarget = false;
            if (_barFill == null) img.color = fallback;
            UiBuild.Anchor((RectTransform)go.transform, Vector2.zero, new Vector2(0f, 1f));
            PillFit.Wrap(img);
            return img;
        }

        private static void Progress(Image fill, float t)
            => ((RectTransform)fill.transform).anchorMax = new Vector2(Mathf.Clamp01(t), 1f);

        /// <summary>
        /// A claim button's two states, as two SPRITES — the kit's green capsule when there is
        /// something to take and its pale one when there is not. This used to grey the green pill,
        /// which is what the pale capsule exists to save it from. See <see cref="AtolyeKit.Face"/>.
        /// </summary>
        private void Dress(Button b, bool live)
        {
            b.interactable = live;
            if (!AtolyeKit.Face(b, _btnLive, _btnDead, live))
                b.GetComponent<Image>().color = live ? Color.white : new Color(0.72f, 0.75f, 0.80f, 1f);
        }

        private static void Ping() => ServiceLocator.Get<HapticService>()?.Medium();

        private static RectTransform Slot(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
        }
    }
}
