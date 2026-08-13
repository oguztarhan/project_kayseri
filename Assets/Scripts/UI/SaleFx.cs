using Game.Core;
using Game.Data;
using Game.Gameplay;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The "+$420" that lifts off the market every time a truck sells its load.
    ///
    /// An idle game's whole feedback loop is watching money arrive. Without this the only sign that the
    /// chain was working at all was a counter in the top bar quietly changing — the map itself never
    /// reacted, so upgrades felt like they went into a spreadsheet rather than into the place you are
    /// looking at. A number popping off the building that earned it ties the two together.
    ///
    /// Screen-space rather than world-space, matching <see cref="StationBadges"/>: the text stays a
    /// readable size at every zoom and never sorts behind a building.
    ///
    /// Everything is pooled at build time and only ever moved and recoloured, so a busy market allocates
    /// nothing. Labels past the pool size are dropped rather than queued — at that rate the player cannot
    /// read them individually anyway, and dropping is cheaper than growing.
    /// </summary>
    public sealed class SaleFx : MonoBehaviour
    {
        [SerializeField] private int poolSize = 14;
        [SerializeField] private float riseSeconds = 1.15f;
        [SerializeField] private float risePixels = 120f;
        [SerializeField] private int fontSize = 30;
        [SerializeField] private float spreadPixels = 42f;   // sideways scatter, so two at once don't overlap
        [SerializeField] private Color cashColor = new Color(0.42f, 0.92f, 0.40f);

        private sealed class Pop
        {
            public RectTransform rt;
            public Text label;
            public Vector3 world;
            public float age = 999f;   // starts expired, so nothing shows on frame one
            public float side;
        }

        private Pop[] _pops;
        private Camera _cam;
        private Canvas _canvas;
        private int _next;

        private void Awake()
        {
            _cam = Camera.main;

            var go = new GameObject("SaleFxCanvas", typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 95;   // over the station chips (90), under the HUD (100)
            var sc = go.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1080f, 1920f);
            sc.matchWidthOrHeight = 0.5f;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _pops = new Pop[Mathf.Max(1, poolSize)];
            for (int i = 0; i < _pops.Length; i++) _pops[i] = BuildPop((RectTransform)go.transform, font, i);
        }

        /// <summary>
        /// Subscribes to whichever island is live. Re-checked on a slow timer rather than once, because
        /// travelling swaps which operation is enabled and the new one is a different object.
        /// </summary>
        private void Update()
        {
            _rebind -= Time.unscaledDeltaTime;
            if (_rebind > 0f) return;
            _rebind = 1f;
            if (_op != null && _op.enabled) return;

            var ops = FindObjectsByType<CoalOperation>(FindObjectsSortMode.None);
            for (int i = 0; i < ops.Length; i++)
            {
                if (!ops[i].enabled) continue;
                if (_op != null) _op.Sold -= OnSold;
                _op = ops[i];
                _op.Sold += OnSold;
                return;
            }
        }

        private void OnDisable()
        {
            if (_op != null) _op.Sold -= OnSold;
            _op = null;
        }

        private void OnSold(Vector3 where, double amount)
        {
            Cash(where, amount);
            // Satış saniyede birkaç kez olabilir; sesin tekrar kapısı AudioLibrary'de.
            if (_audio == null) _audio = ServiceLocator.Get<AudioService>();
            if (_audio != null) _audio.Play(SoundId.Sale);
        }

        private AudioService _audio;

        private CoalOperation _op;
        private float _rebind;

        private Pop BuildPop(RectTransform parent, Font font, int i)
        {
            var go = new GameObject("Pop" + i, typeof(RectTransform), typeof(Text), typeof(Outline));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(260f, 44f);

            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;   // never steal a tap meant for the map or a chip

            // The map is bright green and pale grey by turns, and plain coloured text disappears against
            // one or the other; an outline keeps it readable over both.
            var outline = go.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.62f);
            outline.effectDistance = new Vector2(2f, -2f);

            go.SetActive(false);
            return new Pop { rt = rt, label = text };
        }

        /// <summary>
        /// Floats one earning off <paramref name="worldPos"/>. Safe to call from the simulation on any
        /// frame; sales below <paramref name="minAmount"/> are dropped so a trickle of pennies does not
        /// bury the screen in labels.
        /// </summary>
        public void Cash(Vector3 worldPos, double amount, double minAmount = 1d)
        {
            if (_pops == null || amount < minAmount) return;

            // Round-robin rather than "first free": at a high sale rate every slot is busy, and reusing the
            // oldest keeps the newest numbers visible instead of dropping all of them.
            Pop p = _pops[_next];
            _next = (_next + 1) % _pops.Length;

            p.world = worldPos;
            p.age = 0f;
            // Deterministic alternating scatter — cheaper than Random and reads as spread, not as a stack.
            p.side = ((_next % 3) - 1) * spreadPixels;
            p.label.text = "+$" + NumberFormatter.Format(new BigDouble(amount));
            p.label.color = cashColor;
            p.rt.gameObject.SetActive(true);
        }

        private void LateUpdate()
        {
            if (_pops == null) return;
            if (_cam == null) { _cam = Camera.main; if (_cam == null) return; }
            float dt = Time.deltaTime;

            for (int i = 0; i < _pops.Length; i++)
            {
                Pop p = _pops[i];
                if (p.age >= riseSeconds) continue;
                p.age += dt;
                if (p.age >= riseSeconds) { p.rt.gameObject.SetActive(false); continue; }

                float k = p.age / riseSeconds;
                Vector3 screen = _cam.WorldToScreenPoint(p.world);
                if (screen.z <= 0f) { p.rt.gameObject.SetActive(false); p.age = riseSeconds; continue; }

                // Ease out: quick off the building, then drifting, which reads as lighter than a linear rise.
                float lift = risePixels * (1f - (1f - k) * (1f - k));
                p.rt.anchoredPosition = new Vector2(screen.x / _canvasScale + p.side,
                                                    screen.y / _canvasScale + lift);

                Color c = p.label.color;
                // Hold full opacity for the first half, then fade — a number that starts fading at once
                // is hard to actually read.
                c.a = k < 0.5f ? 1f : 1f - (k - 0.5f) * 2f;
                p.label.color = c;
                float s = k < 0.18f ? Mathf.Lerp(0.7f, 1.08f, k / 0.18f) : Mathf.Lerp(1.08f, 1f, (k - 0.18f) / 0.82f);
                p.rt.localScale = new Vector3(s, s, 1f);
            }
        }

        // Screen pixels to canvas units. Read off the scaler rather than recomputed from a reference
        // resolution written out here: the old copy assumed 1080×1920 portrait, so it was wrong on
        // every device that is not that, and wrong by more than a factor of two in landscape.
        private float _canvasScale =>
            _canvas != null && _canvas.scaleFactor > 0.0001f ? _canvas.scaleFactor : 1f;
    }
}
