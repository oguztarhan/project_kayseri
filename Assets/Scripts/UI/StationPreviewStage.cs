using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// The photo studio behind <see cref="StationScreenUI"/> — one camera, one backdrop and a
    /// turntable, parked two kilometres under the sea floor where nothing else in the scene can
    /// wander into shot.
    ///
    /// The screen shows the player the actual building rather than a picture of one, so the model has
    /// to come from the island's own art. Every district keeps all three of its phase variants in the
    /// scene side by side with only one active, so a preview is a clone of that transform dropped onto
    /// the turntable. That is also the only way the phase-change sequence can work at all: for two
    /// seconds the market the player is leaving and the one they just bought have to exist at the same
    /// time, and out on the island they never do.
    ///
    /// Districts are pure art — Transform, MeshFilter, MeshRenderer and nothing else — so a clone runs
    /// no logic and needs no teardown beyond destroying it. Lighting is deliberately not set up here
    /// either: the scene's own directional light reaches the studio, and matching the island exactly
    /// matters more than a flattering key light would, because the player is about to go and look at
    /// the real thing.
    ///
    /// The camera renders only while the screen is open. Closed, this costs nothing at all.
    /// </summary>
    public sealed class StationPreviewStage : MonoBehaviour
    {
        [Tooltip("Stüdyonun dünyadaki yeri — sahnede hiçbir şeyin uğramadığı boş bir nokta olmalı.")]
        [SerializeField] private Vector3 stagePosition = new Vector3(0f, -2000f, 0f);
        [Tooltip("Önizleme dokusu. Oranı ekrandaki model alanının oranıyla aynı olmalı, yoksa model ezilir.")]
        [SerializeField, Min(128)] private int textureWidth = 864;
        [SerializeField, Min(128)] private int textureHeight = 720;

        [Header("Fon")]
        [SerializeField] private Color backdropTop = new Color(0.24f, 0.42f, 0.68f, 1f);
        [SerializeField] private Color backdropBottom = new Color(0.05f, 0.09f, 0.18f, 1f);

        [Header("Kamera")]
        [SerializeField, Range(10f, 60f)] private float fieldOfView = 28f;
        [Tooltip("Kameranın bakış açısı. Alçak açı binayı büyük gösterir, yüksek açı yerleşimi gösterir.")]
        [SerializeField, Range(0f, 70f)] private float pitch = 24f;
        [SerializeField] private float yaw = 38f;
        [Tooltip("Modelin kareyi ne kadar doldurduğu. 1 = kenarlara değer.")]
        [SerializeField, Range(0.3f, 1f)] private float fill = 0.88f;

        [Header("Salınım")]
        [Tooltip("Tabla tam tur dönmez, sağa sola salınır — binanın hep iyi cephesi kamerada kalsın diye.")]
        [SerializeField, Range(0f, 90f)] private float swayDegrees = 22f;
        [SerializeField, Min(1f)] private float swaySeconds = 11f;

        private Transform _stage;
        private Transform _turntable;
        private Camera _cam;
        private RenderTexture _rt;
        private Material _backdropMat;
        private Texture2D _backdropTex;
        private float _radius = 1f;
        private float _distance = 100f;
        private float _sway;
        private bool _built;

        /// <summary>Bounding-sphere radius of whatever the camera is framing — the caller's animations
        /// are measured in it, so a market and a refinery sink and rise by their own size.</summary>
        public float FocusRadius { get { return _radius; } }

        /// <summary>1 is the authored framing; the phase sequence pushes in past it.</summary>
        public float Zoom { get; set; }

        /// <summary>Whether the camera is rendering. False costs nothing.</summary>
        public bool Live
        {
            get { return _built && _cam != null && _cam.enabled; }
            set
            {
                // Switching off has to survive teardown: the screen turns the stage off from OnDisable,
                // which on play-mode exit runs after the studio itself has already been destroyed.
                if (!value)
                {
                    if (_built && _cam != null) _cam.enabled = false;
                    return;
                }
                Build();
                if (_cam == null) return;
                _cam.enabled = true;
                _sway = 0f;
            }
        }

        /// <summary>The texture the screen's RawImage draws. Created on first use, released on destroy.</summary>
        public RenderTexture Texture
        {
            get
            {
                if (_rt == null)
                {
                    _rt = new RenderTexture(textureWidth, textureHeight, 24, RenderTextureFormat.Default);
                    _rt.name = "IstasyonOnizleme";
                    _rt.antiAliasing = 2;     // the buildings are all hard silhouettes against a flat sky
                    _rt.useMipMap = false;
                    _rt.filterMode = FilterMode.Bilinear;
                    _rt.Create();
                }
                return _rt;
            }
        }

        private void Awake()
        {
            Zoom = 1f;
        }

        private void Build()
        {
            if (_built) return;
            _built = true;

            // A scene root, not a child of this component: the screen lives under a Canvas, and a
            // canvas scale factor applied to 3D geometry would quietly resize the whole studio.
            var root = new GameObject("IstasyonStudyosu");
            root.transform.position = stagePosition;
            _stage = root.transform;

            var turn = new GameObject("Tabla");
            turn.transform.SetParent(_stage, false);
            _turntable = turn.transform;

            var camGo = new GameObject("OnizlemeKamerasi", typeof(Camera));
            camGo.transform.SetParent(_stage, false);
            _cam = camGo.GetComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = backdropBottom;
            _cam.fieldOfView = fieldOfView;
            _cam.nearClipPlane = 1f;
            _cam.farClipPlane = 1400f;
            _cam.depth = -10f;               // renders before the main camera, so the RawImage is never a frame late
            _cam.allowHDR = false;
            _cam.allowMSAA = false;
            _cam.useOcclusionCulling = false;
            _cam.targetTexture = Texture;
            _cam.enabled = false;

            BuildBackdrop();
        }

        /// <summary>
        /// A gradient card parented to the camera, sized to exactly cover the frustum at its own
        /// distance — so it fills the frame at any focal length and never needs re-fitting. Unlit on
        /// purpose: the scene's directional light points wherever the island needed it, and a lit
        /// backdrop would go dark or blow out depending on which island the player is standing on.
        /// </summary>
        private void BuildBackdrop()
        {
            const float dist = 900f;

            _backdropTex = new Texture2D(2, 64, TextureFormat.RGBA32, false);
            _backdropTex.wrapMode = TextureWrapMode.Clamp;
            _backdropTex.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < 64; y++)
            {
                Color c = Color.Lerp(backdropBottom, backdropTop, y / 63f);
                _backdropTex.SetPixel(0, y, c);
                _backdropTex.SetPixel(1, y, c);
            }
            _backdropTex.Apply();

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Fon";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(_cam.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, dist);
            go.transform.localRotation = Quaternion.identity;

            // Sized to the frustum at its own distance, aspect included — a square card in a 4:3 frame
            // leaves the clear colour showing down both sides as two black bars.
            float height = 2f * dist * Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.06f;
            float aspect = (float)textureWidth / Mathf.Max(1, textureHeight);
            go.transform.localScale = new Vector3(height * aspect, height, 1f);

            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Texture");
            _backdropMat = new Material(sh);
            _backdropMat.mainTexture = _backdropTex;
            if (_backdropMat.HasProperty("_BaseMap")) _backdropMat.SetTexture("_BaseMap", _backdropTex);
            if (_backdropMat.HasProperty("_BaseColor")) _backdropMat.SetColor("_BaseColor", Color.white);
            go.GetComponent<MeshRenderer>().sharedMaterial = _backdropMat;
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.GetComponent<MeshRenderer>().receiveShadows = false;
        }

        /// <summary>
        /// Clones a district onto the turntable, recentred on its own bounds so the table spins the
        /// building around itself rather than around whatever origin the island's layout gave it, and
        /// frames the camera on it. Returns the clone for the caller to animate.
        ///
        /// <paramref name="template"/> comes from the prefab asset rather than the scene — see
        /// <see cref="Kayseri.Island.IslandPhaseController.DistrictModel"/> for why the scene copy can
        /// never move. <paramref name="districtBounds"/> is that district's real extent measured off the
        /// scene copy, in the district root's own space, and it is passed in rather than measured here
        /// because the prefab's meshes carry stale bounds (they were written by a generator that never
        /// called RecalculateBounds) and would frame to nothing.
        /// </summary>
        public Transform Mount(Transform template, Bounds districtBounds)
        {
            if (template == null) return null;
            Build();

            // The clone hangs inside a holder rather than on the table directly. A district's art is
            // laid out around the island's origin, not its own, so the offset that centres it is large
            // — and scaling or sinking a transform with an offset like that swings the building across
            // the frame instead of moving it in place. The holder carries the animation, the clone
            // carries the offset, and the two never have to know about each other.
            var holder = new GameObject(template.name);
            holder.transform.SetParent(_turntable, false);

            GameObject clone = Instantiate(template.gameObject, holder.transform);
            clone.name = template.name + " (model)";
            clone.SetActive(true);
            Transform t = clone.transform;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            t.localPosition = -districtBounds.center;

            // Those stale mesh bounds would also have the frustum throw half the building away, so every
            // renderer is given one generous box instead. Nothing else is out here to be culled against.
            var rs = clone.GetComponentsInChildren<Renderer>(true);
            Bounds wide = new Bounds(Vector3.zero, districtBounds.size * 2f);
            for (int i = 0; i < rs.Length; i++)
            {
                rs[i].gameObject.isStatic = false;
                rs[i].localBounds = wide;
            }

            _radius = Mathf.Max(0.5f, districtBounds.extents.magnitude);
            _distance = FitDistance(districtBounds);
            return holder.transform;
        }

        /// <summary>
        /// How far back the camera has to stand for this building to fill the frame.
        ///
        /// Measured off the eight corners of its box turned into camera space, not off a bounding
        /// sphere. A district is a wide, flat slab — a market pad is twelve metres tall and seventy
        /// across — and the sphere around it is nearly all empty air, which pushed the camera back far
        /// enough that the building sat in the middle of the frame like a stamp.
        /// </summary>
        private float FitDistance(Bounds b)
        {
            Quaternion inv = Quaternion.Inverse(Quaternion.Euler(pitch, yaw, 0f));
            Vector3 e = b.extents;
            float hx = 0f, hy = 0f, hz = 0f;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? -e.x : e.x,
                                         (i & 2) == 0 ? -e.y : e.y,
                                         (i & 4) == 0 ? -e.z : e.z);
                Vector3 v = inv * corner;
                hx = Mathf.Max(hx, Mathf.Abs(v.x));
                hy = Mathf.Max(hy, Mathf.Abs(v.y));
                hz = Mathf.Max(hz, Mathf.Abs(v.z));
            }
            float halfTan = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
            float aspect = (float)textureWidth / Mathf.Max(1, textureHeight);
            float need = Mathf.Max(hx / Mathf.Max(0.05f, aspect), hy);
            return need / Mathf.Max(0.05f, fill * halfTan) + hz;
        }

        public void Clear()
        {
            if (_turntable == null) return;
            for (int i = _turntable.childCount - 1; i >= 0; i--)
            {
                // Destroy only lands at the end of the frame, and a screen closed and reopened in one
                // frame would mount the new building alongside the old one and shoot both. Hiding and
                // unparenting take effect now, so the table is empty the moment this returns.
                Transform c = _turntable.GetChild(i);
                c.gameObject.SetActive(false);
                c.SetParent(null, false);
                Destroy(c.gameObject);
            }
        }

        private void Update()
        {
            if (!_built || _cam == null || !_cam.enabled) return;

            _sway += Time.unscaledDeltaTime;
            float angle = Mathf.Sin(_sway / swaySeconds * Mathf.PI * 2f) * swayDegrees;
            _turntable.localRotation = Quaternion.Euler(0f, angle, 0f);

            // Cheap enough to redo every frame, and it has to be: Zoom is animated by the caller.
            float dist = _distance / Mathf.Max(0.2f, Zoom);
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            _cam.transform.rotation = rot;
            _cam.transform.position = _stage.position - rot * Vector3.forward * dist;
        }

        private void OnDestroy()
        {
            if (_stage != null) Destroy(_stage.gameObject);
            if (_backdropMat != null) Destroy(_backdropMat);
            if (_backdropTex != null) Destroy(_backdropTex);
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
        }
    }
}
