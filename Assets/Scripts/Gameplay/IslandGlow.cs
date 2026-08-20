using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;   // GetUniversalAdditionalCameraData is an extension method

namespace Game.Gameplay
{
    /// <summary>
    /// Draws the island's night lights.
    ///
    /// The map generator gives every street lamp, lit window and vehicle lamp its own emissive
    /// material, but the <c>_Overrides</c> pass swaps those meshes for the imported FBX models and
    /// disables the originals — so in Main not one of them is drawn (measured: 14 lamps, 13 windows,
    /// 8 headlights and 7 tail lights, all with their Renderer switched off). And at the game
    /// camera's distance the originals would not have read anyway: a lamp head is about 4x9 pixels,
    /// a tail light 1x2.
    ///
    /// What survives the swap is the <em>positions</em> — the disabled objects are still in the
    /// hierarchy, still parented to the vehicle or building they belong to. So this reads a light's
    /// place off the geometry that was switched off and draws the light there itself, as three
    /// pieces rather than as one dot:
    ///
    ///   the bulb, a disc at the lamp head;
    ///   the pool it throws, a screen-space decal shaded off the depth buffer, so it lands on
    ///   whatever surface is genuinely underneath — road, kerb, wall — with no ground height to
    ///   guess at and nothing to z-fight;
    ///   and the shaft between them, a quad billboarded around the light's own axis.
    ///
    /// All three live in one mesh and cost two draw calls between them (one per shader pass), and
    /// nothing at all during the day, when the renderer is switched off. On top of that the nearest
    /// few lamps get a real spot light so the island's toon shading picks them up as well.
    /// </summary>
    public sealed class IslandGlow : MonoBehaviour
    {
        /// <summary>Materials the generator uses for anything that is meant to be lit at night, with
        /// the size and colour each one should read as.</summary>
        /// <summary>Materials the generator uses for anything meant to be lit at night, plus
        /// <c>buildinglight</c>, which nothing in the art carries — <see cref="BuildingLights"/>
        /// stands one over each of the island's buildings at runtime through the same contract.</summary>
        private static readonly string[] LightMaterials =
            { "lamp_glow", "winlight", "headlight", "taillight", "buildinglight" };

        private const int Lamp = 0, Window = 1, Headlight = 2, Taillight = 3, Building = 4;

        // The bulb is the source, not the effect. It used to be sized to carry the whole light on
        // its own, which is exactly why the lights read as dots; now that the pool and the shaft do
        // that work, a halo this big only blows the middle of every lamp out to white.
        [Header("Işık boyutları (dünya birimi)")]
        [Tooltip("Sokak lambası hâlesinin çapı.")]
        [SerializeField] private float _lampSize = 1.8f;
        [Tooltip("Aydınlık pencere hâlesinin çapı.")]
        [SerializeField] private float _windowSize = 2f;
        // Halved against the street lamp: a vehicle carries two of each now, and two halos where
        // there used to be one is twice the light out of the same lamp.
        [SerializeField] private float _headlightSize = 1.5f;
        [SerializeField] private float _taillightSize = 1.1f;
        [Tooltip("Bina ışığının hâlesi. Bina ışığının görünür bir ampulü yok, sadece bina üstüne düşen ışığı var.")]
        [SerializeField] private float _buildingSize = 0f;

        // Sodium street lamps against cool white headlights. Real cities separate the two that way
        // and the eye reads the split instantly — everything fixed is amber, everything moving is
        // white — which is most of what makes a night skyline look like a night skyline rather than
        // like a dim day.
        [Header("Renkler")]
        [SerializeField] private Color _lampColor = new Color(1f, 0.72f, 0.36f, 1f);
        [SerializeField] private Color _windowColor = new Color(1f, 0.70f, 0.34f, 0.85f);
        [SerializeField] private Color _headlightColor = new Color(0.86f, 0.93f, 1f, 1f);
        [SerializeField] private Color _taillightColor = new Color(1f, 0.12f, 0.06f, 0.9f);
        [SerializeField] private Color _buildingColor = new Color(1f, 0.86f, 0.64f, 1f);

        [Header("Malzeme")]
        [Tooltip("Kayseri/IslandGlow shader'ını kullanan materyal.")]
        [SerializeField] private Material _glowMaterial;

