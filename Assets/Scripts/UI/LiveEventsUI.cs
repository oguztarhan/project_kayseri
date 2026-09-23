using Game.Core;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The events board: what is running, what is coming, and how long is left of each.
    ///
    /// Built in code for the same reason <see cref="ChapterUI"/> and <see cref="CraftingUI"/> are: the
    /// rows come out of <see cref="LiveEventService"/>'s own schedule, so adding an event to the config
    /// should cost one row there and nothing here. The opener is order 5 in the HUD's bottom row, after
    /// the workshop.
    ///
    /// IT LISTS THE COMING, THE RUNNING, AND THE FINISHED THAT STILL OWE. The last of those needs a
    /// module to be honest about: the service counts progress but does not know what any event's
    /// TARGETS are, so only the module owning the content can tell a closed event that is holding a
    /// reward from one the player merely opened once. <see cref="FoundryFestivalService"/> answers
    /// that for the festival, and until a second module exists a closed event of any other kind is
    /// still left off — drawing "reward waiting" over one nobody can settle would be a lie the player
    /// taps. <see cref="LiveEventService.MarkClaimed"/> never checks the window either way, so an
    /// earned slot stays claimable forever whatever this screen draws.
    ///
    /// DRAWN FROM <see cref="EkranKit"/> ONLY. A row's state is said three ways that agree — its icon
    /// (flag and bell running, chest owed, lock to come), its capsule face (green, orange, pale) and
    /// the word on it — and never by tinting the pre-coloured art.
    ///
    /// The once-a-second Update only drives the countdowns, and only while the board is open.
    /// </summary>
    public sealed class LiveEventsUI : MonoBehaviour
    {
        /// <summary>Above the captains' 108, below the workshop's 110.</summary>
        [SerializeField] private int sortingOrder = 109;

        [Header("Renkler")]
        [SerializeField] private Color scrim = new Color(0.04f, 0.05f, 0.08f, 0.92f);

        /// <summary>The rail button's icon. Missing until the art lands — see Docs/ASSETS.md.</summary>
        private const string OpenerIconResource = "UI/Buttons/etkinlik";

        /// <summary>How many cards the column draws. A schedule may hold more; six is what fits without
        /// the rows becoming a list of stripes, and the ones left off are the furthest away.</summary>
        private const int MaxCards = 6;

        /// <summary>
        /// Six rows make a card about 170 units tall against the navy card's 240 px of art, whose top
        /// and bottom borders alone are 151 of them — drawn at 1:1 the navy well would be a sliver.
        /// Halving-and-a-bit the border keeps the frame a frame and the well tall enough for two lines;
        /// a nine-slice scales its corners evenly, so nothing is squashed, only drawn finer.
        /// </summary>
        private const float CardBorderScale = 1.6f;

        private static readonly Color InkSoft = new Color(0.36f, 0.42f, 0.52f, 1f);

        /// <summary>
        /// The state word's band on the green and pale capsules: inside the caps, and short enough that
        /// with <see cref="Fit"/>'s Truncate a second line only fits at a size where the whole word
        /// already fits on one — so "ĐANG DIỄN RA" shrinks onto a line instead of breaking in half.
        /// </summary>
        private static readonly Vector2 CapsBandMin = new Vector2(0.13f, 0.24f);
        private static readonly Vector2 CapsBandMax = new Vector2(0.87f, 0.76f);

        private enum Face { Live, Soon, Owed }

        private LiveEventService _events;
        private FoundryFestivalService _festival;
        private FoundryFestivalUI _festivalUI;
        private HarborFestivalService _harborFestival;
        private HarborFestivalUI _harborFestivalUI;
        private ProductionSprintService _productionSprint;
        private ProductionSprintUI _productionSprintUI;
        private SeasonalIndustryPassService _industryPass;
        private SeasonalIndustryPassUI _industryPassUI;
        private LocalizationService _loc;
        private RectTransform _root;

        private Text _titleLabel, _emptyLabel;
        private RectTransform _empty;
        private TMP_Text _openerCount;
        private GameObject _openerChip;

        private Sprite _iconLive, _iconSoon, _iconOwed;
        private Sprite _faceLive, _faceSoon, _faceOwed;

        private readonly RectTransform[] _cardRoot = new RectTransform[MaxCards];
        private readonly Text[] _cardName = new Text[MaxCards];
        private readonly Text[] _cardState = new Text[MaxCards];
        private readonly Text[] _cardClock = new Text[MaxCards];
        private readonly Image[] _cardIcon = new Image[MaxCards];
        private readonly Image[] _cardChip = new Image[MaxCards];

        /// <summary>Which event each card is showing, as an index into the service. -1 = card unused.</summary>
        private readonly int[] _cardEvent = new int[MaxCards];

        private float _tick;

        private void Awake()
        {
            _events = ServiceLocator.Get<LiveEventService>();
            _festival = ServiceLocator.Get<FoundryFestivalService>();
            _festivalUI = FindAnyObjectByType<FoundryFestivalUI>(FindObjectsInactive.Include);
            _harborFestival = ServiceLocator.Get<HarborFestivalService>();
            _harborFestivalUI = FindAnyObjectByType<HarborFestivalUI>(FindObjectsInactive.Include);
            if (_harborFestivalUI == null)
            {
                var harbor = new GameObject("LimanFestivaliEkrani");
                harbor.transform.SetParent(transform, false);
                _harborFestivalUI = harbor.AddComponent<HarborFestivalUI>();
            }
            _productionSprint = ServiceLocator.Get<ProductionSprintService>();
            _productionSprintUI = FindAnyObjectByType<ProductionSprintUI>(FindObjectsInactive.Include);
            if (_productionSprintUI == null)
            {
                var sprint = new GameObject("UretimSprintiEkrani");
                sprint.transform.SetParent(transform, false);
                _productionSprintUI = sprint.AddComponent<ProductionSprintUI>();
            }
            _industryPass = ServiceLocator.Get<SeasonalIndustryPassService>();
            _industryPassUI = FindAnyObjectByType<SeasonalIndustryPassUI>(FindObjectsInactive.Include);
            if (_industryPassUI == null)
            {
                var pass = new GameObject("SezonlukSanayiBiletiEkrani");
                pass.transform.SetParent(transform, false);
                _industryPassUI = pass.AddComponent<SeasonalIndustryPassUI>();
            }
            Build();
            BuildOpener();
            if (_events != null) _events.Changed += OnChanged;
            if (_festival != null) _festival.Changed += OnChanged;
            if (_harborFestival != null) _harborFestival.Changed += OnChanged;
            if (_productionSprint != null) _productionSprint.Changed += OnChanged;
            if (_industryPass != null) _industryPass.Changed += OnChanged;
            _loc = ServiceLocator.Get<LocalizationService>();
            if (_loc != null) _loc.Changed += OnLanguageChanged;
            Hide();
            RefreshOpener();
        }

        private void OnDestroy()
        {
            if (_events != null) _events.Changed -= OnChanged;
            if (_festival != null) _festival.Changed -= OnChanged;
            if (_harborFestival != null) _harborFestival.Changed -= OnChanged;
            if (_productionSprint != null) _productionSprint.Changed -= OnChanged;
            if (_industryPass != null) _industryPass.Changed -= OnChanged;
            if (_loc != null) _loc.Changed -= OnLanguageChanged;
        }

        private void OnChanged() { Refresh(); RefreshOpener(); }

        private void OnLanguageChanged()
        {
            if (_titleLabel != null) _titleLabel.text = EtkinlikKit.OneLine(Loc.T("etkinlik.baslik"));
            if (_emptyLabel != null) _emptyLabel.text = Loc.T("etkinlik.yok");
            Refresh();
            RefreshOpener();
        }

        public void Show()
        {
            if (_root == null) return;
            _root.gameObject.SetActive(true);
            _tick = 0f;
            Refresh();
            TutorialUI.NotifyFeatureOpened("events");
        }

        public void Hide() { if (_root != null) _root.gameObject.SetActive(false); }

        /// <summary>The only per-second write, and only while the board is up.</summary>
        private void Update()
        {
            if (_root == null || !_root.gameObject.activeSelf) return;
            _tick += Time.unscaledDeltaTime;
            if (_tick < 1f) return;
            _tick = 0f;
            Refresh();
        }

        // ------------------------------------------------------------------ build
        private void Build()
        {
            _iconLive = EkranKit.Get("etkinlik_ikon");
            _iconSoon = EkranKit.Get("kilit");
            _iconOwed = EkranKit.Get("sandik");
            _faceLive = LigKit.Get("al_butonu");
            _faceSoon = EkranKit.Get("btn_bos");
            _faceOwed = EkranKit.Get("btn_turuncu");

            RectTransform canvas = UiBuild.Canvas(transform, "EtkinlikKanvas", sortingOrder);
            _root = UiBuild.Flat(canvas, "Karartma", UiBuild.Opaque(scrim), Vector2.zero, Vector2.one);
            var dismiss = _root.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(Hide);

            BuildBackdrop();
            BuildHeader();

            const float top = 0.668f, bottom = 0.105f, gap = 0.004f;
            float ch = (top - bottom) / MaxCards;
            for (int i = 0; i < MaxCards; i++)
                BuildCard(i, new Vector2(0.110f, top - (i + 1) * ch + gap),
                             new Vector2(0.890f, top - i * ch - gap));

            BuildEmpty();
            // Content into the safe area; the scrim above it keeps covering the notch.
            UiBuild.InsetContent(_root);
        }

        /// <summary>
        /// The league sheet, eating its own taps so the scrim's dismiss cannot fire through it. Across
        /// 0.03–0.97 for the reason <see cref="LadderUI"/> records: that width keeps the crest unstretched.
        /// </summary>
        private void BuildBackdrop()
        {
            Image sheet = EkranKit.Sliced(_root, "Zemin", LigKit.Board,
                                          new Vector2(0.030f, 0.050f), new Vector2(0.970f, 0.870f), false);
            sheet.raycastTarget = true;
            var eat = sheet.gameObject.AddComponent<Button>();
            eat.transition = Selectable.Transition.None;
        }

        /// <summary>The ribbon under the crest and the close disc on the corner, at the league board's
        /// offsets from its top edge — the same header the contract and mining gear screens wear.</summary>
        private void BuildHeader()
        {
            _titleLabel = EtkinlikKit.Header(_root, EtkinlikKit.OneLine(Loc.T("etkinlik.baslik")), Hide);
        }

        /// <summary>
        /// One row: the navy card, its state icon in the left of the well, name over clock, and the
        /// state capsule on the right. The card is the tap target and the way into whatever the event
        /// actually is; a kind with no screen yet does nothing when tapped rather than opening an empty
        /// one. Parented straight to the scrim, so it lands at <c>Karartma/Guvenli/Kart{i}</c>.
        /// </summary>
        private void BuildCard(int i, Vector2 aMin, Vector2 aMax)
        {
            Image frame = EkranKit.Sliced(_root, "Kart" + i, EkranKit.Get("kart_lacivert"), aMin, aMax, false);
            frame.pixelsPerUnitMultiplier = CardBorderScale;
            frame.raycastTarget = true;
            RectTransform card = frame.rectTransform;
            _cardRoot[i] = card;
            _cardEvent[i] = -1;

            int captured = i;
            var open = card.gameObject.AddComponent<Button>();
            open.transition = Selectable.Transition.None;
            open.targetGraphic = frame;
            open.onClick.AddListener(() => OpenCard(captured));

            _cardIcon[i] = EkranKit.Icon(card, "Simge", _iconSoon, new Vector2(0.050f, 0.20f), new Vector2(0.175f, 0.80f));

            _cardName[i] = UiBuild.Label(Slot(card, "Ad", new Vector2(0.200f, 0.49f), new Vector2(0.665f, 0.80f)),
                                         "Text", string.Empty, 32, TextAnchor.MiddleLeft);
            _cardName[i].color = EkranKit.Paper;
            Fit(_cardName[i], 16, 32);

            _cardClock[i] = UiBuild.Label(Slot(card, "Saat", new Vector2(0.200f, 0.20f), new Vector2(0.665f, 0.49f)),
                                          "Text", string.Empty, 24, TextAnchor.MiddleLeft);
            _cardClock[i].color = EkranKit.PaperSoft;
            Fit(_cardClock[i], 12, 24);

            _cardChip[i] = EkranKit.Sliced(card, "Rozet", _faceSoon, new Vector2(0.685f, 0.25f), new Vector2(0.945f, 0.75f), true);
            _cardState[i] = UiBuild.Label(Slot(_cardChip[i].rectTransform, "Yazi", CapsBandMin, CapsBandMax),
                                          "Text", string.Empty, 24, TextAnchor.MiddleCenter);
            _cardState[i].color = EkranKit.Ink;   // the pale face it is built with; SetFace changes both together
            Fit(_cardState[i], 12, 24);

            card.gameObject.SetActive(false);
        }

        /// <summary>
        /// What stands in for an empty board. A screen that opens on nothing and explains nothing is
        /// read as a broken screen, and with no schedule authored yet this is the state the build
        /// actually ships in — so it gets the kit's own events icon, not a lone line of grey.
        /// </summary>
        private void BuildEmpty()
        {
            _empty = Slot(_root, "Bos", new Vector2(0.10f, 0.300f), new Vector2(0.90f, 0.560f));
            EkranKit.Icon(_empty, "Simge", _iconLive, new Vector2(0.30f, 0.40f), new Vector2(0.70f, 1.00f));
            _emptyLabel = UiBuild.Label(Slot(_empty, "Yazi", new Vector2(0f, 0f), new Vector2(1f, 0.34f)),
                                        "Text", Loc.T("etkinlik.yok"), 34, TextAnchor.MiddleCenter);
            _emptyLabel.color = InkSoft;
            Fit(_emptyLabel, 18, 34);
        }

        private void BuildOpener()
        {
            HudUI hud = FindAnyObjectByType<HudUI>(FindObjectsInactive.Include);
            if (hud == null) return;

            Sprite icon = Resources.Load<Sprite>(OpenerIconResource);
            Button open = hud.AttachBottomButton(5, "BtnEtkinlik",
                                                 icon != null ? icon : UiSkin.ButtonBlue, Show);
            if (open == null) return;

            _openerChip = hud.AttachCounterChip(open);
            if (_openerChip != null) _openerCount = _openerChip.GetComponentInChildren<TMP_Text>(true);
        }

        // ---------------------------------------------------------------- refresh
        /// <summary>
        /// Re-seats the cards on whatever is coming or running, nearest first. Rebuilt rather than
        /// diffed: the list is at most six rows and only moves when an event opens or closes, so the
        /// simple pass is the honest one.
        /// </summary>
        private void Refresh()
        {
            if (_root == null || !_root.gameObject.activeSelf) return;

            for (int i = 0; i < MaxCards; i++) _cardEvent[i] = -1;

            int used = 0;
            if (_events != null)
            {
                // What the player can act on outranks what they can only wait for: running first,
                // then a finished festival still holding a reward, then the ones still to come.
                used = Seat(LiveEvents.Phase.Active, used);
                used = SeatOwed(used);
                used = Seat(LiveEvents.Phase.Upcoming, used);
            }

            for (int i = 0; i < MaxCards; i++)
            {
                bool on = _cardEvent[i] >= 0;
                if (_cardRoot[i] != null && _cardRoot[i].gameObject.activeSelf != on)
                    _cardRoot[i].gameObject.SetActive(on);
                if (on) RefreshCard(i);
            }

            if (_empty != null && _empty.gameObject.activeSelf != (used == 0))
                _empty.gameObject.SetActive(used == 0);
        }

        /// <summary>Fills the free cards with every visible event in <paramref name="phase"/>.</summary>
        private int Seat(LiveEvents.Phase phase, int used)
        {
            for (int e = 0; e < _events.Count && used < MaxCards; e++)
            {
                LiveEvents.Definition d = _events.At(e);
                if (!_events.Visible(d.Id)) continue;
                if (_events.PhaseOf(d.Id) != phase) continue;
                _cardEvent[used++] = e;
            }
            return used;
        }

        private void RefreshCard(int i)
        {
            LiveEvents.Definition d = _events.At(_cardEvent[i]);
            LiveEvents.Phase phase = _events.PhaseOf(d.Id);
            bool live = phase == LiveEvents.Phase.Active;
            bool owed = phase == LiveEvents.Phase.Closed;   // only ever seated while it owes something

            if (_cardName[i] != null) _cardName[i].text = Loc.Id("etkinlik", d.Id);

            SetFace(i, owed ? Face.Owed : live ? Face.Live : Face.Soon);
            if (_cardState[i] != null)
                _cardState[i].text = Loc.T(owed ? "etkinlik.odul"
                                                : live ? "etkinlik.suruyor" : "etkinlik.yakinda");

            if (_cardClock[i] == null) return;
            if (owed)
            {
                int pending = d.Kind == FoundryFestival.Kind && _festival != null ? _festival.PendingCount()
                    : d.Kind == HarborFestival.Kind && _harborFestival != null ? _harborFestival.PendingCount()
                    : d.Kind == ProductionSprint.Kind && _productionSprint != null ? _productionSprint.PendingCount()
                    : d.Kind == SeasonalIndustryPass.Kind && _industryPass != null ? _industryPass.PendingCount()
                    : 0;
                _cardClock[i].text = Loc.T("gorev.al") + " ×" + pending;
                return;
            }

            long seconds = live ? _events.SecondsLeft(d.Id) : _events.SecondsUntilStart(d.Id);
            _cardClock[i].text = Loc.T(live ? "etkinlik.kalan" : "etkinlik.basliyor")
                                 + " " + HudUI.LongClock(seconds);
        }

        /// <summary>
        /// Icon, capsule face and label ink for a state. Only written when the face actually changes —
        /// this runs once a second, and re-assigning a sprite dirties the canvas every time.
        /// </summary>
        private void SetFace(int i, Face face)
        {
            Sprite icon = face == Face.Owed ? _iconOwed : face == Face.Live ? _iconLive : _iconSoon;
            Sprite chip = face == Face.Owed ? _faceOwed : face == Face.Live ? _faceLive : _faceSoon;

            Image iconImage = _cardIcon[i];
            if (iconImage != null && icon != null && iconImage.sprite != icon)
            {
                iconImage.sprite = icon;
                iconImage.enabled = true;
            }

            Image chipImage = _cardChip[i];
            if (chipImage == null || chip == null || chipImage.sprite == chip) return;
            chipImage.sprite = chip;
            var fit = chipImage.GetComponent<PillFit>();
            if (fit != null) fit.Fit();

            // The orange capsule's writing space is its cream inlay; the green and pale ones are
            // writable to their caps.
            Text label = _cardState[i];
            if (label == null) return;
            bool inlay = face == Face.Owed;
            UiBuild.Anchor((RectTransform)label.transform.parent,
                           inlay ? EkranKit.InlayMin : CapsBandMin,
                           inlay ? EkranKit.InlayMax : CapsBandMax);
            label.color = face == Face.Live ? EkranKit.Paper : EkranKit.Ink;
        }

        /// <summary>
        /// Puts a finished festival back on the board while it still holds a reward — FIVE_LAYERS.md
        /// R3 made visible, since <see cref="LiveEventService.MarkClaimed"/> would honour the claim
        /// whether or not there were anywhere left to make it.
        /// </summary>
        private int SeatOwed(int used)
        {
            used = SeatOwed(_festival != null ? _festival.Id : null,
                _festival != null ? _festival.PendingCount() : 0, used);
            used = SeatOwed(_harborFestival != null ? _harborFestival.Id : null,
                _harborFestival != null ? _harborFestival.PendingCount() : 0, used);
            used = SeatOwed(_productionSprint != null ? _productionSprint.SeasonId : null,
                _productionSprint != null ? _productionSprint.PendingCount() : 0, used);
            used = SeatOwed(_industryPass != null ? _industryPass.SeasonId : null,
                _industryPass != null ? _industryPass.PendingCount() : 0, used);
            return used;
        }

        private int SeatOwed(string id, int pending, int used)
        {
            if (used >= MaxCards || pending <= 0 || string.IsNullOrEmpty(id)) return used;

            for (int e = 0; e < _events.Count; e++)
            {
                if (_events.At(e).Id != id) continue;
                if (_events.PhaseOf(id) == LiveEvents.Phase.Closed) _cardEvent[used++] = e;
                break;      // a running one is seated already, an upcoming one owes nothing
            }
            return used;
        }

        /// <summary>Opens the module behind a card.</summary>
        private void OpenCard(int card)
        {
            if (_events == null || card < 0 || card >= MaxCards || _cardEvent[card] < 0) return;
            int kind = _events.At(_cardEvent[card]).Kind;
            if (kind == FoundryFestival.Kind && _festivalUI != null) _festivalUI.Show();
            else if (kind == HarborFestival.Kind && _harborFestivalUI != null) _harborFestivalUI.Show();
            else if (kind == ProductionSprint.Kind && _productionSprintUI != null) _productionSprintUI.Show();
            else if (kind == SeasonalIndustryPass.Kind && _industryPassUI != null) _industryPassUI.Show();
        }

        private void RefreshOpener()
        {
            if (_openerChip == null || _events == null) return;

            // Running events plus rewards waiting — both are the board asking to be opened, and a
            // badge that cannot count the second kind goes dark on the day a festival ends still
            // holding a chest.
            int waiting = 0;
            for (int e = 0; e < _events.Count; e++)
            {
                LiveEvents.Definition d = _events.At(e);
                if (_events.Visible(d.Id) && _events.PhaseOf(d.Id) == LiveEvents.Phase.Active) waiting++;
            }
            if (_festival != null) waiting += _festival.PendingCount();
            if (_harborFestival != null) waiting += _harborFestival.PendingCount();
            if (_productionSprint != null) waiting += _productionSprint.PendingCount();
            if (_industryPass != null) waiting += _industryPass.PendingCount();

            if (_openerChip.activeSelf != (waiting > 0)) _openerChip.SetActive(waiting > 0);
            if (waiting > 0 && _openerCount != null)
            {
                string text = waiting.ToString();
                if (_openerCount.text != text) _openerCount.text = text;
            }
        }

        // ------------------------------------------------------------------ pieces
        private static RectTransform Slot(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
        }

        /// <summary>
        /// Shrink-to-fit so a long translation stays inside its band.
        ///
        /// TRUNCATE, NOT <see cref="UiBuild.Label"/>'s OVERFLOW. Best fit only shrinks against a rect
        /// the text is not allowed to spill out of vertically; left on Overflow it keeps the largest
        /// size, wraps, and draws the second line over whatever is under the band — the state capsules'
        /// rims, on the first build of this screen.
        /// </summary>
        private static void Fit(Text label, int min, int max)
        {
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = min;
            label.resizeTextMaxSize = max;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
        }
    }
}
