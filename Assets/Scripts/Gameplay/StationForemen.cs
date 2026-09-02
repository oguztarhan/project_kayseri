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
    /// including the two that are a midpoint between buildings rather than a building.
    ///
    /// WHAT MAKES HIM READ AS A MASTER, in three parts, because there is still no bespoke master ART:
    ///   - He does not walk. Everyone else drifts between posts; he stands where he was put and
    ///     gestures. That contrast was doing all of the work on its own before the rework.
    ///   - He is a DIFFERENT BODY at every station, picked deterministically out of the same people
    ///     pack — eight silhouettes rather than one strongman cloned eight times, so the mine master
    ///     and the market master are recognisably different men.
    ///   - He stands on a plinth tinted by his TIER, and grows with it. That is what makes a Legendary
    ///     legible from across the island without a single new texture, and it is the same colour the
    ///     roster screen paints his card with — see <see cref="Game.Systems.ForemanService.TierTint"/>.
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

        /// <summary>How many stars this station's master carries. Drives his tier, and with it his
        /// plinth colour and his size. Same delegate contract as the two above.</summary>
        public System.Func<int, int> Stars;

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
            public int tier = -1;     // -1 = never dressed, so the first Refresh always paints
        }

        private readonly Boss[] _bosses;
        private readonly Material[] _tierPlinth;
        private readonly float _baseScale;
        private readonly float _tierScaleStep;
        private float _timeScale = 1f;

        /// <summary>How much bigger each tier stands. Five tiers at 5% apiece is a fifth again from
        /// Common to Mythic — enough to notice beside the crew, small enough that he still fits the
        /// doorway he is standing next to.</summary>
        private const float DefaultTierScaleStep = 0.05f;

        public StationForemen(Transform parent, GameObject[] prefabs, int stationCount, float scale,
                              Material[] tierPlinth = null, float plinthRadius = 1.5f,
                              float tierScaleStep = DefaultTierScaleStep)
        {
            _baseScale = scale;
            _tierScaleStep = tierScaleStep;
            _tierPlinth = tierPlinth;

            GameObject[] pack = Compact(prefabs);

            // No bodies wired: no masters, and every method below is a no-op rather than a null deref.
            _bosses = new Boss[pack.Length > 0 ? Mathf.Max(0, stationCount) : 0];
            Mesh disc = _bosses.Length > 0 && tierPlinth != null ? Disc(plinthRadius / Mathf.Max(scale, 0.01f)) : null;

            for (int i = 0; i < _bosses.Length; i++)
            {
                var go = Object.Instantiate(pack[Stride(i, pack.Length)], parent);
                go.name = "OpForeman_" + i;
                go.transform.localScale = Vector3.one * scale;
                _bosses[i] = new Boss
                {
                    t = go.transform,
                    anim = new PersonAnimator(go.transform),
                    // Golden-angle stagger, the same trick StationCrew uses and for the same reason.
                    seed = (i * 2.39996f) % 1f,
                    plinth = disc != null ? Plinth(go.transform, disc, scale) : null,
                };
                go.SetActive(false);
            }
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
        /// Which body a station gets. Walking the pack in fives rather than in ones because the pack is
        /// ordered by build — three normals, then three stouts, then three strongs, each in two sexes —
        /// so consecutive entries are near-identical and the neighbouring stations would end up with
        /// near-identical men. A stride coprime with the pack size visits every entry before repeating.
        /// </summary>
        private static int Stride(int station, int packSize)
            => packSize <= 0 ? 0 : (station * 5) % packSize;

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
                Boss b = _bosses[s];
                if (b == null || b.t == null) continue;

                bool want = Hired != null && Hired(s);
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
        /// Sizes and tints one master for the tier he is currently at.
        ///
        /// In Refresh rather than in the constructor, which is where the scale used to be set once: a
        /// master's tier moves every second star while the island is running, and a promotion the
        /// player just paid for that only shows up after a scene reload is not feedback.
        /// </summary>
        private void Dress(Boss b, int station)
        {
            int stars = Stars != null ? Stars(station) : 1;
            int tier = (int)Foremen.TierOf(stars);
            if (b.tier == tier) return;
            b.tier = tier;

            b.t.localScale = Vector3.one * (_baseScale * (1f + _tierScaleStep * tier));
            if (b.plinth != null && _tierPlinth != null && _tierPlinth.Length > 0)
                b.plinth.sharedMaterial = _tierPlinth[Mathf.Clamp(tier, 0, _tierPlinth.Length - 1)];
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