        // The pool is the whole point of the effect: it is the only part that says a lamp is
        // shining ON something rather than just being bright. Range is how far the light carries,
        // in world units, measured from the bulb — so a street lamp's has to cover the drop to the
        // road plus the spread across it.
        [Header("Zemin ışığı")]
        [Tooltip("Sokak lambasının aydınlattığı yarıçap.")]
        [SerializeField] private float _lampRange = 26f;
        [SerializeField] private float _lampPoolIntensity = 1.9f;
        [Tooltip("Far ışığının yarıçapı.")]
        [SerializeField] private float _headlightRange = 20f;
        [SerializeField] private float _headlightPoolIntensity = 0.45f;
        [Tooltip("Aydınlık pencerenin duvara ve zemine vurduğu yarıçap.")]
        [SerializeField] private float _windowRange = 6f;
        [SerializeField] private float _windowPoolIntensity = 0.3f;
        [Tooltip("Stop lambasının arkaya vurduğu yarıçap.")]
        [SerializeField] private float _taillightRange = 5f;
        [SerializeField] private float _taillightPoolIntensity = 0.22f;
        // Aimed straight down from above the roofs, so the range has to cover the drop to the ground
        // and the spread has to cover the footprint. Kept dim on purpose: this is meant to say "a
        // building is here", not to be the brightest thing on the island.
        [Tooltip("Bina ışığının menzili. Çatının üstünden zemine kadar yetmeli.")]
        [SerializeField] private float _buildingRange = 44f;
        [Tooltip("Binanın üstüne düşen aydınlık lekenin genişliği.")]
        [SerializeField] private float _buildingSpread = 17f;
        [SerializeField] private float _buildingPoolIntensity = 0.5f;
        [Tooltip("Lambanın yolda açtığı aydınlık dairenin genişliği.")]
        [SerializeField] private float _lampSpread = 14f;
        [Tooltip("Farın yolda açtığı aydınlık lekenin genişliği.")]
        [SerializeField] private float _headlightSpread = 8f;

        // Lamps and vehicles get a visible shaft; a window and a tail light do not have one — a lit
        // window is a surface that has been lit from inside, not a lamp pointing at the street.
        [Header("Işık huzmesi")]
        [Tooltip("Sokak lambası huzmesinin boyu. Zemine gömülen kısmını shader derinlikle siler.")]
        [SerializeField] private float _lampBeamLength = 12f;
        [Tooltip("Huzmenin lamba başındaki genişliği.")]
        [SerializeField] private float _lampBeamTop = 0.7f;
        [Tooltip("Huzmenin zemindeki genişliği.")]
        [SerializeField] private float _lampBeamBottom = 6f;
        [SerializeField] private float _headlightBeamLength = 15f;
        [SerializeField] private float _headlightBeamTop = 0.6f;
        [SerializeField] private float _headlightBeamBottom = 5f;
        [Tooltip("Araç farlarının yola bakma açısı (derece).")]
        [SerializeField] private float _headlightPitch = 11f;
        [Tooltip("Bir aracın iki farı arasındaki en fazla açıklık. Gerçek açıklık ışığın kendi ağından ölçülür.")]
        [SerializeField] private float _maxPairSpan = 3.5f;

        // Real lights are the luxury tier: the decal pool already puts light on the ground, but only
        // a Light makes the island's toon shader band a lamp's falloff across the road the way the
        // sun's is banded. Budgeted by distance because an island carries a few hundred lamps and
        // the ones behind the camera are worth nothing.
        [Header("Gerçek ışıklar")]
        [Tooltip("Aynı anda kaç lambaya gerçek ışık düşer. Kameraya en yakınlar seçilir.")]
        [SerializeField] private int _maxRealLights = 24;
        [SerializeField] private float _lampIntensity = 1.4f;
        [SerializeField] private float _lampSpotAngle = 108f;
        [SerializeField] private float _headlightIntensity = 1.1f;
        [SerializeField] private float _headlightSpotAngle = 58f;
        [Tooltip("Hangi lambaların ışık alacağının kaç saniyede bir yeniden seçileceği.")]
        [SerializeField] private float _lightReselectSeconds = 0.25f;

        [SerializeField] private Kayseri.Island.IslandPhaseController _phases;

        /// <summary>A light, kept as an anchor plus a local offset rather than a world position, so
        /// the ones bolted to a truck or the train rake travel with it for free.</summary>
        private struct Source
        {
            public Transform anchor;
            public Vector3 local;
            public Vector3 mount;   // from the middle of the mesh out to this lamp, in local space
            public float span;      // distance between the pair this submesh holds; 0 if it is one lamp
            public int kind;        // index into LightMaterials
        }

        /// <summary>One quad of one light. <see cref="shape"/> matches the shader's GLOW_* codes:
        /// 0 bulb, 1 pool, 2 beam.</summary>
        private struct Quad
        {
            public int source;
            public int shape;
            public float side;   // -1 and +1 for the two lamps of a vehicle pair, 0 for a single light
        }

        private const int ShapeBulb = 0, ShapePool = 1, ShapeWash = 2, ShapeBeam = 3;

        private Source[] _sources;
        private Quad[] _quads;
        private Mesh _mesh;
        private MeshRenderer _renderer;

