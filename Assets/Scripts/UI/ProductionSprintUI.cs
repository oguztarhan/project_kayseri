using Game.Core;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>Code-built action and personal-milestone screen for Production Sprint.</summary>
    public sealed class ProductionSprintUI : MonoBehaviour
    {
        [SerializeField] private int sortingOrder = 113;
        [SerializeField] private Sprite cardPanel;
        [SerializeField] private Sprite ribbon;
        [SerializeField] private Sprite actionButton;
        [SerializeField] private Sprite closeIcon;
        [SerializeField] private Sprite gemIcon;

        private const int Rows = ProductionSprint.MilestoneCount;

        private static readonly Color Ink = new Color(0.09f, 0.14f, 0.24f, 1f);
        private static readonly Color Soft = new Color(0.34f, 0.40f, 0.50f, 1f);
        private static readonly Color Blue = new Color(0.18f, 0.58f, 0.88f, 1f);
        private static readonly Color Gold = new Color(0.95f, 0.67f, 0.18f, 1f);
        private static readonly Color Green = new Color(0.25f, 0.72f, 0.42f, 1f);
        private static readonly Color Disabled = new Color(0.48f, 0.52f, 0.58f, 1f);

        private ProductionSprintService _sprint;
        private LocalizationService _loc;
        private RectTransform _root;
        private Text _title;
        private Text _clock;
        private Text _score;
        private readonly Button[] _tabs = new Button[2];
        private readonly Text[] _tabLabels = new Text[2];
        private readonly RectTransform[] _rows = new RectTransform[Rows];
        private readonly Text[] _rowTitles = new Text[Rows];
        private readonly Text[] _rowDetails = new Text[Rows];
        private readonly RectTransform[] _fills = new RectTransform[Rows];
        private readonly Button[] _claimButtons = new Button[Rows];
        private readonly Text[] _claimLabels = new Text[Rows];
        private RewardRevealUI _reveal;
        private int _tab;
        private float _tick;

        private void Awake()
        {
            _sprint = ServiceLocator.Get<ProductionSprintService>();
            _loc = ServiceLocator.Get<LocalizationService>();
            Build();
            if (_sprint != null) _sprint.Changed += Refresh;
            if (_loc != null) _loc.Changed += Refresh;
            Hide();
        }

        private void OnDestroy()
        {
            if (_sprint != null) _sprint.Changed -= Refresh;
            if (_loc != null) _loc.Changed -= Refresh;
        }

        public void Show()
        {
            if (_root == null) return;
            _root.gameObject.SetActive(true);
            _tick = 0f;
            Refresh();
        }

        public void Hide()
        {
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

        private void Build()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "UretimSprintiKanvas", sortingOrder);
            _root = UiBuild.Flat(canvas, "Karartma", new Color(0.03f, 0.05f, 0.10f, 0.92f),
                Vector2.zero, Vector2.one);
            Button dismiss = _root.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(Hide);

            RectTransform sheet = Art(_root, "Zemin", cardPanel,
                new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.86f));
            sheet.GetComponent<Image>().color = new Color(0.90f, 0.95f, 0.98f, 1f);
            sheet.GetComponent<Image>().raycastTarget = true;
            sheet.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;

            RectTransform band = Art(_root, "Serit", ribbon,
                new Vector2(0.28f, 0.87f), new Vector2(0.72f, 0.985f));
            _title = UiBuild.Label(Slot(band, "Yazi", new Vector2(0.08f, 0.18f), new Vector2(0.92f, 0.86f)),
                "Text", Loc.T("sprint.baslik"), 36, TextAnchor.MiddleCenter);

            Button close = UiBuild.Btn(_root, "Kapat", string.Empty,
                closeIcon != null ? closeIcon : UiSkin.ButtonGrey, Color.white, 32, Hide);
            UiBuild.Anchor((RectTransform)close.transform,
                new Vector2(0.88f, 0.89f), new Vector2(0.95f, 0.97f));

            _clock = UiBuild.Label(Slot(sheet, "Saat", new Vector2(0.05f, 0.90f), new Vector2(0.60f, 0.98f)),
                "Text", string.Empty, 24, TextAnchor.MiddleLeft);
            _clock.color = Soft;
            _score = UiBuild.Label(Slot(sheet, "Puan", new Vector2(0.60f, 0.90f), new Vector2(0.95f, 0.98f)),
                "Text", string.Empty, 28, TextAnchor.MiddleRight);
            _score.color = Ink;

            string[] keys = { "sprint.gorevler", "sprint.kilometre" };
            for (int i = 0; i < _tabs.Length; i++)
            {
                int captured = i;
                _tabs[i] = UiBuild.Btn(sheet, "Sekme" + i, Loc.T(keys[i]), UiSkin.ButtonBlue, Blue, 25,
                    () => { _tab = captured; Refresh(); });
                UiBuild.Anchor((RectTransform)_tabs[i].transform,
                    new Vector2(0.05f + i * 0.46f, 0.81f), new Vector2(0.49f + i * 0.46f, 0.89f));
                _tabLabels[i] = _tabs[i].GetComponentInChildren<Text>();
            }

            const float top = 0.79f;
            const float bottom = 0.05f;
            float height = (top - bottom) / Rows;
            for (int i = 0; i < Rows; i++)
                BuildRow(sheet, i,
                    new Vector2(0.05f, top - (i + 1) * height + 0.007f),
                    new Vector2(0.95f, top - i * height - 0.007f));

            _reveal = RewardRevealUI.Create(_root, cardPanel, gemIcon);
        }

        private void BuildRow(RectTransform parent, int index, Vector2 min, Vector2 max)
        {
            RectTransform row = Art(parent, "Satir" + index, cardPanel, min, max);
            row.GetComponent<Image>().color = Color.white;
            _rows[index] = row;

            _rowTitles[index] = UiBuild.Label(
                Slot(row, "Baslik", new Vector2(0.025f, 0.52f), new Vector2(0.70f, 0.94f)),
                "Text", string.Empty, 23, TextAnchor.MiddleLeft);
            _rowTitles[index].color = Ink;
            _rowDetails[index] = UiBuild.Label(
                Slot(row, "Detay", new Vector2(0.025f, 0.12f), new Vector2(0.70f, 0.52f)),
                "Text", string.Empty, 19, TextAnchor.MiddleLeft);
            _rowDetails[index].color = Soft;

            UiBuild.Bar(row, "Ilerleme", new Color(0.78f, 0.82f, 0.88f, 1f), Green,
                new Vector2(0.025f, 0.05f), new Vector2(0.68f, 0.13f), out _fills[index]);

            int captured = index;
            _claimButtons[index] = UiBuild.Btn(row, "OduluAl", Loc.T("gorev.al"),
                actionButton != null ? actionButton : UiSkin.ButtonGreen, Green, 20,
                () => Claim(captured));
            UiBuild.Anchor((RectTransform)_claimButtons[index].transform,
                new Vector2(0.73f, 0.20f), new Vector2(0.965f, 0.80f));
            _claimLabels[index] = _claimButtons[index].GetComponentInChildren<Text>();
        }

        private void Claim(int index)
        {
            if (_sprint == null || _tab != 1) return;
            ProductionSprint.Reward reward = _sprint.MilestoneAt(index).Reward;
            if (_sprint.ClaimMilestone(index))
            {
                _reveal?.Present(RewardText(reward));
            }
            Refresh();
        }

        private void Refresh()
        {
            if (_root == null || !_root.gameObject.activeSelf) return;

            _title.text = Loc.T("sprint.baslik");
            _tabLabels[0].text = Loc.T("sprint.gorevler");
            _tabLabels[1].text = Loc.T("sprint.kilometre");
            for (int i = 0; i < _tabs.Length; i++)
                _tabs[i].GetComponent<Image>().color = i == _tab ? Gold : Blue;

            bool available = _sprint != null && _sprint.Available;
            _score.text = Loc.T("sprint.puan") + "  " + (available ? _sprint.Score.ToString() : "0");
            if (!available) _clock.text = Loc.T("sprint.yok");
            else if (_sprint.Phase == LiveEvents.Phase.Active)
                _clock.text = Loc.T("etkinlik.kalan") + " " + HudUI.LongClock(_sprint.SecondsLeft);
            else if (_sprint.Phase == LiveEvents.Phase.Upcoming)
                _clock.text = Loc.T("etkinlik.yakinda");
            else
                _clock.text = Loc.T("sprint.bitti");

            for (int i = 0; i < Rows; i++)
            {
                bool visible = available && (_tab == 0 ? i < ProductionSprint.RuleCount : true);
                _rows[i].gameObject.SetActive(visible);
                if (!visible) continue;
                if (_tab == 0) RefreshRule(i);
                else RefreshMilestone(i);
            }
        }

        private void RefreshRule(int index)
        {
            ProductionSprint.ScoringRule rule = _sprint.RuleAt(index);
            long progress = _sprint.RuleProgress(index);
            _rowTitles[index].text = MetricName(rule.Metric) + "  " + progress + "/" + rule.ActionLimit;
            _rowDetails[index].text = "+" + rule.PointsPerAction + " " + Loc.T("sprint.eylem_puani");
            SetFill(index, progress, rule.ActionLimit);
            _claimButtons[index].gameObject.SetActive(false);
        }

        private void RefreshMilestone(int index)
        {
            ProductionSprint.Milestone milestone = _sprint.MilestoneAt(index);
            long score = _sprint.Score;
            _rowTitles[index].text = milestone.Score + " " + Loc.T("sprint.puan");
            _rowDetails[index].text = RewardText(milestone.Reward);
            SetFill(index, score, milestone.Score);

            bool claimed = _sprint.MilestoneClaimed(index);
            _claimButtons[index].gameObject.SetActive(true);
            _claimButtons[index].interactable = _sprint.CanClaimMilestone(index);
            _claimButtons[index].GetComponent<Image>().color = _claimButtons[index].interactable ? Green : Disabled;
            _claimLabels[index].text = claimed ? Loc.T("gorev.alindi") : Loc.T("gorev.al");
        }

        private void SetFill(int index, long progress, long target)
        {
            float ratio = target <= 0L ? 1f : Mathf.Clamp01((float)(progress / (double)target));
            _fills[index].anchorMax = new Vector2(ratio, 1f);
        }

        private static string RewardText(in ProductionSprint.Reward reward)
        {
            string text = string.Empty;
            if (reward.Gems > 0L) text += "+" + reward.Gems + " ◆";
            if (reward.Cards > 0) text += Space(text) + "+" + reward.Cards + " " + Loc.T("ustabasi.kart");
            if (reward.CashMinutes > 0d)
                text += Space(text) + "+" + reward.CashMinutes.ToString("0.#") + " " + Loc.T("sprint.nakit_dakika");
            return text;
        }

        private static string Space(string text) => text.Length > 0 ? "    " : string.Empty;

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

        private static RectTransform Art(RectTransform parent, string name, Sprite sprite,
            Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.sprite = sprite != null ? sprite : UiSkin.Panel;
            image.type = image.sprite != null && image.sprite.border.sqrMagnitude > 0f
                ? Image.Type.Sliced : Image.Type.Simple;
            image.raycastTarget = false;
            return UiBuild.Anchor((RectTransform)go.transform, min, max);
        }

        private static RectTransform Slot(RectTransform parent, string name, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return UiBuild.Anchor((RectTransform)go.transform, min, max);
        }
    }
}
