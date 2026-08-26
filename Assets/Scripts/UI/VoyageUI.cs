using Game.Core;
using Game.Data;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The dock: where the player sends bars to sea and takes the cards off a ship that came home.
    ///
    /// Built at runtime on its own canvas rather than folded into <see cref="MarketHudUI"/>, and that is
    /// deliberate for V1 — the yard HUD is a dense anchored layout that is right, and threading a fifth
    /// card through it to add a feature nobody has played yet risks the screen that already works. One
    /// chip in the corner opens this; nothing else on the HUD moves. Docs/VOYAGES.md §13 V4 is where the
    /// dock stops being a panel and becomes a pad on the yard floor.
    ///
    /// It reads <see cref="VoyageService"/> and never computes anything: what a hold holds, how long a
    /// route takes and what it pays are all <see cref="Voyages"/>'s business. A screen that does its own
    /// arithmetic is a second copy of the balance.
    /// </summary>
    public sealed class VoyageUI : MonoBehaviour
    {
        [SerializeField] private int sortingOrder = 120;   // above MarketHudUI's 100

        /// <summary>The scene the board button loads. Its own constant so the string is in one place.</summary>
        private const string SeaScene = "Sea";
        [SerializeField] private float refreshInterval = 0.25f;

        private static readonly Color Chrome  = new Color(0.13f, 0.16f, 0.23f, 0.96f);
        private static readonly Color Backing = new Color(0.05f, 0.06f, 0.09f, 0.82f);
        private static readonly Color Fill    = new Color(0.36f, 0.74f, 0.99f, 0.95f);

        private VoyageService _voyages;
        private MarketService _market;
        private ForemanService _foremen;
        private CaptainService _captainsSvc;
        private ExpeditionService _seaSvc;
        private string _yardKey;

        private RectTransform _panel;
        private RectTransform _holdFill;
        private TMP_Text _state, _detail, _chipLabel;
        private Button _primary, _secondary;
        private TMP_Text _primaryLabel, _secondaryLabel;

        private Button[] _tierButtons;
        private TMP_Text[] _tierLabels;
        private Button _crew;
        private TMP_Text _crewLabel;
        private Button _captainBtn;
        private TMP_Text _captainLabel;
        private Button _board;
        private TMP_Text _boardLabel;

        private RectTransform _voyageView, _yardView;
        private Button _tabVoyage, _tabYard;
        private TMP_Text _tabVoyageLabel, _tabYardLabel, _salvageLabel;
        private readonly Button[] _buy = new Button[Voyages.ShipTrackCount];
        private readonly TMP_Text[] _trackLabel = new TMP_Text[Voyages.ShipTrackCount];
        private readonly TMP_Text[] _buyLabel = new TMP_Text[Voyages.ShipTrackCount];
        private readonly string[] _lastTrack = new string[Voyages.ShipTrackCount];
        private readonly string[] _lastBuy = new string[Voyages.ShipTrackCount];
        private string _lastSalvage;
        private bool _yardTab;

        private int _tier;          // the route the player has picked, while the berth is empty
        private int _foreman = -1;  // who they have put aboard, -1 = nobody
        private int _captain = -1;  // which captain is aboard, -1 = nobody

        private float _clock;
        private bool _open;
        private float _chipPunch;       // the chip's kick when a ship lands, 1 -> 0
        private RectTransform _chip;

        // Last strings pushed to the labels. Compared before assigning because setting TMP_Text.text
        // rebuilds the mesh whether or not the string changed, and this screen refreshes four times a
        // second — see CLAUDE.md on allocations in repeating paths.
        private string _lastState, _lastDetail, _lastPrimary, _lastSecondary, _lastChip,
                       _lastCrew, _lastCaptain, _lastBoard;
        private readonly string[] _lastTier = new string[Voyages.TierCount];
        private readonly bool[] _tierSelected = new bool[Voyages.TierCount];
        private readonly bool[] _tierOpen = new bool[Voyages.TierCount];

        public void Build(VoyageService voyages, MarketService market, string yardKey)
        {
            _voyages = voyages;
            _market = market;
            _yardKey = yardKey;
            _foremen = ServiceLocator.Get<ForemanService>();
            _captainsSvc = ServiceLocator.Get<CaptainService>();
            _seaSvc = ServiceLocator.Get<ExpeditionService>();

            RectTransform canvas = UiBuild.Canvas(transform, "SeferKanvas", sortingOrder);
            UiBuild.EnsureEventSystem(canvas);

            // The opener: a chip low on the right, clear of the HUD's exit button on the left and of
            // the thumb stick, which owns the bottom band.
            RectTransform chip = UiBuild.Flat(canvas, "SeferDugmesi", Chrome,
                                              new Vector2(0.80f, 0.60f), new Vector2(0.985f, 0.70f));
            var chipImage = chip.GetComponent<Image>();
            chipImage.sprite = UiSkin.ButtonBlue != null ? UiSkin.ButtonBlue : UiSkin.Flat;
            chipImage.type = Image.Type.Sliced;
            if (UiSkin.HasArt) chipImage.color = Color.white;
            var chipButton = chip.gameObject.AddComponent<Button>();
            chipButton.targetGraphic = chipImage;
            chipButton.onClick.AddListener(Toggle);
            _chipLabel = Line(chip, "Yazi", 30f, 0f, 1f);
            _chip = chip;

            // The event has existed since V1 with nothing listening. This is what it was for: a ship
            // landing while the player is anywhere in the yard should be noticed without a panel open.
            voyages.Returned += OnReturned;

            BuildPanel(canvas);
            SetTab(false);
            SetOpen(false);
        }

        private void BuildPanel(RectTransform canvas)
        {
            RectTransform dim = UiBuild.Flat(canvas, "Perde", Backing, Vector2.zero, Vector2.one);
            var close = dim.gameObject.AddComponent<Button>();
            close.targetGraphic = dim.GetComponent<Image>();
            close.onClick.AddListener(Close);          // tapping the dimmed backdrop closes, as everywhere else

            _panel = UiBuild.Flat(dim, "SeferPaneli", Chrome,
                                  new Vector2(0.22f, 0.16f), new Vector2(0.78f, 0.84f));
            var panelImage = _panel.GetComponent<Image>();
            panelImage.sprite = UiSkin.Panel != null ? UiSkin.Panel : UiSkin.Flat;
            panelImage.type = Image.Type.Sliced;
            if (UiSkin.HasArt) panelImage.color = Color.white;
            // The panel eats its own taps so the backdrop's close does not fire through it.
            _panel.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;

            // Two tabs: the dock, and the yard that builds the ships. One panel because they are one
            // subject, and a second chip on the HUD for a screen the player opens once an hour would be
            // a second thing in the way of the one they open constantly.
            // The two tabs stop short of the close button. They used to run to 0.94 while the X sits
            // at 0.84..0.97, so the right-hand end of the shipyard tab WAS the close button — a tap
            // there closed the panel instead of switching to it.
            _tabVoyage = Tab(_panel, "SekmeSefer", 0.06f, 0.43f, () => SetTab(false), out _tabVoyageLabel);
            _tabYard   = Tab(_panel, "SekmeTersane", 0.45f, 0.82f, () => SetTab(true), out _tabYardLabel);
            _tabVoyageLabel.text = Loc.T("sefer.baslik");
            _tabYardLabel.text = Loc.T("sefer.tersane");

            _voyageView = UiBuild.Flat(_panel, "SeferGorunumu", new Color(0f, 0f, 0f, 0f),
                                       Vector2.zero, new Vector2(1f, 0.85f));
            _voyageView.GetComponent<Image>().raycastTarget = false;
            _yardView = UiBuild.Flat(_panel, "TersaneGorunumu", new Color(0f, 0f, 0f, 0f),
                                     Vector2.zero, new Vector2(1f, 0.85f));
            _yardView.GetComponent<Image>().raycastTarget = false;

            BuildTierRow();
            BuildYardView();

            _state  = Line(_voyageView, "Durum", 32f, 0.73f, 0.86f);
            _detail = Line(_voyageView, "Ayrinti", 24f, 0.60f, 0.71f);

            UiBuild.Bar(_voyageView, "AmbarCubugu", new Color(0f, 0f, 0f, 0.45f), Fill,
                        new Vector2(0.08f, 0.49f), new Vector2(0.92f, 0.57f), out _holdFill);

            // TWO OFFICERS, ONE ROW. The foreman cuts the risk and the captain does their own job, so
            // both are live choices on the same voyage and both belong on the same line — stacking
            // them would push the two buttons that actually sail the ship off the bottom of the panel.
            _crew = Action(_voyageView, "FormenDugmesi", UiSkin.ButtonBlue, 0.35f, 0.45f, OnCrew, out _crewLabel);
            Span(_crew, 0.08f, 0.49f);

            _captainBtn = Action(_voyageView, "KaptanDugmesi", UiSkin.ButtonBlue, 0.35f, 0.45f,
                                 OnCaptain, out _captainLabel);
            Span(_captainBtn, 0.51f, 0.92f);
            _primary = Action(_voyageView, "AnaDugme", UiSkin.ButtonGreen, 0.20f, 0.31f, OnPrimary, out _primaryLabel);
            _secondary = Action(_voyageView, "YanDugme", UiSkin.ButtonGrey, 0.06f, 0.17f, OnSecondary, out _secondaryLabel);

            // GO OUT WITH HER. Only ever offered for a ship that is actually at sea — a hold still
            // filling at the dock has nowhere to take anybody — and it changes nothing about the
            // voyage. Docs/FIVE_LAYERS.md §4: the sea is a window, and sailing it may only ever add.
            _board = Action(_voyageView, "DenizeCik", UiSkin.ButtonYellow, 0.06f, 0.17f, OnBoard, out _boardLabel);
            Span(_board, 0.52f, 0.92f);

            // Built with Action rather than UiBuild.Btn: Btn parents a legacy Text child, and one Arial
            // glyph on a screen set in Baloo2 is exactly the one that shows.
            TMP_Text closeLabel;
            RectTransform backRect = (RectTransform)Action(_panel, "KapatDugmesi", UiSkin.ButtonGrey,
                                                           0.86f, 0.97f, Close, out closeLabel).transform;
            backRect.anchorMin = new Vector2(0.855f, 0.865f);
            backRect.anchorMax = new Vector2(0.965f, 0.972f);
            closeLabel.text = "×";
        }

        private static Button Tab(Transform parent, string name, float left, float right,
                                  UnityEngine.Events.UnityAction onClick, out TMP_Text label)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = UiSkin.ButtonGrey != null ? UiSkin.ButtonGrey : UiSkin.Flat;
            image.type = Image.Type.Sliced;
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(left, 0.87f);
            rect.anchorMax = new Vector2(right, 0.97f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            label = Line(rect, "Yazi", 28f, 0f, 1f);
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);
            return button;
        }

        /// <summary>
        /// The shipyard: four tracks, what each costs, and what the player has to spend. Hold, Speed
        /// and Crew are bought with salvage; the third and fourth berth with gems — see
        /// Docs/VOYAGES.md §7.
        /// </summary>
        private void BuildYardView()
        {
            _salvageLabel = Line(_yardView, "Hurda", 28f, 0.88f, 1f);

            const float top = 0.84f, rowHeight = 0.185f, gap = 0.02f;
            for (int k = 0; k < Voyages.ShipTrackCount; k++)
            {
                int track = k;
                float hi = top - rowHeight * k;
                float lo = hi - rowHeight + gap;

                RectTransform row = UiBuild.Flat(_yardView, "Sira" + k, new Color(1f, 1f, 1f, 0.06f),
                                                 new Vector2(0.06f, lo), new Vector2(0.94f, hi));
                row.GetComponent<Image>().raycastTarget = false;
                _trackLabel[k] = Line(row, "Ad", 24f, 0f, 1f);
                _trackLabel[k].alignment = TextAlignmentOptions.Left;
                ((RectTransform)_trackLabel[k].transform).anchorMax = new Vector2(0.58f, 1f);

                var go = new GameObject("Al", typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(row, false);
                var image = go.GetComponent<Image>();
                image.sprite = UiSkin.ButtonGreen != null ? UiSkin.ButtonGreen : UiSkin.Flat;
                image.type = Image.Type.Sliced;
                image.color = UiSkin.HasArt ? Color.white : Chrome;
                var rect = (RectTransform)go.transform;
                rect.anchorMin = new Vector2(0.60f, 0.12f);
                rect.anchorMax = new Vector2(0.98f, 0.88f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                _buyLabel[k] = Line(rect, "Yazi", 22f, 0f, 1f);
                var button = go.GetComponent<Button>();
                button.targetGraphic = image;
                button.onClick.AddListener(() => OnBuy(track));
                _buy[k] = button;
            }
        }

        private static readonly string[] TrackKeys =
            { "sefer.ambar", "sefer.hiz", "sefer.murettebat", "sefer.rihtim" };

        private void SetTab(bool yard)
        {
            _yardTab = yard;
            if (_voyageView != null) _voyageView.gameObject.SetActive(!yard);
            if (_yardView != null) _yardView.gameObject.SetActive(yard);
            if (_tabVoyage != null) Paint(_tabVoyage, !yard);
            if (_tabYard != null) Paint(_tabYard, yard);
            Refresh();
        }

        private static void Paint(Button b, bool on)
        {
            var image = b.targetGraphic as Image;
            if (image == null) return;
            Sprite art = on ? UiSkin.ButtonGreen : UiSkin.ButtonGrey;
            image.sprite = art != null ? art : UiSkin.Flat;
            image.color = UiSkin.HasArt ? Color.white : (on ? Fill : Chrome);
        }

        private void OnBuy(int track)
        {
            if (_voyages == null) return;
            _voyages.TryBuyShip(track);
            Refresh();
        }

        /// <summary>
        /// The four tracks, their levels and what the next one costs. Berths read in gems past the
        /// second, which is the one place this feature asks for them.
        /// </summary>
        private void RefreshYard()
        {
            if (_salvageLabel == null) return;

            string salvage = Loc.T("sefer.hurda") + ": " + _voyages.Salvage;
            if (salvage != _lastSalvage) { _salvageLabel.text = salvage; _lastSalvage = salvage; }

            for (int k = 0; k < Voyages.ShipTrackCount; k++)
            {
                int level = _voyages.LevelOf(k);
                string name = Loc.T(TrackKeys[k]) + "   Lv " + level + " / " + _voyages.MaxLevelOf(k);
                if (name != _lastTrack[k]) { _trackLabel[k].text = name; _lastTrack[k] = name; }

                string cost;
                if (_voyages.IsShipMaxed(k)) cost = Loc.T("sefer.azami");
                else
                {
                    long gems = _voyages.GemCostOf(k);
                    cost = gems > 0L
                        ? gems + " " + Loc.T("sefer.elmas")
                        : _voyages.SalvageCostOf(k) + " " + Loc.T("sefer.hurda");
                }
                if (cost != _lastBuy[k]) { _buyLabel[k].text = cost; _lastBuy[k] = cost; }

                _buy[k].interactable = _voyages.CanBuyShip(k);
            }
        }

        /// <summary>
        /// The four routes, side by side. Shown even when locked, with what it takes to open them —
        /// a ladder the player cannot see is a ladder they do not climb.
        /// </summary>
        private void BuildTierRow()
        {
            _tierButtons = new Button[Voyages.TierCount];
            _tierLabels = new TMP_Text[Voyages.TierCount];

            const float left = 0.06f, right = 0.94f, gap = 0.015f;
            float span = (right - left + gap) / Voyages.TierCount;

            for (int t = 0; t < Voyages.TierCount; t++)
            {
                int tier = t;                                   // captured per button, not the loop variable
                var go = new GameObject("Rota" + t, typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(_voyageView, false);
                var image = go.GetComponent<Image>();
                image.sprite = UiSkin.ButtonGrey != null ? UiSkin.ButtonGrey : UiSkin.Flat;
                image.type = Image.Type.Sliced;
                var rect = (RectTransform)go.transform;
                rect.anchorMin = new Vector2(left + span * t, 0.88f);
                rect.anchorMax = new Vector2(left + span * t + span - gap, 1f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                _tierLabels[t] = Line(rect, "Yazi", 22f, 0f, 1f);
                var button = go.GetComponent<Button>();
                button.targetGraphic = image;
                button.onClick.AddListener(() => OnTier(tier));
                _tierButtons[t] = button;
            }
        }

        /// <summary>
        /// Narrows a button built by <see cref="Action"/> to part of the row. Action spans 0.08..0.92
        /// because almost everything on this panel is full width; the officer chips are the exception.
        /// </summary>
        private static void Span(Button button, float left, float right)
        {
            var rect = (RectTransform)button.transform;
            rect.anchorMin = new Vector2(left, rect.anchorMin.y);
            rect.anchorMax = new Vector2(right, rect.anchorMax.y);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Button Action(Transform parent, string name, Sprite art, float bottom, float top,
                                     UnityEngine.Events.UnityAction onClick, out TMP_Text label)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = art != null ? art : UiSkin.Flat;
            image.type = Image.Type.Sliced;
            image.color = UiSkin.HasArt ? Color.white : Chrome;
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.08f, bottom);
            rect.anchorMax = new Vector2(0.92f, top);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            label = Line(rect, "Yazi", 30f, 0f, 1f);
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);
            return button;
        }

        /// <summary>
        /// TMP rather than <see cref="UiBuild.Label"/>, for the reason MarketHudUI gives: TMP's project
        /// default font asset is the game's own type and UiBuild.Label can only ever be Arial.
        /// </summary>
        private static TMP_Text Line(Transform parent, string name, float size, float bottom, float top)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.enableAutoSizing = true;
            text.fontSizeMin = Mathf.Max(16f, size * 0.55f);
            text.fontSizeMax = size;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.color = Color.white;
            text.fontStyle = FontStyles.Bold;
            text.outlineColor = new Color32(8, 22, 44, 225);
            text.outlineWidth = 0.18f;
            text.raycastTarget = false;
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.06f, bottom);
            rect.anchorMax = new Vector2(0.94f, top);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return text;
        }

        /// <summary>Points the dock at a different yard — the hall is one scene and doorways change yards.</summary>
        public void SetYard(string yardKey)
        {
            _yardKey = yardKey;
            _clock = refreshInterval;      // refresh on the next frame rather than up to a quarter second later
        }

        private void Toggle() => SetOpen(!_open);
        private void Close() => SetOpen(false);

        private void SetOpen(bool open)
        {
            _open = open;
            if (open && _voyages != null)
            {
                // A route that was open last time the panel closed may not be the one the player has
                // since unlocked past, and a foreman may have sailed on something else or been spent.
                if (_tier > _voyages.MaxTier()) _tier = _voyages.MaxTier();
                if (_foreman >= 0 && !FreeForeman(_foreman)) _foreman = -1;
                // Same for the captain: they may have sailed on another berth since the panel was
                // last open, and a chip naming somebody who cannot go is a button that does nothing.
                if (_captain >= 0 && !_voyages.CaptainAvailable(_captain)) _captain = -1;
            }
            if (_panel != null && _panel.parent != null)
                _panel.parent.gameObject.SetActive(open);
            Refresh();
        }

        // ------------------------------------------------------------------ actions
        /// <summary>
        /// One button, four meanings, because a berth is only ever in one of four states and a row of
        /// three greyed-out buttons is how a screen tells the player it does not know what it is for.
        /// </summary>
        private void OnPrimary()
        {
            if (_voyages == null) return;
            if (_voyages.IsWaiting(0)) _voyages.TryClaim(0);
            else if (_voyages.IsLoading(0)) _voyages.TrySail(0);
            else if (_voyages.At(0) == null) _voyages.TryStart(_yardKey, _tier, _foreman, _captain);
            Refresh();
        }

        /// <summary>Pick a route. Ignored once a ship is in the berth — its route was fixed when it opened.</summary>
        private void OnTier(int tier)
        {
            if (_voyages == null || _voyages.At(0) != null) return;
            if (!_voyages.TierUnlocked(tier)) return;
            _tier = tier;
            Refresh();
        }

        /// <summary>
        /// Cycle who is aboard: nobody → each hired, unbusy foreman → nobody again. A cycling chip
        /// rather than a picker because there are eight slots and most of a run has two or three hired,
        /// so a modal would be a screen to open in order to press the only button on it.
        /// </summary>
        private void OnCrew()
        {
            if (_voyages == null) return;

            int next = _foreman;
            for (int step = 0; step < Foremen.Count + 1; step++)
            {
                next++;
                if (next >= Foremen.Count) { next = -1; break; }
                if (FreeForeman(next)) break;
            }
            _foreman = next;

            // While a ship is loading the choice is live, so push it through; the service refuses if
            // the ship has already gone, which is the same answer the panel would give.
            if (_voyages.IsLoading(0)) _voyages.TrySetForeman(0, _foreman);
            Refresh();
        }

        /// <summary>
        /// Board the ship in berth 0 and go to sea with her. Nothing about the voyage changes — the
        /// clock, the route and the odds are identical for a player who never presses this. If the
        /// curtain refuses (one is already up), the boarding is undone rather than left half-made.
        /// </summary>
        private void OnBoard()
        {
            if (_seaSvc == null || !_seaSvc.Board(0)) return;
            ServiceLocator.Get<HapticService>()?.Medium();
            if (!SceneCurtain.Cover(SeaScene, Fill, Loc.T("deniz.baslik"), false)) _seaSvc.Ashore();
        }

        /// <summary>Hired, and not already at sea on something else.</summary>
        private bool FreeForeman(int station)
            => _foremen != null && _foremen.IsHired(station) && !_voyages.ForemanBusy(station);

        /// <summary>
        /// Cycle which captain is aboard: nobody → each pulled, unbusy captain → nobody again. The
        /// same cycling chip the foreman uses, for the same reason — most of a run has two or three
        /// of the ten, so a picker would be a screen to open in order to press its only button.
        /// </summary>
        private void OnCaptain()
        {
            if (_voyages == null) return;

            int next = _captain;
            for (int step = 0; step < Captains.Count + 1; step++)
            {
                next++;
                if (next >= Captains.Count) { next = -1; break; }
                if (_voyages.CaptainAvailable(next)) break;
            }
            _captain = next;

            if (_voyages.IsLoading(0)) _voyages.TrySetCaptain(0, _captain);
            Refresh();
        }

        /// <summary>
        /// Three meanings, by berth state: give up a load, watch an ad to bring a ship home, or pay
        /// gems to skip a repair.
        ///
        /// The ad is asked for HERE and not in <see cref="VoyageService"/>. Game.Systems does not know
        /// what an ad is and should not start knowing; the screen that offered the button is the thing
        /// that owes the player one. Same shape AdRewardUI already uses.
        /// </summary>
        private void OnSecondary()
        {
            if (_voyages == null) return;

            if (_voyages.IsLoading(0)) { _voyages.TryAbandon(0); Refresh(); return; }

            if (_voyages.IsAtSea(0))
            {
                var ad = ServiceLocator.Get<IAdService>();
                if (ad == null || !ad.Available) return;
                ad.ShowRewarded(() => { _voyages.TryFinishNow(0); Refresh(); });
                return;
            }

            if (_voyages.BerthDamaged(0)) { _voyages.TryRepairNow(0); Refresh(); }
        }

        // ------------------------------------------------------------------ refresh
        private void OnReturned(int berth)
        {
            _chipPunch = 1f;
            ServiceLocator.Get<AudioService>()?.Play(SoundId.Reward);
            ServiceLocator.Get<HapticService>()?.Light();
        }

        /// <summary>Unsubscribe or the service keeps a handle on a destroyed screen across a scene load.</summary>
        private void OnDestroy()
        {
            if (_voyages != null) _voyages.Returned -= OnReturned;
        }

        private void Update()
        {
            if (_chipPunch > 0f && _chip != null)
            {
                _chipPunch = Mathf.Max(0f, _chipPunch - Time.unscaledDeltaTime * 2.4f);
                // Eased, and overshooting on the way out — a linear return reads as a glitch rather
                // than as something arriving.
                float k = 1f + Mathf.Sin(_chipPunch * Mathf.PI) * 0.22f;
                _chip.localScale = new Vector3(k, k, 1f);
            }

            _clock += Time.unscaledDeltaTime;
            if (_clock < refreshInterval) return;
            _clock = 0f;
            Refresh();
        }

        private void Refresh()
        {
            if (_voyages == null) return;

            string chip = _voyages.IsWaiting(0) ? Loc.T("sefer.hazir") : Loc.T("sefer.baslik");
            if (chip != _lastChip && _chipLabel != null) { _chipLabel.text = chip; _lastChip = chip; }

            if (!_open) return;

            if (_yardTab) { RefreshYard(); return; }

            VoyageState v = _voyages.At(0);
            RefreshTiers(v);
            RefreshCrew(v);
            RefreshCaptain(v);
            RefreshBoard(v);

            string state, detail, primary, secondary;
            float fill;
            bool primaryOn = true, secondaryOn = false;

            if (v == null && _voyages.BerthDamaged(0))
            {
                // The berth is being put right after a wreck. Nothing to do but wait, and the panel
                // says how long rather than just refusing the button.
                state = Loc.T("sefer.hasarli");
                detail = UiBuild.Clock(_voyages.RepairSecondsLeft(0));
                primary = Loc.T("sefer.tamir");
                secondary = _voyages.RepairSkipGems + " " + Loc.T("sefer.elmas")
                            + " · " + Loc.T("sefer.hemenOnar");
                primaryOn = false;
                secondaryOn = true;
                fill = 0f;
            }
            else if (v == null)
            {
                double hold = _voyages.HoldSizeFor(_yardKey);
                bool ready = hold > 0d;
                state = Loc.T("sefer.rota" + _tier);
                detail = ready
                    ? NumberFormatter.Format(hold) + " " + Loc.T("sefer.yuk")
                      + "  ·  " + UiBuild.Clock((float)Voyages.VoyageSeconds(_tier, 0, _voyages.Tuning))
                      + "  ·  " + Loc.T("sefer.risk") + " " + Percent(_voyages.RiskFor(_tier, _foreman))
                    : Loc.T("sefer.stoksuz");
                primary = Loc.T("sefer.ac");
                secondary = "";
                primaryOn = ready;
                fill = 0f;
            }
            else if (_voyages.IsWaiting(0))
            {
                state = Loc.T(v.succeeded ? "sefer.dondu" : "sefer.basarisiz");
                detail = "+" + v.payoutCards + " " + Loc.T("sefer.kart");
                primary = Loc.T("sefer.topla");
                secondary = "";
                fill = 1f;
            }
            else if (_voyages.IsAtSea(0))
            {
                state = Loc.T("sefer.denizde");
                detail = UiBuild.Clock(_voyages.SecondsLeft(0));
                primary = Loc.T("sefer.bekle");
                // Opt-in and player-started, per GDD §10. Never offered as "guarantee this voyage" —
                // that would sell the one decision the feature exists to create.
                var ad = ServiceLocator.Get<IAdService>();
                bool adReady = ad != null && ad.Available;
                secondary = Loc.T("sefer.simdiBitir");
                secondaryOn = adReady;
                primaryOn = false;
                fill = 1f;
            }
            else
            {
                double f = _voyages.HoldFraction(0);
                state = Loc.T("sefer.yukleniyor");
                detail = Mathf.RoundToInt((float)(f * 100d)) + "%  ·  "
                         + NumberFormatter.Format(v.held) + " / " + NumberFormatter.Format(v.holdSize)
                         + "  ·  " + Loc.T("sefer.risk") + " " + Percent(_voyages.RiskFor(v.tier, v.foreman));
                primary = Loc.T("sefer.yolaCik");
                secondary = Loc.T("sefer.vazgec");
                primaryOn = f >= _voyages.Tuning.MinLaunchFraction;
                secondaryOn = true;
                fill = (float)f;
            }

            Push(_state, state, ref _lastState);
            Push(_detail, detail, ref _lastDetail);
            Push(_primaryLabel, primary, ref _lastPrimary);
            Push(_secondaryLabel, secondary, ref _lastSecondary);

            if (_holdFill != null) _holdFill.anchorMax = new Vector2(Mathf.Clamp01(fill), 1f);
            if (_primary != null) _primary.interactable = primaryOn;
            if (_secondary != null) _secondary.gameObject.SetActive(secondaryOn);
        }

        /// <summary>
        /// Repaints the route row: which is picked, which are still shut, and what each one risks.
        /// While a ship is in the berth the row shows ITS route and stops taking taps — the route was
        /// chosen when the voyage opened and is not a thing the player gets to revise at sea.
        /// </summary>
        private void RefreshTiers(VoyageState v)
        {
            if (_tierButtons == null) return;
            int shown = v != null ? v.tier : _tier;
            int aboard = v != null ? v.foreman : _foreman;
            int captainAboard = v != null ? v.captain : _captain;
            bool pickable = v == null;

            for (int t = 0; t < _tierButtons.Length; t++)
            {
                bool open = _voyages.TierUnlocked(t);

                // No glyphs. The panel is set in Baloo2, whose atlas is Latin — a padlock or a block
                // character renders as a missing-glyph box, which reads as a bug rather than as a lock.
                // The three-argument overload: a bosun aboard changes this number, and a panel that
                // quoted the risk without them would be lying about the only decision it offers.
                string text = open
                    ? Loc.T("sefer.rota" + t) + "\n" + Percent(_voyages.RiskFor(t, aboard, captainAboard))
                    : Loc.T("sefer.kilitli") + "\n" + _voyages.VoyagesToUnlock(t);
                if (text != _lastTier[t]) { _tierLabels[t].text = text; _lastTier[t] = text; }

                _tierButtons[t].interactable = open && pickable;

                // Only repaint when something actually moved. Image.sprite and Image.color both dirty
                // the canvas on assignment whether or not the value changed, and this runs four times
                // a second — see CLAUDE.md on not rebuilding a canvas for nothing.
                bool selected = t == shown;
                if (selected == _tierSelected[t] && open == _tierOpen[t]) continue;
                _tierSelected[t] = selected;
                _tierOpen[t] = open;

                var image = _tierButtons[t].targetGraphic as Image;
                if (image == null) continue;
                Sprite art = selected ? UiSkin.ButtonGreen : UiSkin.ButtonGrey;
                image.sprite = art != null ? art : UiSkin.Flat;
                image.color = UiSkin.HasArt
                    ? (open ? Color.white : new Color(1f, 1f, 1f, 0.45f))
                    : (selected ? Fill : Chrome);
            }
        }

        /// <summary>Who is aboard. Hidden entirely until the player has hired anyone to put there.</summary>
        private void RefreshCrew(VoyageState v)
        {
            if (_crew == null) return;

            bool anyHired = _foremen != null && _foremen.HiredCount > 0;
            _crew.gameObject.SetActive(anyHired);
            if (!anyHired) return;

            int aboard = v != null ? v.foreman : _foreman;
            string text = aboard < 0
                ? Loc.T("sefer.formen") + ": " + Loc.T("sefer.kimseYok")
                : Loc.T("sefer.formen") + ": " + Loc.Id("istasyon", IslandEconomy.Stations[aboard])
                  + " " + _foremen.LevelOf(aboard);
            if (text != _lastCrew) { _crewLabel.text = text; _lastCrew = text; }

            // Settable at the dock, frozen once she has sailed.
            _crew.interactable = v == null || v.sailedUnix <= 0L;
        }

        /// <summary>
        /// Which captain is aboard. Hidden entirely until the player has pulled one, the same way the
        /// foreman chip hides until anyone is hired — a button naming a system the player has never
        /// met is a question they cannot answer.
        /// </summary>
        private void RefreshCaptain(VoyageState v)
        {
            if (_captainBtn == null) return;

            bool anyOwned = _captainsSvc != null && _captainsSvc.OwnedCount > 0;
            _captainBtn.gameObject.SetActive(anyOwned);
            if (!anyOwned) return;

            int aboard = v != null ? v.captain : _captain;
            string text = aboard < 0
                ? Loc.T("sefer.kaptan") + ": " + Loc.T("sefer.kimseYok")
                : Loc.T("kaptan.ad." + Captains.IdOf(aboard)) + " " + _captainsSvc.Level(aboard);
            if (text != _lastCaptain) { _captainLabel.text = text; _lastCaptain = text; }

            _captainBtn.interactable = v == null || v.sailedUnix <= 0L;
        }

        /// <summary>
        /// The way out to sea. Shown only for a ship that has actually sailed: a hold filling at the
        /// dock has nowhere to take anybody, and a live button that answers "not yet" is worse than
        /// no button at all.
        /// </summary>
        private void RefreshBoard(VoyageState v)
        {
            if (_board == null) return;
            bool canGo = _seaSvc != null && _seaSvc.CanBoard(0);
            _board.gameObject.SetActive(canGo);
            if (canGo) Push(_boardLabel, Loc.T("sefer.denizeCik"), ref _lastBoard);

            // The bottom row is shared. The secondary keeps the whole width when there is nowhere to
            // sail to — a button that shrinks to make room for one that is not there would read as a
            // layout that had lost something.
            if (_secondary != null) Span(_secondary, 0.08f, canGo ? 0.48f : 0.92f);
        }

        private static string Percent(double v) => Mathf.RoundToInt((float)(v * 100d)) + "%";

        private static void Push(TMP_Text label, string value, ref string last)
        {
            if (label == null || value == last) return;
            label.text = value;
            last = value;
        }
    }
}