        // Rebuilt in place every night frame; sized once so the per-frame path never allocates.
        private Vector3[] _vertices;
        private Vector2[] _uv;
        private Vector2[] _corners;
        private Vector2[] _cornersRest;   // what _corners holds while the light's anchor is alive
        private Vector2[] _shape;
        private Vector2[] _params;
        private Vector3[] _axis;
        private Color[] _colors;
        private int[] _indices;
        private bool _lit;

        // Where each light is and which way it points this frame, worked out once per source and
        // then read by all three of its quads and by its spot light.
        private Vector3[] _worldPos;
        private Vector3[] _facing;
        private Vector3[] _lateral;
        private Vector3[] _lastPos;
        private bool[] _active;
        private bool[] _hasMoved;
        private bool _tracked;
        private bool _moved, _turned, _toggled;

        private Light[] _lights;
        private int[] _candidates;    // sources that can carry a real light
        private int[] _chosen;        // the nearest _maxRealLights of them
        private float[] _chosenDist;
        private int _chosenCount;
        private float _nextReselect;

        private Camera _camera;
        private float _rebindIn;
        private bool _rebuildQueued;
        private int _rebuildDelayFrames;
        private UnityEngine.Rendering.Universal.UniversalAdditionalCameraData _cameraData;

        private void Start()
        {
            Rebuild();
            StartCoroutine(Settle());
        }

        /// <summary>
        /// Points at whichever island is live and follows its phases.
        ///
        /// The archipelago keeps all eight islands in the one scene and switches one of them on, so
        /// a controller taken once at Start belongs to whichever island answered first, and after
        /// the player travels this component is listening to an island nobody is looking at. The
        /// controllers sit on the island roots and exactly one of those roots is active, so the
        /// active controller is the live one. Following it keeps the authored night lights — the lit
        /// windows, the vehicle lamps — in step with the island and phase actually on show.
        /// </summary>
        private void Rebind()
        {
            _rebindIn -= Time.unscaledDeltaTime;
            if (_rebindIn > 0f) return;
            _rebindIn = 0.5f;

            Kayseri.Island.IslandPhaseController live = null;
            foreach (var controller in FindObjectsByType<Kayseri.Island.IslandPhaseController>(FindObjectsInactive.Exclude))
            { live = controller; break; }
            if (live == _phases) return;

            if (_phases != null) _phases.PhaseRefreshCompleted -= OnPhaseRefreshCompleted;
            _phases = live;
            if (_phases != null) _phases.PhaseRefreshCompleted += OnPhaseRefreshCompleted;

            Rebuild();
        }

        /// <summary>
        /// Looks again over the first few seconds. The phase geometry is not in the scene when this
        /// component starts — a scan at Start finds 179 lights and not one street lamp, the same scan
        /// a second later finds 949 — and there is no ordering guarantee to rely on instead. Same
        /// reason, and same shape, as <see cref="IslandAmbience"/>'s settle.
        /// </summary>
        private System.Collections.IEnumerator Settle()
        {
            for (int i = 0; i < 3; i++)
            {
                yield return new WaitForSeconds(i == 0 ? 0.5f : 1f);
                Rebuild();
            }
        }

        private void OnPhaseRefreshCompleted()
        {
            _rebuildQueued = true;
            _rebuildDelayFrames = Mathf.Max(_rebuildDelayFrames, 3);
        }

        /// <summary>Scan for lights again. For anything that PUTS lights in the scene at runtime —
        /// <see cref="StreetLamps"/>, <see cref="BuildingLights"/> — which would otherwise have to
        /// land inside this component's settle window to be seen at all.</summary>
        public void Refresh()
        {
            _rebuildQueued = true;
            _rebuildDelayFrames = Mathf.Max(_rebuildDelayFrames, 1);
        }

        private void OnDestroy()
        {
            if (_phases != null) _phases.PhaseRefreshCompleted -= OnPhaseRefreshCompleted;
            if (_mesh != null) Destroy(_mesh);
        }

        private void Rebuild()
        {
            Collect();
            BuildMesh();
            BuildLights();
        }

        /// <summary>
        /// Walks every renderer in the scene — disabled ones included, which is the whole point —
        /// and records the centre of each submesh that carries one of the light materials.
        /// </summary>
        private void Collect()
        {
            var found = new List<Source>();

            foreach (var filter in FindObjectsByType<MeshFilter>(FindObjectsInactive.Include))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null) continue;

                var renderer = filter.GetComponent<Renderer>();
                if (renderer == null) continue;

                var materials = renderer.sharedMaterials;
                int count = Mathf.Min(materials.Length, mesh.subMeshCount);
                for (int slot = 0; slot < count; slot++)
                {
                    var material = materials[slot];
                    if (material == null) continue;

                    int kind = System.Array.IndexOf(LightMaterials, material.name);
                    if (kind < 0) continue;

                    if (!SubMeshBounds(mesh, slot, out Bounds lamp)) continue;

                    found.Add(new Source
                    {
                        anchor = filter.transform,
                        local = lamp.center,
                        mount = lamp.center - mesh.bounds.center,
                        span = Vehicle(kind) ? PairSpan(lamp, filter.transform) : 0f,
                        kind = kind,
                    });
                }
            }

