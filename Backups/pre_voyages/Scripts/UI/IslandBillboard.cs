using Game.Core;
using Game.Gameplay;
using Game.Systems;
using TMPro;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// Stands the island's signage out on the open water off the live island: one big roadside-style
    /// board carrying the island's name — the player should be able to read where they are off the map
    /// itself, without opening the world map to check — and, beside it, a small cross-promo board for
    /// our other game.
    ///
    /// Nothing is authored. The boards are primitives on the island's own toon shader, so the sun,
    /// the dusk and the night grade them exactly like the map around them. The name goes through the
    /// same localisation row the world map uses, and the stripe under the lettering is the island's
    /// ore colour — the one colour the game has already taught the player to read (map cards, yard
    /// tinting). After dark the lettering lights up the way the building signs do, and the stripe
    /// glows through the same shader global as the street lamps, so the sign follows dusk with no
    /// clock of its own.
    ///
    /// WHERE IT STANDS is measured, not assumed. It used to reach a fixed 265 units from the island
    /// centre, on the reasoning that the playable art ends around 210 — but the coastal rock ring is
    /// still 60 to 85 units tall out there on all eight maps, so the board was buried in a mountain
    /// with only whatever poked out the far face visible. The land is measured off renderer bounds
    /// instead and the sign is stood clear of it, in the open sea past the shore, where nothing on any
    /// island can grow into it. The water surface is measured too: Coal's sea sits about six units
    /// below the seven derived islands', so the height the posts start at cannot be a constant either.
    ///
    /// Travelling re-binds on a slow poll, the same contract as <see cref="StreetLamps"/>; the sign
    /// is parented under the island's own root, so it vanishes with its island the moment the player
    /// leaves, and the poll raises the next island's sign in its place.
    /// </summary>
    public sealed class IslandBillboard : MonoBehaviour
    {
        private static readonly int NightId = Shader.PropertyToID("_KayseriNight");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int VertexAmountId = Shader.PropertyToID("_VertexColorAmount");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        [Header("Yazı")]
        [Tooltip("Tabelanın yazı tipi. Baloo2-ExtraBold SDF.")]
        [SerializeField] private TMP_FontAsset _font;

        [Header("Yerleşim (dünya birimi)")]
        [Tooltip("Karanın bittiği yerden tabelaya kalan açık deniz payı. Kara kenarı her ada için " +
                 "ölçülür; sekizinde de yaklaşık 320 birimde bitiyor.")]
        [SerializeField] private float _clearWater = 60f;

        [Header("Boyut (dünya birimi)")]
        [Tooltip("Panonun genişliği.")]
        [SerializeField] private float _width = 132f;
        [Tooltip("Panonun yüksekliği.")]
        [SerializeField] private float _height = 34f;
        [Tooltip("Pano alt kenarının su yüzeyinden yüksekliği.")]
        [SerializeField] private float _clearance = 20f;
        [Tooltip("Panonun yüksekten bakan kameraya dönmek için geriye yatma açısı (derece).")]
        [SerializeField] private float _tilt = 24f;

        [Header("Gündüz")]
        [SerializeField] private Color _dayPlate = new Color(0.97f, 0.95f, 0.90f, 1f);
        [SerializeField] private Color _dayText = new Color(0.16f, 0.15f, 0.19f, 1f);

        [Header("Gece")]
        [SerializeField] private Color _nightPlate = new Color(0.09f, 0.08f, 0.12f, 1f);
        [Tooltip("Gece yanan yazı rengi. Sokak lambalarıyla aynı sarıya yakın durmalı.")]
        [SerializeField] private Color _nightText = new Color(1f, 0.83f, 0.50f, 1f);

        [Tooltip("Direklerin rengi.")]
        [SerializeField] private Color _postColor = new Color(0.32f, 0.33f, 0.37f, 1f);

        [Header("Reklam panosu (Bus Jam Traffic Rush)")]
        [Tooltip("Reklam görseli. Boş bırakılırsa reklam panosu hiç kurulmaz.")]
        [SerializeField] private Texture _adArt;
        [Tooltip("Reklam panosunun genişliği. Ada tabelasının yanında, ondan küçük durmalı.")]
        [SerializeField] private float _adWidth = 68f;
        [Tooltip("Reklam panosunun yüksekliği. Görsel 1024x500 olduğu için oran ~2:1 tutulmalı.")]
        [SerializeField] private float _adHeight = 34f;
        [Tooltip("Görselin çerçevesi.")]
        [SerializeField] private Color _adFrame = new Color(0.13f, 0.12f, 0.16f, 1f);

        [Header("Bağlanma")]
        [Tooltip("Hangi adanın canlı olduğuna bu sıklıkta bakılır.")]
        [SerializeField] private float _rebindSeconds = 0.5f;

        private WorldIslands _world;
        private int _builtFor = -1;
        private float _rebindIn;
        private LocalizationService _loc;
        private float _appliedNight = -1f;

        private Transform _sign;
        private Material _plateMaterial;
        private Material _postMaterial;
        private Material _stripeMaterial;
        private Material _adFrameMaterial;
        private Material _posterMaterial;
        private TextMeshPro _label;
        private Material _labelMaterial;

        private void Awake()
        {
            _loc = ServiceLocator.Get<LocalizationService>();
            if (_loc != null) _loc.Changed += OnLanguageChanged;
        }

        private void Update()
        {
            Rebind();
            if (_label != null) Paint(Shader.GetGlobalFloat(NightId));
        }

        private void OnDestroy()
        {
            if (_loc != null) _loc.Changed -= OnLanguageChanged;
            Clear();
        }

        /// <summary>
        /// Ada adı tabelaya kuruluşta yazılıyor ve <see cref="Update"/> yalnız rengi tazeliyor, yani
        /// dil değişince levha eski dilde kalırdı. Kurulu adayı unutmak <see cref="Rebind"/>'in
        /// ada değiştiğinde zaten yürüdüğü yolu açar.
        /// </summary>
        private void OnLanguageChanged()
        {
            _builtFor = -1;
            _rebindIn = 0f;
        }

        /// <summary>Travelling activates a different island root, so the binding is re-checked on
        /// a slow timer rather than taken once — the same contract as the street lamps.</summary>
        private void Rebind()
        {
            _rebindIn -= Time.unscaledDeltaTime;
            if (_rebindIn > 0f) return;
            _rebindIn = Mathf.Max(0.1f, _rebindSeconds);

            if (_world == null) _world = FindAnyObjectByType<WorldIslands>();
            if (_world == null) return;
            if (_world.ActiveIndex == _builtFor && _sign != null) return;

            _builtFor = Build(_world.ActiveIndex) ? _world.ActiveIndex : -1;
        }

        private bool Build(int island)
        {
            Clear();

            GameObject root = FindRoot(_world.RootName(island));
            if (root == null) return false;

            var shader = Shader.Find("Kayseri/IslandVertexLit");
            if (shader == null) return false;

            var renderers = root.GetComponentsInChildren<Renderer>(false);
            if (renderers.Length == 0) return false;
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            // The camera orbit is fixed and the islands are square, so "the far side of the map"
            // is the camera's own look direction, flattened and snapped to the nearer map axis.
            var camera = Camera.main;
            Vector3 look = camera != null ? camera.transform.forward : Vector3.right;
            Vector3 side = Mathf.Abs(look.x) >= Mathf.Abs(look.z)
                ? new Vector3(Mathf.Sign(look.x), 0f, 0f)
                : new Vector3(0f, 0f, Mathf.Sign(look.z));
            Vector3 across = Vector3.Cross(Vector3.up, side);

            // The ad board stands to one side of the name board, so the strip of coast the rig has to
            // clear is wider than the name board alone.
            float adOffset = _adArt != null ? (_width + _adWidth) * 0.5f + 26f : 0f;
            float halfSpan = Mathf.Max(_width * 0.5f, adOffset + _adWidth * 0.5f);

            float sea = SeaLevel(renderers);
            float reach = LandEdge(renderers, bounds.center, side, across, sea, halfSpan + 20f)
                        + _clearWater;

            _sign = new GameObject("AdaTabelasi").transform;
            _sign.SetParent(root.transform, false);
            // Local +Z points out to sea, so the readable face of everything below is the camera
            // side, and local +X runs along the shore. Parented under the root, but placed in world
            // space: the roots stand at scale one, and the sign must not inherit whatever offset the
            // island was tiled out at. The posts start just under the surface so the sea meets them
            // rather than a gap showing beneath.
            _sign.SetPositionAndRotation(
                new Vector3(bounds.center.x, sea - 2f, bounds.center.z) + side * reach,
                Quaternion.LookRotation(side));

            _postMaterial = Instance(shader, _postColor, Color.black);
            _plateMaterial = Instance(shader, _dayPlate, Color.black);
            Color ore = _world.BrandColor(island);   // a label, not a mineral sample
            // The stripe glows its ore colour after dark through the lamps' own emission global.
            _stripeMaterial = Instance(shader, ore, ore * 1.4f);

            // Leaning the board back turns its face up toward the high camera.
            var panel = new GameObject("Pano").transform;
            panel.SetParent(_sign, false);
            panel.localPosition = new Vector3(0f, _clearance, 0f);
            panel.localRotation = Quaternion.Euler(_tilt, 0f, 0f);

            Box(panel, "Levha", new Vector3(0f, _height * 0.5f, 0f),
                new Vector3(_width, _height, 1.4f), _plateMaterial);
            Box(panel, "Serit", new Vector3(0f, _height * 0.10f, -1.0f),
                new Vector3(_width * 0.98f, _height * 0.13f, 0.5f), _stripeMaterial);

            Legs(panel, "Tabela", _width, _height, 2.6f);
            BuildLabel(panel, island);
            if (_adArt != null) BuildAd(shader, adOffset);

            _appliedNight = -1f;
            return true;
        }

        /// <summary>
        /// The water surface, taken as the top of the island's single widest flat plate — the sea tile,
        /// which is four times the width of anything else on the map and twenty times its footprint.
        /// This cannot be a constant: Coal's sea sits at about -3 and the seven derived islands' at
        /// about +3, so a fixed base height either floats the posts or sinks the board.
        /// </summary>
        private static float SeaLevel(Renderer[] renderers)
        {
            float widest = 0f, top = 0f;
            for (int i = 0; i < renderers.Length; i++)
            {
                Bounds b = renderers[i].bounds;
                float footprint = b.size.x * b.size.z;
                if (footprint <= widest) continue;
                widest = footprint;
                top = b.max.y;
            }
            return top;
        }

        /// <summary>
        /// How far the island's land reaches along <paramref name="side"/>, inside the strip the sign
        /// stands in. Anything whose top is at the water is the sea plate itself or its foam ring, not
        /// land. Measured off renderer bounds only — no vertex reads — so a whole island is one pass
        /// over the ~900 renderers of the live phase, on the travel frame that rebuilds the sign and
        /// nowhere else.
        /// </summary>
        private static float LandEdge(Renderer[] renderers, Vector3 centre, Vector3 side,
                                      Vector3 across, float sea, float bandHalf)
        {
            float edge = 0f;
            // Both axes are snapped to world X or Z, so an extent along either is one bounds component.
            bool sideIsX = Mathf.Abs(side.x) > 0.5f;
            for (int i = 0; i < renderers.Length; i++)
            {
                Bounds b = renderers[i].bounds;
                if (b.max.y <= sea + 3f) continue;
                Vector3 offset = b.center - centre;
                float halfAcross = sideIsX ? b.extents.z : b.extents.x;
                if (Mathf.Abs(Vector3.Dot(offset, across)) - halfAcross > bandHalf) continue;
                float reach = Vector3.Dot(offset, side) + (sideIsX ? b.extents.x : b.extents.z);
                if (reach > edge) edge = reach;
            }
            return edge;
        }

        /// <summary>The island's root object, taken off the scene roots the same way
        /// <see cref="WorldIslands"/> flips them — a plain Find could be fooled by some nested
        /// object sharing the root's name.</summary>
        private GameObject FindRoot(string rootName)
        {
            var roots = gameObject.scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                if (roots[i].name == rootName && roots[i].activeInHierarchy) return roots[i];
            return null;
        }

        /// <summary>A one-off material on the island's own toon shader, so the board is graded by
        /// the sun, the dusk and the night exactly like everything around it. Vertex colour is
        /// switched off because a primitive cube carries none worth reading.</summary>
        private static Material Instance(Shader shader, Color color, Color emission)
        {
            var material = new Material(shader);
            material.SetColor(BaseColorId, color);
            material.SetFloat(VertexAmountId, 0f);
            material.SetColor(EmissionId, emission);
            return material;
        }

        /// <summary>
        /// The legs and back brace for one board, hung off the TILTED panel rather than stood under it.
        ///
        /// They used to be plain vertical posts running up from the water to just over half the board's
        /// height, which works on an upright sign and comes apart on a leaning one: the board tips away
        /// from the camera as it rises, so every unit of post above the bottom edge finished further in
        /// FRONT of the face than the last, and the top of each post crossed the artwork. Raking them
        /// with the panel is what a real roadside board does — the legs lie flat against the back, the
        /// brace ties them together, and nothing crosses the face from any angle the camera can reach.
        ///
        /// Everything here is in PANEL space, so the waterline is wherever the rake puts it rather than
        /// a drop measured straight down; the sign's own origin already sits two units under the
        /// surface, which is what stops a gap showing where the legs enter the sea.
        /// </summary>
        private void Legs(Transform panel, string prefix, float boardWidth, float boardHeight,
                          float thickness)
        {
            float radians = _tilt * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians), cos = Mathf.Max(0.2f, Mathf.Cos(radians));
            float back = 0.7f + thickness * 0.5f;          // clear of the plate's own 1.4 of depth
            float foot = (back * sin - _clearance) / cos;  // panel height that lands on the water
            float top = boardHeight * 0.55f;               // stops well inside the board's silhouette
            float span = boardWidth * 0.35f;

            Box(panel, prefix + "_Direk_Sol", new Vector3(-span, (top + foot) * 0.5f, back),
                new Vector3(thickness, top - foot, thickness), _postMaterial);
            Box(panel, prefix + "_Direk_Sag", new Vector3(span, (top + foot) * 0.5f, back),
                new Vector3(thickness, top - foot, thickness), _postMaterial);
            Box(panel, prefix + "_Kusak", new Vector3(0f, top - thickness, back),
                new Vector3(span * 2f, thickness * 0.7f, thickness * 0.7f), _postMaterial);
        }

        /// <summary>A shadowless, colliderless cube. The sign stands in open water where nothing
        /// can receive its shadow but the sea, and at the dusk sun angles a board this size would
        /// sweep a shadow across half the map.</summary>
        private static void Box(Transform parent, string boxName, Vector3 position, Vector3 scale,
                                Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.name = boxName;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void BuildLabel(Transform panel, int island)
        {
            var go = new GameObject("Yazi");
            go.transform.SetParent(panel, false);
            go.transform.localPosition = new Vector3(0f, _height * 0.56f, -0.95f);

            _label = go.AddComponent<TextMeshPro>();
            if (_font != null) _label.font = _font;
            _label.text = Loc.Id("ada", _world.IslandKey(island));
            _label.alignment = TextAlignmentOptions.Center;
            _label.textWrappingMode = TextWrappingModes.NoWrap;
            // Sized by the rect, not by a font size: the names are localised and "ZÜMRÜT ADASI"
            // and its translations are not the same width.
            ((RectTransform)go.transform).sizeDelta = new Vector2(_width * 0.92f, _height * 0.72f);
            _label.enableAutoSizing = true;
            _label.fontSizeMin = 8f;
            _label.fontSizeMax = 400f;
            _label.raycastTarget = false;
            go.GetComponent<MeshRenderer>().shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;

            // One instance, cached — and the glow keyword has to be switched on by hand, because
            // the SDF shader compiles the whole feature out without it and the night lighting
            // below would silently do nothing.
            _labelMaterial = _label.fontMaterial;
            _labelMaterial.EnableKeyword("GLOW_ON");
        }

        /// <summary>
        /// The cross-promo board beside the name sign: our other game's store art on a smaller plate,
        /// same posts, same lean, same stretch of open water.
        ///
        /// The art is UNLIT on purpose, and it is the one thing out here that is. A hoarding is a
        /// lightbox — it is lit from inside, not by the island's sun — so grading it with everything
        /// else would take the artwork down with the dusk and leave an unreadable grey rectangle for
        /// half the day/night cycle. Unlit also means the store art arrives on screen as the store art,
        /// with the toon ramp not repainting somebody else's key colours.
        /// </summary>
        private void BuildAd(Shader shader, float offset)
        {
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null) return;

            var board = new GameObject("ReklamPanosu").transform;
            board.SetParent(_sign, false);
            board.localPosition = new Vector3(offset, 0f, 0f);

            _adFrameMaterial = Instance(shader, _adFrame, Color.black);

            var panel = new GameObject("Pano").transform;
            panel.SetParent(board, false);
            panel.localPosition = new Vector3(0f, _clearance, 0f);
            panel.localRotation = Quaternion.Euler(_tilt, 0f, 0f);

            Box(panel, "Reklam_Levha", new Vector3(0f, _adHeight * 0.5f, 0f),
                new Vector3(_adWidth, _adHeight, 1.4f), _adFrameMaterial);

            Legs(panel, "Reklam", _adWidth, _adHeight, 2.2f);

            _posterMaterial = new Material(unlit);
            _posterMaterial.SetTexture(BaseMapId, _adArt);

            // The plate is a frame and the art is FITTED inside it at its own aspect, rather than
            // stretched to whatever width and height were typed above. Store art is delivered at a
            // fixed ratio and cropping or squashing somebody's key art to fit a billboard is the one
            // thing a cross-promo board must not do.
            const float Frame = 3.5f;
            float aspect = (float)_adArt.width / Mathf.Max(1, _adArt.height);
            float artWidth = Mathf.Min(_adWidth - Frame, (_adHeight - Frame) * aspect);

            // Unity's Quad faces its own -Z, which is already the camera side of the rig, so it is
            // hung unrotated — turning it to face the camera would only mirror the artwork.
            var poster = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(poster.GetComponent<Collider>());
            poster.name = "Reklam_Gorsel";
            poster.transform.SetParent(panel, false);
            poster.transform.localPosition = new Vector3(0f, _adHeight * 0.5f, -0.76f);
            poster.transform.localScale = new Vector3(artWidth, artWidth / aspect, 1f);
            var renderer = poster.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _posterMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Day to night, off the same global the shaders read. Only touched when the
        /// value actually moves — outside a crossfade this is one float compare.</summary>
        private void Paint(float night)
        {
            if (Mathf.Abs(night - _appliedNight) < 0.002f) return;
            _appliedNight = night;

            _plateMaterial.SetColor(BaseColorId, Color.Lerp(_dayPlate, _nightPlate, night));

            var text = Color.Lerp(_dayText, _nightText, night);
            _label.color = text;
            // A lit sign is not just a brighter colour, it spills. The glow rides the night value
            // so the lettering comes up with the lamps rather than snapping on at dusk.
            _labelMaterial.SetColor(ShaderUtilities.ID_GlowColor, text);
            _labelMaterial.SetFloat(ShaderUtilities.ID_GlowPower, night * 0.75f);
            _labelMaterial.SetFloat(ShaderUtilities.ID_GlowOuter, night * 0.4f);
        }

        private void Clear()
        {
            if (_sign != null) Destroy(_sign.gameObject);
            if (_plateMaterial != null) Destroy(_plateMaterial);
            if (_postMaterial != null) Destroy(_postMaterial);
            if (_stripeMaterial != null) Destroy(_stripeMaterial);
            if (_adFrameMaterial != null) Destroy(_adFrameMaterial);
            if (_posterMaterial != null) Destroy(_posterMaterial);
            _sign = null;
            _label = null;
            _labelMaterial = null;   // TMP owns its instance and frees it with the label
        }
    }
}
