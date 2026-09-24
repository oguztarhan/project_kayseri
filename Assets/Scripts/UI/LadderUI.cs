using Game.Core;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The three-day league, laid out the way the reference screens lay a ladder out: the top three on
    /// a podium with first in the middle, the rest as rows beneath, and a reward chest on every paying
    /// position that opens to say what it holds.
    ///
    /// Built in code like <see cref="LiveEventsUI"/> and <see cref="ChapterUI"/>, and made by
    /// <see cref="HudUI"/> when the scene has none — so it needs no prefab wiring to appear. The opener
    /// is order 6 in the bottom row, after the events board.
    ///
    /// IT SAYS THE OPPONENTS ARE NOT PEOPLE, always. The board carries
    /// <see cref="LeaderboardBoard.Synthetic"/> precisely so a screen cannot forget to, and
    /// Docs/LEADERBOARDS.md decision D4 makes that label the condition on which a generated cohort was
    /// allowed to pay rewards at all. The header line and every rival row's tag are written from the
    /// flag, so a real backend behind the same seam retires them without this file changing.
    ///
    /// THE CHEST IS THE REWARD TABLE, not decoration. Every paying rank carries the chest of the
    /// bracket it falls in — gold, silver, bronze, then a plain one for the tail — and tapping it opens
    /// the payout. Ranks past <see cref="Leaderboards.RewardedRanks"/> carry no chest, and a divider
    /// in the list says why.
    ///
    /// A TOP 50: three on the podium and forty-seven rows in a scroll list under it, all built once
    /// when the screen is made and re-labelled on every refresh — nothing is spawned while the board
    /// is open. The list sits on its own sub-canvas, so scrolling it rebuilds the list's batch and not
    /// the podium's. Rows are laid out in canvas units rather than fractions, so a taller phone shows
    /// more rows instead of stretching them.
    ///
    /// THE PLAYER'S BAR is pinned under the board whenever no reward is waiting: their rank, avatar,
    /// name and score, and the EDIT button that opens the profile card. The card also opens by itself
    /// the first time the league is opened, and from a tap on the player's own row.
    ///
    /// ONE COLUMN, because the game is portrait. This screen was first built in two, on the reasoning
    /// that <see cref="ChapterUI"/> had found full-width rows stacked down a LANDSCAPE screen came out
    /// as a letterbox with bars in it. The premise was simply wrong: ProjectSettings allows portrait
    /// only, and <see cref="UiBuild.Canvas"/> lays every screen out against 1080×1920.
    ///
    /// THE ART IS THE DESIGN KIT, loaded through <see cref="LigKit"/>: the board frame with its star
    /// bow, the title ribbon, a medal on each podium card, a chest on every paying position, the green
    /// starred row for the player, and the winged strip for a reward still owed. Avatars are the master
    /// portraits (<see cref="AvatarArt"/>). The kit is pre-coloured, so state is picked by swapping the
    /// SPRITE, never by tinting.
    ///
    /// The once-a-second Update drives the countdown, and once a minute re-asks for the board so the
    /// rivals' hourly progress shows while the screen stays open.
    /// </summary>
    public sealed class LadderUI : MonoBehaviour
    {
        /// <summary>Above the events board's 109.</summary>
        [SerializeField] private int sortingOrder = 111;

        [Header("Renkler")]
        [SerializeField] private Color scrim = new Color(0.04f, 0.05f, 0.08f, 0.92f);

        /// <summary>The podium — first, second, third — and then the rows under it.</summary>
        private const int PodiumSlots = 3;
        private const int VisibleRows = Leaderboards.CohortSize;
        private const int ListRows = VisibleRows - PodiumSlots;

        /// <summary>The list rows, in canvas units. See the class note for why not fractions.</summary>
        private const float RowHeight = 92f, RowGap = 10f, ListPad = 6f, DividerHeight = 58f;

        /// <summary>The player's bar under the board, in canvas units: a little under the 150 px the
        /// starred plate is drawn at, so its caps are never enlarged.</summary>
        private const float PinnedHeight = 130f;

        /// <summary>Right-hand columns of a row: the score, then the chest (or the EDIT button).</summary>
        private const float ScoreWidth = 120f, EditWidth = 190f;

        /// <summary>How often an open board re-asks for standings, so rival progress shows.</summary>
        private const int BoardRefreshSeconds = 60;

        /// <summary>
        /// The board frame's box. Read <see cref="BuildBackdrop"/> before changing either: the width is
        /// what keeps the frame's star bow unstretched, not a taste call about margins.
        /// </summary>
        private static readonly Vector2 BoardMin = new Vector2(0.030f, 0.135f);
        private static readonly Vector2 BoardMax = new Vector2(0.970f, 0.965f);

        /// <summary>
        /// The left and right edge every row and card lines up on: inside the frame's own border, which
        /// is 73 px of the art on the left and 78 on the right, plus a little air.
        /// </summary>
        private const float ContentLeft = 0.110f, ContentRight = 0.888f;

        /// <summary>The scroll list's box: under the podium, down to the frame's inner rim.</summary>
        private static readonly Vector2 ListMin = new Vector2(ContentLeft, 0.192f);
        private static readonly Vector2 ListMax = new Vector2(ContentRight, 0.474f);

        /// <summary>
        /// The band under the board, shared by the player's pinned bar and the claim strip — only one
        /// of the two is ever up. Below the frame rather than inside it so the claim strip's winged
        /// star can rise over the board's bottom edge.
        /// </summary>
        private static readonly Vector2 PinnedMin = new Vector2(0.070f, 0.030f);
        private static readonly Vector2 PinnedMax = new Vector2(0.930f, 0.112f);

        /// <summary>
        /// The plate under a rank that is not the player's — a podium card or an ordinary row.
        ///
        /// BLUE, NOT THE KIT PANEL. <c>UiSkin.Panel</c> is wired to <c>Button_Square_Disable</c>, and
        /// on a board whose field is near-white that grey reads exactly as what it is: the disabled
        /// state. The blue plate belongs to the same blue-and-gold language as the frame, and it is
        /// what makes the gold and silver of a medal carry.
        /// </summary>
        private static Sprite RowPlate => UiSkin.ButtonBlue;

        /// <summary>The same info badge the captain crate's odds button uses.</summary>
        private const string InfoIconResource = "UI/Buttons/bilgi";

        private static readonly Color Ink = new Color(0.09f, 0.14f, 0.24f, 1f);
        private static readonly Color InkSoft = new Color(0.36f, 0.42f, 0.52f, 1f);
        private static readonly Color Paper = new Color(0.96f, 0.97f, 1f, 1f);
        private static readonly Color PaperSoft = new Color(0.84f, 0.91f, 1f, 1f);
        private static readonly Color Warn = new Color(0.80f, 0.16f, 0.14f, 1f);

        private LadderService _ladder;
        private PlayerProfileService _profiles;
        private bool _autoOpenPending;   // profile or points card owed, held while a tutorial card is up
        private LocalizationService _loc;
        private RectTransform _root;

        private Text _titleLabel, _clockLabel, _syntheticLabel, _emptyLabel, _claimLabel, _takeLabel;
        private Text _rewardCloseLabel, _dividerLabel;
        private RectTransform _claimStrip;
        private TMP_Text _openerCount;
        private GameObject _openerChip;

        private ScrollRect _scroll;
        private RectTransform _listContent;

        // One parallel set for every position on the board: 0-2 are the podium, 3-49 the list rows.
        private readonly RectTransform[] _slotRoot = new RectTransform[VisibleRows];
        private readonly Image[] _slotBody = new Image[VisibleRows];
        private readonly Text[] _slotRank = new Text[VisibleRows];
        private readonly Text[] _slotName = new Text[VisibleRows];
        /// <summary>Rows only: "simulated rival" under a rival's handle, "you" under the player's.</summary>
        private readonly Text[] _slotTag = new Text[VisibleRows];
        private readonly Text[] _slotScore = new Text[VisibleRows];
        private readonly Image[] _slotChest = new Image[VisibleRows];
        private readonly Image[] _slotAvatar = new Image[VisibleRows];
        /// <summary>Podium only (0-2): the medal carries the place, so those cards have no rank text.</summary>
        private readonly Image[] _slotMedal = new Image[PodiumSlots];
        /// <summary>
        /// The rank each position is currently showing, which is what a chest pays out on.
        ///
        /// Held as a number because the podium has no rank LABEL to read it back out of — the medal
        /// says the place. Parsing it out of the text was always the fragile way round.
        /// </summary>
        private readonly int[] _slotRankValue = new int[VisibleRows];
        private readonly bool[] _slotIsPlayer = new bool[VisibleRows];

        private RectTransform _pinnedRoot;
        private Text _pinnedRank, _pinnedName, _pinnedTag, _pinnedScore, _pinnedEditLabel;
        private Image _pinnedAvatar;
        private int _playerRank;

        private RectTransform _rewardCard;
        private Image _rewardChest;
        private Text _rewardTitle, _rewardNone;
        private RectTransform _rewardGemRow, _rewardCardRow;
        private Text _rewardGemLabel, _rewardCardLabel;

        private RectTransform _pointsCard;
        private Text _pointsTitle, _pointsIntro, _pointsLimit, _pointsTransition, _pointsCloseLabel;
        private readonly RectTransform[] _pointsRow = new RectTransform[4];
        private readonly Text[] _pointsName = new Text[4];
        private readonly Text[] _pointsValue = new Text[4];
        private readonly Text[] _pointsProgress = new Text[4];

        private RectTransform _profileCard;
        private Text _profileTitle, _profileHint, _profilePickLabel, _profileSaveLabel, _profileCancelLabel;
        private Text _namePlaceholder;
        private InputField _nameInput;
        private Image _profilePreview;
        private readonly Image[] _avatarFrame = new Image[PlayerProfiles.AvatarCount];
        private readonly Image[] _avatarHead = new Image[PlayerProfiles.AvatarCount];
        private int _pickedAvatar;

        private readonly Sprite[] _chest = new Sprite[4];
        private float _tick;
        private int _boardSeconds;

        private void Awake()
        {
            _ladder = ServiceLocator.Get<LadderService>();
            _profiles = ServiceLocator.Get<PlayerProfileService>();
            Detach();
            LoadChests();
            Build();
            BuildOpener();
            if (_ladder != null) _ladder.Changed += OnChanged;
            if (_profiles != null) _profiles.Changed += OnChanged;
            _loc = ServiceLocator.Get<LocalizationService>();
            if (_loc != null) _loc.Changed += OnLanguageChanged;
            Hide();
            RefreshOpener();
        }

        private void OnDestroy()
        {
            if (_ladder != null) _ladder.Changed -= OnChanged;
            if (_profiles != null) _profiles.Changed -= OnChanged;
            if (_loc != null) _loc.Changed -= OnLanguageChanged;
        }

        private void OnChanged() { Refresh(); RefreshOpener(); }

        private void OnLanguageChanged()
        {
            if (_titleLabel != null) _titleLabel.text = Loc.T("lig.baslik");
            if (_syntheticLabel != null) _syntheticLabel.text = Loc.T("lig.temsili");
            if (_takeLabel != null) _takeLabel.text = Loc.T("gorev.al");
            if (_emptyLabel != null) _emptyLabel.text = Loc.T("lig.yok");
            if (_rewardCloseLabel != null) _rewardCloseLabel.text = Loc.T("lig.kapat");
            if (_pointsCloseLabel != null) _pointsCloseLabel.text = Loc.T("lig.kapat");
            if (_dividerLabel != null) _dividerLabel.text = DividerText();
            if (_pinnedEditLabel != null) _pinnedEditLabel.text = Loc.T("lig.profil_duzenle");
            if (_profileTitle != null) _profileTitle.text = Loc.T("lig.profil_baslik");
            if (_profilePickLabel != null) _profilePickLabel.text = Loc.T("lig.profil_avatar");
            if (_profileSaveLabel != null) _profileSaveLabel.text = Loc.T("lig.profil_kaydet");
            if (_profileCancelLabel != null) _profileCancelLabel.text = Loc.T("lig.profil_iptal");
            if (_namePlaceholder != null) _namePlaceholder.text = Loc.T("lig.profil_ad");
            RefreshProfileHint(false);
            RefreshPoints();
            Refresh();
            RefreshOpener();
        }

        public void Show()
        {
            if (_root == null) return;
            _root.gameObject.SetActive(true);
            HideReward();
            SetActive(_pointsCard, false);
            SetActive(_profileCard, false);
            _tick = 0f;
            _boardSeconds = 0;
            ServiceLocator.Get<IAnalytics>()?.Log("ladder_open", "season",
                _ladder != null ? _ladder.CurrentSeasonId : string.Empty);
            Refresh();
            ScrollToPlayer();

            // Once each, the first time the league opens: the profile first, because the board is
            // about to show the player's name; then the points card. Most players never tap an info
            // button, and a score whose rules are never read is a number that moves at random.
            // Max's introduction to the league comes first when it is owed; these cards wait for it.
            TutorialUI.NotifyFeatureOpened("league");
            _autoOpenPending = true;
            RunAutoOpen();
        }

        private void RunAutoOpen()
        {
            if (!_autoOpenPending || TutorialUI.CardOnScreen) return;
            _autoOpenPending = false;
            if (_profiles != null && !_profiles.Prompted) ShowProfile();
            else AutoOpenPoints();
        }

        public void Hide()
        {
            _autoOpenPending = false;
            if (_root != null) _root.gameObject.SetActive(false);
        }

        /// <summary>
        /// The countdown every second; the board once a minute. A board is an allocation per request —
        /// fifty entries and the object holding them — so it is never asked for once a second.
        /// </summary>
        private void Update()
        {
            if (_root == null || !_root.gameObject.activeSelf) return;
            RunAutoOpen();
            _tick += Time.unscaledDeltaTime;
            if (_tick < 1f) return;
            _tick = 0f;
            RefreshClock();

            if (++_boardSeconds < BoardRefreshSeconds) return;
            _boardSeconds = 0;
            Refresh();
        }

        private void RefreshClock()
        {
            if (_clockLabel == null || _ladder == null || !_ladder.Available) return;

            // Syncing here is what notices a season ending while the player is watching, and what
            // pushes a score earned since the screen opened onto the board: the service raises Changed
            // and the standings redraw. The countdown alone cannot see either.
            _ladder.Sync();
            _clockLabel.text = Loc.T("etkinlik.kalan") + " " + HudUI.LongClock(_ladder.SecondsLeftInSeason);
        }

        // ------------------------------------------------------------------ build
        /// <summary>
        /// Moves this screen out of any canvas it was hung inside, before a canvas of its own is made.
        ///
        /// A Canvas nested in another Canvas has its render mode IGNORED: it becomes a sub-canvas laid
        /// out inside the parent's RectTransform. Parented under the HUD — the obvious place for
        /// whoever adds this component — it inherits a rect that is not the screen and every anchored
        /// child collapses into it. The first build of this screen did exactly that.
        /// </summary>
        private void Detach()
        {
            if (transform.parent == null) return;
            if (GetComponentInParent<Canvas>(true) == null) return;
            transform.SetParent(null, false);
        }

        /// <summary>Gold, silver, bronze, and the plain chest every paying rank below third shares.
        /// From the design kit; a missing piece leaves the slot null and the position simply shows no
        /// chest rather than a broken one.</summary>
        private void LoadChests()
        {
            _chest[0] = LigKit.Get("sandik_altin");
            _chest[1] = LigKit.Get("sandik_gumus");
            _chest[2] = LigKit.Get("sandik_bronz");
            _chest[3] = LigKit.Get("sandik_sade");
        }

        private void Build()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "LigKanvas", sortingOrder);
            _root = UiBuild.Flat(canvas, "Karartma", UiBuild.Opaque(scrim), Vector2.zero, Vector2.one);
            var dismiss = _root.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(Hide);

            BuildBackdrop();
            BuildHeader();
            BuildPodium();
            BuildList();
            BuildPinnedRow();
            BuildClaimStrip();

            _emptyLabel = UiBuild.Label(Slot(_root, "Bos", new Vector2(0.12f, 0.430f), new Vector2(0.88f, 0.530f)),
                                        "Text", Loc.T("lig.yok"), 32, TextAnchor.MiddleCenter);
            _emptyLabel.color = InkSoft;

            BuildRewardCard();
            BuildPointsCard();
            BuildProfileCard();
            // Content into the safe area; the scrim above it keeps covering the notch.
            RectTransform safe = UiBuild.InsetContent(_root);
            Column(safe);
        }

        /// <summary>
        /// Moves the safe area's content into a column no wider than the 9:16 reference shape. Every
        /// portrait phone is that shape or taller, so on a phone this is a no-op; on a tablet it keeps
        /// the frames from growing wider than their art (see <see cref="AspectCap"/>).
        /// </summary>
        private static void Column(RectTransform safe)
        {
            if (safe == null) return;
            var moving = new Transform[safe.childCount];
            for (int i = 0; i < moving.Length; i++) moving[i] = safe.GetChild(i);

            RectTransform column = Slot(safe, "Sutun", Vector2.zero, Vector2.one);
            for (int i = 0; i < moving.Length; i++) moving[i].SetParent(column, false);
            AspectCap.Wrap(column, 1080f / 1920f);
        }

        /// <summary>
        /// The board: the kit's frame, bow and all, nine-sliced.
        ///
        /// SPANNING 0.03–0.97 IS NOT A MARGIN CHOICE. The frame's star bow rides in the top-centre
        /// segment of a nine-slice, which is the one part that stretches horizontally. The art is 974 px
        /// wide, and this box is about 1015 units on a 16:9 phone and 908 on a 20:9 one. So the bow
        /// would be drawn anywhere from 7% squashed to 4% stretched, and <see cref="FrameFit"/> scales
        /// the slice borders with the box's width to hold it at exactly its drawn shape. Pull these
        /// anchors in and the whole frame just draws smaller.
        /// </summary>
        private void BuildBackdrop()
        {
            var sprite = LigKit.Board;
            var go = new GameObject("Zemin", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_root, false);

            var image = go.GetComponent<Image>();
            image.sprite = sprite != null ? sprite : UiSkin.Panel;
            image.type = Image.Type.Sliced;
            image.raycastTarget = true;
            UiBuild.Anchor((RectTransform)go.transform, BoardMin, BoardMax);
            if (sprite != null) FrameFit.Wrap(image);

            // A tap on the board must not fall through to the scrim behind it and close the screen.
            var eat = go.GetComponent<Button>();
            eat.transition = Selectable.Transition.None;
        }

        /// <summary>
        /// The crown, then the banner. The frame's bow sits at the apex of the board and the title
        /// ribbon lies across the rail just beneath it, which is the arrangement the two pieces were
        /// drawn for — the bow is the ornament, the ribbon is the label.
        /// </summary>
        private void BuildHeader()
        {
            Image band = LigKit.Sliced(_root, "Serit", "serit",
                                       new Vector2(0.215f, 0.793f), new Vector2(0.785f, 0.885f), true);
            _titleLabel = UiBuild.Label(Slot(band.rectTransform, "Yazi",
                                             new Vector2(0.20f, 0.18f), new Vector2(0.80f, 0.82f)),
                                        "Text", Loc.T("lig.baslik"), 38, TextAnchor.MiddleCenter);
            _titleLabel.color = Paper;

            var close = new GameObject("Kapat", typeof(RectTransform), typeof(Image), typeof(Button));
            close.transform.SetParent(_root, false);
            var closeImage = close.GetComponent<Image>();
            Sprite cross = LigKit.Get("kapat");
            closeImage.sprite = cross != null ? cross : UiSkin.ButtonGrey;
            closeImage.preserveAspect = true;
            closeImage.raycastTarget = true;
            var closeButton = close.GetComponent<Button>();
            closeButton.transition = Selectable.Transition.None;
            closeButton.targetGraphic = closeImage;
            closeButton.onClick.AddListener(Hide);
            UiBuild.Anchor((RectTransform)close.transform,
                           new Vector2(0.838f, 0.884f), new Vector2(0.952f, 0.972f));

            // The info button mirrors the close cross in the other top corner — the only free place on a
            // board whose header, podium and rows already fill it top to bottom.
            var info = new GameObject("Bilgi", typeof(RectTransform), typeof(Image), typeof(Button));
            info.transform.SetParent(_root, false);
            var infoImage = info.GetComponent<Image>();
            Sprite badge = Resources.Load<Sprite>(InfoIconResource);
            infoImage.sprite = badge != null ? badge : UiSkin.ButtonBlue;
            infoImage.preserveAspect = true;
            infoImage.raycastTarget = true;
            var infoButton = info.GetComponent<Button>();
            infoButton.transition = Selectable.Transition.None;
            infoButton.targetGraphic = infoImage;
            infoButton.onClick.AddListener(ShowPoints);
            UiBuild.Anchor((RectTransform)info.transform,
                           new Vector2(0.048f, 0.884f), new Vector2(0.162f, 0.972f));
            if (badge == null)
                UiBuild.Label(info.transform, "Text", "i", 40, TextAnchor.MiddleCenter).color = Paper;

            _clockLabel = UiBuild.Label(Slot(_root, "Sure", new Vector2(0.130f, 0.744f), new Vector2(0.870f, 0.792f)),
                                        "Text", string.Empty, 30, TextAnchor.MiddleCenter);
            _clockLabel.color = Ink;

            _syntheticLabel = UiBuild.Label(Slot(_root, "Temsili", new Vector2(0.130f, 0.708f), new Vector2(0.870f, 0.744f)),
                                            "Text", Loc.T("lig.temsili"), 22, TextAnchor.MiddleCenter);
            _syntheticLabel.color = InkSoft;
            Fit(_syntheticLabel, 14, 22);
        }

        /// <summary>
        /// The three cards at the top. First sits in the MIDDLE and stands taller than the other two —
        /// the arrangement is what says "podium" before a single number is read, and putting first on
        /// the left would just be a list with bigger rows.
        /// </summary>
        private void BuildPodium()
        {
            BuildPodiumCard(0, new Vector2(0.370f, 0.489f), new Vector2(0.630f, 0.702f));          // 1st, centre
            BuildPodiumCard(1, new Vector2(ContentLeft, 0.489f), new Vector2(0.358f, 0.650f));      // 2nd, left
            BuildPodiumCard(2, new Vector2(0.642f, 0.489f), new Vector2(ContentRight, 0.634f));     // 3rd, right
        }

        /// <summary>
        /// One podium card, top to bottom: the avatar with the medal hung on its corner, the name, the
        /// chest, the score. The avatar box is kept square by an AspectRatioFitter whatever shape the
        /// card comes out at on a given phone, so the portrait is never stretched.
        /// </summary>
        private void BuildPodiumCard(int i, Vector2 aMin, Vector2 aMax)
        {
            RectTransform body = Art(_root, "Podyum" + i, RowPlate, aMin, aMax);
            _slotRoot[i] = body;
            _slotBody[i] = body.GetComponent<Image>();
            Tappable(body, i);

            _slotAvatar[i] = AvatarArt.Badge(body, "Avatar", new Vector2(0.5f, 0.585f), new Vector2(0.5f, 0.955f),
                                             out Image frame);
            var square = frame.gameObject.AddComponent<AspectRatioFitter>();
            square.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            square.aspectRatio = 1f;

            // The medal carries the place — it is drawn with the 1, 2 or 3 on its face — so there is no
            // rank label to duplicate it. Pinned on the avatar's lower-left corner, it reads as a medal
            // on the winner rather than a sticker on the card. Its bottom sits on the avatar's, so it
            // never reaches down into the name; its overhang to the left is 0.22 of the avatar, which
            // stays inside the narrowest card's margin beside a centred avatar.
            _slotMedal[i] = LigKit.Icon(frame.rectTransform, "Madalya", "madalya_altin",
                                        new Vector2(-0.22f, 0.00f), new Vector2(0.30f, 0.52f));

            _slotName[i] = Rimmed(UiBuild.Label(Slot(body, "Ad", new Vector2(0.05f, 0.465f), new Vector2(0.95f, 0.575f)),
                                         "Text", string.Empty, 24, TextAnchor.MiddleCenter));
            Fit(_slotName[i], 14, 24);
            _slotChest[i] = Chest(body, new Vector2(0.215f, 0.165f), new Vector2(0.785f, 0.455f), i);
            _slotScore[i] = Rimmed(UiBuild.Label(Slot(body, "Puan", new Vector2(0.04f, 0.025f), new Vector2(0.96f, 0.150f)),
                                          "Text", string.Empty, 28, TextAnchor.MiddleCenter));

            body.gameObject.SetActive(false);
        }

        /// <summary>
        /// Ranks four to fifty, in a vertical scroll list under the podium.
        ///
        /// THE LIST IS ITS OWN CANVAS. Scrolling moves the content every frame the finger does, and a
        /// moved graphic dirties the batch of the canvas it sits on. On the screen's own canvas that
        /// would be the podium, the header and the frame as well; on a sub-canvas it is only the rows.
        /// </summary>
        private void BuildList()
        {
            var listGo = new GameObject("Liste", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster),
                                        typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            listGo.transform.SetParent(_root, false);
            RectTransform viewport = UiBuild.Anchor((RectTransform)listGo.transform, ListMin, ListMax);

            // Invisible, but a raycast target: a drag that starts between two rows must still scroll.
            var catcher = listGo.GetComponent<Image>();
            catcher.sprite = UiSkin.Flat;
            catcher.color = new Color(1f, 1f, 1f, 0f);
            catcher.raycastTarget = true;
            catcher.canvasRenderer.cullTransparentMesh = true;   // hit-testable, never drawn

            var content = new GameObject("Icerik", typeof(RectTransform));
            content.transform.SetParent(viewport, false);
            _listContent = (RectTransform)content.transform;
            _listContent.anchorMin = new Vector2(0f, 1f);
            _listContent.anchorMax = new Vector2(1f, 1f);
            _listContent.pivot = new Vector2(0.5f, 1f);
            _listContent.anchoredPosition = Vector2.zero;
            _listContent.sizeDelta = new Vector2(0f, RowTop(ListRows - 1) + RowHeight + ListPad);

            _scroll = listGo.GetComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Elastic;
            _scroll.inertia = true;
            _scroll.scrollSensitivity = 30f;
            _scroll.viewport = viewport;
            _scroll.content = _listContent;

            for (int r = 0; r < ListRows; r++) BuildListRow(PodiumSlots + r, RowTop(r));
            BuildDivider();
        }

        /// <summary>Top edge of list row <paramref name="r"/> (rank r + 4), measured down from the
        /// content's top. Rows past the last paying rank sit below the divider.</summary>
        private static float RowTop(int r)
        {
            float top = ListPad + r * (RowHeight + RowGap);
            if (r >= Leaderboards.RewardedRanks - PodiumSlots) top += DividerHeight;
            return top;
        }

        private void BuildListRow(int i, float top)
        {
            RectTransform body = Art(_listContent, "Satir" + i, RowPlate, new Vector2(0f, 1f), new Vector2(1f, 1f));
            body.offsetMin = new Vector2(0f, -(top + RowHeight));
            body.offsetMax = new Vector2(0f, -top);
            _slotRoot[i] = body;
            _slotBody[i] = body.GetComponent<Image>();
            Tappable(body, i);

            // A row swaps between the 80×80 kit panel and the kit's 725×150 starred plate, whose end
            // caps must stay round in a box shorter than the art — so it carries the same correction
            // every capsule in the game does.
            PillFit.Wrap(_slotBody[i]);

            BuildLine(body, RowHeight, RowHeight * 0.74f + 26f, 26,
                      out _slotRank[i], out _slotAvatar[i], out _slotName[i], out _slotTag[i], out _slotScore[i]);

            // Square and inside the plate's rim, so the chest's own outline never crosses the border.
            _slotChest[i] = Chest(body, Vector2.zero, Vector2.zero, i);
            PinRight((RectTransform)_slotChest[i].transform, 14f, RowHeight * 0.74f, RowHeight * 0.74f);

            body.gameObject.SetActive(false);
        }

        /// <summary>
        /// The pieces of one full-width line, placed in units of its height <paramref name="h"/>:
        /// the rank centred on the starred plate's gold star (the star sits 0.45 h in from the left
        /// edge once <see cref="PillFit"/> maps the art's height onto the row's), a square avatar just
        /// right of the star, the name over its tag, and the score. <paramref name="rightReserve"/> is
        /// what the caller keeps free at the right end for its own chest or button.
        /// </summary>
        private static void BuildLine(RectTransform body, float h, float rightReserve, int nameSize,
                                      out Text rank, out Image avatar, out Text name, out Text tag, out Text score)
        {
            rank = Rimmed(UiBuild.Label(PinLeft(Slot(body, "Sira", Vector2.zero, Vector2.zero), 0f, h * 0.9f, h),
                                        "Text", string.Empty, 28, TextAnchor.MiddleCenter));

            avatar = AvatarArt.Badge(body, "Avatar", Vector2.zero, Vector2.zero, out Image frame);
            PinLeft(frame.rectTransform, h * 0.95f, h * 0.80f, h * 0.80f);

            float nameLeft = h * 1.90f;
            float nameRight = rightReserve + ScoreWidth + 12f;

            RectTransform nameBox = Slot(body, "Ad", new Vector2(0f, 0.46f), new Vector2(1f, 0.94f));
            nameBox.offsetMin = new Vector2(nameLeft, 0f);
            nameBox.offsetMax = new Vector2(-nameRight, 0f);
            name = Rimmed(UiBuild.Label(nameBox, "Text", string.Empty, nameSize, TextAnchor.MiddleLeft));
            Fit(name, 14, nameSize);

            RectTransform tagBox = Slot(body, "Etiket", new Vector2(0f, 0.08f), new Vector2(1f, 0.46f));
            tagBox.offsetMin = new Vector2(nameLeft, 0f);
            tagBox.offsetMax = new Vector2(-nameRight, 0f);
            tag = UiBuild.Label(tagBox, "Text", string.Empty, 18, TextAnchor.MiddleLeft);
            tag.color = PaperSoft;
            Fit(tag, 12, 18);
            var rim = tag.gameObject.AddComponent<Outline>();
            rim.effectColor = Rim;
            rim.effectDistance = new Vector2(1f, -1f);

            RectTransform scoreBox = Slot(body, "Puan", new Vector2(1f, 0f), new Vector2(1f, 1f));
            scoreBox.offsetMin = new Vector2(-(rightReserve + ScoreWidth), 0f);
            scoreBox.offsetMax = new Vector2(-rightReserve, 0f);
            score = Rimmed(UiBuild.Label(scoreBox, "Text", string.Empty, 26, TextAnchor.MiddleRight));
            Fit(score, 16, 26);
        }

        /// <summary>
        /// The line between 30th and 31st: the last paying rank above it, the ranks that pay nothing
        /// below. Without it the chests simply stop, which reads as art failing to load.
        /// </summary>
        private void BuildDivider()
        {
            float top = RowTop(Leaderboards.RewardedRanks - PodiumSlots - 1) + RowHeight;
            float bottom = RowTop(Leaderboards.RewardedRanks - PodiumSlots);

            RectTransform band = Slot(_listContent, "OdulsuzSinir", new Vector2(0f, 1f), new Vector2(1f, 1f));
            band.offsetMin = new Vector2(0f, -bottom);
            band.offsetMax = new Vector2(0f, -top);

            Color line = new Color(InkSoft.r, InkSoft.g, InkSoft.b, 0.45f);
            UiBuild.Flat(band, "SolCizgi", line, new Vector2(0.02f, 0.48f), new Vector2(0.18f, 0.52f));
            UiBuild.Flat(band, "SagCizgi", line, new Vector2(0.82f, 0.48f), new Vector2(0.98f, 0.52f));

            _dividerLabel = UiBuild.Label(Slot(band, "Yazi", new Vector2(0.20f, 0.05f), new Vector2(0.80f, 0.95f)),
                                          "Text", DividerText(), 22, TextAnchor.MiddleCenter);
            _dividerLabel.color = InkSoft;
            Fit(_dividerLabel, 14, 22);
        }

        private static string DividerText()
            => string.Format(Loc.T("lig.odulsuz"), Leaderboards.RewardedRanks + 1, Leaderboards.CohortSize);

        /// <summary>
        /// A tappable chest. It is a real button rather than an image: the payout table is not written
        /// anywhere else the player can reach, so this is how "what is 7th worth?" gets answered.
        /// </summary>
        private Image Chest(RectTransform parent, Vector2 aMin, Vector2 aMax, int slot)
        {
            var go = new GameObject("Sandik", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = true;

            var button = go.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = image;
            int captured = slot;
            button.onClick.AddListener(() => ShowReward(captured));

            UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
            return image;
        }

        /// <summary>A position's plate as a button: the player's own row opens their profile.</summary>
        private void Tappable(RectTransform body, int slot)
        {
            var plate = body.GetComponent<Image>();
            plate.raycastTarget = true;
            var button = body.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = plate;
            int captured = slot;
            button.onClick.AddListener(() => { if (_slotIsPlayer[captured]) ShowProfile(); });
        }

        /// <summary>
        /// The player's own bar, below the board rather than inside it — both this and the claim strip
        /// sit in the gap under the frame, where the claim strip's winged star is free to rise over the
        /// board's bottom edge instead of being boxed in by it. A fixed height centred in that band, so
        /// the starred plate is never drawn taller than its art.
        /// </summary>
        private void BuildPinnedRow()
        {
            float mid = (PinnedMin.y + PinnedMax.y) * 0.5f;
            Image plate = LigKit.Sliced(_root, "SenSatiri", "satir_sen",
                                        new Vector2(PinnedMin.x, mid), new Vector2(PinnedMax.x, mid), true);
            _pinnedRoot = plate.rectTransform;
            _pinnedRoot.offsetMin = new Vector2(0f, -PinnedHeight * 0.5f);
            _pinnedRoot.offsetMax = new Vector2(0f, PinnedHeight * 0.5f);

            plate.raycastTarget = true;
            var tap = _pinnedRoot.gameObject.AddComponent<Button>();
            tap.transition = Selectable.Transition.None;
            tap.targetGraphic = plate;
            tap.onClick.AddListener(ShowProfile);

            // The button sits clear of the plate's round right cap, and the score clear of the button.
            const float editRight = 28f;
            BuildLine(_pinnedRoot, PinnedHeight, editRight + EditWidth + 22f, 28,
                      out _pinnedRank, out _pinnedAvatar, out _pinnedName, out _pinnedTag, out _pinnedScore);

            Button edit = LigKit.Capsule(_pinnedRoot, "Duzenle", Loc.T("lig.profil_duzenle"),
                                         Vector2.zero, Vector2.zero, ShowProfile, out _pinnedEditLabel);
            PinRight((RectTransform)edit.transform, editRight, EditWidth, PinnedHeight * 0.60f);
            Fit(_pinnedEditLabel, 14, 28);

            _pinnedRoot.gameObject.SetActive(false);
        }

        private void BuildClaimStrip()
        {
            _claimStrip = LigKit.Plate(_root, "OdulSeridi", "odul_serit", PinnedMin, PinnedMax);

            // Clear of the winged star, which rides the middle of the bar.
            _claimLabel = UiBuild.Label(Slot(_claimStrip, "Yazi", new Vector2(0.050f, 0.02f), new Vector2(0.560f, 0.78f)),
                                        "Text", string.Empty, 28, TextAnchor.MiddleLeft);
            _claimLabel.color = Ink;

            LigKit.Capsule(_claimStrip, "Al", Loc.T("gorev.al"),
                           new Vector2(0.620f, 0.100f), new Vector2(0.955f, 0.700f),
                           ClaimAll, out _takeLabel);

            _claimStrip.gameObject.SetActive(false);
        }

        /// <summary>What a chest holds. Built once and re-labelled, on top of everything else.</summary>
        private void BuildRewardCard()
        {
            _rewardCard = UiBuild.Flat(_root, "OdulKarti", new Color(0.04f, 0.05f, 0.08f, 0.86f),
                                       Vector2.zero, Vector2.one);
            var dismiss = _rewardCard.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(HideReward);

            // 0.19–0.81 for the same reason the board spans 0.03–0.97: the popup frame's crown rides
            // the stretching top-centre segment, and a 670-unit box over 660 px of art draws it at 1.0
            // on 16:9. FrameFit holds it there on every other portrait shape.
            Image frame = CardFrame(_rewardCard, new Vector2(0.190f, 0.270f), new Vector2(0.810f, 0.730f));
            RectTransform card = frame.rectTransform;
            frame.raycastTarget = true;
            var eat = card.gameObject.AddComponent<Button>();
            eat.transition = Selectable.Transition.None;

            _rewardTitle = UiBuild.Label(Slot(card, "Baslik", new Vector2(0.08f, 0.700f), new Vector2(0.92f, 0.820f)),
                                         "Text", string.Empty, 34, TextAnchor.MiddleCenter);
            _rewardTitle.color = Ink;

            var chest = new GameObject("Sandik", typeof(RectTransform), typeof(Image));
            chest.transform.SetParent(card, false);
            _rewardChest = chest.GetComponent<Image>();
            _rewardChest.preserveAspect = true;
            _rewardChest.raycastTarget = false;
            UiBuild.Anchor((RectTransform)chest.transform,
                           new Vector2(0.320f, 0.430f), new Vector2(0.680f, 0.690f));

            // What the chest holds, as the tokens themselves rather than the words for them. This is
            // the only place in the game a player can read what a finishing place pays, so the two
            // currencies are shown the way they are shown everywhere else they are earned.
            _rewardGemRow = RewardLine(card, "Elmas", "elmas", 0.320f, out _rewardGemLabel);
            _rewardCardRow = RewardLine(card, "Kart", "usta_kart", 0.205f, out _rewardCardLabel);

            _rewardNone = UiBuild.Label(Slot(card, "Yok", new Vector2(0.08f, 0.230f), new Vector2(0.92f, 0.360f)),
                                        "Text", Loc.T("lig.odul_yok"), 28, TextAnchor.MiddleCenter);
            _rewardNone.color = InkSoft;

            LigKit.Capsule(card, "Kapat", Loc.T("lig.kapat"),
                           new Vector2(0.230f, 0.040f), new Vector2(0.770f, 0.170f),
                           HideReward, out _rewardCloseLabel);

            _rewardCard.gameObject.SetActive(false);
        }

        /// <summary>
        /// How league points are earned: one row per <see cref="Ladder.Scoring"/> rule — the action, what
        /// it pays, and how many of it have counted this season against the cap.
        ///
        /// THE NUMBERS ARE NEVER IN THE TRANSLATIONS. Values and caps are read from the rules when the
        /// card opens, so retuning a rule cannot leave eleven languages quoting the old one.
        ///
        /// Same frame and width as the reward card, for the same crown reason; it is only TALLER, which
        /// stretches the frame's side rails and not its crown.
        /// </summary>
        private void BuildPointsCard()
        {
            _pointsCard = UiBuild.Flat(_root, "PuanKarti", new Color(0.04f, 0.05f, 0.08f, 0.86f),
                                       Vector2.zero, Vector2.one);
            var dismiss = _pointsCard.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(HidePoints);

            Image frame = CardFrame(_pointsCard, new Vector2(0.190f, 0.230f), new Vector2(0.810f, 0.770f));
            RectTransform card = frame.rectTransform;
            frame.raycastTarget = true;
            var eat = card.gameObject.AddComponent<Button>();
            eat.transition = Selectable.Transition.None;

            _pointsTitle = UiBuild.Label(Slot(card, "Baslik", new Vector2(0.08f, 0.735f), new Vector2(0.92f, 0.825f)),
                                         "Text", Loc.T("lig.puan_baslik"), 34, TextAnchor.MiddleCenter);
            _pointsTitle.color = Ink;
            Fit(_pointsTitle, 20, 34);

            _pointsIntro = UiBuild.Label(Slot(card, "Aciklama", new Vector2(0.10f, 0.650f), new Vector2(0.90f, 0.730f)),
                                         "Text", Loc.T("lig.puan_aciklama"), 24, TextAnchor.MiddleCenter);
            _pointsIntro.color = InkSoft;
            Fit(_pointsIntro, 16, 24);

            const float top = 0.630f, rowHeight = 0.085f;
            for (int i = 0; i < _pointsRow.Length; i++)
            {
                RectTransform row = Slot(card, "Kural" + i, new Vector2(0.100f, top - (i + 1) * rowHeight),
                                         new Vector2(0.900f, top - i * rowHeight));
                _pointsRow[i] = row;

                _pointsName[i] = UiBuild.Label(Slot(row, "Ad", new Vector2(0f, 0f), new Vector2(0.580f, 1f)),
                                               "Text", string.Empty, 26, TextAnchor.MiddleLeft);
                _pointsName[i].color = Ink;
                Fit(_pointsName[i], 16, 26);

                _pointsValue[i] = UiBuild.Label(Slot(row, "Deger", new Vector2(0.590f, 0f), new Vector2(0.740f, 1f)),
                                                "Text", string.Empty, 28, TextAnchor.MiddleCenter);
                _pointsValue[i].color = Ink;

                _pointsProgress[i] = UiBuild.Label(Slot(row, "Ilerleme", new Vector2(0.750f, 0f), new Vector2(1f, 1f)),
                                                   "Text", string.Empty, 22, TextAnchor.MiddleRight);
                _pointsProgress[i].color = InkSoft;
                Fit(_pointsProgress[i], 14, 22);
            }

            _pointsTransition = UiBuild.Label(Slot(card, "Gecis", new Vector2(0.10f, 0.330f), new Vector2(0.90f, 0.630f)),
                                              "Text", Loc.T("lig.puan_gecis"), 26, TextAnchor.MiddleCenter);
            _pointsTransition.color = Ink;
            Fit(_pointsTransition, 16, 26);

            _pointsLimit = UiBuild.Label(Slot(card, "Sinir", new Vector2(0.10f, 0.190f), new Vector2(0.90f, 0.275f)),
                                         "Text", Loc.T("lig.puan_sinir"), 22, TextAnchor.MiddleCenter);
            _pointsLimit.color = InkSoft;
            Fit(_pointsLimit, 14, 22);

            LigKit.Capsule(card, "Kapat", Loc.T("lig.kapat"),
                           new Vector2(0.230f, 0.045f), new Vector2(0.770f, 0.156f),
                           HidePoints, out _pointsCloseLabel);

            _pointsCard.gameObject.SetActive(false);
        }

        /// <summary>
        /// The profile card: a preview of the chosen avatar beside the name field, the fifteen avatars
        /// to pick from, and save / cancel.
        ///
        /// Same frame and width as the other two cards, for the crown reason written there; it is the
        /// tallest of the three. The avatar grid is sized to the box it is given each time the card
        /// opens (<see cref="FitAvatarGrid"/>): square cells, the largest five-by-three that fits, so the
        /// avatars neither run into the card's rails on a narrow phone nor float in empty space on a
        /// tall one.
        /// </summary>
        private void BuildProfileCard()
        {
            _profileCard = UiBuild.Flat(_root, "ProfilKarti", new Color(0.04f, 0.05f, 0.08f, 0.86f),
                                        Vector2.zero, Vector2.one);
            var dismiss = _profileCard.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(CancelProfile);

            Image frame = CardFrame(_profileCard, new Vector2(0.190f, 0.225f), new Vector2(0.810f, 0.775f));
            RectTransform card = frame.rectTransform;
            frame.raycastTarget = true;
            var eat = card.gameObject.AddComponent<Button>();
            eat.transition = Selectable.Transition.None;

            // Below the crown's inner edge on the shortest card (16:9), where the crown is the
            // largest share of the card's height.
            _profileTitle = UiBuild.Label(Slot(card, "Baslik", new Vector2(0.08f, 0.756f), new Vector2(0.92f, 0.820f)),
                                          "Text", Loc.T("lig.profil_baslik"), 34, TextAnchor.MiddleCenter);
            _profileTitle.color = Ink;
            Fit(_profileTitle, 20, 34);

            // The preview: square by construction, pinned to the card's left rail, and centred on the
            // same line as the name field beside it.
            _profilePreview = AvatarArt.Badge(card, "Onizleme", new Vector2(0.09f, 0.622f), new Vector2(0.09f, 0.748f),
                                              out Image previewFrame);
            RectTransform preview = previewFrame.rectTransform;
            preview.pivot = new Vector2(0f, 0.5f);
            var square = previewFrame.gameObject.AddComponent<AspectRatioFitter>();
            square.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            square.aspectRatio = 1f;

            _nameInput = NameField(Slot(card, "Ad", new Vector2(0.40f, 0.668f), new Vector2(0.91f, 0.746f)));

            _profileHint = UiBuild.Label(Slot(card, "Kural", new Vector2(0.40f, 0.624f), new Vector2(0.91f, 0.664f)),
                                         "Text", string.Empty, 20, TextAnchor.MiddleLeft);
            _profileHint.color = InkSoft;
            Fit(_profileHint, 12, 20);

            // The label and the grid are one group: FitAvatarGrid sits the label directly on top of the
            // cells and centres the pair in this box, so no phone shape leaves a gap between them.
            _profilePickLabel = UiBuild.Label(Slot(card, "Secim", new Vector2(0.08f, 0.556f), new Vector2(0.92f, 0.600f)),
                                              "Text", Loc.T("lig.profil_avatar"), 24, TextAnchor.MiddleCenter);
            _profilePickLabel.color = Ink;
            Fit(_profilePickLabel, 14, 24);

            RectTransform grid = Slot(card, "Avatarlar", new Vector2(0.07f, 0.190f), new Vector2(0.93f, 0.600f));
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(88f, 88f);
            layout.spacing = new Vector2(AvatarGap, AvatarGap);
            layout.padding = new RectOffset(0, 0, (int)(PickLabelHeight + AvatarGap), 0);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 5;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.startAxis = GridLayoutGroup.Axis.Horizontal;

            for (int k = 0; k < PlayerProfiles.AvatarCount; k++)
            {
                _avatarHead[k] = AvatarArt.Badge(grid, "Avatar" + k, Vector2.zero, Vector2.one, out _avatarFrame[k]);
                _avatarFrame[k].raycastTarget = true;
                var pick = _avatarFrame[k].gameObject.AddComponent<Button>();
                pick.transition = Selectable.Transition.None;
                pick.targetGraphic = _avatarFrame[k];
                int captured = k;
                pick.onClick.AddListener(() => PickAvatar(captured));
                AvatarArt.Show(_avatarHead[k], k);
            }

            LigKit.Capsule(card, "Iptal", Loc.T("lig.profil_iptal"),
                           new Vector2(0.080f, 0.055f), new Vector2(0.480f, 0.155f),
                           CancelProfile, out _profileCancelLabel);
            Fit(_profileCancelLabel, 14, 28);
            LigKit.Capsule(card, "Kaydet", Loc.T("lig.profil_kaydet"),
                           new Vector2(0.520f, 0.055f), new Vector2(0.920f, 0.155f),
                           SaveProfile, out _profileSaveLabel);
            Fit(_profileSaveLabel, 14, 28);

            _profileCard.gameObject.SetActive(false);
        }

        /// <summary>
        /// The name box: the blue kit plate with the typed name in paper ink, like the rows it will be
        /// shown on. Characters the name rules refuse never reach the text, and the length is capped
        /// at the same number <see cref="PlayerProfiles.Sanitize"/> cuts to.
        /// </summary>
        private InputField NameField(RectTransform slot)
        {
            var go = new GameObject("Alan", typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(slot, false);
            UiBuild.Anchor((RectTransform)go.transform, Vector2.zero, Vector2.one);

            var plate = go.GetComponent<Image>();
            Sprite blue = UiSkin.ButtonBlue;
            plate.sprite = blue != null ? blue : UiSkin.Flat;
            plate.type = Image.Type.Sliced;
            plate.raycastTarget = true;

            RectTransform inner = Slot((RectTransform)go.transform, "Ic", Vector2.zero, Vector2.one);
            inner.offsetMin = new Vector2(20f, 6f);
            inner.offsetMax = new Vector2(-20f, -6f);

            Text text = UiBuild.Label(inner, "Text", string.Empty, 30, TextAnchor.MiddleLeft);
            text.color = Paper;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;

            _namePlaceholder = UiBuild.Label(inner, "Placeholder", Loc.T("lig.profil_ad"), 26, TextAnchor.MiddleLeft);
            _namePlaceholder.color = new Color(Paper.r, Paper.g, Paper.b, 0.6f);
            _namePlaceholder.fontStyle = FontStyle.Italic;
            Fit(_namePlaceholder, 14, 26);

            var input = go.GetComponent<InputField>();
            input.transition = Selectable.Transition.None;
            input.targetGraphic = plate;
            input.textComponent = text;
            input.placeholder = _namePlaceholder;
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit = PlayerProfiles.MaxNameLength;
            input.onValidateInput = (typed, index, c) => PlayerProfiles.IsAllowedChar(c) ? c : '\0';
            input.onValueChanged.AddListener(_ => RefreshProfileHint(false));
            return input;
        }

        /// <summary>One "icon, then how many" line in the reward card. Two of them, stacked.</summary>
        private static RectTransform RewardLine(RectTransform card, string name, string art, float bottom,
                                                out Text amount)
        {
            RectTransform row = Slot(card, name, new Vector2(0.140f, bottom), new Vector2(0.860f, bottom + 0.100f));
            LigKit.Icon(row, "Ikon", art, new Vector2(0.230f, 0.02f), new Vector2(0.430f, 0.98f));
            amount = UiBuild.Label(Slot(row, "Sayi", new Vector2(0.470f, 0f), new Vector2(0.860f, 1f)),
                                   "Text", string.Empty, 30, TextAnchor.MiddleLeft);
            amount.color = Ink;
            return row;
        }

        private void BuildOpener()
        {
            HudUI hud = FindAnyObjectByType<HudUI>(FindObjectsInactive.Include);
            if (hud == null) return;

            // No ladder in this build means no entry point at all — not an empty board
            // (Docs/LEADERBOARDS.md §13).
            if (_ladder == null || !_ladder.Available) return;

            Sprite icon = LigKit.Get("lig_ikon");
            Button open = hud.AttachBottomButton(6, "BtnLig",
                                                 icon != null ? icon : UiSkin.ButtonBlue, Show);
            if (open == null) return;

            _openerChip = hud.AttachCounterChip(open);
            if (_openerChip != null) _openerCount = _openerChip.GetComponentInChildren<TMP_Text>(true);
        }

        // ---------------------------------------------------------------- refresh
        private void Refresh()
        {
            if (_root == null || !_root.gameObject.activeSelf) return;
            if (_ladder == null || !_ladder.Available) { Blank(); return; }

            RefreshClaimStrip();

            _ladder.RequestBoard(board =>
            {
                if (board == null || board.Status != LeaderboardStatus.Ok || board.Entries.Length == 0)
                {
                    Blank();
                    return;
                }

                if (_clockLabel != null)
                    _clockLabel.text = Loc.T("etkinlik.kalan") + " " + HudUI.LongClock(board.SecondsLeft);

                if (_syntheticLabel != null && _syntheticLabel.gameObject.activeSelf != board.Synthetic)
                    _syntheticLabel.gameObject.SetActive(board.Synthetic);

                int shown = board.Entries.Length < VisibleRows ? board.Entries.Length : VisibleRows;
                for (int i = 0; i < VisibleRows; i++)
                {
                    bool on = i < shown;
                    if (_slotRoot[i] != null && _slotRoot[i].gameObject.activeSelf != on)
                        _slotRoot[i].gameObject.SetActive(on);
                    if (on) Seat(i, board.Entries[i], board.Synthetic);
                    else { _slotRankValue[i] = 0; _slotIsPlayer[i] = false; }
                }

                _playerRank = board.PlayerRank;

                // The player's bar, ONLY WHEN NO REWARD IS OWED. It and the claim strip share the band
                // under the board, so only one may ever be up — RefreshClaimStrip settles that before
                // the board is even asked for, and this callback, which arrives afterwards, must not
                // undo it. It did once: both were drawn, and the green row showed through the strip's
                // transparent corners as if the art were broken.
                bool owed = _claimStrip != null && _claimStrip.gameObject.activeSelf;
                if (!owed) SeatPinned(board);
                else if (_pinnedRoot != null && _pinnedRoot.gameObject.activeSelf)
                    _pinnedRoot.gameObject.SetActive(false);

                if (_emptyLabel != null && _emptyLabel.gameObject.activeSelf)
                    _emptyLabel.gameObject.SetActive(false);
            });
        }

        private static readonly Color Rim = new Color(0.03f, 0.10f, 0.25f, 0.9f);

        /// <summary>A navy outline for text that sits on the game's bright blue and green plates.</summary>
        private static Text Rimmed(Text label)
        {
            var rim = label.gameObject.AddComponent<Outline>();
            rim.effectColor = Rim;
            rim.effectDistance = new Vector2(2f, -2f);
            return label;
        }

        /// <summary>The player's name as the board prints it: the saved one when there is a valid one,
        /// otherwise the localized "YOU".</summary>
        private static string PlayerLabel(string saved)
        {
            string clean = PlayerProfiles.Sanitize(saved);
            return PlayerProfiles.IsValidName(clean) ? clean : Loc.T("lig.siram");
        }

        /// <summary>The line under a name: what a rival is, or who the player is.</summary>
        private static string TagFor(in LeaderboardEntry entry, bool synthetic)
        {
            if (!entry.IsPlayer) return synthetic ? Loc.T("lig.sim_rakip") : string.Empty;
            string clean = PlayerProfiles.Sanitize(entry.Name);
            return PlayerProfiles.IsValidName(clean) ? Loc.T("lig.siram") : Loc.T("lig.profil_ipucu");
        }

        private void Seat(int i, in LeaderboardEntry entry, bool synthetic)
        {
            _slotRankValue[i] = entry.Rank;
            _slotIsPlayer[i] = entry.IsPlayer;
            if (_slotRank[i] != null) _slotRank[i].text = entry.Rank.ToString();
            // One line each, shrinking rather than wrapping — see EtkinlikKit.OneLine.
            if (_slotName[i] != null)
                _slotName[i].text = EtkinlikKit.OneLine(entry.IsPlayer ? PlayerLabel(entry.Name) : entry.Name);
            if (_slotTag[i] != null) _slotTag[i].text = EtkinlikKit.OneLine(TagFor(entry, synthetic));
            if (_slotScore[i] != null) _slotScore[i].text = NumberFormatter.Format((double)entry.Score, 0);
            AvatarArt.Show(_slotAvatar[i], entry.Avatar);

            // The podium's place is the medal, and the medal is picked by the RANK the card is showing
            // rather than by which of the three cards it is: a board with fewer than three entrants
            // still seats them 1, 2, 3 from the left-hand card outward.
            if (i < PodiumSlots && _slotMedal[i] != null)
            {
                Sprite medal = MedalFor(entry.Rank);
                if (medal != null && _slotMedal[i].sprite != medal) _slotMedal[i].sprite = medal;
                if (_slotMedal[i].enabled != (medal != null)) _slotMedal[i].enabled = medal != null;
            }

            // Pre-coloured kit art: state is the SPRITE, never a tint over a neutral plate. The player's
            // row is the kit's starred green one; everyone else's is the game's own panel. A podium card
            // is too tall for the starred plate's capsule shape, so the player's card takes the kit's
            // green square panel instead — the same green, sliced like the blue one it replaces.
            Sprite plate = RowPlate;
            if (entry.IsPlayer)
            {
                Sprite playerPlate = i >= PodiumSlots ? LigKit.Get("satir_sen") : null;
                plate = playerPlate != null ? playerPlate : UiSkin.ButtonGreen;
            }
            if (_slotBody[i] != null && plate != null && _slotBody[i].sprite != plate)
            {
                _slotBody[i].sprite = plate;
                // The two plates are different shapes, so the slicer has to be told which one it is
                // holding — and PillFit, if it is on, has to re-measure against the new art.
                _slotBody[i].type = plate.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                var fit = _slotBody[i].GetComponent<PillFit>();
                if (fit != null) fit.Fit();
            }

            // Name and score are paper ink with a navy rim on both plates: dark navy on the saturated
            // blue panel read as a smear. The RANK is dark only on the player's row, where it sits on
            // the gold star and white would vanish.
            if (_slotRank[i] != null)
            {
                bool onStar = entry.IsPlayer && i >= PodiumSlots;
                _slotRank[i].color = onStar ? Ink : Paper;
                _slotRank[i].GetComponent<Outline>().enabled = !onStar;   // a rim smears the dark digits
            }
            if (_slotName[i] != null) _slotName[i].color = Paper;
            if (_slotScore[i] != null) _slotScore[i].color = Paper;

            // Only a paying rank carries a chest. Below the divider the slot is simply empty.
            if (_slotChest[i] != null)
            {
                bool pays = entry.Rank <= Leaderboards.RewardedRanks;
                if (pays) _slotChest[i].sprite = ChestFor(entry.Rank);
                if (_slotChest[i].enabled != pays) _slotChest[i].enabled = pays;
            }
        }

        private void SeatPinned(LeaderboardBoard board)
        {
            if (_pinnedRoot == null) return;
            if (!_pinnedRoot.gameObject.activeSelf) _pinnedRoot.gameObject.SetActive(true);

            bool ranked = board.PlayerRank > 0 && board.PlayerRank <= board.Entries.Length;
            LeaderboardEntry me = ranked ? board.Entries[board.PlayerRank - 1] : default;

            if (_pinnedRank != null) _pinnedRank.text = ranked ? board.PlayerRank.ToString() : "—";
            if (_pinnedName != null)
                _pinnedName.text = EtkinlikKit.OneLine(ranked ? PlayerLabel(me.Name) : Loc.T("lig.listede_degil"));
            if (_pinnedTag != null) _pinnedTag.text = ranked ? EtkinlikKit.OneLine(TagFor(me, board.Synthetic)) : string.Empty;
            if (_pinnedScore != null)
                _pinnedScore.text = NumberFormatter.Format((double)board.PlayerScore, 0);
            AvatarArt.Show(_pinnedAvatar, ranked ? me.Avatar : (_profiles != null ? _profiles.Avatar : 0));

            if (_pinnedRank != null)
            {
                _pinnedRank.color = Ink;   // it sits on the gold star; see Seat
                _pinnedRank.GetComponent<Outline>().enabled = false;
            }
            if (_pinnedName != null) _pinnedName.color = Paper;
            if (_pinnedScore != null) _pinnedScore.color = Paper;
        }

        /// <summary>
        /// Brings the player's row into the middle of the list when the screen opens. Only then — a
        /// refresh while the player is scrolling must not yank the list back.
        /// </summary>
        private void ScrollToPlayer()
        {
            if (_scroll == null || _listContent == null) return;

            Canvas.ForceUpdateCanvases();
            float view = _scroll.viewport.rect.height;
            float max = _listContent.rect.height - view;
            if (max < 0f) max = 0f;

            float y = 0f;
            if (_playerRank > PodiumSlots)
                y = RowTop(_playerRank - PodiumSlots - 1) - (view - RowHeight) * 0.5f;

            _scroll.StopMovement();
            _listContent.anchoredPosition = new Vector2(0f, Mathf.Clamp(y, 0f, max));
        }

        /// <summary>The medal for a podium place, or null for a rank that does not get one — which is
        /// what blanks the image rather than leaving last season's medal on the card.</summary>
        private static Sprite MedalFor(int rank)
        {
            if (rank == 1) return LigKit.Get("madalya_altin");
            if (rank == 2) return LigKit.Get("madalya_gumus");
            if (rank == 3) return LigKit.Get("madalya_bronz");
            return null;
        }

        /// <summary>Gold, silver, bronze for the podium; the plain chest for every paying rank below.</summary>
        private Sprite ChestFor(int rank)
        {
            if (rank == 1) return _chest[0];
            if (rank == 2) return _chest[1];
            if (rank == 3) return _chest[2];
            return _chest[3];
        }

        /// <summary>The strip that says a closed season still owes something. It shares the bottom band
        /// with the pinned bar, so only one is ever up: a reward the player can take outranks a rank
        /// they can only read.</summary>
        private void RefreshClaimStrip()
        {
            int waiting = _ladder != null ? _ladder.UnclaimedCount : 0;
            bool owed = waiting > 0;

            if (_claimStrip != null && _claimStrip.gameObject.activeSelf != owed)
                _claimStrip.gameObject.SetActive(owed);
            if (owed && _pinnedRoot != null && _pinnedRoot.gameObject.activeSelf)
                _pinnedRoot.gameObject.SetActive(false);

            if (owed && _claimLabel != null)
                _claimLabel.text = Loc.T("lig.odul_bekliyor") + " ×" + waiting;
        }

        private void ClaimAll()
        {
            if (_ladder == null) return;
            if (_ladder.ClaimAll() > 0) { Refresh(); RefreshOpener(); }
        }

        // ----------------------------------------------------------------- reward
        /// <summary>
        /// Opens the chest on a position: which ranks share that bracket, and what it pays. Keyed off
        /// the RANK the slot is showing rather than the slot index, because the two only agree while
        /// the board is full.
        /// </summary>
        private void ShowReward(int slot)
        {
            if (_rewardCard == null) return;
            int rank = _slotRankValue[slot];
            if (rank <= 0) return;

            int tier = Leaderboards.RewardTier(rank, Leaderboards.DefaultBracketEnds);
            Ladder.Reward reward = _ladder != null ? _ladder.RewardFor(tier) : default;

            if (_rewardTitle != null) _rewardTitle.text = BracketLabel(tier);
            if (_rewardChest != null) _rewardChest.sprite = ChestFor(rank);

            bool gems = reward.Gems > 0L;
            bool cards = reward.Cards > 0;

            if (_rewardGemRow != null && _rewardGemRow.gameObject.activeSelf != gems)
                _rewardGemRow.gameObject.SetActive(gems);
            if (gems && _rewardGemLabel != null)
                _rewardGemLabel.text = NumberFormatter.Format((double)reward.Gems, 0);

            if (_rewardCardRow != null && _rewardCardRow.gameObject.activeSelf != cards)
                _rewardCardRow.gameObject.SetActive(cards);
            if (cards && _rewardCardLabel != null)
                _rewardCardLabel.text = reward.Cards + " " + Loc.T("lig.kart");

            // A bracket that pays nothing has to SAY so; two empty rows read as a screen that failed
            // to load rather than as a place worth nothing.
            if (_rewardNone != null && _rewardNone.gameObject.activeSelf == (gems || cards))
                _rewardNone.gameObject.SetActive(!gems && !cards);

            _rewardCard.gameObject.SetActive(true);
        }

        // ----------------------------------------------------------------- points
        private void ShowPoints()
        {
            if (_pointsCard == null) return;
            HideReward();
            _pointsCard.gameObject.SetActive(true);
            RefreshPoints();
        }

        /// <summary>The automatic first open of the points card, in a points season only.</summary>
        private void AutoOpenPoints()
        {
            if (_root == null || !_root.gameObject.activeSelf) return;
            if (_ladder != null && _ladder.Available && !_ladder.PointsHelpSeen && _ladder.ScoresPoints)
                ShowPoints();
        }

        /// <summary>Closing the card is what counts as having read it, so the automatic open stops.</summary>
        private void HidePoints()
        {
            if (_pointsCard == null || !_pointsCard.gameObject.activeSelf) return;
            _pointsCard.gameObject.SetActive(false);
            if (_ladder != null) _ladder.MarkPointsHelpSeen();
        }

        /// <summary>
        /// Relabels the card. A season an older build opened still ranks bars, and listing the points
        /// rules there would explain a score the board is not showing — so it says that instead.
        /// </summary>
        private void RefreshPoints()
        {
            if (_pointsCard == null || !_pointsCard.gameObject.activeSelf) return;

            bool live = _ladder != null && _ladder.Available;
            bool points = !live || _ladder.ScoresPoints;

            if (_pointsTitle != null) _pointsTitle.text = Loc.T("lig.puan_baslik");
            // One line each, shrinking rather than wrapping: a sentence a few units too wide otherwise
            // breaks with a single word left on the second line.
            if (_pointsIntro != null) _pointsIntro.text = EtkinlikKit.OneLine(Loc.T("lig.puan_aciklama"));
            if (_pointsLimit != null) _pointsLimit.text = EtkinlikKit.OneLine(Loc.T("lig.puan_sinir"));
            if (_pointsTransition != null) _pointsTransition.text = Loc.T("lig.puan_gecis");

            SetActive(_pointsIntro, points);
            SetActive(_pointsLimit, points);
            SetActive(_pointsTransition, !points);

            Ladder.ScoringRule[] rules = Ladder.Scoring;
            for (int i = 0; i < _pointsRow.Length; i++)
            {
                bool on = points && i < rules.Length;
                SetActive(_pointsRow[i], on);
                if (!on) continue;

                Ladder.ScoringRule rule = rules[i];
                if (_pointsName[i] != null) _pointsName[i].text = EtkinlikKit.OneLine(MetricLabel(rule.Metric));
                if (_pointsValue[i] != null) _pointsValue[i].text = "+" + rule.PointsPerAction;
                if (_pointsProgress[i] != null)
                {
                    SetActive(_pointsProgress[i], live);
                    if (live) _pointsProgress[i].text = _ladder.CountedActions(rule) + " / " + rule.SeasonCap;
                }
            }
        }

        private static string MetricLabel(int metric)
        {
            switch (metric)
            {
                case Goals.Upgrades:      return Loc.T("gorev.metrik.yukseltme");
                case Goals.Contracts:     return Loc.T("gorev.metrik.kontrat");
                case Goals.Repairs:       return Loc.T("gorev.metrik.onarim");
                case Goals.ForemanLevels: return Loc.T("gorev.metrik.ustabasi");
                default:                  return string.Empty;
            }
        }

        // ---------------------------------------------------------------- profile
        /// <summary>Opens the profile card on the saved name and avatar.</summary>
        public void ShowProfile()
        {
            if (_profileCard == null || _profiles == null) return;
            HideReward();
            SetActive(_pointsCard, false);

            _pickedAvatar = _profiles.Avatar;
            if (_nameInput != null) _nameInput.text = _profiles.Name;
            RefreshAvatarPicks();
            RefreshProfileHint(false);
            _profileCard.gameObject.SetActive(true);
            FitAvatarGrid();
        }

        private const float AvatarGap = 12f, AvatarCellMax = 104f, PickLabelHeight = 44f;
        private const int AvatarColumns = 5;

        /// <summary>
        /// The largest square cell that fits five across and three down in the grid's box (under the
        /// label's share of it), capped so a tablet does not get poster-sized faces. Then the label is
        /// pinned to the top of the cells, so the pair is centred in the box as one group. Runs when the
        /// card opens, which is after the canvas has its real size — the box is a share of the card,
        /// and the card of the screen.
        /// </summary>
        private void FitAvatarGrid()
        {
            if (_profileCard == null) return;
            Transform gridT = _profileCard.Find("Kart/Avatarlar");
            if (gridT == null) return;
            var grid = gridT.GetComponent<GridLayoutGroup>();
            var box = (RectTransform)gridT;

            Canvas.ForceUpdateCanvases();
            int rows = (PlayerProfiles.AvatarCount + AvatarColumns - 1) / AvatarColumns;
            float top = grid.padding.top;
            float byWidth = (box.rect.width - (AvatarColumns - 1) * AvatarGap) / AvatarColumns;
            float byHeight = (box.rect.height - top - (rows - 1) * AvatarGap) / rows;
            float cell = Mathf.Floor(Mathf.Min(AvatarCellMax, Mathf.Min(byWidth, byHeight)));
            if (cell <= 0f) return;
            if (!Mathf.Approximately(grid.cellSize.x, cell)) grid.cellSize = new Vector2(cell, cell);

            // The group is the label's band plus the cells, centred in the box by MiddleCenter.
            var label = _profilePickLabel != null ? (RectTransform)_profilePickLabel.transform.parent : null;
            if (label == null) return;
            float group = top + rows * cell + (rows - 1) * AvatarGap;
            float centre = (box.anchorMin.y + box.anchorMax.y) * 0.5f;
            label.anchorMin = new Vector2(label.anchorMin.x, centre);
            label.anchorMax = new Vector2(label.anchorMax.x, centre);
            label.offsetMin = new Vector2(0f, group * 0.5f - PickLabelHeight);
            label.offsetMax = new Vector2(0f, group * 0.5f);
        }

        private void PickAvatar(int avatar)
        {
            _pickedAvatar = PlayerProfiles.ClampAvatar(avatar);
            RefreshAvatarPicks();
        }

        /// <summary>The chosen avatar in the gold frame, every other in the blue — a sprite swap, as
        /// everywhere on this kit.</summary>
        private void RefreshAvatarPicks()
        {
            AvatarArt.Show(_profilePreview, _pickedAvatar);
            Sprite picked = UiSkin.ButtonYellow, other = UiSkin.ButtonBlue;
            for (int k = 0; k < _avatarFrame.Length; k++)
            {
                if (_avatarFrame[k] == null) continue;
                Sprite want = k == _pickedAvatar ? picked : other;
                if (want != null && _avatarFrame[k].sprite != want) _avatarFrame[k].sprite = want;
            }
        }

        /// <summary>The length rule under the field — red once the typed name breaks it, or when a
        /// save was refused.</summary>
        private void RefreshProfileHint(bool refused)
        {
            if (_profileHint == null) return;
            _profileHint.text = string.Format(Loc.T("lig.profil_kural"),
                                              PlayerProfiles.MinNameLength, PlayerProfiles.MaxNameLength);

            string typed = _nameInput != null ? _nameInput.text : string.Empty;
            bool bad = refused || (typed.Length > 0 && !PlayerProfiles.IsValidName(PlayerProfiles.Sanitize(typed)));
            _profileHint.color = bad ? Warn : InkSoft;
        }

        private void SaveProfile()
        {
            if (_profiles == null || _nameInput == null) return;
            if (!_profiles.TrySet(_nameInput.text, _pickedAvatar))
            {
                RefreshProfileHint(true);
                return;
            }

            SetActive(_profileCard, false);
            Refresh();
            AutoOpenPoints();
        }

        /// <summary>Closing without saving keeps the old profile, and still counts as having been shown,
        /// so the card does not open by itself again.</summary>
        private void CancelProfile()
        {
            if (_profileCard == null || !_profileCard.gameObject.activeSelf) return;
            SetActive(_profileCard, false);
            if (_profiles != null) _profiles.MarkPrompted();
            AutoOpenPoints();
        }

        private static void SetActive(Component c, bool on)
        {
            if (c != null && c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
        }

        /// <summary>Shrink-to-fit that actually shrinks: best fit does nothing while the text may
        /// overflow its box, so the box clips.</summary>
        private static void Fit(Text label, int min, int max)
        {
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = min;
            label.resizeTextMaxSize = max;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private void HideReward()
        {
            if (_rewardCard != null && _rewardCard.gameObject.activeSelf)
                _rewardCard.gameObject.SetActive(false);
        }

        /// <summary>"1", or "4-10" — the ranks that share one chest.</summary>
        private static string BracketLabel(int tier)
        {
            int[] ends = Leaderboards.DefaultBracketEnds;
            if (tier < 0 || tier >= ends.Length) return string.Empty;

            int first = tier == 0 ? 1 : ends[tier - 1] + 1;
            int last = ends[tier];
            return first == last ? first.ToString() : first + "-" + last;
        }

        /// <summary>What the screen shows when there is no board — an explained empty state rather than
        /// fifty blank plates, which reads as broken.</summary>
        private void Blank()
        {
            for (int i = 0; i < VisibleRows; i++)
            {
                // Cleared as well as hidden: the rank is what a chest pays out on, and a hidden slot
                // still holding last board's number could open a reward it no longer stands for.
                _slotRankValue[i] = 0;
                _slotIsPlayer[i] = false;
                if (_slotRoot[i] != null && _slotRoot[i].gameObject.activeSelf)
                    _slotRoot[i].gameObject.SetActive(false);
            }

            if (_pinnedRoot != null && _pinnedRoot.gameObject.activeSelf) _pinnedRoot.gameObject.SetActive(false);
            if (_clockLabel != null) _clockLabel.text = string.Empty;
            if (_emptyLabel != null && !_emptyLabel.gameObject.activeSelf) _emptyLabel.gameObject.SetActive(true);
        }

        private void RefreshOpener()
        {
            if (_openerChip == null || _ladder == null) return;

            int waiting = _ladder.UnclaimedCount;
            if (_openerChip.activeSelf != (waiting > 0)) _openerChip.SetActive(waiting > 0);
            if (waiting > 0 && _openerCount != null)
            {
                string text = waiting.ToString();
                if (_openerCount.text != text) _openerCount.text = text;
            }
        }

        // ------------------------------------------------------------------ pieces
        /// <summary>
        /// A 9-sliced panel, or an aspect-locked image when the art cannot be sliced.
        ///
        /// THE SLICE IS DECIDED FROM THE SPRITE ACTUALLY USED, not from the argument. The version of
        /// this helper the other screens carry asks <c>sprite != null &amp;&amp; sprite.border...</c>
        /// and then assigns <c>sprite ?? UiSkin.Panel</c> — so a null argument falls back to the kit
        /// panel (which is 9-sliceable) while still being typed Simple with preserveAspect on. Those
        /// screens never see it because their sprite slots are wired in the Inspector; this one is
        /// created at runtime, where every slot is null by definition, and the whole board rendered as
        /// aspect-locked squares that would not fill their rows.
        /// </summary>
        private static RectTransform Art(RectTransform parent, string name, Sprite sprite,
                                         Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            Sprite art = sprite != null ? sprite : UiSkin.Panel;
            img.sprite = art;
            img.type = art != null && art.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            img.preserveAspect = img.type == Image.Type.Simple;
            img.color = Color.white;
            img.raycastTarget = false;
            return UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
        }

        /// <summary>The kit's crowned popup frame, with the crown held at its drawn shape.</summary>
        private static Image CardFrame(RectTransform parent, Vector2 aMin, Vector2 aMax)
        {
            Image frame = LigKit.Sliced(parent, "Kart", "odul_pano", aMin, aMax, false);
            if (LigKit.Get("odul_pano") != null) FrameFit.Wrap(frame);
            return frame;
        }

        private static RectTransform Slot(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
        }

        /// <summary>A fixed-size box, vertically centred, <paramref name="x"/> units in from the left.</summary>
        private static RectTransform PinLeft(RectTransform rt, float x, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta = new Vector2(width, height);
            return rt;
        }

        /// <summary>A fixed-size box, vertically centred, <paramref name="right"/> units in from the right.</summary>
        private static RectTransform PinRight(RectTransform rt, float right, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-right, 0f);
            rt.sizeDelta = new Vector2(width, height);
            return rt;
        }
    }
}
