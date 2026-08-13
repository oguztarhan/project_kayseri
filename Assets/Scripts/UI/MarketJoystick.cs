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
    /// It reports a direction and nothing else. What that direction means is the yard's business.
    /// </summary>
    public sealed class MarketJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [Tooltip("Kolun tam itilmiş sayılması için parmağın merkezden uzaklaşması gereken piksel. " +
                 "Küçültürsen kol daha çabuk doludur ama ince yürüyüş kaybolur.")]
        [SerializeField, Min(1f)] private float radiusPixels = 150f;

        private RectTransform _zone;      // the draggable area, which is this object
        private RectTransform _ring;      // the base that appears under the thumb
        private RectTransform _knob;
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

        public void OnPointerDown(PointerEventData e)
        {
            if (_finger != -1) return;                     // already driving; a second thumb is not a vote
            _finger = e.pointerId;
            _origin = Local(e);
            if (_ring != null) _ring.anchoredPosition = _origin;
            if (_knob != null) _knob.anchoredPosition = _origin;
            Value = Vector2.zero;
            Show(true);
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
