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
        [SerializeField, Min(0.1f)] private float refreshSeconds = 0.3f;

        private static readonly YardUpgrade[] Tracks = { YardUpgrade.DepositSlot, YardUpgrade.QueueSlot,
            YardUpgrade.HireCarry, YardUpgrade.HireServe, YardUpgrade.HireCollect, YardUpgrade.CarryCapacity };
        private static readonly string[] Names = { "Stock capacity", "Customer capacity", "Restocking crew",
            "Sales crew", "Order dispatch", "Porter load" };
        private static readonly string[] TurkishNames = { "Stok kapasitesi", "Müşteri kapasitesi", "İkmal ekibi",
            "Satış ekibi", "Sipariş hazırlama", "Taşıma kapasitesi" };
        private static readonly Color Ink = new Color(0.08f, 0.13f, 0.19f);
        private static readonly Color Paper = new Color(0.97f, 0.98f, 1f);
        private static readonly Color LightInk = new Color(0.78f, 0.88f, 1f);
        private static readonly Color Scrim = new Color(0.015f, 0.035f, 0.08f, 0.90f);
        private static readonly Color Gold = new Color(0.98f, 0.72f, 0.24f);
        private readonly Button[] _buy = new Button[6];
        private readonly Text[] _names = new Text[6], _levels = new Text[6], _prices = new Text[6];
        private readonly Image[] _rowArt = new Image[6];
        private readonly Image[] _icons = new Image[6];
        private MarketService _market;
        private WalletService _wallet;
        private Action _persist;
        private RectTransform _sheet;
        private GameObject _overlay;
        private Text _title, _cash, _hint;
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

        /// <summary>The HUD's dedicated market button enters the active island's yard.</summary>
        public void ShowActiveIsland()
        {
            EnsureConfigured();
            if (_market != null) Show(_market.ActiveIsland);
        }

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
            EnsureConfigured();
            Refresh();
        }

        private void EnsureConfigured()
        {
            if (_configured) return;
            var market = ServiceLocator.Get<MarketService>();
            var wallet = ServiceLocator.Get<WalletService>();
            var data = ServiceLocator.Get<SaveData>();
            var save = ServiceLocator.Get<SaveService>();
            // Never construct a market or placeholder save to make the panel available.
            if (market != null && wallet != null && data != null && save != null)
                Configure(market, wallet, () => save.Save(data));
        }

        public void Show(string islandKey)
        {
            EnsureConfigured();
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
            var veil = UiBuild.Flat(area, "YardUpgradeOverlay", Scrim, Vector2.zero, Vector2.one);
            _overlay = veil.gameObject;
            _sheet = UiBuild.Flat(veil, "YardUpgradeSheet", Paper, new Vector2(0.055f, 0.10f), new Vector2(0.945f, 0.90f));
            Dress(_sheet.GetComponent<Image>(), TycoonUpgradeArt.Panel, Paper);

            RectTransform titlePlate = UiBuild.Flat(_sheet, "TitlePlate", Color.white,
                new Vector2(0.05f, 0.84f), new Vector2(0.66f, 0.966f));
            Dress(titlePlate.GetComponent<Image>(), TycoonUpgradeArt.Title, Color.white);
            titlePlate.GetComponent<Image>().preserveAspect = true;
            _title = Label(titlePlate, "Title", 30, new Vector2(0.20f, 0.13f), new Vector2(0.80f, 0.87f), Color.white);
            _title.alignment = TextAnchor.MiddleCenter;
            var closeArt = TycoonUpgradeArt.Close;
            var close = UiBuild.Btn(_sheet, "Close", string.Empty, closeArt != null ? closeArt : UiSkin.ButtonGrey,
                                    Color.white, 32, Hide);
            UiBuild.Anchor((RectTransform)close.transform, new Vector2(0.91f, 0.895f), new Vector2(0.99f, 0.965f));
            close.GetComponent<Image>().preserveAspect = true;

            RectTransform wallet = UiBuild.Flat(_sheet, "WalletPlate", Paper, new Vector2(0.66f, 0.865f), new Vector2(0.90f, 0.925f));
            Dress(wallet.GetComponent<Image>(), TycoonUpgradeArt.Wallet, Paper);
            wallet.GetComponent<Image>().preserveAspect = true;
            Art(wallet, "CashIcon", TycoonUpgradeArt.CashIcon,
                new Vector2(0.03f, 0.05f), new Vector2(0.42f, 0.95f));
            _cash = Label(wallet, "Wallet", 24, new Vector2(0.43f, 0.10f), new Vector2(0.91f, 0.90f), Color.white);
            _cash.alignment = TextAnchor.MiddleCenter;
            _hint = Label(_sheet, "AutomaticWork", 20, new Vector2(0.09f, 0.08f), new Vector2(0.91f, 0.125f), LightInk);
            _hint.alignment = TextAnchor.MiddleCenter;
            for (int i = 0; i < Tracks.Length; i++)
            {
                int captured = i;
                float top = 0.80f - i * 0.112f;
                var row = UiBuild.Flat(_sheet, "Track_" + Tracks[i], Paper, new Vector2(0.06f, top - 0.098f), new Vector2(0.94f, top));
                _rowArt[i] = row.GetComponent<Image>();
                Dress(_rowArt[i], TycoonUpgradeArt.Card, Paper);
                _icons[i] = Art(row, "Icon", TycoonUpgradeArt.YardIcon(i),
                    new Vector2(0.025f, 0.12f), new Vector2(0.19f, 0.88f));
                _names[i] = Label(row, "Name", 27, new Vector2(0.20f, 0.48f), new Vector2(0.67f, 0.94f), Color.white);
                _levels[i] = Label(row, "Level", 20, new Vector2(0.20f, 0.08f), new Vector2(0.67f, 0.48f), LightInk);
                Sprite buyArt = TycoonUpgradeArt.Buy;
                _buy[i] = UiBuild.Btn(row, "Buy_" + Tracks[i], "", buyArt != null ? buyArt : UiSkin.ButtonYellow,
                                      buyArt != null ? Color.white : Gold, 23, () => Purchase(Tracks[captured]));
                UiBuild.Anchor((RectTransform)_buy[i].transform, new Vector2(0.68f, 0.12f), new Vector2(0.985f, 0.88f));
                _prices[i] = _buy[i].GetComponentInChildren<Text>();
                _prices[i].color = Ink;
                Fit(_prices[i], 23);
            }
            Hide();
            Refresh();
        }

        private static void Dress(Image image, Sprite art, Color fallback)
        {
            if (image == null) return;
            image.sprite = art != null ? art : UiSkin.Panel;
            image.type = image.sprite != null && image.sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            image.color = art != null ? Color.white : fallback;
        }

        private static Text Label(Transform parent, string name, int size, Vector2 min, Vector2 max, Color color)
        {
            var label = UiBuild.Label(parent, name, "", size, TextAnchor.MiddleLeft);
            UiBuild.Anchor((RectTransform)label.transform, min, max);
            label.color = color;
            Fit(label, size);
            return label;
        }

        private static Image Art(Transform parent, string name, Sprite sprite, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            UiBuild.Anchor(rect, min, max);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = Color.white;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
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
