using System;
using Game.Core;
using Game.Systems;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>Island purchases only. The market service owns prices, spending and progression.</summary>
    public sealed class IslandYardUpgradeUI : MonoBehaviour
    {
        [SerializeField] private int sortingOrder = 180;
        [SerializeField] private Vector2 openerMin = new Vector2(0.70f, 0.30f);
        [SerializeField] private Vector2 openerMax = new Vector2(0.97f, 0.355f);
        [SerializeField, Min(0.1f)] private float refreshSeconds = 0.3f;

        private static readonly YardUpgrade[] Tracks = { YardUpgrade.DepositSlot, YardUpgrade.QueueSlot,
            YardUpgrade.HireCarry, YardUpgrade.HireServe, YardUpgrade.HireCollect, YardUpgrade.CarryCapacity };
        private static readonly string[] Names = { "Stock capacity", "Customer capacity", "Restocking crew",
            "Sales crew", "Order dispatch", "Porter load" };
        private static readonly string[] TurkishNames = { "Stok kapasitesi", "Müşteri kapasitesi", "İkmal ekibi",
            "Satış ekibi", "Sipariş hazırlama", "Taşıma kapasitesi" };
        private static readonly Color Ink = new Color(0.08f, 0.13f, 0.19f);
        private static readonly Color Gold = new Color(0.98f, 0.72f, 0.24f);
        private readonly Button[] _buy = new Button[6];
        private readonly Text[] _names = new Text[6], _levels = new Text[6], _prices = new Text[6];
        private MarketService _market;
        private WalletService _wallet;
        private Action _persist;
        private RectTransform _sheet;
        private GameObject _overlay;
        private Button _opener;
        private Text _title, _cash, _hint, _openerText;
        private string _island;
        private float _untilRefresh;
        private bool _configured;
        public bool IsOpen => _overlay != null && _overlay.activeSelf;
        public string IslandKey => _island;

        // Install only into Main, without editing the bootstrap or dirty scene files owned by B.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            SceneManager.sceneLoaded += SceneLoaded;
            SceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "Main") return;
            foreach (var root in scene.GetRootGameObjects())
                if (root.GetComponentInChildren<IslandYardUpgradeUI>(true) != null) return;
            var host = new GameObject("IslandYardUpgrades");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<IslandYardUpgradeUI>();
        }

        private void Awake() => Build();

        public void Configure(MarketService market, WalletService wallet, Action persist = null)
        {
            _market = market;
            _wallet = wallet;
            _persist = persist;
            _configured = true;
            if (_overlay == null) Build();
            Refresh();
        }

        private void Update()
        {
            _untilRefresh -= Time.unscaledDeltaTime;
            if (_untilRefresh > 0f) return;
            _untilRefresh = refreshSeconds;
            if (!_configured)
            {
                var market = ServiceLocator.Get<MarketService>();
                var wallet = ServiceLocator.Get<WalletService>();
                var data = ServiceLocator.Get<SaveData>();
                var save = ServiceLocator.Get<SaveService>();
                // Never construct a market or placeholder save to make the panel available.
                if (market != null && wallet != null && data != null && save != null)
                    Configure(market, wallet, () => save.Save(data));
            }
            Refresh();
        }

        public void Show(string islandKey)
        {
            if (_market == null || _wallet == null || Chapters.Of(islandKey) < 0) return;
            _island = islandKey;
            _overlay.SetActive(true);
            Refresh();
        }

        public void Hide() => _overlay.SetActive(false);

        public bool Purchase(YardUpgrade kind)
        {
            int index = Array.IndexOf(Tracks, kind);
            if (!IsOpen || index < 0 || _market == null || _wallet == null) return false;
            FollowIsland(); // A queued click after travel must not spend on the old island.
            if (Chapters.Of(_island) < 0) return false;
            bool bought = _market.TryBuy(_island, kind);
            if (bought) _persist?.Invoke();
            Refresh();
            return bought;
        }

        private void FollowIsland()
        {
            if (_market != null && Chapters.Of(_market.ActiveIsland) >= 0) _island = _market.ActiveIsland;
        }

        public void Refresh()
        {
            if (_overlay == null) return;
            bool ready = _market != null && _wallet != null;
            _opener.interactable = ready && Chapters.Of(_market.ActiveIsland) >= 0;
            _openerText.text = Tr("YARD UPGRADES", "PAZAR GELİŞTİR");
            if (!IsOpen || !ready) return;
            FollowIsland();
            if (Chapters.Of(_island) < 0) { Hide(); return; }
            Put(_title, Loc.Id("ada", _island) + " · " + Tr("Market", "Pazar"));
            Put(_cash, Tr("Coins  ", "Para  ") + NumberFormatter.Format(_wallet.Cash));
            Put(_hint, Tr("Your crew works and sells automatically.", "Ekibin otomatik çalışır ve satış yapar."));
            bool tr = ServiceLocator.Get<LocalizationService>()?.Code == "tr";
            for (int i = 0; i < Tracks.Length; i++)
            {
                var kind = Tracks[i];
                int level = _market.Level(_island, kind);
                bool maxed = _market.IsTrackMaxed(_island, kind);
                double price = _market.Cost(_island, kind);
                Put(_names[i], tr ? TurkishNames[i] : Names[i]);
                string scope = kind == YardUpgrade.CarryCapacity ? Tr("All islands", "Tüm adalar") : Tr("This island", "Bu ada");
                Put(_levels[i], scope + "  ·  " + level + " / " + MarketPrices.MaxLevel(kind));
                Put(_prices[i], maxed ? Tr("MAX", "MAKS") : price <= 0d ? Tr("Loading…", "Yükleniyor…")
                    : "+1  ·  " + NumberFormatter.Format(new BigDouble(price)));
                _buy[i].interactable = !maxed && price > 0d && _wallet.Cash >= new BigDouble(price);
            }
        }

        private static void Put(Text label, string value) { if (label.text != value) label.text = value; }
        private static string Tr(string en, string tr) => ServiceLocator.Get<LocalizationService>()?.Code == "tr" ? tr : en;

        private void Build()
        {
            if (_overlay != null) return;
            var canvas = UiBuild.Canvas(transform, "IslandYardCanvas", sortingOrder);
            UiBuild.EnsureEventSystem(transform);
            var safe = new GameObject("SafeArea", typeof(RectTransform));
            safe.transform.SetParent(canvas, false);
            var area = UiBuild.Anchor((RectTransform)safe.transform, Vector2.zero, Vector2.one);
            safe.AddComponent<SafeArea>();
            _opener = UiBuild.Btn(area, "OpenYardUpgrades", "", UiSkin.ButtonYellow, Gold, 24,
                () => Show(_market != null ? _market.ActiveIsland : null));
            UiBuild.Anchor((RectTransform)_opener.transform, openerMin, openerMax);
            _openerText = _opener.GetComponentInChildren<Text>();
            _openerText.color = Ink;
            Fit(_openerText, 24);

            var veil = UiBuild.Flat(area, "YardUpgradeOverlay", new Color(0.02f, 0.04f, 0.08f, 0.72f), Vector2.zero, Vector2.one);
            _overlay = veil.gameObject;
            _sheet = UiBuild.Flat(veil, "YardUpgradeSheet", new Color(0.94f, 0.95f, 0.93f), new Vector2(0.045f, 0.15f), new Vector2(0.955f, 0.85f));
            _title = Label(_sheet, "Title", 32, new Vector2(0.04f, 0.925f), new Vector2(0.82f, 0.985f), Ink);
            var close = UiBuild.Btn(_sheet, "Close", "×", UiSkin.Flat, Ink, 32, Hide);
            close.GetComponentInChildren<Text>().color = Ink;
            UiBuild.Anchor((RectTransform)close.transform, new Vector2(0.87f, 0.928f), new Vector2(0.97f, 0.98f));
            _cash = Label(_sheet, "Wallet", 26, new Vector2(0.04f, 0.865f), new Vector2(0.96f, 0.92f), Ink);
            _hint = Label(_sheet, "AutomaticWork", 21, new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.075f), Ink);
            for (int i = 0; i < Tracks.Length; i++)
            {
                int captured = i;
                float top = 0.85f - i * 0.127f;
                var row = UiBuild.Flat(_sheet, "Track_" + Tracks[i], Color.white, new Vector2(0.035f, top - 0.115f), new Vector2(0.965f, top));
                _names[i] = Label(row, "Name", 28, new Vector2(0.025f, 0.48f), new Vector2(0.64f, 0.94f), Ink);
                _levels[i] = Label(row, "Level", 21, new Vector2(0.025f, 0.08f), new Vector2(0.64f, 0.48f), Ink);
                _buy[i] = UiBuild.Btn(row, "Buy_" + Tracks[i], "", UiSkin.ButtonYellow, Gold, 23, () => Purchase(Tracks[captured]));
                UiBuild.Anchor((RectTransform)_buy[i].transform, new Vector2(0.67f, 0.17f), new Vector2(0.98f, 0.83f));
                _prices[i] = _buy[i].GetComponentInChildren<Text>();
                _prices[i].color = Ink;
                Fit(_prices[i], 23);
            }
            Hide();
            Refresh();
        }

        private static Text Label(Transform parent, string name, int size, Vector2 min, Vector2 max, Color color)
        {
            var label = UiBuild.Label(parent, name, "", size, TextAnchor.MiddleLeft);
            UiBuild.Anchor((RectTransform)label.transform, min, max);
            label.color = color;
            Fit(label, size);
            return label;
        }

        private static void Fit(Text label, int size)
        {
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 16;
            label.resizeTextMaxSize = size;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
        }
    }
}
