using Game.Core;
using Game.Gameplay;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

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

        [Header("Yön Levhaları")]
        [Tooltip("Yerdeki yön yazısının punto büyüklüğü. Telefonda, kameranın normal uzaklığından " +
                 "okunabilmesi gerek.")]
        [SerializeField, Min(0.5f)] private float signFontSize = 7.5f;

        [Tooltip("Yazının kapladığı alan, dünya birimi. Adı sığmayan bir dilde büyüt.")]
        [SerializeField] private Vector2 signArea = new Vector2(14f, 4f);

        [Tooltip("Boş bırakılırsa TMP'nin öntanımlı yazı tipi kullanılır.")]
        [SerializeField] private TMP_FontAsset signFont;

        [Header("Işık")]
        [Tooltip("Güneşin açısı. Avlu tek yönlü ışıkla aydınlanıyor, adadaki gibi.")]
        [SerializeField] private Vector3 sunAngles = new Vector3(48f, 35f, 0f);
        [SerializeField] private Color sunColor = new Color(1f, 0.96f, 0.89f);
        [SerializeField, Min(0f)] private float sunIntensity = 1.15f;

        private readonly System.Collections.Generic.List<MarketYardScene> _yards =
            new System.Collections.Generic.List<MarketYardScene>();
        private readonly System.Collections.Generic.List<Material> _tints =
            new System.Collections.Generic.List<Material>();
        // Parallel to _yards, and it has to stay that way: the gate plates are set from the row's
        // point of view rather than the yard's, so BuildWayfinding indexes both lists together.
        private readonly System.Collections.Generic.List<MarketYardSigns> _signs =
            new System.Collections.Generic.List<MarketYardSigns>();

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

            // Indoors now. The island's bed is gulls and surf, which is a strange thing to hear
            // under a roof; the market has its own room tone and Leave puts the island's back.
            ServiceLocator.Get<AudioService>()?.SetMarketAmbience(true);

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

            // Price tags on every pad in the hall, not just this yard's. They used to be readable
            // through the doorway, which was the reason for doing all of them at once; now a shut yard's
            // roof hides its own floor and its prices with it, and what tells you which shop is which
            // from outside is the roof's ore tint instead. Still built in one pass: they are painted
            // once, at load, and a label that only appeared when you walked in would be a label built
            // on the frame the player is least able to afford one.
            var labels = new GameObject("PedEtiketleri").AddComponent<YardPadLabels>();
            labels.transform.SetParent(transform, false);
            labels.Build(_market, AllPads());

            BuildWayfinding();
            EnterYard(arrival);
        }

        /// <summary>
        /// Which way the other markets are, painted on the floor of every yard: an arrow at the opening in
        /// the wall, and the name of the market on the other side of it.
        ///
        /// ON THE GROUND, because the camera is a fixed angle and never turns. Anything standing up in a
        /// doorway faces one of the two rooms and shows the other its back, and anything standing up over
        /// one fights the roofs that meet above it. The floor faces the camera from both sides and has
        /// nothing above it to argue with — the same reason the pad prices are painted down there.
        ///
        /// Each yard's signs are only ever seen from inside that yard, and that falls out for free: a shut
        /// yard's roof hides its own floor, so the signs light up when the player walks in and go away with
        /// the rest of the room.
        /// </summary>
        private void BuildWayfinding()
        {
            for (int i = 0; i < _yards.Count; i++)
            {
                if (i + 1 < _yards.Count)
                {
                    string next = _yards[i + 1].IslandKey;
                    Signpost(_yards[i], next, true);
                    // And the same thing at head height, on the one wall the camera can read. Only
                    // eastward: the way back west goes through the PREVIOUS yard's east wall, whose
                    // inner face points away from the camera — a plate there could only be read
                    // edge-on. That direction keeps the floor arrow and nothing else.
                    _signs[i].SetGateSign(next, OreTint(next), MarketTheme.For(_yards[i].IslandKey),
                                          signFont);
                }
                if (i > 0) Signpost(_yards[i], _yards[i - 1].IslandKey, false);
            }
        }

        /// <summary>One yard's sign toward one neighbour: the arrow, and the name to read on the way to it.</summary>
        private void Signpost(MarketYardScene yard, string towardKey, bool eastward)
        {
            // Lightened well off the raw ore — coal's is nearly black and would read as a stain rather
            // than a sign — but still recognisably the colour of the market it points at, which is the
            // same colour as that market's roof from outside.
            Material paint = MarketYardBuild.Mat(Color.Lerp(OreTint(towardKey), Color.white, 0.45f));
            // East one right up against the wall, in the clear floor between the stock pad and the
            // opening. The west one has to sit further in, and not for a reason of taste: the yard to the
            // west is shut, its roof stands where that wall is, and a roof at this camera angle hides the
            // two and a half units of floor beyond its own edge. An arrow painted in that band would have
            // its point cut off by the roof of the very market it is pointing at.
            MarketYardBuild.FloorArrow(yard.transform, "Yon_Ok",
                                       new Vector3(eastward ? 20.5f : -18f, 0f, 0f), eastward, paint);

            // The name cannot stand with the arrow. The arrow sits in the four units of clear floor at the
            // opening, which is not wide enough to write a word in, and floor text only reads the right way
            // round running east-west — turned to fit that gap it would be sideways on screen. So it goes
            // in the nearest clear ground on the way to the door instead, and the two spots are not mirror
            // images because the room is not: the stock pad owns the middle of the east half and the rank
            // of pads owns the west wall.
            var go = new GameObject("Yon_Yazi");
            go.transform.SetParent(yard.transform, false);
            go.transform.localPosition = eastward ? new Vector3(15f, 0.22f, -8.5f)
                                                  : new Vector3(-8f, 0.22f, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var text = go.AddComponent<TextMeshPro>();
            if (signFont != null) text.font = signFont;
            text.fontSize = signFontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.rectTransform.sizeDelta = signArea;
            text.fontStyle = FontStyles.Bold;
            text.outlineWidth = 0.2f;
            text.outlineColor = new Color32(12, 14, 20, 235);
            text.color = Color.Lerp(OreTint(towardKey), Color.white, 0.75f);
            // Chevrons on the side the market is, doubling the arrow at reading distance. ASCII on
            // purpose: this line is drawn in eleven languages and a glyph the font is missing is a box.
            string name = Loc.Id("ada", towardKey);
            text.text = eastward ? name + "  >>" : "<<  " + name;
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

                // The signage goes up after the yard is standing, and it is built from here rather
                // than from inside the yard because it draws text — Game.Gameplay cannot see
                // TextMeshPro. The yard takes the holder back so it can park the signs with the rest
                // of itself; the price board on it reads the ledger twice a second and there is no
                // sense in seven shut yards doing that under their own roofs.
                MarketYardSigns signs = MarketYardSigns.Build(yard.transform, _market, owned[i], tint,
                                                              MarketTheme.For(owned[i]), signFont);
                yard.AddFitting(signs.gameObject);
                // The shopfront on the roof is built AFTER the yard has taken the rest, and hung off
                // the yard root rather than the holder it just took. It is the one sign that has to be
                // readable while this yard is shut — that is the whole of what it says.
                signs.BuildNeon(yard.transform, tint, MarketTheme.For(owned[i]), signFont);

                _yards.Add(yard);
                _signs.Add(signs);
                _tints.Add(MarketYardBuild.Mat(tint));
            }
        }

        /// <summary>
        /// Which yard the player is standing in, checked as they walk rather than only on arrival —
        /// the doorway between two yards is a step, not a screen.
        ///
        /// The cadence used to be a quarter second, which was fine while crossing over only swapped
        /// which components were ticking. It is now also what takes the roof off, and a roof that comes
        /// off a fifth of a second after you are already inside reads as the game noticing late.
        /// </summary>
        private void Update()
        {
            // Main normally lends the additive market its EventSystem. Keep this recovery here as well:
            // an editor hot reload or an interrupted transition may have parked the old system before
            // the market could inherit it. Without one, movement, taps and the return button all stop.
            if (EventSystem.current == null || !EventSystem.current.isActiveAndEnabled)
                UiBuild.EnsureEventSystem(transform);

            if (_player == null || _yards.Count < 2) return;
            _checkIn -= Time.deltaTime;
            if (_checkIn > 0f) return;
            _checkIn = 0.06f;

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
                // The shopfront says the same thing the roof does, in words and from further away.
                // Set here rather than inside SetLive because the sign is Game.UI's and the yard's
                // own assembly cannot see it.
                if (i < _signs.Count) _signs[i].SetOpen(live);
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
            // Behind a loading screen, and this is the button players reported as broken.
            //
            // Nothing was wrong with it. Main is the heavy scene — every island, all three phase roots —
            // and the async read of it takes long enough on a mid-range phone that the tap had no visible
            // answer: the yard just carried on. So it read as a dead button, and the guard above then
            // made the second and third tap genuinely dead. The curtain answers on the first frame.
            //
            // Kept as a guard as well, because it is free and the curtain is not the only way in here.
            string key = _yardKey;
            if (!SceneCurtain.Cover(islandSceneName, WorldIslands.OreColorFor(key), Loc.Id("ada", key)))
                return;
            _leaving = true;
            ServiceLocator.Get<AudioService>()?.SetMarketAmbience(false);
            // Hand the yard back to the ledger. Leaving this set would freeze whichever yard the
            // player was last standing in: the scene that was selling for it is about to be unloaded,
            // and the ledger would still think somebody else had it.
            //
            // After the curtain has taken the job, not before: if it refused, this scene is staying and
            // its yard must go on being the simulated one.
            if (_market != null) _market.SetSimulatedYard(null);
            // Which island we come back to is decided by the island scene's own WorldIslands as it wakes,
            // off the saved 'worldactive' row — not here.
        }

        /// <summary>A last resort for any other way out — a hot reload, or a future path that swaps the
        /// scene without going through the button.</summary>
        private void OnDestroy()
        {
            if (_leaving) return;
            if (_market != null) _market.SetSimulatedYard(null);
            // The bed too, for the same reason: a hot reload or a future path that swaps the scene
            // without the button would otherwise leave the island humming with the market's room tone.
            ServiceLocator.Get<AudioService>()?.SetMarketAmbience(false);
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

            // His own colour, not the island's ore. The ore was the obvious thing to tint him with and it
            // was the wrong one: on the coal island the ore is very nearly black, so the one body in the
            // room the player was driving came out as a dark capsule among a queue of lighter ones. The
            // ore material still goes on his LOAD, which does have to look like the stuff he is carrying.
            //
            // Only ever seen on the greybox capsule — a wired model keeps its own materials, which is why
            // PlayerMarker is doing the same job again in a way art cannot undo.
            Material kit = MarketYardBuild.Mat(new Color(0.18f, 0.62f, 0.85f));
            Transform shape = prefabs != null
                ? prefabs.SpawnPerson(prefabs.Player, feet, "Model",
                                      new Vector3(bodyRadius * 2f, bodyHeight * 0.5f, bodyRadius * 2f), kit)
                : MarketPrefabs.Spawn(null, feet, "Model", PrimitiveType.Capsule,
                                      new Vector3(bodyRadius * 2f, bodyHeight * 0.5f, bodyRadius * 2f), kit);
            // The primitive fallback is a capsule centred on its middle, so it needs lifting onto its
            // feet. A wired prefab is expected to stand on its own origin and is left alone.
            if (prefabs == null || prefabs.Player == null)
                shape.localPosition = new Vector3(0f, height * 0.5f, 0f);
            _playerBody = shape;

            // The ring, the shadow and the marker overhead. On the feet pivot rather than the body, so it
            // rides up onto a pad with him and stays clear of whatever the model's own scale is.
            var markerGo = new GameObject("OyuncuIsareti");
            markerGo.transform.SetParent(feet, false);
            markerGo.AddComponent<PlayerMarker>().Build(height);

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
