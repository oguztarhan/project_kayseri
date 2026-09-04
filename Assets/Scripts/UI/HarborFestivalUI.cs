using Game.Core;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>Code-built Tasks, Rewards, and Catalogue screen for Harbor Festival.</summary>
    public sealed class HarborFestivalUI : MonoBehaviour
    {
        [SerializeField] private int sortingOrder = 112;
        [SerializeField] private Sprite cardPanel;
        [SerializeField] private Sprite ribbon;
        [SerializeField] private Sprite actionButton;
        [SerializeField] private Sprite closeIcon;

        private static readonly Color Ink = new Color(0.09f, 0.14f, 0.24f, 1f);
        private static readonly Color Soft = new Color(0.34f, 0.40f, 0.50f, 1f);
        private static readonly Color Blue = new Color(0.18f, 0.58f, 0.88f, 1f);
        private static readonly Color Gold = new Color(0.95f, 0.67f, 0.18f, 1f);
        private static readonly Color Disabled = new Color(0.48f, 0.52f, 0.58f, 1f);

        private const int Rows = HarborFestival.TierCount;

        private HarborFestivalService _festival;
        private LocalizationService _loc;
        private RectTransform _root;
        private Text _title;
        private Text _clock;
        private Text _tokens;
        private readonly Button[] _tabs = new Button[3];
        private readonly Text[] _tabText = new Text[3];
        private readonly RectTransform[] _row = new RectTransform[Rows];
        private readonly Text[] _rowTitle = new Text[Rows];
        private readonly Text[] _rowReward = new Text[Rows];
        private readonly Button[] _primary = new Button[Rows];
        private readonly Text[] _primaryText = new Text[Rows];
        private readonly Button[] _secondary = new Button[Rows];
        private readonly Text[] _secondaryText = new Text[Rows];
        private int _tab;
        private float _tick;

        private void Awake()
        {
            _festival = ServiceLocator.Get<HarborFestivalService>();
            _loc = ServiceLocator.Get<LocalizationService>();
            Build();
            if (_festival != null) _festival.Changed += Refresh;
            if (_loc != null) _loc.Changed += Refresh;
            Hide();
        }

        private void OnDestroy()
        {
            if (_festival != null) _festival.Changed -= Refresh;
            if (_loc != null) _loc.Changed -= Refresh;
        }

        public void Show()
        {
            if (_root == null) return;
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

        private void Build()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "LimanFestivaliKanvas", sortingOrder);
            _root = UiBuild.Flat(canvas, "Karartma", new Color(0.03f, 0.05f, 0.10f, 0.92f), Vector2.zero, Vector2.one);
            var dismiss = _root.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(Hide);

            RectTransform sheet = Art(_root, "Zemin", cardPanel, new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.86f));
            sheet.GetComponent<Image>().color = new Color(0.88f, 0.94f, 0.98f, 1f);
            sheet.GetComponent<Image>().raycastTarget = true;
            sheet.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;

            RectTransform band = Art(_root, "Serit", ribbon, new Vector2(0.28f, 0.87f), new Vector2(0.72f, 0.985f));
            _title = UiBuild.Label(Slot(band, "Yazi", new Vector2(0.08f, 0.18f), new Vector2(0.92f, 0.86f)),
                "Text", Loc.T("liman.baslik"), 36, TextAnchor.MiddleCenter);

            Button close = UiBuild.Btn(_root, "Kapat", string.Empty,
                closeIcon != null ? closeIcon : UiSkin.ButtonGrey, Color.white, 32, Hide);
            UiBuild.Anchor((RectTransform)close.transform, new Vector2(0.88f, 0.89f), new Vector2(0.95f, 0.97f));

            _clock = UiBuild.Label(Slot(sheet, "Saat", new Vector2(0.05f, 0.90f), new Vector2(0.58f, 0.98f)),
                "Text", string.Empty, 25, TextAnchor.MiddleLeft);
            _clock.color = Soft;
            _tokens = UiBuild.Label(Slot(sheet, "Jeton", new Vector2(0.58f, 0.90f), new Vector2(0.95f, 0.98f)),
                "Text", string.Empty, 28, TextAnchor.MiddleRight);
            _tokens.color = Ink;

            string[] keys = { "liman.gorevler", "liman.oduller", "liman.katalog" };
            for (int i = 0; i < 3; i++)
            {
                int captured = i;
                _tabs[i] = UiBuild.Btn(sheet, "Sekme" + i, Loc.T(keys[i]), UiSkin.ButtonBlue, Blue, 25,
                    () => { _tab = captured; Refresh(); });
                UiBuild.Anchor((RectTransform)_tabs[i].transform,
                    new Vector2(0.05f + i * 0.305f, 0.81f), new Vector2(0.33f + i * 0.305f, 0.89f));
                _tabText[i] = _tabs[i].GetComponentInChildren<Text>();
            }

            const float top = 0.79f, bottom = 0.05f;
            float height = (top - bottom) / Rows;
            for (int i = 0; i < Rows; i++)
                BuildRow(sheet, i, new Vector2(0.05f, top - (i + 1) * height + 0.005f),
                    new Vector2(0.95f, top - i * height - 0.005f));
        }

        private void BuildRow(RectTransform parent, int index, Vector2 min, Vector2 max)
        {
            RectTransform row = Art(parent, "Satir" + index, cardPanel, min, max);
            row.GetComponent<Image>().color = Color.white;
            _row[index] = row;
            _rowTitle[index] = UiBuild.Label(Slot(row, "Baslik", new Vector2(0.025f, 0.48f), new Vector2(0.55f, 0.94f)),
                "Text", string.Empty, 23, TextAnchor.MiddleLeft);
            _rowTitle[index].color = Ink;
            _rowReward[index] = UiBuild.Label(Slot(row, "Odul", new Vector2(0.025f, 0.06f), new Vector2(0.55f, 0.50f)),
                "Text", string.Empty, 20, TextAnchor.MiddleLeft);
            _rowReward[index].color = Soft;

            int captured = index;
            _primary[index] = UiBuild.Btn(row, "Birincil", string.Empty, actionButton != null ? actionButton : UiSkin.ButtonGreen,
                Blue, 20, () => ActPrimary(captured));
            UiBuild.Anchor((RectTransform)_primary[index].transform, new Vector2(0.58f, 0.17f), new Vector2(0.76f, 0.83f));
            _primaryText[index] = _primary[index].GetComponentInChildren<Text>();

            _secondary[index] = UiBuild.Btn(row, "Ikincil", string.Empty, actionButton != null ? actionButton : UiSkin.ButtonGreen,
                Gold, 20, () => ActSecondary(captured));
            UiBuild.Anchor((RectTransform)_secondary[index].transform, new Vector2(0.78f, 0.17f), new Vector2(0.965f, 0.83f));
            _secondaryText[index] = _secondary[index].GetComponentInChildren<Text>();
        }

        private void ActPrimary(int index)
        {
            if (_festival == null) return;
            bool claimed = _tab == 0 ? _festival.ClaimTask(index)
                : _tab == 1 ? _festival.ClaimFreeTier(index)
                : _festival.Phase == LiveEvents.Phase.Closed && index == 0
                    ? _festival.ClaimExpiryConversion()
                    : _festival.Redeem(index);
            if (claimed) ServiceLocator.Get<HapticService>()?.Medium();
            Refresh();
        }

        private void ActSecondary(int index)
        {
            if (_festival == null || _tab != 1) return;
            if (_festival.ClaimPremiumTier(index)) ServiceLocator.Get<HapticService>()?.Medium();
            Refresh();
        }

        private void Refresh()
        {
            if (_root == null || !_root.gameObject.activeSelf) return;
            _title.text = Loc.T("liman.baslik");
            string[] keys = { "liman.gorevler", "liman.oduller", "liman.katalog" };
            for (int i = 0; i < 3; i++)
            {
                _tabText[i].text = Loc.T(keys[i]);
                _tabs[i].GetComponent<Image>().color = i == _tab ? Gold : Blue;
            }

            bool available = _festival != null && _festival.Available;
            _tokens.text = Loc.T("liman.jeton") + "  " + (available ? _festival.TokenBalance.ToString() : "0");
            if (!available) _clock.text = Loc.T("liman.yok");
            else if (_festival.Phase == LiveEvents.Phase.Active)
                _clock.text = Loc.T("etkinlik.kalan") + " " + HudUI.LongClock(_festival.SecondsLeft);
            else if (_festival.Phase == LiveEvents.Phase.Upcoming)
                _clock.text = Loc.T("etkinlik.yakinda");
            else
            {
                int expiry = _festival.ExpiryGems;
                _clock.text = expiry > 0 ? Loc.T("liman.donusum") + " +" + expiry : Loc.T("senlik.bitti");
            }

            for (int i = 0; i < Rows; i++)
            {
                bool visible = available && (_tab == 0 ? i < HarborFestival.TaskCount
                    : _tab == 1 ? i < HarborFestival.TierCount : i < HarborFestival.CatalogueCount);
                _row[i].gameObject.SetActive(visible);
                if (!visible) continue;
                if (_tab == 0) RefreshTask(i);
                else if (_tab == 1) RefreshTier(i);
                else RefreshCatalogue(i);
            }
        }

        private void RefreshTask(int index)
        {
            HarborFestival.Task task = _festival.TaskAt(index);
            _rowTitle[index].text = MetricName(task.Metric) + "  " + _festival.TaskProgress(index) + "/" + task.Target;
            _rowReward[index].text = "+" + task.Tokens + " " + Loc.T("liman.jeton") + RewardText(task.Reward);
            bool claimed = _festival.TaskClaimed(index);
            bool can = _festival.TaskDone(index) && !claimed;
            SetButton(_primary[index], _primaryText[index], true, can,
                claimed ? Loc.T("gorev.alindi") : Loc.T("gorev.al"));
            SetButton(_secondary[index], _secondaryText[index], false, false, string.Empty);
        }

        private void RefreshTier(int index)
        {
            HarborFestival.Tier tier = _festival.TierAt(index);
            _rowTitle[index].text = tier.Tokens + " " + Loc.T("liman.jeton");
            _rowReward[index].text = Loc.T("liman.ucretsiz") + RewardText(tier.Free)
                + "   " + Loc.T("liman.premium") + RewardText(tier.Premium);
            bool freeClaimed = _festival.FreeTierClaimed(index);
            SetButton(_primary[index], _primaryText[index], true, _festival.CanClaimFreeTier(index),
                freeClaimed ? Loc.T("gorev.alindi") : Loc.T("liman.ucretsiz"));
            bool premiumClaimed = _festival.PremiumTierClaimed(index);
            string premium = !_festival.PremiumOwned ? Loc.T("liman.kilitli")
                : premiumClaimed ? Loc.T("gorev.alindi") : Loc.T("liman.premium");
            SetButton(_secondary[index], _secondaryText[index], true, _festival.CanClaimPremiumTier(index), premium);
        }

        private void RefreshCatalogue(int index)
        {
            if (_festival.Phase == LiveEvents.Phase.Closed)
            {
                bool conversion = index == 0 && _festival.ExpiryGems > 0;
                _row[index].gameObject.SetActive(conversion);
                if (!conversion) return;
                _rowTitle[index].text = Loc.T("liman.donusum");
                _rowReward[index].text = "+" + _festival.ExpiryGems + " ◆";
                SetButton(_primary[index], _primaryText[index], true, true, Loc.T("gorev.al"));
                SetButton(_secondary[index], _secondaryText[index], false, false, string.Empty);
                return;
            }

            HarborFestival.CatalogueItem item = _festival.CatalogueAt(index);
            _rowTitle[index].text = Loc.T("liman.takas") + " " + (index + 1);
            _rowReward[index].text = item.Cost + " " + Loc.T("liman.jeton") + RewardText(item.Reward);
            bool claimed = _festival.CatalogueClaimed(index);
            SetButton(_primary[index], _primaryText[index], true, _festival.CanRedeem(index),
                claimed ? Loc.T("gorev.alindi") : Loc.T("liman.takas"));
            SetButton(_secondary[index], _secondaryText[index], false, false, string.Empty);
        }

        private static void SetButton(Button button, Text label, bool visible, bool enabled, string value)
        {
            button.gameObject.SetActive(visible);
            if (!visible) return;
            button.interactable = enabled;
            button.GetComponent<Image>().color = enabled ? Blue : Disabled;
            label.text = value;
        }

        private static string RewardText(in HarborFestival.Reward reward)
        {
            string text = "  →";
            if (reward.Gems > 0L) text += "  +" + reward.Gems + " ◆";
            if (reward.Cards > 0) text += "  +" + reward.Cards + " " + Loc.T("ustabasi.kart");
            if (reward.Charts > 0L) text += "  +" + reward.Charts + " " + Loc.T("kaptan.harita");
            if (reward.BoostMult > 1d) text += "  ×" + reward.BoostMult.ToString("0.#");
            return text;
        }

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

        private static RectTransform Art(RectTransform parent, string name, Sprite sprite, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.sprite = sprite != null ? sprite : UiSkin.Panel;
            image.type = image.sprite != null && image.sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
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
