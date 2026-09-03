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
    /// Built in code for the same reason <see cref="GoalsUI"/> and <see cref="LiveEventsUI"/> are: the
    /// rows come out of <see cref="FoundryFestival"/>'s own table, so retuning the festival costs
    /// nothing here.
    ///
    /// The once-a-second Update drives the countdown and the day rollover, and only while the screen
    /// is open.
    /// </summary>
    public sealed class FoundryFestivalUI : MonoBehaviour
    {
        /// <summary>Above the events board's 109, which stays open behind this.</summary>
        [SerializeField] private int sortingOrder = 111;

        [Header("Görseller")]
        [Tooltip("Satır gövdesi — MaviSet/panel_beyaz.")]
        [SerializeField] private Sprite cardPanel;
        [Tooltip("Başlık şeridi — MaviSet/serit_mavi.")]
        [SerializeField] private Sprite ribbon;
        [Tooltip("Al düğmesi — MaviSet/btn_hap_kalin.")]
        [SerializeField] private Sprite actionButton;
        [Tooltip("Kapat düğmesi — MaviSet/btn_kapat_yeni.")]
        [SerializeField] private Sprite closeIcon;
        [Tooltip("İlerleme çubuğunun yatağı ve dolgusu — Gostergeler/slider_yatak, bar_dolgu.")]
        [SerializeField] private Sprite barTrack;
        [SerializeField] private Sprite barFill;
        [Tooltip("Gün ve sandık rozetleri — MaviSet/gosterge_grafit.")]
        [SerializeField] private Sprite chipPill;
        [Tooltip("Ödülün solundaki elmas ikonu — Ikonlar/ikon_elmas.")]
        [SerializeField] private Sprite gemIcon;

        [Header("Renkler")]
        [SerializeField] private Color scrim = new Color(0.04f, 0.05f, 0.08f, 0.90f);
        [SerializeField] private Color backdrop = new Color(0.15f, 0.18f, 0.26f, 1f);
        [SerializeField] private Color card = new Color(0.16f, 0.19f, 0.27f, 1f);
        [SerializeField] private Color track = new Color(0.10f, 0.11f, 0.16f, 1f);
        [SerializeField] private Color taskFill = new Color(0.35f, 0.72f, 0.98f, 1f);
        [SerializeField] private Color pointFill = new Color(0.98f, 0.74f, 0.24f, 1f);
        [Tooltip("Bakılan gün.")]
        [SerializeField] private Color dayOpen = new Color(0.36f, 0.82f, 0.45f, 1f);
        [Tooltip("Açılmış ama bakılmayan gün.")]
        [SerializeField] private Color dayPast = new Color(0.30f, 0.36f, 0.48f, 1f);
        [Tooltip("Henüz açılmamış gün.")]
        [SerializeField] private Color dayLocked = new Color(0.20f, 0.23f, 0.30f, 1f);

        private static readonly Color Ink = new Color(0.09f, 0.14f, 0.24f, 1f);
        private static readonly Color InkSoft = new Color(0.36f, 0.42f, 0.52f, 1f);
        private static readonly Color InkFaint = new Color(0.58f, 0.63f, 0.71f, 1f);
        private static readonly Color Paper = new Color(0.96f, 0.97f, 1f, 1f);

        private FoundryFestivalService _festival;
        private LocalizationService _loc;
        private RectTransform _root;

        /// <summary>Everything but the scrim, the close button and the empty line. One switch hides
        /// the whole sheet when there is no festival to draw.</summary>
        private RectTransform _body;

        private Text _titleLabel, _clockLabel, _emptyLabel, _pointsLabel, _chestCaption;
        private Button _claimAllBtn;
        private Text _claimAllText;
        private Image _pointsFill;

        private readonly Image[] _dayChip = new Image[FoundryFestival.Days];
        private readonly Text[] _dayText = new Text[FoundryFestival.Days];

        private readonly Text[] _taskText = new Text[FoundryFestival.TasksPerDay];
        private readonly Text[] _taskReward = new Text[FoundryFestival.TasksPerDay];
        private readonly Image[] _taskFillImage = new Image[FoundryFestival.TasksPerDay];
        private readonly Button[] _taskBtn = new Button[FoundryFestival.TasksPerDay];
        private readonly Text[] _taskBtnText = new Text[FoundryFestival.TasksPerDay];

        private readonly Image[] _chest = new Image[FoundryFestival.MilestoneCount];
        private readonly Text[] _chestText = new Text[FoundryFestival.MilestoneCount];
        private readonly Button[] _chestBtn = new Button[FoundryFestival.MilestoneCount];

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

        public void Hide() { if (_root != null) _root.gameObject.SetActive(false); }

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
            _root = UiBuild.Flat(canvas, "Karartma", scrim, Vector2.zero, Vector2.one);
            var dismiss = _root.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(Hide);

            _body = Slot(_root, "Govde", Vector2.zero, Vector2.one);
            BuildBackdrop();
            BuildHeader();
            BuildDayStrip();

            const float top = 0.685f, bottom = 0.395f;
            float h = (top - bottom) / FoundryFestival.TasksPerDay;
            for (int i = 0; i < FoundryFestival.TasksPerDay; i++)
                BuildTask(i, new Vector2(0.055f, top - (i + 1) * h + 0.008f),
                             new Vector2(0.945f, top - i * h - 0.008f));

            BuildChests();

            // What the screen says when there is no festival. Reachable only if the board opened this
            // while the schedule changed underneath it, but a blank sheet reads as a broken screen.
            _emptyLabel = UiBuild.Label(Slot(_root, "Bos", new Vector2(0.10f, 0.400f), new Vector2(0.90f, 0.500f)),
                                        "Text", Loc.T("senlik.yok"), 34, TextAnchor.MiddleCenter);
            _emptyLabel.color = InkSoft;
        }

        /// <summary>The sheet everything sits on. Eats its own taps so the scrim's dismiss cannot fire
        /// through it — see ChapterUI.BuildBackdrop.</summary>
        private void BuildBackdrop()
        {
            RectTransform sheet = Art(_body, "Zemin", cardPanel,
                                      new Vector2(0.020f, 0.020f), new Vector2(0.980f, 0.842f));
            var image = sheet.GetComponent<Image>();
            image.color = backdrop;
            image.raycastTarget = true;
            var eat = sheet.gameObject.AddComponent<Button>();
            eat.transition = Selectable.Transition.None;
        }

        private void BuildHeader()
        {
            RectTransform band = Art(_body, "Serit", ribbon,
                                     new Vector2(0.330f, 0.850f), new Vector2(0.670f, 0.992f));
            _titleLabel = UiBuild.Label(Slot(band, "Yazi", new Vector2(0.10f, 0.547f), new Vector2(0.90f, 0.807f)),
                                        "Text", Loc.T("senlik.baslik"), 36, TextAnchor.MiddleCenter);
            Fit(_titleLabel, 20, 36);

            Button close = UiBuild.Btn(_root, "Kapat", string.Empty,
                                       closeIcon != null ? closeIcon : UiSkin.ButtonGrey,
                                       track, 34, Hide);
            var closeImage = close.GetComponent<Image>();
            closeImage.type = Image.Type.Simple;
            closeImage.preserveAspect = true;
            UiBuild.Anchor((RectTransform)close.transform,
                           new Vector2(0.878f, 0.873f), new Vector2(0.938f, 0.970f));

            _clockLabel = UiBuild.Label(Slot(_body, "Saat", new Vector2(0.060f, 0.775f), new Vector2(0.620f, 0.832f)),
                                        "Text", string.Empty, 28, TextAnchor.MiddleLeft);
            _clockLabel.color = Paper;

            _claimAllBtn = UiBuild.Btn(_body, "HepsiniAl", string.Empty,
                                       actionButton != null ? actionButton : UiSkin.ButtonGreen,
                                       new Color(0.24f, 0.68f, 0.36f, 1f), 26, ClaimAll);
            UiBuild.Anchor((RectTransform)_claimAllBtn.transform,
                           new Vector2(0.640f, 0.772f), new Vector2(0.945f, 0.836f));
            PillFit.Wrap(_claimAllBtn.GetComponent<Image>());
            _claimAllText = _claimAllBtn.GetComponentInChildren<Text>();
            Fit(_claimAllText, 14, 26);
        }

        /// <summary>The week, as seven chips. Tapping one looks at that day; it never claims.</summary>
        private void BuildDayStrip()
        {
            const float left = 0.050f, right = 0.950f, gap = 0.006f;
            float w = (right - left) / FoundryFestival.Days;

            for (int d = 0; d < FoundryFestival.Days; d++)
            {
                RectTransform chip = Chip(_body, "Gun" + d,
                                          new Vector2(left + d * w + gap, 0.700f),
                                          new Vector2(left + (d + 1) * w - gap, 0.762f));
                _dayChip[d] = chip.GetComponent<Image>();
                _dayChip[d].raycastTarget = true;

                _dayText[d] = UiBuild.Label(Slot(chip, "Yazi", new Vector2(0.05f, 0f), new Vector2(0.95f, 1f)),
                                            "Text", (d + 1).ToString(), 28, TextAnchor.MiddleCenter);
                _dayText[d].color = Paper;

                int captured = d;
                var pick = chip.gameObject.AddComponent<Button>();
                pick.transition = Selectable.Transition.None;
                pick.onClick.AddListener(() => { _day = captured; Refresh(); });
            }
        }

        private void BuildTask(int index, Vector2 aMin, Vector2 aMax)
        {
            RectTransform c = Art(_body, "Gorev" + index, cardPanel, aMin, aMax);
            if (cardPanel == null) c.GetComponent<Image>().color = card;

            _taskText[index] = UiBuild.Label(Slot(c, "Yazi", new Vector2(0.045f, 0.60f), new Vector2(0.665f, 0.93f)),
                                             "Text", string.Empty, 30, TextAnchor.MiddleLeft);
            _taskText[index].color = Ink;
            Fit(_taskText[index], 16, 30);

            _taskFillImage[index] = Bar(c, new Vector2(0.045f, 0.42f), new Vector2(0.665f, 0.56f), taskFill);

            RectTransform odul = Slot(c, "Odul", new Vector2(0.045f, 0.10f), new Vector2(0.665f, 0.36f));
            Icon(odul, "Elmas", gemIcon, new Vector2(0f, 0.08f), new Vector2(0.10f, 0.92f));
            _taskReward[index] = UiBuild.Label(Slot(odul, "Yazi", new Vector2(0.12f, 0f), new Vector2(1f, 1f)),
                                               "Text", string.Empty, 26, TextAnchor.MiddleLeft);
            _taskReward[index].color = InkSoft;

            int captured = index;
            _taskBtn[index] = UiBuild.Btn(c, "Al", string.Empty,
                                          actionButton != null ? actionButton : UiSkin.ButtonGreen,
                                          new Color(0.24f, 0.68f, 0.36f, 1f), 26,
                                          () => ClaimTask(captured));
            UiBuild.Anchor((RectTransform)_taskBtn[index].transform,
                           new Vector2(0.700f, 0.365f), new Vector2(0.972f, 0.635f));
            PillFit.Wrap(_taskBtn[index].GetComponent<Image>());
            _taskBtnText[index] = _taskBtn[index].GetComponentInChildren<Text>();
        }

        private void BuildChests()
        {
            _chestCaption = UiBuild.Label(Slot(_body, "SandikBaslik", new Vector2(0.060f, 0.330f),
                                               new Vector2(0.940f, 0.380f)),
                                          "Text", Loc.T("senlik.sandiklar"), 26, TextAnchor.MiddleLeft);
            _chestCaption.color = new Color(1f, 1f, 1f, 0.82f);

            _pointsLabel = UiBuild.Label(Slot(_body, "Puan", new Vector2(0.060f, 0.330f), new Vector2(0.940f, 0.380f)),
                                         "Text", string.Empty, 26, TextAnchor.MiddleRight);
            _pointsLabel.color = new Color(1f, 1f, 1f, 0.82f);

            _pointsFill = Bar(_body, new Vector2(0.060f, 0.286f), new Vector2(0.940f, 0.324f), pointFill);

            const float left = 0.045f, right = 0.955f, gap = 0.008f;
            float w = (right - left) / FoundryFestival.MilestoneCount;

            for (int i = 0; i < FoundryFestival.MilestoneCount; i++)
            {
                RectTransform box = Art(_body, "Sandik" + i, cardPanel,
                                        new Vector2(left + i * w + gap, 0.090f),
                                        new Vector2(left + (i + 1) * w - gap, 0.262f));
                if (cardPanel == null) box.GetComponent<Image>().color = card;
                _chest[i] = box.GetComponent<Image>();
                _chest[i].raycastTarget = true;

                _chestText[i] = UiBuild.Label(Slot(box, "Yazi", new Vector2(0.06f, 0.08f), new Vector2(0.94f, 0.92f)),
                                              "Text", string.Empty, 24, TextAnchor.MiddleCenter);
                _chestText[i].color = Ink;
                Fit(_chestText[i], 13, 24);

                int captured = i;
                _chestBtn[i] = box.gameObject.AddComponent<Button>();
                _chestBtn[i].transition = Selectable.Transition.None;
                _chestBtn[i].onClick.AddListener(() => ClaimMilestone(captured));
            }
        }

        // ------------------------------------------------------------------ claim
        private void ClaimTask(int index)
        {
            if (_festival == null) return;
            if (_festival.ClaimTask(FoundryFestival.TaskSlot(_day, index))) Ping();
            Refresh();
        }

        private void ClaimMilestone(int index)
        {
            if (_festival == null) return;
            if (_festival.ClaimMilestone(index)) Ping();
            Refresh();
        }

        private void ClaimAll()
        {
            if (_festival == null) return;
            if (_festival.ClaimAll() > 0) Ping();
            Refresh();
        }

        // ---------------------------------------------------------------- refresh
        private void Refresh()
        {
            if (_root == null || !_root.gameObject.activeSelf) return;

            bool has = _festival != null && _festival.Available;
            if (_emptyLabel != null && _emptyLabel.gameObject.activeSelf != !has)
                _emptyLabel.gameObject.SetActive(!has);
            if (_body != null && _body.gameObject.activeSelf != has) _body.gameObject.SetActive(has);
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
                    _clockLabel.text = Loc.T("etkinlik.kalan")
                                       + " " + HudUI.LongClock(_festival.SecondsLeft);
                    break;
                case LiveEvents.Phase.Upcoming:
                    _clockLabel.text = Loc.T("etkinlik.basliyor")
                                       + " " + HudUI.LongClock(_festival.SecondsUntilStart);
                    break;
                default:
                    _clockLabel.text = Loc.T("senlik.bitti");
                    break;
            }

            int pending = _festival.PendingCount();
            _claimAllText.text = pending > 0
                ? Loc.T("senlik.hepsini_al") + " ×" + pending
                : Loc.T("senlik.hepsini_al");
            Dress(_claimAllBtn, pending > 0);
        }

        private void RefreshDay(int day)
        {
            bool unlocked = _festival.TaskUnlocked(FoundryFestival.TaskSlot(day, 0));
            _dayChip[day].color = day == _day ? dayOpen : unlocked ? dayPast : dayLocked;
            _dayText[day].color = unlocked || day == _day ? Paper : InkFaint;
        }

        private void RefreshTask(int index)
        {
            int slot = FoundryFestival.TaskSlot(_day, index);
            FoundryFestival.Task t = _festival.TaskAt(slot);
            bool unlocked = _festival.TaskUnlocked(slot);
            bool claimed = _festival.TaskClaimed(slot);
            long have = _festival.TaskProgress(slot);

            _taskText[index].text = unlocked
                ? string.Format("{0}   {1} / {2}", MetricName(t.Metric), have, t.Target)
                : string.Format("{0}   ·   {1}", MetricName(t.Metric), Loc.T("senlik.kilitli"));
            _taskText[index].color = claimed ? InkFaint : unlocked ? Ink : InkFaint;

            Progress(_taskFillImage[index], unlocked ? Goals.Progress(have, t.Target) : 0f);
            _taskReward[index].text = RewardLine(t.Gems, t.Cards, 0L);

            _taskBtnText[index].text = claimed ? Loc.T("gorev.alindi") : Loc.T("gorev.al");
            Dress(_taskBtn[index], _festival.CanClaimTask(slot));
        }

        private void RefreshChests()
        {
            int points = _festival.Points;
            int next = _festival.NextMilestonePoints;

            _pointsLabel.text = next > 0
                ? string.Format("{0} / {1} {2}", points, next, Loc.T("senlik.puan"))
                : string.Format("{0} {1}", points, Loc.T("senlik.puan"));
            Progress(_pointsFill, next > 0 ? Goals.Progress(points, next) : 1f);

            for (int i = 0; i < FoundryFestival.MilestoneCount; i++)
            {
                FoundryFestival.Milestone m = _festival.MilestoneAt(i);
                bool claimed = _festival.MilestoneClaimed(i);
                bool ready = _festival.CanClaimMilestone(i);

                _chestText[i].text = claimed
                    ? Loc.T("gorev.alindi")
                    : string.Format("{0}\n{1}", m.Points, RewardLine(m.Gems, m.Cards, m.Charts));
                _chestText[i].color = claimed ? InkFaint : Ink;
                _chest[i].color = ready ? Color.white
                                        : claimed ? new Color(0.80f, 0.83f, 0.88f, 1f)
                                                  : new Color(0.72f, 0.75f, 0.80f, 1f);
                _chestBtn[i].interactable = ready;
            }
        }

        // ------------------------------------------------------------------ pieces
        // The same handful GoalsUI and LiveEventsUI keep, local for the same reason theirs are.

        private static string RewardLine(long gems, int cards, long charts)
        {
            string line = gems.ToString();
            if (cards > 0) line += "   +" + cards + " " + Loc.T("ustabasi.kart");
            if (charts > 0L) line += "   +" + charts + " " + Loc.T("kaptan.harita");
            return line;
        }

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

        private static void Dress(Button b, bool live)
        {
            b.interactable = live;
            b.GetComponent<Image>().color = live ? Color.white : new Color(0.72f, 0.75f, 0.80f, 1f);
        }

        private static void Progress(Image fill, float t)
            => ((RectTransform)fill.transform).anchorMax = new Vector2(Mathf.Clamp01(t), 1f);

        private static void Ping() => ServiceLocator.Get<HapticService>()?.Medium();

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

        private static void Fit(Text label, int min, int max)
        {
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = min;
            label.resizeTextMaxSize = max;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
        }

        /// <summary>The capsule bar — see GoalsUI.Bar for why the fill is a width rather than an
        /// Image.Type.Filled draw.</summary>
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

        private static RectTransform Slot(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
        }
    }
}
