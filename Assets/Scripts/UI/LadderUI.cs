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
    /// NINE POSITIONS, NOT THIRTY: three on the podium and six in two columns of three, plus the player
    /// pinned underneath when they sit outside them. Two columns because this game runs landscape and,
    /// as <see cref="ChapterUI"/> records, full-width rows stacked down a landscape screen come out as
    /// a letterbox with bars in it. No scroll view, no pooling, a fixed allocation.
    ///
    /// The once-a-second Update only drives the countdown, and only while the board is open.
    /// </summary>
    public sealed class LadderUI : MonoBehaviour
    {
        /// <summary>Above the events board's 109.</summary>
        [SerializeField] private int sortingOrder = 111;

        [Header("Görseller")]
        [Tooltip("Satır gövdesi. Boşsa UiSkin'in kit paneli kullanılır.")]
        [SerializeField] private Sprite cardPanel;
        [Tooltip("Başlık şeridi. Boşsa UiSkin'in kapsülü kullanılır.")]
        [SerializeField] private Sprite ribbon;
        [Tooltip("Kapat düğmesi.")]
        [SerializeField] private Sprite closeIcon;

        [Header("Renkler")]
        [SerializeField] private Color scrim = new Color(0.04f, 0.05f, 0.08f, 0.92f);
        [SerializeField] private Color backdrop = new Color(0.15f, 0.18f, 0.26f, 1f);

        private const string OpenerIconResource = "UI/Buttons/lig";
        private const string ChestResources = "UI/Lig/sandik_";

        /// <summary>The podium — first, second, third — and then the rows under it.</summary>
        private const int PodiumSlots = 3;
        private const int RowSlots = 6;
        private const int VisibleRows = PodiumSlots + RowSlots;

        private static readonly Color Ink = new Color(0.09f, 0.14f, 0.24f, 1f);
        private static readonly Color InkSoft = new Color(0.36f, 0.42f, 0.52f, 1f);
        private static readonly Color Paper = new Color(0.96f, 0.97f, 1f, 1f);

        private LadderService _ladder;
        private LocalizationService _loc;
        private RectTransform _root;

        private Text _titleLabel, _clockLabel, _syntheticLabel, _emptyLabel, _claimLabel;
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

        private RectTransform _pinnedRoot;
        private Text _pinnedRank, _pinnedName, _pinnedScore;

        private RectTransform _rewardCard;
        private Image _rewardChest;
        private Text _rewardTitle, _rewardBody;

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

        /// <summary>Gold, silver, bronze, and the plain chest every rank below third shares. Drawn by
        /// <c>Tools/ui/lig_sandik.py</c>; a missing file leaves the slot null and the position simply
        /// shows no chest rather than a broken one.</summary>
        private void LoadChests()
        {
            _chest[0] = Resources.Load<Sprite>(ChestResources + "altin");
            _chest[1] = Resources.Load<Sprite>(ChestResources + "gumus");
            _chest[2] = Resources.Load<Sprite>(ChestResources + "bronz");
            _chest[3] = Resources.Load<Sprite>(ChestResources + "sade");
        }

        private void Build()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "LigKanvas", sortingOrder);
            _root = UiBuild.Flat(canvas, "Karartma", scrim, Vector2.zero, Vector2.one);
            var dismiss = _root.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(Hide);

            BuildBackdrop();
            BuildHeader();
            BuildPodium();
            BuildRows();
            BuildPinnedRow();
            BuildClaimStrip();

            _emptyLabel = UiBuild.Label(Slot(_root, "Bos", new Vector2(0.10f, 0.380f), new Vector2(0.90f, 0.470f)),
                                        "Text", Loc.T("lig.yok"), 32, TextAnchor.MiddleCenter);
            _emptyLabel.color = InkSoft;

            BuildRewardCard();
        }

        private void BuildBackdrop()
        {
            RectTransform sheet = Art(_root, "Zemin", cardPanel,
                                      new Vector2(0.020f, 0.020f), new Vector2(0.980f, 0.860f));
            var image = sheet.GetComponent<Image>();
            image.color = backdrop;
            image.raycastTarget = true;
            var eat = sheet.gameObject.AddComponent<Button>();
            eat.transition = Selectable.Transition.None;
        }

        private void BuildHeader()
        {
            RectTransform band = Art(_root, "Serit", ribbon != null ? ribbon : UiSkin.Pill,
                                     new Vector2(0.350f, 0.885f), new Vector2(0.650f, 0.995f));
            _titleLabel = UiBuild.Label(Slot(band, "Yazi", new Vector2(0.10f, 0.15f), new Vector2(0.90f, 0.85f)),
                                        "Text", Loc.T("lig.baslik"), 36, TextAnchor.MiddleCenter);

            Button close = UiBuild.Btn(_root, "Kapat", string.Empty,
                                       closeIcon != null ? closeIcon : UiSkin.ButtonGrey,
                                       new Color(0.10f, 0.11f, 0.16f, 1f), 34, Hide);
            var closeImage = close.GetComponent<Image>();
            closeImage.type = Image.Type.Simple;
            closeImage.preserveAspect = true;
            UiBuild.Anchor((RectTransform)close.transform,
                           new Vector2(0.900f, 0.880f), new Vector2(0.965f, 0.985f));

            _clockLabel = UiBuild.Label(Slot(_root, "Sure", new Vector2(0.200f, 0.820f), new Vector2(0.800f, 0.876f)),
                                        "Text", string.Empty, 30, TextAnchor.MiddleCenter);
            _clockLabel.color = Paper;

            _syntheticLabel = UiBuild.Label(Slot(_root, "Temsili", new Vector2(0.200f, 0.780f), new Vector2(0.800f, 0.818f)),
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
            BuildSlot(0, new Vector2(0.365f, 0.480f), new Vector2(0.635f, 0.775f), true);   // 1st, centre
            BuildSlot(1, new Vector2(0.075f, 0.480f), new Vector2(0.345f, 0.710f), true);   // 2nd, left
            BuildSlot(2, new Vector2(0.655f, 0.480f), new Vector2(0.925f, 0.690f), true);   // 3rd, right
        }

        /// <summary>Ranks four to nine, three down each column.</summary>
        private void BuildRows()
        {
            const float top = 0.455f, bottom = 0.155f;
            const int perColumn = RowSlots / 2;
            float ch = (top - bottom) / perColumn;

            for (int i = 0; i < RowSlots; i++)
            {
                int slot = i % perColumn;
                bool left = i < perColumn;
                BuildSlot(PodiumSlots + i,
                          new Vector2(left ? 0.075f : 0.510f, top - (slot + 1) * ch + 0.008f),
                          new Vector2(left ? 0.490f : 0.925f, top - slot * ch - 0.008f), false);
            }
        }

        /// <summary>
        /// One position, podium card or row. The two differ only in how the four pieces are placed:
        /// a card stacks them, a row lays them along its length.
        /// </summary>
        private void BuildSlot(int i, Vector2 aMin, Vector2 aMax, bool podium)
        {
            RectTransform body = Art(_root, (podium ? "Podyum" : "Satir") + i, cardPanel, aMin, aMax);
            _slotRoot[i] = body;
            _slotBody[i] = body.GetComponent<Image>();

            if (podium)
            {
                _slotRank[i] = UiBuild.Label(Slot(body, "Sira", new Vector2(0.05f, 0.775f), new Vector2(0.95f, 0.965f)),
                                             "Text", string.Empty, 40, TextAnchor.MiddleCenter);
                _slotName[i] = UiBuild.Label(Slot(body, "Ad", new Vector2(0.05f, 0.630f), new Vector2(0.95f, 0.775f)),
                                             "Text", string.Empty, 24, TextAnchor.MiddleCenter);
                _slotChest[i] = Chest(body, new Vector2(0.270f, 0.230f), new Vector2(0.730f, 0.610f), i);
                _slotScore[i] = UiBuild.Label(Slot(body, "Puan", new Vector2(0.05f, 0.045f), new Vector2(0.95f, 0.215f)),
                                              "Text", string.Empty, 28, TextAnchor.MiddleCenter);
            }
            else
            {
                _slotRank[i] = UiBuild.Label(Slot(body, "Sira", new Vector2(0.030f, 0f), new Vector2(0.140f, 1f)),
                                             "Text", string.Empty, 28, TextAnchor.MiddleCenter);
                _slotName[i] = UiBuild.Label(Slot(body, "Ad", new Vector2(0.160f, 0f), new Vector2(0.560f, 1f)),
                                             "Text", string.Empty, 26, TextAnchor.MiddleLeft);
                _slotScore[i] = UiBuild.Label(Slot(body, "Puan", new Vector2(0.570f, 0f), new Vector2(0.810f, 1f)),
                                              "Text", string.Empty, 26, TextAnchor.MiddleRight);
                _slotChest[i] = Chest(body, new Vector2(0.840f, 0.100f), new Vector2(0.975f, 0.900f), i);
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

        private void BuildPinnedRow()
        {
            _pinnedRoot = Art(_root, "SenSatiri", UiSkin.ButtonGreen,
                              new Vector2(0.075f, 0.035f), new Vector2(0.925f, 0.140f));

            _pinnedRank = UiBuild.Label(Slot(_pinnedRoot, "Sira", new Vector2(0.030f, 0f), new Vector2(0.140f, 1f)),
                                        "Text", string.Empty, 28, TextAnchor.MiddleCenter);
            _pinnedName = UiBuild.Label(Slot(_pinnedRoot, "Ad", new Vector2(0.160f, 0f), new Vector2(0.640f, 1f)),
                                        "Text", string.Empty, 26, TextAnchor.MiddleLeft);
            _pinnedScore = UiBuild.Label(Slot(_pinnedRoot, "Puan", new Vector2(0.650f, 0f), new Vector2(0.960f, 1f)),
                                         "Text", string.Empty, 26, TextAnchor.MiddleRight);

            _pinnedRoot.gameObject.SetActive(false);
        }

        private void BuildClaimStrip()
        {
            _claimStrip = Art(_root, "OdulSeridi", UiSkin.ButtonYellow,
                              new Vector2(0.075f, 0.035f), new Vector2(0.925f, 0.140f));

            _claimLabel = UiBuild.Label(Slot(_claimStrip, "Yazi", new Vector2(0.040f, 0f), new Vector2(0.640f, 1f)),
                                        "Text", string.Empty, 28, TextAnchor.MiddleLeft);
            _claimLabel.color = Ink;

            Button take = UiBuild.Btn(_claimStrip, "Al", Loc.T("gorev.al"), UiSkin.ButtonGreen,
                                      Paper, 28, ClaimAll);
            UiBuild.Anchor((RectTransform)take.transform,
                           new Vector2(0.670f, 0.130f), new Vector2(0.960f, 0.870f));

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

            RectTransform card = Art(_rewardCard, "Kart", cardPanel,
                                     new Vector2(0.330f, 0.300f), new Vector2(0.670f, 0.720f));
            card.GetComponent<Image>().raycastTarget = true;
            var eat = card.gameObject.AddComponent<Button>();
            eat.transition = Selectable.Transition.None;

            _rewardTitle = UiBuild.Label(Slot(card, "Baslik", new Vector2(0.05f, 0.800f), new Vector2(0.95f, 0.950f)),
                                         "Text", string.Empty, 34, TextAnchor.MiddleCenter);
            _rewardTitle.color = Ink;

            var chest = new GameObject("Sandik", typeof(RectTransform), typeof(Image));
            chest.transform.SetParent(card, false);
            _rewardChest = chest.GetComponent<Image>();
            _rewardChest.preserveAspect = true;
            _rewardChest.raycastTarget = false;
            UiBuild.Anchor((RectTransform)chest.transform,
                           new Vector2(0.300f, 0.380f), new Vector2(0.700f, 0.780f));

            _rewardBody = UiBuild.Label(Slot(card, "Odul", new Vector2(0.05f, 0.180f), new Vector2(0.95f, 0.360f)),
                                        "Text", string.Empty, 28, TextAnchor.MiddleCenter);
            _rewardBody.color = Ink;

            Button close = UiBuild.Btn(card, "Kapat", Loc.T("lig.kapat"), UiSkin.ButtonGreen,
                                       Paper, 28, HideReward);
            UiBuild.Anchor((RectTransform)close.transform,
                           new Vector2(0.240f, 0.045f), new Vector2(0.760f, 0.165f));

            _rewardCard.gameObject.SetActive(false);
        }

        private void BuildOpener()
        {
            HudUI hud = FindAnyObjectByType<HudUI>(FindObjectsInactive.Include);
            if (hud == null) return;

            // No ladder in this build means no entry point at all — not an empty board
            // (Docs/LEADERBOARDS.md §13).
            if (_ladder == null || !_ladder.Available) return;

            Sprite icon = Resources.Load<Sprite>(OpenerIconResource);
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
                bool pin = board.PlayerRank > shown || board.PlayerRank == 0;
                if (pin) SeatPinned(board);
                else if (_pinnedRoot != null && _pinnedRoot.gameObject.activeSelf)
                    _pinnedRoot.gameObject.SetActive(false);

                if (_emptyLabel != null && _emptyLabel.gameObject.activeSelf)
                    _emptyLabel.gameObject.SetActive(false);
            });
        }

        private void Seat(int i, in LeaderboardEntry entry)
        {
            if (_slotRank[i] != null) _slotRank[i].text = entry.Rank.ToString();
            if (_slotName[i] != null) _slotName[i].text = entry.Name;
            if (_slotScore[i] != null) _slotScore[i].text = NumberFormatter.Format((double)entry.Score, 0);

            // Pre-coloured kit art: state is the SPRITE, never a tint over a neutral plate.
            Sprite plate = entry.IsPlayer ? UiSkin.ButtonGreen : UiSkin.Panel;
            if (_slotBody[i] != null && _slotBody[i].sprite != plate) _slotBody[i].sprite = plate;

            Color text = entry.IsPlayer ? Paper : Ink;
            if (_slotRank[i] != null) _slotRank[i].color = text;
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

            if (_pinnedRank != null) _pinnedRank.color = Paper;
            if (_pinnedName != null) _pinnedName.color = Paper;
            if (_pinnedScore != null) _pinnedScore.color = Paper;
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
            if (_rewardCard == null || _slotRank[slot] == null) return;
            if (!int.TryParse(_slotRank[slot].text, out int rank) || rank <= 0) return;

            int tier = Leaderboards.RewardTier(rank, Leaderboards.DefaultBracketEnds);
            Ladder.Reward reward = _ladder != null ? _ladder.RewardFor(tier) : default;

            if (_rewardTitle != null) _rewardTitle.text = BracketLabel(tier);
            if (_rewardChest != null) _rewardChest.sprite = ChestFor(rank);

            if (_rewardBody != null)
            {
                string text = reward.Gems > 0L
                    ? NumberFormatter.Format((double)reward.Gems, 0) + " " + Loc.T("ortak.elmas")
                    : string.Empty;
                if (reward.Cards > 0)
                {
                    if (text.Length > 0) text += "\n";
                    text += reward.Cards + " " + Loc.T("lig.kart");
                }
                _rewardBody.text = text.Length > 0 ? text : Loc.T("lig.odul_yok");
            }

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
                if (_slotRoot[i] != null && _slotRoot[i].gameObject.activeSelf)
                    _slotRoot[i].gameObject.SetActive(false);

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
