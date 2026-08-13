using Game.Core;
using Game.Gameplay;
using Game.Systems;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.UI
{
    /// <summary>
    /// The whole of the market scene, assembled on load: the yard, a body to walk it with, a camera and
    /// the HUD. The scene asset holds one object carrying this and nothing else.
    ///
    /// It lives in <c>Game.UI</c> because it is the only assembly that can see all three of the things it
    /// has to put together — the yard and the player are <c>Game.Gameplay</c>, the HUD is here, and the
    /// ledger is <c>Game.Systems</c>. <see cref="LoadingScreen"/> made the opposite call for the opposite
    /// reason: the bootstrapper holds a reference to it, so it had to sit below.
    ///
    /// Nothing here is registered or persistent. Services were built in Bootstrap and outlived the island
    /// scene, so this asks <see cref="ServiceLocator"/> for them and hands them back untouched on the way
    /// out. The two things it does have to say are which yard the player is standing in, and that no
    /// island is simulating while they are here.
    /// </summary>
    public sealed class MarketSceneBoot : MonoBehaviour
    {
        [Header("Sahneler")]
        [Tooltip("Çıkışta dönülecek sahne. Hangi adaya döneceğini kayıttaki 'worldactive' belirler.")]
        [SerializeField] private string islandSceneName = "Main";

        [Header("Görseller")]
        [Tooltip("Avlunun canlıları ve eşyaları. Hepsi boş bırakılabilir — boş olan gri kutuya düşer, " +
                 "yani sahne yarı bağlıyken de çalışır.")]
        [SerializeField] private MarketPrefabs prefabs = new MarketPrefabs();

        [Header("Oyuncu")]
        [Tooltip("Gövdenin yarıçapı ve boyu, dünya birimi. Duvarlara bunlarla çarpar.")]
        [SerializeField, Min(0.1f)] private float bodyRadius = 0.65f;
        [SerializeField, Min(0.2f)] private float bodyHeight = 2.1f;

        [Header("Işık")]
        [Tooltip("Güneşin açısı. Avlu tek yönlü ışıkla aydınlanıyor, adadaki gibi.")]
        [SerializeField] private Vector3 sunAngles = new Vector3(48f, 35f, 0f);
        [SerializeField] private Color sunColor = new Color(1f, 0.96f, 0.89f);
        [SerializeField, Min(0f)] private float sunIntensity = 1.15f;

        private readonly System.Collections.Generic.List<MarketYardScene> _yards =
            new System.Collections.Generic.List<MarketYardScene>();
        private readonly System.Collections.Generic.List<Material> _tints =
            new System.Collections.Generic.List<Material>();

        private MarketService _market;
        private MarketHudUI _hud;
        private Transform _player;
        private CarryStack _carry;
        private Transform _playerBody;
        private string _yardKey;
        private float _checkIn;
        private bool _leaving;

        private void Start()
        {
            _market = ServiceLocator.Get<MarketService>();

            // Whose yard this is. The island scene is already gone, but the ledger outlived it and still
            // remembers which island was live — that is the one whose market the player just walked into.
            _yardKey = _market != null ? _market.ActiveIsland : null;
            if (string.IsNullOrEmpty(_yardKey)) _yardKey = "coal";

            // No island is simulating while the player is in here, so every yard — including this one —
            // is fed by the delivery rate its own lorries last managed. What the player does to this
            // yard is not a rate at all: it is bars off the pads, one at a time, by hand.
            if (_market != null) _market.SetActiveIsland(null);

            BuildSun();

            // The player is built before the yards because each of them wires itself to his body: the
            // cash floor needs someone to fly to, and the carry pad needs a back to lengthen.
            MarketPlayer player = BuildPlayer(Vector3.zero, MarketYardBuild.Mat(OreTint(_yardKey)));
            _carry = player.GetComponent<CarryStack>();

            BuildYards(player.transform);
            MarketYardScene arrival = YardFor(_yardKey) ?? (_yards.Count > 0 ? _yards[0] : null);
            if (arrival != null) { _yardKey = arrival.IslandKey; Teleport(player, arrival.PlayerStart); }

            MarketCamera camera = BuildCamera();
            camera.Follow(player.transform);
            player.SetCameraBasis(camera.transform);

            _player = player.transform;
            _hud = new GameObject("MarketHud").AddComponent<MarketHudUI>();
            _hud.transform.SetParent(transform, false);
            _hud.Build(player, _market, _yardKey, arrival != null ? arrival.Pads : null, Leave);

            // Price tags over every pad in the hall, not just this yard's — you should be able to read
            // what the copper cashier costs through the doorway before deciding to walk over.
            var labels = new GameObject("PedEtiketleri").AddComponent<YardPadLabels>();
            labels.transform.SetParent(transform, false);
            labels.Build(_market, AllPads());

            EnterYard(arrival);
        }

        /// <summary>
        /// One yard per island the player owns, shoulder to shoulder in ladder order.
        ///
        /// Packed rather than placed at their ladder index: the world map lets an island be bought out
        /// of order, and a hall with a hole in it where silver would go is a corridor of nothing to
        /// walk down. Owned yards are simply the next one along.
        /// </summary>
        private void BuildYards(Transform player)
        {
            if (_market == null) return;
            string[] ladder = WorldIslands.LadderKeys();

            var owned = new System.Collections.Generic.List<string>();
            for (int i = 0; i < ladder.Length; i++)
                if (_market.IsOwned(ladder[i])) owned.Add(ladder[i]);
            if (owned.Count == 0) owned.Add(ladder[0]);   // the home island, before anything is saved

            for (int i = 0; i < owned.Count; i++)
            {
                var go = new GameObject("Avlu_" + owned[i]);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(i * MarketYardBuild.Width, 0f, 0f);

                Color tint = OreTint(owned[i]);
                var yard = go.AddComponent<MarketYardScene>();
                yard.Build(_market, owned[i], tint, MarketYardBuild.Mat(tint),
                           westWall: i == 0, eastDoorway: i < owned.Count - 1, player, _carry, prefabs);
                _yards.Add(yard);
                _tints.Add(MarketYardBuild.Mat(tint));
            }
        }

        /// <summary>
        /// Which yard the player is standing in, checked as they walk rather than only on arrival —
        /// the doorway between two yards is a step, not a screen.
        /// </summary>
        private void Update()
        {
            if (_player == null || _yards.Count < 2) return;
            _checkIn -= Time.deltaTime;
            if (_checkIn > 0f) return;
            _checkIn = 0.25f;

            for (int i = 0; i < _yards.Count; i++)
                if (_yards[i].Contains(_player.position) && _yards[i].IslandKey != _yardKey)
                {
                    EnterYard(_yards[i]);
                    return;
                }
        }

        /// <summary>Hands the hall over to one yard: it runs, the rest are scenery, and the HUD follows.</summary>
        private void EnterYard(MarketYardScene yard)
        {
            if (yard == null) return;
            // Anything on the player's back belongs to the yard he is leaving. Carrying coal through
            // the doorway used to repaint it copper on arrival, which quietly turned one island's ore
            // into another's — the cheapest laundering scheme in the game. He puts it down instead,
            // back on the pad it came off, and walks into the next yard empty-handed.
            if (_carry != null && !_carry.IsEmpty &&
                !string.IsNullOrEmpty(_yardKey) && _yardKey != yard.IslandKey)
            {
                int dropped = _carry.Count;
                while (_carry.TryRemove()) { }
                if (_market != null) _market.Deliver(_yardKey, dropped);
            }

            _yardKey = yard.IslandKey;
            // Only the yard on screen is acted out; the ledger keeps selling for all the others.
            if (_market != null) _market.SetSimulatedYard(_yardKey);
            for (int i = 0; i < _yards.Count; i++)
            {
                bool live = _yards[i] == yard;
                _yards[i].SetLive(live);
                if (live && _carry != null && i < _tints.Count) _carry.SetMaterial(_tints[i]);
            }
            if (_hud != null) _hud.SetYard(_yardKey, yard.Pads);
        }

        /// <summary>Every pad in the hall, flattened — the label layer draws them all at once.</summary>
        private UpgradePad[] AllPads()
        {
            var all = new System.Collections.Generic.List<UpgradePad>();
            for (int i = 0; i < _yards.Count; i++)
                if (_yards[i].Pads != null) all.AddRange(_yards[i].Pads);
            return all.ToArray();
        }

        private MarketYardScene YardFor(string islandKey)
        {
            for (int i = 0; i < _yards.Count; i++)
                if (_yards[i].IslandKey == islandKey) return _yards[i];
            return null;
        }

        /// <summary>Moves a CharacterController without it fighting the move.</summary>
        private static void Teleport(MarketPlayer player, Vector3 to)
        {
            var body = player.GetComponent<CharacterController>();
            if (body != null) body.enabled = false;
            player.transform.position = to;
            if (body != null) body.enabled = true;
        }

        /// <summary>
        /// Returns to the island. Guarded because the button is a button: a second tap during the load
        /// would start a second load of the same scene.
        /// </summary>
        public void Leave()
        {
            if (_leaving) return;
            _leaving = true;
            // Hand the yard back to the ledger. Leaving this set would freeze whichever yard the
            // player was last standing in: the scene that was selling for it is about to be unloaded,
            // and the ledger would still think somebody else had it.
            if (_market != null) _market.SetSimulatedYard(null);
            // The island's own WorldIslands sets the active island again as it wakes, off the saved
            // 'worldactive' row — so which island we come back to is decided there, not here.
            // Async: Main is the heavy scene (every island, all three phase roots), and loading it
            // synchronously froze the market for the whole read. The yard stays animated instead.
            SceneManager.LoadSceneAsync(islandSceneName, LoadSceneMode.Single);
        }

        /// <summary>A last resort for any other way out — a hot reload, or a future path that swaps the
        /// scene without going through the button.</summary>
        private void OnDestroy()
        {
            if (!_leaving && _market != null) _market.SetSimulatedYard(null);
        }

        private MarketPlayer BuildPlayer(Vector3 at, Material ore)
        {
            var go = new GameObject("Oyuncu");
            go.transform.SetParent(transform, false);
            go.transform.position = at + new Vector3(0f, bodyHeight * 0.5f *
                                                     (prefabs != null ? prefabs.PersonScale : 1f), 0f);

            // The controller grows with the model. Sizing it separately is how you end up with a big
            // character that bumps into walls a metre before touching them.
            float grow = prefabs != null ? prefabs.PersonScale : 1f;
            float radius = bodyRadius * grow, height = bodyHeight * grow;

            var body = go.AddComponent<CharacterController>();
            body.radius = radius;
            body.height = height;
            body.center = Vector3.zero;
            // A step this size clears the pads and the kerbs without letting the player walk up a wall.
            body.stepOffset = 0.4f;
            body.slopeLimit = 50f;

            // The body hangs off an unscaled pivot at the player's feet, so an authored model authored
            // around its own origin drops straight in without inheriting the controller's proportions.
            var feet = new GameObject("Govde").transform;
            feet.SetParent(go.transform, false);
            feet.localPosition = new Vector3(0f, -height * 0.5f, 0f);
            Transform shape = prefabs != null
                ? prefabs.SpawnPerson(prefabs.Player, feet, "Model",
                                      new Vector3(bodyRadius * 2f, bodyHeight * 0.5f, bodyRadius * 2f), ore)
                : MarketPrefabs.Spawn(null, feet, "Model", PrimitiveType.Capsule,
                                      new Vector3(bodyRadius * 2f, bodyHeight * 0.5f, bodyRadius * 2f), ore);
            // The primitive fallback is a capsule centred on its middle, so it needs lifting onto its
            // feet. A wired prefab is expected to stand on its own origin and is left alone.
            if (prefabs == null || prefabs.Player == null)
                shape.localPosition = new Vector3(0f, height * 0.5f, 0f);
            _playerBody = shape;

            // The load rides an unscaled mount, not the body — the capsule is squashed to fit the
            // controller and a stack parented to it would inherit the squash.
            var mount = new GameObject("Sirt").transform;
            mount.SetParent(go.transform, false);
            mount.localPosition = new Vector3(0f, -height * 0.5f, 0f);
            var carry = go.AddComponent<CarryStack>();
            carry.SetPrefabs(prefabs);
            carry.Configure(mount, ore, _market != null ? MarketCarryLevel() : 0);

            var player = go.AddComponent<MarketPlayer>();
            player.BindBody(_playerBody);
            return player;
        }

        /// <summary>How tall a stack the player can shoulder. One body, one upgrade — not per yard.</summary>
        private int MarketCarryLevel()
        {
            var data = ServiceLocator.Get<SaveData>();
            return data != null ? data.marketCarryLevel : 0;
        }

        private MarketCamera BuildCamera()
        {
            var go = new GameObject("MarketKamerasi", typeof(Camera), typeof(AudioListener));
            go.tag = "MainCamera";
            go.transform.SetParent(transform, false);
            var cam = go.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.09f, 0.11f, 0.15f);
            cam.farClipPlane = 200f;
            return go.AddComponent<MarketCamera>();
        }

        private void BuildSun()
        {
            var go = new GameObject("Gunes", typeof(Light));
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.Euler(sunAngles);
            var light = go.GetComponent<Light>();
            light.type = LightType.Directional;
            light.color = sunColor;
            light.intensity = sunIntensity;
            light.shadows = LightShadows.Soft;
        }

        /// <summary>
        /// The yard's accent, taken from the ladder <see cref="WorldIslands"/> owns — a static lookup,
        /// because that component lives in the island scene and this one has just replaced it. A new
        /// island's yard is then the same layout in its own colour, with nothing here to keep in step.
        /// </summary>
        private static Color OreTint(string islandKey) => WorldIslands.OreColorFor(islandKey);
    }
}
