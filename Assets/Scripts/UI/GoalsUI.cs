using Game.Core;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>Tabbed presentation over GoalService. This class never grants rewards.</summary>
    public sealed class GoalsUI : MonoBehaviour
    {
        public enum Tab { Daily, Weekly, Achievements }

        [SerializeField] private int sortingOrder = 106;
        [Header("Görseller")]
        [SerializeField] private Sprite cardPanel;
        [SerializeField] private Sprite ribbon;
        [SerializeField] private Sprite actionButton;
        [SerializeField] private Sprite closeIcon;
        [SerializeField] private Sprite barTrack;
        [SerializeField] private Sprite barFill;
        [SerializeField] private Sprite chipPill;
        [SerializeField] private Sprite gemIcon;
        [Header("Renkler")]
        [SerializeField] private Color scrim = new Color(0.04f, 0.05f, 0.08f, 0.86f);
        [SerializeField] private Color card = new Color(0.16f, 0.19f, 0.27f, 1f);
        [SerializeField] private Color track = new Color(0.10f, 0.11f, 0.16f, 1f);
        [SerializeField] private Color dailyFill = new Color(0.35f, 0.72f, 0.98f, 1f);
        [SerializeField] private Color weeklyFill = new Color(0.37f, 0.82f, 0.55f, 1f);
        [SerializeField] private Color ladderFill = new Color(0.98f, 0.74f, 0.24f, 1f);

        private const string OpenerIconResource = "UI/Buttons/gorev";
        private const float RibbonBand = 0.677f;
        private static readonly Color Ink = new Color(0.09f, 0.14f, 0.24f, 1f);
        private static readonly Color InkFaint = new Color(0.58f, 0.63f, 0.71f, 1f);
        private static readonly Color Paper = new Color(0.96f, 0.97f, 1f, 1f);

        private GoalService _goals;
        private LocalizationService _loc;
        private RectTransform _root;
        private Text _titleLabel, _pendingLabel, _claimAllLabel;
        private Text _weeklyTaskCaption, _weeklyRewardCaption;
        private RectTransform _pendingChip;
        private Button _claimAllButton;
        private readonly Button[] _tabButtons = new Button[3];
        private readonly Text[] _tabLabels = new Text[3];
        private readonly RectTransform[] _pages = new RectTransform[3];
        private Tab _selected;
        private RewardRevealUI _reveal;
        private TMP_Text _openerCount;
        private GameObject _openerChip;

        private readonly Text[] _dailyText = new Text[Goals.DailySlots];
        private readonly Text[] _dailyReward = new Text[Goals.DailySlots];
        private readonly Image[] _dailyFillImage = new Image[Goals.DailySlots];
        private readonly Button[] _dailyBtn = new Button[Goals.DailySlots];
        private readonly Text[] _dailyBtnText = new Text[Goals.DailySlots];
        private readonly Text[] _weeklyTaskText = new Text[Goals.WeeklySlots];
        private readonly Image[] _weeklyTaskFill = new Image[Goals.WeeklySlots];
        private readonly Text[] _weeklyMilestoneText = new Text[Goals.WeeklyMilestones.Length];
        private readonly Button[] _weeklyMilestoneBtn = new Button[Goals.WeeklyMilestones.Length];
        private readonly Text[] _weeklyMilestoneBtnText = new Text[Goals.WeeklyMilestones.Length];
        private readonly Text[] _ladderText = new Text[Goals.Ladder.Length];
        private readonly Image[] _ladderFillImage = new Image[Goals.Ladder.Length];
        private readonly Button[] _ladderBtn = new Button[Goals.Ladder.Length];
        private readonly Text[] _ladderBtnText = new Text[Goals.Ladder.Length];

        private void Awake()
        {
            _goals = ServiceLocator.Get<GoalService>();
            Build();
            BuildOpener();
            if (_goals != null) _goals.Changed += OnChanged;
            _loc = ServiceLocator.Get<LocalizationService>();
            if (_loc != null) _loc.Changed += OnLanguageChanged;
            Hide();
            RefreshOpener();
        }

        private void OnDestroy()
        {
            if (_goals != null) _goals.Changed -= OnChanged;
            if (_loc != null) _loc.Changed -= OnLanguageChanged;
        }

        private void OnLanguageChanged()
        {
            _titleLabel.text = Loc.T("gorev.baslik");
            _tabLabels[0].text = Loc.T("gorev.sekme.gunluk");
            _tabLabels[1].text = Loc.T("gorev.sekme.haftalik");
            _tabLabels[2].text = Loc.T("gorev.sekme.basarim");
            _claimAllLabel.text = Loc.T("gorev.hepsini_al");
            _weeklyTaskCaption.text = Loc.T("gorev.haftalik_gorevler");
            _weeklyRewardCaption.text = Loc.T("gorev.haftalik_oduller");
            Refresh();
            RefreshOpener();
        }

        private void OnChanged() { Refresh(); RefreshOpener(); }
        public void Show() => Show(Tab.Daily);

        /// <summary>Deep-link entry point. Unknown targets safely open the daily page.</summary>
        public void Show(string target)
        {
            if (target == "weekly" || target == "goals:weekly") Show(Tab.Weekly);
            else if (target == "achievements" || target == "goals:achievements") Show(Tab.Achievements);
            else Show(Tab.Daily);
        }

        public void Show(Tab tab)
        {
            if (_root != null) _root.gameObject.SetActive(true);
            SelectTab(tab);
        }

        public void Hide() { if (_root != null) _root.gameObject.SetActive(false); }

        private void Build()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "GorevKanvas", sortingOrder);
            _root = UiBuild.Flat(canvas, "Karartma", scrim, Vector2.zero, Vector2.one);
            BuildHeader();
            BuildTabs();
            _pages[0] = Page("GunlukSayfa");
            _pages[1] = Page("HaftalikSayfa");
            _pages[2] = Page("BasarimSayfa");
            BuildDailyPage(_pages[0]);
            BuildWeeklyPage(_pages[1]);
            BuildAchievementsPage(_pages[2]);
            _reveal = RewardRevealUI.Create(_root, cardPanel, gemIcon);
        }

        private void BuildHeader()
        {
            RectTransform band = Art(_root, "Serit", ribbon,
                new Vector2(0.36f, 0.87f), new Vector2(0.64f, 0.995f));
            _titleLabel = UiBuild.Label(Slot(band, "Yazi", new Vector2(0.13f, RibbonBand - 0.13f),
                new Vector2(0.87f, RibbonBand + 0.13f)), "Text", Loc.T("gorev.baslik"), 38,
                TextAnchor.MiddleCenter);
            _claimAllButton = UiBuild.Btn(_root, "HepsiniAl", Loc.T("gorev.hepsini_al"),
                actionButton != null ? actionButton : UiSkin.ButtonGreen,
                new Color(0.24f, 0.68f, 0.36f, 1f), 24, ClaimAll);
            UiBuild.Anchor((RectTransform)_claimAllButton.transform,
                new Vector2(0.035f, 0.895f), new Vector2(0.235f, 0.955f));
            PillFit.Wrap(_claimAllButton.GetComponent<Image>());
            _claimAllLabel = _claimAllButton.GetComponentInChildren<Text>();
            _pendingChip = Chip(_root, "Bekleyen", new Vector2(0.25f, 0.895f), new Vector2(0.34f, 0.955f));
            _pendingLabel = UiBuild.Label(_pendingChip, "Yazi", string.Empty, 26, TextAnchor.MiddleCenter);
            _pendingLabel.color = Paper;
            Button close = UiBuild.Btn(_root, "Kapat", string.Empty,
                closeIcon != null ? closeIcon : UiSkin.ButtonGrey, track, 34, Hide);
            Image image = close.GetComponent<Image>();
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            UiBuild.Anchor((RectTransform)close.transform,
                new Vector2(0.878f, 0.887f), new Vector2(0.938f, 0.975f));
        }

        private void BuildTabs()
        {
            string[] keys = { "gorev.sekme.gunluk", "gorev.sekme.haftalik", "gorev.sekme.basarim" };
            for (int i = 0; i < 3; i++)
            {
                int captured = i;
                float left = 0.035f + i * 0.31f;
                _tabButtons[i] = UiBuild.Btn(_root, "Sekme_" + i, Loc.T(keys[i]),
                    actionButton != null ? actionButton : UiSkin.ButtonGrey, track, 24,
                    () => SelectTab((Tab)captured));
                UiBuild.Anchor((RectTransform)_tabButtons[i].transform,
                    new Vector2(left, 0.795f), new Vector2(left + 0.285f, 0.855f));
                PillFit.Wrap(_tabButtons[i].GetComponent<Image>());
                _tabLabels[i] = _tabButtons[i].GetComponentInChildren<Text>();
                Fit(_tabLabels[i], 16, 24);
            }
        }

        private RectTransform Page(string name)
            => Slot(_root, name, new Vector2(0.02f, 0.025f), new Vector2(0.98f, 0.78f));

        private void BuildDailyPage(RectTransform page)
        {
            float height = 0.94f / Goals.DailySlots;
            for (int i = 0; i < Goals.DailySlots; i++)
            {
                RectTransform row = Card(page, "Gunluk_" + i,
                    new Vector2(0.02f, 0.97f - (i + 1) * height + 0.012f),
                    new Vector2(0.98f, 0.97f - i * height - 0.012f));
                _dailyText[i] = RowText(row, new Vector2(0.035f, 0.58f), new Vector2(0.69f, 0.91f), 30);
                _dailyFillImage[i] = Bar(row, new Vector2(0.035f, 0.39f), new Vector2(0.69f, 0.54f), dailyFill);
                _dailyReward[i] = RowText(row, new Vector2(0.10f, 0.09f), new Vector2(0.69f, 0.34f), 25);
                Icon(row, "Elmas", gemIcon, new Vector2(0.035f, 0.10f), new Vector2(0.09f, 0.33f));
                int captured = i;
                _dailyBtn[i] = ClaimButton(row, () => ClaimDaily(captured), out _dailyBtnText[i]);
            }
        }

        private void BuildWeeklyPage(RectTransform page)
        {
            _weeklyTaskCaption = Caption(page, "HaftalikGorev", Loc.T("gorev.haftalik_gorevler"), 0.02f, 0.49f);
            _weeklyRewardCaption = Caption(page, "HaftalikOdul", Loc.T("gorev.haftalik_oduller"), 0.51f, 0.98f);
            float height = 0.84f / Goals.WeeklySlots;
            for (int i = 0; i < Goals.WeeklySlots; i++)
            {
                RectTransform row = Card(page, "HaftalikGorev_" + i,
                    new Vector2(0.02f, 0.88f - (i + 1) * height + 0.01f),
                    new Vector2(0.49f, 0.88f - i * height - 0.01f));
                _weeklyTaskText[i] = RowText(row, new Vector2(0.04f, 0.46f), new Vector2(0.96f, 0.91f), 25);
                _weeklyTaskFill[i] = Bar(row, new Vector2(0.04f, 0.16f), new Vector2(0.96f, 0.36f), weeklyFill);
            }
            for (int i = 0; i < Goals.WeeklyMilestones.Length; i++)
            {
                RectTransform row = Card(page, "HaftalikOdul_" + i,
                    new Vector2(0.51f, 0.88f - (i + 1) * height + 0.01f),
                    new Vector2(0.98f, 0.88f - i * height - 0.01f));
                _weeklyMilestoneText[i] = RowText(row, new Vector2(0.04f, 0.18f), new Vector2(0.67f, 0.85f), 24);
                int captured = i;
                _weeklyMilestoneBtn[i] = ClaimButton(row, () => ClaimWeekly(captured), out _weeklyMilestoneBtnText[i]);
            }
        }

        private void BuildAchievementsPage(RectTransform page)
        {
            const int rows = 3;
            float height = 0.96f / rows;
            for (int i = 0; i < Goals.Ladder.Length; i++)
            {
                int column = i / rows, rowIndex = i % rows;
                float left = column == 0 ? 0.02f : 0.51f;
                float right = column == 0 ? 0.49f : 0.98f;
                RectTransform row = Card(page, "Basarim_" + i,
                    new Vector2(left, 0.98f - (rowIndex + 1) * height + 0.012f),
                    new Vector2(right, 0.98f - rowIndex * height - 0.012f));
                _ladderText[i] = RowText(row, new Vector2(0.04f, 0.52f), new Vector2(0.70f, 0.92f), 24);
                _ladderFillImage[i] = Bar(row, new Vector2(0.04f, 0.18f), new Vector2(0.70f, 0.40f), ladderFill);
                int captured = i;
                _ladderBtn[i] = ClaimButton(row, () => ClaimAchievement(captured), out _ladderBtnText[i]);
            }
        }

        private void SelectTab(Tab tab)
        {
            _selected = tab;
            for (int i = 0; i < 3; i++)
            {
                bool selected = i == (int)tab;
                _pages[i].gameObject.SetActive(selected);
                _tabButtons[i].GetComponent<Image>().color = selected
                    ? Color.white : new Color(0.58f, 0.62f, 0.70f, 1f);
            }
            Refresh();
        }

        private void ClaimDaily(int slot)
        {
            if (_goals != null && _goals.ClaimDaily(slot, out GoalService.ClaimReceipt receipt))
                _reveal.Present(receipt);
        }

        private void ClaimWeekly(int index)
        {
            if (_goals != null && _goals.ClaimWeeklyMilestone(index, out GoalService.ClaimReceipt receipt))
                _reveal.Present(receipt);
        }

        private void ClaimAchievement(int index)
        {
            if (_goals != null && _goals.ClaimAchievement(index, out GoalService.ClaimReceipt receipt))
                _reveal.Present(receipt);
        }

        private void ClaimAll()
        {
            if (_goals != null && _goals.ClaimAll(out GoalService.ClaimReceipt receipt))
                _reveal.Present(receipt);
        }

        private void Refresh()
        {
            if (_goals == null || _root == null || !_root.gameObject.activeSelf) return;
            int pending = _goals.PendingCount();
            _pendingChip.gameObject.SetActive(pending > 0);
            if (pending > 0) _pendingLabel.text = pending.ToString();
            Dress(_claimAllButton, pending > 0, false);
            if (_selected == Tab.Daily)
                for (int i = 0; i < Goals.DailySlots; i++) RefreshDaily(i);
            else if (_selected == Tab.Weekly) RefreshWeekly();
            else for (int i = 0; i < Goals.Ladder.Length; i++) RefreshLadder(i);
        }

        private void RefreshDaily(int slot)
        {
            Goals.Task task = _goals.DailyTask(slot);
            long have = _goals.DailyProgress(slot);
            bool claimed = _goals.DailyClaimed(slot), ready = _goals.CanClaimDaily(slot);
            _dailyText[slot].text = string.Format("{0}   {1} / {2}", MetricName(task.Metric), have, task.Target);
            _dailyText[slot].color = claimed ? InkFaint : Ink;
            Progress(_dailyFillImage[slot], Goals.Progress(have, task.Target));
            _dailyReward[slot].text = RewardLine(task.Gems, task.Cards);
            _dailyBtnText[slot].text = StateText(ready, claimed);
            Dress(_dailyBtn[slot], ready, claimed);
        }

        private void RefreshWeekly()
        {
            for (int i = 0; i < Goals.WeeklySlots; i++)
            {
                Goals.WeeklyTask task = Goals.WeeklyTasks[i];
                long have = _goals.WeeklyProgress(i);
                bool done = _goals.WeeklyDone(i);
                _weeklyTaskText[i].text = string.Format("{0}   {1}/{2}   ·   +{3} {4}",
                    MetricName(task.Metric), have, task.Target, task.Points, Loc.T("gorev.puan"));
                _weeklyTaskText[i].color = done ? InkFaint : Ink;
                Progress(_weeklyTaskFill[i], Goals.Progress(have, task.Target));
            }
            int points = _goals.WeeklyPoints();
            for (int i = 0; i < Goals.WeeklyMilestones.Length; i++)
            {
                Goals.WeeklyMilestone milestone = Goals.WeeklyMilestones[i];
                bool claimed = _goals.WeeklyMilestoneClaimed(i), ready = _goals.CanClaimWeeklyMilestone(i);
                _weeklyMilestoneText[i].text = string.Format("{0}/{1} {2}\n{3}", points,
                    milestone.Points, Loc.T("gorev.puan"), RewardLine(milestone.Gems, milestone.Cards));
                _weeklyMilestoneText[i].color = claimed ? InkFaint : Ink;
                _weeklyMilestoneBtnText[i].text = StateText(ready, claimed);
                Dress(_weeklyMilestoneBtn[i], ready, claimed);
            }
        }

        private void RefreshLadder(int index)
        {
            Goals.Achievement achievement = Goals.Ladder[index];
            long total = _goals.Lifetime(achievement.Metric);
            int reached = Goals.TiersReached(achievement, total);
            long next = Goals.NextTier(achievement, total);
            int owed = _goals.UnclaimedTiers(index);
            _ladderText[index].text = next > 0L
                ? string.Format("{0}\n{1} {2}/{3}   ·   {4}/{5}", MetricName(achievement.Metric),
                    Loc.T("gorev.kademe"), reached, achievement.Tiers.Length, total, next)
                : string.Format("{0}\n{1}", MetricName(achievement.Metric), Loc.T("gorev.tamamlandi"));
            long from = reached > 0 ? achievement.Tiers[reached - 1] : 0L;
            Progress(_ladderFillImage[index], next > 0L ? Goals.Progress(total - from, next - from) : 1f);
            _ladderBtnText[index].text = owed > 0
                ? string.Format("{0} ×{1}", Loc.T("gorev.al"), owed) : Loc.T("gorev.kilitli");
            Dress(_ladderBtn[index], owed > 0, false);
        }

        private static string StateText(bool ready, bool claimed)
            => claimed ? Loc.T("gorev.alindi") : Loc.T(ready ? "gorev.al" : "gorev.kilitli");

        private static void Dress(Button button, bool ready, bool claimed)
        {
            button.interactable = ready;
            button.GetComponent<Image>().color = ready ? Color.white : claimed
                ? new Color(0.52f, 0.70f, 0.60f, 1f) : new Color(0.66f, 0.69f, 0.75f, 1f);
        }

        private Button ClaimButton(RectTransform parent, UnityEngine.Events.UnityAction action, out Text label)
        {
            Button button = UiBuild.Btn(parent, "Al", string.Empty,
                actionButton != null ? actionButton : UiSkin.ButtonGreen,
                new Color(0.24f, 0.68f, 0.36f, 1f), 23, action);
            UiBuild.Anchor((RectTransform)button.transform, new Vector2(0.72f, 0.29f), new Vector2(0.97f, 0.70f));
            PillFit.Wrap(button.GetComponent<Image>());
            label = button.GetComponentInChildren<Text>();
            Fit(label, 14, 23);
            return button;
        }

        private RectTransform Card(RectTransform parent, string name, Vector2 min, Vector2 max)
        {
            RectTransform result = Art(parent, name, cardPanel, min, max);
            if (cardPanel == null) result.GetComponent<Image>().color = card;
            return result;
        }

        private static Text RowText(RectTransform parent, Vector2 min, Vector2 max, int size)
        {
            Text text = UiBuild.Label(Slot(parent, "Yazi", min, max), "Text", string.Empty, size,
                TextAnchor.MiddleLeft);
            text.color = Ink;
            Fit(text, 14, size);
            return text;
        }

        private static Text Caption(RectTransform parent, string name, string value, float left, float right)
        {
            Text text = UiBuild.Label(Slot(parent, name, new Vector2(left, 0.90f), new Vector2(right, 0.99f)),
                "Text", value, 25, TextAnchor.MiddleLeft);
            text.color = Paper;
            Fit(text, 15, 25);
            return text;
        }

        private static RectTransform Art(RectTransform parent, string name, Sprite sprite, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.sprite = sprite != null ? sprite : UiSkin.Panel;
            image.type = sprite != null && sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            image.preserveAspect = image.type == Image.Type.Simple;
            image.color = Color.white;
            image.raycastTarget = false;
            return UiBuild.Anchor((RectTransform)go.transform, min, max);
        }

        private RectTransform Chip(RectTransform parent, string name, Vector2 min, Vector2 max)
        {
            Sprite art = chipPill != null ? chipPill : cardPanel;
            RectTransform result = Art(parent, name, art, min, max);
            Image image = result.GetComponent<Image>();
            if (art != null) { image.type = Image.Type.Sliced; image.preserveAspect = false; PillFit.Wrap(image); }
            return result;
        }

        private static Image Icon(RectTransform parent, string name, Sprite sprite, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.enabled = sprite != null;
            UiBuild.Anchor((RectTransform)go.transform, min, max);
            return image;
        }

        private Image Bar(RectTransform parent, Vector2 min, Vector2 max, Color fallback)
        {
            RectTransform bed = Art(parent, "Cubuk", barTrack, min, max);
            Image bedImage = bed.GetComponent<Image>();
            bedImage.type = Image.Type.Sliced;
            bedImage.preserveAspect = false;
            PillFit.Wrap(bedImage);
            if (barTrack == null) bedImage.color = track;
            RectTransform area = Slot(bed, "DolguAlani", Vector2.zero, Vector2.one);
            area.offsetMin = new Vector2(3f, 3f);
            area.offsetMax = new Vector2(-3f, -3f);
            var go = new GameObject("Dolgu", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(area, false);
            Image image = go.GetComponent<Image>();
            image.sprite = barFill;
            image.type = Image.Type.Sliced;
            image.raycastTarget = false;
            if (barFill == null) image.color = fallback;
            UiBuild.Anchor((RectTransform)go.transform, Vector2.zero, new Vector2(0f, 1f));
            PillFit.Wrap(image);
            return image;
        }

        private static void Progress(Image fill, float value)
            => ((RectTransform)fill.transform).anchorMax = new Vector2(Mathf.Clamp01(value), 1f);

        private static void Fit(Text label, int min, int max)
        {
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = min;
            label.resizeTextMaxSize = max;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private static RectTransform Slot(RectTransform parent, string name, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return UiBuild.Anchor((RectTransform)go.transform, min, max);
        }

        private void BuildOpener()
        {
            HudUI hud = FindAnyObjectByType<HudUI>(FindObjectsInactive.Include);
            if (hud == null) return;
            Button open = hud.AttachBottomButton(0, "BtnGorev", Resources.Load<Sprite>(OpenerIconResource), Show);
            if (open == null) return;
            _openerChip = hud.AttachCounterChip(open);
            if (_openerChip != null) _openerCount = _openerChip.GetComponentInChildren<TMP_Text>(true);
        }

        private void RefreshOpener()
        {
            if (_openerChip == null || _goals == null) return;
            int pending = _goals.PendingCount();
            _openerChip.SetActive(pending > 0);
            if (pending > 0) _openerCount.text = pending.ToString();
        }

        private static string RewardLine(long gems, int cards)
            => cards > 0 ? string.Format("{0} ◆   +{1} {2}", gems, cards, Loc.T("ustabasi.kart"))
                         : string.Format("{0} ◆", gems);

        private static string MetricName(int metric)
        {
            switch (metric)
            {
                case Goals.BarsSold: return Loc.T("gorev.metrik.kulce");
                case Goals.Upgrades: return Loc.T("gorev.metrik.yukseltme");
                case Goals.Contracts: return Loc.T("gorev.metrik.kontrat");
                case Goals.Repairs: return Loc.T("gorev.metrik.onarim");
                case Goals.Islands: return Loc.T("gorev.metrik.ada");
                case Goals.ForemanLevels: return Loc.T("gorev.metrik.ustabasi");
                default: return string.Empty;
            }
        }
    }
}
