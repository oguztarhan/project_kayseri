using Game.Gameplay;
using UnityEngine;
using UnityEngine.Rendering;

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
    /// the real thing. It matches the island's DAY, though — see <see cref="BeginPreviewCamera"/>.
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
        [Tooltip("Kadrajın alabileceği en geniş bölge (dünya birimi). Bundan uzun bir bölge ortasından " +
                 "çekilir, uçları kareden taşar — demiryolu 250 birim, tamamı kareye sığdırılınca " +
                 "bina değil saç teli oluyordu. 0 = sınırsız.")]
        [SerializeField, Min(0f)] private float maxSpan = 120f;

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
        private DayNightCycle _clock;
        private bool _clockSearched;
        private bool _holdingDaylight;

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
                _cam.targetTexture = Texture;   // the texture may have been rebuilt since the last open
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
                    // The buildings are all hard silhouettes against a flat sky, so MSAA earns its keep —
                    // but only where the device has it. Asking for it on a GPU that cannot do it fails the
                    // whole allocation, and the screen then draws an empty frame with no error the player
                    // could report beyond "the model is gone".
                    _rt.antiAliasing = SystemInfo.supportsMultisampledTextures != 0 ? 2 : 1;
                    _rt.useMipMap = false;
                    _rt.filterMode = FilterMode.Bilinear;
                    _rt.Create();
                }
                // Android throws the contents away whenever the app loses its graphics context — coming
                // back from a rewarded ad is enough. The camera would keep rendering into a dead handle.
                else if (!_rt.IsCreated()) _rt.Create();
                return _rt;
            }
        }

        private void Awake()
        {
            Zoom = 1f;
        }

        private void OnEnable()
        {
            _clockSearched = false;
            RenderPipelineManager.beginCameraRendering += BeginPreviewCamera;
            RenderPipelineManager.endCameraRendering += EndPreviewCamera;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginPreviewCamera;
            RenderPipelineManager.endCameraRendering -= EndPreviewCamera;
            ReleaseDaylight();
        }

        /// <summary>
        /// The studio stands in the island's world and is lit by the island's sky, so after dark the
        /// building the player is being asked to buy was being photographed by moonlight. There is no
        /// second light to switch on — the island's shader reads the main light and the ambient probe
        /// and nothing else — so the clock is turned back to noon for this camera's pass and handed
        /// the night again the moment it is done. Everything outside the frame, the island behind the
        /// panel included, is still at whatever time it actually is.
        ///
        /// This runs before URP culls, which is what makes it work at all: the sun's colour, angle
        /// and intensity are read out of the cull results, and the shader globals are read at draw
        /// time. Both are already daylight by then.
        /// </summary>
        private void BeginPreviewCamera(ScriptableRenderContext context, Camera cam)
        {
            if (cam != _cam || _holdingDaylight) return;
            if (!_clockSearched)
            {
                _clockSearched = true;
                _clock = FindAnyObjectByType<DayNightCycle>();
            }
            if (_clock == null) return;      // a scene with no clock in it is always daylight already
            _holdingDaylight = true;
            _clock.HoldDaylight(true);
        }

        private void EndPreviewCamera(ScriptableRenderContext context, Camera cam)
        {
            if (cam != _cam) return;
            ReleaseDaylight();
        }

        private void ReleaseDaylight()
        {
            if (!_holdingDaylight) return;
            _holdingDaylight = false;
            if (_clock != null) _clock.HoldDaylight(false);
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

            Bounds framed = Framed(districtBounds);
            _radius = Mathf.Max(0.5f, framed.extents.magnitude);
            // The table is turning the whole time, so the shot has to hold at both ends of the sway as
            // well as at rest — a fit solved only for the resting angle lets the corners of a long
            // building swing out through the sides of the frame.
            _distance = Mathf.Max(FitDistance(framed, yaw),
                        Mathf.Max(FitDistance(framed, yaw - swayDegrees),
                                  FitDistance(framed, yaw + swayDegrees)));
            // A district that needed a long dolly must not fall out the back of the frustum. The backdrop
            // rides the camera at 900, so the plane can only ever move outward from there.
            _cam.farClipPlane = Mathf.Max(1400f, _distance * 1.6f);
            return holder.transform;
        }

        /// <summary>
        /// The part of the district the camera is asked to hold. Everything on this island is a
        /// building except the railway, which is a quarter of a kilometre of track; fitting that whole
        /// length into a 4:3 frame drew a hairline with two dots on it. Past <see cref="maxSpan"/> the
        /// shot is taken on the middle of the district at building scale and the ends run out of frame,
        /// which is what a model of a railway is supposed to look like.
        /// </summary>
        private Bounds Framed(Bounds b)
        {
            if (maxSpan <= 0f) return b;
            Vector3 s = b.size;
            if (s.x <= maxSpan && s.z <= maxSpan) return b;
            return new Bounds(b.center, new Vector3(Mathf.Min(s.x, maxSpan), s.y, Mathf.Min(s.z, maxSpan)));
        }

        /// <summary>
        /// How far back the camera has to stand for this building to fill the frame.
        ///
        /// Solved per corner: each one has to sit inside both the horizontal and the vertical frustum
        /// at the dolly distance, and the answer is the largest demand any of the eight makes. The old
        /// version took the widest corner and the deepest corner and added them, which is only the same
        /// number when they are the same corner — on anything longer than it is wide it stood the
        /// camera back by half the length for nothing, and the building came out a stamp in the middle
        /// of the frame.
        /// </summary>
        private float FitDistance(Bounds b, float atYaw)
        {
            Quaternion inv = Quaternion.Inverse(Quaternion.Euler(pitch, atYaw, 0f));
            float vTan = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad) * Mathf.Max(0.05f, fill);
            float hTan = vTan * ((float)textureWidth / Mathf.Max(1, textureHeight));

            Vector3 e = b.extents;
            float dist = 1f;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        Vector3 local = inv * new Vector3(e.x * sx, e.y * sy, e.z * sz);
                        float needH = Mathf.Abs(local.x) / hTan - local.z;
                        float needV = Mathf.Abs(local.y) / vTan - local.z;
                        if (needH > dist) dist = needH;
                        if (needV > dist) dist = needV;
                    }
            return dist;
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
            // A pass that began and never ended — a camera whose render was skipped after the begin
            // callback — would otherwise leave the whole island in daylight. Runs before rendering.
            ReleaseDaylight();

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
