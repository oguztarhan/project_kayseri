using System.Collections;
using UnityEngine;

namespace Kayseri.Island
{
    /// <summary>
    /// Advances the island's art through its three build phases, one district at a time.
    ///
    /// Each station's own level range is split in thirds, so a station capped at 150 moves up
    /// at 50 and 100, and one capped at 100 moves up at 33 and 67. Applied PER DISTRICT rather
    /// than to the island as a whole: take the mine a third of the way and the mine yard
    /// rebuilds while the market is still a phase-1 stall. Each district takes its level from
    /// the station that owns it - the mine from MINE, the railway from TRAIN, and so on - so
    /// the island grows unevenly, following whatever the player actually spent on.
    ///
    /// A station's level is its total across every axis, measured against that station's own
    /// cap. Landscape that belongs to no single station (terrain, foliage, sites, props)
    /// follows the furthest-advanced station so unlocked ground appears on time.
    ///
    /// All three phase roots stay active; visibility is controlled on their district children.
    /// A district that changes phase pops in rather than snapping, so the rebuild is something
    /// the player sees happen.
    /// </summary>
    public sealed class IslandPhaseController : MonoBehaviour
    {
        [SerializeField] private GameObject[] _phaseRoots;
        [SerializeField] private Game.Gameplay.CoalOperation _operation;

        [Header("Phase change")]
        [SerializeField] private float _growSeconds = 0.55f;   // how long a district takes to pop in
        [SerializeField] private float _overshoot = 1.12f;     // peak scale before it settles back to 1

        [Header("Phase change burst")]
        [SerializeField] private int _burstCount = 44;
        [SerializeField] private float _burstSpeed = 16f;
        [SerializeField] private float _burstLife = 1.5f;
        [SerializeField] private float _burstSize = 2.6f;
        [SerializeField] private Color _burstColor = new Color(1f, 0.86f, 0.35f, 1f);

        /// <summary>Districts, and the station whose level advances each one.</summary>
        private static readonly string[] Districts =
        {
            "Mine", "Rail", "Depot", "Refinery", "Market", "Port",
            "Roads", "Sites", "Props", "Terrain", "Foliage",
        };

        /// <summary>Driver station per district; null = follow the furthest-advanced station.</summary>
        private static readonly string[] Drivers =
        {
            "MINE", "TRAIN", "STORAGE", "SMELTER", "MARKET", "MARKET",
            "ORE TRUCKS", null, null, null, null,
        };

        // The operation binds to the train and truck Transforms once, at startup, and lifts them
        // out of this group onto the island root. Swapping the group later would strand those
        // references on hidden objects, so the rig is pinned to phase 1 and never switched.
        private const string VehicleGroup = "Vehicles";

        private int[] _shown;        // phase currently displayed per district, 0 = not resolved yet
        private int _topPhase = 1;
        private bool _started;       // suppresses the pop on the first resolve at load

        /// <summary>Highest phase any district has reached.</summary>
        public int CurrentPhase => _topPhase;

        public int PhaseCount => _phaseRoots != null ? _phaseRoots.Length : 0;

        /// <summary>Raised when a district changes phase: district name, new phase.</summary>
        public event System.Action<string, int> PhaseChanged;

        private void Awake()
        {
            if (_operation == null) _operation = GetComponentInParent<Game.Gameplay.CoalOperation>();
            _shown = new int[Districts.Length];
            Refresh();
        }

        /// <summary>
        /// Recomputes every district's phase from the current station levels. Cheap enough to
        /// call straight from an upgrade - it only walks the district lists and flips actives.
        /// </summary>
        public void Refresh()
        {
            if (_phaseRoots == null || _phaseRoots.Length == 0)
            {
                Debug.LogWarning("[Island] No phase roots assigned.", this);
                return;
            }
            if (_shown == null) _shown = new int[Districts.Length];

            for (int i = 0; i < _phaseRoots.Length; i++)
                if (_phaseRoots[i] != null && !_phaseRoots[i].activeSelf)
                    _phaseRoots[i].SetActive(true);

            // Resolve every district's phase first, so the shared landscape can follow the
            // furthest-advanced one in the same pass.
            int count = Districts.Length;
            var wanted = new int[count];
            int top = 1;
            for (int d = 0; d < count; d++)
            {
                wanted[d] = Drivers[d] == null ? 1 : PhaseForStation(Drivers[d]);
                if (wanted[d] > top) top = wanted[d];
            }
            for (int d = 0; d < count; d++)
                if (Drivers[d] == null) wanted[d] = top;

            _topPhase = top;

            for (int d = 0; d < count; d++)
            {
                if (_shown[d] == wanted[d]) continue;

                bool changed = _shown[d] != 0 && _started;
                Show(Districts[d], wanted[d], changed);
                _shown[d] = wanted[d];

                if (changed)
                {
                    if (PhaseChanged != null) PhaseChanged(Districts[d], wanted[d]);
                    Debug.Log("[Island] " + Districts[d] + " rebuilt to phase " + wanted[d]);
                }
            }

            Show(VehicleGroup, 1, false);
            _started = true;
        }

