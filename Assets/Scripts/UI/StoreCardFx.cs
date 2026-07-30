using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Store-card feel, one component per card: an entrance pop when the screen opens (grid cells
    /// enter as a diagonal wave), press-down/spring-back under the finger, a punch on purchase and —
    /// on the offer cards only — a periodic gloss sweep. The pack-cell template carries this too, so
    /// every cloned cell inherits it and nobody maintains a list. All scale sources multiply into one
    /// localScale, which layout groups ignore, so the vertical list and the grids never fight it.
    /// No allocations after Awake.
    /// </summary>
    public sealed class StoreCardFx : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        [Header("Giriş dalgası")]
        [Tooltip("Kart bu ölçekten 1'e hafif taşarak belirir.")]
        [SerializeField] private float entranceScale = 0.88f;
        [SerializeField] private float entranceSeconds = 0.34f;
        [Tooltip("Sıradaki her kart bir öncekinden bu kadar geç başlar. Sıra hiyerarşiden gelir; ızgarada satır+sütun (çapraz dalga).")]
        [SerializeField] private float entranceStep = 0.055f;
        [Tooltip("Bu grubun tümü için ek bekleme — paket ızgaraları tekliflerden sonra başlasın diye.")]
        [SerializeField] private float entranceBaseDelay = 0f;
        [Tooltip("Kart, kaydırma penceresine bu kadar px girene dek girişini bekletir; görünür olunca oynar. Böylece ekran altındaki paketler dalgalarını kaydırınca gözünün önünde oynar, açılışta boşa değil.")]
        [SerializeField] private float revealMargin = 40f;

        [Header("Basma")]
        [Tooltip("Parmak kartın üstündeyken ölçek.")]
        [SerializeField] private float pressedScale = 0.96f;
        [Tooltip("Bırakınca geri yaylanmanın sertliği (yay sabiti).")]
        [SerializeField] private float springStiffness = 340f;
        [Tooltip("Bırakıştaki sönümleme; düşük değer daha çok seker.")]
        [SerializeField] private float releaseDamping = 13f;

        [Header("Satın alma vuruşu")]
        [SerializeField] private float punchScale = 1.06f;
        [SerializeField] private float punchSeconds = 0.3f;

        [Header("Parıltı (yalnız teklif kartları)")]
        [Tooltip("RectMask2D içindeki eğik parlak bant; boş bırakılırsa parıltı yok.")]
        [SerializeField] private RectTransform shineBand;
        [SerializeField] private float shineInterval = 5f;
        [SerializeField] private float shineSeconds = 0.7f;
        [Tooltip("Kartlar birlikte parlamasın diye karta özel ilk gecikme.")]
        [SerializeField] private float shinePhase = 0f;

        private RectTransform _rt;
        private CanvasGroup _group;      // optional — the fade needs it, the scales work without
        private Selectable _selectable;  // optional — a disabled button should not dip when touched
        private RectTransform _viewport; // scroll viewport, when the card lives in one
        private float _entranceT;        // seconds; negative while waiting for our slot in the wave
        private bool _entering;
        private bool _seen;              // entrance clock only runs once the card has been in view
        private int _layoutGrace;        // frames to wait before trusting positions (layout runs late)
        private float _press = 1f;
        private float _pressVel;
        private bool _pressed;
        private float _punchT = 1f;      // normalized; >= 1 is idle
        private float _shineT;
        private float _applied = 1f;

        private void Awake()
        {
            _rt = (RectTransform)transform;
            _group = GetComponent<CanvasGroup>();
            _selectable = GetComponent<Selectable>();
            ScrollRect scroll = GetComponentInParent<ScrollRect>();
            if (scroll != null)
                _viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
        }

        private void OnEnable()
        {
            _entering = entranceSeconds > 0f;
            _entranceT = -(entranceBaseDelay + entranceStep * EntranceOrder());
            _seen = _viewport == null;   // no scroll view → no gating, play immediately
            _layoutGrace = 2;            // grid positions are wrong until the first canvas rebuild
            _press = 1f; _pressVel = 0f; _pressed = false;
            _punchT = 1f;
            _shineT = -shinePhase;
            if (shineBand != null) Sweep(1f);            // park the band off the card
            if (_group != null) _group.alpha = _entering ? 0f : 1f;
            Apply(_entering ? entranceScale : 1f);
        }

        private void OnDisable()
        {
            // leave the card exactly as authored so no mid-tween state survives into the next open
            Apply(1f);
            if (_group != null) _group.alpha = 1f;
        }

        /// <summary>Purchase feedback — StorePurchaseFx calls this on the bought card.</summary>
        public void Punch() => _punchT = 0f;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_selectable != null && !_selectable.interactable) return;
            _pressed = true;
        }

        public void OnPointerUp(PointerEventData eventData) => _pressed = false;

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt > 0.05f) dt = 0.05f;                  // an editor hitch must not launch the spring

            bool moving = false;

            if (_entering && !_seen)
            {
                // hold at alpha 0 until the card scrolls into the viewport, so the pop happens
                // in front of the player instead of below the fold on open
                if (_layoutGrace > 0) _layoutGrace--;
                else _seen = InView();
            }

            if (_entering && _seen)
            {
                _entranceT += dt;
                moving = true;
                if (_entranceT >= entranceSeconds)
                {
                    _entering = false;
                    if (_group != null) _group.alpha = 1f;
                }
                else if (_group != null && _entranceT > 0f)
                    _group.alpha = Mathf.Clamp01(_entranceT / (entranceSeconds * 0.6f));
            }

            if (_pressed)
            {
                // sinks onto pressedScale with no wobble; the bounce is saved for the release
                _press = Mathf.Lerp(_press, pressedScale, 1f - Mathf.Exp(-30f * dt));
                _pressVel = 0f;
                moving = true;
            }
            else if (Mathf.Abs(_press - 1f) > 0.0004f || Mathf.Abs(_pressVel) > 0.004f)
            {
                _pressVel += (1f - _press) * springStiffness * dt;
                _pressVel *= Mathf.Exp(-releaseDamping * dt);
                _press += _pressVel * dt;
                moving = true;
            }
            else { _press = 1f; _pressVel = 0f; }

            if (_punchT < 1f)
            {
                _punchT += dt / Mathf.Max(0.05f, punchSeconds);
                moving = true;
            }

            if (moving) Apply(ComputeScale());

            if (shineBand != null) TickShine(dt);
        }

        private float ComputeScale()
        {
            float entrance = 1f;
            if (_entering)
            {
                float p = Mathf.Clamp01(_entranceT / entranceSeconds);
                entrance = entranceScale + (1f - entranceScale) * BackOut(p);
            }
            float punch = _punchT < 1f ? 1f + (punchScale - 1f) * Mathf.Sin(_punchT * Mathf.PI) : 1f;
            return entrance * _press * punch;
        }

        /// <summary>Ease-out with a small overshoot — the classic pop.</summary>
        private static float BackOut(float p)
        {
            const float s = 1.4f;
            p -= 1f;
            return 1f + p * p * ((s + 1f) * p + s);
        }

        private void Apply(float scale)
        {
            if (Mathf.Abs(scale - _applied) < 0.0005f) return;  // an idle card never dirties the canvas
            _applied = scale;
            _rt.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>Is enough of the card inside the scroll viewport to be worth animating?</summary>
        private bool InView()
        {
            Vector3 local = _viewport.InverseTransformPoint(_rt.position);
            Rect view = _viewport.rect;
            float half = _rt.rect.height * 0.5f;
            return local.y + half > view.yMin + revealMargin && local.y - half < view.yMax - revealMargin;
        }

        // ---------- entrance order ----------

        /// <summary>
        /// This card's slot in the entrance wave, read from the hierarchy: cards in a plain list enter
        /// in child order; inside a grid the slot is row+column, which turns the same stagger into a
        /// top-left to bottom-right diagonal wave.
        /// </summary>
        private int EntranceOrder()
        {
            Transform parent = transform.parent;
            if (parent == null) return 0;
            int index = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child == transform) break;
                if (child.gameObject.activeSelf && child.GetComponent<StoreCardFx>() != null) index++;
            }
            GridLayoutGroup grid = parent.GetComponent<GridLayoutGroup>();
            if (grid == null) return index;
            int cols = Columns(grid);
            return index / cols + index % cols;
        }

        private static int Columns(GridLayoutGroup grid)
        {
            if (grid.constraint == GridLayoutGroup.Constraint.FixedColumnCount)
                return Mathf.Max(1, grid.constraintCount);
            float w = ((RectTransform)grid.transform).rect.width - grid.padding.horizontal + grid.spacing.x;
            return Mathf.Max(1, Mathf.FloorToInt(w / Mathf.Max(1f, grid.cellSize.x + grid.spacing.x)));
        }

        // ---------- shine ----------

        private void TickShine(float dt)
        {
            _shineT += dt;
            if (_shineT < 0f) return;
            if (_shineT <= shineSeconds)
            {
                Sweep(shineSeconds > 0f ? _shineT / shineSeconds : 1f);
            }
            else
            {
                Sweep(1f);
                _shineT = -shineInterval;
            }
        }

        /// <summary>0..1 sweeps the band across its mask; at the ends it sits fully outside, culled.</summary>
        private void Sweep(float p)
        {
            RectTransform mask = (RectTransform)shineBand.parent;
            float half = mask.rect.width * 0.5f + shineBand.rect.width;
            float eased = p * p * (3f - 2f * p);
            shineBand.anchoredPosition = new Vector2(Mathf.Lerp(-half, half, eased), shineBand.anchoredPosition.y);
        }
    }
}
