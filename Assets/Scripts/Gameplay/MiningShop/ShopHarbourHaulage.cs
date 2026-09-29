using System;
using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>Shop staging shelf → depot → quay. Cosmetic parcels never remove stock or pay rewards.</summary>
    public sealed class ShopHarbourHaulage : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float _walkSpeed = 110f;
        [SerializeField, Min(0.1f)] private float _handlingSeconds = 1.4f;
        [SerializeField, Min(0f)] private float _laneOffset = 4f;
        [SerializeField, Min(1f)] private float _portDisplaySeconds = 8f;
        [Tooltip("Local points around the west side of the shop, onto the authored harbour road.")]
        [SerializeField] private Vector3[] _shopExit =
        {
            new Vector3(-455f, 33.58f, -1460f), new Vector3(-505f, 33.58f, -1420f),
            new Vector3(-515f, 33.58f, -1350f), new Vector3(-480f, 33.58f, -1280f),
            new Vector3(-430f, 42f, -1250f)
        };
        [SerializeField] private Vector3 _pickupOffset = new Vector3(-30f, 0f, 0f);

        private sealed class Carrier
        {
            public Transform Body, Cargo;
            public PersonAnimator Animation;
            public Vector3[] Route;
            public float Length;
        }

        private MiningShopBusinessService _shop;
        private AccessibilityConfig _accessibility;
        private ShopHaulage _state;
        private Transform _visuals;
        private Carrier _first, _second;
        private readonly Transform[] _depot = new Transform[ShopHaulage.Capacity];
        private readonly Transform[] _port = new Transform[ShopHaulage.Capacity];
        private readonly float[] _portLife = new float[ShopHaulage.Capacity];
        private long _delivered;
        private float _height;
        private CoalOperation _operation;
        private Vector3 _shelf;
        private float _personHeight;

        private void Awake() => _accessibility = ServiceLocator.Get<AccessibilityConfig>();

        public void Initialize(CoalOperation operation, MiningShopBusinessService shop, Vector3 shelf, float height)
        {
            _operation = operation;
            _shop = shop;
            _shelf = shelf;
            _personHeight = height;
        }

        // MiningShopView can start before CoalOperation builds its island routes.
        private void Start() => Build(_operation, _shop, _shelf, _personHeight);

        private void Build(CoalOperation operation, MiningShopBusinessService shop, Vector3 shelf, float height)
        {
            if (_state != null || operation == null || shop == null) return;
            if (!operation.TryGetShopHaulRoads(out Vector3[] portRoad, out Vector3[] road,
                out Vector3 depot, out Vector3 port)) { enabled = false; return; }
            GameObject[] people = operation.WorkerPrefabs;
            GameObject crate = Resources.Load<GameObject>("Market/Props/crate");
            if (people == null || people.Length == 0 || people[0] == null || crate == null)
            { enabled = false; return; }

            _shop = shop;
            _height = height * 0.9f;
            int depotIndex = Nearest(road, depot), quayIndex = Nearest(portRoad, port);
            var first = new List<Vector3> { shelf + transform.TransformVector(_pickupOffset) };
            for (int i = 0; i < _shopExit.Length; i++) first.Add(transform.TransformPoint(_shopExit[i]));
            first.AddRange(portRoad);
            for (int i = 0; i <= depotIndex; i++) first.Add(road[i]);
            var second = new List<Vector3>();
            for (int i = depotIndex; i >= 0; i--) second.Add(road[i]);
            for (int i = portRoad.Length - 1; i >= quayIndex; i--) second.Add(portRoad[i]);
            second.Add(port);

            _visuals = new GameObject("SevkiyatGorselleri").transform;
            _visuals.SetParent(transform, false);
            _first = BuildCarrier(people[0], crate, first.ToArray(), "DepoyaTasiyan");
            _second = BuildCarrier(people.Length > 1 && people[1] != null ? people[1] : people[0],
                crate, second.ToArray(), "LimanaTasiyan");
            for (int i = 0; i < ShopHaulage.Capacity; i++)
            {
                _depot[i] = Prop(crate, _visuals, _height * 0.45f);
                _depot[i].name = "DepoSandigi" + i;
                _depot[i].position = first[first.Count - 1] + new Vector3(35f, (i / 2) * _height * 0.35f, (i % 2) * _height * 0.5f);
                _port[i] = Prop(crate, _visuals, _height * 0.45f);
                _port[i].name = "LimanSandigi" + i;
                _port[i].position = port + new Vector3(-35f, (i / 2) * _height * 0.35f, (i % 2) * _height * 0.5f);
                _depot[i].gameObject.SetActive(false);
                _port[i].gameObject.SetActive(false);
            }
            _state = new ShopHaulage(_first.Length / _walkSpeed, _second.Length / _walkSpeed, _handlingSeconds);
            _state.Baseline(ArrivedAtShop());
            Draw(_first, true);
            Draw(_second, false);
        }

        private static int Nearest(Vector3[] route, Vector3 point)
        {
            int best = 0;
            float nearest = float.MaxValue;
            for (int i = 0; i < route.Length; i++)
            {
                float distance = (route[i] - point).sqrMagnitude;
                if (distance < nearest) { nearest = distance; best = i; }
            }
            return best;
        }

        private Carrier BuildCarrier(GameObject person, GameObject crate, Vector3[] route, string name)
        {
            var carrier = new Carrier { Body = new GameObject(name).transform, Route = route };
            carrier.Body.SetParent(_visuals, false);
            Transform body = Prop(person, carrier.Body, _height, true);
            carrier.Animation = new PersonAnimator(body);
            carrier.Cargo = Prop(crate, carrier.Body, _height * 0.45f);
            carrier.Cargo.name = "TasinanSandik";
            for (int i = 1; i < route.Length; i++) carrier.Length += Vector3.Distance(route[i - 1], route[i]);
            return carrier;
        }

        private static Transform Prop(GameObject prefab, Transform parent, float size, bool person = false)
        {
            // Measure the prefab, before animation changes a skinned renderer's bounds.
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(Vector3.zero, Vector3.one);
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            Transform pivot = new GameObject(prefab.name).transform;
            pivot.SetParent(parent, false);
            Transform item = Instantiate(prefab, pivot).transform;
            item.localRotation = Quaternion.identity;
            float extent = person ? bounds.size.y : Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            float scale = size / Mathf.Max(0.001f, extent);
            item.localScale = Vector3.one * scale;
            item.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) * scale;
            // Props must never intercept taps meant for the island.
            foreach (Collider collider in item.GetComponentsInChildren<Collider>(true)) Destroy(collider);
            return pivot;
        }

        private long ArrivedAtShop()
        {
            MiningShopBusinessSimulation.Snapshot view = _shop.View;
            long total = 0;
            for (int i = 0; i < view.AvailableProductCount; i++)
            {
                var line = view.ProductAt(i);
                long arrived = Math.Max(0, line.Produced - line.OutputStock - line.PickupReserved
                    - (view.CarrierProductIndex == i ? view.CarrierCount : 0));
                total += Math.Min(long.MaxValue - total, arrived);
            }
            return total;
        }

        private void OnEnable()
        {
            if (_state != null) _state.Baseline(ArrivedAtShop());
        }

        private void Update()
        {
            if (_state == null) return;
            bool visible = _accessibility == null || !_accessibility.ReduceMotion;
            if (_visuals.gameObject.activeSelf != visible) _visuals.gameObject.SetActive(visible);
            if (!visible || _shop.View.PendingSeconds > 0d)
            { _state.Baseline(ArrivedAtShop()); return; }
            _state.Observe(ArrivedAtShop());
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            _state.Tick(dt);
            Draw(_first, true);
            Draw(_second, false);
            if (_state.Delivered > _delivered)
            {
                _delivered = _state.Delivered;
                _portLife[(int)((_delivered - 1) % ShopHaulage.Capacity)] = _portDisplaySeconds;
            }
            for (int i = 0; i < ShopHaulage.Capacity; i++)
            {
                bool stacked = i < _state.AtDepot;
                if (_depot[i].gameObject.activeSelf != stacked) _depot[i].gameObject.SetActive(stacked);
                _portLife[i] = Mathf.Max(0f, _portLife[i] - dt);
                bool shown = _portLife[i] > 0f;
                if (_port[i].gameObject.activeSelf != shown) _port[i].gameObject.SetActive(shown);
            }
        }

        private void Draw(Carrier carrier, bool first)
        {
            ShopHaulage.Phase phase = first ? _state.First : _state.Second;
            float progress = (float)_state.Progress(first);
            bool moving = phase == ShopHaulage.Phase.Carrying || phase == ShopHaulage.Phase.Returning;
            float fraction = phase == ShopHaulage.Phase.Returning ? 1f - progress
                : phase == ShopHaulage.Phase.Carrying ? progress : phase == ShopHaulage.Phase.Unloading ? 1f : 0f;
            Vector3 position = At(carrier.Route, carrier.Length * fraction, out Vector3 direction);
            if (phase == ShopHaulage.Phase.Returning) direction = -direction;
            direction.y = 0f;
            direction.Normalize();
            carrier.Body.position = position + Vector3.Cross(Vector3.up, direction) * _laneOffset;
            if (direction.sqrMagnitude > 0.001f) carrier.Body.rotation = Quaternion.LookRotation(direction);
            carrier.Animation.SetMoving(moving);
            carrier.Animation.SetSpeed(moving ? _walkSpeed / (_height * 0.8f) : 1f);
            bool loaded = phase == ShopHaulage.Phase.Loading || phase == ShopHaulage.Phase.Carrying || phase == ShopHaulage.Phase.Unloading;
            if (carrier.Cargo.gameObject.activeSelf != loaded) carrier.Cargo.gameObject.SetActive(loaded);
            float lift = phase == ShopHaulage.Phase.Loading ? progress : phase == ShopHaulage.Phase.Unloading ? 1f - progress : 1f;
            carrier.Cargo.localPosition = new Vector3(0f, Mathf.Lerp(2f, _height * 0.45f, lift), _height * 0.35f);
        }

        private static Vector3 At(Vector3[] points, float distance, out Vector3 direction)
        {
            for (int i = 1; i < points.Length; i++)
            {
                direction = points[i] - points[i - 1];
                float length = direction.magnitude;
                if (distance <= length && length > 0.001f)
                    return Vector3.Lerp(points[i - 1], points[i], distance / length);
                distance -= length;
            }
            direction = points[points.Length - 1] - points[points.Length - 2];
            return points[points.Length - 1];
        }
    }
}
