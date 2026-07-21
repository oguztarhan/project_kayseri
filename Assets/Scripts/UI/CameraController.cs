using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Game.UI
{
    /// <summary>
    /// Isometric map camera: scroll / pinch to zoom (orthographic size), drag to pan (mouse or touch),
    /// clamped to map bounds. Uses the Input System package (this project's active input handling).
    /// </summary>
    public sealed class CameraController : MonoBehaviour
    {
        [SerializeField] private Camera cam;
        [SerializeField] private float minSize = 8f;
        [SerializeField] private float maxSize = 95f;
        [SerializeField] private float scrollZoomSpeed = 6f;
        [SerializeField] private float pinchZoomSpeed = 0.04f;
        [SerializeField] private float panSpeed = 1.4f;
        [SerializeField] private Vector2 boundsX = new Vector2(-250f, 250f);
        [SerializeField] private Vector2 boundsZ = new Vector2(-250f, 250f);

        private Vector3 _right, _forward;
        private bool _dragging;
        private Vector2 _lastMouse;
        private float _lastPinch;

        private void Awake()
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (cam == null) cam = Camera.main;
            Vector3 r = transform.right; r.y = 0f; _right = r.sqrMagnitude > 0.0001f ? r.normalized : Vector3.right;
            Vector3 f = transform.forward; f.y = 0f; _forward = f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
        }

        private void Update()
        {
            if (cam == null) return;
            // Don't pan/zoom the world camera while the finger (or mouse) is over UI — otherwise dragging the
            // World Map or tapping a button secretly drags the 3D camera underneath.
            if (PointerOverUI()) { _dragging = false; _lastPinch = 0f; return; }
            Zoom();
            Pan();
        }

        public static bool PointerOverUI()
        {
            var es = EventSystem.current;
            if (es == null) return false;
            if (es.IsPointerOverGameObject()) return true;   // mouse / primary pointer
            var ts = Touchscreen.current;
            if (ts != null)
            {
                var touches = ts.touches;
                for (int i = 0; i < touches.Count; i++)
                    if (touches[i].press.isPressed && es.IsPointerOverGameObject(touches[i].touchId.ReadValue()))
                        return true;
            }
            return false;
        }

        private void Zoom()
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f) SetZoom(cam.orthographicSize - Mathf.Sign(scroll) * scrollZoomSpeed);
            }

            var ts = Touchscreen.current;
            if (ts != null)
            {
                Vector2 a = Vector2.zero, b = Vector2.zero; int n = 0;
                var touches = ts.touches;
                for (int i = 0; i < touches.Count && n < 2; i++)
                {
                    if (!touches[i].press.isPressed) continue;
                    if (n == 0) a = touches[i].position.ReadValue(); else b = touches[i].position.ReadValue();
                    n++;
                }
                if (n == 2)
                {
                    float dist = Vector2.Distance(a, b);
                    if (_lastPinch > 0f) { float d = dist - _lastPinch; if (Mathf.Abs(d) > 0.01f) SetZoom(cam.orthographicSize - d * pinchZoomSpeed); }
                    _lastPinch = dist;
                }
                else { _lastPinch = 0f; }
            }
        }

        private void Pan()
        {
            var ts = Touchscreen.current;
            if (ts != null)
            {
                var touches = ts.touches; int n = 0; TouchControl first = null;
                for (int i = 0; i < touches.Count; i++) if (touches[i].press.isPressed) { n++; if (first == null) first = touches[i]; }
                if (n > 1) return;
                if (n == 1 && first != null) { Vector2 d = first.delta.ReadValue(); PanBy(-d.x, -d.y); return; }
            }

            var mouse = Mouse.current;
            if (mouse == null) return;
            bool held = mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed;
            Vector2 mp = mouse.position.ReadValue();
            if (held && !_dragging) { _dragging = true; _lastMouse = mp; }
            if (!held) _dragging = false;
            if (_dragging) { Vector2 d = mp - _lastMouse; PanBy(-d.x, -d.y); _lastMouse = mp; }
        }

        public void SetZoom(float size) => cam.orthographicSize = Mathf.Clamp(size, minSize, maxSize);

        public void PanBy(float screenDx, float screenDy)
        {
            float scale = (cam.orthographicSize / 200f) * panSpeed;
            Vector3 move = _right * (screenDx * scale) + _forward * (screenDy * scale);
            Vector3 p = transform.position + move;
            p.x = Mathf.Clamp(p.x, boundsX.x, boundsX.y);
            p.z = Mathf.Clamp(p.z, boundsZ.x, boundsZ.y);
            transform.position = p;
        }

        // ---- camera profiles (used by WorldMapUI to switch between MAP mode and ISLAND mode) ----

        /// <summary>Set the pan clamp rectangle (world X/Z ranges).</summary>
        public void SetBounds(Vector2 x, Vector2 z) { boundsX = x; boundsZ = z; }

        /// <summary>Set the zoom (orthographic-size) range and re-clamp the current size into it.</summary>
        public void SetZoomRange(float min, float max)
        {
            minSize = min; maxSize = max;
            if (cam != null) cam.orthographicSize = Mathf.Clamp(cam.orthographicSize, minSize, maxSize);
        }

        /// <summary>Snap the camera to a framing (position + rotation + ortho size). Recaches the pan basis so
        /// dragging stays correct after a rotation change.</summary>
        public void FrameTo(Vector3 pos, Quaternion rot, float size)
        {
            transform.SetPositionAndRotation(pos, rot);
            Vector3 r = transform.right; r.y = 0f; _right = r.sqrMagnitude > 0.0001f ? r.normalized : Vector3.right;
            Vector3 f = transform.forward; f.y = 0f; _forward = f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
            if (cam != null) cam.orthographicSize = Mathf.Clamp(size, minSize, maxSize);
        }
    }
}
