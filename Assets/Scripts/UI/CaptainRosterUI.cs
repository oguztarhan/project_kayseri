using Game.Core;
using Game.Data;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The captain screen: the crate down the left, the ten captains in two columns on the right.
    ///
    /// Built in code for the same reason <see cref="GoalsUI"/>, <see cref="ForemanRosterUI"/> and
    /// <see cref="ChapterUI"/> are — the rows come out of <see cref="Captains.Roster"/>, so appending
    /// a captain should cost one entry in that table and nothing here.
    ///
    /// TEN ROWS IN TWO COLUMNS, not one list of ten. The screen is landscape; ten full-height rows
    /// down one side come out around seventy pixels each, which is the letterbox problem GoalsUI's
    /// comment already describes. Five and five gives every row twice the height at no cost.
    ///
    /// THE CRATE SHOWS ITS PITY. Both counters are on the card, in words, because a crate that hides
    /// them is a crate the player has to take on faith — and the whole reason the pity exists is that
    /// a run of bad luck should be visibly finite.
    /// </summary>
    public sealed class CaptainRosterUI : MonoBehaviour
    {
        [SerializeField] private int sortingOrder = 108;

        [Header("Görseller")]
        [Tooltip("Satır gövdesi — MaviSet/panel_beyaz.")]
        [SerializeField] private Sprite cardPanel;
        [Tooltip("Başlık şeridi — MaviSet/serit_mavi.")]
        [SerializeField] private Sprite ribbon;
        [Tooltip("Aksiyon düğmesi — MaviSet/btn_hap_kalin.")]
        [SerializeField] private Sprite actionButton;
        [Tooltip("Kapat düğmesi — MaviSet/btn_kapat_yeni.")]
        [SerializeField] private Sprite closeIcon;
        [Tooltip("İlerleme çubuğunun yatağı ve dolgusu — Gostergeler/slider_yatak, bar_dolgu.")]
        [SerializeField] private Sprite barTrack;
        [SerializeField] private Sprite barFill;
        [Tooltip("Harita sayacı — MaviSet/gosterge_grafit.")]
        [SerializeField] private Sprite chipPill;

        [Header("Renkler")]
        [SerializeField] private Color scrim = new Color(0.04f, 0.05f, 0.08f, 0.92f);
        [Tooltip("Kartların üstünde durduğu zemin. Panel sanatı bağlıysa onu boyar.")]
        [SerializeField] private Color backdrop = new Color(0.15f, 0.18f, 0.26f, 1f);
        [SerializeField] private Color track = new Color(0.10f, 0.11f, 0.16f, 1f);
        [SerializeField] private Color dupeFill = new Color(0.35f, 0.72f, 0.98f, 1f);

        [Header("Derece renkleri — Sıradan → Mitik")]
        [SerializeField]
        private Color[] gradeTint =
        {
            new Color(0.48f, 0.54f, 0.62f, 1f),   // Common
            new Color(0.26f, 0.60f, 0.92f, 1f),   // Rare
            new Color(0.62f, 0.38f, 0.92f, 1f),   // Epic
            new Color(0.96f, 0.66f, 0.18f, 1f),   // Legendary
            new Color(0.94f, 0.28f, 0.42f, 1f),   // Mythic
        };

        /// <summary>The rail button's icon. Missing until the art lands — see Docs/ASSETS.md.</summary>
        private const string OpenerIconResource = "UI/Buttons/kaptan";

        /// <summary>The odds badge on the crate card. Loaded the same way, for the same reason.</summary>
        private const string InfoIconResource = "UI/Buttons/bilgi";

        private static readonly Color Ink = new Color(0.09f, 0.14f, 0.24f, 1f);
        private static readonly Color InkSoft = new Color(0.36f, 0.42f, 0.52f, 1f);
        private static readonly Color InkFaint = new Color(0.58f, 0.63f, 0.71f, 1f);
        private static readonly Color Paper = new Color(0.96f, 0.97f, 1f, 1f);

        private const float RibbonBand = 0.677f;
        private static readonly System.Globalization.CultureInfo Culture =
            System.Globalization.CultureInfo.InvariantCulture;

        private CaptainService _captains;
        private LocalizationService _loc;
        private RectTransform _root;

        private Text _titleLabel, _chartsLabel, _collectedLabel, _pityLabel, _lastPullLabel, _sourceLabel;
        private RectTransform _chartsChip;
        private Button _openOne, _openBulk;
        private Text _openOneText, _openBulkText;
        private TMP_Text _openerCount;
        private GameObject _openerChip;

        private readonly Image[] _rowArt = new Image[Captains.Count];
        private readonly Image[] _rowGrade = new Image[Captains.Count];
        private readonly Text[] _rowName = new Text[Captains.Count];
        private readonly Text[] _rowRole = new Text[Captains.Count];
        private readonly Image[] _rowFill = new Image[Captains.Count];
        private readonly Button[] _rowBtn = new Button[Captains.Count];
        private readonly Text[] _rowBtnText = new Text[Captains.Count];
        private readonly RectTransform[] _rowRoot = new RectTransform[Captains.Count];
        private readonly RosterCardState[] _cardState = new RosterCardState[Captains.Count];
        private readonly int[] _visibleOrder = new int[Captains.Count];
        private RosterSortMode _sortMode;
        private RosterFilterMode _filterMode;
        private Text _sortText, _filterText, _emptyText;
        private RosterInspectPanel _inspect;
        private OddsSheetUI _odds;
        private int _selected = -1;

        private void Awake()
        {
            _captains = ServiceLocator.Get<CaptainService>();
            Build();
            BuildOpener();
            if (_captains != null) _captains.Changed += OnChanged;
            _loc = ServiceLocator.Get<LocalizationService>();
            if (_loc != null) _loc.Changed += OnLanguageChanged;
            Hide();
            RefreshOpener();
        }

        private void OnDestroy()
        {
            if (_captains != null) _captains.Changed -= OnChanged;
            if (_loc != null) _loc.Changed -= OnLanguageChanged;
        }

        private void OnLanguageChanged()
        {
            if (_titleLabel != null) _titleLabel.text = Loc.T("kaptan.baslik");
            if (_sourceLabel != null) _sourceLabel.text = Loc.T("kaptan.nereden");
            Refresh();
            RefreshOpener();
        }

        private void OnChanged() { Refresh(); RefreshOpener(); }

        public void Show() { if (_root != null) _root.gameObject.SetActive(true); Refresh(); }
        public void Hide()
        {
            if (_inspect != null) _inspect.Hide();
            if (_odds != null) _odds.Hide();
            if (_root != null) _root.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ build
        private void Build()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "KaptanKanvas", sortingOrder);
            _root = UiBuild.Flat(canvas, "Karartma", scrim, Vector2.zero, Vector2.one);
            var dismiss = _root.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(Hide);

            BuildBackdrop();

            BuildHeader();
            BuildCrate();
            BuildBrowseBar();

            // Sağ taraf: beş ve beş. Tek sütunda on satır, yatay ekranda şeritten ibaret kalıyor.
            const float top = 0.765f, bottom = 0.030f;
            int perColumn = (Captains.Count + 1) / 2;
            float rh = (top - bottom) / perColumn;

            for (int c = 0; c < Captains.Count; c++)
            {
                int column = c / perColumn;
                int row = c % perColumn;
                float left = column == 0 ? 0.355f : 0.665f;
                float right = column == 0 ? 0.650f : 0.965f;
                BuildRow(c, new Vector2(left, top - (row + 1) * rh + 0.006f),
                            new Vector2(right, top - row * rh - 0.006f));
            }
            _inspect = new RosterInspectPanel(_root);
            _odds = new OddsSheetUI(_root);
        }

        private void BuildBrowseBar()
        {
            Button sort = UiBuild.Btn(_root, "Sirala", string.Empty,
                                      actionButton != null ? actionButton : UiSkin.ButtonGreen,
                                      new Color(0.24f, 0.55f, 0.84f, 1f), 22, CycleSort);
            UiBuild.Anchor((RectTransform)sort.transform,
                           new Vector2(0.355f, 0.775f), new Vector2(0.565f, 0.815f));
            PillFit.Wrap(sort.GetComponent<Image>());
            _sortText = sort.GetComponentInChildren<Text>();
            Fit(_sortText, 12, 22);

            Button filter = UiBuild.Btn(_root, "Filtre", string.Empty,
                                        actionButton != null ? actionButton : UiSkin.ButtonGreen,
                                        new Color(0.24f, 0.55f, 0.84f, 1f), 22, CycleFilter);
            UiBuild.Anchor((RectTransform)filter.transform,
                           new Vector2(0.575f, 0.775f), new Vector2(0.785f, 0.815f));
            PillFit.Wrap(filter.GetComponent<Image>());
            _filterText = filter.GetComponentInChildren<Text>();
            Fit(_filterText, 12, 22);

            _emptyText = UiBuild.Label(Slot(_root, "FiltreBos", new Vector2(0.39f, 0.35f), new Vector2(0.92f, 0.58f)),
                                       "Text", Loc.T("kadro.bos"), 28, TextAnchor.MiddleCenter);
            _emptyText.color = Paper;
            Fit(_emptyText, 16, 28);
            _emptyText.gameObject.SetActive(false);
        }

        private void CycleSort()
        {
            _sortMode = (RosterSortMode)(((int)_sortMode + 1) % 4);
            Refresh();
        }

        private void CycleFilter()
        {
            _filterMode = (RosterFilterMode)(((int)_filterMode + 1) % 4);
            if (_inspect != null) _inspect.Hide();
            Refresh();
        }

        private void BuildHeader()
        {
            RectTransform band = Art(_root, "Serit", ribbon, new Vector2(0.360f, 0.850f), new Vector2(0.640f, 0.992f));
            _titleLabel = UiBuild.Label(Slot(band, "Yazi", new Vector2(0.13f, RibbonBand - 0.13f),
                                        new Vector2(0.87f, RibbonBand + 0.13f)),
                                   "Text", Loc.T("kaptan.baslik"), 38, TextAnchor.MiddleCenter);

            _chartsChip = Chip(_root, "Harita", new Vector2(0.035f, 0.880f), new Vector2(0.235f, 0.963f));
            _chartsLabel = UiBuild.Label(Slot(_chartsChip, "Yazi", new Vector2(0.08f, 0f), new Vector2(0.92f, 1f)),
                                         "Text", string.Empty, 30, TextAnchor.MiddleCenter);
            _chartsLabel.color = Paper;

            Button close = UiBuild.Btn(_root, "Kapat", string.Empty,
                                       closeIcon != null ? closeIcon : UiSkin.ButtonGrey, track, 34, Hide);
            var closeImage = close.GetComponent<Image>();
            closeImage.type = Image.Type.Simple;
            closeImage.preserveAspect = true;
            UiBuild.Anchor((RectTransform)close.transform, new Vector2(0.878f, 0.873f), new Vector2(0.938f, 0.970f));
        }

        /// <summary>The crate card: what it costs, what the two counters are at, and what came out.</summary>
        private void BuildCrate()
        {
            RectTransform c = Art(_root, "Sandik", cardPanel, new Vector2(0.035f, 0.030f), new Vector2(0.330f, 0.815f));

            UiBuild.Label(Slot(c, "Baslik", new Vector2(0.07f, 0.900f), new Vector2(0.78f, 0.975f)),
                          "Text", Loc.T("kaptan.sandik"), 32, TextAnchor.MiddleCenter).color = Ink;

            // Charts cannot be bought, so this crate is outside the platforms' paid-loot-box rule. The
            // badge is here anyway: the card already shows how far each guarantee is away, and the
            // weights behind it are the half that was still taken on faith.
            Sprite infoIcon = Resources.Load<Sprite>(InfoIconResource);
            Button odds = UiBuild.Btn(c, "Oran", infoIcon != null ? string.Empty : "i",
                                      infoIcon != null ? infoIcon : UiSkin.ButtonGrey,
                                      new Color(0.45f, 0.49f, 0.56f, 1f), 22,
                                      () => { if (_odds != null && _captains != null)
                                                  _odds.ShowCaptainCrate(_captains.CrateTuning); });
            UiBuild.Anchor((RectTransform)odds.transform,
                           new Vector2(0.805f, 0.900f), new Vector2(0.945f, 0.975f));

            _collectedLabel = UiBuild.Label(Slot(c, "Toplandi", new Vector2(0.07f, 0.840f), new Vector2(0.93f, 0.895f)),
                                            "Text", string.Empty, 24, TextAnchor.MiddleCenter);
            _collectedLabel.color = InkSoft;

            _openOne = UiBuild.Btn(c, "AcBir", string.Empty,
                                   actionButton != null ? actionButton : UiSkin.ButtonGreen,
                                   new Color(0.24f, 0.68f, 0.36f, 1f), 26, () => Open(1));
            UiBuild.Anchor((RectTransform)_openOne.transform, new Vector2(0.08f, 0.690f), new Vector2(0.92f, 0.800f));
            PillFit.Wrap(_openOne.GetComponent<Image>());
            _openOneText = _openOne.GetComponentInChildren<Text>();

            _openBulk = UiBuild.Btn(c, "AcCok", string.Empty,
                                    actionButton != null ? actionButton : UiSkin.ButtonYellow,
                                    new Color(0.94f, 0.68f, 0.20f, 1f), 26,
                                    () => Open(_captains != null ? _captains.CrateTuning.BulkCount : 10));
            UiBuild.Anchor((RectTransform)_openBulk.transform, new Vector2(0.08f, 0.560f), new Vector2(0.92f, 0.670f));
            PillFit.Wrap(_openBulk.GetComponent<Image>());
            _openBulkText = _openBulk.GetComponentInChildren<Text>();

            _pityLabel = UiBuild.Label(Slot(c, "Teselli", new Vector2(0.07f, 0.400f), new Vector2(0.93f, 0.535f)),
                                       "Text", string.Empty, 22, TextAnchor.UpperCenter);
            _pityLabel.color = InkSoft;
            Fit(_pityLabel, 13, 22);

            _lastPullLabel = UiBuild.Label(Slot(c, "SonCekilis", new Vector2(0.07f, 0.130f), new Vector2(0.93f, 0.385f)),
                                           "Text", string.Empty, 24, TextAnchor.UpperCenter);
            Fit(_lastPullLabel, 14, 24);

            _sourceLabel = UiBuild.Label(Slot(c, "Kaynak", new Vector2(0.07f, 0.030f), new Vector2(0.93f, 0.115f)),
                                         "Text", Loc.T("kaptan.nereden"), 20, TextAnchor.MiddleCenter);
            _sourceLabel.color = InkFaint;
            Fit(_sourceLabel, 12, 20);
        }

        private void BuildRow(int captain, Vector2 aMin, Vector2 aMax)
        {
            RectTransform c = Art(_root, "Kaptan_" + captain, cardPanel, aMin, aMax);
            _rowRoot[captain] = c;
            _rowArt[captain] = c.GetComponent<Image>();
            _rowArt[captain].raycastTarget = true;
            var inspect = c.gameObject.AddComponent<Button>();
            inspect.transition = Selectable.Transition.None;
            int selected = captain;
            inspect.onClick.AddListener(() => ShowDetails(selected));

            // A grade stripe down the left edge — the fastest read on the screen, and the one thing a
            // collection row has to answer before anything else.
            _rowGrade[captain] = Flat(c, "Derece", new Vector2(0.020f, 0.12f), new Vector2(0.055f, 0.88f));

            _rowName[captain] = UiBuild.Label(Slot(c, "Ad", new Vector2(0.085f, 0.55f), new Vector2(0.690f, 0.93f)),
                                              "Text", string.Empty, 26, TextAnchor.MiddleLeft);
            _rowName[captain].color = Ink;
            Fit(_rowName[captain], 13, 26);

            _rowRole[captain] = UiBuild.Label(Slot(c, "Gorev", new Vector2(0.085f, 0.30f), new Vector2(0.690f, 0.53f)),
                                              "Text", string.Empty, 21, TextAnchor.MiddleLeft);
            _rowRole[captain].color = InkSoft;
            Fit(_rowRole[captain], 11, 21);

            _rowFill[captain] = Bar(c, new Vector2(0.085f, 0.09f), new Vector2(0.690f, 0.26f), dupeFill);

            int captured = captain;
            _rowBtn[captain] = UiBuild.Btn(c, "Yukselt", string.Empty,
                                           actionButton != null ? actionButton : UiSkin.ButtonGreen,
                                           new Color(0.24f, 0.68f, 0.36f, 1f), 22,
                                           () => { if (_captains != null && _captains.TryLevelUp(captured)) Ping(); });
            UiBuild.Anchor((RectTransform)_rowBtn[captain].transform,
                           new Vector2(0.715f, 0.300f), new Vector2(0.972f, 0.700f));
            PillFit.Wrap(_rowBtn[captain].GetComponent<Image>());
            _rowBtnText[captain] = _rowBtn[captain].GetComponentInChildren<Text>();
            Fit(_rowBtnText[captain], 11, 22);   // "LEVEL UP" is longer in most languages than "3/2"
        }

        // ------------------------------------------------------------------ act
        private void Open(int crates)
        {
            if (_captains == null) return;
            int[] got = _captains.TryOpen(crates);
            if (got == null || got.Length == 0) return;

            Ping();
            _lastPullLabel.text = PullLine(got);
            _lastPullLabel.color = TintOf(Best(got));
        }

        /// <summary>The best thing in a batch — what the card leads with after a bulk open.</summary>
        private static int Best(int[] got)
        {
            int best = got[0];
            for (int i = 1; i < got.Length; i++)
                if (Captains.RankOf(got[i]) > Captains.RankOf(best)) best = got[i];
            return best;
        }

        /// <summary>
        /// One name for a single open; the best of the batch plus a count for a bulk one. Listing ten
        /// names would not fit and would bury the only one the player is looking for.
        /// </summary>
        private static string PullLine(int[] got)
        {
            int best = Best(got);
            string name = Loc.T("kaptan.ad." + Captains.IdOf(best));
            string grade = Loc.T("kaptan.derece." + (int)Captains.RankOf(best));
            return got.Length == 1
                ? grade + "\n" + name
                : grade + "\n" + name + "\n+" + (got.Length - 1);
        }

        // --------------------------------------------------------------- refresh
        private void Refresh()
        {
            if (_captains == null || _root == null || !_root.gameObject.activeSelf) return;

            // The word, then the number. A chip reading "47K" on its own does not say what 47K IS,
            // and this is the only place the player ever sees charts counted.
            _chartsLabel.text = Loc.T("kaptan.harita") + "  "
                              + NumberFormatter.Format((double)_captains.Charts, 0);
            _collectedLabel.text = string.Format(Loc.T("kaptan.toplandi"),
                                                 _captains.OwnedCount, Captains.Count);
            _sortText.text = "↕ " + Loc.T("kadro.sirala." + (int)_sortMode);
            _filterText.text = "⌄ " + Loc.T("kadro.filtre." + (int)_filterMode);
            _emptyText.text = Loc.T("kadro.bos");

            CaptainCrate.Tuning ct = _captains.CrateTuning;
            // COUNT, THEN PRICE, WITH A SEPARATOR. These read "OPEN 100" and "OPEN 10 900" before,
            // so the single-open button quoted a price with no count and the ten-open button quoted
            // what looked like one number: ten thousand nine hundred. Both now say the same two things
            // in the same order.
            _openOneText.text = string.Format("{0} ×1   ·   {1}", Loc.T("kaptan.ac"), _captains.CrateCost(1));
            _openBulkText.text = string.Format("{0} ×{1}   ·   {2}", Loc.T("kaptan.ac"), ct.BulkCount,
                                               _captains.CrateCost(ct.BulkCount));
            Dress(_openOne, _captains.CanOpen(1));
            Dress(_openBulk, _captains.CanOpen(ct.BulkCount));

            _pityLabel.text = PityLine(ct);

            for (int c = 0; c < Captains.Count; c++)
            {
                _cardState[c] = _captains.CardState(c);
                RefreshRow(c);
            }
            ReflowRows();
            if (_selected >= 0 && _inspect != null && _inspect.Visible) ShowDetails(_selected);
        }

        private void ReflowRows()
        {
            int count = RosterCardQuery.Fill(_cardState, Captains.Count, _sortMode, _filterMode, _visibleOrder);
            for (int c = 0; c < Captains.Count; c++) _rowRoot[c].gameObject.SetActive(false);

            int perColumn = Mathf.Max(1, (count + 1) / 2);
            const float top = 0.765f, bottom = 0.030f;
            float rowHeight = (top - bottom) / perColumn;
            for (int position = 0; position < count; position++)
            {
                int captain = _visibleOrder[position];
                int column = position / perColumn;
                int row = position % perColumn;
                float left = column == 0 ? 0.355f : 0.665f;
                float right = column == 0 ? 0.650f : 0.965f;
                RectTransform card = _rowRoot[captain];
                UiBuild.Anchor(card,
                    new Vector2(left, top - (row + 1) * rowHeight + 0.006f),
                    new Vector2(right, top - row * rowHeight - 0.006f));
                card.gameObject.SetActive(true);
            }
            _emptyText.gameObject.SetActive(count == 0);
        }

        /// <summary>
        /// How far each guarantee is away, in pulls. Shown rather than hidden: a run of bad luck the
        /// player can see the end of is a very different experience from one they cannot.
        /// </summary>
        private string PityLine(in CaptainCrate.Tuning ct)
        {
            string line = string.Empty;
            if (ct.EpicPity > 0)
            {
                int left = ct.EpicPity - _captains.SinceEpic;
                if (left < 1) left = 1;
                line += string.Format(Loc.T("kaptan.teselli"), Loc.T("kaptan.derece.2"), left);
            }
            if (ct.LegendaryPity > 0)
            {
                int left = ct.LegendaryPity - _captains.SinceLegendary;
                if (left < 1) left = 1;
                if (line.Length > 0) line += "\n";
                line += string.Format(Loc.T("kaptan.teselli"), Loc.T("kaptan.derece.3"), left);
            }
            return line;
        }

        private void RefreshRow(int captain)
        {
            RosterCardState state = _cardState[captain];
            bool owned = state.Owned;
            int level = state.Level;
            var grade = Captains.RankOf(captain);

            _rowGrade[captain].color = TintOf(captain);
            _rowArt[captain].color = owned ? Color.white : new Color(0.82f, 0.84f, 0.88f, 1f);

            // Locked entries stay named and visible as collection goals. Ownership is stated on the
            // second line and by the disabled action; hiding the name would turn a goal into a blank.
            _rowName[captain].text = Loc.T("kaptan.ad." + Captains.IdOf(captain));
            _rowName[captain].color = owned ? Ink : InkFaint;

            string role = Loc.T("kaptan.rol." + Captains.RoleOf(captain));
            string rank = Loc.T("kaptan.derece." + (int)grade);
            _rowRole[captain].text = owned
                ? string.Format("{0} · {1} · Lv {2}", rank, role, level)
                : string.Format("{0} · {1} · {2}", rank, role, Loc.T("kaptan.bulunmadi"));
            _rowRole[captain].color = owned ? InkSoft : InkFaint;

            int need = state.DuplicatesRequired;
            int have = state.Duplicates;
            Progress(_rowFill[captain], state.Progress);

            if (!owned)
            {
                _rowBtnText[captain].text = "—";
                Dress(_rowBtn[captain], false);
            }
            else if (level >= Captains.MaxLevel)
            {
                _rowBtnText[captain].text = Loc.T("sefer.azami");
                Dress(_rowBtn[captain], false);
            }
            else
            {
                // The ACTION when it can be taken, the PROGRESS when it cannot. A button that only
                // ever reads "3/2" is a readout somebody has made clickable.
                bool ready = state.CanUpgrade;
                _rowBtnText[captain].text = ready ? Loc.T("kaptan.yukselt") : have + "/" + need;
                Dress(_rowBtn[captain], ready);
            }
        }

        private void ShowDetails(int captain)
        {
            if (_captains == null || _inspect == null || !Captains.Exists(captain)) return;
            _selected = captain;
            RosterCardState state = _captains.CardState(captain);
            string name = Loc.T("kaptan.ad." + Captains.IdOf(captain));
            string rarity = Loc.T("kaptan.derece." + (int)state.Tier);
            string role = Loc.T("kaptan.rol." + state.Role);
            string identity = state.Owned
                ? rarity + " · " + role + " · " + string.Format(Loc.T("atolye.seviye"), state.Level)
                : rarity + " · " + role;
            int nextLevel = state.Owned ? Mathf.Min(Captains.MaxLevel, state.Level + 1) : 1;
            string next = state.IsMaxed ? Loc.T("sefer.azami")
                                        : EffectDeltaAt(captain, state.Level, nextLevel);
            string progress = state.Owned && !state.IsMaxed
                ? string.Format(Loc.T("kadro.ilerleme"), state.Duplicates, state.DuplicatesRequired)
                : state.IsMaxed ? Loc.T("sefer.azami") : Loc.T("kaptan.bulunmadi");
            string status = !state.Owned ? Loc.T("kaptan.bulunmadi")
                : state.Busy ? Loc.T("kaptan.denizde")
                : state.IsMaxed ? Loc.T("sefer.azami")
                : state.CanUpgrade ? Loc.T("kaptan.yukselt") : string.Empty;

            int selected = captain;
            _inspect.Show(name, identity,
                          string.Format(Loc.T("kadro.simdi"), EffectAt(captain, state.Level)),
                          string.Format(Loc.T("kadro.sonraki"), next),
                          progress, status, Loc.T("kaptan.yukselt"), state.CanUpgrade,
                          () => { _captains.TryLevelUp(selected); ShowDetails(selected); });
        }

        private string EffectAt(int captain, int level)
        {
            int role = Captains.RoleOf(captain);
            double first;
            double second;
            switch (role)
            {
                case Captains.Gunner:
                    first = (Captains.SalvageMultiplier(captain, level, _captains.Tuning) - 1d) * 100d;
                    return string.Format(Loc.T("kaptan.rol.1.not"), Percent(first));
                case Captains.Bosun:
                    first = Captains.RiskReduction(captain, level, _captains.Tuning) * 100d;
                    second = (1d - Captains.RepairMultiplier(captain, level, _captains.Tuning)) * 100d;
                    return string.Format(Loc.T("kaptan.rol.2.not"), Percent(first), Percent(second));
                case Captains.Purser:
                    first = Captains.DirectedShare(captain, level, _captains.Tuning) * 100d;
                    return string.Format(Loc.T("kaptan.rol.3.not"), Percent(first));
                default:
                    first = (Captains.ChartMultiplier(captain, level, _captains.Tuning) - 1d) * 100d;
                    return string.Format(Loc.T("kaptan.rol.0.not"), Percent(first));
            }
        }

        private string EffectDeltaAt(int captain, int fromLevel, int toLevel)
        {
            int role = Captains.RoleOf(captain);
            double first;
            double second;
            switch (role)
            {
                case Captains.Gunner:
                    first = (Captains.SalvageMultiplier(captain, toLevel, _captains.Tuning)
                           - Captains.SalvageMultiplier(captain, fromLevel, _captains.Tuning)) * 100d;
                    return string.Format(Loc.T("kaptan.rol.1.not"), Percent(first));
                case Captains.Bosun:
                    first = (Captains.RiskReduction(captain, toLevel, _captains.Tuning)
                           - Captains.RiskReduction(captain, fromLevel, _captains.Tuning)) * 100d;
                    second = (Captains.RepairMultiplier(captain, fromLevel, _captains.Tuning)
                            - Captains.RepairMultiplier(captain, toLevel, _captains.Tuning)) * 100d;
                    return string.Format(Loc.T("kaptan.rol.2.not"), Percent(first), Percent(second));
                case Captains.Purser:
                    first = (Captains.DirectedShare(captain, toLevel, _captains.Tuning)
                           - Captains.DirectedShare(captain, fromLevel, _captains.Tuning)) * 100d;
                    return string.Format(Loc.T("kaptan.rol.3.not"), Percent(first));
                default:
                    first = (Captains.ChartMultiplier(captain, toLevel, _captains.Tuning)
                           - Captains.ChartMultiplier(captain, fromLevel, _captains.Tuning)) * 100d;
                    return string.Format(Loc.T("kaptan.rol.0.not"), Percent(first));
            }
        }

        private static string Percent(double value) => value.ToString("0.#", Culture);

        private Color TintOf(int captain)
        {
            int g = (int)Captains.RankOf(captain);
            return gradeTint != null && g >= 0 && g < gradeTint.Length ? gradeTint[g] : InkSoft;
        }

        // ---------------------------------------------------------------- opener
        /// <summary>
        /// Order 3 in the HUD's bottom row, after goals, roster and chapters — see
        /// <see cref="HudUI.AttachBottomButton"/> for why a code-built screen borrows a real row
        /// button's rect. The chip counts captains ready to be levelled.
        /// </summary>
        private void BuildOpener()
        {
            HudUI hud = FindAnyObjectByType<HudUI>(FindObjectsInactive.Include);
            if (hud == null) return;

            Sprite icon = Resources.Load<Sprite>(OpenerIconResource);
            Button open = hud.AttachBottomButton(3, "BtnKaptan",
                                                 icon != null ? icon : UiSkin.ButtonYellow, Show);
            if (open == null) return;

            _openerChip = hud.AttachCounterChip(open);
            if (_openerChip != null) _openerCount = _openerChip.GetComponentInChildren<TMP_Text>(true);
        }

        private void RefreshOpener()
        {
            if (_openerChip == null || _captains == null) return;
            int pending = _captains.PendingCount();
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
            RectTransform sheet = Art(_root, "Zemin", cardPanel,
                                      new Vector2(0.020f, 0.020f), new Vector2(0.980f, 0.842f));
            var image = sheet.GetComponent<Image>();
            image.color = backdrop;
            image.raycastTarget = true;
            var eat = sheet.gameObject.AddComponent<Button>();
            eat.transition = Selectable.Transition.None;
        }

        // ------------------------------------------------------------------ pieces
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
            UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
            return (RectTransform)go.transform;
        }

        /// <summary>A plain coloured quad — the grade stripe, which must keep its own colour.</summary>
        private static Image Flat(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = UiSkin.Flat;
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
            return img;
        }

        private static void Fit(Text label, int min, int max)
        {
            AccessibilityConfig accessibility = ServiceLocator.Get<AccessibilityConfig>();
            float scale = accessibility != null ? accessibility.TextScale : 1f;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = Mathf.Max(1, Mathf.RoundToInt(min * scale));
            label.resizeTextMaxSize = Mathf.Max(label.resizeTextMinSize, Mathf.RoundToInt(max * scale));
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private RectTransform Chip(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            Sprite art = chipPill != null ? chipPill : cardPanel;
            RectTransform rt = Art(parent, name, art, aMin, aMax);
            var img = rt.GetComponent<Image>();
            if (art != null) { img.type = Image.Type.Sliced; img.preserveAspect = false; PillFit.Wrap(img); }
            return rt;
        }

        private Image Bar(RectTransform parent, Vector2 aMin, Vector2 aMax, Color fallback)
        {
            RectTransform bed = Art(parent, "Cubuk", barTrack, aMin, aMax);
            var bedImage = bed.GetComponent<Image>();
            bedImage.type = Image.Type.Sliced;
            bedImage.preserveAspect = false;
            PillFit.Wrap(bedImage);
            if (barTrack == null) bedImage.color = track;

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
            if (barFill == null) img.color = fallback;
            UiBuild.Anchor((RectTransform)go.transform, Vector2.zero, new Vector2(0f, 1f));
            PillFit.Wrap(img);
            return img;
        }

        private static void Progress(Image fill, float t)
            => ((RectTransform)fill.transform).anchorMax = new Vector2(Mathf.Clamp01(t), 1f);

        private static void Dress(Button b, bool live)
        {
            b.interactable = live;
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
