using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Gameplay
{
    /// <summary>Two reusable coastal launches and a camera-focused sound bed, outside the cargo lane.</summary>
    public sealed class CoastalDiscovery : MonoBehaviour
    {
        [SerializeField] private Vector2 _laneOffset = new Vector2(-350f, 180f);
        [SerializeField] private Vector2 _laneRadius = new Vector2(80f, 95f);
        [SerializeField, Min(10f)] private float _lapSeconds = 65f;
        [SerializeField, Min(1f)] private float _nearViewSize = 450f;
        [SerializeField, Min(1f)] private float _farViewSize = 1800f;
        [SerializeField, Min(0.1f)] private float _focusSpeed = 3f;
        [SerializeField, Range(0f, 1f)] private float _surfVolume = 0.22f;
        [SerializeField, Range(0f, 1f)] private float _gullVolume = 0.18f;
        [SerializeField, Range(0f, 1f)] private float _engineVolume = 0.10f;
        [SerializeField] private AudioClip _surfClip, _gullClip, _engineClip;
        private readonly Transform[] _boats = new Transform[2];
        private readonly LineRenderer[] _wakes = new LineRenderer[4];
        private readonly Material[] _materials = new Material[3];
        private readonly AudioClip[] _generated = new AudioClip[3];
        private Transform _visuals;
        private Camera _camera;
        private CoalOperation _operation;
        private AccessibilityConfig _accessibility;
        private AudioService _audio;
        private AudioSource _surf, _gull, _engine;
        private Mesh _hull;
        private Vector3 _laneCentre, _focus;
        private float _clock, _gullWait = 4f;
        public float Nearness { get; private set; }
        public Vector3 ShorePoint { get; private set; }

        private void Awake()
        {
            _camera = Camera.main;
            _accessibility = ServiceLocator.Get<AccessibilityConfig>();
            _audio = ServiceLocator.Get<AudioService>();
        }

        public void Initialize(CoalOperation operation, Bounds quay, float waterY)
        {
            _operation = operation;
            ShorePoint = new Vector3(quay.min.x - 2f, waterY + 0.7f, quay.center.z);
            _focus = new Vector3(quay.min.x - 90f, quay.max.y, quay.center.z);
            _laneCentre = new Vector3(quay.min.x + _laneOffset.x, waterY, quay.center.z + _laneOffset.y);
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) { enabled = false; return; }
            Color[] colors = { new Color(0.17f, 0.38f, 0.5f), new Color(0.94f, 0.9f, 0.74f), new Color(0.93f, 0.98f, 1f) };
            for (int i = 0; i < _materials.Length; i++)
            {
                _materials[i] = new Material(shader) { name = "Kiyi detayi " + i };
                _materials[i].SetColor("_BaseColor", colors[i]);
            }
            _visuals = new GameObject("KiyiTekneleri").transform;
            _visuals.SetParent(transform, false);
            // Shared low-poly hull, nose +Z. No prefab copies, colliders or moving allocations.
            _hull = new Mesh { name = "Kiyi teknesi govdesi" };
            _hull.vertices = new[] { new Vector3(-13,0,-25),new Vector3(13,0,-25),new Vector3(13,0,14),new Vector3(0,0,30),new Vector3(-13,0,14),
                new Vector3(-17,9,-25),new Vector3(17,9,-25),new Vector3(17,9,14),new Vector3(0,9,34),new Vector3(-17,9,14) };
            _hull.triangles = new[] {5,9,8,5,8,7,5,7,6,0,5,6,0,6,1,1,6,7,1,7,2,2,7,8,2,8,3,3,8,9,3,9,4,4,9,5,4,5,0};
            _hull.RecalculateNormals(); _hull.RecalculateBounds();
            for (int i = 0; i < 2; i++)
            {
                Transform boat = new GameObject("KiyiTeknesi" + i).transform;
                boat.SetParent(_visuals, false); _boats[i] = boat;
                boat.gameObject.AddComponent<MeshFilter>().sharedMesh = _hull;
                Renderer r = boat.gameObject.AddComponent<MeshRenderer>(); r.sharedMaterial = _materials[0];
                r.shadowCastingMode = ShadowCastingMode.Off;
                Part(boat, "Kabin", new Vector3(0,16,-6), new Vector3(20,17,19), _materials[1]);
                Part(boat, "Cam", new Vector3(0,18,4), new Vector3(17,7,1), _materials[0]);
                Part(boat, "Tavan", new Vector3(0,25,-6), new Vector3(25,3,24), _materials[2]);
                for (int side = 0; side < 2; side++)
                {
                    var go = new GameObject("TekneSuIzi"); go.transform.SetParent(_visuals, false);
                    var wake = go.AddComponent<LineRenderer>(); wake.sharedMaterial = _materials[2];
                    wake.positionCount = 6; wake.useWorldSpace = true;
                    wake.startWidth = 2f; wake.endWidth = 0f; wake.shadowCastingMode = ShadowCastingMode.Off;
                    _wakes[i * 2 + side] = wake;
                }
            }
            _surf = Sound("IskeleDalgasi", _surfClip != null ? _surfClip : Generate(0), true);
            _gull = Sound("MartiSesi", _gullClip != null ? _gullClip : Generate(1), false);
            _engine = Sound("KiyiMotoru", _engineClip != null ? _engineClip : Generate(2), true);
        }

        private void Part(Transform boat, string name, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            go.transform.SetParent(boat, false); go.transform.localPosition = position; go.transform.localScale = scale;
            Destroy(go.GetComponent<Collider>());
            Renderer r = go.GetComponent<Renderer>(); r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.Off;
        }
        private AudioSource Sound(string name, AudioClip clip, bool loop)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>(); source.clip = clip; source.loop = loop;
            source.playOnAwake = false; source.spatialBlend = 0f; source.volume = 0f;
            // Camera focus handles distance for both orthographic zoom and perspective dolly.
            return source;
        }

        private AudioClip Generate(int kind)
        {
            const int rate = 22050;
            float seconds = kind == 1 ? 1.4f : 6f;
            var samples = new float[Mathf.RoundToInt(rate * seconds)];
            uint seed = 173u; float low = 0f, phase = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)rate;
                seed = seed * 1664525u + 1013904223u;
                float noise = ((seed >> 8) / 16777215f) * 2f - 1f;
                low += (noise - low) * 0.09f;
                if (kind == 0)
                    samples[i] = low * (0.3f + 0.7f * Mathf.Pow(Mathf.Sin(Mathf.PI * t / seconds), 2f));
                else if (kind == 1)
                {
                    float call = Mathf.Repeat(t, 0.7f) / 0.7f;
                    phase += 2f * Mathf.PI * (1400f - 650f * call + 65f * Mathf.Sin(t * 37f)) / rate;
                    float envelope = Mathf.Pow(Mathf.Sin(Mathf.PI * call), 2f);
                    samples[i] = (Mathf.Sin(phase) * 0.32f + Mathf.Sin(phase * 2f) * 0.07f + low * 0.1f) * envelope;
                }
                else samples[i] = (Mathf.Sin(t * Mathf.PI * 2f * 90f) * 0.3f + Mathf.Sin(t * Mathf.PI * 2f * 180f) * 0.1f)
                    * (0.85f + 0.15f * Mathf.Sin(t * Mathf.PI * 2f * 3f));
                samples[i] *= Mathf.Min(1f, t / 0.03f, (seconds - t) / 0.03f);
            }
            var clip = AudioClip.Create("KiyiOrtam" + kind, samples.Length, 1, rate, false);
            clip.SetData(samples, 0); _generated[kind] = clip;
            return clip;
        }

        private void Update()
        {
            if (_visuals == null) return;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            bool active = _operation != null && _operation.isActiveAndEnabled;
            bool reduce = _accessibility != null && _accessibility.ReduceMotion;
            float target = 0f;
            if (active && _camera != null)
            {
                Vector3 view = _camera.WorldToViewportPoint(_focus);
                float size = _camera.orthographic ? _camera.orthographicSize
                    : Mathf.Max(0f, view.z) * Mathf.Tan(_camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
                float framing = Mathf.Max(Mathf.Abs(view.x - 0.5f), Mathf.Abs(view.y - 0.5f));
                if (view.z > 0)
                    target = (1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(_nearViewSize, _farViewSize, size)))
                        * (1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.25f, 0.7f, framing)));
            }
            Nearness = Mathf.Lerp(Nearness, target, 1f - Mathf.Exp(-dt * _focusSpeed));
            bool show = active && !reduce;
            if (_visuals.gameObject.activeSelf != show) _visuals.gameObject.SetActive(show);
            if (show)
            {
                _clock += dt;
                for (int i = 0; i < 2; i++)
                {
                    float a = _clock * Mathf.PI * 2f / _lapSeconds + i * Mathf.PI;
                    Vector3 forward = new Vector3(-Mathf.Sin(a) * _laneRadius.x, 0, Mathf.Cos(a) * _laneRadius.y).normalized;
                    Vector3 p = _laneCentre + new Vector3(Mathf.Cos(a) * _laneRadius.x, 0, Mathf.Sin(a) * _laneRadius.y);
                    _boats[i].position = p + Vector3.up * (Mathf.Sin(_clock * 1.7f + i) * Mathf.Lerp(0.3f, 1.4f, Nearness));
                    _boats[i].rotation = Quaternion.LookRotation(forward) * Quaternion.Euler(0, 0, Mathf.Sin(_clock * 1.1f + i) * Nearness * 2f);
                    Vector3 right = Vector3.Cross(Vector3.up, forward);
                    for (int side = 0; side < 2; side++)
                    {
                        LineRenderer wake = _wakes[i * 2 + side];
                        wake.startWidth = Mathf.Lerp(1.4f, 3f, Nearness);
                        for (int j = 0; j < 6; j++)
                            wake.SetPosition(j, p - forward * (24f + j * 10f) + right * ((side == 0 ? -1 : 1) * (10f + j * 3f)) + Vector3.up * 0.8f);
                    }
                }
            }
            float mix = active && _audio != null ? _audio.SfxVolume * Nearness : 0f;
            Loop(_surf, mix * _surfVolume);
            Loop(_engine, show ? mix * _engineVolume : 0f);
            _gull.volume = mix * _gullVolume;
            _gull.panStereo = Mathf.Sin(_clock * 0.14f) * 0.25f;
            if (_gull.volume < 0.001f) { if (_gull.isPlaying) _gull.Stop(); }
            else
            {
                _gullWait -= dt;
                if (_gullWait <= 0f) { _gull.Play(); _gullWait = 9f + 3f * (1f + Mathf.Sin(_clock * 0.37f)); }
            }
        }

        private static void Loop(AudioSource source, float volume)
        {
            source.volume = volume;
            if (volume > 0.001f) { if (!source.isPlaying) source.Play(); }
            else if (source.isPlaying) source.Stop();
        }
        private void OnDisable()
        {
            Nearness = 0f;
            if (_visuals != null) _visuals.gameObject.SetActive(false);
            if (_surf != null) _surf.Stop();
            if (_gull != null) _gull.Stop();
            if (_engine != null) _engine.Stop();
        }
        private void OnDestroy()
        {
            if (_hull != null) Destroy(_hull);
            for (int i = 0; i < _materials.Length; i++) if (_materials[i] != null) Destroy(_materials[i]);
            for (int i = 0; i < _generated.Length; i++) if (_generated[i] != null) Destroy(_generated[i]);
        }
    }
}
