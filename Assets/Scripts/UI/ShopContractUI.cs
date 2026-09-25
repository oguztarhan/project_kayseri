using System;
using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The shop contract screen: this half hour's order with what it pays, an Accept button, and — once a contract
    /// is running — how far the contract customer's order has come. What the offer shows is exactly what
    /// <see cref="ShopContractService.Accept"/> would lock in: its quantity and price are read from the bench at the
    /// moment the screen refreshes.
    ///
    /// DRAWN FROM <see cref="EtkinlikKit"/>, like the events screens and the port contract screen it replaces in the
    /// shop: sheet, ribbon, one navy card, a green capsule. Built in code and added by <see cref="HudUI"/>, so no
    /// scene or prefab carries it. Nothing is paid here — the celebration (<see cref="ShopContractCelebrationUI"/>)
    /// shows the payment when the contract completes.
    ///
    /// The text is only rewritten when what it says changes: the screen ticks twice a second for its clock, and a
    /// board that sits open for minutes should not build the same strings every tick.
    /// </summary>
    public sealed class ShopContractUI : MonoBehaviour
    {
        [SerializeField] private int sortingOrder = 113;
        [SerializeField] private Color scrim = new Color(0.03f, 0.05f, 0.10f, 1f);
        [Tooltip("Primi öne çıkaran altın yazı rengi.")]
        [SerializeField] private Color premiumInk = new Color(1f, 0.79f, 0.27f, 1f);
        [SerializeField] private float refreshInterval = 0.5f;

        private static readonly string[] SizeKeys = { "dukkan_kontrat.boyut0", "dukkan_kontrat.boyut1", "dukkan_kontrat.boyut2" };

        private ShopContractService _contracts;
        private MarketService _market;
        private ShopContractConfig _config;
        private LocalizationService _loc;

        private RectTransform _canvas, _root, _card, _empty;
        private Text _title, _clock, _emptyLabel;
        private Image _icon;
        private Text _size, _order, _time, _premium, _note, _count, _acceptLabel, _status;
        private EtkinlikOdulSatiri _reward;
        private Image _fill;
        private Button _accept;
        private float _tick;

        // What is on show, so a tick that changes nothing writes nothing. Mode: 0 locked, 1 offer, 2 active, 3 taken.
        private int _shownMode = -1, _shownProduct = -1, _shownSize = -1, _shownQuantity = -1, _shownDelivered = -1;
        private double _shownCash = -1d;
        private int _shownClock = -1;
        private long _offerSlot;

        /// <summary>The screen's own canvas, which the celebration may draw over.</summary>
        public Canvas Canvas => _canvas != null ? _canvas.GetComponent<Canvas>() : null;

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;

        private void Awake()
        {
            _contracts = ServiceLocator.Get<ShopContractService>();
            _market = ServiceLocator.Get<MarketService>();
            _config = ServiceLocator.Get<ShopContractConfig>();
            _loc = ServiceLocator.Get<LocalizationService>();
            Build();
            if (_contracts != null) _contracts.Changed += Redraw;
            if (_loc != null) _loc.Changed += Redraw;
            Hide();
        }

        private void OnDestroy()
        {
            if (_contracts != null) _contracts.Changed -= Redraw;
            if (_loc != null) _loc.Changed -= Redraw;
        }

        public void Toggle()
        {
            if (IsOpen) Hide();
            else Show();
        }

        public void Show()
        {
            if (_root == null) return;
            _root.gameObject.SetActive(true);
            Redraw();
        }

        public void Hide()
        {
            if (_root != null) _root.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!IsOpen) return;
            _tick -= Time.unscaledDeltaTime;
            if (_tick > 0f) return;
            _tick = refreshInterval;
            Refresh();
        }

        // ------------------------------------------------------------------ build
        private void Build()
        {
            _canvas = UiBuild.Canvas(transform, "DukkanKontratKanvas", sortingOrder);
            _root = UiBuild.Flat(_canvas, "Karartma", UiBuild.Opaque(scrim), Vector2.zero, Vector2.one);
            Button dismiss = _root.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(Hide);

            RectTransform sheet = EtkinlikKit.Sheet(_root).rectTransform;
            _title = EtkinlikKit.Header(_root, Loc.T("kontrat.baslik"), Hide);
            _clock = EtkinlikKit.Label(sheet, "Saat", new Vector2(0.090f, 0.705f), new Vector2(0.910f, 0.765f),
                                       string.Empty, 26, TextAnchor.MiddleCenter, EtkinlikKit.InkSoft, 13);

            _card = EtkinlikKit.Card(sheet, "Kart", new Vector2(0.085f, 0.200f), new Vector2(0.915f, 0.690f), 1.2f).rectTransform;
            _icon = EkranKit.Icon(_card, "Simge", null, new Vector2(0.06f, 0.60f), new Vector2(0.27f, 0.92f));
            _size = EtkinlikKit.Label(_card, "Boyut", new Vector2(0.30f, 0.81f), new Vector2(0.94f, 0.92f),
                                      string.Empty, 26, TextAnchor.MiddleLeft, EkranKit.PaperSoft, 13);
            _order = EtkinlikKit.Label(_card, "Siparis", new Vector2(0.30f, 0.63f), new Vector2(0.94f, 0.81f),
                                       string.Empty, 46, TextAnchor.MiddleLeft, EkranKit.Paper, 20);
            _time = EtkinlikKit.Label(_card, "Sure", new Vector2(0.30f, 0.54f), new Vector2(0.94f, 0.63f),
                                      string.Empty, 24, TextAnchor.MiddleLeft, EkranKit.PaperSoft, 12);
            _reward = EtkinlikOdulSatiri.Create(_card, "Odul", new Vector2(0.06f, 0.37f), new Vector2(0.94f, 0.52f),
                                                38, EkranKit.Paper, TextAnchor.MiddleCenter);
            _premium = EtkinlikKit.Label(_card, "Prim", new Vector2(0.06f, 0.27f), new Vector2(0.94f, 0.37f),
                                         string.Empty, 26, TextAnchor.MiddleCenter, premiumInk, 12);
            _note = EtkinlikKit.Label(_card, "Not", new Vector2(0.06f, 0.07f), new Vector2(0.94f, 0.22f),
                                      string.Empty, 22, TextAnchor.MiddleCenter, EkranKit.PaperSoft, 11);
            _fill = EtkinlikKit.Bar(_card, "Ilerleme", new Vector2(0.08f, 0.15f), new Vector2(0.92f, 0.23f), "cubuk_altin");
            _count = EtkinlikKit.Label(_card, "Sayi", new Vector2(0.06f, 0.04f), new Vector2(0.94f, 0.14f),
                                       string.Empty, 26, TextAnchor.MiddleCenter, EkranKit.Paper, 12);

            _accept = EtkinlikKit.Capsule(sheet, "KabulEt", new Vector2(0.250f, 0.070f), new Vector2(0.750f, 0.170f),
                                          OnAccept, out _acceptLabel);
            EtkinlikKit.SetFace(_accept, _acceptLabel, EtkinlikKit.Face.Claim, true);
            _status = EtkinlikKit.Label(sheet, "Durum", new Vector2(0.090f, 0.070f), new Vector2(0.910f, 0.170f),
                                        string.Empty, 28, TextAnchor.MiddleCenter, EkranKit.Ink, 14);

            _empty = EtkinlikKit.Empty(sheet, string.Empty, out _emptyLabel);
            UiBuild.InsetContent(_root);
        }

        // ---------------------------------------------------------------- refresh
        /// <summary>Forgets what is on show and draws everything again: a language change, an accept, a cancel.</summary>
        private void Redraw()
        {
            _shownMode = _shownProduct = _shownSize = _shownQuantity = _shownDelivered = _shownClock = -1;
            _shownCash = -1d;
            if (_title != null) _title.text = Loc.T("kontrat.baslik");
            if (_acceptLabel != null) _acceptLabel.text = EtkinlikKit.OneLine(Loc.T("kontrat.kabul"));
            _tick = 0f;
            if (IsOpen) Refresh();
        }

        private void Refresh()
        {
            if (_contracts == null || _market == null || _market.MiningShopBusiness == null) return;
            MiningShopBusinessSimulation.Snapshot view = _market.MiningShopBusiness.View;

            int mode;
            ShopContractService.Offer offer = default;
            if (!_contracts.Unlocked) mode = 0;
            else if (view.ContractActive) mode = 2;
            else if (_contracts.TryGetOffer(out offer)) mode = 1;
            else mode = 3;

            if (mode != _shownMode)
            {
                _shownMode = mode;
                _shownProduct = _shownSize = _shownQuantity = _shownDelivered = -1;
                _shownCash = -1d;
                bool card = mode == 1 || mode == 2;
                EtkinlikKit.SetActive(_card, card);
                EtkinlikKit.SetActive(_empty, !card);
                EtkinlikKit.SetActive(_clock.transform.parent, mode != 0);
                EtkinlikKit.SetActive(_accept, mode == 1);
                EtkinlikKit.SetActive(_status.transform.parent, mode == 2);
                EtkinlikKit.SetActive(_note.transform.parent, mode == 1);
                EtkinlikKit.SetActive(_fill.transform.parent.parent, mode == 2);
                EtkinlikKit.SetActive(_count.transform.parent, mode == 2);
                if (mode == 0) _emptyLabel.text = Loc.T("dukkan_kontrat.kilitli");
                if (mode == 3) _emptyLabel.text = Loc.T("dukkan_kontrat.alindi");
                if (mode == 1) _note.text = Loc.T("dukkan_kontrat.odul_sonda");
                if (mode == 2) _status.text = Loc.T("dukkan_kontrat.bekliyor");
            }

            if (mode != 0)
            {
                int clock = Mathf.CeilToInt((float)_contracts.SecondsToNextOffer);
                if (clock != _shownClock)
                {
                    _shownClock = clock;
                    _clock.text = string.Format(Loc.T("dukkan_kontrat.yeni_teklif"), HudUI.LongClock(clock));
                }
            }

            if (mode == 1)
            {
                _offerSlot = offer.Slot;
                DrawOrder(offer.ProductIndex, offer.SizeIndex, offer.Terms.Quantity, offer.Terms.Cash,
                          offer.Terms.Gems, offer.Terms.ForemanCards, offer.Terms.EstimatedSeconds);
            }
            else if (mode == 2)
            {
                int product = view.ContractProductIndex;
                int quantity = view.ContractQuantity;
                double seconds = quantity * _market.MiningShopBusiness.CraftSeconds(product) / _contracts.Tuning.ContractShare;
                DrawOrder(product, view.ContractSizeIndex, quantity, view.ContractCash, view.ContractGems,
                          view.ContractForemanCards, seconds);
                int delivered = view.ContractDelivered;
                if (delivered != _shownDelivered)
                {
                    _shownDelivered = delivered;
                    _count.text = string.Format(Loc.T("dukkan_kontrat.teslim"), delivered, quantity);
                    EtkinlikKit.Progress(_fill, quantity > 0 ? (float)delivered / quantity : 0f);
                }
            }
        }

        /// <summary>The order card: what, how big, how long, what it pays. Rewritten only when one of them moved.</summary>
        private void DrawOrder(int product, int size, int quantity, double cash, long gems, int cards, double seconds)
        {
            if (product == _shownProduct && size == _shownSize && quantity == _shownQuantity && cash == _shownCash) return;
            _shownProduct = product;
            _shownSize = size;
            _shownQuantity = quantity;
            _shownCash = cash;

            Sprite icon = _config != null ? _config.ProductIcon(product) : null;
            _icon.sprite = icon;
            _icon.enabled = icon != null;
            _size.text = size >= 0 && size < SizeKeys.Length ? Loc.T(SizeKeys[size]) : string.Empty;
            _order.text = string.Format(Loc.T("dukkan_kontrat.siparis"), quantity, ProductName(product));
            _time.text = string.Format(Loc.T("dukkan_kontrat.sure"), Math.Max(1, (int)Math.Round(seconds / 60d)));
            _reward.Set(CashText(cash), gems, cards, 0L, null);
            _premium.text = string.Format(Loc.T("dukkan_kontrat.prim"), PremiumPercent(size));
        }

        private void OnAccept()
        {
            if (_contracts == null) return;
            if (_contracts.Accept(_offerSlot))
            {
                ServiceLocator.Get<AudioService>()?.Play(SoundId.Purchase);
                ServiceLocator.Get<HapticService>()?.Light();
                // Closed so the player sees the contract customer walk in.
                Hide();
            }
            else
            {
                ServiceLocator.Get<AudioService>()?.Play(SoundId.Denied);
                Redraw();
            }
        }

        // ------------------------------------------------------------------ pieces
        public static string ProductName(int product) => Loc.T("dukkan_kontrat.urun" + Mathf.Clamp(product, 0, 3));

        public static string CashText(double cash) => "$" + NumberFormatter.Format(new BigDouble(cash));

        private int PremiumPercent(int size)
        {
            ShopContract.Tuning t = _contracts.Tuning;
            return size >= 0 && size < t.Sizes.Length ? (int)Math.Round(t.Sizes[size].Premium * 100d) : 0;
        }
    }
}
