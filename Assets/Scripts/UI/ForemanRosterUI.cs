using Game.Core;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The foreman roster screen: eight cards, one per station, each showing who is hired, how far
    /// they are levelled, and what the next step costs.
    ///
    /// BUILT IN CODE rather than authored as a prefab, the same way <see cref="UiBuild"/>'s other
    /// screens are. That is a deliberate trade: an authored sheet would look better, but the roster
    /// is exactly eight identical cards driven off <see cref="Foremen"/>'s own tables, and a prefab
    /// would mean eight hand-wired copies that fall out of step the moment a slot's rarity changes.
    /// The card layout here reads the tables, so it cannot disagree with the maths.
    ///
    /// Refreshed on open and on <see cref="ForemanService.RosterChanged"/> — never per frame. Nothing
    /// on this screen animates, and the wallet only moves when the player presses something on it.
    /// </summary>
    public sealed class ForemanRosterUI : MonoBehaviour
    {
        [Header("Açma düğmesi")]
        // The HUD is an authored prefab, and this screen is built in code, so it brings its own way in
        // rather than needing a slot wired into UI_HUD. Anchors are fractions of the screen so the
        // pill sits in the same place in every aspect ratio; move it in the Inspector if it clashes
        // with the left rail on a device.
        [Tooltip("Açma düğmesinin ekrandaki yeri, oran olarak. Sol raydaki ikonların altına oturur.")]
        [SerializeField] private Vector2 openerMin = new Vector2(0.012f, 0.30f);
        [SerializeField] private Vector2 openerMax = new Vector2(0.10f, 0.42f);
        [SerializeField] private int openerSortingOrder = 96;

        [Header("Yerleşim")]
        [Tooltip("Kart ızgarası: yatayda kaç sütun. Sekiz slot 4x2 olarak oturur.")]
        [SerializeField] private int columns = 4;
        [SerializeField] private int sortingOrder = 105;

        [Header("Renkler")]
        [SerializeField] private Color scrim = new Color(0.04f, 0.05f, 0.08f, 0.86f);
        [SerializeField] private Color cardHired = new Color(0.16f, 0.19f, 0.27f, 1f);
        [SerializeField] private Color cardLocked = new Color(0.11f, 0.12f, 0.17f, 1f);
        [Tooltip("Sıradanlık renkleri: Common, Rare, Epic.")]
        [SerializeField] private Color commonTint = new Color(0.62f, 0.68f, 0.78f, 1f);
        [SerializeField] private Color rareTint = new Color(0.33f, 0.62f, 0.92f, 1f);
        [SerializeField] private Color epicTint = new Color(0.72f, 0.45f, 0.95f, 1f);

        private ForemanService _foremen;
        private WalletService _wallet;
        private RectTransform _root;
        private Text _header;
        private Text _openerBadge;

        // One entry per slot, built once. No allocation after Build().
        private readonly Text[] _name = new Text[Foremen.Count];
        private readonly Text[] _level = new Text[Foremen.Count];
        private readonly Text[] _effect = new Text[Foremen.Count];
        private readonly Text[] _cost = new Text[Foremen.Count];
        private readonly Button[] _action = new Button[Foremen.Count];
        private readonly Text[] _actionText = new Text[Foremen.Count];
        private readonly Image[] _card = new Image[Foremen.Count];
        private readonly RectTransform[] _fill = new RectTransform[Foremen.Count];

        private void Awake()
        {
            _foremen = ServiceLocator.Get<ForemanService>();
            _wallet = ServiceLocator.Get<WalletService>();
            Build();
            BuildOpener();
            if (_foremen != null) _foremen.RosterChanged += OnRosterChanged;
            Hide();
            RefreshOpener();
        }

        private void OnDestroy()
        {
            if (_foremen != null) _foremen.RosterChanged -= OnRosterChanged;
        }

        private void OnRosterChanged(int station) { Refresh(); RefreshOpener(); }

        private void BuildOpener()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "UstabasiAcKanvas", openerSortingOrder);
            var safe = new GameObject("GuvenliAlan", typeof(RectTransform), typeof(SafeArea));
            safe.transform.SetParent(canvas, false);
            RectTransform safeRect = UiBuild.Anchor((RectTransform)safe.transform, Vector2.zero, Vector2.one);

            Button open = UiBuild.Btn(safeRect, "UstabasiAc", Loc.T("ustabasi.baslik"),
                                      UiSkin.ButtonBlue, new Color(0.22f, 0.34f, 0.55f, 0.94f), 20, Show);
            UiBuild.Anchor((RectTransform)open.transform, openerMin, openerMax);

            // How many slots are hired, so the button says whether there is anything to come back for.
            _openerBadge = UiBuild.Label(Slot((RectTransform)open.transform, "Rozet",
                                              new Vector2(0f, -0.34f), new Vector2(1f, 0.02f)),
                                         "Text", string.Empty, 18, TextAnchor.MiddleCenter);
        }

        private void RefreshOpener()
        {
            if (_openerBadge == null || _foremen == null) return;
            _openerBadge.text = string.Format("{0}/{1}", _foremen.HiredCount, Foremen.Count);
        }

        public void Show() { if (_root != null) _root.gameObject.SetActive(true); Refresh(); }
        public void Hide() { if (_root != null) _root.gameObject.SetActive(false); }
        public void Toggle()
        {
            if (_root == null) return;
            if (_root.gameObject.activeSelf) Hide(); else Show();
        }

        // ------------------------------------------------------------------ build
        private void Build()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "UstabasiKanvas", sortingOrder);
            _root = UiBuild.Flat(canvas, "Karartma", scrim, Vector2.zero, Vector2.one);

            _header = UiBuild.Label(Slot(_root, "Baslik", new Vector2(0.05f, 0.86f), new Vector2(0.88f, 0.96f)),
                                    "Text", string.Empty, 44, TextAnchor.MiddleLeft);

            // Btn does not anchor itself — see RepairMarkers, which anchors the returned transform the
            // same way. Passing a pre-anchored parent is not enough: the button lands inside it at the
            // default centre anchors and comes out zero-sized.
            Button close = UiBuild.Btn(_root, "Kapat", "X", UiSkin.ButtonGrey, cardLocked, 34, Hide);
            UiBuild.Anchor((RectTransform)close.transform, new Vector2(0.90f, 0.87f), new Vector2(0.965f, 0.955f));

            int rows = (Foremen.Count + columns - 1) / columns;
            const float left = 0.05f, right = 0.95f, top = 0.82f, bottom = 0.06f;
            float cellW = (right - left) / columns, cellH = (top - bottom) / rows;
            const float padX = 0.008f, padY = 0.02f;

            for (int s = 0; s < Foremen.Count; s++)
            {
                int col = s % columns, row = s / columns;
                var aMin = new Vector2(left + col * cellW + padX, top - (row + 1) * cellH + padY);
                var aMax = new Vector2(left + (col + 1) * cellW - padX, top - row * cellH - padY);
                BuildCard(s, aMin, aMax);
            }
        }

        private void BuildCard(int station, Vector2 aMin, Vector2 aMax)
        {
            RectTransform card = UiBuild.Box(_root, "Kart_" + station, cardHired, aMin, aMax);
            _card[station] = card.GetComponent<Image>();

            // The rarity stripe. Fixed per slot, so it is set once here and never refreshed.
            UiBuild.Flat(card, "Seritt", TintFor(station), new Vector2(0f, 0.94f), new Vector2(1f, 1f));

            _name[station] = UiBuild.Label(
                Slot(card, "Ad", new Vector2(0.06f, 0.76f), new Vector2(0.94f, 0.93f)),
                "Text", string.Empty, 30, TextAnchor.MiddleCenter);

            _level[station] = UiBuild.Label(
                Slot(card, "Seviye", new Vector2(0.06f, 0.60f), new Vector2(0.94f, 0.75f)),
                "Text", string.Empty, 40, TextAnchor.MiddleCenter);

            _effect[station] = UiBuild.Label(
                Slot(card, "Etki", new Vector2(0.06f, 0.47f), new Vector2(0.94f, 0.59f)),
                "Text", string.Empty, 26, TextAnchor.MiddleCenter);

            // Cards-toward-next-level. The bar is the collection made visible: gems can be bought,
            // duplicates cannot, so this is the line that actually paces the roster.
            UiBuild.Bar(card, "KartCubugu", cardLocked, TintFor(station),
                        new Vector2(0.10f, 0.36f), new Vector2(0.90f, 0.43f), out _fill[station]);

            _cost[station] = UiBuild.Label(
                Slot(card, "Bedel", new Vector2(0.06f, 0.22f), new Vector2(0.94f, 0.34f)),
                "Text", string.Empty, 24, TextAnchor.MiddleCenter);

            int captured = station;
            _action[station] = UiBuild.Btn(card, "Dugme", string.Empty, UiSkin.ButtonGreen,
                                           new Color(0.24f, 0.68f, 0.36f, 1f), 26, () => OnPressed(captured));
            UiBuild.Anchor((RectTransform)_action[station].transform,
                           new Vector2(0.10f, 0.05f), new Vector2(0.90f, 0.20f));
            _actionText[station] = _action[station].GetComponentInChildren<Text>();
        }

        /// <summary>A child rect anchored inside the card — the shape UiBuild's helpers want.</summary>
        private static RectTransform Slot(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
        }

        private Color TintFor(int station)
        {
            switch (Foremen.Slot(station))
            {
                case Foremen.Rarity.Epic: return epicTint;
                case Foremen.Rarity.Rare: return rareTint;
                default:                  return commonTint;
            }
        }

        // ------------------------------------------------------------------ press
        private void OnPressed(int station)
        {
            if (_foremen == null) return;
            bool done = _foremen.IsHired(station) ? _foremen.TryLevelUp(station) : _foremen.TryHire(station);
            if (done) ServiceLocator.Get<HapticService>()?.Light();
            Refresh();   // RosterChanged already refreshes on success; this covers the refusal too
        }

        // ---------------------------------------------------------------- refresh
        private void Refresh()
        {
            if (_foremen == null || _root == null || !_root.gameObject.activeSelf) return;

            double mult = _foremen.IncomeMultiplier;
            long gems = _wallet != null ? _wallet.Gems : 0L;
            _header.text = string.Format("{0}   ×{1:0.00}   ·   {2} {3}",
                                         Loc.T("ustabasi.baslik"), mult, gems, Loc.T("ortak.elmas"));

            for (int s = 0; s < Foremen.Count; s++) RefreshCard(s);
        }

        private void RefreshCard(int s)
        {
            bool hired = _foremen.IsHired(s);
            bool maxed = _foremen.IsMaxed(s);
            int level = _foremen.LevelOf(s);

            _card[s].color = UiSkin.HasArt ? Color.white : (hired ? cardHired : cardLocked);
            _name[s].text = Loc.Id("istasyon", IslandEconomy.Stations[s]);
            _name[s].color = hired ? Color.white : new Color(1f, 1f, 1f, 0.55f);

            _level[s].text = hired
                ? string.Format(Loc.T("yukseltme.seviye"), level)
                : Loc.T("ustabasi.kiralikdegil");

            // What this foreman is worth right now, as a percentage — the same number on the station
            // and on the empire, because it is literally the same term. See Game.Core.Foremen.
            double perLevel = Foremen.PerLevel(s, _foremen.Tuning);
            _effect[s].text = hired
                ? string.Format("+{0:0.#}%", perLevel * level * 100d)
                : string.Format("+{0:0.#}% / {1}", perLevel * 100d, string.Format(Loc.T("yukseltme.seviye"), 1));

            int have = _foremen.DuplicatesOf(s);
            int need = hired && !maxed ? _foremen.DuplicatesToLevel(s) : 0;
            float t = need > 0 ? Mathf.Clamp01(have / (float)need) : (maxed ? 1f : 0f);
            _fill[s].anchorMax = new Vector2(t, 1f);

            if (maxed)
            {
                _cost[s].text = string.Format("{0} / {0}", need > 0 ? need : have);
                _actionText[s].text = Loc.T("ustabasi.azami");
                _action[s].interactable = false;
            }
            else if (hired)
            {
                _cost[s].text = string.Format("{0}/{1} {2}   ·   {3} {4}",
                                              have, need, Loc.T("ustabasi.kart"),
                                              _foremen.GemsToLevel(s), Loc.T("ortak.elmas"));
                _actionText[s].text = Loc.T("ustabasi.seviyeatla");
                _action[s].interactable = _foremen.CanLevel(s);
            }
            else
            {
                _cost[s].text = string.Format("{0} {1}", _foremen.HireGems(s), Loc.T("ortak.elmas"));
                _actionText[s].text = Loc.T("ustabasi.isealt");
                _action[s].interactable = _foremen.CanHire(s);
            }
        }
    }
}
