using Game.Core;
using Game.Gameplay;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Sale feedback: a "+$X" label that rises off the market and coins that fly into the cash counter.
    /// Without this the only signal that the whole factory is working is a number quietly changing.
    ///
    /// Deliberately decoupled from <see cref="CoalOperation"/> — it watches the wallet for increases and
    /// batches them on a short interval, rather than firing an event per truck dump (which would be both
    /// noisy and a per-sale allocation). Labels and coins come from <see cref="Pool{T}"/>.
    /// </summary>
    public sealed class HudJuice : MonoBehaviour
    {
        [SerializeField] private float popInterval = 0.35f;   // batch window for "+$X" labels
        [SerializeField] private float popRise = 130f;        // px the label travels
        [SerializeField] private float popSeconds = 1.0f;
        [SerializeField] private int coinsPerPop = 3;
        [SerializeField] private float coinSeconds = 0.65f;
        [SerializeField] private float coinSize = 54f;

        private WalletService _wallet;
        private CoalOperation _op;
        private Camera _cam;
        private Canvas _canvas;
        private Font _font;
        private RectTransform _root;

        private BigDouble _lastCash;
        private bool _haveLast;
        private double _pending;
        private float _timer;

        private sealed class Pop
        {
            public RectTransform rt;
            public Text text;
            public float t;
            public Vector2 from;
            public bool active;
        }
        private sealed class Coin
        {
            public RectTransform rt;
            public Image img;
            public float t;
            public Vector2 from, ctrl;
            public bool active;
        }

        private Pool<Pop> _popPool;
        private Pool<Coin> _coinPool;
        private readonly System.Collections.Generic.List<Pop> _livePops = new System.Collections.Generic.List<Pop>();
        private readonly System.Collections.Generic.List<Coin> _liveCoins = new System.Collections.Generic.List<Coin>();

        private void Start()
        {
            _wallet = ServiceLocator.Get<WalletService>();
            _cam = Camera.main;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BindEnabledOp();
            Build();
        }

        public void SetOperation(CoalOperation op) { if (op != null) _op = op; }

        private void BindEnabledOp()
        {
            var ops = FindObjectsByType<CoalOperation>();
            for (int i = 0; i < ops.Length; i++) if (ops[i].enabled) { _op = ops[i]; return; }
            if (_op == null && ops.Length > 0) _op = ops[0];
        }

        private void Build()
        {
            var go = new GameObject("HudJuiceCanvas", typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 95;                       // above badges (90), below the HUD (100)
            var sc = go.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1080f, 1920f);
            sc.matchWidthOrHeight = 0.5f;
            _root = (RectTransform)go.transform;

            _popPool = new Pool<Pop>(NewPop, p => p.rt.gameObject.SetActive(true), p => p.rt.gameObject.SetActive(false), 4);
            _coinPool = new Pool<Coin>(NewCoin, c => c.rt.gameObject.SetActive(true), c => c.rt.gameObject.SetActive(false), 8);
        }

        private Pop NewPop()
        {
            var go = new GameObject("Pop", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(_root, false);
            var t = go.GetComponent<Text>();
            t.font = _font; t.fontSize = 40; t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(300f, 60f);
            go.SetActive(false);
            return new Pop { rt = rt, text = t };
        }

        private Coin NewCoin()
        {
            var go = new GameObject("Coin", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_root, false);
            var img = go.GetComponent<Image>();
            img.sprite = UiSkin.Coin;
            img.raycastTarget = false;
            img.color = UiSkin.Coin != null ? Color.white : new Color(1f, 0.84f, 0.25f, 1f);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(coinSize, coinSize);
            go.SetActive(false);
            return new Coin { rt = rt, img = img };
        }

        private void Update()
        {
            if (_wallet == null) { _wallet = ServiceLocator.Get<WalletService>(); return; }
            if (_cam == null) _cam = Camera.main;
            if (_op == null || !_op.enabled) BindEnabledOp();

            AccumulateEarnings();
            Animate(Time.unscaledDeltaTime);
        }

        private void AccumulateEarnings()
        {
            BigDouble cash = _wallet.Cash;
            if (!_haveLast) { _lastCash = cash; _haveLast = true; return; }
            if (cash > _lastCash)
            {
                // Only the delta matters, and only roughly — this drives a label, not the economy.
                double gained = cash.ToDouble() - _lastCash.ToDouble();
                if (gained > 0d) _pending += gained;
            }
            _lastCash = cash;

            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f || _pending <= 0d) return;
            _timer = popInterval;
            Emit(_pending);
            _pending = 0d;
        }

        private void Emit(double amount)
        {
            if (_canvas == null || _cam == null || _op == null) return;
            Vector3 world;
            if (!_op.StationAnchor(6, out world)) return;      // 6 = MARKET, where money is actually made
            float sf = _canvas.scaleFactor > 0.0001f ? _canvas.scaleFactor : 1f;
            Vector3 sp = _cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) return;                             // behind the camera
            Vector2 at = new Vector2(sp.x / sf, sp.y / sf);

            var pop = _popPool.Get();
            pop.text.text = "+$" + NumberFormatter.Format(new BigDouble(amount));
            pop.text.color = new Color(0.55f, 1f, 0.6f, 1f);
            pop.from = at;
            pop.rt.anchoredPosition = at;
            pop.t = 0f; pop.active = true;
            pop.rt.SetAsLastSibling();
            _livePops.Add(pop);

            for (int i = 0; i < coinsPerPop; i++)
            {
                var c = _coinPool.Get();
                c.from = at + new Vector2(Random.Range(-40f, 40f), Random.Range(-20f, 20f));
                // arc out sideways before homing on the counter, so coins read as thrown rather than slid
                c.ctrl = c.from + new Vector2(Random.Range(-120f, 120f), Random.Range(160f, 300f));
                c.rt.anchoredPosition = c.from;
                c.t = -i * 0.06f;                               // slight stagger
                c.active = true;
                _liveCoins.Add(c);
            }
        }

        private void Animate(float dt)
        {
            float refH = _canvas != null && _canvas.scaleFactor > 0.0001f ? Screen.height / _canvas.scaleFactor : 1920f;
            float refW = _canvas != null && _canvas.scaleFactor > 0.0001f ? Screen.width / _canvas.scaleFactor : 1080f;
            Vector2 counter = new Vector2(refW * 0.5f, refH - 90f);   // the cash readout in the top bar

            for (int i = _livePops.Count - 1; i >= 0; i--)
            {
                Pop p = _livePops[i];
                p.t += dt / Mathf.Max(0.05f, popSeconds);
                if (p.t >= 1f)
                {
                    _livePops.RemoveAt(i); p.active = false; _popPool.Return(p);
                    continue;
                }
                float e = 1f - (1f - p.t) * (1f - p.t);                       // ease-out
                p.rt.anchoredPosition = p.from + new Vector2(0f, popRise * e);
                var col = p.text.color; col.a = p.t < 0.65f ? 1f : 1f - (p.t - 0.65f) / 0.35f;
                p.text.color = col;
                float s = p.t < 0.2f ? Mathf.Lerp(0.6f, 1.1f, p.t / 0.2f) : Mathf.Lerp(1.1f, 1f, (p.t - 0.2f) / 0.8f);
                p.rt.localScale = new Vector3(s, s, 1f);
            }

            for (int i = _liveCoins.Count - 1; i >= 0; i--)
            {
                Coin c = _liveCoins[i];
                c.t += dt / Mathf.Max(0.05f, coinSeconds);
                if (c.t < 0f) continue;                                       // still staggered
                if (c.t >= 1f)
                {
                    _liveCoins.RemoveAt(i); c.active = false; _coinPool.Return(c);
                    continue;
                }
                // quadratic bezier from the market, through the arc control point, into the counter
                float u = 1f - c.t;
                Vector2 pos = u * u * c.from + 2f * u * c.t * c.ctrl + c.t * c.t * counter;
                c.rt.anchoredPosition = pos;
                float s = Mathf.Lerp(1f, 0.45f, c.t);
                c.rt.localScale = new Vector3(s, s, 1f);
                var col = c.img.color; col.a = c.t > 0.8f ? 1f - (c.t - 0.8f) / 0.2f : 1f;
                c.img.color = col;
            }
        }
    }
}
