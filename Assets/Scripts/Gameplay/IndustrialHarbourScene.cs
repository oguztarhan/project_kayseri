using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Gameplay
{
    /// <summary>The industrial map's authored ship, driven by the real contract visit.</summary>
    public sealed class IndustrialHarbourScene : MonoBehaviour
    {
        [SerializeField, Min(100f)] private float _seaDistance = 1000f;
        [SerializeField, Min(0f)] private float _berthClearance = 28f;
        [SerializeField, Min(1f)] private float _loadingSeconds = 14f;
        [SerializeField, Min(0.1f)] private float _tieSeconds = 1.4f;
        [SerializeField, Min(0f)] private float _sway = 0.65f;
        [SerializeField, Min(1f)] private float _workerHeight = 46f;
        [SerializeField, Min(1f)] private float _crateSize = 23f;
        [SerializeField, Min(0.1f)] private float _ropeWidth = 1.5f;
        [SerializeField, Range(0f, 1f)] private float _hornVolume = 0.45f;
        [SerializeField] private Color _ropeColor = new Color(0.43f, 0.32f, 0.19f);
        [SerializeField] private Color _lampColor = new Color(1f, 0.83f, 0.32f);
        private CoalOperation _operation;
        private ContractService _contract;
        private AccessibilityConfig _accessibility;
        private AudioService _audio;
        private Transform _ship, _effects, _worker, _cargo, _hook;
        private PersonAnimator _animation;
        private AudioSource _horn;
        private Material _ropeMaterial, _lampMaterial, _steelMaterial;
        private readonly LineRenderer[] _ropes = new LineRenderer[2];
        private readonly Vector3[] _shore = new Vector3[2], _cleat = new Vector3[2];
        private readonly Transform[] _waiting = new Transform[HarbourLoading.QueueCapacity];
        private readonly Transform[] _deck = new Transform[HarbourLoading.DeckCapacity];
        private readonly Renderer[] _lamps = new Renderer[3];
        private LineRenderer _cable;
        private Vector3 _berth, _stock, _pickup, _deckPoint, _boomTop;
        private float _clock, _tension;
        private ContractService.PortState _last;
        private HarbourLoading _loading;
        private bool _wasVisible;

        private void Awake()
        {
            _contract = ServiceLocator.Get<ContractService>();
            _accessibility = ServiceLocator.Get<AccessibilityConfig>();
            _audio = ServiceLocator.Get<AudioService>();
        }

        public void Initialize(CoalOperation operation, Transform island, AudioClip horn)
        {
            Renderer[] art = island.GetComponentsInChildren<Renderer>(true);
            Renderer hull = Find(art, "Ship | shaped hull"), quay = Find(art, "Port | concrete quay");
            Renderer rail = Find(art, "Crane | boom chord");
            GameObject crate = Resources.Load<GameObject>("Market/Props/crate");
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (hull == null || quay == null || rail == null || crate == null || shader == null || _contract == null)
            { enabled = false; return; }
            _operation = operation;
            _loading = new HarbourLoading(_loadingSeconds);
            _last = _contract.State;
            _berth = hull.bounds.center;
            _ship = new GameObject("LimanZiyaretGemisi").transform;
            _ship.SetParent(island, false);
            _ship.position = _berth;
            hull.transform.parent.SetParent(_ship, true);
            // Deck containers are separate groups in this export. They must travel with the hull.
            for (int i = 0; i < art.Length; i++)
            {
                Renderer r = art[i];
                if (!r.name.StartsWith("Intermodal container", System.StringComparison.Ordinal)) continue;
                Vector3 p = r.bounds.center;
                if (Mathf.Abs(p.z - _berth.z) < hull.bounds.extents.z && Mathf.Abs(p.x - _berth.x) < hull.bounds.extents.x)
                    r.transform.parent.SetParent(_ship, true);
            }
            // The static export overlaps the quay lip. Leave visible water between hull and quay.
            _ship.position += Vector3.back * _berthClearance;
            _berth = _ship.position;
            _effects = new GameObject("LimanDonanimi").transform;
            _effects.SetParent(transform, false);
            _ropeMaterial = Mat(shader, _ropeColor, "Liman halat");
            _lampMaterial = Mat(shader, _lampColor, "Liman isik");
            _steelMaterial = Mat(shader, new Color(0.95f, 0.7f, 0.08f), "Liman vinc uzantisi");
            Bounds q = quay.bounds;
            float edge = q.min.z + 8f, deckY = hull.bounds.max.y + 3f;
            _stock = new Vector3(q.center.x - 85f, q.max.y + 1f, q.center.z - 14f);
            _pickup = new Vector3(q.min.x + 36f, q.max.y + 1f, edge + 38f);
            _deckPoint = new Vector3(_pickup.x, deckY, _berth.z + 28f);
            _boomTop = new Vector3(_pickup.x, rail.bounds.center.y, rail.bounds.center.z);
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? Mathf.Max(q.min.x + 40f, hull.bounds.min.x + 55f)
                    : Mathf.Min(q.max.x - 70f, hull.bounds.max.x - 80f);
                _shore[i] = new Vector3(x, q.max.y + 9f, edge);
                _cleat[i] = _ship.InverseTransformPoint(new Vector3(x + (i == 0 ? -10 : 10), deckY + 3f, _berth.z + 43f));
                _ropes[i] = Line("BaglamaHalati", 13, _ropeWidth);
                Shape("Baba", PrimitiveType.Cylinder, _shore[i] - Vector3.up * 4f, new Vector3(7, 7, 7), _steelMaterial);
            }
            Vector3 armEnd = new Vector3(_pickup.x, _boomTop.y, _deckPoint.z);
            Shape("VincYuklemeKolu", PrimitiveType.Cube, (_boomTop + armEnd) * 0.5f,
                new Vector3(8, 7, Mathf.Abs(armEnd.z - _boomTop.z) + 12f), _steelMaterial);
            Shape("VincGemiUstuRay", PrimitiveType.Cube, armEnd + Vector3.right * 27f,
                new Vector3(70, 7, 8), _steelMaterial);
            _hook = Shape("VincKancasi", PrimitiveType.Cube, _pickup, new Vector3(20, 4, 14), _steelMaterial);
            _cable = Line("VincKablosu", 2, 1.2f);
            _cargo = Prop(crate, _effects, _crateSize);
            for (int i = 0; i < _waiting.Length; i++)
            {
                _waiting[i] = Prop(crate, _effects, _crateSize);
                _waiting[i].position = _stock + new Vector3((i % 2) * 26f, (i / 2) * 19f, 0);
                _waiting[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < _deck.Length; i++)
            {
                _deck[i] = Prop(crate, _ship, _crateSize);
                _deck[i].position = _deckPoint + Vector3.right * (i * 27f);
                _deck[i].gameObject.SetActive(false);
            }
            GameObject[] people = operation.WorkerPrefabs;
            if (people != null && people.Length > 0 && people[0] != null)
            {
                _worker = Prop(people[0], _effects, _workerHeight, true);
                _animation = new PersonAnimator(_worker);
            }
            for (int i = 0; i < _lamps.Length; i++)
            {
                Vector3 p = new Vector3(q.min.x + 45f + i * 105f, q.max.y + 28f, edge + 13f);
                Shape("LimanIsikDiregi", PrimitiveType.Cylinder, p - Vector3.up * 14f, new Vector3(3, 14, 3), _steelMaterial);
                _lamps[i] = Shape("LimanKarsilamaIsigi", PrimitiveType.Sphere, p, Vector3.one * 10f, _lampMaterial).GetComponent<Renderer>();
            }
            _horn = _ship.gameObject.AddComponent<AudioSource>();
            _horn.clip = horn; _horn.playOnAwake = false; _horn.loop = false;
            _horn.spatialBlend = 1f; _horn.rolloffMode = AudioRolloffMode.Linear;
            _horn.minDistance = 650f; _horn.maxDistance = 2500f;
            _horn.dopplerLevel = 0f;
            Draw(0f, true);
        }

        public bool QueueParcel() => _loading != null && _loading.Enqueue();

        private static Renderer Find(Renderer[] art, string name)
        {
            for (int i = 0; i < art.Length; i++) if (art[i].name == name) return art[i];
            return null;
        }
        private static Material Mat(Shader shader, Color color, string name)
        {
            var mat = new Material(shader) { name = name };
            mat.SetColor("_BaseColor", color);
            return mat;
        }
        private Transform Shape(string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name; go.transform.SetParent(_effects, false);
            go.transform.position = position; go.transform.localScale = scale;
            Destroy(go.GetComponent<Collider>());
            Renderer r = go.GetComponent<Renderer>(); r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return go.transform;
        }
        private LineRenderer Line(string name, int points, float width)
        {
            var go = new GameObject(name); go.transform.SetParent(_effects, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = _ropeMaterial; line.positionCount = points;
            line.startWidth = line.endWidth = width; line.useWorldSpace = true;
            line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
            return line;
        }
        private static Transform Prop(GameObject prefab, Transform parent, float size, bool person = false)
        {
            Renderer[] rs = prefab.GetComponentsInChildren<Renderer>(true);
            Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds(Vector3.zero, Vector3.one);
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            var pivot = new GameObject(person ? "LimanYukIscisi" : "SevkiyatSandigi").transform;
            pivot.SetParent(parent, false);
            Transform model = Instantiate(prefab, pivot).transform;
            float scale = size / Mathf.Max(0.001f, person ? b.size.y : Mathf.Max(b.size.x, b.size.y, b.size.z));
            model.localScale = Vector3.one * scale; model.localRotation = Quaternion.identity;
            model.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z) * scale;
            foreach (Collider c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);
            return pivot;
        }

        private void LateUpdate()
        {
            if (_loading == null) return;
            bool visible = _operation != null && _operation.isActiveAndEnabled;
            if (_effects.gameObject.activeSelf != visible) _effects.gameObject.SetActive(visible);
            if (!visible) { _ship.gameObject.SetActive(false); _horn.Stop(); _wasVisible = false; return; }
            Draw(Mathf.Min(Time.deltaTime, 0.1f), !_wasVisible);
            _wasVisible = true;
        }

        private void Draw(float dt, bool snap)
        {
            bool reduce = _accessibility != null && _accessibility.ReduceMotion;
            var state = _contract.State;
            bool moored = state == ContractService.PortState.Offering || state == ContractService.PortState.Active || state == ContractService.PortState.Reward;
            if (state != _last)
            {
                if (state == ContractService.PortState.Away || state == ContractService.PortState.Arriving) _loading.NewVisit();
                if (!snap && ((moored && _last == ContractService.PortState.Arriving) || state == ContractService.PortState.Departing))
                {
                    _horn.volume = (_audio != null ? _audio.SfxVolume : 1f) * _hornVolume;
                    if (_horn.volume > 0f) _horn.Play();
                }
                _last = state;
            }
            _horn.volume = (_audio != null ? _audio.SfxVolume : 1f) * _hornVolume;
            if (_horn.volume <= 0f && _horn.isPlaying) _horn.Stop();
            _clock += reduce ? 0 : dt;
            float dock = _contract.ShipDock01;
            // Keep the ship alongside until the mooring lines have been released.
            if (state == ContractService.PortState.Departing) dock = Mathf.Clamp01(dock / 0.985f);
            if (reduce) dock = moored ? 1f : 0f;
            _ship.gameObject.SetActive(state != ContractService.PortState.Away && (!reduce || moored));
            _ship.position = _berth + Vector3.left * (_seaDistance * (1f - dock))
                + Vector3.back * (Mathf.Sin((1f - dock) * Mathf.PI) * 30f)
                + Vector3.up * (reduce ? 0 : Mathf.Sin(_clock * 0.8f) * _sway * dock);
            float turn = state == ContractService.PortState.Departing
                ? 180f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.75f, 1f - dock)) : 0f;
            _ship.rotation = Quaternion.Euler(0f, reduce ? 0 : turn + Mathf.Sin((1f - dock) * Mathf.PI) * 5f, 0f);
            _tension = snap || reduce ? (moored ? 1f : 0f) : Mathf.MoveTowards(_tension, moored ? 1f : 0f, dt / _tieSeconds);
            for (int r = 0; r < 2; r++)
            {
                _ropes[r].enabled = _tension > 0.02f && dock > 0.985f;
                Vector3 end = _ship.TransformPoint(_cleat[r]);
                for (int p = 0; p < 13; p++)
                {
                    float k = p / 12f;
                    _ropes[r].SetPosition(p, Vector3.Lerp(_shore[r], end, k)
                        - Vector3.up * (Mathf.Sin(k * Mathf.PI) * Mathf.Lerp(22f, 2.5f, _tension)));
                }
            }
            for (int i = 0; i < _lamps.Length; i++)
                _lamps[i].enabled = moored || (!reduce && state != ContractService.PortState.Away && Mathf.Sin(_clock * 3f - i * 0.6f) > -0.2f);
            if (!reduce) _loading.Tick(dt, moored && _tension >= 0.999f);
            else if (!moored) _loading.Tick(0, false);
            DrawLoading(reduce);
        }

        private void DrawLoading(bool reduce)
        {
            for (int i = 0; i < _waiting.Length; i++) _waiting[i].gameObject.SetActive(i < _loading.Waiting);
            for (int i = 0; i < _deck.Length; i++) _deck[i].gameObject.SetActive(i < _loading.Loaded);
            float p = (float)_loading.Progress;
            bool active = _loading.Active && !reduce;
            Vector3 target = _ship.TransformPoint(_deck[_loading.Loaded % _deck.Length].localPosition);
            Vector3 highPickup = new Vector3(_pickup.x, _boomTop.y - 30f, _pickup.z);
            Vector3 highDeck = new Vector3(target.x, highPickup.y, target.z);
            Vector3 cargo = _stock;
            if (p < 0.25f) cargo = Vector3.Lerp(_stock, _pickup, Mathf.SmoothStep(0, 1, p / 0.25f)) + Vector3.up * (_workerHeight * 0.43f);
            else if (p < 0.3f) cargo = _pickup + Vector3.up * (_workerHeight * 0.43f * (1f - Mathf.InverseLerp(0.25f, 0.3f, p)));
            else if (p < 0.45f) cargo = Vector3.Lerp(_pickup, highPickup, Mathf.SmoothStep(0, 1, (p - 0.3f) / 0.15f));
            else if (p < 0.65f) cargo = Vector3.Lerp(highPickup, highDeck, Mathf.SmoothStep(0, 1, (p - 0.45f) / 0.2f));
            else cargo = Vector3.Lerp(highDeck, target, Mathf.SmoothStep(0, 1, (p - 0.65f) / 0.2f));
            _cargo.gameObject.SetActive(active);
            _cargo.position = cargo;
            Vector3 hook = highPickup;
            if (active)
            {
                if (p < 0.3f) hook = Vector3.Lerp(highPickup, _pickup + Vector3.up * (_crateSize * 0.8f), Mathf.SmoothStep(0, 1, p / 0.3f));
                else if (p < 0.85f) hook = cargo + Vector3.up * (_crateSize * 0.8f);
                else hook = Vector3.Lerp(target + Vector3.up * (_crateSize * 0.8f), highPickup, Mathf.SmoothStep(0, 1, (p - 0.85f) / 0.15f));
            }
            _hook.position = hook;
            _cable.SetPosition(0, new Vector3(hook.x, _boomTop.y, hook.z)); _cable.SetPosition(1, hook);
            if (_worker != null)
            {
                bool outward = active && p < 0.3f;
                float move = outward ? Mathf.SmoothStep(0, 1, p / 0.25f) : active ? 1f - Mathf.Clamp01((p - 0.3f) / 0.3f) : 0f;
                Vector3 direction = outward ? _pickup - _stock : _stock - _pickup;
                _worker.position = Vector3.Lerp(_stock, _pickup, move) - (_pickup - _stock).normalized * 17f;
                if (direction.sqrMagnitude > 0.01f) _worker.rotation = Quaternion.LookRotation(direction);
                _animation.SetMoving(active && (p < 0.25f || p >= 0.3f && p < 0.6f));
            }
        }

        private void OnDisable() { if (_horn != null) _horn.Stop(); }
        private void OnDestroy()
        {
            if (_ropeMaterial != null) Destroy(_ropeMaterial);
            if (_lampMaterial != null) Destroy(_lampMaterial);
            if (_steelMaterial != null) Destroy(_steelMaterial);
        }
    }
}
