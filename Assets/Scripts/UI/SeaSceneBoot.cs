using Game.Core;
using Game.Gameplay;
using Game.Systems;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// The whole of the sea scene, assembled on load: the water, the lane, the ship, a camera and a
    /// four-line HUD. The scene asset holds one object carrying this and nothing else — the same
    /// shape as <see cref="MarketSceneBoot"/>, and Docs/VOYAGES.md §20 records why that shape is
    /// worth keeping: a scene built in code needs no Unity bridge to change.
    ///
    /// It lives in <c>Game.UI</c> for the reason MarketSceneBoot gives — it is the only assembly that
    /// can see all three of the things it has to put together.
    ///
    /// IT OWNS THE FRAME AND THE BOAT DOES NOT. <see cref="PlayerShip"/> is placed from here every
    /// frame rather than reading the service itself. One owner per scene, and the boat is not it —
    /// which also means there is exactly one place where the voyage's clock becomes a position, and
    /// exactly one place S2 has to hook into to make an encounter hold the ship still.
    ///
    /// LEAVING IS ALWAYS ALLOWED. Nothing out here is a commitment — the ship is the player's own,
    /// moored at their island's port between trips — so the back button needs no confirmation:
    /// nothing is being abandoned.
    /// </summary>
    public sealed class SeaSceneBoot : MonoBehaviour
    {
        [Header("Sahneler")]
        [Tooltip("Karaya çıkınca dönülecek sahne — adanın kendisi. Ada Sea'nin altında park " +
                 "halinde beklediği için dönüş onu uyandırır, yeniden kurmaz.")]
        [SerializeField] private string homeSceneName = "Main";

        [Header("Işık")]
        [SerializeField] private Vector3 sunAngles = new Vector3(42f, 200f, 0f);
        [SerializeField] private Color sunColor = new Color(1f, 0.95f, 0.86f);
        [SerializeField, Min(0f)] private float sunIntensity = 1.2f;

        [Tooltip("Ortam ışığı. BOŞ BİR SAHNEDE HİÇ YOKTUR — gökyüzü de yok, ortam da yok — yani " +
                 "tek yönlü ışığın görmediği her yüzey simsiyah çıkar. İlk denemede liman ve " +
                 "şamandıralar öyle çıktı. Fazlası da yanlış: parlak bir ortam bütün malzemeleri " +
                 "yıkayıp sahneyi tek renkli bir camgöbeğine çevirdi.")]
        [SerializeField] private Color ambient = new Color(0.34f, 0.38f, 0.44f, 1f);

        [Header("Ufuk")]
        [Tooltip("Gökyüzü rengi ve sisin başladığı/bittiği uzaklık. Sis, su levhasının kenarını " +
                 "ufka karıştırıyor; olmadan deniz bir yerde bıçakla kesilmiş gibi bitiyor.")]
        [SerializeField] private Color sky = new Color(0.55f, 0.73f, 0.86f, 1f);
        [SerializeField, Min(0f)] private float fogStart = 700f;
        [SerializeField, Min(1f)] private float fogEnd = 2200f;

        private ExpeditionService _sea;
        private PlayerShip _ship;
        private SeaLane _lane;
        private SeaHudUI _hud;
        private bool _leaving;

        private void Start()
        {
            _sea = ServiceLocator.Get<ExpeditionService>();

            _lane = new GameObject("Rota").AddComponent<SeaLane>();
            _lane.transform.SetParent(transform, false);

            BuildSun();

            var world = new GameObject("Deniz").AddComponent<SeaScene>();
            world.transform.SetParent(transform, false);
            world.Build(_lane, HomeTint());

            _ship = BuildShip();
            _ship.Bind(_lane, _ship.transform.GetChild(0));

            var camera = new GameObject("DenizKamerasi").AddComponent<SeaCamera>();
            camera.transform.SetParent(transform, false);
            var cam = camera.gameObject.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = sky;
            cam.farClipPlane = 3000f;
            cam.fieldOfView = 40f;

            // Placed once before the camera latches on, so it does not spend its first second flying
            // in from the origin.
            Place(true);
            camera.Follow(_ship.Hull);

            _hud = new GameObject("DenizHud").AddComponent<SeaHudUI>();
            _hud.transform.SetParent(transform, false);
            _hud.Build(_sea, Ashore);

            // The fights, and their face — the fight is staged in 2D over the crossing (see
            // SeaFightUI), so the controller needs no lane and no hull, only the clock. Separate
            // from the HUD on purpose: SeaHudUI is the voyage's instruments and never learns a
            // fight exists.
            var fights = new GameObject("Karsilasmalar").AddComponent<EncounterController>();
            fights.transform.SetParent(transform, false);
            fights.Init();

            var fightUi = new GameObject("CarpismaHud").AddComponent<SeaFightUI>();
            fightUi.transform.SetParent(transform, false);
            fightUi.Build(fights);
        }

        private void Update()
        {
            if (_leaving) return;
            Place(false);
        }

        private void Place(bool snap)
        {
            if (_ship == null) return;
            float u = _sea != null ? (float)_sea.LanePosition : 0f;
            bool outbound = _sea == null || _sea.Outbound;
            _ship.Place(u, outbound, snap);
        }

        /// <summary>
        /// The hull: a body and a deckhouse, one nested inside a bob node.
        ///
        /// Three primitives, like the dock in Docs/VOYAGES.md §20 — and replaced the same way, once
        /// the crossing has been played and is worth modelling. <c>SM_Harbor_Launch.fbx</c> is already
        /// in the project and is the obvious candidate; wiring it needs a serialized slot and an
        /// Inspector pass, which is a decision for whoever does the art.
        /// </summary>
        private PlayerShip BuildShip()
        {
            var root = new GameObject("Gemi").AddComponent<PlayerShip>();
            root.transform.SetParent(transform, false);

            var hull = new GameObject("Govde");
            hull.transform.SetParent(root.transform, false);

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Tekne";
            body.transform.SetParent(hull.transform, false);
            body.transform.localScale = new Vector3(7.5f, 3.4f, 19f);
            body.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            Paint(body, new Color(0.62f, 0.24f, 0.18f, 1f));

            var deck = GameObject.CreatePrimitive(PrimitiveType.Cube);
            deck.name = "Kamara";
            deck.transform.SetParent(hull.transform, false);
            deck.transform.localScale = new Vector3(5.4f, 3.6f, 6.4f);
            deck.transform.localPosition = new Vector3(0f, 4.4f, -2.2f);
            Paint(deck, new Color(0.90f, 0.88f, 0.82f, 1f));

            var mast = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            mast.name = "Direk";
            mast.transform.SetParent(hull.transform, false);
            mast.transform.localScale = new Vector3(0.9f, 7.5f, 0.9f);
            mast.transform.localPosition = new Vector3(0f, 9f, 3.4f);
            Paint(mast, new Color(0.42f, 0.30f, 0.19f, 1f));

            return root;
        }

        private static void Paint(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = MarketYardBuild.Mat(c);
            Object.Destroy(go.GetComponent<Collider>());
        }

        /// <summary>The ore colour of the island she sailed from — the home port wears it, the way the
        /// market roofs and the map medallions already do.</summary>
        private Color HomeTint()
        {
            string key = _sea != null ? _sea.IslandKey : null;
            return string.IsNullOrEmpty(key) ? new Color(0.45f, 0.47f, 0.52f, 1f)
                                             : WorldIslands.OreColorFor(key);
        }

        private void BuildSun()
        {
            var go = new GameObject("Gunes");
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.Euler(sunAngles);
            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = sunColor;
            light.intensity = sunIntensity;
            light.shadows = LightShadows.None;   // nothing out here casts one worth the fill rate

            // An EmptyScene carries no lighting settings whatsoever, so this is not a preference —
            // without it every face turned away from the sun renders black, which is what the port
            // and the buoys did on the first run.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = ambient;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = sky;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
        }

        /// <summary>
        /// Back to the island. Guarded like MarketSceneBoot's Leave is, and for the same reason: the
        /// curtain answers on the first frame but the button is still a button.
        /// </summary>
        public void Ashore()
        {
            if (_leaving) return;
            // parkCurrent: false. The island is parked UNDER this scene, so the curtain wakes it
            // rather than loading anything — and parking the SEA on the way out would leave a whole
            // scene resident behind a screen the player has left.
            string key = _sea != null ? _sea.IslandKey : null;
            string caption = string.IsNullOrEmpty(key) ? Loc.T("deniz.rihtim") : Loc.Id("ada", key);
            if (!SceneCurtain.Cover(homeSceneName, HomeTint(), caption, false)) return;
            _leaving = true;
            _sea?.Ashore();
        }

        /// <summary>Any other way out — a hot reload, or a path that swaps the scene without the button.</summary>
        private void OnDestroy()
        {
            if (_leaving) return;
            _sea?.Ashore();
        }
    }
}
