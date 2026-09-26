using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// "Contract Completed!": confetti, the crowned reward card, what was delivered, the cash counting up to what
    /// the contract paid with its premium, the gems and foreman cards, and who the cards went to.
    ///
    /// PRESENTATION ONLY. The contract was paid in the save that completed it; this screen reads
    /// <see cref="ShopContractService.PendingCelebration"/> and, when closed, marks it shown. A kill before it
    /// opens or while it is up shows it again on the next launch, and closing it can never lose anything.
    ///
    /// IT WAITS ITS TURN. While something else covers the middle of the screen — another popup, a tutorial card,
    /// the starter offer — it holds back and looks again twice a second, so it is never opened behind something
    /// or on top of a moment that is not its own. The HUD and the contract screen do not count: the contract
    /// screen is exactly where it belongs.
    /// </summary>
    public sealed class ShopContractCelebrationUI : MonoBehaviour
    {
        [Tooltip("Ödül ekranı 130'da; kutlama onun da üstünde, hiçbir şeyin arkasında kalmasın.")]
        [SerializeField] private int sortingOrder = 135;
        [SerializeField] private float enterSeconds = 0.25f;
        [SerializeField] private float countSeconds = 0.9f;
        [SerializeField] private float checkSeconds = 0.5f;
        [SerializeField] private Color premiumInk = new Color(0.85f, 0.45f, 0.05f, 1f);

        private ShopContractService _contracts;
        private LocalizationService _loc;
        private Canvas _hudCanvas, _screenCanvas;

        private RectTransform _canvas, _root, _card;
        private CanvasGroup _group;
        private Text _title, _summary, _cash, _premium, _foreman, _closeLabel;
        private Image _icon;
        private EtkinlikOdulSatiri _extras;
        private ConfettiBurst _confetti;

        private ShopContractService.Completion _shown;
        private float _shownAt, _check;
        private bool _entering, _counting;
        private readonly List<RaycastResult> _hits = new List<RaycastResult>();
        private PointerEventData _probe;
        private EventSystem _probeSystem;

        /// <summary>Called once by <see cref="HudUI"/>, which owns both canvases this one may draw over.</summary>
        public void Bind(Canvas hud, Canvas screen)
        {
            _hudCanvas = hud != null ? hud.rootCanvas : null;
            _screenCanvas = screen;
        }

        public bool Showing => _root != null && _root.gameObject.activeSelf;

        private void Awake()
        {
            _contracts = ServiceLocator.Get<ShopContractService>();
            _loc = ServiceLocator.Get<LocalizationService>();
            Build();
            _root.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (Showing)
            {
                Animate();
                return;
            }
            _check -= Time.unscaledDeltaTime;
            if (_check > 0f) return;
            _check = checkSeconds;
            if (_contracts != null && _contracts.CelebrationPending && !Covered()) Present(_contracts.PendingCelebration);
        }

        // ------------------------------------------------------------------ build
        private void Build()
        {
            _canvas = UiBuild.Canvas(transform, "KontratKutlamaKanvas", sortingOrder);
            var go = new GameObject("Karartma", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            go.transform.SetParent(_canvas, false);
            _root = UiBuild.Anchor((RectTransform)go.transform, Vector2.zero, Vector2.one);
            Image scrim = go.GetComponent<Image>();
            scrim.sprite = UiSkin.Flat;
            scrim.type = Image.Type.Sliced;
            scrim.color = new Color(0.02f, 0.03f, 0.06f, 0.78f);
            _group = go.GetComponent<CanvasGroup>();

            // The events reward card's crowned panel, taller: it carries a title, a summary and two reward lines.
            Image card = EkranKit.Sliced(_root, "Kart", LigKit.Get("odul_pano"),
                                         new Vector2(0.12f, 0.29f), new Vector2(0.88f, 0.69f), false);
            _card = card.rectTransform;
            _title = EtkinlikKit.Label(_card, "Baslik", new Vector2(0.12f, 0.64f), new Vector2(0.88f, 0.77f),
                                       string.Empty, 46, TextAnchor.MiddleCenter, EkranKit.Ink, 22);
            _summary = EtkinlikKit.Label(_card, "Ozet", new Vector2(0.24f, 0.55f), new Vector2(0.92f, 0.64f),
                                         string.Empty, 28, TextAnchor.MiddleLeft, EtkinlikKit.InkSoft, 14);
            _icon = EkranKit.Icon(_card, "Simge", null, new Vector2(0.08f, 0.53f), new Vector2(0.22f, 0.66f));
            _cash = EtkinlikKit.Label(_card, "Nakit", new Vector2(0.08f, 0.40f), new Vector2(0.92f, 0.54f),
                                      string.Empty, 56, TextAnchor.MiddleCenter, new Color(0.12f, 0.38f, 0.70f, 1f), 24);
            _premium = EtkinlikKit.Label(_card, "Prim", new Vector2(0.08f, 0.34f), new Vector2(0.92f, 0.41f),
                                         string.Empty, 26, TextAnchor.MiddleCenter, premiumInk, 12);
            _extras = EtkinlikOdulSatiri.Create(_card, "Ekstra", new Vector2(0.08f, 0.255f), new Vector2(0.92f, 0.34f),
                                                38, new Color(0.12f, 0.38f, 0.70f, 1f), TextAnchor.MiddleCenter);
            _foreman = EtkinlikKit.Label(_card, "Usta", new Vector2(0.08f, 0.205f), new Vector2(0.92f, 0.255f),
                                         string.Empty, 24, TextAnchor.MiddleCenter, EtkinlikKit.InkSoft, 12);

            Button close = EtkinlikKit.Capsule(_card, "Harika", new Vector2(0.27f, 0.075f), new Vector2(0.73f, 0.195f),
                                               Close, out _closeLabel);
            EtkinlikKit.SetFace(close, _closeLabel, EtkinlikKit.Face.Claim, true);
            EtkinlikKit.Fit(_closeLabel, 12, 34);

            // On this canvas, after the card, so the paper falls in front of it and above every other screen.
            _confetti = _canvas.gameObject.AddComponent<ConfettiBurst>();
            UiBuild.InsetContent(_root);
        }

        // ------------------------------------------------------------------ show
        private void Present(ShopContractService.Completion done)
        {
            _shown = done;
            _title.text = Loc.T("dukkan_kontrat.tamamlandi");
            _closeLabel.text = EtkinlikKit.OneLine(Loc.T("dukkan_kontrat.harika"));
            _summary.text = string.Format(Loc.T("dukkan_kontrat.teslim_ozet"), done.Quantity,
                                          ShopContractUI.ProductName(done.ProductIndex));
            ShopContractConfig config = ServiceLocator.Get<ShopContractConfig>();
            Sprite icon = config != null ? config.ProductIcon(done.ProductIndex) : null;
            _icon.sprite = icon;
            _icon.enabled = icon != null;

            int percent = done.NormalCash > 0d ? (int)System.Math.Round((done.Cash / done.NormalCash - 1d) * 100d) : 0;
            _premium.text = percent > 0 ? string.Format(Loc.T("dukkan_kontrat.prim_etiket"), percent) : string.Empty;
            _extras.Set(null, done.Gems, done.ForemanCards, 0L, null);
            bool named = done.ForemanCards > 0 && Foremen.Exists(done.Foreman);
            _foreman.text = named ? string.Format(Loc.T("dukkan_kontrat.kart_kime"), ForemanRosterUI.DisplayName(done.Foreman))
                                  : string.Empty;
            _cash.text = ShopContractUI.CashText(0d);

            _root.gameObject.SetActive(true);
            _shownAt = Time.unscaledTime;
            _entering = true;
            _counting = true;
            _group.alpha = 0f;
            _card.localScale = Vector3.one * 0.8f;

            _confetti.Play();
            ServiceLocator.Get<AudioService>()?.Play(SoundId.Reward);
            ServiceLocator.Get<HapticService>()?.Medium();
        }

        /// <summary>The pop-in, then the cash counting up to what was paid; after that nothing moves.</summary>
        private void Animate()
        {
            float age = Time.unscaledTime - _shownAt;
            if (_entering)
            {
                float t = Mathf.Clamp01(age / enterSeconds);
                _group.alpha = t;
                _card.localScale = Vector3.one * Mathf.Lerp(0.8f, 1f + Mathf.Sin(t * Mathf.PI) * 0.08f, t);
                if (t >= 1f)
                {
                    _entering = false;
                    _card.localScale = Vector3.one;
                }
            }
            if (_counting)
            {
                float u = Mathf.Clamp01((age - enterSeconds) / countSeconds);
                // Ease out: fast at first, settling on the exact figure.
                float eased = 1f - (1f - u) * (1f - u) * (1f - u);
                _cash.text = "+" + ShopContractUI.CashText(_shown.Cash * eased);
                if (u >= 1f) _counting = false;
            }
        }

        private void Close()
        {
            _root.gameObject.SetActive(false);
            _contracts?.MarkCelebrationShown();
            ServiceLocator.Get<AudioService>()?.Play(SoundId.Tap);
        }

        /// <summary>
        /// Whether some other screen is up: anything drawn above the HUD that catches a tap in the middle of the
        /// screen, bar the contract screen. Runs only while a celebration is waiting, twice a second.
        /// </summary>
        private bool Covered()
        {
            EventSystem events = EventSystem.current;
            if (events == null) return false;
            if (_probe == null || _probeSystem != events)
            {
                _probe = new PointerEventData(events);
                _probeSystem = events;
            }
            _probe.position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            _hits.Clear();
            events.RaycastAll(_probe, _hits);
            // Only what draws over the HUD is a screen: the world's own badges and signs sit under it (90-92).
            int hudOrder = _hudCanvas != null ? _hudCanvas.sortingOrder : 100;
            for (int i = 0; i < _hits.Count; i++)
            {
                GameObject hit = _hits[i].gameObject;
                if (hit == null) continue;
                Canvas canvas = hit.GetComponentInParent<Canvas>();
                Canvas root = canvas != null ? canvas.rootCanvas : null;
                if (root == null || root == _hudCanvas || root == _screenCanvas || root.sortingOrder <= hudOrder) continue;
                return true;
            }
            return false;
        }
    }
}