            _sources = found.ToArray();

            _worldPos = new Vector3[_sources.Length];
            _facing = new Vector3[_sources.Length];
            _lateral = new Vector3[_sources.Length];
            _lastPos = new Vector3[_sources.Length];
            _active = new bool[_sources.Length];
            _hasMoved = new bool[_sources.Length];
            _tracked = false;
            for (int i = 0; i < _sources.Length; i++) _facing[i] = Vector3.down;
        }

        /// <summary>
        /// A submesh's extent in the mesh's local space, taken from the submesh descriptor rather
        /// than from the vertices. That is not a style choice: every island mesh is imported with
        /// Read/Write disabled, so <c>mesh.vertices</c> comes back empty and averaging it silently
        /// finds nothing — which is exactly how the first version of this managed to collect the
        /// vehicle lamps and not one of the 323 street lamps. Descriptor bounds are metadata and
        /// survive the import setting, and they skip copying an entire vertex array per mesh.
        /// </summary>
        private static bool SubMeshBounds(Mesh mesh, int slot, out Bounds bounds)
        {
            var descriptor = mesh.GetSubMesh(slot);
            bounds = descriptor.bounds;
            return descriptor.indexCount > 0;
        }

        /// <summary>
        /// How far apart a vehicle's two lamps are.
        ///
        /// A truck's headlights are one submesh, so its centre — which is all the descriptor gives —
        /// falls exactly between them, and drawing the light there put a single lamp up the middle
        /// of every bonnet. The pair is still in the numbers though: whatever the mesh's own axes
        /// are, two lamps side by side make the submesh widest across the gap between them. So the
        /// widest edge of that box, in world units, IS the track between the lamps, and it needs no
        /// guess about which way the model was built.
        ///
        /// It reads a single lamp correctly too — the train's one headlight measures a few
        /// centimetres across, and a pair a few centimetres apart is a pair nobody can tell from one.
        /// </summary>
        private float PairSpan(Bounds lamp, Transform anchor)
        {
            Vector3 scale = anchor.lossyScale, size = lamp.size;
            float span = Mathf.Max(size.x * Mathf.Abs(scale.x),
                         Mathf.Max(size.y * Mathf.Abs(scale.y), size.z * Mathf.Abs(scale.z)));
            return Mathf.Min(span, _maxPairSpan);
        }

        private float SizeFor(int kind)
        {
            switch (kind)
            {
                case Lamp: return _lampSize;
                case Window: return _windowSize;
                case Headlight: return _headlightSize;
                case Building: return _buildingSize;
                default: return _taillightSize;
            }
        }

        private Color ColorFor(int kind)
        {
            switch (kind)
            {
                case Lamp: return _lampColor;
                case Window: return _windowColor;
                case Headlight: return _headlightColor;
                case Building: return _buildingColor;
                default: return _taillightColor;
            }
        }

        private float RangeFor(int kind)
        {
            switch (kind)
            {
                case Lamp: return _lampRange;
                case Window: return _windowRange;
                case Headlight: return _headlightRange;
                case Building: return _buildingRange;
                default: return _taillightRange;
            }
        }

        private float PoolIntensityFor(int kind)
        {
            switch (kind)
            {
                case Lamp: return _lampPoolIntensity;
                case Window: return _windowPoolIntensity;
                case Headlight: return _headlightPoolIntensity;
                case Building: return _buildingPoolIntensity;
                default: return _taillightPoolIntensity;
            }
        }

        private float SpreadFor(int kind)
        {
            switch (kind)
            {
                case Lamp: return _lampSpread;
                case Building: return _buildingSpread;
                default: return _headlightSpread;
            }
        }

        /// <summary>The two kinds that come in pairs, one either side of a vehicle's centreline.</summary>
        private static bool Vehicle(int kind) => kind == Headlight || kind == Taillight;

        /// <summary>
        /// Whether this light throws a pool with a width and an edge, rather than an even wash.
        ///
        /// A lit window and a tail light are not aimed at anything — they are surfaces that happen
        /// to be bright — so the wash is right for them. Everything else is aimed, and a building's
        /// light most of all: washed evenly it lights the ground, the yard and the air around it by
        /// the same amount and the district turns into a pale round smudge with no edge and no
        /// shape, which reads as fog sitting on the island rather than as a lit building.
        /// </summary>
        private bool Aimed(int kind) => kind == Lamp || kind == Headlight || kind == Building;

        /// <summary>Which lights show the shaft of air between the lamp and what it lights. A
        /// building's floodlight is aimed but has no visible beam: a shaft the width of a district
        /// is a column of fog standing over the island.</summary>
        private bool Shaft(int kind) => kind == Lamp || kind == Headlight;

        /// <summary>Street lamps and headlights are the two that light the ground hard enough to be
        /// worth a real Light as well.</summary>
        private static bool Emits(int kind) => kind == Lamp || kind == Headlight;

        // ------------------------------------------------------------------------------ mesh

        private void BuildQuads()
        {
            bool beams = Game.Systems.QualityService.NightBeamsAllowed;

            int count = 0;
            for (int i = 0; i < _sources.Length; i++)
            {
                int shapes = beams && Shaft(_sources[i].kind) ? 3 : 2;
                count += Vehicle(_sources[i].kind) ? shapes * 2 : shapes;
            }

            _quads = new Quad[count];
            int q = 0;
            for (int i = 0; i < _sources.Length; i++)
            {
                int kind = _sources[i].kind;
                bool shaft = beams && Shaft(kind);
                int pool = Aimed(kind) ? ShapePool : ShapeWash;

                // A vehicle lamp is drawn twice, once either side of the centreline; everything
                // else is a single light and sits on it.
                if (!Vehicle(kind))
                {
                    _quads[q++] = new Quad { source = i, shape = ShapeBulb };
                    _quads[q++] = new Quad { source = i, shape = pool };
                    if (shaft) _quads[q++] = new Quad { source = i, shape = ShapeBeam };
                    continue;
                }

                for (int pair = 0; pair < 2; pair++)
                {
                    float side = pair == 0 ? -1f : 1f;
                    _quads[q++] = new Quad { source = i, shape = ShapeBulb, side = side };
                    _quads[q++] = new Quad { source = i, shape = pool, side = side };
                    if (shaft) _quads[q++] = new Quad { source = i, shape = ShapeBeam, side = side };
                }
            }
        }

        private void BuildMesh()
        {
            BuildQuads();

            int quads = _quads.Length;
            if (quads == 0) return;

            _vertices = new Vector3[quads * 4];
            _uv = new Vector2[quads * 4];
            _corners = new Vector2[quads * 4];
            _cornersRest = new Vector2[quads * 4];
            _shape = new Vector2[quads * 4];
            _params = new Vector2[quads * 4];
            _axis = new Vector3[quads * 4];
            _colors = new Color[quads * 4];
            _indices = new int[quads * 6];

            for (int i = 0; i < quads; i++)
            {
                int v = i * 4;
                var source = _sources[_quads[i].source];
                int kind = source.kind;

                _uv[v + 0] = new Vector2(0f, 0f);
                _uv[v + 1] = new Vector2(1f, 0f);
                _uv[v + 2] = new Vector2(1f, 1f);
                _uv[v + 3] = new Vector2(0f, 1f);

                switch (_quads[i].shape)
                {
                    case ShapeBulb:
                    {
                        Square(_cornersRest, v, SizeFor(kind) * 0.5f);
                        Fill(_shape, v, new Vector2(ShapeBulb, 0f));
                        Fill(_params, v, new Vector2(0f, 1f));
                        break;
                    }
                    case ShapePool:
                    case ShapeWash:
                    {
                        // A shade wider than the light's reach: the quad only has to cover the
                        // volume the lamp can touch, and perspective makes the near side of that
                        // volume bigger than its radius. Anything past it shades to zero anyway.
                        float range = RangeFor(kind);
                        bool aimed = _quads[i].shape == ShapePool;
                        float spread = aimed ? SpreadFor(kind) : 0f;
                        Square(_cornersRest, v, Mathf.Max(range, spread) * 1.15f);
                        Fill(_shape, v, new Vector2(_quads[i].shape, range));
                        Fill(_params, v, new Vector2(spread, PoolIntensityFor(kind)));
                        break;
                    }
                    default:
                    {
                        // The beam's corners are its cone: narrow at the bulb, wide where it lands,
                        // laid out along the light's axis rather than in the camera's plane.
                        bool lamp = kind == Lamp;
                        float length = lamp ? _lampBeamLength : _headlightBeamLength;
                        float top = (lamp ? _lampBeamTop : _headlightBeamTop) * 0.5f;
                        float bottom = (lamp ? _lampBeamBottom : _headlightBeamBottom) * 0.5f;

                        _cornersRest[v + 0] = new Vector2(-top, 0f);
                        _cornersRest[v + 1] = new Vector2(top, 0f);
                        _cornersRest[v + 2] = new Vector2(bottom, length);
                        _cornersRest[v + 3] = new Vector2(-bottom, length);

                        Fill(_shape, v, new Vector2(ShapeBeam, length));
                        Fill(_params, v, new Vector2(0f, 1f));
                        break;
                    }
                }

                var color = ColorFor(kind);
                _colors[v + 0] = _colors[v + 1] = _colors[v + 2] = _colors[v + 3] = color;

                int t = i * 6;
                _indices[t + 0] = v + 0; _indices[t + 1] = v + 2; _indices[t + 2] = v + 1;
                _indices[t + 3] = v + 0; _indices[t + 4] = v + 3; _indices[t + 5] = v + 2;
            }

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "IslandGlow" };
                _mesh.MarkDynamic();

                var go = new GameObject("Glow");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = _mesh;
                _renderer = go.AddComponent<MeshRenderer>();
                _renderer.sharedMaterial = _glowMaterial;
                _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _renderer.receiveShadows = false;
                _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                _renderer.enabled = false;
            }

            // A rebuild can change the light count, so the whole mesh is re-laid out here; Refresh
            // afterwards only ever touches the streams that move — positions, corners and axes.
            _mesh.Clear();
            _mesh.indexFormat = quads * 4 > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;

            Track();
            PlaceQuads();
            _mesh.vertices = _vertices;
            _mesh.normals = _axis;
            _mesh.uv = _uv;
            _mesh.uv2 = _corners;
            _mesh.uv3 = _shape;
            _mesh.uv4 = _params;
            _mesh.colors = _colors;
            _mesh.SetTriangles(_indices, 0, false);
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);   // billboards defeat culling
        }

        private static void Square(Vector2[] target, int v, float half)
        {
            target[v + 0] = new Vector2(-half, -half);
            target[v + 1] = new Vector2(half, -half);
            target[v + 2] = new Vector2(half, half);
            target[v + 3] = new Vector2(-half, half);
        }

        private static void Fill(Vector2[] target, int v, Vector2 value)
            => target[v + 0] = target[v + 1] = target[v + 2] = target[v + 3] = value;

        // ---------------------------------------------------------------------------- lights

        /// <summary>
        /// A fixed pool of spot lights, created once and reused. They are parented here rather than
        /// to the lamps so a phase rebuild cannot destroy them, and they are handed to whichever
        /// lamps are nearest the camera each time <see cref="ChooseLights"/> runs — an island
        /// carries a few hundred street lamps and lighting all of them would cost real frames on
        /// lamps that are off screen or a district away.
        /// </summary>
        private void BuildLights()
        {
            var candidates = new List<int>();
            if (Game.Systems.QualityService.NightSpotLightsAllowed)
                for (int i = 0; i < _sources.Length; i++)
                    if (Emits(_sources[i].kind)) candidates.Add(i);

            _candidates = candidates.ToArray();

            int wanted = Mathf.Min(Mathf.Max(0, _maxRealLights), _candidates.Length);
            if (_lights == null || _lights.Length < wanted)
            {
                var grown = new Light[wanted];
                if (_lights != null) System.Array.Copy(_lights, grown, _lights.Length);
                for (int i = _lights == null ? 0 : _lights.Length; i < wanted; i++)
                {
                    var go = new GameObject("Pool");
                    go.transform.SetParent(transform, false);
                    var light = go.AddComponent<Light>();
                    light.type = LightType.Spot;
                    light.shadows = LightShadows.None;   // 24 shadow-casting spots is the one thing that will not hold 60fps
                    light.renderMode = LightRenderMode.ForcePixel;
                    light.enabled = false;
                    grown[i] = light;
                }
                _lights = grown;
            }

            _chosen = new int[wanted];
            _chosenDist = new float[wanted];
            _chosenCount = 0;
            _nextReselect = 0f;
        }

        /// <summary>Keeps the nearest <see cref="_maxRealLights"/> candidates to the camera. A
        /// straight insertion into a list this short beats sorting a few hundred entries, and the
        /// early-out means most candidates cost one compare.</summary>
        private void ChooseLights(Vector3 eye)
        {
            _chosenCount = 0;
            if (_chosen == null || _chosen.Length == 0) return;

            int capacity = _chosen.Length;
            for (int c = 0; c < _candidates.Length; c++)
            {
                int source = _candidates[c];
                if (!_active[source]) continue;

                float distance = (_worldPos[source] - eye).sqrMagnitude;
                if (_chosenCount == capacity && distance >= _chosenDist[capacity - 1]) continue;

                int at = _chosenCount < capacity ? _chosenCount : capacity - 1;
                while (at > 0 && _chosenDist[at - 1] > distance)
                {
                    _chosenDist[at] = _chosenDist[at - 1];
                    _chosen[at] = _chosen[at - 1];
                    at--;
                }
                _chosenDist[at] = distance;
                _chosen[at] = source;
                if (_chosenCount < capacity) _chosenCount++;
            }
        }

        /// <summary>Moves each pooled light onto its lamp, aims it, and fades it up with the night so
        /// dusk brings the lights in rather than snapping them on.</summary>
        private void PlaceLights(float night)
        {
            if (_lights == null || _lights.Length == 0) return;

            for (int slot = 0; slot < _lights.Length; slot++)
            {
                var light = _lights[slot];
                if (light == null) continue;

                if (slot >= _chosenCount)
                {
                    light.enabled = false;
                    continue;
                }

                int source = _chosen[slot];
                bool lamp = _sources[source].kind == Lamp;
                Vector3 axis = _facing[source];

                light.transform.SetPositionAndRotation(_worldPos[source], Aim(axis));
                light.color = ColorFor(_sources[source].kind);
                light.range = RangeFor(_sources[source].kind);
                light.spotAngle = lamp ? _lampSpotAngle : _headlightSpotAngle;
                light.innerSpotAngle = light.spotAngle * 0.45f;
                light.intensity = (lamp ? _lampIntensity : _headlightIntensity) * night;
                light.enabled = true;
            }
        }

        /// <summary>A lamp points straight down, which is exactly the direction
        /// <see cref="Quaternion.LookRotation"/> cannot take Vector3.up as a hint for.</summary>
        private static Quaternion Aim(Vector3 axis)
            => Quaternion.LookRotation(axis, Mathf.Abs(axis.y) > 0.99f ? Vector3.forward : Vector3.up);

        // ----------------------------------------------------------------------------- frame

        private void Update()
        {
            Rebind();

            // BuildingLights, StreetLamps and the phase controller can all request the same global
            // scan during one rebuild. Coalesce them here, after their hierarchy edits have finished,
            // so the scene is walked once on the following frame rather than repeatedly inside the
            // purchase click.
            if (_rebuildDelayFrames > 0)
                _rebuildDelayFrames--;
            else if (_rebuildQueued)
            {
                _rebuildQueued = false;
                Rebuild();
            }

            // All three, not just the mesh: a domain reload in play mode hands the Mesh reference
            // back but drops the plain arrays alongside it, and the frame after a recompile then
            // walks a null source list.
            if (_mesh == null || _sources == null || _quads == null) return;

            // The shader fades everything by _KayseriNight, so this only has to decide whether the
            // mesh is worth drawing and submitting at all.
            float night = Shader.GetGlobalFloat("_KayseriNight");
            bool lit = night > 0.01f;
            if (lit)
            {
                Track();

                // Only the streams that actually changed. Three quads a light is a lot of vertices
                // to hand the driver sixty times a second, and most of them belong to street lamps
                // that have not moved since the island loaded: positions go up when a vehicle
                // drives, axes when one turns, corners when a phase switches something on or off.
                // A night with everything parked costs nothing here at all.
                if (_moved || _turned || _toggled)
                {
                    PlaceQuads();
                    // Positions follow a turn as well as a move — a vehicle's two lamps sit either
                    // side of its heading, so swinging the heading moves both of them.
                    _mesh.vertices = _vertices;
                    if (_turned || _toggled) _mesh.normals = _axis;
                    if (_toggled) _mesh.uv2 = _corners;
                }

                var eye = Eye();
                if (Time.time >= _nextReselect)
                {
                    _nextReselect = Time.time + Mathf.Max(0.05f, _lightReselectSeconds);
                    ChooseLights(eye);
                }
                PlaceLights(night);
            }

            if (lit == _lit) return;
            _lit = lit;
            if (_renderer != null) _renderer.enabled = lit;

            // The pools and beams read the depth buffer, which URP only fills when something asks
            // for it. Asked for on the first night and left alone from then on.
            if (lit) RequireDepth();

            if (!lit && _lights != null)
                for (int i = 0; i < _lights.Length; i++)
                    if (_lights[i] != null) _lights[i].enabled = false;
        }

        private Vector3 Eye()
        {
            if (_camera == null) _camera = Camera.main;
            return _camera != null ? _camera.transform.position : transform.position;
        }

        /// <summary>
        /// Ask the camera for a depth texture, which the pools and beams read to find the surface
        /// behind each pixel.
        ///
        /// Only ever raised, never lowered — and that is not laziness. This flag does not belong to
        /// this component: the SSAO feature raises it too, and it does so some way into the session
        /// rather than at load. Restoring "the value we found" therefore restores a value that was
        /// only false because nobody had asked yet, and the first sunrise after a night switches the
        /// depth texture off underneath whatever else had since come to depend on it. Leaving it on
        /// costs a prepass on a tier that has one already; turning it off costs correctness.
        /// </summary>
        private void RequireDepth()
        {
            if (_cameraData != null) return;
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;

            _cameraData = _camera.GetUniversalAdditionalCameraData();
            if (_cameraData != null) _cameraData.requiresDepthTexture = true;
        }

        /// <summary>
        /// Works out where every light is this frame and which way it is pointing.
        ///
        /// A street lamp and a window point down. A vehicle lamp points where the vehicle is going,
        /// taken from how far the anchor has actually travelled since the last frame — not from the
        /// anchor's own axes. The island vehicles are authored nose along -X with +Z up, and the
        /// pose that reaches this hierarchy is not the pose the mesh was modelled in, so a transform
        /// axis here would aim the headlights sideways on some islands and backwards on others.
        /// Where the thing moved is unambiguous on all four.
        ///
        /// Until it has moved, the lamp's own place on the body answers the same question: a
        /// headlight sits ahead of the middle of the mesh and a tail light sits behind it, so the
        /// offset between the two points the right way on a vehicle that is standing still — which
        /// the depot loaders do for most of the night. Without it a parked truck pools its
        /// headlights on the ground underneath itself.
        /// </summary>
        private void Track()
        {
            float pitch = Mathf.Deg2Rad * _headlightPitch;
            float forward = Mathf.Cos(pitch), down = Mathf.Sin(pitch);

            // Recomputed from scratch every pass. After a Collect the whole _active array reads
            // false, so the first pass flags everything as toggled and the mesh goes up in full.
            _moved = _turned = _toggled = false;

            for (int i = 0; i < _sources.Length; i++)
            {
                var anchor = _sources[i].anchor;
                bool wasActive = _active[i];
                _active[i] = anchor != null && anchor.gameObject.activeInHierarchy;
                if (wasActive != _active[i]) _toggled = true;
                if (!_active[i]) continue;

                Vector3 world = anchor.TransformPoint(_sources[i].local);
                if (world != _worldPos[i]) _moved = true;
                _worldPos[i] = world;

                int kind = _sources[i].kind;
                if (kind == Lamp || kind == Window)
                {
                    _facing[i] = Vector3.down;
                    continue;
                }

                Vector3 wasFacing = _facing[i];

                if (_tracked)
                {
                    Vector3 travel = world - _lastPos[i];
                    travel.y = 0f;
                    if (travel.sqrMagnitude > 1e-6f)
                    {
                        _hasMoved[i] = true;
                        Vector3 heading = travel.normalized;
                        if (kind == Taillight) heading = -heading;

                        // Eased rather than snapped: a lamp a metre off the vehicle's centre swings
                        // through a corner faster than the vehicle does, and the raw per-frame
                        // delta jitters when it is barely moving.
                        Vector3 aim = heading * forward + Vector3.down * down;
                        _facing[i] = Vector3.Normalize(_facing[i] + (aim - _facing[i]) * 0.25f);
                    }
                }
                _lastPos[i] = world;

                // Still parked. Read the direction off the body instead, every frame rather than
                // once, so a loader that turns on the spot turns its headlights with it. The moment
                // it drives anywhere, travel takes over for good.
                if (!_hasMoved[i])
                {
                    Vector3 mount = anchor.TransformVector(_sources[i].mount);
                    mount.y = 0f;
                    if (mount.sqrMagnitude > 1e-6f)
                        _facing[i] = mount.normalized * forward + Vector3.down * down;
                }

                if (_facing[i] != wasFacing) _turned = true;

                // Across the heading: where the left and right lamp of the pair sit.
                Vector3 across = Vector3.Cross(Vector3.up, _facing[i]);
                _lateral[i] = across.sqrMagnitude > 1e-6f ? across.normalized : Vector3.right;
            }

            _tracked = true;
        }

        /// <summary>
        /// Moves every quad onto its light. Recomputed each night frame rather than cached because
        /// the truck and train lamps travel, and splitting the movers from the fixed ones would cost
        /// more bookkeeping than it saves at this count. Lights on an island or phase that is
        /// switched off get zero-size corners, which the GPU throws away as degenerate triangles.
        /// </summary>
        private void PlaceQuads()
        {
            for (int i = 0; i < _quads.Length; i++)
            {
                int source = _quads[i].source;
                int v = i * 4;

                if (!_active[source])
                {
                    _corners[v + 0] = _corners[v + 1] = _corners[v + 2] = _corners[v + 3] = Vector2.zero;
                    continue;
                }

                _corners[v + 0] = _cornersRest[v + 0];
                _corners[v + 1] = _cornersRest[v + 1];
                _corners[v + 2] = _cornersRest[v + 2];
                _corners[v + 3] = _cornersRest[v + 3];

                Vector3 world = _worldPos[source];
                if (_quads[i].side != 0f)
                    world += _lateral[source] * (_quads[i].side * 0.5f * _sources[source].span);
                _vertices[v + 0] = _vertices[v + 1] = _vertices[v + 2] = _vertices[v + 3] = world;

                Vector3 axis = _facing[source];
                _axis[v + 0] = _axis[v + 1] = _axis[v + 2] = _axis[v + 3] = axis;
            }
        }
    }
}