        /// <summary>
        /// Enables the named district under one phase root and disables it in the rest.
        /// When <paramref name="animate"/>, the incoming district grows into place.
        /// </summary>
        private void Show(string district, int phase, bool animate)
        {
            for (int i = 0; i < _phaseRoots.Length; i++)
            {
                var root = _phaseRoots[i];
                if (root == null) continue;

                Transform t = root.transform.Find(district);
                if (t == null) continue;   // that phase does not build this district

                bool on = (i == phase - 1);
                if (t.gameObject.activeSelf != on) t.gameObject.SetActive(on);

                if (on && animate && isActiveAndEnabled)
                {
                    StopCoroutine("Grow");
                    StartCoroutine(Grow(t));
                    Burst(t);
                }
            }
        }

        /// <summary>
        /// One-shot sparkle over the rebuilt district. Built in code and destroyed with itself
        /// so the island needs no authored VFX prefab wired up, and sized from the district's
        /// own renderer bounds so a small yard and a whole terrace both read correctly.
        /// </summary>
        private void Burst(Transform district)
        {
            if (district == null || _burstCount <= 0) return;

            Bounds b = new Bounds(district.position, Vector3.one * 8f);
            var rends = district.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            }
            float radius = Mathf.Clamp(Mathf.Max(b.size.x, b.size.z) * 0.5f, 4f, 60f);

            var go = new GameObject("OpPhaseBurst");
            go.transform.SetParent(district, false);
            go.transform.position = new Vector3(b.center.x, b.max.y + 1f, b.center.z);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 1f;
            main.loop = false;
            main.startLifetime = _burstLife;
            main.startSpeed = _burstSpeed;
            main.startSize = _burstSize;
            main.startColor = _burstColor;
            main.gravityModifier = 0.55f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            main.maxParticles = Mathf.Max(_burstCount * 2, 64);

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)_burstCount) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = radius;

            // Fade out rather than vanishing mid-air.
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.55f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.15f));

            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Billboard;
            // Built-in sprite shader so this works without an authored particle material.
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Sprites/Default");
            if (shader != null) rend.material = new Material(shader) { color = _burstColor };

            ps.Play();
            Destroy(go, _burstLife + 1.2f);
        }

        /// <summary>Scale pop: the district swells past full size, then settles.</summary>
        private IEnumerator Grow(Transform t)
        {
            if (t == null) yield break;

            Vector3 full = Vector3.one;
            float elapsed = 0f;
            while (elapsed < _growSeconds)
            {
                elapsed += Time.deltaTime;
                float k = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, _growSeconds));
                // ease out, then overshoot and settle
                float s = k < 0.6f
                    ? Mathf.Lerp(0.55f, _overshoot, k / 0.6f)
                    : Mathf.Lerp(_overshoot, 1f, (k - 0.6f) / 0.4f);
                if (t == null) yield break;
                t.localScale = full * s;
                yield return null;
            }
            if (t != null) t.localScale = full;
        }

        /// <summary>
        /// The station's phase: its total level across every axis, against its own cap, in
        /// thirds. A 150-cap station steps at 50 and 100; a 100-cap one at 33 and 67.
        /// </summary>
        private int PhaseForStation(string stationName)
        {
            if (_operation == null) return 1;

            for (int s = 0; s < _operation.StationCount; s++)
            {
                if (_operation.StationName(s) != stationName) continue;

                int cap = _operation.StationLevelCap(s);
                if (cap <= 0) return 1;

                int level = _operation.StationLevelTotal(s);
                float third = cap / 3f;
                if (level < third) return 1;
                if (level < third * 2f) return 2;
                return 3;
            }
            return 1;
        }
    }
}
