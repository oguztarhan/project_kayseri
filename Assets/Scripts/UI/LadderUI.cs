using Game.Core;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The three-day league, laid out the way the reference screens lay a ladder out: the top three on
    /// a podium with first in the middle, the rest as rows beneath, and a reward chest on every single
    /// position that opens to say what it holds.
    ///
    /// Built in code like <see cref="LiveEventsUI"/> and <see cref="ChapterUI"/>, and made by
    /// <see cref="HudUI"/> when the scene has none — so it needs no prefab wiring to appear. The opener
    /// is order 6 in the bottom row, after the events board.
    ///
    /// IT SAYS THE OPPONENTS ARE NOT PEOPLE, always. The board carries
    /// <see cref="LeaderboardBoard.Synthetic"/> precisely so a screen cannot forget to, and
    /// Docs/LEADERBOARDS.md decision D4 makes that label the condition on which a generated cohort was
    /// allowed to pay rewards at all. The line is written from the flag, so a real backend behind the
    /// same seam retires it without this file changing.
    ///
    /// THE CHEST IS THE REWARD TABLE, not decoration. Every rank shown carries the chest of the bracket
    /// it falls in — gold, silver, bronze, then a plain one for the tail — and tapping it opens the
    /// payout. That is the only place in the game a player can find out what finishing 7th is worth
    /// before the season ends, which is the whole reason the reference puts a chest on every row.
    ///
    /// NINE POSITIONS, NOT THIRTY: three on the podium and six full-width rows under it, plus the
    /// player pinned below the board when they sit outside them. No scroll view, no pooling, a fixed
    /// allocation.
    ///
    /// ONE COLUMN, because the game is portrait. This screen was first built in two, on the reasoning
    /// — written into the comment that used to stand here — that <see cref="ChapterUI"/> had found
    /// full-width rows stacked down a LANDSCAPE screen came out as a letterbox with bars in it. The
    /// premise was simply wrong: ProjectSettings allows portrait only, and <see cref="UiBuild.Canvas"/>
    /// lays every screen out against 1080×1920. Two columns of a 1080-unit canvas gave each row about
    /// 430 units to hold a rank, a name, a score and a chest, which is why they were unreadable.
    ///
    /// THE ART IS THE DESIGN KIT, loaded through <see cref="LigKit"/>: the board frame with its star
    /// bow, the title ribbon, a medal instead of a rank number on each podium card, a chest on every
    /// position, the green starred row for the player, and the winged strip for a reward still owed.
    /// The kit is pre-coloured, so state is picked by swapping the SPRITE, never by tinting.
    ///
    /// The once-a-second Update only drives the countdown, and only while the board is open.
    /// </summary>
    public sealed class LadderUI : MonoBehaviour
    {
        /// <summary>Above the events board's 109.</summary>
        [SerializeField] private int sortingOrder = 111;

        [Header("Renkler")]
        [SerializeField] private Color scrim = new Color(0.04f, 0.05f, 0.08f, 0.92f);

        /// <summary>The podium — first, second, third — and then the rows under it.</summary>
        private const int PodiumSlots = 3;
        private const int RowSlots = 6;
        private const int VisibleRows = PodiumSlots + RowSlots;

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

        /// <summary>
        /// The band under the board, shared by the player's pinned row and the claim strip — only one
        /// of the two is ever up. Below the frame rather than inside it so the claim strip's winged
        /// star can rise over the board's bottom edge.
        /// </summary>
        private static readonly Vector2 PinnedMin = new Vector2(0.070f, 0.030f);
        private static readonly Vector2 PinnedMax = new Vector2(0.930f, 0.112f);

        /// <summary>
        /// Where a rank sits along its row: over the gold star the player's row art carries in its left
        /// cap. <see cref="PillFit"/> scales that cap with the row's height, and at the heights used
        /// here it lands a little under a fifth of the way across — so the number is centred on it.
        /// </summary>
        private static readonly Vector2 RankMin = new Vector2(0.042f, 0f);
        private static readonly Vector2 RankMax = new Vector2(0.132f, 1f);

        /// <summary>
        /// The plate under a rank that is not the player's — a podium card or an ordinary row.
        ///
        /// BLUE, NOT THE KIT PANEL. <c>UiSkin.Panel</c> is wired to <c>Button_Square_Disable</c>, and
        /// on a board whose field is near-white that grey reads exactly as what it is: the disabled
        /// state. The blue plate belongs to the same blue-and-gold language as the frame, and it is
        /// what makes the gold and silver of a medal carry.
        /// </summary>
        private static Sprite RowPlate => UiSkin.ButtonBlue;

        private static readonly Color Ink = new Color(0.09f, 0.14f, 0.24f, 1f);
        private static readonly Color InkSoft = new Color(0.36f, 0.42f, 0.52f, 1f);
        private static readonly Color Paper = new Color(0.96f, 0.97f, 1f, 1f);

        private LadderService _ladder;
        private LocalizationService _loc;
        private RectTransform _root;

        private Text _titleLabel, _clockLabel, _syntheticLabel, _emptyLabel, _claimLabel, _takeLabel;
        private Text _rewardCloseLabel;
        private RectTransform _claimStrip;
        private TMP_Text _openerCount;
        private GameObject _openerChip;

        // One parallel set for every position on screen: 0-2 are the podium, 3-8 the rows.
        private readonly RectTransform[] _slotRoot = new RectTransform[VisibleRows];
        private readonly Image[] _slotBody = new Image[VisibleRows];
        private readonly Text[] _slotRank = new Text[VisibleRows];
        private readonly Text[] _slotName = new Text[VisibleRows];
        private readonly Text[] _slotScore = new Text[VisibleRows];
        private readonly Image[] _slotChest = new Image[VisibleRows];
        /// <summary>Podium only (0-2): the medal carries the place, so those cards have no rank text.</summary>
        private readonly Image[] _slotMedal = new Image[PodiumSlots];
        /// <summary>
        /// The rank each position is currently showing, which is what a chest pays out on.
        ///
        /// Held as a number because the podium no longer has a rank LABEL to read it back out of — the
        /// medal says the place. Parsing it out of the text was always the fragile way round.
        /// </summary>
        private readonly int[] _slotRankValue = new int[VisibleRows];

        private RectTransform _pinnedRoot;
        private Text _pinnedRank, _pinnedName, _pinnedScore;

        private RectTransform _rewardCard;
        private Image _rewardChest;
        private Text _rewardTitle, _rewardNone;
        private RectTransform _rewardGemRow, _rewardCardRow;
        private Text _rewardGemLabel, _rewardCardLabel;

        private readonly Sprite[] _chest = new Sprite[4];
        private float _tick;

        private void Awake()
        {
            _ladder = ServiceLocator.Get<LadderService>();
            Detach();
            LoadChests();
            Build();
            BuildOpener();
            if (_ladder != null) _ladder.Changed += OnChanged;
            _loc = ServiceLocator.Get<LocalizationService>();
            if (_loc != null) _loc.Changed += OnLanguageChanged;
            Hide();
            RefreshOpener();
        }

        private void OnDestroy()
        {
            if (_ladder != null) _ladder.Changed -= OnChanged;
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
            Refresh();
            RefreshOpener();
        }

        public void Show()
        {
            if (_root == null) return;
            _root.gameObject.SetActive(true);
            HideReward();
            _tick = 0f;
            ServiceLocator.Get<IAnalytics>()?.Log("ladder_open", "season",
                _ladder != null ? _ladder.CurrentSeasonId : string.Empty);
            Refresh();
        }

        public void Hide() { if (_root != null) _root.gameObject.SetActive(false); }

        /// <summary>
        /// The countdown, and only the countdown. A board is an allocation per request — thirty entries
        /// and the object holding them — so <see cref="ILeaderboardService.RequestBoard"/> is asked on
        /// open and on <c>Changed</c>, never once a second.
        /// </summary>
        private void Update()
        {
            if (_root == null || !_root.gameObject.activeSelf) return;
            _tick += Time.unscaledDeltaTime;
            if (_tick < 1f) return;
            _tick = 0f;
            RefreshClock();
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

        /// <summary>Gold, silver, bronze, and the plain chest every rank below third shares. From the
        /// design kit; a missing piece leaves the slot null and the position simply shows no chest
        /// rather than a broken one.</summary>
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
            BuildRows();
            BuildPinnedRow();
            BuildClaimStrip();

            _emptyLabel = UiBuild.Label(Slot(_root, "Bos", new Vector2(0.12f, 0.430f), new Vector2(0.88f, 0.530f)),
                                        "Text", Loc.T("lig.yok"), 32, TextAnchor.MiddleCenter);
            _emptyLabel.color = InkSoft;

            BuildRewardCard();
            // Content into the safe area; the scrim above it keeps covering the notch.
            UiBuild.InsetContent(_root);
        }

        /// <summary>
        /// The board: the kit's frame, bow and all, nine-sliced.
        ///
        /// SPANNING 0.03–0.97 IS NOT A MARGIN CHOICE. The frame's star bow rides in the top-centre
        /// segment of a nine-slice, which is the one part that stretches horizontally, and it stretches
        /// by the drawn width over the art width. The art is 974 px wide, so a box of about 1015 units
        /// draws the bow at 1.05× — near enough to unstretched that nothing reads as squashed. Pull
        /// these anchors in and the bow narrows with them; push them out and it smears.
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

            _clockLabel = UiBuild.Label(Slot(_root, "Sure", new Vector2(0.130f, 0.744f), new Vector2(0.870f, 0.792f)),
                                        "Text", string.Empty, 30, TextAnchor.MiddleCenter);
            _clockLabel.color = Ink;

            _syntheticLabel = UiBuild.Label(Slot(_root, "Temsili", new Vector2(0.130f, 0.710f), new Vector2(0.870f, 0.744f)),
                                            "Text", Loc.T("lig.temsili"), 22, TextAnchor.MiddleCenter);
            _syntheticLabel.color = InkSoft;
        }

        /// <summary>
        /// The three cards at the top. First sits in the MIDDLE and stands taller than the other two —
        /// the arrangement is what says "podium" before a single number is read, and putting first on
        /// the left would just be a list with bigger rows.
        /// </summary>
        private void BuildPodium()
        {
            BuildSlot(0, new Vector2(0.370f, 0.487f), new Vector2(0.630f, 0.700f), true);   // 1st, centre
            BuildSlot(1, new Vector2(ContentLeft, 0.487f), new Vector2(0.358f, 0.648f), true);   // 2nd, left
            BuildSlot(2, new Vector2(0.642f, 0.487f), new Vector2(ContentRight, 0.632f), true);  // 3rd, right
        }

        /// <summary>Ranks four to nine, stacked full width down the board.</summary>
        private void BuildRows()
        {
            const float top = 0.470f, bottom = 0.195f, gap = 0.004f;
            float ch = (top - bottom) / RowSlots;

            for (int i = 0; i < RowSlots; i++)
                BuildSlot(PodiumSlots + i,
                          new Vector2(ContentLeft, top - (i + 1) * ch + gap),
                          new Vector2(ContentRight, top - i * ch - gap), false);
        }

        /// <summary>
        /// One position, podium card or row. The two differ only in how the four pieces are placed:
        /// a card stacks them, a row lays them along its length.
        /// </summary>
        private void BuildSlot(int i, Vector2 aMin, Vector2 aMax, bool podium)
        {
            RectTransform body = Art(_root, (podium ? "Podyum" : "Satir") + i, RowPlate, aMin, aMax);
            _slotRoot[i] = body;
            _slotBody[i] = body.GetComponent<Image>();

            if (podium)
            {
                // The medal carries the place — it is drawn with the 1, 2 or 3 on its face — so there is
                // no rank label here to duplicate it. It overhangs the card's top edge on purpose: a
                // podium medal pinned to the card reads as a sticker, hung over it as a medal.
                _slotMedal[i] = LigKit.Icon(body, "Madalya", "madalya_altin",
                                            new Vector2(0.26f, 0.620f), new Vector2(0.74f, 1.030f));
                _slotName[i] = UiBuild.Label(Slot(body, "Ad", new Vector2(0.04f, 0.480f), new Vector2(0.96f, 0.600f)),
                                             "Text", string.Empty, 24, TextAnchor.MiddleCenter);
                _slotChest[i] = Chest(body, new Vector2(0.215f, 0.150f), new Vector2(0.785f, 0.460f), i);
                _slotScore[i] = UiBuild.Label(Slot(body, "Puan", new Vector2(0.04f, 0.020f), new Vector2(0.96f, 0.140f)),
                                              "Text", string.Empty, 28, TextAnchor.MiddleCenter);
            }
            else
            {
                // A row swaps between the 80×80 kit panel and the kit's 725×150 starred plate, whose
                // end caps must stay round in a box a fifth of the art's height — so it carries the
                // same correction every capsule in the game does. The podium cards must NOT: PillFit
                // on a 442-unit-tall card would draw the panel's 26 px border at 144 units.
                PillFit.Wrap(_slotBody[i]);

                // The rank is centred on the player row's gold star, not placed beside it: the star is a
                // rank badge, which is what it was drawn as. It lands in the same place on an ordinary
                // plate, where it simply reads as a left-hand rank column.
                _slotRank[i] = UiBuild.Label(Slot(body, "Sira", RankMin, RankMax),
                                             "Text", string.Empty, 28, TextAnchor.MiddleCenter);
                _slotName[i] = UiBuild.Label(Slot(body, "Ad", new Vector2(0.200f, 0f), new Vector2(0.575f, 1f)),
                                             "Text", string.Empty, 26, TextAnchor.MiddleLeft);
                _slotScore[i] = UiBuild.Label(Slot(body, "Puan", new Vector2(0.585f, 0f), new Vector2(0.845f, 1f)),
                                              "Text", string.Empty, 26, TextAnchor.MiddleRight);
                _slotChest[i] = Chest(body, new Vector2(0.860f, 0.060f), new Vector2(0.988f, 0.940f), i);
            }

            body.gameObject.SetActive(false);
        }

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

        /// <summary>
        /// The player's own line, below the board rather than inside it — both this and the claim strip
        /// sit in the gap under the frame, where the claim strip's winged star is free to rise over the
        /// board's bottom edge instead of being boxed in by it.
        /// </summary>
        private void BuildPinnedRow()
        {
            _pinnedRoot = LigKit.Sliced(_root, "SenSatiri", "satir_sen",
                                        PinnedMin, PinnedMax, true).rectTransform;

            _pinnedRank = UiBuild.Label(Slot(_pinnedRoot, "Sira", RankMin, RankMax),
                                        "Text", string.Empty, 28, TextAnchor.MiddleCenter);
            _pinnedName = UiBuild.Label(Slot(_pinnedRoot, "Ad", new Vector2(0.200f, 0f), new Vector2(0.640f, 1f)),
                                        "Text", string.Empty, 26, TextAnchor.MiddleLeft);
            _pinnedScore = UiBuild.Label(Slot(_pinnedRoot, "Puan", new Vector2(0.650f, 0f), new Vector2(0.950f, 1f)),
                                         "Text", string.Empty, 26, TextAnchor.MiddleRight);

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
            // the stretching top-centre segment, and a 670-unit box over 660 px of art draws it at 1.0.
            Image frame = LigKit.Sliced(_rewardCard, "Kart", "odul_pano",
                                        new Vector2(0.190f, 0.270f), new Vector2(0.810f, 0.730f), false);
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
                    if (on) Seat(i, board.Entries[i]);
                }

                // Pinned only when the player is off the visible positions. Inside them their own card
                // is already the green plate, and drawing them twice reads as a bug, not emphasis.
                //
                // AND ONLY WHEN NO REWARD IS OWED. The pinned row and the claim strip share the band
                // under the board, so only one may ever be up — <see cref="RefreshClaimStrip"/> settles
                // that before the board is even asked for, and this callback, which arrives afterwards,
                // must not undo it. It did: both were drawn, and the green row showed through the
                // strip's transparent corners as if the art were broken.
                bool owed = _claimStrip != null && _claimStrip.gameObject.activeSelf;
                bool pin = !owed && (board.PlayerRank > shown || board.PlayerRank == 0);
                if (pin) SeatPinned(board);
                else if (_pinnedRoot != null && _pinnedRoot.gameObject.activeSelf)
                    _pinnedRoot.gameObject.SetActive(false);

                if (_emptyLabel != null && _emptyLabel.gameObject.activeSelf)
                    _emptyLabel.gameObject.SetActive(false);
            });
        }

        private void Seat(int i, in LeaderboardEntry entry)
        {
            _slotRankValue[i] = entry.Rank;
            if (_slotRank[i] != null) _slotRank[i].text = entry.Rank.ToString();
            if (_slotName[i] != null) _slotName[i].text = entry.Name;
            if (_slotScore[i] != null) _slotScore[i].text = NumberFormatter.Format((double)entry.Score, 0);

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
            // row is the kit's starred green one; everyone else's is the game's own panel.
            Sprite playerPlate = LigKit.Get("satir_sen");
            if (playerPlate == null) playerPlate = UiSkin.ButtonGreen;
            Sprite plate = entry.IsPlayer ? playerPlate : RowPlate;
            if (_slotBody[i] != null && _slotBody[i].sprite != plate)
            {
                _slotBody[i].sprite = plate;
                // The two plates are different shapes, so the slicer has to be told which one it is
                // holding — and PillFit, if it is on, has to re-measure against the new art.
                _slotBody[i].type = plate.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                var fit = _slotBody[i].GetComponent<PillFit>();
                if (fit != null) fit.Fit();
            }

            // The name and score sit on the plate, so they invert with it. The RANK does not: on the
            // player's row it sits on the gold star, where white would be unreadable, and on an
            // ordinary plate dark is right anyway. So it stays dark on both.
            Color text = entry.IsPlayer ? Paper : Ink;
            if (_slotRank[i] != null) _slotRank[i].color = Ink;
            if (_slotName[i] != null) _slotName[i].color = text;
            if (_slotScore[i] != null) _slotScore[i].color = text;

            if (_slotChest[i] != null) _slotChest[i].sprite = ChestFor(entry.Rank);
        }

        private void SeatPinned(LeaderboardBoard board)
        {
            if (_pinnedRoot == null) return;
            if (!_pinnedRoot.gameObject.activeSelf) _pinnedRoot.gameObject.SetActive(true);

            bool ranked = board.PlayerRank > 0;
            if (_pinnedRank != null) _pinnedRank.text = ranked ? board.PlayerRank.ToString() : "—";
            if (_pinnedName != null)
                _pinnedName.text = ranked ? Loc.T("lig.siram") : Loc.T("lig.listede_degil");
            if (_pinnedScore != null)
                _pinnedScore.text = NumberFormatter.Format((double)board.PlayerScore, 0);

            if (_pinnedRank != null) _pinnedRank.color = Ink;   // it sits on the gold star; see Seat
            if (_pinnedName != null) _pinnedName.color = Paper;
            if (_pinnedScore != null) _pinnedScore.color = Paper;
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

        /// <summary>Gold, silver, bronze for the podium; the plain chest for everyone below.</summary>
        private Sprite ChestFor(int rank)
        {
            if (rank == 1) return _chest[0];
            if (rank == 2) return _chest[1];
            if (rank == 3) return _chest[2];
            return _chest[3];
        }

        /// <summary>The strip that says a closed season still owes something. It shares the bottom band
        /// with the pinned row, so only one is ever up: a reward the player can take outranks a rank
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
        /// nine blank plates, which reads as broken.</summary>
        private void Blank()
        {
            for (int i = 0; i < VisibleRows; i++)
            {
                // Cleared as well as hidden: the rank is what a chest pays out on, and a hidden slot
                // still holding last board's number could open a reward it no longer stands for.
                _slotRankValue[i] = 0;
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

        private static RectTransform Slot(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
        }
    }
}
