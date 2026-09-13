using Game.Core;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The Production Sprint: the actions that score (with how far each has been pushed) on one tab,
    /// the personal score milestones and their rewards on the other.
    ///
    /// DRAWN FROM <see cref="EtkinlikKit"/>, the events family's shared pieces — sheet, ribbon, tabs,
    /// navy rows, capsules — and a claim shows <see cref="EtkinlikOdulUI"/>, the events family's own
    /// reward card, not the shared <see cref="RewardRevealUI"/>.
    ///
    /// THE ROWS AND TABS SIT STRAIGHT ON THE SHEET (<c>Zemin/Sekme0</c>, <c>Zemin/Satir0/OduluAl</c>):
    /// that is the shape the UI smoke test walks, and five rows fit the sheet without a scroll list.
    /// </summary>
    public sealed class ProductionSprintUI : MonoBehaviour
    {
        [SerializeField] private int sortingOrder = 113;
        [SerializeField] private Color scrim = new Color(0.03f, 0.05f, 0.10f, 1f);

        private const int Rows = ProductionSprint.MilestoneCount;
        private const int TabCount = 2;

        /// <summary>Five rows between the tabs and the sheet's bottom rail.</summary>
        private const float RowTop = 0.615f, RowBottom = 0.075f, RowGap = 0.005f;

        /// <summary>The navy card at 1/1.6 border — the events board's row.</summary>
        private const float BorderScale = 1.6f;

        private static readonly string[] TabKeys = { "sprint.gorevler", "sprint.kilometre" };

        private ProductionSprintService _sprint;
        private LocalizationService _loc;
        private RectTransform _root, _empty;
        private Text _title, _clock, _score, _emptyLabel;
        private readonly Button[] _tabs = new Button[TabCount];
        private readonly Text[] _tabLabels = new Text[TabCount];
        private readonly RectTransform[] _rows = new RectTransform[Rows];
        private readonly Image[] _rowIcons = new Image[Rows];
        private readonly Text[] _rowTitles = new Text[Rows];
        private readonly Text[] _rowCounts = new Text[Rows];
        private readonly Image[] _fills = new Image[Rows];
        private readonly EtkinlikOdulSatiri[] _rowRewards = new EtkinlikOdulSatiri[Rows];
        private readonly Text[] _rowEarned = new Text[Rows];
        private readonly Button[] _claimButtons = new Button[Rows];
        private readonly Text[] _claimLabels = new Text[Rows];
        private EtkinlikOdulUI _popup;
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
            RectTransform canvas = UiBuild.Canvas(transform, "UretimSprintiKanvas", sortingOrder);
            _root = UiBuild.Flat(canvas, "Karartma", UiBuild.Opaque(scrim), Vector2.zero, Vector2.one);
            Button dismiss = _root.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(Hide);

            RectTransform sheet = EtkinlikKit.Sheet(_root).rectTransform;
            _title = EtkinlikKit.Header(_root, Loc.T("sprint.baslik"), Hide);

            _clock = EtkinlikKit.Label(sheet, "Saat", new Vector2(0.090f, 0.705f), new Vector2(0.560f, 0.765f),
                                       string.Empty, 24, TextAnchor.MiddleLeft, EtkinlikKit.InkSoft, 12);
            _score = EtkinlikKit.Chip(sheet, "Puan", new Vector2(0.600f, 0.705f), new Vector2(0.910f, 0.765f));

            const float left = 0.090f, right = 0.910f, gap = 0.008f;
            float w = (right - left) / TabCount;
            for (int i = 0; i < TabCount; i++)
            {
                int captured = i;
                _tabs[i] = EtkinlikKit.Tab(sheet, "Sekme" + i,
                                           new Vector2(left + i * w + gap, 0.628f), new Vector2(left + (i + 1) * w - gap, 0.694f),
                                           () => { _tab = captured; Refresh(); }, out _tabLabels[i]);
            }

            float height = (RowTop - RowBottom) / Rows;
            for (int i = 0; i < Rows; i++)
                BuildRow(sheet, i, new Vector2(0.085f, RowTop - (i + 1) * height + RowGap),
                                   new Vector2(0.915f, RowTop - i * height - RowGap));

            _empty = EtkinlikKit.Empty(sheet, Loc.T("sprint.yok"), out _emptyLabel);
            _popup = EtkinlikOdulUI.Create(_root);
            // Content into the safe area; the scrim above it keeps covering the notch.
            UiBuild.InsetContent(_root);
        }

        /// <summary>
        /// One navy row: an icon, the name over its count, a green bar, and under it either the points an
        /// action is worth (tasks) or the reward (milestones) — with the claim capsule on milestones only.
        /// </summary>
        private void BuildRow(RectTransform parent, int index, Vector2 min, Vector2 max)
        {
            RectTransform row = EtkinlikKit.Card(parent, "Satir" + index, min, max, BorderScale).rectTransform;
            _rows[index] = row;

            _rowIcons[index] = EkranKit.Icon(row, "Simge", null, new Vector2(0.030f, 0.24f), new Vector2(0.150f, 0.76f));
            _rowTitles[index] = EtkinlikKit.Label(row, "Baslik", new Vector2(0.175f, 0.54f), new Vector2(0.500f, 0.78f),
                                                  string.Empty, 26, TextAnchor.MiddleLeft, EkranKit.Paper, 14);
            _rowCounts[index] = EtkinlikKit.Label(row, "Sayi", new Vector2(0.500f, 0.54f), new Vector2(0.660f, 0.78f),
                                                  string.Empty, 22, TextAnchor.MiddleRight, EkranKit.PaperSoft, 11);
            _fills[index] = EtkinlikKit.Bar(row, "Ilerleme", new Vector2(0.175f, 0.40f), new Vector2(0.660f, 0.53f), "cubuk_yesil");
            _rowRewards[index] = EtkinlikOdulSatiri.Create(row, "Detay", new Vector2(0.175f, 0.20f), new Vector2(0.660f, 0.39f),
                                                          22, EkranKit.PaperSoft, TextAnchor.MiddleLeft);

            // A task row has no claim, so the claim's place carries what that action has scored so far —
            // otherwise the right third of the card is empty.
            _rowEarned[index] = EtkinlikKit.Label(row, "Kazanilan", new Vector2(0.690f, 0.27f), new Vector2(0.955f, 0.73f),
                                                  string.Empty, 32, TextAnchor.MiddleCenter, EkranKit.Paper, 16);

            int captured = index;
            _claimButtons[index] = EtkinlikKit.Capsule(row, "OduluAl", new Vector2(0.690f, 0.27f), new Vector2(0.955f, 0.73f),
                                                       () => Claim(captured), out _claimLabels[index]);
        }

        private void Claim(int index)
        {
            if (_sprint == null || _tab != 1) return;
            ProductionSprint.Reward reward = _sprint.MilestoneAt(index).Reward;
            if (_sprint.ClaimMilestone(index))
                _popup.Present(reward.Gems, reward.Cards, 0L, CashText(reward.CashMinutes));
            Refresh();
        }

        // ---------------------------------------------------------------- refresh
        private void Refresh()
        {
            if (_root == null || !_root.gameObject.activeSelf) return;

            _title.text = Loc.T("sprint.baslik");
            bool available = _sprint != null && _sprint.Available;

            EtkinlikKit.SetActive(_empty, !available);
            EtkinlikKit.SetActive(_clock.transform.parent, available);
            EtkinlikKit.SetActive(_score.transform.parent.parent, available);
            for (int i = 0; i < TabCount; i++)
            {
                EtkinlikKit.SetActive(_tabs[i], available);
                _tabLabels[i].text = EtkinlikKit.OneLine(Loc.T(TabKeys[i]));
                EtkinlikKit.SetTab(_tabs[i], i == _tab);
            }
            if (!available)
            {
                _emptyLabel.text = Loc.T("sprint.yok");
                for (int i = 0; i < Rows; i++) EtkinlikKit.SetActive(_rows[i], false);
                return;
            }

            _score.text = _sprint.Score + " " + Loc.T("sprint.puan");
            if (_sprint.Phase == LiveEvents.Phase.Active)
                _clock.text = Loc.T("etkinlik.kalan") + " " + HudUI.LongClock(_sprint.SecondsLeft);
            else if (_sprint.Phase == LiveEvents.Phase.Upcoming)
                _clock.text = Loc.T("etkinlik.yakinda");
            else
                _clock.text = Loc.T("sprint.bitti");

            for (int i = 0; i < Rows; i++)
            {
                bool visible = _tab == 0 ? i < ProductionSprint.RuleCount : true;
                EtkinlikKit.SetActive(_rows[i], visible);
                if (!visible) continue;
                if (_tab == 0) RefreshRule(i);
                else RefreshMilestone(i);
            }
        }

        private void RefreshRule(int index)
        {
            ProductionSprint.ScoringRule rule = _sprint.RuleAt(index);
            long progress = _sprint.RuleProgress(index);

            SetIcon(index, EkranKit.Get("etkinlik_ikon"), progress < rule.ActionLimit);
            _rowTitles[index].text = MetricName(rule.Metric);
            _rowCounts[index].text = progress + " / " + rule.ActionLimit;
            EtkinlikKit.Progress(_fills[index], Ratio(progress, rule.ActionLimit));
            _rowRewards[index].Set("+" + rule.PointsPerAction + " " + Loc.T("sprint.eylem_puani"), 0L, 0, 0L, null);
            EtkinlikKit.SetActive(_claimButtons[index], false);
            EtkinlikKit.SetActive(_rowEarned[index].transform.parent, true);
            _rowEarned[index].text = (progress * rule.PointsPerAction) + " " + Loc.T("sprint.puan");
        }

        private void RefreshMilestone(int index)
        {
            ProductionSprint.Milestone milestone = _sprint.MilestoneAt(index);
            long score = _sprint.Score;
            bool claimed = _sprint.MilestoneClaimed(index);
            bool can = _sprint.CanClaimMilestone(index);

            SetIcon(index, EtkinlikKit.Chest(index < 3 ? index : 3), !claimed);
            _rowTitles[index].text = milestone.Score + " " + Loc.T("sprint.puan");
            _rowCounts[index].text = (score < milestone.Score ? score : milestone.Score) + " / " + milestone.Score;
            EtkinlikKit.Progress(_fills[index], Ratio(score, milestone.Score));
            _rowRewards[index].Set(null, milestone.Reward.Gems, milestone.Reward.Cards, 0L, CashText(milestone.Reward.CashMinutes));

            EtkinlikKit.SetActive(_rowEarned[index].transform.parent, false);
            EtkinlikKit.SetActive(_claimButtons[index], true);
            _claimLabels[index].text = EtkinlikKit.OneLine(claimed ? Loc.T("gorev.alindi") : Loc.T("gorev.al"));
            EtkinlikKit.SetFace(_claimButtons[index], _claimLabels[index],
                                can ? EtkinlikKit.Face.Claim : EtkinlikKit.Face.Dead, can);
        }

        // ------------------------------------------------------------------ pieces
        private void SetIcon(int index, Sprite sprite, bool bright)
        {
            Image icon = _rowIcons[index];
            if (icon.sprite != sprite)
            {
                icon.sprite = sprite;
                icon.enabled = sprite != null;
            }
            icon.color = bright ? Color.white : EtkinlikKit.Faded;
        }

        private static float Ratio(long progress, long target)
            => target <= 0L ? 1f : Mathf.Clamp01((float)(progress / (double)target));

        private static string CashText(double minutes)
            => minutes > 0d ? "+" + minutes.ToString("0.#") + " " + Loc.T("sprint.nakit_dakika") : null;

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
