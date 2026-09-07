using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The masters, standing at the stations they run.
    ///
    /// The roster in <see cref="Game.Core.Foremen"/> is a screen full of numbers, and numbers are what
    /// the game already had too many of. A master you opened chests for should be somewhere on the
    /// island, visibly in charge of the thing he makes faster — that is the whole reason the genre's
    /// managers are people rather than upgrade rows.
    ///
    /// WHY NOT STATION CREW. <see cref="StationCrew"/> posts staff by DISTRICT — Mine, Refinery, Depot,
    /// Market, Port, Power, Haul, Fleet, Civic — and the roster is by STATION. The two nearly line up
    /// and then do not: TRAIN owns no district at all, and CARGO TRUCKS owns two. So a master hangs off
    /// <see cref="CoalOperation.StationAnchor"/> instead, which answers for all eight stations
    /// including the two that are a midpoint between buildings rather than a building. Only five of
    /// those eight have a master to post; the other three stay empty, which the Hired delegate says.
    ///
    /// WHAT MAKES HIM READ AS A MASTER, in three parts, because there is still no bespoke master ART:
    ///   - He does not walk. Everyone else drifts between posts; he stands where he was put and
    ///     gestures. That contrast was doing all of the work on its own before the rework.
    ///   - He is a DIFFERENT BODY at every station, picked deterministically out of the same people
    ///     pack — eight silhouettes rather than one strongman cloned eight times, so the mine master
    ///     and the market master are recognisably different men.
    ///   - He stands on a plinth tinted by his RARITY, and grows with it. That is what makes a
    ///     Legendary legible from across the island without a single new texture, and it is the same
    ///     colour the roster screen paints his card with — see
    ///     <see cref="Game.Systems.ForemanService.RarityTint"/>.
    /// Proper hard hats and hi-vis would still be better and remain a job for the art pipeline.
    ///
    /// Instantiated once and thereafter only shown, hidden, re-posted or re-tinted, so a running island
    /// allocates nothing. A plain class rather than a MonoBehaviour, for the same reason StationCrew
    /// and SiteLife are: CoalOperation already owns the per-island update order.
    /// </summary>
    public sealed class StationForemen
    {
        /// <summary>Where a station's foreman stands, and what he looks at. False when the island has
        /// no such station built yet — the body simply stays hidden.</summary>
        public delegate bool PostFunc(int station, out Vector3 ground, out Vector3 lookAt);

        /// <summary>Is this station's foreman hired? Set by the owner; a delegate rather than a service
        /// reference so this class knows nothing about the save or the economy.</summary>
        public System.Func<int, bool> Hired;

        /// <summary>Where to stand. Same contract, same reason.</summary>
        public PostFunc Post;

        /// <summary>Which rarity the master posted at this station is, as an int in
        /// [0, <see cref="Foremen.RarityCount"/>). Drives his plinth colour and his size. Same
        /// delegate contract as the two above.
        ///
        /// RARITY RATHER THAN STARS. It used to be a star count, because stars were what decided a
        /// master's tier; rarity is now a fact about the card and stars only make him stronger. Sizing
        /// him by stars would put a maxed Common shoulder to shoulder with a fresh Legendary, which is
        /// exactly the thing the plinth exists to tell apart from across the island.</summary>
        public System.Func<int, int> Rank;

        /// <summary>
        /// WHICH MAN is posted here, as an index the body pack is picked from. -1 keeps whatever body
        /// the station started with.
        ///
        /// The station used to choose the body, back when a station HAD one master and the only thing
        /// that could change about him was how far you had taken him. Three masters share a station
        /// now, so a body fixed to the post means swapping your Common for a Legendary changes the
        /// plinth colour under a man who is visibly the same man. The body follows the master instead,
        /// and the pack has eighteen variants against fifteen masters, so no two share one.
        /// </summary>
        public System.Func<int, int> Who;

        private sealed class Boss
        {
            public Transform t;
            public PersonAnimator anim;
            public MeshRenderer plinth;
            public float seed;        // so eight of them never gesture on the same beat
            public float timer;
            public bool gesturing;
            public bool placed;
            public bool active;
            public int rank = -1;     // -1 = never dressed, so the first Refresh always paints
            public int who = -1;      // which master's body this is, so a swap re-picks it
        }

        private readonly Boss[] _bosses;
        private readonly Material[] _rankPlinth;
        private readonly float _baseScale;
        private readonly float _rankScaleStep;
        private float _timeScale = 1f;

        // Kept so a body can be re-picked when the posted master changes — see Who.
        private readonly Transform _parent;
        private readonly GameObject[] _pack;
        private readonly Mesh _disc;

        /// <summary>How much bigger each rarity stands. Three rarities at 10% apiece is a fifth again
        /// from Common to Legendary — enough to notice beside the crew, small enough that he still fits
        /// the doorway he is standing next to. It was 5% over five tiers, for the same total.</summary>
        private const float DefaultRankScaleStep = 0.10f;

        public StationForemen(Transform parent, GameObject[] prefabs, int stationCount, float scale,
                              Material[] rankPlinth = null, float plinthRadius = 1.5f,
                              float rankScaleStep = DefaultRankScaleStep)
        {
            _baseScale = scale;
            _rankScaleStep = rankScaleStep;
            _rankPlinth = rankPlinth;

            _parent = parent;
            _pack = Compact(prefabs);

            // No bodies wired: no masters, and every method below is a no-op rather than a null deref.
            _bosses = new Boss[_pack.Length > 0 ? Mathf.Max(0, stationCount) : 0];
            _disc = _bosses.Length > 0 && rankPlinth != null
                ? Disc(plinthRadius / Mathf.Max(scale, 0.01f)) : null;

            // The bodies are NOT made here. Which body a station gets depends on which master is
            // posted there, and nobody has told us yet — the delegates are assigned by the caller's
            // object initialiser, which runs after this constructor. Building eight arbitrary men now
            // would mean destroying and rebuilding five of them on the first Refresh.
        }

        /// <summary>One body out of the pack, posed and switched off. Also the swap path — see Who.</summary>
        private Boss MakeBoss(int station, int variant)
        {
            if (_pack.Length == 0) return null;
            if (variant < 0) variant = 0;

            var go = Object.Instantiate(_pack[Stride(variant, _pack.Length)], _parent);
            go.name = "OpForeman_" + station;
            go.transform.localScale = Vector3.one * _baseScale;
            var boss = new Boss
            {
                t = go.transform,
                anim = new PersonAnimator(go.transform),
                // Golden-angle stagger, the same trick StationCrew uses and for the same reason.
                seed = (station * 2.39996f) % 1f,
                plinth = _disc != null ? Plinth(go.transform, _disc, _baseScale) : null,
            };
            go.SetActive(false);
            return boss;
        }

        /// <summary>
        /// Makes sure the body at a station belongs to the master posted there, building it the first
        /// time and swapping it when the posting changes. Only ever runs on a roster change — a tap,
        /// not a frame — so the destroy-and-instantiate costs nothing that matters, and it keeps one
        /// body per station rather than fifteen with ten of them hidden.
        /// </summary>
        private Boss Ensure(int station, int who)
        {
            Boss old = _bosses[station];
            if (old != null && old.t != null && old.who == who) return old;

            Boss fresh = MakeBoss(station, who);
            if (fresh == null) return old;      // no pack: keep whoever is already standing there

            if (old != null && old.t != null)
            {
                // Switched off before it goes: Destroy is deferred to the end of the frame, so an
                // active body would stand inside its replacement until then.
                old.t.gameObject.SetActive(false);
                Kill(old.t.gameObject);
            }

            fresh.who = who;
            _bosses[station] = fresh;
            return fresh;
        }

        /// <summary>
        /// Destroy, by whichever route this run allows. Edit mode refuses <see cref="Object.Destroy"/>
        /// outright, and the roster's EditMode tests drive this class directly.
        /// </summary>
        private static void Kill(GameObject go)
        {
            if (Application.isPlaying) Object.Destroy(go);
            else Object.DestroyImmediate(go);
        }

        /// <summary>The wired pack with its holes removed, so the spread below indexes real bodies.</summary>
        private static GameObject[] Compact(GameObject[] prefabs)
        {
            if (prefabs == null) return new GameObject[0];
            int n = 0;
            for (int i = 0; i < prefabs.Length; i++) if (prefabs[i] != null) n++;
            var pack = new GameObject[n];
            for (int i = 0, at = 0; i < prefabs.Length; i++)
                if (prefabs[i] != null) pack[at++] = prefabs[i];
            return pack;
        }

        /// <summary>
        /// Which body a MASTER gets. Walking the pack in fives rather than in ones because the pack is
        /// ordered by build — three normals, then three stouts, then three strongs, each in two sexes —
        /// so consecutive entries are near-identical. That mattered when it keyed off the station and
        /// matters more now that it keys off the master: a station's Common, Rare and Legendary are
        /// three consecutive roster indices, and in a row they would be three near-identical men, which
        /// is the one comparison the player actually makes. A stride coprime with the pack size visits
        /// every entry before repeating.
        /// </summary>
        private static int Stride(int who, int packSize)
            => packSize <= 0 ? 0 : (Mathf.Max(0, who) * 5) % packSize;

        /// <summary>
        /// The disc the master stands on: one shared mesh, drawn flat on the ground and tinted per
        /// tier. Built from <see cref="BoxMeshBuilder"/> like every other piece of generated island
        /// geometry, and parented under the runtime-instantiated body — never under the district art,
        /// which is static-batched into world space and cannot carry a child that moves.
        /// </summary>
        private static Mesh Disc(float radius)
        {
            var b = new BoxMeshBuilder();
            b.AddDisc(Vector3.zero, radius, 22, 0);
            var mesh = new Mesh { name = "UstaKaidesi" };
            b.Apply(mesh);
            return mesh;
        }

        private static MeshRenderer Plinth(Transform body, Mesh disc, float scale)
        {
            var go = new GameObject("Kaide", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(body, false);
            // Just clear of the ground: the deck it sits on is a drawn surface too, and coplanar
            // geometry z-fights the moment the camera moves.
            go.transform.localPosition = new Vector3(0f, 0.06f / Mathf.Max(scale, 0.01f), 0f);
            go.GetComponent<MeshFilter>().sharedMesh = disc;
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mr;
        }

        /// <summary>
        /// Re-reads who is hired and re-posts them.
        ///
        /// Called explicitly rather than polled: the only two things that can change the answer are a
        /// roster change and a district rebuild, and both already raise something. A half-second poll
        /// would re-walk eight station anchors forever to discover nothing had happened.
        /// </summary>
        public void Refresh()
        {
            for (int s = 0; s < _bosses.Length; s++)
            {
                bool want = Hired != null && Hired(s);

                // The body is made on demand and re-picked when the posting moves — both BEFORE it is
                // posed, or the new man spends a frame standing where the old one was. A station
                // nobody staffs never builds a body at all.
                Boss b = _bosses[s];
                if (want)
                {
                    int who = Who != null ? Who(s) : -1;
                    b = Ensure(s, who);
                }
                if (b == null || b.t == null) continue;

                if (want && Post != null && Post(s, out Vector3 ground, out Vector3 lookAt))
                {
                    b.t.position = ground;
                    Vector3 flat = lookAt - ground;
                    flat.y = 0f;
                    if (flat.sqrMagnitude > 0.0001f) b.t.rotation = Quaternion.LookRotation(flat.normalized);
                    b.placed = true;
                }
                else want = false;

                if (want) Dress(b, s);

                if (b.active == want) continue;
                b.active = want;
                b.t.gameObject.SetActive(want);
                if (want) b.anim.Set(PersonAnimator.Idle);
            }
        }

        /// <summary>
        /// Sizes and tints one master for the rarity of whoever is posted there.
        ///
        /// In Refresh rather than in the constructor, which is where the scale used to be set once: the
        /// player can swap a station's master for a rarer one while the island is running, and a
        /// Legendary that only shows up after a scene reload is not feedback.
        /// </summary>
        private void Dress(Boss b, int station)
        {
            int rank = Rank != null ? Rank(station) : 0;
            if (rank < 0) rank = 0;
            if (b.rank == rank) return;
            b.rank = rank;

            b.t.localScale = Vector3.one * (_baseScale * (1f + _rankScaleStep * rank));
            if (b.plinth != null && _rankPlinth != null && _rankPlinth.Length > 0)
                b.plinth.sharedMaterial = _rankPlinth[Mathf.Clamp(rank, 0, _rankPlinth.Length - 1)];
        }

        /// <summary>Matches the island's own clock so a boosted island's foremen do not stand still.</summary>
        public void SetTimeScale(float scale) => _timeScale = Mathf.Max(0f, scale);

        public void Tick(float dt)
        {
            if (_bosses.Length == 0) return;

            float step = dt * _timeScale;
            for (int s = 0; s < _bosses.Length; s++)
            {
                Boss b = _bosses[s];
                if (b == null || !b.active || !b.placed) continue;

                b.timer -= step;
                if (b.timer > 0f) continue;

                // Supervising: mostly standing, occasionally waving someone on. He never walks — that
                // is what tells him apart from the crew drifting around him.
                b.gesturing = !b.gesturing;
                b.anim.Set(b.gesturing ? PersonAnimator.Wave : PersonAnimator.Idle);
                b.timer = b.gesturing ? 1.4f + b.seed * 0.8f : 3.5f + b.seed * 4f;
            }
        }
    }
}
