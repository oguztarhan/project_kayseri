using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The thumb stick for the market yard: a floating joystick over a full-screen drag zone.
    ///
    /// Floating rather than fixed. A stick painted in a corner has to be found before it can be used,
    /// and on a phone held one-handed the corner it is painted in is the wrong one half the time. This
    /// one appears wherever the thumb lands and disappears when it lifts, so the control is always
    /// exactly where the player already put their hand.
    ///
    /// WHICH LEAVES NOTHING ON SCREEN TO SAY SO. A control that only exists once you have already used it
    /// cannot teach itself, and the market is the one part of this game where the player has a body to
    /// walk — several arrive, look at a yard they cannot see any way to act on, and leave. So there is a
    /// resting pad: a dim stick sitting in one corner with a ripple going out of it, shown whenever no
    /// thumb is down. It is a HINT and not a second control — dragging on it does what dragging anywhere
    /// else in the zone does, which happens to be exactly what it looks like it should do.
    ///
    /// It reports a direction and nothing else. What that direction means is the yard's business.
    /// </summary>
    public sealed class MarketJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [Tooltip("Kolun tam itilmiş sayılması için parmağın merkezden uzaklaşması gereken piksel. " +
                 "Küçültürsen kol daha çabuk doludur ama ince yürüyüş kaybolur.")]
        [SerializeField, Min(1f)] private float radiusPixels = 150f;

        [Tooltip("Bekleyen kolun üstünden çıkan dalganın bir turu. Uzatırsan daha sakin nefes alır.")]
        [SerializeField, Min(0.2f)] private float rippleSeconds = 1.9f;

        [Tooltip("Dalganın büyüdüğü son ölçek. 1 yaparsan dalga hiç yayılmaz.")]
        [SerializeField, Min(1f)] private float rippleScale = 1.7f;

        private RectTransform _zone;      // the draggable area, which is this object
        private RectTransform _ring;      // the base that appears under the thumb
        private RectTransform _knob;
        private RectTransform _rest;      // the parked stick shown while nobody is touching
        private RectTransform _ripple;
        private Graphic _rippleGraphic;
        private float _rippleAlpha;
        private int _finger = -1;         // the pointer that owns the stick; others are ignored
        private Vector2 _origin;

        /// <summary>The current push, unit length at full deflection. Zero when nobody is touching.</summary>
        public Vector2 Value { get; private set; }

        /// <summary>Wires the two graphics the stick moves. Both are hidden until a thumb lands.</summary>
        public void Bind(RectTransform ring, RectTransform knob)
        {
            _zone = (RectTransform)transform;
            _ring = ring;
            _knob = knob;
            Show(false);
        }

        /// <summary>
        /// Wires the resting pad: the whole thing to show or hide, and the ring inside it that ripples.
        /// Optional — a yard with nothing bound here behaves exactly as it did before.
        /// </summary>
        public void BindRest(RectTransform pad, RectTransform ripple)
        {
            _rest = pad;
            _ripple = ripple;
            _rippleGraphic = ripple != null ? ripple.GetComponent<Graphic>() : null;
            _rippleAlpha = _rippleGraphic != null ? _rippleGraphic.color.a : 0f;
            Rest(true);
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (_finger != -1) return;                     // already driving; a second thumb is not a vote
            _finger = e.pointerId;
            _origin = Local(e);
            if (_ring != null) _ring.anchoredPosition = _origin;
            if (_knob != null) _knob.anchoredPosition = _origin;
            Value = Vector2.zero;
            Show(true);
            // The hint's whole job is done the moment a thumb is down, and two sticks on screen at once —
            // one live under the thumb, one parked in the corner — reads as the wrong one being broken.
            Rest(false);
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != _finger) return;
            Vector2 offset = Local(e) - _origin;
            // Clamped, not scaled: past the ring the stick is simply at full tilt, so a long drag keeps
            // walking instead of running out of travel.
            Vector2 clamped = Vector2.ClampMagnitude(offset, radiusPixels);
            if (_knob != null) _knob.anchoredPosition = _origin + clamped;
            Value = clamped / radiusPixels;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != _finger) return;
            _finger = -1;
            Value = Vector2.zero;
            Show(false);
            Rest(true);
        }

        /// <summary>Screen point in the drag zone's own space, so the stick lands under the thumb exactly.</summary>
        private Vector2 Local(PointerEventData e)
        {
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _zone, e.position, e.pressEventCamera, out local);
            return local;
        }

        private void Show(bool on)
        {
            if (_ring != null) _ring.gameObject.SetActive(on);
            if (_knob != null) _knob.gameObject.SetActive(on);
        }

        private void Rest(bool on)
        {
            if (_rest != null && _rest.gameObject.activeSelf != on) _rest.gameObject.SetActive(on);
        }

        /// <summary>
        /// The ripple, and only while the pad is parked on screen. One scale and one colour write per
        /// frame, on an object that is switched off for the whole time the player is actually walking.
        /// </summary>
        private void Update()
        {
            if (_ripple == null || _rest == null || !_rest.gameObject.activeSelf) return;
            float cycle = Time.unscaledTime / rippleSeconds;
            float t = cycle - Mathf.Floor(cycle);
            _ripple.localScale = Vector3.one * Mathf.Lerp(1f, rippleScale, t);
            if (_rippleGraphic == null) return;
            Color c = _rippleGraphic.color;
            // Fading as it grows, so it reads as one wave leaving rather than a ring pumping in place.
            c.a = _rippleAlpha * (1f - t) * (1f - t);
            _rippleGraphic.color = c;
        }

        /// <summary>
        /// A transparent graphic over the whole zone. uGUI only raycasts against something that draws,
        /// so without this the drag zone would be an empty rect that never receives a pointer at all.
        /// </summary>
        public static Image DragSurface(RectTransform rect)
        {
            var img = rect.gameObject.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);
            img.raycastTarget = true;
            return img;
        }
    }
}
