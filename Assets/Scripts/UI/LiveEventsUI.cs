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
    /// The once-a-second Update only drives the countdowns, and only while the board is open.
    /// </summary>
    public sealed class LiveEventsUI : MonoBehaviour
    {
        /// <summary>Above the captains' 108, below the workshop's 110.</summary>
        [SerializeField] private int sortingOrder = 109;

        [Header("Görseller")]
        [Tooltip("Satır gövdesi — MaviSet/panel_beyaz.")]
        [SerializeField] private Sprite cardPanel;
        [Tooltip("Başlık şeridi — MaviSet/serit_mavi.")]
        [SerializeField] private Sprite ribbon;
        [Tooltip("Kapat düğmesi — MaviSet/btn_kapat_yeni.")]
        [SerializeField] private Sprite closeIcon;
        [Tooltip("Durum rozeti — MaviSet/gosterge_grafit.")]
        [SerializeField] private Sprite chipPill;

        [Header("Renkler")]
        [SerializeField] private Color scrim = new Color(0.04f, 0.05f, 0.08f, 0.92f);
        [Tooltip("Kartların üstünde durduğu zemin. Panel sanatı bağlıysa onu boyar.")]
        [SerializeField] private Color backdrop = new Color(0.15f, 0.18f, 0.26f, 1f);
        [Tooltip("Süren etkinliğin rozeti.")]
        [SerializeField] private Color liveTint = new Color(0.36f, 0.82f, 0.45f, 1f);
        [Tooltip("Henüz açılmamış etkinliğin rozeti.")]
        [SerializeField] private Color soonTint = new Color(0.55f, 0.62f, 0.74f, 1f);
        [Tooltip("Bitmiş ama ödülü duran etkinliğin rozeti.")]
        [SerializeField] private Color owedTint = new Color(0.98f, 0.74f, 0.24f, 1f);

        /// <summary>The rail button's icon. Missing until the art lands — see Docs/ASSETS.md.</summary>
        private const string OpenerIconResource = "UI/Buttons/etkinlik";

        /// <summary>How many cards the column draws. A schedule may hold more; six is what fits without
        /// the rows becoming a list of stripes, and the ones left off are the furthest away.</summary>
        private const int MaxCards = 6;

        private static readonly Color Ink = new Color(0.09f, 0.14f, 0.24f, 1f);
        private static readonly Color InkSoft = new Color(0.36f, 0.42f, 0.52f, 1f);
        private static readonly Color Paper = new Color(0.96f, 0.97f, 1f, 1f);

        private LiveEventService _events;
        private FoundryFestivalService _festival;
        private FoundryFestivalUI _festivalUI;
        private LocalizationService _loc;
        private RectTransform _root;

        private Text _titleLabel, _emptyLabel;
        private TMP_Text _openerCount;
        private GameObject _openerChip;

        private readonly RectTransform[] _cardRoot = new RectTransform[MaxCards];
        private readonly Text[] _cardName = new Text[MaxCards];
        private readonly Text[] _cardState = new Text[MaxCards];
        private readonly Text[] _cardClock = new Text[MaxCards];
        private readonly Image[] _cardBadge = new Image[MaxCards];

        /// <summary>Which event each card is showing, as an index into the service. -1 = card unused.</summary>
        private readonly int[] _cardEvent = new int[MaxCards];

        private float _tick;

        private void Awake()
        {
            _events = ServiceLocator.Get<LiveEventService>();
            _festival = ServiceLocator.Get<FoundryFestivalService>();
            _festivalUI = FindAnyObjectByType<FoundryFestivalUI>(FindObjectsInactive.Include);
            Build();
            BuildOpener();
            if (_events != null) _events.Changed += OnChanged;
            if (_festival != null) _festival.Changed += OnChanged;
            _loc = ServiceLocator.Get<LocalizationService>();
            if (_loc != null) _loc.Changed += OnLanguageChanged;
            Hide();
            RefreshOpener();
        }

        private void OnDestroy()
        {
            if (_events != null) _events.Changed -= OnChanged;
            if (_festival != null) _festival.Changed -= OnChanged;
            if (_loc != null) _loc.Changed -= OnLanguageChanged;
        }

        private void OnChanged() { Refresh(); RefreshOpener(); }

        private void OnLanguageChanged()
        {
            if (_titleLabel != null) _titleLabel.text = Loc.T("etkinlik.baslik");
            Refresh();
            RefreshOpener();
        }

        public void Show()
        {
            if (_root == null) return;
            _root.gameObject.SetActive(true);
            _tick = 0f;
            Refresh();
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
            RectTransform canvas = UiBuild.Canvas(transform, "EtkinlikKanvas", sortingOrder);
            _root = UiBuild.Flat(canvas, "Karartma", scrim, Vector2.zero, Vector2.one);
            var dismiss = _root.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(Hide);

            BuildBackdrop();
            BuildHeader();

            const float top = 0.800f, bottom = 0.040f;
            float ch = (top - bottom) / MaxCards;
            for (int i = 0; i < MaxCards; i++)
                BuildCard(i, new Vector2(0.060f, top - (i + 1) * ch + 0.008f),
                             new Vector2(0.940f, top - i * ch - 0.008f));

            // The line that stands in for an empty board. A screen that opens on nothing and explains
            // nothing is read as a broken screen, and with no schedule authored yet this is the state
            // the build actually ships in.
            _emptyLabel = UiBuild.Label(Slot(_root, "Bos", new Vector2(0.10f, 0.380f), new Vector2(0.90f, 0.480f)),
                                        "Text", Loc.T("etkinlik.yok"), 34, TextAnchor.MiddleCenter);
            _emptyLabel.color = InkSoft;
        }

        /// <summary>The sheet everything else sits on — see ChapterUI.BuildBackdrop for why it exists.
        /// Built first so sibling order puts it behind every card, and it eats its own taps so the
        /// scrim's dismiss cannot fire through it.</summary>
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

        private void BuildHeader()
        {
            RectTransform band = Art(_root, "Serit", ribbon,
                                     new Vector2(0.360f, 0.850f), new Vector2(0.640f, 0.992f));
            _titleLabel = UiBuild.Label(Slot(band, "Yazi", new Vector2(0.13f, 0.547f), new Vector2(0.87f, 0.807f)),
                                        "Text", Loc.T("etkinlik.baslik"), 38, TextAnchor.MiddleCenter);

            Button close = UiBuild.Btn(_root, "Kapat", string.Empty,
                                       closeIcon != null ? closeIcon : UiSkin.ButtonGrey,
                                       new Color(0.10f, 0.11f, 0.16f, 1f), 34, Hide);
            var closeImage = close.GetComponent<Image>();
            closeImage.type = Image.Type.Simple;
            closeImage.preserveAspect = true;
            UiBuild.Anchor((RectTransform)close.transform,
                           new Vector2(0.878f, 0.873f), new Vector2(0.938f, 0.970f));
        }

        private void BuildCard(int i, Vector2 aMin, Vector2 aMax)
        {
            RectTransform card = Art(_root, "Kart" + i, cardPanel, aMin, aMax);
            _cardRoot[i] = card;
            _cardEvent[i] = -1;

            // The card is the way into whatever the event actually is. A kind with no screen yet does
            // nothing when tapped rather than opening an empty one.
            card.GetComponent<Image>().raycastTarget = true;
            int captured = i;
            var open = card.gameObject.AddComponent<Button>();
            open.transition = Selectable.Transition.None;
            open.onClick.AddListener(() => OpenCard(captured));

            _cardName[i] = UiBuild.Label(Slot(card, "Ad", new Vector2(0.040f, 0.480f), new Vector2(0.640f, 0.920f)),
                                         "Text", string.Empty, 34, TextAnchor.MiddleLeft);
            _cardName[i].color = Ink;

            _cardClock[i] = UiBuild.Label(Slot(card, "Saat", new Vector2(0.040f, 0.090f), new Vector2(0.640f, 0.470f)),
                                          "Text", string.Empty, 28, TextAnchor.MiddleLeft);
            _cardClock[i].color = InkSoft;

            RectTransform badge = Chip(card, "Rozet", new Vector2(0.680f, 0.280f), new Vector2(0.960f, 0.720f));
            _cardBadge[i] = badge.GetComponent<Image>();
            _cardState[i] = UiBuild.Label(Slot(badge, "Yazi", new Vector2(0.06f, 0f), new Vector2(0.94f, 1f)),
                                          "Text", string.Empty, 26, TextAnchor.MiddleCenter);
            _cardState[i].color = Paper;

            card.gameObject.SetActive(false);
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

            if (_emptyLabel != null && _emptyLabel.gameObject.activeSelf != (used == 0))
                _emptyLabel.gameObject.SetActive(used == 0);
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

            if (_cardState[i] != null)
                _cardState[i].text = Loc.T(owed ? "etkinlik.odul"
                                                : live ? "etkinlik.suruyor" : "etkinlik.yakinda");
            if (_cardBadge[i] != null) _cardBadge[i].color = owed ? owedTint : live ? liveTint : soonTint;

            if (_cardClock[i] == null) return;
            if (owed)
            {
                _cardClock[i].text = Loc.T("gorev.al") + " ×" + _festival.PendingCount();
                return;
            }

            long seconds = live ? _events.SecondsLeft(d.Id) : _events.SecondsUntilStart(d.Id);
            _cardClock[i].text = Loc.T(live ? "etkinlik.kalan" : "etkinlik.basliyor")
                                 + " " + HudUI.LongClock(seconds);
        }

        /// <summary>
        /// Puts a finished festival back on the board while it still holds a reward — FIVE_LAYERS.md
        /// R3 made visible, since <see cref="LiveEventService.MarkClaimed"/> would honour the claim
        /// whether or not there were anywhere left to make it.
        /// </summary>
        private int SeatOwed(int used)
        {
            if (_festival == null || used >= MaxCards) return used;
            if (_festival.PendingCount() <= 0) return used;

            string id = _festival.Id;
            if (string.IsNullOrEmpty(id)) return used;

            for (int e = 0; e < _events.Count; e++)
            {
                if (_events.At(e).Id != id) continue;
                if (_events.PhaseOf(id) == LiveEvents.Phase.Closed) _cardEvent[used++] = e;
                break;      // a running one is seated already, an upcoming one owes nothing
            }
            return used;
        }

        /// <summary>Opens the module behind a card. Only the festival has a screen so far.</summary>
        private void OpenCard(int card)
        {
            if (_events == null || card < 0 || card >= MaxCards || _cardEvent[card] < 0) return;
            if (_events.At(_cardEvent[card]).Kind != FoundryFestival.Kind) return;
            if (_festivalUI != null) _festivalUI.Show();
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

            if (_openerChip.activeSelf != (waiting > 0)) _openerChip.SetActive(waiting > 0);
            if (waiting > 0 && _openerCount != null)
            {
                string text = waiting.ToString();
                if (_openerCount.text != text) _openerCount.text = text;
            }
        }

        // ------------------------------------------------------------------ pieces
        // The same handful ChapterUI and GoalsUI use, kept local for the same reason theirs are.

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

        private RectTransform Chip(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            Sprite art = chipPill != null ? chipPill : cardPanel;
            RectTransform rt = Art(parent, name, art, aMin, aMax);
            var img = rt.GetComponent<Image>();
            if (art != null) { img.type = Image.Type.Sliced; img.preserveAspect = false; PillFit.Wrap(img); }
            return rt;
        }

        private static RectTransform Slot(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
        }
    }
}
