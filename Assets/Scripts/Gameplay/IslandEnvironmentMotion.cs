using Game.Core;
using Game.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Gameplay
{
    /// <summary>Small harbour details in one reusable mesh. No colliders, economy or spawned particles.</summary>
    public sealed class IslandEnvironmentMotion : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float _wingSpan = 22f;
        [SerializeField, Min(1f)] private float _flightHeight = 55f;
        [SerializeField, Min(10f)] private float _flightRadius = 65f;
        [SerializeField, Min(5f)] private float _orbitSeconds = 32f;
        [SerializeField, Min(1f)] private float _waveLength = 26f;
        [SerializeField, Min(0.1f)] private float _waveWidth = 3f;
        [SerializeField, Min(1f)] private float _waveSeconds = 7f;
        [SerializeField, Min(0f)] private float _waterOffset = 0.5f;
        [SerializeField] private Vector3 _wind = new Vector3(1.1f, 0f, 0.45f);
        [SerializeField] private Color _tint = new Color(0.9f, 0.97f, 0.97f, 1f);

        private const int Birds = 3, Waves = 6, Segments = 8, BirdVertices = Birds * 9;
        private readonly Vector3[] _vertices = new Vector3[BirdVertices + Waves * Segments * 4];
        private CoalOperation _operation;
        private AccessibilityConfig _accessibility;
        private Mesh _mesh;
        private Material _material;
        private MeshRenderer _renderer;
        private Transform _waterMarker;
        private Vector3 _waterMarkerCentre;
        private float _clock;
        private CoastalDiscovery _coast;

        public Vector3 WindVelocity => _accessibility != null && _accessibility.ReduceMotion
            ? Vector3.zero : _wind * (0.8f + 0.2f * Mathf.Sin(_clock * 0.45f));

        private void Awake() => _accessibility = ServiceLocator.Get<AccessibilityConfig>();

        public void Initialize(CoalOperation operation)
        {
            _operation = operation;
            if (_mesh != null) return;
            // The industrial map's shipOut point is on the quay. Its authored sea ripples,
            // unlike that gameplay anchor, identify the actual water surface.
            if (operation.OurShipBerth(out Vector3 berth, out _, out Transform island))
            {
                float nearest = float.MaxValue;
                Renderer[] art = island.GetComponentsInChildren<Renderer>(true);
                Renderer quay = null, sea = null;
                for (int i = 0; i < art.Length; i++)
                {
                    if (art[i].name == "Port | concrete quay") quay = art[i];
                    if (art[i].name == "Endless turquoise sea") sea = art[i];
                    if (!art[i].name.StartsWith("Sea | little ripple", System.StringComparison.Ordinal)) continue;
                    float distance = (art[i].bounds.center - berth).sqrMagnitude;
                    if (distance >= nearest) continue;
                    nearest = distance;
                    _waterMarker = art[i].transform;
                    _waterMarkerCentre = _waterMarker.InverseTransformPoint(art[i].bounds.center);
                }
                if (quay != null && sea != null)
                {
                    _coast = gameObject.AddComponent<CoastalDiscovery>();
                    _coast.Initialize(operation, quay.bounds, sea.bounds.max.y);
                }
            }
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) { enabled = false; return; }
            _material = new Material(shader) { name = "Island environment" };
            _material.SetColor("_BaseColor", _tint);
            _material.SetFloat("_Cull", (float)CullMode.Off);
            _mesh = new Mesh { name = "Harbour birds and ripples" };
            _mesh.MarkDynamic();
            int[] indices = new int[BirdVertices + Waves * Segments * 6];
            for (int i = 0; i < BirdVertices; i++) indices[i] = i;
            for (int i = 0; i < Waves * Segments; i++)
            {
                int v = BirdVertices + i * 4, k = BirdVertices + i * 6;
                indices[k] = v; indices[k + 1] = v + 1; indices[k + 2] = v + 2;
                indices[k + 3] = v; indices[k + 4] = v + 2; indices[k + 5] = v + 3;
            }
            _mesh.vertices = _vertices;
            _mesh.triangles = indices;
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _renderer.enabled = false;
        }

        private void Update()
        {
            if (_renderer == null) return;
            bool visible = _operation != null && _operation.isActiveAndEnabled
                && (_accessibility == null || !_accessibility.ReduceMotion);
            Vector3 berth = default, heading = default;
            Transform parent;
            visible = visible && _operation.OurShipBerth(out berth, out heading, out parent);
            if (_renderer.enabled != visible) _renderer.enabled = visible;
            if (!visible) return;
            _clock += Mathf.Min(Time.deltaTime, 0.1f);
            Vector3 water = berth - heading * (_flightRadius + 20f);
            if (_waterMarker != null)
            {
                water = _waterMarker.TransformPoint(_waterMarkerCentre);
                heading = berth - water;
                heading.y = 0f;
                heading.Normalize();
                water -= heading * (_flightRadius * 0.2f);
            }
            Vector3 right = Vector3.Cross(Vector3.up, heading).normalized;
            float near = _coast != null ? _coast.Nearness : 0f;
            if (_coast != null)
            {
                heading = Vector3.right;
                right = Vector3.back;
                water = _coast.ShorePoint - heading * 110f;
            }
            Matrix4x4 local = transform.worldToLocalMatrix;
            for (int bird = 0; bird < Birds; bird++)
            {
                float a = _clock * (Mathf.PI * 2f / _orbitSeconds) + bird * 2.1f;
                Vector3 forward = (right * -Mathf.Sin(a) + heading * Mathf.Cos(a) * 0.6f).normalized;
                Vector3 wing = Vector3.Cross(Vector3.up, forward);
                Vector3 centre = water + right * (Mathf.Cos(a) * _flightRadius)
                    + heading * (Mathf.Sin(a) * _flightRadius * 0.6f)
                    + Vector3.up * ((_coast != null ? _flightHeight * 3.3f : _flightHeight) + bird * 14f + Mathf.Sin(a * 2f) * (4f + near * 5f));
                float flap = Mathf.Sin(_clock * 6f + bird) * Mathf.Max(0f, Mathf.Sin(_clock * 0.7f + bird)) * (0.7f + near * 0.6f);
                float span = _wingSpan * (1f - bird * 0.1f);
                int v = bird * 9;
                _vertices[v] = local.MultiplyPoint3x4(centre + forward * span * 0.18f);
                _vertices[v + 1] = local.MultiplyPoint3x4(centre - forward * span * 0.15f);
                _vertices[v + 2] = local.MultiplyPoint3x4(centre - wing * span * 0.5f + Vector3.up * (flap * span * 0.2f));
                _vertices[v + 3] = _vertices[v];
                _vertices[v + 4] = _vertices[v + 1];
                _vertices[v + 5] = local.MultiplyPoint3x4(centre + wing * span * 0.5f + Vector3.up * (flap * span * 0.2f));
                _vertices[v + 6] = local.MultiplyPoint3x4(centre + forward * span * 0.3f);
                _vertices[v + 7] = local.MultiplyPoint3x4(centre - wing * span * 0.08f - forward * span * 0.25f);
                _vertices[v + 8] = local.MultiplyPoint3x4(centre + wing * span * 0.08f - forward * span * 0.25f);
            }
            for (int wave = 0; wave < Waves; wave++)
            {
                float u = Mathf.Repeat(_clock / _waveSeconds + wave * 0.167f, 1f);
                float envelope = Mathf.Sin(u * Mathf.PI);
                Vector3 centre = water + right * ((wave % 3 - 1) * 35f)
                    + heading * ((wave / 3 - 0.5f) * 35f + u * 12f) + Vector3.up * _waterOffset;
                if (_coast != null)
                    centre = _coast.ShorePoint + right * ((wave % 3 - 1) * 48f)
                        - heading * ((1f - u) * 48f + 3f)
                        + Vector3.up * (_waterOffset + Mathf.Sin(Mathf.InverseLerp(0.7f, 1f, u) * Mathf.PI) * (2f + near * 5f));
                for (int segment = 0; segment < Segments; segment++)
                {
                    float a = (float)segment / Segments - 0.5f, b = (float)(segment + 1) / Segments - 0.5f;
                    Vector3 p = centre + right * (a * _waveLength * envelope) + heading * (a * a * 5f);
                    Vector3 q = centre + right * (b * _waveLength * envelope) + heading * (b * b * 5f);
                    Vector3 width = heading * (_waveWidth * envelope * (1f + near * 0.7f));
                    int v = BirdVertices + (wave * Segments + segment) * 4;
                    _vertices[v] = local.MultiplyPoint3x4(p);
                    _vertices[v + 1] = local.MultiplyPoint3x4(q);
                    _vertices[v + 2] = local.MultiplyPoint3x4(q + width);
                    _vertices[v + 3] = local.MultiplyPoint3x4(p + width);
                }
            }
            _mesh.vertices = _vertices;
            _mesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_material != null) Destroy(_material);
        }
    }
}
