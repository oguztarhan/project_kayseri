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
        [Tooltip("Faz köklerinin PREFAB varlıkları — istasyon ekranı modeli buradan kopyalar. Boş bırakılırsa " +
                 "sahnedeki kopya kullanılır ve model kıpırdayamaz (aşağıdaki nota bak).")]
        [SerializeField] private GameObject[] _phasePrefabs;
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
            "Power", "Haul", "Fleet", "Civic",
            "Roads", "Sites", "Props", "Terrain", "Foliage", "Theme",
        };

        /// <summary>Driver station per district; null = follow the furthest-advanced station.</summary>
        private static readonly string[] Drivers =
        {
            // Port used to advance on MARKET, so the quay and the market always
            // upgraded together and the export chain had no district of its own.
            // It rides CARGO TRUCKS now - the station that actually feeds it.
            //
            // Haul comes before Roads so DistrictArt("ORE TRUCKS") hands the
            // station screen the haul yard rather than the road network, which
            // means nothing on a turntable. Civic has no station: it follows the
            // furthest-advanced one, so the town grows with the island.
            // Theme is the island's signature dressing (iron's blast furnace and outcrops). It is
            // authored ACTIVE under every phase root, so before it was a district here all three
            // copies drew stacked on top of each other from the first Refresh.
            "MINE", "TRAIN", "STORAGE", "SMELTER", "MARKET", "CARGO TRUCKS",
            "POWER PLANT", "ORE TRUCKS", "CARGO TRUCKS", null,
            "ORE TRUCKS", null, null, null, null, null,
        };

        // The operation binds to the train and truck Transforms once, at startup, and lifts them
        // out of this group onto the island root. Swapping the group later would strand those
        // references on hidden objects, so the rig is pinned to phase 1 and never switched.
        private const string VehicleGroup = "Vehicles";

        private int[] _shown;        // phase currently displayed per district, 0 = not resolved yet
        private int _topPhase = 1;
        private bool _started;       // suppresses the pop on the first resolve at load
        private Material _burstMat;  // one shared burst material; see Burst for why it is cached

        /// <summary>Highest phase any district has reached.</summary>
        public int CurrentPhase => _topPhase;

        public int PhaseCount => _phaseRoots != null ? _phaseRoots.Length : 0;

        /// <summary>Raised when a district changes phase: district name, new phase.</summary>
        public event System.Action<string, int> PhaseChanged;

        /// <summary>
        /// Raised once after every district affected by one refresh has been switched. Global systems
        /// that scan the whole island must listen here rather than to <see cref="PhaseChanged"/>:
        /// one station threshold can also advance all shared landscape districts, and rebuilding the
        /// same lights, lamps or ambience for every one of them causes a visible frame stall.
        /// </summary>
        public event System.Action PhaseRefreshCompleted;

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

            bool anyChanged = false;
            for (int d = 0; d < count; d++)
            {
                if (_shown[d] == wanted[d]) continue;

                bool changed = _shown[d] != 0 && _started;
                // Shared landscape follows the leading station and several of those districts can
                // switch together. Bursting Terrain/Foliage/Theme scans enormous renderer trees and
                // creates several particle systems in the purchase frame. Only station-owned art is
                // the rebuild the player bought, so only that art gets the celebration.
                Show(Districts[d], wanted[d], changed && Drivers[d] != null);
                _shown[d] = wanted[d];

                if (changed)
                {
                    anyChanged = true;
                    if (PhaseChanged != null) PhaseChanged(Districts[d], wanted[d]);
                    Debug.Log("[Island] " + Districts[d] + " rebuilt to phase " + wanted[d]);
                }
            }

            Show(VehicleGroup, 1, false);
            _started = true;
            if (anyChanged && PhaseRefreshCompleted != null) PhaseRefreshCompleted();
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
            if (_burstMat == null)
            {
                // Built-in sprite shader so this works without an authored particle material. Built
                // once and reused: a Shader.Find plus a fresh Material on every rebuild was a hitch
                // inside the upgrade tap, and nothing ever destroyed the previous copy.
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                             ?? Shader.Find("Sprites/Default");
                if (shader != null) _burstMat = new Material(shader) { color = _burstColor };
            }
            if (_burstMat != null) rend.sharedMaterial = _burstMat;

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
        /// The scene object holding one station's district art at a given phase. All three variants sit
        /// in the scene at once with only one active, so this can hand out the phase the player is
        /// leaving and the one they just bought at the same moment. Null when the station drives no
        /// district, or when that phase does not build it.
        /// </summary>
        public Transform DistrictArt(string stationName, int phase)
        {
            return District(_phaseRoots, stationName, phase);
        }

        /// <summary>
        /// The district the island is showing right now, asked for by district name rather than by the
        /// station that drives it — <see cref="IslandAmbience"/> follows a place ("Refinery", "Port"),
        /// not an upgrade, and it does not know which phase is live. <see cref="Show"/> keeps exactly
        /// one variant of each district enabled, so the live one is whichever answers. Null when no
        /// phase builds that district at all.
        /// </summary>
        public Transform ActiveDistrict(string district)
        {
            if (_phaseRoots == null || string.IsNullOrEmpty(district)) return null;
            for (int i = 0; i < _phaseRoots.Length; i++)
            {
                if (_phaseRoots[i] == null) continue;
                Transform t = _phaseRoots[i].transform.Find(district);
                if (t != null && t.gameObject.activeInHierarchy) return t;
            }
            return null;
        }

        /// <summary>
        /// The same district, but taken from the PREFAB ASSET — what the station screen clones onto its
        /// turntable.
        ///
        /// It cannot clone the scene copy. The island's art is marked Batching Static, so at load Unity
        /// welds every district into one combined mesh whose vertices are already in world space; a
        /// renderer in that batch ignores its own transform completely, and a clone of it inherits the
        /// batch and stands immovably out on the island no matter where it is parented. (The same fact
        /// is why <see cref="Grow"/> below has never actually been visible.) The prefab asset was never
        /// batched, so a clone of it is ordinary geometry that moves when it is told to.
        ///
        /// Falls back to the scene object when the prefabs are not wired, which draws the right building
        /// and simply cannot animate it.
        /// </summary>
        public Transform DistrictModel(string stationName, int phase)
        {
            Transform t = District(_phasePrefabs, stationName, phase);
            return t != null ? t : District(_phaseRoots, stationName, phase);
        }

        private static Transform District(GameObject[] roots, string stationName, int phase)
        {
            if (roots == null || phase < 1 || phase > roots.Length) return null;
            GameObject root = roots[phase - 1];
            if (root == null) return null;
            for (int d = 0; d < Districts.Length; d++)
                if (Drivers[d] == stationName) return root.transform.Find(Districts[d]);
            return null;
        }

        /// <summary>
        /// The station's phase: its total level across every axis, against its own cap, in
        /// thirds. A 150-cap station steps at 50 and 100; a 100-cap one at 33 and 67.
        ///
        /// Public because the station screen draws the bar toward the next rebuild from it, and a
        /// second copy of the rule in the UI is a second place for it to drift.
        /// </summary>
        public int PhaseForStation(string stationName)
        {
            if (_operation == null) return 1;

            for (int s = 0; s < _operation.StationCount; s++)
                if (_operation.StationName(s) == stationName)
                    return _operation.PhaseForStation(s);
            return 1;
        }
    }
}
