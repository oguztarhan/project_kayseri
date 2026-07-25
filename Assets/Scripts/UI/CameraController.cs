using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Game.UI
{
    /// <summary>
    /// Island camera: drag to pan, scroll / pinch to zoom, clamped to the operation's footprint.
    /// Uses the Input System package (this project's active input handling).
    ///
    /// Input never writes the transform directly — it moves a target position that the transform eases
    /// toward, and a released drag keeps its velocity. Snapping the camera to raw input every frame is
    /// most of what makes a mobile camera feel unfinished.
    /// </summary>
    public sealed class CameraController : MonoBehaviour
    {
        [SerializeField] private Camera cam;
        [SerializeField] private float minSize = 8f;
        [SerializeField] private float maxSize = 95f;
        [SerializeField] private float scrollZoomSpeed = 6f;
        [SerializeField] private float pinchZoomSpeed = 0.04f;
        [SerializeField] private float panSpeed = 1f;             // 1 = content tracks the finger 1:1
        [SerializeField] private float smoothTime = 0.07f;        // transform → target easing
        [SerializeField] private float inertiaDamping = 6f;       // higher = flick stops sooner
        [SerializeField] private float inertiaCutoff = 0.4f;      // world units/sec below which a flick stops
        [SerializeField] private Vector2 boundsX = new Vector2(-250f, 250f);
        [SerializeField] private Vector2 boundsZ = new Vector2(-250f, 250f);
        [SerializeField] private float groundY = 6f;   // ground plane height — perspective zoom is the dolly distance to it

        private Vector3 _right, _forward;
        private bool _dragging;
        private Vector2 _lastMouse;
        private float _lastPinch;

        private Vector3 _target;      // where the camera wants to be
        private Vector3 _smoothVel;   // SmoothDamp state
        private Vector3 _panVel;      // world units/sec, for flick inertia
        private bool _pannedThisFrame;

        private void Awake()
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (cam == null) cam = Camera.main;
            CacheBasis();
            _target = transform.position;
        }

        private void CacheBasis()
        {
            Vector3 r = transform.right; r.y = 0f; _right = r.sqrMagnitude > 0.0001f ? r.normalized : Vector3.right;
            Vector3 f = transform.forward; f.y = 0f; _forward = f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
        }

        private void Update()
        {
            if (cam == null) return;
            float dt = Time.unscaledDeltaTime;
            _pannedThisFrame = false;

            // Don't pan/zoom the world camera while the finger (or mouse) is over UI — otherwise dragging a
            // panel or tapping a button secretly drags the 3D camera underneath.
            if (PointerOverUI()) { _dragging = false; _lastPinch = 0f; _panVel = Vector3.zero; }
            else { Zoom(); Pan(dt); }

            // A released flick keeps gliding, then settles.
            if (!_pannedThisFrame && _panVel.sqrMagnitude > inertiaCutoff * inertiaCutoff)
            {
                _target += _panVel * dt;
                _panVel *= Mathf.Exp(-inertiaDamping * dt);
                ClampTarget();
            }
            else if (!_pannedThisFrame) _panVel = Vector3.zero;

            transform.position = Vector3.SmoothDamp(transform.position, _target, ref _smoothVel, smoothTime, Mathf.Infinity, dt);
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
                if (Mathf.Abs(scroll) > 0.01f) SetZoom(CurrentZoom - Mathf.Sign(scroll) * scrollZoomSpeed);
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
                    if (_lastPinch > 0f) { float d = dist - _lastPinch; if (Mathf.Abs(d) > 0.01f) SetZoom(CurrentZoom - d * pinchZoomSpeed); }
                    _lastPinch = dist;
                }
                else { _lastPinch = 0f; }
            }
        }

        private void Pan(float dt)
        {
            var ts = Touchscreen.current;
            if (ts != null)
            {
                var touches = ts.touches; int n = 0; TouchControl first = null;
                for (int i = 0; i < touches.Count; i++) if (touches[i].press.isPressed) { n++; if (first == null) first = touches[i]; }
                if (n > 1) { _panVel = Vector3.zero; return; }
                if (n == 1 && first != null) { Vector2 d = first.delta.ReadValue(); PanBy(-d.x, -d.y, dt); return; }
                if (n == 0 && _dragging) _dragging = false;
            }

            var mouse = Mouse.current;
            if (mouse == null) return;
            bool held = mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed;
            Vector2 mp = mouse.position.ReadValue();
            if (held && !_dragging) { _dragging = true; _lastMouse = mp; }
            if (!held) _dragging = false;
            if (_dragging) { Vector2 d = mp - _lastMouse; PanBy(-d.x, -d.y, dt); _lastMouse = mp; }
        }

        /// <summary>Current zoom level: orthographic size, or (perspective) dolly distance to the ground plane.</summary>
        public float CurrentZoom => cam.orthographic
            ? cam.orthographicSize
            : (_target.y - groundY) / Mathf.Max(0.2f, -transform.forward.y);

        public void SetZoom(float size)
        {
            if (cam.orthographic) { cam.orthographicSize = Mathf.Clamp(size, minSize, maxSize); return; }
            float target = Mathf.Clamp(size, minSize, maxSize);
            _target += transform.forward * (CurrentZoom - target);   // dolly toward/away from the ground
            ClampTarget();                                           // dollying must not walk us out of bounds
        }

        public void PanBy(float screenDx, float screenDy) { PanBy(screenDx, screenDy, Time.unscaledDeltaTime); }

        private void PanBy(float screenDx, float screenDy, float dt)
        {
            // World units covered by one screen pixel at the ground plane. The forward axis is foreshortened
            // by the camera's tilt, so it needs the 1/sin(pitch) correction to track the ground 1:1.
            float halfFov = (cam.orthographic ? 30f : cam.fieldOfView) * 0.5f * Mathf.Deg2Rad;
            float worldPerPixel = (2f * CurrentZoom * Mathf.Tan(halfFov)) / Mathf.Max(1, Screen.height) * panSpeed;
            float sinPitch = Mathf.Max(0.25f, -transform.forward.y);

            Vector3 move = _right * (screenDx * worldPerPixel) + _forward * (screenDy * worldPerPixel / sinPitch);
            _target += move;
            ClampTarget();
            if (dt > 0.0001f) _panVel = move / dt;
            _pannedThisFrame = true;
        }

        private void ClampTarget()
        {
            _target.x = Mathf.Clamp(_target.x, boundsX.x, boundsX.y);
            _target.z = Mathf.Clamp(_target.z, boundsZ.x, boundsZ.y);
        }

        // ---- camera profiles (used by OperationCameraBoot when framing / travelling) ----

        /// <summary>Set the pan clamp rectangle (world X/Z ranges).</summary>
        public void SetBounds(Vector2 x, Vector2 z) { boundsX = x; boundsZ = z; ClampTarget(); }

        /// <summary>Set the ground height used by perspective zoom.</summary>
        public void SetGroundY(float y) { groundY = y; }

        /// <summary>Set the zoom range and re-clamp the current zoom into it.</summary>
        public void SetZoomRange(float min, float max)
        {
            minSize = min; maxSize = max;
            if (cam != null) SetZoom(CurrentZoom);
        }

        /// <summary>
        /// Snap the camera to a framing. In perspective the dolly distance must already be baked into
        /// <paramref name="pos"/> by the caller — <paramref name="size"/> is only used for orthographic.
        /// </summary>
        public void FrameTo(Vector3 pos, Quaternion rot, float size)
        {
            transform.SetPositionAndRotation(pos, rot);
            CacheBasis();
            _target = pos;
            _smoothVel = Vector3.zero;
            _panVel = Vector3.zero;
            if (cam != null && cam.orthographic) cam.orthographicSize = Mathf.Clamp(size, minSize, maxSize);
        }
    }
}
