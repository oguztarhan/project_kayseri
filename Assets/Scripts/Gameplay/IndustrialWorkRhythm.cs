using Game.Core;
using Game.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Gameplay
{
    /// <summary>Staggered maintenance cycles; visible goods still belong to the real stock buffers.</summary>
    public sealed class IndustrialWorkRhythm : MonoBehaviour
    {
        [SerializeField, Min(4f)] private float _cycleSeconds = 12f;
        [SerializeField, Range(0.2f, 0.8f)] private float _workFraction = 0.6f;
        [SerializeField, Min(0f)] private float _fanSpeed = 150f;
        [SerializeField, Min(1f)] private float _beltLength = 145f;
        [SerializeField, Min(1f)] private float _pistonTravel = 16f;
        [SerializeField, Min(1f)] private float _dustSize = 18f;
        private readonly Material[] _materials = new Material[4];
        private readonly Transform[] _slats = new Transform[8], _rocks = new Transform[4], _dust = new Transform[4], _steam = new Transform[3];
        private CoalOperation _operation;
        private IndustrialSiteActivity _crew;
        private AccessibilityConfig _accessibility;
        private Transform _root, _mine, _factory, _refinery, _fan, _arm, _piston, _cart, _cartLoad;
        private Renderer _mineSource, _factorySource, _refinerySource, _doorSource;
        private Vector3 _beltStart, _cartStart, _dustOrigin, _pistonHome, _steamOrigin, _doorHome;
        private Quaternion _armHome;
        private Renderer _lamp;
        private float _fanAngle, _beltPhase, _doorHeight;
        private double _lastRefined;
        private float _heat;

        private void Awake() => _accessibility = ServiceLocator.Get<AccessibilityConfig>();

        public void Initialize(CoalOperation operation, Transform island, IndustrialSiteActivity crew)
        {
            _operation = operation; _crew = crew;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) { enabled = false; return; }
            Color[] colors = { new Color(0.22f, 0.28f, 0.32f), new Color(0.91f, 0.63f, 0.14f),
                new Color(0.76f, 0.81f, 0.79f), new Color(1f, 0.41f, 0.08f) };
            for (int i = 0; i < _materials.Length; i++)
            {
                _materials[i] = new Material(shader) { name = "Tesis calisma " + i };
                _materials[i].SetColor("_BaseColor", colors[i]);
            }
            _root = Group("CalisanMakineler", transform);
            Renderer fanSource = null;
            foreach (Renderer r in island.GetComponentsInChildren<Renderer>(true))
            {
                switch (r.name)
                {
                    case "Mine | pitch black entrance": _mineSource = r; break;
                    case "Factory | rooftop exhaust fan": fanSource = r; break;
                    case "Factory | roller door": _factorySource = r; break;
                    case "Refinery | equipment slab": _refinerySource = r; break;
                    case "Warehouse | loading door": _doorSource = r; break;
                }
            }
            if (_mineSource != null) BuildMine(_mineSource.bounds);
            if (fanSource != null && _factorySource != null) BuildFactory(fanSource.bounds, _factorySource.bounds);
            if (_refinerySource != null) BuildRefinery(_refinerySource.bounds);
            if (_doorSource != null)
            { _doorHome = _doorSource.transform.localPosition; _doorHeight = _doorSource.bounds.size.y * 0.7f; }
        }

        private Transform Group(string name, Transform parent)
        {
            var t = new GameObject(name).transform; t.SetParent(parent, false); return t;
        }

        private Transform Shape(string name, Transform parent, Vector3 point, Vector3 size, int material, PrimitiveType type = PrimitiveType.Cube)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name;
            go.transform.SetParent(parent, false); go.transform.position = point; go.transform.localScale = size;
            Destroy(go.GetComponent<Collider>());
            var r = go.GetComponent<Renderer>(); r.sharedMaterial = _materials[material]; r.shadowCastingMode = ShadowCastingMode.Off;
            return go.transform;
        }

        private void BuildMine(Bounds b)
        {
            _mine = Group("MadenBakimHatti", _root);
            _beltStart = new Vector3(b.min.x - 190f, b.min.y + 14f, b.min.z - 205f);
            Shape("BantGovdesi", _mine, _beltStart + Vector3.right * (_beltLength * 0.5f), new Vector3(_beltLength + 20f, 10, 28), 0);
            for (int i = 0; i < _slats.Length; i++) _slats[i] = Shape("BantDilimi", _mine, _beltStart, new Vector3(5, 3, 29), 1);
            for (int i = 0; i < _rocks.Length; i++)
            {
                _rocks[i] = Shape("BanttakiCevher", _mine, _beltStart, new Vector3(13, 12, 14), 0);
                _rocks[i].rotation = Quaternion.Euler(18, i * 43f, 27);
            }
            _cartStart = _beltStart + new Vector3(_beltLength + 30, -8, 0);
            _cart = Group("KontrolVagonu", _mine); _cart.position = _cartStart;
            Shape("VagonGovdesi", _cart, _cartStart + Vector3.up * 12, new Vector3(35, 18, 30), 1);
            for (int i = 0; i < 4; i++)
                Shape("VagonTekerlegi", _cart, _cartStart + new Vector3(i % 2 == 0 ? -14 : 14, 2, i < 2 ? -14 : 14), Vector3.one * 10, 0, PrimitiveType.Sphere);
            _cartLoad = Shape("VagonCevheri", _cart, _cartStart + Vector3.up * 23, new Vector3(26, 12, 22), 0);
            _dustOrigin = new Vector3(b.center.x - 50, b.min.y + 8, b.min.z - 35);
            for (int i = 0; i < _dust.Length; i++) _dust[i] = Shape("KazmaTozu", _mine, _dustOrigin, Vector3.one, 2, PrimitiveType.Sphere);
        }

        private void BuildFactory(Bounds fan, Bounds door)
        {
            _factory = Group("FabrikaBakimi", _root);
            _fan = Group("DonenFan", _factory); _fan.position = new Vector3(fan.center.x, fan.max.y + 3, fan.center.z);
            Shape("FanKanadiA", _fan, _fan.position, new Vector3(fan.size.x * 0.78f, 3, 7), 0);
            Shape("FanKanadiB", _fan, _fan.position, new Vector3(7, 3, fan.size.z * 0.78f), 0);
            Vector3 basePoint = new Vector3(door.max.x + 24, door.min.y + 14, door.min.z - 24);
            Shape("KolTabani", _factory, basePoint, new Vector3(30, 26, 30), 0);
            _arm = Group("MakineKolu", _factory); _arm.position = basePoint + Vector3.up * 12;
            Shape("Kol", _arm, _arm.position + Vector3.up * 23, new Vector3(9, 46, 9), 1);
            Shape("KolBasi", _arm, _arm.position + new Vector3(0, 45, -10), new Vector3(17, 12, 23), 0);
            _armHome = _arm.localRotation;
            _lamp = Shape("CalismaLambasi", _factory, basePoint + new Vector3(0, 65, 0), Vector3.one * 11, 3, PrimitiveType.Sphere).GetComponent<Renderer>();
        }

        private void BuildRefinery(Bounds b)
        {
            _refinery = Group("RafineriBakimi", _root);
            _pistonHome = new Vector3(b.min.x + 34, b.min.y + 33, b.min.z - 35);
            Shape("PistonTabani", _refinery, _pistonHome - Vector3.up * 20, new Vector3(32, 22, 32), 0);
            _piston = Shape("CalisanPiston", _refinery, _pistonHome, new Vector3(14, 30, 14), 1);
            _steamOrigin = _pistonHome + new Vector3(22, 10, 0);
            for (int i = 0; i < _steam.Length; i++) _steam[i] = Shape("BasincBuhari", _refinery, _steamOrigin, Vector3.one, 2, PrimitiveType.Sphere);
        }

        private void Update()
        {
            if (_root == null) return;
            bool active = _operation != null && _operation.isActiveAndEnabled && (_accessibility == null || !_accessibility.ReduceMotion);
            if (_root.gameObject.activeSelf != active) _root.gameObject.SetActive(active);
            if (!active) { RestoreDoor(); return; }
            float dt = Mathf.Min(Time.deltaTime, 0.1f), time = _crew.ActivityTime;
            float minePhase = Mathf.Repeat(time / _cycleSeconds, 1f);
            bool working = minePhase < _workFraction;
            if (_mine != null)
            {
                _mine.gameObject.SetActive(_mineSource.enabled && _mineSource.gameObject.activeInHierarchy);
                if (working) _beltPhase = Mathf.Repeat(_beltPhase + dt * 0.18f, 1f);
                for (int i = 0; i < _slats.Length; i++) _slats[i].position = _beltStart + new Vector3(Mathf.Repeat(_beltPhase + i / 8f, 1f) * _beltLength, 7, 0);
                bool stocked = _operation.StorageOre > 0d;
                for (int i = 0; i < _rocks.Length; i++)
                { _rocks[i].gameObject.SetActive(stocked); _rocks[i].position = _beltStart + new Vector3(Mathf.Repeat(_beltPhase + i / 4f, 1f) * _beltLength, 16, 0); }
                _cartLoad.gameObject.SetActive(stocked && minePhase > 0.25f && minePhase < 0.65f);
                _cart.position = _cartStart + Vector3.back * (Mathf.SmoothStep(0, 1, Mathf.PingPong(minePhase * 2f, 1f)) * 70f);
                Puffs(_dust, _crew.MiningNow ? _crew.MineWorkPoint : _dustOrigin, time, _crew.MiningNow, _dustSize);
            }
            float factoryPhase = Mathf.Repeat((time + 4f) / _cycleSeconds, 1f);
            if (_factory != null)
            {
                _factory.gameObject.SetActive(_factorySource.enabled && _factorySource.gameObject.activeInHierarchy);
                bool running = factoryPhase < _workFraction;
                _fanAngle += dt * _fanSpeed * (running ? 1f : 0.1f);
                _fan.localRotation = Quaternion.Euler(0, _fanAngle, 0);
                _arm.localRotation = _armHome * Quaternion.Euler(running ? Mathf.Sin(factoryPhase / _workFraction * Mathf.PI * 4) * 28f : 0, 0, 0);
                _lamp.enabled = running || factoryPhase > 0.85f;
            }
            bool refining = _operation.TotalRefined > _lastRefined;
            _lastRefined = _operation.TotalRefined;
            _heat = Mathf.MoveTowards(_heat, refining ? 1 : 0, dt * 2);
            if (_refinery != null)
            {
                _refinery.gameObject.SetActive(_refinerySource.enabled && _refinerySource.gameObject.activeInHierarchy);
                float phase = Mathf.Repeat((time + 8f) / _cycleSeconds, 1);
                bool running = phase < _workFraction;
                _piston.position = _pistonHome + Vector3.up * (running ? Mathf.Sin(phase / _workFraction * Mathf.PI * 6) * _pistonTravel * Mathf.Lerp(0.35f, 1, _heat) : 0);
                Puffs(_steam, _steamOrigin, time * 0.6f, phase > 0.5f && phase < 0.68f, _dustSize * 0.8f);
            }
            if (_doorSource != null) _doorSource.transform.position = _doorSource.transform.parent.TransformPoint(_doorHome) + Vector3.up * (_doorHeight * _crew.WarehouseDoorOpen);
        }

        private static void Puffs(Transform[] puffs, Vector3 origin, float time, bool on, float size)
        {
            for (int i = 0; i < puffs.Length; i++)
            {
                float age = Mathf.Repeat(time * 0.9f + i * 0.23f, 1);
                puffs[i].gameObject.SetActive(on);
                puffs[i].position = origin + new Vector3((i - 1.5f) * 8f + age * 12, age * 30, -age * 15);
                puffs[i].localScale = Vector3.one * (size * Mathf.Sin(age * Mathf.PI));
            }
        }
        private void RestoreDoor() { if (_doorSource != null) _doorSource.transform.localPosition = _doorHome; }
        private void OnDisable() { RestoreDoor(); if (_root != null) _root.gameObject.SetActive(false); }
        private void OnDestroy()
        {
            RestoreDoor();
            for (int i = 0; i < _materials.Length; i++) if (_materials[i] != null) Destroy(_materials[i]);
        }
    }
}
