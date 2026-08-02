using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Idle motion for a store card's hero icon — one component on the icon itself, never on the card.
    ///
    /// <see cref="StoreCardFx"/> already owns everything that happens TO a card: it enters, presses,
    /// springs and punches. This owns what the thing ON the card does while nobody is touching it, and
    /// the two never write the same transform, so they compose without knowing about each other.
    ///
    /// Each product gets the motion its own object would make. An hourglass turns over; a chest
    /// rattles because something inside wants out; a bolt flickers; coins bob. Motion is the second
    /// half of what the icon says — the sprite names the product, the movement says what it does — and
    /// it is why the heroes are separate Images rather than baked into the card art.
    ///
    /// Everything runs on unscaled time (the store is often open while the game is paused), rests at
    /// the authored transform, and writes nothing while idle so a still card never dirties the canvas.
    /// </summary>
    public sealed class StoreHeroFx : MonoBehaviour
    {
        public enum Motion
        {
            None,
            Bob,        // drifts up and down — coins, anything that reads as a pile
            Pulse,      // breathes — a button asking to be pressed
            Tip,        // rocks side to side — the investor's hat
            Flip,       // turns over on a timer — the hourglass
            Shake,      // sits still, then rattles — the chest
            Flicker,    // sits still, then double-blinks — the bolt
        }

        [SerializeField] private Motion motion = Motion.Bob;
        [Tooltip("Tüm hareketleri hızlandırır/yavaşlatır. 1 = tasarlanan tempo.")]
        [SerializeField, Min(0.05f)] private float speed = 1f;
        [Tooltip("Tüm hareketleri büyütür/küçültür. 1 = tasarlanan genlik.")]
        [SerializeField, Min(0f)] private float strength = 1f;
        [Tooltip("Karta özel başlangıç kayması — aynı ızgaradaki kartlar tek vücut hareket etmesin diye.")]
        [SerializeField] private float phase = 0f;

        // Period and amplitude are per-motion facts, not per-card ones: a bob is measured in pixels, a
        // pulse in scale, a tip in degrees, and one shared Inspector number cannot be right for all
        // three. Baking them here is what lets every cloned cell share one template with no tuning --
        // the catalog picks a Motion and nothing else.
        private static float PeriodOf(Motion m)
        {
            switch (m)
            {
                case Motion.Flip: return 5.5f;      // long enough that a turned hourglass reads as settled
                case Motion.Shake: return 4f;
                case Motion.Flicker: return 3.4f;
                case Motion.Pulse: return 1.9f;
                case Motion.Tip: return 3.2f;
                default: return 2.6f;               // Bob
            }
        }

        private static float AmplitudeOf(Motion m)
        {
            switch (m)
            {
                case Motion.Bob: return 7f;         // pixels
                case Motion.Pulse: return 0.05f;    // scale
                case Motion.Tip: return 5f;         // degrees
                case Motion.Shake: return 9f;       // degrees
                default: return 1f;                 // Flip and Flicker carry their own shape
            }
        }

        private float Period => PeriodOf(motion) / speed;
        private float Amplitude => AmplitudeOf(motion) * strength;

        private RectTransform _rt;
        private Image _image;
        private Vector2 _homePos;
        private Vector3 _homeScale;
        private float _homeAlpha = 1f;
        private float _t;

        private void Awake()
        {
            _rt = (RectTransform)transform;
            _image = GetComponent<Image>();
            _homePos = _rt.anchoredPosition;
            _homeScale = _rt.localScale;
            if (_image != null) _homeAlpha = _image.color.a;
        }

        /// <summary>Set up a cloned cell's hero from the catalog, since the cell is built at runtime.</summary>
        public void Configure(Motion m, float phaseOffset)
        {
            motion = m;
            phase = phaseOffset;
        }

        private void OnEnable()
        {
            _t = phase;
            Rest();
        }

        private void OnDisable() => Rest();

        private void Rest()
        {
            if (_rt == null) return;
            _rt.anchoredPosition = _homePos;
            _rt.localScale = _homeScale;
            _rt.localRotation = Quaternion.identity;
            SetAlpha(_homeAlpha);
        }

        private void Update()
        {
            if (motion == Motion.None) return;
            _t += Time.unscaledDeltaTime;

            switch (motion)
            {
                case Motion.Bob:
                    _rt.anchoredPosition = _homePos + new Vector2(0f, Mathf.Sin(Tau(_t)) * Amplitude);
                    break;

                case Motion.Pulse:
                {
                    float s = 1f + Mathf.Sin(Tau(_t)) * Amplitude;
                    _rt.localScale = _homeScale * s;
                    break;
                }

                case Motion.Tip:
                    _rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Tau(_t)) * Amplitude);
                    break;

                case Motion.Flip:
                {
                    // Rests upright, then turns over across the last FlipSeconds of the period and
                    // stays turned — an hourglass that flipped back would be an hourglass nobody used.
                    float p2 = Period;
                    float phaseT = Mathf.Repeat(_t, p2 * 2f);
                    float turns = phaseT < p2 ? 0f : 1f;
                    float edge = Mathf.Clamp01((Mathf.Repeat(phaseT, p2) - (p2 - FlipSeconds)) / FlipSeconds);
                    float angle = 180f * (turns + Smooth(edge));
                    _rt.localRotation = Quaternion.Euler(0f, 0f, angle);
                    break;
                }

                case Motion.Shake:
                {
                    // Still for most of the period, then three quick knocks.
                    float ps = Period;
                    float phaseT = Mathf.Repeat(_t, ps);
                    if (phaseT < ps - ShakeSeconds) { _rt.localRotation = Quaternion.identity; break; }
                    float k = (phaseT - (ps - ShakeSeconds)) / ShakeSeconds;
                    float decay = 1f - k;
                    _rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(k * Mathf.PI * 6f) * Amplitude * decay);
                    break;
                }

                case Motion.Flicker:
                {
                    // Two fast blinks, then dark for the rest of the period. Scale and alpha together:
                    // a bolt that only scaled would read as breathing rather than sparking.
                    float phaseT = Mathf.Repeat(_t, Period);
                    if (phaseT > FlickerSeconds)
                    {
                        _rt.localScale = _homeScale;
                        SetAlpha(_homeAlpha);
                        break;
                    }
                    float k = phaseT / FlickerSeconds;
                    float spark = Mathf.Abs(Mathf.Sin(k * Mathf.PI * 2f)) * (1f - k);
                    _rt.localScale = _homeScale * (1f + spark * 0.14f);
                    SetAlpha(_homeAlpha * (1f - spark * 0.35f));
                    break;
                }
            }
        }

        private const float FlipSeconds = 0.55f;
        private const float ShakeSeconds = 0.7f;
        private const float FlickerSeconds = 0.5f;

        private float Tau(float t) => t / Period * Mathf.PI * 2f;

        private static float Smooth(float p) => p * p * (3f - 2f * p);

        private void SetAlpha(float a)
        {
            if (_image == null) return;
            Color c = _image.color;
            if (Mathf.Abs(c.a - a) < 0.004f) return;
            c.a = a;
            _image.color = c;
        }
    }
}
