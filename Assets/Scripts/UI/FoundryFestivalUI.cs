using Game.Core;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The Foundry Festival screen: the week across the top, the day's three tasks in the middle, the
    /// chests they fill along the bottom.
    ///
    /// Opened from the events board rather than the HUD rail. The rail is full and a festival is not a
    /// permanent feature of the game — it is one card on a board that already exists to list what is
    /// running, and reaching it in two taps is the honest depth for something that is gone in a week.
    ///
    /// ONE DAY AT A TIME. Twenty-one rows down a phone is a spreadsheet; three rows and a strip of
    /// seven chips is a day's work with the week visible behind it. Any day can be looked at — the
    /// locked ones show what is coming, because a player deciding whether to come back tomorrow wants
    /// to know what tomorrow asks for.
    ///
    /// DRAWN FROM <see cref="EtkinlikKit"/>, the events family's shared pieces: the days are its tabs
    /// (gold for the one being looked at, the locked face for one not yet open), the tasks its navy
    /// rows, the chests the league's four, richer as they climb. Every claim that pays shows
    /// <see cref="EtkinlikOdulUI"/> — "claim all" once, with the total.
    ///
    /// The once-a-second Update drives the countdown and the day rollover, and only while the screen
    /// is open.
    /// </summary>
    public sealed class FoundryFestivalUI : MonoBehaviour
    {
        /// <summary>Above the events board's 109, which stays open behind this.</summary>
        [SerializeField] private int sortingOrder = 111;

        [Header("Renkler")]
        [SerializeField] private Color scrim = new Color(0.04f, 0.05f, 0.08f, 0.90f);

        /// <summary>Three task rows in the band between the day strip and the chests: 0.315–0.615 of the sheet.</summary>
        private const float TaskTop = 0.615f, TaskBottom = 0.318f, TaskGap = 0.006f;

        /// <summary>The navy card at 1/1.8 border — a task row is shorter than the board's.</summary>
        private const float TaskBorderScale = 1.8f;

        /// <summary>A chest tile is narrow and tall; its navy frame drawn finer still.</summary>
        private const float ChestBorderScale = 2.1f;

        private FoundryFestivalService _festival;
        private LocalizationService _loc;
        private RectTransform _root;

        /// <summary>Everything on the sheet but the empty state. One switch hides it when there is no festival.</summary>
        private RectTransform _body, _empty;

        private Text _titleLabel, _clockLabel, _emptyLabel, _pointsLabel, _chestCaption;
        private Button _claimAllBtn;
        private Text _claimAllText;
        private Image _pointsFill;
        private EtkinlikOdulUI _popup;

        private readonly Button[] _dayChip = new Button[FoundryFestival.Days];
        private readonly Text[] _dayText = new Text[FoundryFestival.Days];

        private readonly Text[] _taskText = new Text[FoundryFestival.TasksPerDay];
        private readonly Text[] _taskCount = new Text[FoundryFestival.TasksPerDay];
        private readonly EtkinlikOdulSatiri[] _taskReward = new EtkinlikOdulSatiri[FoundryFestival.TasksPerDay];
        private readonly Image[] _taskFillImage = new Image[FoundryFestival.TasksPerDay];
        private readonly Button[] _taskBtn = new Button[FoundryFestival.TasksPerDay];
        private readonly Text[] _taskBtnText = new Text[FoundryFestival.TasksPerDay];

        private readonly Image[] _chestIcon = new Image[FoundryFestival.MilestoneCount];
        private readonly EtkinlikOdulSatiri[] _chestGems = new EtkinlikOdulSatiri[FoundryFestival.MilestoneCount];
        private readonly EtkinlikOdulSatiri[] _chestExtra = new EtkinlikOdulSatiri[FoundryFestival.MilestoneCount];
        private readonly Button[] _chestBtn = new Button[FoundryFestival.MilestoneCount];
        private readonly Text[] _chestBtnText = new Text[FoundryFestival.MilestoneCount];

        /// <summary>The day being looked at, which is today's until the player taps another chip.</summary>
        private int _day;

        private float _tick;

        private void Awake()
        {
            _festival = ServiceLocator.Get<FoundryFestivalService>();
            Build();
            if (_festival != null) _festival.Changed += OnChanged;
            _loc = ServiceLocator.Get<LocalizationService>();
            if (_loc != null) _loc.Changed += OnLanguageChanged;
            Hide();
        }

        private void OnDestroy()
        {
            if (_festival != null) _festival.Changed -= OnChanged;
            if (_loc != null) _loc.Changed -= OnLanguageChanged;
        }

        private void OnChanged() => Refresh();

        private void OnLanguageChanged()
        {
            if (_titleLabel != null) _titleLabel.text = Loc.T("senlik.baslik");
            if (_chestCaption != null) _chestCaption.text = Loc.T("senlik.sandiklar");
            if (_emptyLabel != null) _emptyLabel.text = Loc.T("senlik.yok");
            Refresh();
        }

        /// <summary>Opens on the day the player is actually on — the one they can still work at.</summary>
        public void Show()
        {
            if (_root == null) return;
            _day = _festival != null ? _festival.Day : 0;
            _root.gameObject.SetActive(true);
            _tick = 0f;
            Refresh();
        }

        public void Hide()
        {
            if (_popup != null) _popup.Hide();
            if (_root != null) _root.gameObject.SetActive(false);
        }

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
            RectTransform canvas = UiBuild.Canvas(transform, "SenlikKanvas", sortingOrder);
            _root = UiBuild.Flat(canvas, "Karartma", UiBuild.Opaque(scrim), Vector2.zero, Vector2.one);
            var dismiss = _root.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(Hide);

            RectTransform sheet = EtkinlikKit.Sheet(_root).rectTransform;
            _titleLabel = EtkinlikKit.Header(_root, Loc.T("senlik.baslik"), Hide);
            _body = EtkinlikKit.Slot(sheet, "Govde", Vector2.zero, Vector2.one);

            BuildTop();
            BuildDayStrip();

            float h = (TaskTop - TaskBottom) / FoundryFestival.TasksPerDay;
            for (int i = 0; i < FoundryFestival.TasksPerDay; i++)
                BuildTask(i, new Vector2(0.085f, TaskTop - (i + 1) * h + TaskGap),
                             new Vector2(0.915f, TaskTop - i * h - TaskGap));

            BuildChests();

            // What the screen says when there is no festival. Reachable only if the board opened this
            // while the schedule changed underneath it, but a blank sheet reads as a broken screen.
            _empty = EtkinlikKit.Empty(sheet, Loc.T("senlik.yok"), out _emptyLabel);
            _popup = EtkinlikOdulUI.Create(_root);
            // Content into the safe area; the scrim above it keeps covering the notch.
            UiBuild.InsetContent(_root);
        }

        /// <summary>The countdown, and the orange "claim all" — the one act on the screen that takes everything at once.</summary>
        private void BuildTop()
        {
            _clockLabel = EtkinlikKit.Label(_body, "Saat", new Vector2(0.090f, 0.705f), new Vector2(0.580f, 0.765f),
                                            string.Empty, 26, TextAnchor.MiddleLeft, EtkinlikKit.InkSoft, 14);
            _claimAllBtn = EtkinlikKit.Capsule(_body, "HepsiniAl", new Vector2(0.590f, 0.703f), new Vector2(0.910f, 0.767f),
                                               ClaimAll, out _claimAllText);
        }

        /// <summary>The week, as seven tabs. Tapping one looks at that day; it never claims.</summary>
        private void BuildDayStrip()
        {
            const float left = 0.085f, right = 0.915f, gap = 0.004f;
            float w = (right - left) / FoundryFestival.Days;

            for (int d = 0; d < FoundryFestival.Days; d++)
            {
                int captured = d;
                _dayChip[d] = EtkinlikKit.Tab(_body, "Gun" + d,
                                              new Vector2(left + d * w + gap, 0.628f), new Vector2(left + (d + 1) * w - gap, 0.690f),
                                              () => { _day = captured; Refresh(); }, out _dayText[d]);
                _dayText[d].text = (d + 1).ToString();
            }
        }

        /// <summary>The task over its count, the bar, the gems and cards it pays, and its claim.</summary>
        private void BuildTask(int index, Vector2 aMin, Vector2 aMax)
        {
            RectTransform card = EtkinlikKit.Card(_body, "Gorev" + index, aMin, aMax, TaskBorderScale).rectTransform;

            _taskText[index] = EtkinlikKit.Label(card, "Yazi", new Vector2(0.045f, 0.54f), new Vector2(0.500f, 0.78f),
                                                 string.Empty, 26, TextAnchor.MiddleLeft, EkranKit.Paper, 14);
            _taskCount[index] = EtkinlikKit.Label(card, "Sayi", new Vector2(0.500f, 0.54f), new Vector2(0.660f, 0.78f),
                                                  string.Empty, 22, TextAnchor.MiddleRight, EkranKit.PaperSoft, 12);
            _taskFillImage[index] = EtkinlikKit.Bar(card, "Cubuk", new Vector2(0.045f, 0.40f), new Vector2(0.660f, 0.53f), "cubuk_mavi");
            _taskReward[index] = EtkinlikOdulSatiri.Create(card, "Odul", new Vector2(0.045f, 0.20f), new Vector2(0.660f, 0.39f),
                                                          22, EkranKit.PaperSoft, TextAnchor.MiddleLeft);

            int captured = index;
            _taskBtn[index] = EtkinlikKit.Capsule(card, "Al", new Vector2(0.690f, 0.27f), new Vector2(0.960f, 0.73f),
                                                  () => ClaimTask(captured), out _taskBtnText[index]);
        }

        /// <summary>
        /// The points line over a gold bar, then five chest tiles: the chest, what it pays, and a capsule
        /// that is the points it needs until it is earned, then the claim, then "claimed".
        /// </summary>
        private void BuildChests()
        {
            _chestCaption = EtkinlikKit.Label(_body, "SandikBaslik", new Vector2(0.090f, 0.268f), new Vector2(0.500f, 0.308f),
                                              Loc.T("senlik.sandiklar"), 26, TextAnchor.MiddleLeft, EkranKit.Ink, 14);
            _pointsLabel = EtkinlikKit.Label(_body, "Puan", new Vector2(0.500f, 0.268f), new Vector2(0.910f, 0.308f),
                                             string.Empty, 24, TextAnchor.MiddleRight, EtkinlikKit.InkSoft, 12);
            _pointsFill = EtkinlikKit.Bar(_body, "PuanCubugu", new Vector2(0.090f, 0.238f), new Vector2(0.910f, 0.266f), "cubuk_altin");

            const float left = 0.085f, right = 0.915f, gap = 0.005f;
            float w = (right - left) / FoundryFestival.MilestoneCount;

            for (int i = 0; i < FoundryFestival.MilestoneCount; i++)
            {
                RectTransform tile = EtkinlikKit.Card(_body, "Sandik" + i,
                                                      new Vector2(left + i * w + gap, 0.082f),
                                                      new Vector2(left + (i + 1) * w - gap, 0.228f), ChestBorderScale).rectTransform;
                _chestIcon[i] = EkranKit.Icon(tile, "Simge", EtkinlikKit.Chest(i < 3 ? i : 3),
                                              new Vector2(0.16f, 0.56f), new Vector2(0.84f, 0.92f));
                // Two short lines, not one: the richest chest pays gems, cards AND charts, and three
                // icon-and-amount pairs on one line ran past a tile a fifth of the sheet wide.
                _chestGems[i] = EtkinlikOdulSatiri.Create(tile, "Elmas", new Vector2(0.04f, 0.425f), new Vector2(0.96f, 0.555f),
                                                         17, EkranKit.Paper, TextAnchor.MiddleCenter);
                _chestExtra[i] = EtkinlikOdulSatiri.Create(tile, "Diger", new Vector2(0.04f, 0.295f), new Vector2(0.96f, 0.425f),
                                                          17, EkranKit.Paper, TextAnchor.MiddleCenter);
                int captured = i;
                _chestBtn[i] = EtkinlikKit.Capsule(tile, "Al", new Vector2(0.08f, 0.06f), new Vector2(0.92f, 0.29f),
                                                   () => ClaimMilestone(captured), out _chestBtnText[i]);
            }
        }

        // ------------------------------------------------------------------ claim
        private void ClaimTask(int index)
        {
            if (_festival == null) return;
            int slot = FoundryFestival.TaskSlot(_day, index);
            FoundryFestival.Task task = _festival.TaskAt(slot);
            if (_festival.ClaimTask(slot)) _popup.Present(task.Gems, task.Cards, 0L, null);
            Refresh();
        }

        private void ClaimMilestone(int index)
        {
            if (_festival == null) return;
            FoundryFestival.Milestone m = _festival.MilestoneAt(index);
            if (_festival.ClaimMilestone(index)) _popup.Present(m.Gems, m.Cards, m.Charts, null);
            Refresh();
        }

        /// <summary>
        /// Adds up what is waiting BEFORE claiming — the service pays it all in one save and does not
        /// hand back the sum — so the card shows the total that was actually taken.
        /// </summary>
        private void ClaimAll()
        {
            if (_festival == null) return;
            long gems = 0L, charts = 0L;
            int cards = 0;
            for (int slot = 0; slot < FoundryFestival.TaskCount; slot++)
            {
                if (!_festival.CanClaimTask(slot)) continue;
                FoundryFestival.Task t = _festival.TaskAt(slot);
                gems += t.Gems;
                cards += t.Cards;
            }
            for (int i = 0; i < FoundryFestival.MilestoneCount; i++)
            {
                if (!_festival.CanClaimMilestone(i)) continue;
                FoundryFestival.Milestone m = _festival.MilestoneAt(i);
                gems += m.Gems;
                cards += m.Cards;
                charts += m.Charts;
            }
            if (_festival.ClaimAll() > 0) _popup.Present(gems, cards, charts, null);
            Refresh();
        }

        // ---------------------------------------------------------------- refresh
        private void Refresh()
        {
            if (_root == null || !_root.gameObject.activeSelf) return;
            _titleLabel.text = Loc.T("senlik.baslik");

            bool has = _festival != null && _festival.Available;
            EtkinlikKit.SetActive(_empty, !has);
            EtkinlikKit.SetActive(_body, has);
            if (!has) return;

            // A day the player was looking at can stop existing when the festival is swapped under
            // them; and the strip is the only thing that ever moves the cursor off today.
            if (_day < 0 || _day >= FoundryFestival.Days) _day = _festival.Day;

            RefreshHeader();
            for (int d = 0; d < FoundryFestival.Days; d++) RefreshDay(d);
            for (int i = 0; i < FoundryFestival.TasksPerDay; i++) RefreshTask(i);
            RefreshChests();
        }

        private void RefreshHeader()
        {
            switch (_festival.Phase)
            {
                case LiveEvents.Phase.Active:
                    _clockLabel.text = Loc.T("etkinlik.kalan") + " " + HudUI.LongClock(_festival.SecondsLeft);
                    break;
                case LiveEvents.Phase.Upcoming:
                    _clockLabel.text = Loc.T("etkinlik.basliyor") + " " + HudUI.LongClock(_festival.SecondsUntilStart);
                    break;
                default:
                    _clockLabel.text = Loc.T("senlik.bitti");
                    break;
            }

            int pending = _festival.PendingCount();
            _claimAllText.text = EtkinlikKit.OneLine(pending > 0
                ? Loc.T("senlik.hepsini_al") + " ×" + pending
                : Loc.T("senlik.hepsini_al"));
            EtkinlikKit.SetFace(_claimAllBtn, _claimAllText,
                                pending > 0 ? EtkinlikKit.Face.Primary : EtkinlikKit.Face.Dead, pending > 0);
        }

        /// <summary>
        /// The blue chip for the day being looked at, the pale capsule for the rest, and a faint number
        /// on a day still to come.
        ///
        /// NOT THE TABS' OWN FACES. Seven across makes each chip barely wider than its two round caps,
        /// and the tab art carries its ornament in those caps — the selected tab's gold studs sat on
        /// the day's number and the locked tab's padlock covered it. The chip and the pale capsule are
        /// plain to their ends, so the number has the whole face.
        /// </summary>
        private void RefreshDay(int day)
        {
            bool unlocked = _festival.TaskUnlocked(FoundryFestival.TaskSlot(day, 0));
            bool looking = day == _day;
            Button chip = _dayChip[day];
            Sprite want = looking ? AtolyeKit.Get("hap_cip") : EkranKit.Get("btn_bos");
            var image = (Image)chip.targetGraphic;
            if (want != null && image.sprite != want)
            {
                image.sprite = want;
                var fit = image.GetComponent<PillFit>();
                if (fit != null) fit.Fit();
            }
            _dayText[day].color = looking ? EkranKit.Paper : unlocked ? EkranKit.Ink : EtkinlikKit.InkSoft;
        }

        private void RefreshTask(int index)
        {
            int slot = FoundryFestival.TaskSlot(_day, index);
            FoundryFestival.Task t = _festival.TaskAt(slot);
            bool unlocked = _festival.TaskUnlocked(slot);
            bool claimed = _festival.TaskClaimed(slot);
            bool can = _festival.CanClaimTask(slot);
            long have = _festival.TaskProgress(slot);

            _taskText[index].text = MetricName(t.Metric);
            _taskText[index].color = unlocked && !claimed ? EkranKit.Paper : EkranKit.PaperSoft;
            _taskCount[index].text = unlocked ? have + " / " + t.Target : Loc.T("senlik.kilitli");
            EtkinlikKit.Progress(_taskFillImage[index], unlocked ? Goals.Progress(have, t.Target) : 0f);
            _taskReward[index].Set("+" + t.Points + " " + Loc.T("senlik.puan"), t.Gems, t.Cards, 0L, null);

            _taskBtnText[index].text = EtkinlikKit.OneLine(claimed ? Loc.T("gorev.alindi")
                                     : unlocked ? Loc.T("gorev.al") : Loc.T("gorev.kilitli"));
            EtkinlikKit.SetFace(_taskBtn[index], _taskBtnText[index],
                                can ? EtkinlikKit.Face.Claim : EtkinlikKit.Face.Dead, can);
        }

        private void RefreshChests()
        {
            int points = _festival.Points;
            int next = _festival.NextMilestonePoints;

            _pointsLabel.text = next > 0
                ? string.Format("{0} / {1} {2}", points, next, Loc.T("senlik.puan"))
                : string.Format("{0} {1}", points, Loc.T("senlik.puan"));
            EtkinlikKit.Progress(_pointsFill, next > 0 ? Goals.Progress(points, next) : 1f);

            for (int i = 0; i < FoundryFestival.MilestoneCount; i++)
            {
                FoundryFestival.Milestone m = _festival.MilestoneAt(i);
                bool claimed = _festival.MilestoneClaimed(i);
                bool ready = _festival.CanClaimMilestone(i);

                _chestIcon[i].color = ready ? Color.white : EtkinlikKit.Faded;
                _chestGems[i].Set(null, m.Gems, 0, 0L, null);
                _chestExtra[i].Set(null, 0L, m.Cards, m.Charts, null);
                _chestBtnText[i].text = EtkinlikKit.OneLine(claimed ? Loc.T("gorev.alindi")
                                      : ready ? Loc.T("gorev.al") : m.Points.ToString());
                EtkinlikKit.SetFace(_chestBtn[i], _chestBtnText[i],
                                    ready ? EtkinlikKit.Face.Claim : EtkinlikKit.Face.Dead, ready);
            }
        }

        // ------------------------------------------------------------------ pieces
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
