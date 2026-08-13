using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Lights the island's buildings, so that after dark the player can still see where they are.
    ///
    /// Street lamps light the ROADS — that is the whole point of where they stand — and the result
    /// is a lit road network with black holes at every place the player actually needs to tap. The
    /// buildings carry no light of their own: the map art gives them lit windows, but on Gold only
    /// 13 of the 244 window markers belong to the island and phase on show, and a lit window is a
    /// bright pane, not a lit building.
    ///
    /// The buildings are not guessed at here. <see cref="CoalOperation"/> already knows which of its
    /// stations own a structure — the mine, the depot, the refinery, the market and the power plant,
    /// as against the train and the two truck fleets, which own no building at all — and it can
    /// measure each one's district off its renderers. This stands a light over the middle of each,
    /// high enough to wash the whole footprint.
    ///
    /// Nothing is drawn here. The light is a marker carrying a material named <c>buildinglight</c>
    /// on a switched-off renderer, which is the same contract the map art uses for its own night
    /// lights, so <see cref="IslandGlow"/> picks these up through exactly the same path.
    /// </summary>
    public sealed class BuildingLights : MonoBehaviour
    {
        private const string GlowMaterial = "buildinglight";

        [Tooltip("Işığın binanın tepesinden ne kadar yukarıda duracağı.")]
        [SerializeField] private float _height = 16f;
        [Tooltip("Hangi operasyonun canlı olduğuna bu sıklıkta bakılır.")]
        [SerializeField] private float _rebindSeconds = 0.5f;

        private CoalOperation _operation;
        private Kayseri.Island.IslandPhaseController _phases;
        private Transform _root;
        private Mesh _marker;
        private Material _material;
        private float _rebindIn;
        private bool _built;

        /// <summary>
        /// Travelling to another island enables a different <see cref="CoalOperation"/> and disables
        /// this one, so the binding is re-checked on a slow timer rather than taken once — the same
        /// reason <c>UpgradeReadyMarkers</c> does it. A new operation means new buildings.
        ///
        /// The phase controller is found separately, because there is no path from an operation to
        /// one: all eight operations are components on a single object, while the controllers sit on
        /// the island roots. Exactly one of those roots is active, so the active controller is the
        /// live island's. Its PhaseChanged matters here as much as the island does — a phase rebuild
        /// puts a district's buildings up bigger and somewhere else in the plot, and a light that is
        /// not found again stays hanging over where the old shed was.
        /// </summary>
        private void Update()
        {
            _rebindIn -= Time.unscaledDeltaTime;
            if (_rebindIn > 0f) return;
            _rebindIn = Mathf.Max(0.1f, _rebindSeconds);

            CoalOperation live = null;
            foreach (var candidate in FindObjectsByType<CoalOperation>(FindObjectsInactive.Exclude))
                if (candidate.enabled) { live = candidate; break; }

            Kayseri.Island.IslandPhaseController phases = null;
            foreach (var controller in FindObjectsByType<Kayseri.Island.IslandPhaseController>(FindObjectsInactive.Exclude))
            { phases = controller; break; }

            if (live == _operation && phases == _phases && _built) return;
            _operation = live;

            if (phases != _phases)
            {
                if (_phases != null) _phases.PhaseChanged -= OnPhaseChanged;
                _phases = phases;
                if (_phases != null) _phases.PhaseChanged += OnPhaseChanged;
            }

            _built = Rebuild();
        }

        private void OnPhaseChanged(string district, int phase) => _built = Rebuild();

        private void OnDestroy()
        {
            if (_phases != null) _phases.PhaseChanged -= OnPhaseChanged;
            if (_marker != null) Destroy(_marker);
            if (_material != null) Destroy(_material);
        }

        /// <summary>True once lights are actually standing. The operation cannot answer where its
        /// buildings are until the island's districts have resolved, which is a few frames after
        /// travelling and after any phase change.</summary>
        private bool Rebuild()
        {
            if (_operation == null) return false;

            if (_root != null) Destroy(_root.gameObject);
            _root = new GameObject("BuildingLights").transform;
            _root.SetParent(transform, false);

            int raised = 0;
            for (int station = 0; station < _operation.StationCount; station++)
            {
                if (!_operation.StationHasBody(station)) continue;
                if (!Centre(station, out Vector3 point)) continue;
                Raise(point);
                raised++;
            }

            var glow = FindAnyObjectByType<IslandGlow>();
            if (glow != null) glow.Refresh();

            return raised > 0;
        }

        /// <summary>
        /// Where to hang a building's light: over the biggest STRUCTURE in the district, found by
        /// looking at what is actually standing there.
        ///
        /// Neither of the two points the operation can hand over is the building. Its district box
        /// is a plot, and the middle of a plot is the yard — on the depot that box centres on bare
        /// paving with the sheds off to one side. Its station anchor is no better: for the depot it
        /// resolves to the same empty pad. Both put a bright oval on open ground with the buildings
        /// they were meant to be showing still dark beside them.
        ///
        /// So the district is only used to say WHERE to look, and the biggest renderer standing
        /// inside it says what to light. Anything spanning most of the district is the ground it is
        /// all sitting on rather than a building, and is passed over.
        /// </summary>
        private bool Centre(int station, out Vector3 point)
        {
            point = Vector3.zero;
            if (!_operation.StationFocus(station, out Bounds area))
            {
                if (!_operation.StationAnchor(station, out point)) return false;
                point.y += _height;
                return true;
            }

            float ground = area.size.x * area.size.z * 0.45f;
            float best = 0f;
            Bounds building = default;

            foreach (var renderer in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
            {
                var box = renderer.bounds;
                if (!area.Contains(box.center)) continue;

                float footprint = box.size.x * box.size.z;
                if (footprint > ground || footprint <= best) continue;

                best = footprint;
                building = box;
            }

            point = best > 0f
                ? new Vector3(building.center.x, building.max.y + _height, building.center.z)
                : new Vector3(area.center.x, area.min.y + _height, area.center.z);
            return true;
        }

        /// <summary>
        /// A material whose only job is to be named <c>buildinglight</c>. Its shader is copied off
        /// one of the island's own lamp markers rather than looked up by name, because a shader that
        /// nothing in the project references by asset is exactly what build-time stripping removes —
        /// and a lamp marker is guaranteed to be in the scene wherever there is an island to light.
        /// </summary>
        private Material BuildMarkerMaterial()
        {
            foreach (var renderer in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null || materials[i].name != "lamp_glow") continue;
                    return new Material(materials[i]) { name = GlowMaterial };
                }
            }
            return null;
        }

        private void Raise(Vector3 point)
        {
            // One degenerate triangle and a material that is never rendered. IslandGlow reads the
            // submesh's bounds and the material's name; it never draws either, and the renderer is
            // switched off, so this costs a transform and nothing else.
            if (_marker == null)
            {
                _marker = new Mesh { name = "BuildingLightMarker" };
                _marker.vertices = new Vector3[3];
                _marker.SetTriangles(new int[] { 0, 1, 2 }, 0, false);
                _marker.bounds = new Bounds(Vector3.zero, Vector3.zero);
            }
            if (_material == null) _material = BuildMarkerMaterial();
            if (_material == null) return;

            var light = new GameObject("Light");
            light.transform.SetParent(_root, false);
            light.transform.position = point;
            light.AddComponent<MeshFilter>().sharedMesh = _marker;
            var renderer = light.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.enabled = false;
        }
    }
}
