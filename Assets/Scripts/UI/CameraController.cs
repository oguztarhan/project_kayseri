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
        // Both are FRACTIONS, not world units — see Zoom(). 0.12 per notch is a 13% step; 0.0025 per
        // pixel means a 500-pixel pinch changes the distance about three and a half times.
        [SerializeField] private float scrollZoomSpeed = 0.12f;
        [SerializeField] private float pinchZoomSpeed = 0.0025f;
        [SerializeField] private float panSpeed = 1f;             // 1 = content tracks the finger 1:1
        [SerializeField] private float smoothTime = 0.07f;        // transform → target easing
        [SerializeField] private float inertiaDamping = 6f;       // higher = flick stops sooner
        [SerializeField] private float inertiaCutoff = 0.4f;      // world units/sec below which a flick stops
        [SerializeField] private Vector2 boundsX = new Vector2(-250f, 250f);
        [SerializeField] private Vector2 boundsZ = new Vector2(-250f, 250f);
        [SerializeField] private float groundY = 6f;   // ground plane height — perspective zoom is the dolly distance to it
        [Tooltip("Sarsıntının titreşim hızı. Yüksek değer daha sert, düşük değer daha yumuşak sallar.")]
        [SerializeField] private float shakeFrequency = 24f;

        private Vector3 _right, _forward;
        private bool _dragging;
        private Vector2 _lastMouse;
        private float _lastPinch;

        private Vector3 _target;      // where the camera wants to be
        private Vector3 _smoothVel;   // SmoothDamp state

        private float _shakeAmp, _shakeTotal, _shakeLeft, _shakeSeed;

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

            // Anything in the simulation that wanted a jolt this frame left it here. See
            // Game.Systems.CameraShake for why the request comes to us rather than us being called.
            if (Game.Systems.CameraShake.Consume(out float shakeAmp, out float shakeSecs))
                Shake(shakeAmp, shakeSecs);

            // Shake is added AFTER the smoothing and is never written back into _target, _panVel or
            // ClampTarget. Fed in earlier it would be something the camera chases and the bounds
            // clamp fights, and a shake that can push the view out of bounds is a shake that can
            // strand it there. Here it is a pure offset on the rendered position and costs nothing
            // once it has decayed.
            if (_shakeLeft > 0f)
            {
                _shakeLeft -= dt;
                if (_shakeLeft <= 0f) { _shakeLeft = 0f; }
                else
                {
                    float fade = _shakeLeft / _shakeTotal;           // linear decay, so it ends flat
                    float t = (_shakeTotal - _shakeLeft) * shakeFrequency;
                    // Perlin rather than Random: successive samples are correlated, so this reads as
                    // a jolt settling rather than per-frame static. No allocation.
                    float x = Mathf.PerlinNoise(_shakeSeed, t) * 2f - 1f;
                    float y = Mathf.PerlinNoise(_shakeSeed + 37.7f, t) * 2f - 1f;
                    float amp = _shakeAmp * fade * fade;
                    transform.position += transform.right * (x * amp) + transform.up * (y * amp);
                }
            }
        }

        /// <summary>
        /// Punctuate something that just happened — a district finishing its rebuild, an unlock
        /// landing. Keep it short and small: on an idle game the camera is a window the player
        /// stares at for minutes at a time, and a shake that draws attention to itself is one they
        /// will be sick of by the third time. A second call while one is running wins only if it is
        /// stronger, so a big event is never flattened by a small one landing on top of it.
        /// </summary>
        public void Shake(float amplitude, float seconds)
        {
            if (amplitude <= 0f || seconds <= 0f) return;
            if (_shakeLeft > 0f && amplitude < _shakeAmp) return;
            _shakeAmp = amplitude;
            _shakeTotal = seconds;
            _shakeLeft = seconds;
            _shakeSeed += 13.31f;      // a different pattern each time, without Random
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

        /// <summary>
        /// Zoom is MULTIPLICATIVE, not additive — a pinch scales the distance rather than subtracting a
        /// fixed number of world units from it.
        ///
        /// It used to subtract, and that cannot be tuned to feel right at both ends. The travel here
        /// runs from roughly 150 units to 930, so a step big enough to be felt at 930 threw the camera
        /// through the floor at 150, and a step safe at 150 was invisible at 930. Measured before the
        /// change: pinchZoomSpeed 0.05 meant a 500-pixel pinch moved 25 units — five per cent of the
        /// range — which is why zoom read as broken rather than as slow.
        ///
        /// Exponent rather than a plain multiply so the sign is symmetric: pinching out by n pixels
        /// undoes pinching in by n pixels exactly, and no delta can ever drive the distance negative.
        /// The two speeds are therefore FRACTIONS PER PIXEL / PER NOTCH, not world units.
        /// </summary>
        private void Zoom()
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    SetZoom(CurrentZoom * Mathf.Exp(-Mathf.Sign(scroll) * scrollZoomSpeed));
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
                    if (_lastPinch > 0f) { float d = dist - _lastPinch; if (Mathf.Abs(d) > 0.01f) SetZoom(CurrentZoom * Mathf.Exp(-d * pinchZoomSpeed)); }
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
        /// <summary>
        /// Where the camera sits in its own zoom band: 0 fully in, 1 fully out. The band is solved per
        /// island by OperationCameraBoot and handed over through <see cref="SetZoomRange"/>, so a raw
        /// distance means nothing on its own — anything that wants to fade with zoom wants this.
        /// </summary>
        public float ZoomT => Mathf.InverseLerp(minSize, maxSize, CurrentZoom);

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
