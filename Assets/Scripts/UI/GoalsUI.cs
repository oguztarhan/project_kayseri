using Game.Core;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The checklist screen: today's three tasks above, the permanent ladder below.
    ///
    /// Built in code for the same reason <see cref="ForemanRosterUI"/> is — the rows are generated
    /// from <see cref="Goals"/>'s own tables, so an authored sheet would be a set of hand-wired copies
    /// that fall out of step the moment a task or a tier changes.
    ///
    /// Refreshed on open and on <see cref="GoalService.Changed"/>, never per frame. The opener carries
    /// the pending count, because a checklist nobody is told about is a checklist nobody opens.
    /// </summary>
    public sealed class GoalsUI : MonoBehaviour
    {
        [SerializeField] private int sortingOrder = 106;

        [Header("Renkler")]
        [SerializeField] private Color scrim = new Color(0.04f, 0.05f, 0.08f, 0.86f);
        [SerializeField] private Color card = new Color(0.16f, 0.19f, 0.27f, 1f);
        [SerializeField] private Color track = new Color(0.10f, 0.11f, 0.16f, 1f);
        [SerializeField] private Color dailyFill = new Color(0.35f, 0.72f, 0.98f, 1f);
        [SerializeField] private Color ladderFill = new Color(0.98f, 0.74f, 0.24f, 1f);

        /// <summary>The rail button's icon, loaded at runtime — this screen has no Inspector to wire.</summary>
        private const string OpenerIconResource = "UI/Buttons/gorev";

        private GoalService _goals;
        private RectTransform _root;
        private Text _header;
        private TMP_Text _openerCount;
        private GameObject _openerChip;

        private readonly Text[] _dailyText = new Text[Goals.DailySlots];
        private readonly Text[] _dailyReward = new Text[Goals.DailySlots];
        private readonly RectTransform[] _dailyFillRect = new RectTransform[Goals.DailySlots];
        private readonly Button[] _dailyBtn = new Button[Goals.DailySlots];
        private readonly Text[] _dailyBtnText = new Text[Goals.DailySlots];

        private Text[] _ladderText;
        private RectTransform[] _ladderFillRect;
        private Button[] _ladderBtn;
        private Text[] _ladderBtnText;

        private void Awake()
        {
            _goals = ServiceLocator.Get<GoalService>();
            Build();
            BuildOpener();
            if (_goals != null) _goals.Changed += OnChanged;
            Hide();
            RefreshOpener();
        }

        private void OnDestroy() { if (_goals != null) _goals.Changed -= OnChanged; }

        private void OnChanged() { Refresh(); RefreshOpener(); }

        public void Show() { if (_root != null) _root.gameObject.SetActive(true); Refresh(); }
        public void Hide() { if (_root != null) _root.gameObject.SetActive(false); }

        // ------------------------------------------------------------------ build
        private void Build()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "GorevKanvas", sortingOrder);
            _root = UiBuild.Flat(canvas, "Karartma", scrim, Vector2.zero, Vector2.one);

            _header = UiBuild.Label(Slot(_root, "Baslik", new Vector2(0.05f, 0.90f), new Vector2(0.88f, 0.98f)),
                                    "Text", string.Empty, 42, TextAnchor.MiddleLeft);

            Button close = UiBuild.Btn(_root, "Kapat", "X", UiSkin.ButtonGrey, track, 34, Hide);
            UiBuild.Anchor((RectTransform)close.transform, new Vector2(0.90f, 0.91f), new Vector2(0.965f, 0.985f));

            UiBuild.Label(Slot(_root, "GunlukBaslik", new Vector2(0.05f, 0.84f), new Vector2(0.95f, 0.89f)),
                          "Text", Loc.T("gorev.gunluk"), 26, TextAnchor.MiddleLeft);

            const float dTop = 0.83f, dBottom = 0.55f;
            float w = (0.95f - 0.05f) / Goals.DailySlots;
            for (int i = 0; i < Goals.DailySlots; i++)
            {
                var aMin = new Vector2(0.05f + i * w + 0.006f, dBottom);
                var aMax = new Vector2(0.05f + (i + 1) * w - 0.006f, dTop);
                BuildDaily(i, aMin, aMax);
            }

            UiBuild.Label(Slot(_root, "LadderBaslik", new Vector2(0.05f, 0.48f), new Vector2(0.95f, 0.53f)),
                          "Text", Loc.T("gorev.basarimlar"), 26, TextAnchor.MiddleLeft);

            int n = Goals.Ladder.Length;
            _ladderText = new Text[n];
            _ladderFillRect = new RectTransform[n];
            _ladderBtn = new Button[n];
            _ladderBtnText = new Text[n];

            const float lTop = 0.47f, lBottom = 0.04f;
            float rowH = (lTop - lBottom) / n;
            for (int i = 0; i < n; i++)
            {
                var aMin = new Vector2(0.05f, lTop - (i + 1) * rowH + 0.004f);
                var aMax = new Vector2(0.95f, lTop - i * rowH - 0.004f);
                BuildLadderRow(i, aMin, aMax);
            }
        }

        private void BuildDaily(int slot, Vector2 aMin, Vector2 aMax)
        {
            RectTransform c = UiBuild.Box(_root, "Gunluk_" + slot, card, aMin, aMax);

            _dailyText[slot] = UiBuild.Label(Slot(c, "Yazi", new Vector2(0.06f, 0.58f), new Vector2(0.94f, 0.92f)),
                                             "Text", string.Empty, 26, TextAnchor.MiddleCenter);

            UiBuild.Bar(c, "Cubuk", track, dailyFill,
                        new Vector2(0.08f, 0.44f), new Vector2(0.92f, 0.54f), out _dailyFillRect[slot]);

            _dailyReward[slot] = UiBuild.Label(Slot(c, "Odul", new Vector2(0.06f, 0.28f), new Vector2(0.94f, 0.42f)),
                                               "Text", string.Empty, 22, TextAnchor.MiddleCenter);

            int captured = slot;
            _dailyBtn[slot] = UiBuild.Btn(c, "Al", string.Empty, UiSkin.ButtonGreen,
                                          new Color(0.24f, 0.68f, 0.36f, 1f), 24,
                                          () => { if (_goals != null && _goals.ClaimDaily(captured)) Ping(); });
            UiBuild.Anchor((RectTransform)_dailyBtn[slot].transform,
                           new Vector2(0.10f, 0.06f), new Vector2(0.90f, 0.24f));
            _dailyBtnText[slot] = _dailyBtn[slot].GetComponentInChildren<Text>();
        }

        private void BuildLadderRow(int index, Vector2 aMin, Vector2 aMax)
        {
            RectTransform c = UiBuild.Box(_root, "Basarim_" + index, card, aMin, aMax);

            _ladderText[index] = UiBuild.Label(Slot(c, "Yazi", new Vector2(0.02f, 0.42f), new Vector2(0.72f, 0.96f)),
                                               "Text", string.Empty, 24, TextAnchor.MiddleLeft);

            UiBuild.Bar(c, "Cubuk", track, ladderFill,
                        new Vector2(0.02f, 0.12f), new Vector2(0.72f, 0.36f), out _ladderFillRect[index]);

            int captured = index;
            _ladderBtn[index] = UiBuild.Btn(c, "Al", string.Empty, UiSkin.ButtonGreen,
                                            new Color(0.24f, 0.68f, 0.36f, 1f), 24,
                                            () => { if (_goals != null && _goals.ClaimAchievement(captured)) Ping(); });
            UiBuild.Anchor((RectTransform)_ladderBtn[index].transform,
                           new Vector2(0.76f, 0.14f), new Vector2(0.98f, 0.86f));
            _ladderBtnText[index] = _ladderBtn[index].GetComponentInChildren<Text>();
        }

        private static void Ping() => ServiceLocator.Get<HapticService>()?.Medium();

        private static RectTransform Slot(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
        }

        // ---------------------------------------------------------------- opener
        /// <summary>
        /// The opener goes on the end of the HUD's left rail, under the ads and offer buttons, rather
        /// than floating at a screen fraction of its own — see <see cref="HudUI.AttachRailButton"/> for
        /// why a fraction of a landscape screen is not a place on a portrait-authored rail.
        ///
        /// The pending count rides under it in the rail's own counter chip, the same one the contract
        /// and offer buttons wear, and goes away when there is nothing to claim: a checklist nobody is
        /// told about is a checklist nobody opens.
        /// </summary>
        private void BuildOpener()
        {
            HudUI hud = FindAnyObjectByType<HudUI>(FindObjectsInactive.Include);
            if (hud == null) return;

            Button open = hud.AttachRailButton(false, "BtnGorev", Resources.Load<Sprite>(OpenerIconResource), Show);
            if (open == null) return;

            _openerChip = hud.AttachRailChip(open);
            if (_openerChip != null) _openerCount = _openerChip.GetComponentInChildren<TMP_Text>(true);
        }

        private void RefreshOpener()
        {
            if (_openerChip == null || _goals == null) return;
            int pending = _goals.PendingCount();
            _openerChip.SetActive(pending > 0);
            if (pending > 0) _openerCount.text = pending.ToString();
        }

        // --------------------------------------------------------------- refresh
        private void Refresh()
        {
            if (_goals == null || _root == null || !_root.gameObject.activeSelf) return;

            int pending = _goals.PendingCount();
            _header.text = pending > 0
                ? string.Format("{0}   ·   {1}", Loc.T("gorev.baslik"), pending)
                : Loc.T("gorev.baslik");

            for (int i = 0; i < Goals.DailySlots; i++) RefreshDaily(i);
            for (int i = 0; i < Goals.Ladder.Length; i++) RefreshLadder(i);
        }

        private void RefreshDaily(int slot)
        {
            Goals.Task t = _goals.DailyTask(slot);
            long have = _goals.DailyProgress(slot);
            bool claimed = _goals.DailyClaimed(slot);

            _dailyText[slot].text = string.Format("{0}\n{1} / {2}", MetricName(t.Metric), have, t.Target);
            _dailyFillRect[slot].anchorMax = new Vector2(Goals.Progress(have, t.Target), 1f);
            _dailyReward[slot].text = RewardLine(t.Gems, t.Cards);

            _dailyBtnText[slot].text = claimed ? Loc.T("gorev.alindi") : Loc.T("gorev.al");
            _dailyBtn[slot].interactable = _goals.CanClaimDaily(slot);
        }

        private void RefreshLadder(int index)
        {
            Goals.Achievement a = Goals.Ladder[index];
            long total = _goals.Lifetime(a.Metric);
            int reached = Goals.TiersReached(a, total);
            long next = Goals.NextTier(a, total);
            int owed = _goals.UnclaimedTiers(index);

            _ladderText[index].text = next > 0L
                ? string.Format("{0}   ·   {1} {2}/{3}   ·   {4} / {5}",
                                MetricName(a.Metric), Loc.T("gorev.kademe"), reached, a.Tiers.Length, total, next)
                : string.Format("{0}   ·   {1}", MetricName(a.Metric), Loc.T("gorev.tamamlandi"));

            long from = reached > 0 ? a.Tiers[reached - 1] : 0L;
            _ladderFillRect[index].anchorMax = new Vector2(
                next > 0L ? Goals.Progress(total - from, next - from) : 1f, 1f);

            _ladderBtnText[index].text = owed > 0 ? string.Format("{0} ×{1}", Loc.T("gorev.al"), owed)
                                                  : Loc.T("gorev.al");
            _ladderBtn[index].interactable = owed > 0;
        }

        private static string RewardLine(long gems, int cards)
            => cards > 0
                ? string.Format("{0} {1} + {2} {3}", gems, Loc.T("ortak.elmas"), cards, Loc.T("ustabasi.kart"))
                : string.Format("{0} {1}", gems, Loc.T("ortak.elmas"));

        /// <summary>
        /// A metric's name. Four of the six already have a word in the table — the stations and the
        /// foreman screen named them — so only the two that do not get their own key.
        /// </summary>
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
