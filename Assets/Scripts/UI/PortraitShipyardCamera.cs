using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Game.UI
{
    [RequireComponent(typeof(Camera))]
    public sealed class PortraitShipyardCamera : MonoBehaviour
    {
        public float minTravel = -16f, maxTravel = 16f;
        public float halfWidth = 10.5f;
        public Vector3 origin;
        public float travel;
        private Camera _camera;
        private bool _dragging;
        private Vector2 _previous;
        private int _pointerId;
        private float _shown, _velocity;
        private readonly System.Collections.Generic.List<RaycastResult> _hits = new System.Collections.Generic.List<RaycastResult>();
        private PointerEventData _pointerEvent;
        private EventSystem _eventSystem;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _camera.ResetAspect();
            _shown = travel;
            Screen.orientation = ScreenOrientation.Portrait;
        }

        public void Focus(Vector3 point, bool immediately = false)
        {
            // Project onto camera-up, so terrace height participates in vertical framing.
            travel = Mathf.Clamp(Vector3.Dot(point - origin, transform.up), minTravel, maxTravel);
            if (immediately) { _shown = travel; _velocity = 0; ApplyPose(); }
        }

        public void PanPixels(float dy, float pixelHeight)
        {
            var camera = _camera != null ? _camera : GetComponent<Camera>();
            travel = Mathf.Clamp(travel - dy * camera.orthographicSize * 2f / Mathf.Max(1f, pixelHeight), minTravel, maxTravel);
        }

        private bool OverUI(Vector2 position)
        {
            if (EventSystem.current == null) return false;
            if (_eventSystem != EventSystem.current)
            {
                _eventSystem = EventSystem.current;
                _pointerEvent = new PointerEventData(_eventSystem);
            }
            _pointerEvent.Reset(); _pointerEvent.position = position;
            _hits.Clear();
            _eventSystem.RaycastAll(_pointerEvent, _hits);
            return _hits.Count > 0;
        }

        private void Update()
        {
            bool down = false, began = false;
            Vector2 position = default;
            int pointer = -1;
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
            {
                var touch = Touchscreen.current.primaryTouch;
                // A second finger cancels the gesture; there is deliberately no pinch or rotation.
                int count = 0;
                foreach (var t in Touchscreen.current.touches) if (t.press.isPressed) count++;
                if (count > 1) { _dragging = false; return; }
                down = true; began = touch.press.wasPressedThisFrame;
                position = touch.position.ReadValue(); pointer = touch.touchId.ReadValue();
            }
            else if (Mouse.current != null)
            {
                down = Mouse.current.leftButton.isPressed;
                began = Mouse.current.leftButton.wasPressedThisFrame;
                position = Mouse.current.position.ReadValue();
                if (!OverUI(position)) PanPixels(Mouse.current.scroll.ReadValue().y * 0.8f, Screen.height);
            }
            if (began)
            {
                _dragging = !OverUI(position); _previous = position; _pointerId = pointer;
            }
            if (!down || pointer != _pointerId) _dragging = false;
            if (_dragging) { PanPixels(position.y - _previous.y, Screen.height); _previous = position; }
            _shown = Mathf.SmoothDamp(_shown, travel, ref _velocity, .08f, Mathf.Infinity, Time.unscaledDeltaTime);
            ApplyPose();
        }

        private void ApplyPose()
        {
            if (_camera == null) _camera = GetComponent<Camera>();
            _camera.orthographic = true;
            // Fix width, not height: tall devices reveal more coastline without cropping stations.
            _camera.orthographicSize = halfWidth / Mathf.Max(.1f, _camera.aspect);
            transform.position = origin + transform.up * _shown;
        }
    }
}
