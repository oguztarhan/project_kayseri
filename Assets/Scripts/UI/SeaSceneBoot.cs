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
        private Transform _helm;          // the captain at the wheel; hidden when nobody is aboard
        private Renderer _helmCoat;       // his coat, tinted with his grade
        private CaptainService _roster;
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

            // Who is at the wheel. Re-read on roster changes rather than per frame: the only thing
            // that can move it is a crate opened or a level bought, and both raise Changed.
            _roster = ServiceLocator.Get<CaptainService>();
            if (_roster != null) _roster.Changed += RefreshHelm;
            RefreshHelm();
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

            BuildHelm(hull.transform);
            return root;
        }

        /// <summary>
        /// The wheel on the cabin roof, and the captain standing at it.
        ///
        /// SHE USED TO SAIL HERSELF. The roster let you level five named officers and the only place
        /// any of them appeared was a card on a menu — the masters at least stand on the island beside
        /// the station they run. A captain you levelled should be visible doing the job you levelled
        /// him for, and the helm is that job.
        ///
        /// BUILT FROM PRIMITIVES, like the rest of this scene. The island uses rigged bodies out of a
        /// people pack, but the sea scene has no pack wired and every other thing out here is a cube
        /// or a cylinder — a skinned character at the wheel of a box would read as a bug rather than
        /// as detail. He is a body, a head and a cap, and his COAT carries his grade colour, which is
        /// the same colour his card is drawn in (CaptainService.GradeTint).
        ///
        /// Built once and only ever shown, hidden or re-tinted, so a voyage with nobody aboard costs a
        /// SetActive and nothing else.
        /// </summary>
        private void BuildHelm(Transform hull)
        {
            // Cabin roof: Kamara sits at y 4.4 with a height of 3.6, so its top is 6.2.
            const float roof = 6.2f;

            var wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            wheel.name = "Dumen";
            wheel.transform.SetParent(hull, false);
            // A cylinder stands on Y; rolled a quarter turn about X it becomes a disc facing forward,
            // which is a ship's wheel seen from behind.
            wheel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            wheel.transform.localScale = new Vector3(2.2f, 0.16f, 2.2f);
            wheel.transform.localPosition = new Vector3(0f, roof + 1.5f, 0.9f);
            Paint(wheel, new Color(0.45f, 0.31f, 0.18f, 1f));

            _helm = new GameObject("Kaptan").transform;
            _helm.SetParent(hull, false);
            _helm.localPosition = new Vector3(0f, roof, -0.5f);
            // Deliberately over-scale. Built at true proportion he is about a tenth of a 19-unit hull
            // and disappears at the distance the sea camera actually sits; this is the size at which
            // you can tell somebody is up there without him looking like a giant.
            _helm.localScale = Vector3.one * 0.7f;

            var coat = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            coat.name = "Govde";
            coat.transform.SetParent(_helm, false);
            coat.transform.localScale = new Vector3(1.5f, 1.7f, 1.5f);
            coat.transform.localPosition = new Vector3(0f, 1.7f, 0f);
            _helmCoat = coat.GetComponent<Renderer>();
            Paint(coat, Color.white);          // re-tinted per captain by RefreshHelm

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Bas";
            head.transform.SetParent(_helm, false);
            head.transform.localScale = new Vector3(0.95f, 0.95f, 0.95f);
            head.transform.localPosition = new Vector3(0f, 3.5f, 0f);
            Paint(head, new Color(0.85f, 0.70f, 0.58f, 1f));

            var cap = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cap.name = "Kasket";
            cap.transform.SetParent(_helm, false);
            cap.transform.localScale = new Vector3(1.05f, 0.18f, 1.05f);
            cap.transform.localPosition = new Vector3(0f, 4.0f, 0f);
            Paint(cap, new Color(0.16f, 0.18f, 0.24f, 1f));

            _helm.gameObject.SetActive(false);
        }

        /// <summary>
        /// Puts whoever is aboard at the wheel, or empties it. <see cref="ExpeditionService"/> already
        /// decides who that is — the best-levelled captain the player owns — so this only has to draw
        /// the answer. Called on Start and whenever the roster moves; nothing here runs per frame.
        /// </summary>
        private void RefreshHelm()
        {
            if (_helm == null) return;

            int captain = _sea != null ? _sea.CaptainAboard : -1;
            bool aboard = Captains.Exists(captain);
            if (_helm.gameObject.activeSelf != aboard) _helm.gameObject.SetActive(aboard);
            if (!aboard) return;

            Color coat = _roster != null ? _roster.GradeTintOf(captain)
                                         : new Color(0.48f, 0.54f, 0.62f, 1f);
            if (_helmCoat != null) _helmCoat.sharedMaterial = MarketYardBuild.Mat(coat);
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
            if (!SceneCurtain.Cover(SceneCurtain.HomeScene(homeSceneName), HomeTint(), caption, false)) return;
            _leaving = true;
            _sea?.Ashore();
        }

        /// <summary>Any other way out — a hot reload, or a path that swaps the scene without the button.</summary>
        private void OnDestroy()
        {
            if (_roster != null) _roster.Changed -= RefreshHelm;
            if (_leaving) return;
            _sea?.Ashore();
        }
    }
}
