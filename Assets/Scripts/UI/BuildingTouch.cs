using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.UI
{
    /// <summary>Release-to-select input and a bounded, two-step building preview.</summary>
    public sealed class BuildingTouch : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float _dragPixels = 18f;
        [SerializeField, Min(1f)] private float _previewSeconds = 10f;
        private readonly List<Renderer> _bodies = new List<Renderer>();
        private readonly List<int> _stations = new List<int>();
        private CoalOperation _operation;
        private BuildingSigns _signs;
        private CameraController _rig;
        private Camera _camera;
        private IndustrialSiteActivity _activity;
        private StationScreenUI _screen;
        private AccessibilityConfig _accessibility;
        private Vector2 _press;
        private bool _pressed;
        private float _until;
        private int _selected = -1;
        private Bounds _hitArea;
        private bool _hasHit;
        public int SelectedStation => _selected;
        public Vector3 PreviewAnchor { get; private set; }

        private void Awake()
        {
            _camera = Camera.main;
            _accessibility = ServiceLocator.Get<AccessibilityConfig>();
            _screen = FindAnyObjectByType<StationScreenUI>(FindObjectsInactive.Include);
        }

        public void Initialize(CoalOperation operation, BuildingSigns signs, CameraController rig)
        {
            _operation = operation; _signs = signs; _rig = rig;
            _selected = -1; _pressed = false;
            _bodies.Clear(); _stations.Clear();
            if (!operation.OurShipBerth(out _, out _, out Transform island) || island == null) return;
            _activity = island.GetComponentInChildren<IndustrialSiteActivity>(true);
            foreach (Renderer r in island.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.name;
                int station = n.StartsWith("Mine |", System.StringComparison.Ordinal) ? IslandEconomy.Mine
                    : n.StartsWith("Warehouse |", System.StringComparison.Ordinal) ? IslandEconomy.Storage
                    : n.StartsWith("Smelter |", System.StringComparison.Ordinal)
                        || n.StartsWith("Factory |", System.StringComparison.Ordinal)
                        || n.StartsWith("Refinery |", System.StringComparison.Ordinal) ? IslandEconomy.Smelter
                    : n.StartsWith("Market |", System.StringComparison.Ordinal) ? IslandEconomy.Market
                    : n == "Port | concrete quay" ? IslandEconomy.Power : -1;
                if (station < 0) continue;
                _bodies.Add(r); _stations.Add(station);
            }
        }

        public bool Select(int station)
        {
            if (_rig != null && _rig.InputLocked) return true;
            if (_selected == station && Time.unscaledTime < _until)
            {
                _selected = -1; _signs.PreviewBuilding(-1); return false;
            }
            Bounds area = _hitArea;
            if (!_hasHit)
            {
                bool found = false;
                for (int i = 0; i < _bodies.Count; i++)
                {
                    if (_stations[i] != station || _bodies[i] == null) continue;
                    // Factory/refinery art shares the refining upgrade, but a label focuses its primary furnace.
                    if (station == IslandEconomy.Smelter && !_bodies[i].name.StartsWith("Smelter |", System.StringComparison.Ordinal)) continue;
                    if (!found) { area = _bodies[i].bounds; found = true; }
                    else area.Encapsulate(_bodies[i].bounds);
                }
                if (!found && !_operation.StationFocus(station, out area))
                {
                    if (!_operation.StationAnchor(station, out Vector3 point)) return false;
                    area = new Bounds(point, Vector3.one * 100f);
                }
            }
            _hasHit = false;
            _selected = station; _until = Time.unscaledTime + _previewSeconds;
            PreviewAnchor = new Vector3(area.center.x, area.max.y, area.center.z);
            _signs.PreviewBuilding(station);
            bool reduce = _accessibility != null && _accessibility.ReduceMotion;
            if (_rig != null) _rig.FocusBuilding(area, reduce);
            if (!reduce && _activity != null && _camera != null) _activity.React(area.center, _camera.transform.position);
            return true;
        }

        private void Update()
        {
            if (_selected >= 0 && Time.unscaledTime >= _until)
            { _selected = -1; _signs.PreviewBuilding(-1); }
            if (_operation == null || !_operation.isActiveAndEnabled || _camera == null
                || (_rig != null && _rig.InputLocked) || (_screen != null && _screen.IsOpen))
            { _pressed = false; return; }
            var touch = Touchscreen.current;
            if (touch != null && (touch.primaryTouch.press.isPressed || touch.primaryTouch.press.wasReleasedThisFrame))
            {
                int count = 0;
                for (int i = 0; i < touch.touches.Count; i++) if (touch.touches[i].press.isPressed) count++;
                if (count > 1) { _pressed = false; return; }
                Pointer(touch.primaryTouch.position.ReadValue(), touch.primaryTouch.press.wasPressedThisFrame,
                    touch.primaryTouch.press.wasReleasedThisFrame);
                return;
            }
            var mouse = Mouse.current;
            if (mouse != null) Pointer(mouse.position.ReadValue(), mouse.leftButton.wasPressedThisFrame, mouse.leftButton.wasReleasedThisFrame);
        }

        private void Pointer(Vector2 position, bool down, bool up)
        {
            if (down) { _press = position; _pressed = !CameraController.PointerOverUI(); }
            float threshold = _dragPixels * Mathf.Max(1f, Screen.height / 1920f);
            if (_pressed && ((position - _press).sqrMagnitude > threshold * threshold || CameraController.PointerOverUI())) _pressed = false;
            if (!up || !_pressed) return;
            _pressed = false;
            Ray ray = _camera.ScreenPointToRay(position);
            float nearest = float.MaxValue; int picked = -1;
            for (int i = 0; i < _bodies.Count; i++)
            {
                Renderer r = _bodies[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!r.bounds.IntersectRay(ray, out float distance) || distance >= nearest) continue;
                nearest = distance; picked = i;
            }
            if (picked < 0) return;
            // Frame the selected building group, not an individual chimney or roof tile.
            Transform group = _bodies[picked].transform.parent;
            _hitArea = _bodies[picked].bounds;
            for (int i = 0; i < _bodies.Count; i++)
                if (_bodies[i] != null && _bodies[i].transform.parent == group) _hitArea.Encapsulate(_bodies[i].bounds);
            _hasHit = true;
            _signs.TouchStation(_stations[picked]);
            _hasHit = false;
        }
    }
}
