using System;
using System.Collections.Generic;
using Game.Core;
using Game.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Gameplay
{
    /// <summary>Small crews and pooled exhaust for the single-mesh industrial island layout.</summary>
    public sealed class IndustrialSiteActivity : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float _workerHeight = 46f;
        [SerializeField, Min(1f)] private float _walkSpeed = 24f;
        [SerializeField, Min(0.1f)] private float _pauseSeconds = 2.5f;
        [SerializeField, Min(1f)] private float _smokeWidth = 42f;
        [SerializeField] private Color _smokeTint = new Color(0.83f, 0.86f, 0.87f, 1f);
        [SerializeField] private Color _dayExhaust = new Color(0.045f, 0.05f, 0.06f, 1f);
        [SerializeField] private Color _nightExhaust = new Color(0.38f, 0.40f, 0.44f, 1f);
        [SerializeField, Range(1f, 2f)] private float _exhaustWidth = 1.2f;
        [SerializeField, Min(0.5f)] private float _smokeSeconds = 5.5f;
        [SerializeField, Min(0f)] private float _smokeRise = 18f;
        [SerializeField] private Vector3 _wind = new Vector3(5f, 0f, 2f);
        private const int PuffsPerVent = 3, MaxVents = 10;

        private sealed class Vent
        {
            public Renderer Source;
            public Vector3 Origin;
            public float Size, Rise;
            public Transform[] Puffs;
        }

        private sealed class Hand
        {
            public Renderer Site;
            public Transform Body;
            public PersonAnimator Animation;
            public Vector3 A, B;
            public float Distance, Offset;
        }

        private readonly List<Vent> _vents = new List<Vent>(MaxVents);
        private readonly List<Hand> _hands = new List<Hand>(7);
        private CoalOperation _operation;
        private AccessibilityConfig _accessibility;
        private Transform _visuals;
        private float _clock, _puffScale;
        private Material _smokeMaterial;
        private Material _steamMaterial;
        private float _appliedNight = -1f;
        private static readonly int NightId = Shader.PropertyToID("_KayseriNight");

        private void Awake() => _accessibility = ServiceLocator.Get<AccessibilityConfig>();

        public bool Initialize(CoalOperation operation, Transform island, GameObject[] people,
            GameObject puff, Material smoke)
        {
            Renderer[] art = island.GetComponentsInChildren<Renderer>(true);
            Renderer mine = Find(art, "Mine | pitch black entrance");
            Renderer factory = Find(art, "Factory | main blue tower");
            // Older phased islands retain their existing SiteLife/StationCrew implementation.
            if (mine == null || factory == null) { enabled = false; return false; }
            _operation = operation;
            _visuals = new GameObject("DumanVeEkipler").transform;
            _visuals.SetParent(transform, false);
            if (puff != null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader != null)
                {
                    _smokeMaterial = new Material(shader) { name = "Tesis hafif duman" };
                    _smokeMaterial.SetColor("_BaseColor", _dayExhaust);
                    _steamMaterial = new Material(shader) { name = "Tesis havalandirma" };
                    _steamMaterial.SetColor("_BaseColor", _smokeTint);
                    smoke = _smokeMaterial;
                }
                Bounds bounds = BoundsOf(puff);
                _puffScale = _smokeWidth / Mathf.Max(0.001f, bounds.size.x, bounds.size.y, bounds.size.z);
                for (int i = 0; i < art.Length; i++)
                {
                    string n = art[i].name;
                    if (n.StartsWith("Smelter | dark chimney throat", StringComparison.Ordinal)
                        || n == "Factory | chimney interior"
                        || n == "Factory | rooftop exhaust fan"
                        || n.StartsWith("Refinery | open top", StringComparison.Ordinal)
                        || n.StartsWith("Warehouse | vent cap", StringComparison.Ordinal))
                    {
                        Bounds b = art[i].bounds;
                        bool exhaust = n.StartsWith("Smelter", StringComparison.Ordinal)
                            || n.StartsWith("Refinery", StringComparison.Ordinal) || n == "Factory | chimney interior";
                        AddVent(art[i], new Vector3(b.center.x, b.max.y + 2f, b.center.z),
                            exhaust ? _exhaustWidth : 0.65f, 1f, puff,
                            exhaust ? smoke : _steamMaterial, exhaust ? 4 : PuffsPerVent);
                    }
                }
                Bounds m = mine.bounds;
                AddVent(mine, new Vector3(m.center.x, m.min.y + 18f, m.min.z - 8f), 0.6f, 0.25f, puff, _steamMaterial);
            }
            if (people != null && people.Length > 0 && people[0] != null)
            {
                Bounds m = mine.bounds;
                Vector3 door = new Vector3(m.center.x, m.min.y + 2f, m.min.z);
                AddHand(mine, door + new Vector3(-55, 0, -45), door + new Vector3(-55, 0, -105), people);
                AddHand(mine, door + new Vector3(20, 0, -55), door + new Vector3(100, 0, -65), people);
                AddHand(mine, door + new Vector3(-120, 0, -85), door + new Vector3(-120, 0, -145), people);
                AddFront(Find(art, "Factory | roller door"), 45f, 60f, people);
                AddFront(Find(art, "Warehouse | loading door"), 55f, -60f, people);
                AddFront(Find(art, "Smelter | main furnace"), 40f, 80f, people);
                AddFront(Find(art, "Refinery | equipment slab"), 30f, -90f, people);
            }
            return true;
        }

        private static Renderer Find(Renderer[] art, string name)
        {
            for (int i = 0; i < art.Length; i++) if (art[i].name == name) return art[i];
            return null;
        }

        private static Bounds BoundsOf(GameObject prefab)
        {
            Renderer[] rs = prefab.GetComponentsInChildren<Renderer>(true);
            Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds(Vector3.zero, Vector3.one);
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        private void AddVent(Renderer source, Vector3 point, float size, float rise, GameObject puff, Material material,
            int count = PuffsPerVent)
        {
            if (_vents.Count >= MaxVents) return;
            var vent = new Vent { Source = source, Origin = source.transform.InverseTransformPoint(point),
                Size = size, Rise = rise, Puffs = new Transform[count] };
            for (int i = 0; i < count; i++)
            {
                var go = Instantiate(puff, _visuals);
                go.name = "TesisDumani_" + _vents.Count + "_" + i;
                foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (material != null) r.sharedMaterial = material;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }
                foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
                vent.Puffs[i] = go.transform;
                go.SetActive(false);
            }
            _vents.Add(vent);
        }

        private void AddFront(Renderer site, float clearance, float sideways, GameObject[] people)
        {
            if (site == null) return;
            Bounds b = site.bounds;
            Vector3 a = new Vector3(b.center.x, b.min.y + 2f, b.min.z - clearance);
            AddHand(site, a, a + Vector3.right * sideways, people);
        }

        private void AddHand(Renderer site, Vector3 a, Vector3 b, GameObject[] people)
        {
            GameObject prefab = people[_hands.Count % people.Length] ?? people[0];
            Bounds bounds = BoundsOf(prefab);
            Transform body = new GameObject("TesisIscisi_" + _hands.Count).transform;
            body.SetParent(_visuals, false);
            Transform model = Instantiate(prefab, body).transform;
            float scale = _workerHeight / Mathf.Max(0.001f, bounds.size.y);
            model.localScale = Vector3.one * scale;
            model.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) * scale;
            model.localRotation = Quaternion.identity;
            foreach (Collider c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);
            var hand = new Hand { Site = site, Body = body, Animation = new PersonAnimator(model),
                A = site.transform.InverseTransformPoint(a), B = site.transform.InverseTransformPoint(b),
                Distance = Vector3.Distance(a, b), Offset = _hands.Count * 1.73f };
            body.position = a;
            _hands.Add(hand);
        }

        private void Update()
        {
            if (_visuals == null) return;
            bool visible = _operation != null && _operation.isActiveAndEnabled
                && (_accessibility == null || !_accessibility.ReduceMotion);
            if (_visuals.gameObject.activeSelf != visible) _visuals.gameObject.SetActive(visible);
            if (!visible) return;
            float night = Mathf.Clamp01(Shader.GetGlobalFloat(NightId));
            if (_smokeMaterial != null && Mathf.Abs(night - _appliedNight) > 0.002f)
            {
                _appliedNight = night;
                _smokeMaterial.SetColor("_BaseColor", Color.Lerp(_dayExhaust, _nightExhaust, night));
            }
            _clock += Mathf.Min(Time.deltaTime, 0.1f);
            for (int v = 0; v < _vents.Count; v++)
            {
                Vent vent = _vents[v];
                bool on = vent.Source != null && vent.Source.enabled && vent.Source.gameObject.activeInHierarchy;
                for (int i = 0; i < vent.Puffs.Length; i++)
                {
                    Transform puff = vent.Puffs[i];
                    if (puff.gameObject.activeSelf != on) puff.gameObject.SetActive(on);
                    if (!on) continue;
                    float age = Mathf.Repeat(_clock + i * _smokeSeconds / vent.Puffs.Length + v * 0.73f, _smokeSeconds);
                    float k = age / _smokeSeconds;
                    puff.position = vent.Source.transform.TransformPoint(vent.Origin)
                        + Vector3.up * (_smokeRise * vent.Rise * age)
                        + _wind * (age * (0.3f + k))
                        + Vector3.right * (Mathf.Sin(age * 1.4f + v) * 3f * k);
                    float envelope = Mathf.SmoothStep(0f, 1f, k * 6f)
                        * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 1f, k)));
                    puff.localScale = Vector3.one * (_puffScale * vent.Size * (0.45f + k) * envelope);
                }
            }
            for (int i = 0; i < _hands.Count; i++)
            {
                Hand h = _hands[i];
                bool on = h.Site != null && h.Site.enabled && h.Site.gameObject.activeInHierarchy;
                if (h.Body.gameObject.activeSelf != on) h.Body.gameObject.SetActive(on);
                if (!on) continue;
                float walk = h.Distance / _walkSpeed, half = walk + _pauseSeconds;
                float phase = Mathf.Repeat(_clock + h.Offset, half * 2f);
                bool returning = phase >= half;
                float leg = returning ? phase - half : phase;
                float progress = Mathf.Clamp01(leg / walk);
                if (returning) progress = 1f - progress;
                Vector3 a = h.Site.transform.TransformPoint(h.A), b = h.Site.transform.TransformPoint(h.B);
                h.Body.position = Vector3.Lerp(a, b, progress);
                Vector3 direction = (returning ? a - b : b - a);
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.01f) h.Body.rotation = Quaternion.LookRotation(direction);
                h.Animation.SetMoving(leg < walk);
                h.Animation.SetSpeed(leg < walk ? _walkSpeed / (_workerHeight * 0.7f) : 1f);
            }
        }

        private void OnDisable()
        {
            if (_visuals != null) _visuals.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_smokeMaterial != null) Destroy(_smokeMaterial);
            if (_steamMaterial != null) Destroy(_steamMaterial);
        }
    }
}
