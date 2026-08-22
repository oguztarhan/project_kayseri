using Game.Gameplay;
using Game.Systems;
using TMPro;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// Stands one big roadside-style billboard in the shallows off the live island, carrying the
    /// island's name — the player should be able to read where they are off the map itself,
    /// without opening the world map to check.
    ///
    /// Nothing is authored. The board is primitives on the island's own toon shader, so the sun,
    /// the dusk and the night grade it exactly like the map around it. The name goes through the
    /// same localisation row the world map uses, and the stripe under the lettering is the
    /// island's ore colour — the one colour the game has already taught the player to read (map
    /// cards, yard tinting). After dark the lettering lights up the way the building signs do,
    /// and the stripe glows through the same shader global as the street lamps, so the sign
    /// follows dusk with no clock of its own.
    ///
    /// It stands past the seaward edge of the playable art, on the far side from the camera, so
    /// it reads across the whole island like a skyline sign and never covers anything playable.
    /// Travelling re-binds on a slow poll, the same contract as <see cref="StreetLamps"/>; the
    /// sign is parented under the island's own root, so it vanishes with its island the moment
    /// the player leaves, and the poll raises the next island's sign in its place.
    /// </summary>
    public sealed class IslandBillboard : MonoBehaviour
    {
        private static readonly int NightId = Shader.PropertyToID("_KayseriNight");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int VertexAmountId = Shader.PropertyToID("_VertexColorAmount");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        [Header("Yazı")]
        [Tooltip("Tabelanın yazı tipi. Baloo2-ExtraBold SDF.")]
        [SerializeField] private TMP_FontAsset _font;

        [Header("Yerleşim (dünya birimi)")]
        // 265, not the island bounds: the bounds are the 736-unit sea tile, and the playable art
        // ends about 210 units out, so a fixed reach lands the sign in the one strip of open water
        // that every island is guaranteed to have.
        [Tooltip("Ada merkezinden tabelaya uzaklık. Kara yaklaşık 210 birimde bitiyor.")]
        [SerializeField] private float _fromCenter = 265f;
        [Tooltip("Direklerin dikildiği taban yüksekliği. Deniz yüzeyi yaklaşık 3'te.")]
        [SerializeField] private float _baseY = 2.5f;

        [Header("Boyut (dünya birimi)")]
        [Tooltip("Panonun genişliği.")]
        [SerializeField] private float _width = 132f;
        [Tooltip("Panonun yüksekliği.")]
        [SerializeField] private float _height = 34f;
        [Tooltip("Pano alt kenarının tabandan yüksekliği.")]
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

        [Header("Bağlanma")]
        [Tooltip("Hangi adanın canlı olduğuna bu sıklıkta bakılır.")]
        [SerializeField] private float _rebindSeconds = 0.5f;

        private WorldIslands _world;
        private int _builtFor = -1;
        private float _rebindIn;
        private float _appliedNight = -1f;

        private Transform _sign;
        private Material _plateMaterial;
        private Material _postMaterial;
        private Material _stripeMaterial;
        private TextMeshPro _label;
        private Material _labelMaterial;

        private void Update()
        {
            Rebind();
            if (_label != null) Paint(Shader.GetGlobalFloat(NightId));
        }

        private void OnDestroy() => Clear();

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

            _sign = new GameObject("AdaTabelasi").transform;
            _sign.SetParent(root.transform, false);
            // Local +Z points out to sea, so the readable face of everything below is the camera
            // side. Parented under the root, but placed in world space: the roots stand at scale
            // one, and the sign must not inherit whatever offset the island was tiled out at.
            _sign.SetPositionAndRotation(
                new Vector3(bounds.center.x, _baseY, bounds.center.z) + side * _fromCenter,
                Quaternion.LookRotation(side));

            _postMaterial = Instance(shader, _postColor, Color.black);
            _plateMaterial = Instance(shader, _dayPlate, Color.black);
            Color ore = _world.BrandColor(island);   // a label, not a mineral sample
            // The stripe glows its ore colour after dark through the lamps' own emission global.
            _stripeMaterial = Instance(shader, ore, ore * 1.4f);

            float postHeight = _clearance + _height * 0.55f;
            Box(_sign, "Direk_Sol", new Vector3(-_width * 0.36f, postHeight * 0.5f, 0f),
                new Vector3(2.6f, postHeight, 2.6f), _postMaterial);
            Box(_sign, "Direk_Sag", new Vector3(_width * 0.36f, postHeight * 0.5f, 0f),
                new Vector3(2.6f, postHeight, 2.6f), _postMaterial);

            // Leaning the board back turns its face up toward the high camera.
            var panel = new GameObject("Pano").transform;
            panel.SetParent(_sign, false);
            panel.localPosition = new Vector3(0f, _clearance, 0f);
            panel.localRotation = Quaternion.Euler(_tilt, 0f, 0f);

            Box(panel, "Levha", new Vector3(0f, _height * 0.5f, 0f),
                new Vector3(_width, _height, 1.4f), _plateMaterial);
            Box(panel, "Serit", new Vector3(0f, _height * 0.10f, -1.0f),
                new Vector3(_width * 0.98f, _height * 0.13f, 0.5f), _stripeMaterial);

            BuildLabel(panel, island);

            _appliedNight = -1f;
            return true;
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
            _sign = null;
            _label = null;
            _labelMaterial = null;   // TMP owns its instance and frees it with the label
        }
    }
}
