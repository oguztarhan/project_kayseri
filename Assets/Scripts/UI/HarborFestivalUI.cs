using Game.Core;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The Harbor Festival: its tasks, the token tiers they climb (a free and a premium reward on each),
    /// and the catalogue the tokens are spent in. Opened from its card on the events board — including
    /// the "reward waiting" card a finished festival leaves behind.
    ///
    /// DRAWN FROM <see cref="EtkinlikKit"/>, the events family's shared pieces, so it wears the board's
    /// sheet, ribbon, navy rows and capsules. Each tab is its own scrolling list rather than one set of
    /// rows re-laid per tab: a tier row carries two reward columns and a task row a progress bar, and
    /// three honest layouts are simpler than one that moves its pieces every time a tab is tapped.
    ///
    /// Every claim that pays shows <see cref="EtkinlikOdulUI"/> with what it paid. Tokens are not a
    /// claim — a task grants them the moment it completes — so they are shown on the task, never on the card.
    /// </summary>
    public sealed class HarborFestivalUI : MonoBehaviour
    {
        [SerializeField] private int sortingOrder = 112;
        [SerializeField] private Color scrim = new Color(0.03f, 0.05f, 0.10f, 1f);

        private const int TabCount = 3;
        private const int TabTasks = 0, TabTiers = 1, TabCatalogue = 2;

        /// <summary>Row pitch in canvas units; each card is 14 shorter, which is the gap between rows.</summary>
        private const float TaskPitch = 196f, TierPitch = 212f, ItemPitch = 196f, RowGap = 14f;

        /// <summary>The navy card drawn at 1/1.5 border — see <see cref="EtkinlikKit.Card"/>.</summary>
        private const float BorderScale = 1.5f;

        private static readonly string[] TabKeys = { "liman.gorevler", "liman.oduller", "liman.katalog" };

        private HarborFestivalService _festival;
        private LocalizationService _loc;
        private RectTransform _root, _body, _empty;
        private Text _title, _clock, _tokens, _emptyLabel;
        private EtkinlikOdulUI _popup;

        private readonly Button[] _tabs = new Button[TabCount];
        private readonly Text[] _tabText = new Text[TabCount];
        private readonly RectTransform[] _lists = new RectTransform[TabCount];
        private RectTransform _catalogueContent;
        private int _tab;
        private float _tick;

        /// <summary>Decided once at build: the premium column exists only when the track can be bought.</summary>
        private bool _premiumShown;

        private readonly Image[] _taskIcon = new Image[HarborFestival.TaskCount];
        private readonly Text[] _taskName = new Text[HarborFestival.TaskCount];
        private readonly Text[] _taskCount = new Text[HarborFestival.TaskCount];
        private readonly Image[] _taskFill = new Image[HarborFestival.TaskCount];
        private readonly EtkinlikOdulSatiri[] _taskReward = new EtkinlikOdulSatiri[HarborFestival.TaskCount];
        private readonly Button[] _taskBtn = new Button[HarborFestival.TaskCount];
        private readonly Text[] _taskBtnText = new Text[HarborFestival.TaskCount];

        private readonly Image[] _tierIcon = new Image[HarborFestival.TierCount];
        private readonly Text[] _tierTokens = new Text[HarborFestival.TierCount];
        private readonly Text[] _tierCaption = new Text[HarborFestival.TierCount];
        private readonly EtkinlikOdulSatiri[] _freeReward = new EtkinlikOdulSatiri[HarborFestival.TierCount];
        private readonly EtkinlikOdulSatiri[] _premiumReward = new EtkinlikOdulSatiri[HarborFestival.TierCount];
        private readonly Button[] _freeBtn = new Button[HarborFestival.TierCount];
        private readonly Text[] _freeText = new Text[HarborFestival.TierCount];
        private readonly Button[] _premiumBtn = new Button[HarborFestival.TierCount];
        private readonly Text[] _premiumText = new Text[HarborFestival.TierCount];

        private readonly RectTransform[] _itemRow = new RectTransform[HarborFestival.CatalogueCount];
        private readonly Image[] _itemIcon = new Image[HarborFestival.CatalogueCount];
        private readonly EtkinlikOdulSatiri[] _itemReward = new EtkinlikOdulSatiri[HarborFestival.CatalogueCount];
        private readonly Text[] _itemCost = new Text[HarborFestival.CatalogueCount];
        private readonly Button[] _itemBtn = new Button[HarborFestival.CatalogueCount];
        private readonly Text[] _itemBtnText = new Text[HarborFestival.CatalogueCount];

        private RectTransform _conversionRow;
        private Text _conversionTitle, _conversionBtnText;
        private EtkinlikOdulSatiri _conversionReward;
        private Button _conversionBtn;

        private void Awake()
        {
            _festival = ServiceLocator.Get<HarborFestivalService>();
            _loc = ServiceLocator.Get<LocalizationService>();
            _premiumShown = _festival != null && _festival.PremiumAvailable;
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
            RectTransform canvas = UiBuild.Canvas(transform, "LimanFestivaliKanvas", sortingOrder);
            _root = UiBuild.Flat(canvas, "Karartma", UiBuild.Opaque(scrim), Vector2.zero, Vector2.one);
            var dismiss = _root.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(Hide);

            RectTransform sheet = EtkinlikKit.Sheet(_root).rectTransform;
            _title = EtkinlikKit.Header(_root, Loc.T("liman.baslik"), Hide);
            _body = EtkinlikKit.Slot(sheet, "Govde", Vector2.zero, Vector2.one);

            _clock = EtkinlikKit.Label(_body, "Saat", new Vector2(0.090f, 0.705f), new Vector2(0.560f, 0.765f),
                                       string.Empty, 26, TextAnchor.MiddleLeft, EtkinlikKit.InkSoft, 14);
            _tokens = EtkinlikKit.Chip(_body, "Jeton", new Vector2(0.580f, 0.705f), new Vector2(0.910f, 0.765f));

            const float left = 0.090f, right = 0.910f, gap = 0.008f;
            float w = (right - left) / TabCount;
            for (int i = 0; i < TabCount; i++)
            {
                int captured = i;
                _tabs[i] = EtkinlikKit.Tab(_body, "Sekme" + i,
                                           new Vector2(left + i * w + gap, 0.628f), new Vector2(left + (i + 1) * w - gap, 0.694f),
                                           () => SelectTab(captured), out _tabText[i]);
            }

            Vector2 listMin = new Vector2(0.085f, 0.075f), listMax = new Vector2(0.915f, 0.615f);

            RectTransform tasks = EtkinlikKit.Scroll(_body, "Gorevler", listMin, listMax, HarborFestival.TaskCount, TaskPitch);
            _lists[TabTasks] = (RectTransform)tasks.parent.parent;
            for (int i = 0; i < HarborFestival.TaskCount; i++) BuildTask(tasks, i);

            RectTransform tiers = EtkinlikKit.Scroll(_body, "Kademeler", listMin, listMax, HarborFestival.TierCount, TierPitch);
            _lists[TabTiers] = (RectTransform)tiers.parent.parent;
            for (int i = 0; i < HarborFestival.TierCount; i++) BuildTier(tiers, i);

            _catalogueContent = EtkinlikKit.Scroll(_body, "Katalog", listMin, listMax, HarborFestival.CatalogueCount, ItemPitch);
            _lists[TabCatalogue] = (RectTransform)_catalogueContent.parent.parent;
            for (int i = 0; i < HarborFestival.CatalogueCount; i++) BuildItem(_catalogueContent, i);
            BuildConversion(_catalogueContent);

            _empty = EtkinlikKit.Empty(sheet, Loc.T("liman.yok"), out _emptyLabel);
            _popup = EtkinlikOdulUI.Create(_root);
            // Content into the safe area; the scrim above it keeps covering the notch.
            UiBuild.InsetContent(_root);
        }

        /// <summary>Chest, the task and its count over a bar, the tokens and reward it pays, and its claim.</summary>
        private void BuildTask(RectTransform content, int i)
        {
            RectTransform card = EtkinlikKit.Row(content, "Gorev" + i, i, TaskPitch, TaskPitch - RowGap, BorderScale).rectTransform;
            _taskIcon[i] = EkranKit.Icon(card, "Simge", EkranKit.Get("sandik"), new Vector2(0.030f, 0.24f), new Vector2(0.150f, 0.76f));
            _taskName[i] = EtkinlikKit.Label(card, "Ad", new Vector2(0.175f, 0.54f), new Vector2(0.500f, 0.77f),
                                             string.Empty, 26, TextAnchor.MiddleLeft, EkranKit.Paper, 14);
            _taskCount[i] = EtkinlikKit.Label(card, "Sayi", new Vector2(0.500f, 0.54f), new Vector2(0.645f, 0.77f),
                                              string.Empty, 22, TextAnchor.MiddleRight, EkranKit.PaperSoft, 12);
            _taskFill[i] = EtkinlikKit.Bar(card, "Cubuk", new Vector2(0.175f, 0.405f), new Vector2(0.645f, 0.535f), "cubuk_mavi");
            _taskReward[i] = EtkinlikOdulSatiri.Create(card, "Odul", new Vector2(0.175f, 0.21f), new Vector2(0.645f, 0.41f),
                                                      22, EkranKit.PaperSoft, TextAnchor.MiddleLeft);
            int captured = i;
            _taskBtn[i] = EtkinlikKit.Capsule(card, "Al", new Vector2(0.670f, 0.27f), new Vector2(0.955f, 0.73f),
                                              () => ClaimTask(captured), out _taskBtnText[i]);
        }

        /// <summary>
        /// A chest that grows richer up the ladder, the token count that opens it, and two columns —
        /// the free reward over its claim, the premium reward over its own.
        ///
        /// With no premium store id the premium column is not built into view: the free reward takes
        /// both columns' width and its claim sits centred under it. The capsule keeps its size — only
        /// the text slot widens — so the button art never stretches.
        /// </summary>
        private void BuildTier(RectTransform content, int i)
        {
            RectTransform card = EtkinlikKit.Row(content, "Kademe" + i, i, TierPitch, TierPitch - RowGap, BorderScale).rectTransform;
            _tierIcon[i] = EkranKit.Icon(card, "Simge", EtkinlikKit.Chest(i * 4 / HarborFestival.TierCount),
                                         new Vector2(0.030f, 0.24f), new Vector2(0.150f, 0.76f));
            _tierTokens[i] = EtkinlikKit.Label(card, "Jeton", new Vector2(0.170f, 0.50f), new Vector2(0.425f, 0.78f),
                                               string.Empty, 34, TextAnchor.MiddleLeft, EkranKit.Paper, 18);
            _tierCaption[i] = EtkinlikKit.Label(card, "Aciklama", new Vector2(0.170f, 0.22f), new Vector2(0.425f, 0.50f),
                                                string.Empty, 20, TextAnchor.MiddleLeft, EkranKit.PaperSoft, 11);

            int captured = i;
            _freeReward[i] = EtkinlikOdulSatiri.Create(card, "UcretsizOdul", new Vector2(0.440f, 0.52f), new Vector2(0.690f, 0.77f),
                                                      22, EkranKit.Paper, TextAnchor.MiddleCenter);
            _freeBtn[i] = EtkinlikKit.Capsule(card, "UcretsizAl", new Vector2(0.445f, 0.21f), new Vector2(0.685f, 0.51f),
                                              () => ClaimTier(captured, false), out _freeText[i]);

            _premiumReward[i] = EtkinlikOdulSatiri.Create(card, "PremiumOdul", new Vector2(0.705f, 0.52f), new Vector2(0.955f, 0.77f),
                                                         22, EkranKit.Paper, TextAnchor.MiddleCenter);
            _premiumBtn[i] = EtkinlikKit.Capsule(card, "PremiumAl", new Vector2(0.710f, 0.21f), new Vector2(0.950f, 0.51f),
                                                 () => ClaimTier(captured, true), out _premiumText[i]);

            if (_premiumShown) return;
            _premiumReward[i].Root.gameObject.SetActive(false);
            _premiumBtn[i].gameObject.SetActive(false);

            _freeReward[i].Root.anchorMin = new Vector2(0.440f, 0.52f);
            _freeReward[i].Root.anchorMax = new Vector2(0.955f, 0.77f);
            var freeButton = (RectTransform)_freeBtn[i].transform;
            freeButton.anchorMin = new Vector2(0.5775f, 0.21f);
            freeButton.anchorMax = new Vector2(0.8175f, 0.51f);
        }

        /// <summary>What the item pays, what it costs in tokens, and the orange trade button.</summary>
        private void BuildItem(RectTransform content, int i)
        {
            Image card = EtkinlikKit.Row(content, "Urun" + i, i, ItemPitch, ItemPitch - RowGap, BorderScale);
            _itemRow[i] = card.rectTransform;
            _itemIcon[i] = EkranKit.Icon(card.rectTransform, "Simge", null, new Vector2(0.030f, 0.24f), new Vector2(0.150f, 0.76f));
            _itemReward[i] = EtkinlikOdulSatiri.Create(card.rectTransform, "Odul", new Vector2(0.175f, 0.50f), new Vector2(0.645f, 0.77f),
                                                      28, EkranKit.Paper, TextAnchor.MiddleLeft);
            _itemCost[i] = EtkinlikKit.Label(card.rectTransform, "Bedel", new Vector2(0.175f, 0.22f), new Vector2(0.645f, 0.50f),
                                             string.Empty, 22, TextAnchor.MiddleLeft, EkranKit.PaperSoft, 12);
            int captured = i;
            _itemBtn[i] = EtkinlikKit.Capsule(card.rectTransform, "Takas", new Vector2(0.670f, 0.27f), new Vector2(0.955f, 0.73f),
                                              () => Redeem(captured), out _itemBtnText[i]);
        }

        /// <summary>The one row a closed festival's catalogue still has: its unspent tokens, as gems.</summary>
        private void BuildConversion(RectTransform content)
        {
            Image card = EtkinlikKit.Row(content, "Donusum", 0, ItemPitch, ItemPitch - RowGap, BorderScale);
            _conversionRow = card.rectTransform;
            EkranKit.Icon(card.rectTransform, "Simge", EtkinlikKit.Gem, new Vector2(0.030f, 0.24f), new Vector2(0.150f, 0.76f));
            _conversionTitle = EtkinlikKit.Label(card.rectTransform, "Ad", new Vector2(0.175f, 0.50f), new Vector2(0.645f, 0.77f),
                                                 string.Empty, 26, TextAnchor.MiddleLeft, EkranKit.Paper, 14);
            _conversionReward = EtkinlikOdulSatiri.Create(card.rectTransform, "Odul", new Vector2(0.175f, 0.22f), new Vector2(0.645f, 0.50f),
                                                         24, EkranKit.PaperSoft, TextAnchor.MiddleLeft);
            _conversionBtn = EtkinlikKit.Capsule(card.rectTransform, "Al", new Vector2(0.670f, 0.27f), new Vector2(0.955f, 0.73f),
                                                 ClaimConversion, out _conversionBtnText);
            _conversionRow.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ claim
        private void SelectTab(int tab)
        {
            _tab = tab;
            Refresh();
        }

        private void ClaimTask(int index)
        {
            if (_festival == null) return;
            HarborFestival.Reward reward = _festival.TaskAt(index).Reward;
            if (_festival.ClaimTask(index)) Present(reward);
            Refresh();
        }

        private void ClaimTier(int index, bool premium)
        {
            if (_festival == null) return;
            HarborFestival.Tier tier = _festival.TierAt(index);
            bool paid = premium ? _festival.ClaimPremiumTier(index) : _festival.ClaimFreeTier(index);
            if (paid) Present(premium ? tier.Premium : tier.Free);
            Refresh();
        }

        private void Redeem(int index)
        {
            if (_festival == null) return;
            HarborFestival.Reward reward = _festival.CatalogueAt(index).Reward;
            if (_festival.Redeem(index)) Present(reward);
            Refresh();
        }

        private void ClaimConversion()
        {
            if (_festival == null) return;
            int gems = _festival.ExpiryGems;
            if (_festival.ClaimExpiryConversion()) _popup.Present(gems, 0, 0L, null);
            Refresh();
        }

        private void Present(in HarborFestival.Reward reward)
            => _popup.Present(reward.Gems, reward.Cards, reward.Charts, Boost(reward));

        // ---------------------------------------------------------------- refresh
        private void Refresh()
        {
            if (_root == null || !_root.gameObject.activeSelf) return;
            _title.text = Loc.T("liman.baslik");

            bool available = _festival != null && _festival.Available;
            EtkinlikKit.SetActive(_body, available);
            EtkinlikKit.SetActive(_empty, !available);
            if (!available)
            {
                _emptyLabel.text = Loc.T("liman.yok");
                return;
            }

            for (int i = 0; i < TabCount; i++)
            {
                _tabText[i].text = EtkinlikKit.OneLine(Loc.T(TabKeys[i]));
                EtkinlikKit.SetTab(_tabs[i], i == _tab);
                EtkinlikKit.SetActive(_lists[i], i == _tab);
            }

            _tokens.text = _festival.TokenBalance + " " + Loc.T("liman.jeton");
            switch (_festival.Phase)
            {
                case LiveEvents.Phase.Active:
                    _clock.text = Loc.T("etkinlik.kalan") + " " + HudUI.LongClock(_festival.SecondsLeft);
                    break;
                case LiveEvents.Phase.Upcoming:
                    _clock.text = Loc.T("etkinlik.yakinda");
                    break;
                default:
                    _clock.text = Loc.T("senlik.bitti");
                    break;
            }

            if (_tab == TabTasks) RefreshTasks();
            else if (_tab == TabTiers) RefreshTiers();
            else RefreshCatalogue();
        }

        private void RefreshTasks()
        {
            string tokenName = Loc.T("liman.jeton");
            for (int i = 0; i < HarborFestival.TaskCount; i++)
            {
                HarborFestival.Task task = _festival.TaskAt(i);
                long have = _festival.TaskProgress(i);
                bool claimed = _festival.TaskClaimed(i);
                bool can = _festival.TaskDone(i) && !claimed;

                _taskName[i].text = MetricName(task.Metric);
                _taskCount[i].text = have + " / " + task.Target;
                EtkinlikKit.Progress(_taskFill[i], Goals.Progress(have, task.Target));
                _taskReward[i].Set("+" + task.Tokens + " " + tokenName,
                                   task.Reward.Gems, task.Reward.Cards, task.Reward.Charts, Boost(task.Reward));
                _taskIcon[i].color = claimed ? EtkinlikKit.Faded : Color.white;

                _taskBtnText[i].text = EtkinlikKit.OneLine(Loc.T(claimed ? "gorev.alindi" : "gorev.al"));
                EtkinlikKit.SetFace(_taskBtn[i], _taskBtnText[i], can ? EtkinlikKit.Face.Claim : EtkinlikKit.Face.Dead, can);
            }
        }

        private void RefreshTiers()
        {
            int earned = _festival.TokensEarned;
            bool owned = _festival.PremiumOwned;
            string tokenName = Loc.T("liman.jeton");

            for (int i = 0; i < HarborFestival.TierCount; i++)
            {
                HarborFestival.Tier tier = _festival.TierAt(i);
                bool reached = earned >= tier.Tokens;

                _tierTokens[i].text = tier.Tokens.ToString();
                _tierCaption[i].text = tokenName;
                _tierIcon[i].color = reached ? Color.white : EtkinlikKit.Faded;

                _freeReward[i].Set(null, tier.Free.Gems, tier.Free.Cards, tier.Free.Charts, Boost(tier.Free));
                bool freeClaimed = _festival.FreeTierClaimed(i);
                bool canFree = _festival.CanClaimFreeTier(i);
                _freeText[i].text = EtkinlikKit.OneLine(freeClaimed ? Loc.T("gorev.alindi") : Loc.T("liman.ucretsiz"));
                EtkinlikKit.SetFace(_freeBtn[i], _freeText[i], canFree ? EtkinlikKit.Face.Claim : EtkinlikKit.Face.Dead, canFree);

                if (!_premiumShown) continue;
                _premiumReward[i].Set(null, tier.Premium.Gems, tier.Premium.Cards, tier.Premium.Charts, Boost(tier.Premium));
                bool premiumClaimed = _festival.PremiumTierClaimed(i);
                bool canPremium = _festival.CanClaimPremiumTier(i);
                _premiumText[i].text = EtkinlikKit.OneLine(!owned ? Loc.T("liman.kilitli")
                                     : premiumClaimed ? Loc.T("gorev.alindi") : Loc.T("liman.premium"));
                EtkinlikKit.SetFace(_premiumBtn[i], _premiumText[i],
                                    canPremium ? EtkinlikKit.Face.Primary : EtkinlikKit.Face.Dead, canPremium);
            }
        }

        private void RefreshCatalogue()
        {
            bool closed = _festival.Phase == LiveEvents.Phase.Closed;
            int expiry = closed ? _festival.ExpiryGems : 0;

            for (int i = 0; i < HarborFestival.CatalogueCount; i++) EtkinlikKit.SetActive(_itemRow[i], !closed);
            EtkinlikKit.SetActive(_conversionRow, closed && expiry > 0);

            if (closed)
            {
                EtkinlikKit.SetRows(_catalogueContent, expiry > 0 ? 1 : 0, ItemPitch);
                if (expiry <= 0) return;
                _conversionTitle.text = Loc.T("liman.donusum");
                _conversionReward.Set(null, expiry, 0, 0L, null);
                _conversionBtnText.text = EtkinlikKit.OneLine(Loc.T("gorev.al"));
                EtkinlikKit.SetFace(_conversionBtn, _conversionBtnText, EtkinlikKit.Face.Claim, true);
                return;
            }

            EtkinlikKit.SetRows(_catalogueContent, HarborFestival.CatalogueCount, ItemPitch);
            string tokenName = Loc.T("liman.jeton");
            for (int i = 0; i < HarborFestival.CatalogueCount; i++)
            {
                HarborFestival.CatalogueItem item = _festival.CatalogueAt(i);
                bool claimed = _festival.CatalogueClaimed(i);
                bool can = _festival.CanRedeem(i);

                Sprite icon = RewardIcon(item.Reward);
                if (_itemIcon[i].sprite != icon)
                {
                    _itemIcon[i].sprite = icon;
                    _itemIcon[i].enabled = icon != null;
                }
                _itemIcon[i].color = claimed ? EtkinlikKit.Faded : Color.white;
                _itemReward[i].Set(null, item.Reward.Gems, item.Reward.Cards, item.Reward.Charts, Boost(item.Reward));
                _itemCost[i].text = item.Cost + " " + tokenName;

                _itemBtnText[i].text = EtkinlikKit.OneLine(claimed ? Loc.T("gorev.alindi") : Loc.T("liman.takas"));
                EtkinlikKit.SetFace(_itemBtn[i], _itemBtnText[i], can ? EtkinlikKit.Face.Primary : EtkinlikKit.Face.Dead, can);
            }
        }

        // ------------------------------------------------------------------ pieces
        private static string Boost(in HarborFestival.Reward reward)
            => EtkinlikKit.Boost(reward.BoostMult, reward.BoostSeconds);

        /// <summary>The icon for what a reward mostly is; a boost-only reward gets the chest.</summary>
        private static Sprite RewardIcon(in HarborFestival.Reward reward)
        {
            if (reward.Gems > 0L) return EtkinlikKit.Gem;
            if (reward.Cards > 0) return EtkinlikKit.MasterCard;
            if (reward.Charts > 0L) return EtkinlikKit.Chart;
            return EkranKit.Get("sandik");
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
    }
}
