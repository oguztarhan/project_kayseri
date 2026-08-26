using System.Collections.Generic;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Puts street lamps along the island's roads, because the art does not have any.
    ///
    /// Measured on Gold: the island carries 14 lamps at phase 1, 30 at phase 2 and 38 at phase 3,
    /// and every one of them stands inside a plot — three in Civic, two in Fleet, one at the mine.
    /// Road.Ring, Road.X, Road.Y and the town streets have none at all, which is why the roads are
    /// the darkest thing on the map at night no matter how well the lamps that do exist are lit.
    ///
    /// So this traces the kerbs and stands lamps along them. Nothing here is authored: the lamp is a
    /// clone of the island's own lamp — the same model the <c>_Overrides</c> pass already puts on
    /// every plot lamp — and its proportions, its scale and the offset from its base to its head are
    /// all measured off a live one rather than typed in, so a lamp raised on the road matches the
    /// lamps beside it on every island and at every phase.
    ///
    /// The kerbs come from the <c>.shoulder</c> meshes, which are the thin outlines Blender left
    /// around each road surface. The road meshes themselves are imported with Read/Write off and
    /// cannot be sampled at all; the shoulders were left readable, and being outlines they run down
    /// both sides of every road, which is exactly where lamps belong.
    ///
    /// Each lamp is given a disabled emissive marker at its head. That is the same contract the map
    /// art uses — <see cref="IslandGlow"/> finds night lights by looking for the emissive materials
    /// on renderers that are switched off — so the lamps raised here light up through exactly the
    /// same path as the authored ones, and this component knows nothing about how they are drawn.
    /// </summary>
    public sealed class StreetLamps : MonoBehaviour
    {
        private const string GlowMaterial = "lamp_glow";

        [Header("Yerleşim")]
        [Tooltip("Yol kenarı ağlarının adının sonu. Yolların kendi ağları okunamaz, omuzları okunur.")]
        [SerializeField] private string _kerbSuffix = ".shoulder";
        // Wide rather than close. A lamp every 24 units put 76 of them on the island and the roads
        // read as a solid ribbon of light with no dark between; spaced out, each one throws a pool
        // you can actually see the edge of, which is what makes the road look lit rather than tinted.
        [Tooltip("İki lamba arasındaki mesafe (dünya birimi).")]
        [SerializeField] private float _spacing = 95f;
        [Tooltip("Üst sınır. Her lamba bir çizim çağrısı, bu yüzden sayı serbest bırakılmıyor.")]
        [SerializeField] private int _maxLamps = 110;
        [Tooltip("Yolun yaklaşık genişliği. Lambanın kolunun hangi yöne bakacağını bulmakta kullanılır.")]
        [SerializeField] private float _roadWidth = 9f;

        // The model to stand on the kerb. It has to be the source asset, not the lamp already in the
        // scene: the island's lamps are batching-static, so their MeshFilter no longer holds a lamp
        // at all — it holds the 63,328 vertex combined mesh of everything static around it, and the
        // renderer draws a slice of that at a baked world position. Cloning one gives 76 lamps that
        // all draw on top of the lamp they were copied from. Everything ELSE about the lamp is still
        // measured off that scene instance; only the mesh has to come from the asset.
        [Header("Model")]
        [Tooltip("Sokak lambası modeli. Adadaki lambaların kullandığı modelin ta kendisi.")]
        [SerializeField] private GameObject _lampPrefab;

        [Tooltip("Hangi adanın canlı olduğuna bu sıklıkta bakılır.")]
        [SerializeField] private float _rebindSeconds = 0.5f;

        private Kayseri.Island.IslandPhaseController _phases;
        private float _rebindIn;
        private bool _built;

        /// <summary>What a live plot lamp looks like, measured rather than authored.</summary>
        private struct Template
        {
            public Vector3 modelOffset;   // from the lamp's base out to the post model's pivot
            public Vector3 modelScale;    // the size the override system settled the post at
            public Vector3 headOffset;    // from the base up to the lit head
            public Material glow;
        }

        /// <summary>A thinned kerb vertex, with the direction the road runs and the direction the
        /// road is, both worked out once and then reused by the spacing pass.</summary>
        private struct Node
        {
            public Vector3 point;
            public Vector3 along;
            public Vector3 across;
            public int road;      // which kerb mesh this came off
        }

        private Transform _root;
        private Mesh _marker;
        private Coroutine _phaseRebuild;
        private readonly List<Vector3> _kerb = new List<Vector3>();
        private readonly List<int> _kerbRoad = new List<int>();
        private readonly List<Node> _nodes = new List<Node>();
        private readonly List<Node> _placed = new List<Node>();

        /// <summary>
        /// Follows whichever island is live, and whatever phase it is at.
        ///
        /// Binding once at Start covers neither. The archipelago keeps all eight islands in the one
        /// scene and switches one of them on, so a controller taken by <c>FindAnyObjectByType</c>
        /// belongs to whichever island answered first — after the player travels, the lamps are
        /// standing on a road network that is no longer switched on, and the island they are
        /// looking at has none. Nor can the controller be found through the live operation: all
        /// eight operations are components on the ONE object, so there is no hierarchy from an
        /// operation down to its island. The island roots are where the controllers live, and
        /// exactly one of those roots is ever active — which makes "the active controller" both
        /// the simplest question to ask and the right one.
        ///
        /// Its PhaseChanged covers the other half: a district that rebuilds lays new roads, and new
        /// roads want lamps down them.
        /// </summary>
        private void Update()
        {
            _rebindIn -= Time.unscaledDeltaTime;
            if (_rebindIn > 0f) return;
            _rebindIn = Mathf.Max(0.1f, _rebindSeconds);

            var live = ActiveController();
            if (live == _phases && _built) return;

            if (live != _phases)
            {
                if (_phases != null) _phases.PhaseRefreshCompleted -= OnPhaseRefreshCompleted;
                _phases = live;
                if (_phases != null) _phases.PhaseRefreshCompleted += OnPhaseRefreshCompleted;
            }

            // Keeps trying until it actually stands something. The roads are not in the scene for
            // the first fraction of a second, and an island with no readable kerb never will be.
            _built = Rebuild();
        }

        /// <summary>The one island root that is switched on, and so the one phase controller that
        /// is live. Inactive objects are excluded by the search itself.</summary>
        private static Kayseri.Island.IslandPhaseController ActiveController()
        {
            foreach (var controller in FindObjectsByType<Kayseri.Island.IslandPhaseController>(FindObjectsInactive.Exclude))
                return controller;
            return null;
        }

        private void OnPhaseRefreshCompleted()
        {
            if (_phaseRebuild == null) _phaseRebuild = StartCoroutine(RebuildAfterPhase());
        }

        private System.Collections.IEnumerator RebuildAfterPhase()
        {
            // Lamp placement samples readable road meshes and can instantiate a large row. Keep it
            // off both the purchase frame and the building-light frame.
            yield return null;
            yield return null;
            _built = Rebuild();
            _phaseRebuild = null;
        }

        private void OnDestroy()
        {
            if (_phases != null) _phases.PhaseRefreshCompleted -= OnPhaseRefreshCompleted;
            if (_marker != null) Destroy(_marker);
        }

        /// <summary>True once lamps are actually standing, which is what tells the caller to stop
        /// trying. Everything it needs — the prefab, a lamp to measure, a readable kerb — can be
        /// missing for the first frames of an island, or for good on one that has no roads.</summary>
        private bool Rebuild()
        {
            if (_lampPrefab == null) return false;
            if (!FindTemplate(out Template template)) return false;

            GatherKerbs();
            if (_kerb.Count == 0) return false;

            BuildNodes();
            if (_nodes.Count == 0) return false;

            if (_root != null) Destroy(_root.gameObject);
            _root = new GameObject("StreetLamps").transform;
            _root.SetParent(transform, false);

            Place(template);

            // The lamps only light up once IslandGlow has seen them, and its own look-again pass may
            // already be finished by the time the roads have resolved.
            var glow = FindAnyObjectByType<IslandGlow>();
            if (glow != null) glow.Refresh();

            return _placed.Count > 0;
        }

        /// <summary>
        /// Finds a plot lamp that is currently standing and reads its proportions off it. A lamp is
        /// a pair: the original mesh, kept in the hierarchy with its renderer switched off, whose
        /// emissive submesh marks where the light is; and the replacement model beside it under
        /// <c>_Overrides</c>, which is the post you actually see. Both are needed — one gives the
        /// head, the other gives the thing to clone.
        /// </summary>
        private bool FindTemplate(out Template template)
        {
            template = default;

            foreach (var filter in FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null) continue;

                var renderer = filter.GetComponent<Renderer>();
                if (renderer == null) continue;

                var materials = renderer.sharedMaterials;
                int slot = -1;
                for (int i = 0; i < Mathf.Min(materials.Length, mesh.subMeshCount); i++)
                    if (materials[i] != null && materials[i].name == GlowMaterial) { slot = i; break; }
                if (slot < 0) continue;

                var parent = filter.transform.parent;
                if (parent == null) continue;
                var model = parent.Find("_Overrides/" + filter.name);
                if (model == null || !model.gameObject.activeInHierarchy) continue;

                // Every authored lamp stands unrotated, so the offsets measured here are already in
                // the lamp's own frame and only need the yaw this component gives each new one.
                Vector3 basePoint = filter.transform.position;
                template = new Template
                {
                    modelOffset = model.position - basePoint,
                    modelScale = model.lossyScale,
                    headOffset = filter.transform.TransformPoint(mesh.GetSubMesh(slot).bounds.center) - basePoint,
                    glow = materials[slot],
                };
                return true;
            }

            return false;
        }

        /// <summary>Every kerb vertex on the island, in world space, each tagged with the road it
        /// came off — which is what lets the spacing pass treat one road at a time.</summary>
        private void GatherKerbs()
        {
            _kerb.Clear();
            _kerbRoad.Clear();

            int road = 0;
            foreach (var filter in FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude))
            {
                if (!filter.name.EndsWith(_kerbSuffix)) continue;

                var mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;

                var local = mesh.vertices;
                var transformer = filter.transform;
                for (int i = 0; i < local.Length; i++)
                {
                    _kerb.Add(transformer.TransformPoint(local[i]));
                    _kerbRoad.Add(road);
                }
                road++;
            }
        }

        /// <summary>
        /// Thins the raw kerb vertices down to a workable set and works out, for each survivor,
        /// which way the road runs and which way the road is.
        ///
        /// The thinning is not only for speed. A shoulder mesh is dense and uneven — 3713 vertices
        /// on the ring alone, bunched at every corner — and both the direction fit and the spacing
        /// pass want points that stand for a length of kerb rather than for a vertex.
        /// </summary>
        private void BuildNodes()
        {
            _nodes.Clear();
            float step = Mathf.Max(1f, _roadWidth * 0.45f);
            float stepSqr = step * step;

            var thinned = new List<int>();
            for (int i = 0; i < _kerb.Count; i++)
            {
                bool crowded = false;
                for (int t = 0; t < thinned.Count; t++)
                {
                    if (Flat(_kerb[thinned[t]] - _kerb[i]).sqrMagnitude >= stepSqr) continue;
                    crowded = true;
                    break;
                }
                if (!crowded) thinned.Add(i);
            }

            float near = _roadWidth * 0.75f;
            float minimum = _roadWidth * 0.4f, maximum = _roadWidth * 1.6f;

            for (int i = 0; i < thinned.Count; i++)
            {
                Vector3 point = _kerb[thinned[i]];

                // Which way the kerb runs here: the major axis of the 2x2 covariance of the nearby
                // offsets in the ground plane.
                float xx = 0f, xz = 0f, zz = 0f;
                for (int j = 0; j < thinned.Count; j++)
                {
                    Vector3 offset = Flat(_kerb[thinned[j]] - point);
                    if (offset.sqrMagnitude > near * near) continue;
                    xx += offset.x * offset.x; xz += offset.x * offset.z; zz += offset.z * offset.z;
                }

                float angle = 0.5f * Mathf.Atan2(2f * xz, xx - zz);
                var along = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var across = new Vector3(along.z, 0f, -along.x);

                // Which side the road is on. The kerb outline runs down BOTH sides of a road, so
                // from a point on one side there is always a stretch of the same outline about a
                // road's width away on the other — and the side that stretch is on is the road.
                int ahead = 0, behind = 0;
                for (int j = 0; j < thinned.Count; j++)
                {
                    Vector3 offset = Flat(_kerb[thinned[j]] - point);
                    float distance = offset.magnitude;
                    if (distance < minimum || distance > maximum) continue;

                    float side = Vector3.Dot(offset, across);
                    if (side > minimum) ahead++;
                    else if (side < -minimum) behind++;
                }

                _nodes.Add(new Node
                {
                    point = point,
                    along = along,
                    across = ahead >= behind ? across : -across,
                    road = _kerbRoad[thinned[i]],
                });
            }

            KeepOneSide();
        }

        /// <summary>
        /// Throws away one of each road's two kerbs, so the lamps run down a single side.
        ///
        /// Spacing alone cannot do this. A shoulder outline wraps the road, both of its edges are
        /// the same mesh, and a distance rule cannot tell them apart — so the lamp accepted at each
        /// interval is whichever edge the vertex order reached first, and the line of lamps crosses
        /// the road and back down its length.
        ///
        /// Which edge is "one side" depends on the shape of the road, and there are only two shapes
        /// here. A straight road is long and thin, so its two edges are cleanly separated by the sign
        /// of their offset from the centre, across the road's own axis. That same test would cut a
        /// closed loop in half rather than into edges — but a loop has something better: every node
        /// already knows which way the road is, and on the outer edge of a loop that direction points
        /// back towards the middle while on the inner edge it points away. Which of the two a road is
        /// falls straight out of its own covariance: a loop's spread is even in both directions, a
        /// street's is all in one.
        ///
        /// The obvious test for the loop — keep everything past the median radius — is wrong, and
        /// looked right until measured. The ring road is a rounded diamond, not a circle, so radius
        /// varies as much along the loop as across it: that test kept the corners, both edges of
        /// them, and dropped the straights. Two of the ring's three lamps came out on the inner kerb.
        /// </summary>
        private void KeepOneSide()
        {
            var kept = new List<Node>();
            var road = new List<Node>();

            int roads = 0;
            for (int i = 0; i < _nodes.Count; i++) roads = Mathf.Max(roads, _nodes[i].road + 1);

            for (int r = 0; r < roads; r++)
            {
                road.Clear();
                for (int i = 0; i < _nodes.Count; i++)
                    if (_nodes[i].road == r) road.Add(_nodes[i]);
                if (road.Count < 3) { kept.AddRange(road); continue; }

                Vector3 centre = Vector3.zero;
                for (int i = 0; i < road.Count; i++) centre += road[i].point;
                centre /= road.Count;

                float xx = 0f, xz = 0f, zz = 0f;
                for (int i = 0; i < road.Count; i++)
                {
                    Vector3 offset = Flat(road[i].point - centre);
                    xx += offset.x * offset.x; xz += offset.x * offset.z; zz += offset.z * offset.z;
                }

                // Eigenvalues of the 2x2 covariance. Their ratio is how elongated the road is.
                float trace = xx + zz;
                float gap = Mathf.Sqrt(Mathf.Max(0f, trace * trace - 4f * (xx * zz - xz * xz)));
                float major = (trace + gap) * 0.5f, minor = (trace - gap) * 0.5f;

                if (major > 0f && minor / major < 0.25f)
                {
                    float angle = 0.5f * Mathf.Atan2(2f * xz, xx - zz);
                    var side = new Vector3(Mathf.Sin(angle), 0f, -Mathf.Cos(angle));
                    for (int i = 0; i < road.Count; i++)
                        if (Vector3.Dot(Flat(road[i].point - centre), side) >= 0f) kept.Add(road[i]);
                    continue;
                }

                for (int i = 0; i < road.Count; i++)
                {
                    Vector3 inward = Flat(centre - road[i].point).normalized;
                    if (Vector3.Dot(road[i].across, inward) >= 0f) kept.Add(road[i]);
                }
            }

            _nodes.Clear();
            _nodes.AddRange(kept);
        }

        /// <summary>
        /// Stands a lamp every <see cref="_spacing"/> along each road, one road at a time.
        ///
        /// The spacing is a plain distance, but it only counts against lamps already standing on the
        /// SAME kerb mesh. That one qualifier is what makes it work here, and it took two wrong
        /// answers to get to. A plain distance against everything blankets whole streets, because
        /// the ring road passes within a few metres of the cross roads and the town: 24 lamps for
        /// the network, with roads skipped entirely because a different road nearby already had one.
        /// Splitting the test into along-the-kerb and across-it fixed that but leaked on every bend —
        /// a point further round a curve reads as across rather than along, so the ring kept taking
        /// lamps no matter how far the spacing was wound out: 55 at 42 units, 50 at 70.
        ///
        /// Per-road distance has neither failure. Curves are handled because distance does not care
        /// which way the road was heading, and roads cannot starve each other because they are never
        /// compared. A small all-roads minimum on top keeps junctions, where two kerbs genuinely do
        /// meet, from stacking lamps on the same corner.
        /// </summary>
        private void Place(Template template)
        {
            _placed.Clear();
            float spacing = Mathf.Max(1f, _spacing);
            float spacingSqr = spacing * spacing;
            float junctionSqr = spacingSqr * 0.12f;

            for (int i = 0; i < _nodes.Count && _placed.Count < _maxLamps; i++)
            {
                var node = _nodes[i];

                bool crowded = false;
                for (int p = 0; p < _placed.Count; p++)
                {
                    float distance = Flat(node.point - _placed[p].point).sqrMagnitude;
                    bool sameRoad = _placed[p].road == node.road;
                    if (distance >= (sameRoad ? spacingSqr : junctionSqr)) continue;
                    crowded = true;
                    break;
                }
                if (crowded) continue;

                _placed.Add(node);
                Raise(node.point, node.across, template);
            }
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private void Raise(Vector3 basePoint, Vector3 arm, Template template)
        {
            var lamp = new GameObject("Lamp");
            lamp.transform.SetParent(_root, false);
            // The authored lamps all reach along -X, so that is the direction being turned here.
            lamp.transform.SetPositionAndRotation(basePoint, Quaternion.FromToRotation(Vector3.left, arm));

            var post = Instantiate(_lampPrefab, lamp.transform);
            post.name = "Post";
            post.transform.localPosition = template.modelOffset;
            post.transform.localRotation = Quaternion.identity;
            post.transform.localScale = template.modelScale;

            // What makes the lamp a light: IslandGlow scans for emissive materials on switched-off
            // renderers, so a marker at the head is all it takes. A single degenerate triangle —
            // the scan reads the submesh's bounds, never its vertices, and the renderer never draws.
            if (_marker == null)
            {
                _marker = new Mesh { name = "StreetLampGlow" };
                _marker.vertices = new Vector3[3];
                _marker.SetTriangles(new int[] { 0, 1, 2 }, 0, false);
                _marker.bounds = new Bounds(Vector3.zero, Vector3.zero);
            }

            var glow = new GameObject("Glow");
            glow.transform.SetParent(lamp.transform, false);
            glow.transform.localPosition = template.headOffset;
            glow.AddComponent<MeshFilter>().sharedMesh = _marker;
            var renderer = glow.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = template.glow;
            renderer.enabled = false;
        }
    }
}
