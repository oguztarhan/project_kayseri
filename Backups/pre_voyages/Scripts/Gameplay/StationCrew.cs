using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The people who work the island's districts: a few bodies posted at the mine face, the smelter,
    /// the depot, the quay and the town, standing at their stations, shifting about between jobs, and
    /// working when their station is actually running.
    ///
    /// <see cref="SiteLife"/> already walks a crew around the ring pavement, which gives the island
    /// movement but no PLACE — the buildings stay empty while people file past them. This is the other
    /// half: staff who belong to a building rather than to a path, so the mine reads as a mine being
    /// worked instead of a model of one.
    ///
    /// Posts are derived from the district ART rather than authored, because the art is rebuilt twice
    /// as the island advances and any hand-placed point would be left standing inside the next phase's
    /// wall. Each district is measured, and its staff are ringed around the OUTSIDE of that footprint —
    /// which is also what keeps a worker from ending up inside a building.
    ///
    /// Everything is instantiated once and only ever moved or switched off, so a running island
    /// allocates nothing. A plain class rather than a MonoBehaviour, for the same reason
    /// <see cref="SiteLife"/> is one: <see cref="CoalOperation"/> already owns the per-island update
    /// order, and this has no independent lifecycle of its own.
    /// </summary>
    public sealed class StationCrew
    {
        /// <summary>
        /// The districts that get staff, the station whose levels pay for it, and the build of person
        /// who works there.
        ///
        /// Kept in step by index. The district names are the art objects
        /// <see cref="Kayseri.Island.IslandPhaseController.ActiveDistrict"/> hands back, and the driver
        /// stations are the same ones that advance each district's phase — so the crew grows on exactly
        /// the purchases that rebuild the buildings they stand next to.
        ///
        /// Roads, Sites, Props, Terrain, Foliage and Theme are deliberately absent: they are landscape,
        /// not workplaces, and a man standing to attention in a field reads as a bug.
        /// </summary>
        private static readonly string[] Districts =
        { "Mine", "Refinery", "Depot", "Market", "Port", "Power", "Haul", "Fleet", "Civic" };

        /// <summary>Driver station per district; null = follow the furthest-advanced station.</summary>
        private static readonly string[] Drivers =
        { "MINE", "SMELTER", "STORAGE", "MARKET", "CARGO TRUCKS", "POWER PLANT", "ORE TRUCKS", "CARGO TRUCKS", null };

        /// <summary>
        /// Which body works where — the prefix of the people pack's prefab names. Labour at the mine,
        /// the furnace and the haul yard; yard hands at the depot, the quay and the truck park; ordinary
        /// townspeople at the market and in the town. Costs nothing (they share one palette texture) and
        /// it is the difference between a workforce and a crowd of clones.
        /// </summary>
        private static readonly string[] Builds =
        { "strong", "strong", "stout", "normal", "stout", "strong", "strong", "stout", "normal" };

        /// <summary>How often staffing and the per-district busy flags are re-read. Both change on the
        /// scale of purchases and smelter cycles, so polling them per frame buys nothing.</summary>
        private const float PollSeconds = 0.5f;

        /// <summary>Close enough to count as standing on the spot.</summary>
        private const float Arrived = 0.2f;

        private sealed class Hand
        {
            public Transform t;
            public PersonAnimator anim;
            public int district;
            public int slot;              // which post at that district; slot 0 is the first one hired
            public Vector3 target;        // where this body is walking to — its post, or a drift spot
            public Quaternion facing;     // where it looks once it gets there
            public float timer;           // until the next decision
            public float work;            // seconds of work gesture left
            public bool worked;           // last decision was to work, so the next one is to shift about
            public bool drifting;
            public float seed;            // per-body offset, so no two act on the same beat
            public bool active;
        }

        /// <summary>
        /// Total levels bought on a station, by name — how the crew knows a district has been invested
        /// in. A delegate rather than a reference to the operation because, like <see cref="SiteLife"/>,
        /// this class deliberately knows nothing about the simulation.
        /// </summary>
        public System.Func<string, int> StationLevels;

        /// <summary>"Is this district actually running right now?" — what turns the work gesture on.</summary>
        public System.Func<string, bool> Busy;

        private readonly Kayseri.Island.IslandPhaseController _phases;
        private readonly Hand[] _hands;
        private readonly Vector3[] _postPos;      // district * _perDistrict + slot
        private readonly Vector3[] _postLook;     // the district centre each post faces
        private readonly bool[] _postOk;          // false where the phase builds no such district
        private readonly bool[] _busy;
        private readonly Bounds[] _pick;          // scratch: the biggest structures in the district being measured
        private readonly float[] _pickSize;
        private readonly int _perDistrict;
        private readonly int _levelsPerWorker;
        private readonly float _walkSpeed;
        private readonly float _driftRange;

        private float _poll;
        private bool _dirty;      // a district rebuilt; the posts are re-measured on the next tick

        public StationCrew(Transform parent, GameObject[] prefabs, Kayseri.Island.IslandPhaseController phases,
                           float scale, int maxWorkers, int postsPerDistrict, int levelsPerWorker,
                           float walkSpeed, float driftRange)
        {
            _phases = phases;
            _perDistrict = Mathf.Max(1, postsPerDistrict);
            _levelsPerWorker = Mathf.Max(1, levelsPerWorker);
            _walkSpeed = Mathf.Max(0.1f, walkSpeed);
            _driftRange = Mathf.Max(0f, driftRange);

            int posts = Districts.Length * _perDistrict;
            _postPos = new Vector3[posts];
            _postLook = new Vector3[posts];
            _postOk = new bool[posts];
            _busy = new bool[Districts.Length];
            _pick = new Bounds[_perDistrict];
            _pickSize = new float[_perDistrict];

            bool haveBodies = false;
            if (prefabs != null)
                for (int i = 0; i < prefabs.Length && !haveBodies; i++) haveBodies = prefabs[i] != null;

            // No bodies wired, or a generated island with no phase art to measure: no crew, and every
            // method below turns into a no-op rather than a null reference.
            _hands = new Hand[haveBodies && phases != null ? Mathf.Clamp(maxWorkers, 0, posts) : 0];

            // Filled slot-major: every district gets its first worker before any district gets a second.
            // Budget-first the other way round and a small crew would put four men on the mine and
            // leave the rest of the island deserted.
            for (int i = 0; i < _hands.Length; i++)
            {
                int slot = i / Districts.Length;
                int district = i % Districts.Length;

                var go = Object.Instantiate(PickBody(prefabs, Builds[district], i), parent);
                go.name = "OpStaff_" + Districts[district] + slot;
                go.transform.localScale = Vector3.one * scale;

                _hands[i] = new Hand
                {
                    t = go.transform,
                    anim = new PersonAnimator(go.transform),
                    district = district,
                    slot = slot,
                    // Golden-angle stagger: cheaper than Random, and it repeats slowly enough that the
                    // crew never falls into step with itself.
                    seed = (i * 2.39996f) % 1f,
                };
                go.SetActive(false);
            }

            Refresh();
            if (_phases != null) _phases.PhaseChanged += OnPhaseChanged;
        }

        /// <summary>Drops the phase subscription. The island root going away takes the bodies with it.</summary>
        public void Dispose()
        {
            if (_phases != null) _phases.PhaseChanged -= OnPhaseChanged;
        }

        /// <summary>
        /// Marks the posts stale rather than re-measuring on the spot. One purchase can advance several
        /// districts at once — every district with no station of its own follows the furthest-advanced
        /// one, so a single level can raise seven of them — and each would otherwise walk the whole
        /// island's renderers again inside the same frame.
        /// </summary>
        private void OnPhaseChanged(string district, int phase) => _dirty = true;

        /// <summary>
        /// Re-measures every district and lays its posts back out around the new art.
        ///
        /// Called on a phase change and a few times over the first seconds of an island — the mine yard
        /// is not authored where it ends up (<see cref="CoalOperation"/> arranges it at runtime) and
        /// there is no guarantee this is built after that has run. <see cref="IslandAmbience"/> settles
        /// its sound sources for exactly the same reason.
        /// </summary>
        public void Refresh()
        {
            if (_phases == null) return;

            for (int d = 0; d < Districts.Length; d++)
            {
                Transform art = _phases.ActiveDistrict(Districts[d]);
                if (art == null) { ClearPosts(d); continue; }

                // Posted against a BUILDING, not against the district as a whole. Ringing the district's
                // own bounds sounds equivalent and is not: the mine district spans most of that corner of
                // the island, so its ring lands tens of units out in bare grass, with the worker facing a
                // mountain from the middle of a field. Beside the shed, they read as staff.
                int found = LargestBuildings(art);
                if (found == 0) { ClearPosts(d); continue; }

                for (int p = 0; p < _perDistrict; p++)
                {
                    int i = d * _perDistrict + p;
                    Bounds b = _pick[p % found];

                    // Just clear of that building's footprint, on the ground it stands on. The island is
                    // not flat — authored ground runs 5 to 16 units across one map — so a single island
                    // deck height would bury half the crew and float the other half.
                    float radius = Mathf.Max(b.extents.x, b.extents.z) * 1.15f + 1.5f;
                    var centre = new Vector3(b.center.x, b.min.y, b.center.z);

                    // Staggered by slot AND by district, so neither the crew of one district nor the
                    // island as a whole lines everyone up on the same compass bearing. The extra p term
                    // matters when two posts share a building — it is what stops them overlapping.
                    float ang = ((p + 0.5f) / _perDistrict + d * 0.19f + p * 0.31f) * Mathf.PI * 2f;
                    _postPos[i] = centre + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * radius;
                    _postLook[i] = centre;
                    _postOk[i] = true;
                }
            }

            // Anyone whose post just moved walks to the new one rather than teleporting: a district
            // rebuild is something the player watches happen, and the crew relocating sells it.
            for (int i = 0; i < _hands.Length; i++)
            {
                Hand h = _hands[i];
                h.drifting = false;
                h.target = Post(h);
                h.facing = LookAt(Post(h), PostLook(h));
                if (!h.active) h.t.position = h.target;
            }
        }

        private void ClearPosts(int d)
        {
            for (int p = 0; p < _perDistrict; p++) _postOk[d * _perDistrict + p] = false;
        }

        /// <summary>
        /// Fills <see cref="_pick"/> with the biggest structures in a district, largest first, and
        /// returns how many it found.
        ///
        /// Biggest rather than first-found because child order is whatever the exporter happened to
        /// write: taking children in order posts workers next to smoke markers and fence props while the
        /// shed they belong beside stands empty. Size is the cheapest available proxy for "this is a
        /// building". Falls back to the district itself when it has no children worth standing at, which
        /// is what a single-mesh district looks like.
        /// </summary>
        private int LargestBuildings(Transform art)
        {
            int found = 0;
            for (int c = 0; c < art.childCount; c++)
            {
                Transform kid = art.GetChild(c);
                if (!kid.gameObject.activeInHierarchy) continue;

                var rends = kid.GetComponentsInChildren<Renderer>();
                if (rends.Length == 0) continue;

                Bounds b = rends[0].bounds;
                for (int r = 1; r < rends.Length; r++) b.Encapsulate(rends[r].bounds);
                // Flat things are ground, not somewhere to work beside — a tarmac apron or a decal would
                // otherwise win on footprint alone and take the post.
                if (b.size.y < 1.5f) continue;

                float size = b.extents.x * b.extents.z;
                // Insertion into a fixed top-N. N is the posts per district — two or three — so this
                // stays cheaper than sorting and allocates nothing.
                for (int s = 0; s < _pick.Length; s++)
                {
                    if (s < found && size <= _pickSize[s]) continue;
                    for (int k = Mathf.Min(found, _pick.Length - 1); k > s; k--)
                    {
                        _pick[k] = _pick[k - 1];
                        _pickSize[k] = _pickSize[k - 1];
                    }
                    _pick[s] = b;
                    _pickSize[s] = size;
                    if (found < _pick.Length) found++;
                    break;
                }
            }

            if (found > 0) return found;

            var all = art.GetComponentsInChildren<Renderer>();
            if (all.Length == 0) return 0;
            Bounds whole = all[0].bounds;
            for (int r = 1; r < all.Length; r++) whole.Encapsulate(all[r].bounds);
            _pick[0] = whole;
            return 1;
        }

        private Vector3 Post(Hand h) => _postPos[h.district * _perDistrict + h.slot];
        private Vector3 PostLook(Hand h) => _postLook[h.district * _perDistrict + h.slot];
        private bool PostOk(Hand h) => _postOk[h.district * _perDistrict + h.slot];

        /// <summary>
        /// The clock the island is being ticked at, so the walk cycles keep up with the ground these
        /// bodies cover. Called on change, not per frame — see <c>CoalOperation.TimeScale</c>.
        /// </summary>
        public void SetTimeScale(float scale)
        {
            for (int i = 0; i < _hands.Length; i++) _hands[i].anim.SetSpeed(scale);
        }

        public void Tick(float dt)
        {
            if (_hands.Length == 0) return;
            if (_dirty) { _dirty = false; Refresh(); }

            _poll -= dt;
            if (_poll <= 0f) { _poll = PollSeconds; Poll(); }

            for (int i = 0; i < _hands.Length; i++)
            {
                Hand h = _hands[i];
                if (!h.active) continue;
                Step(h, dt);
            }
        }

        /// <summary>Re-reads who is hired and which districts are running.</summary>
        private void Poll()
        {
            for (int d = 0; d < Districts.Length; d++)
                _busy[d] = Busy != null && Busy(Districts[d]);

            // A district with no station of its own — the town — follows the furthest-advanced station,
            // the same rule the phase controller uses, so the town grows with the island rather than
            // staying a hamlet forever.
            int top = 0;
            for (int d = 0; d < Districts.Length; d++)
                if (Drivers[d] != null && StationLevels != null)
                {
                    int lv = StationLevels(Drivers[d]);
                    if (lv > top) top = lv;
                }

            for (int i = 0; i < _hands.Length; i++)
            {
                Hand h = _hands[i];
                string driver = Drivers[h.district];
                int levels = driver == null ? top
                           : StationLevels != null ? StationLevels(driver) : 0;

                // Slot 0 is free — a district that exists is manned. Every further body has to be paid
                // for, so hiring is a visible side effect of spending rather than something to manage.
                bool on = PostOk(h) && levels >= h.slot * _levelsPerWorker;
                if (on == h.active) continue;

                h.active = on;
                h.t.gameObject.SetActive(on);
                if (!on) continue;

                h.t.position = Post(h);
                h.t.rotation = LookAt(Post(h), PostLook(h));
                h.target = Post(h);
                h.drifting = false;
                h.timer = 1f + h.seed * 3f;
            }
        }

        private void Step(Hand h, float dt)
        {
            Vector3 delta = h.target - h.t.position;
            delta.y = 0f;
            float distance = delta.magnitude;

            if (distance > Arrived)
            {
                h.t.position += delta / distance * Mathf.Min(_walkSpeed * dt, distance);
                h.t.rotation = Quaternion.Slerp(h.t.rotation, Quaternion.LookRotation(delta), 8f * dt);
                h.anim.Set(PersonAnimator.Walk);
                return;
            }

            // At the spot. Settle onto the facing this post wants, rather than staying turned toward
            // wherever the walk happened to come in from — a worker with their back to the furnace is
            // the clearest way to make the whole crew look broken.
            h.t.rotation = Quaternion.Slerp(h.t.rotation, h.facing, 6f * dt);

            if (h.work > 0f)
            {
                h.work -= dt;
                h.anim.Set(PersonAnimator.Wave);
                return;
            }
            h.anim.Set(PersonAnimator.Idle);

            h.timer -= dt;
            if (h.timer > 0f) return;

            if (h.drifting)
            {
                // Wandered far enough — back to the post.
                h.drifting = false;
                h.target = Post(h);
                h.facing = LookAt(Post(h), PostLook(h));
                h.timer = 2.5f + h.seed * 4f;
                return;
            }

            // Standing at the post. Alternate between getting on with the job and shifting about, rather
            // than picking one or the other: a worker who only ever works is a mannequin on a loop, and
            // one who only ever wanders is not working at a station at all. Whichever they did last is
            // what they don't do next.
            if (_busy[h.district] && !h.worked)
            {
                h.worked = true;
                h.work = 0.9f;
                h.timer = 3f + h.seed * 5f;
                return;
            }
            h.worked = false;

            if (_driftRange > 0.01f)
            {
                float ang = (h.seed + Time.time * 0.03f) * Mathf.PI * 2f;
                Vector3 off = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (_driftRange * (0.5f + h.seed * 0.5f));
                h.drifting = true;
                h.target = Post(h) + off;
                h.facing = LookAt(h.target, PostLook(h));
                h.timer = 1.5f + h.seed * 3f;
                return;
            }

            h.timer = 2f + h.seed * 3f;
        }

        /// <summary>A flat look rotation, safe when the two points sit on top of each other.</summary>
        private static Quaternion LookAt(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            d.y = 0f;
            return d.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(d.normalized, Vector3.up) : Quaternion.identity;
        }

        /// <summary>
        /// A body of the right build for this district, or any body at all when the pack wired up has
        /// none of that kind — a half-filled prefab list should staff the island with the wrong people,
        /// not leave it empty.
        /// </summary>
        private static GameObject PickBody(GameObject[] prefabs, string build, int nth)
        {
            int matches = 0;
            for (int i = 0; i < prefabs.Length; i++)
                if (prefabs[i] != null && prefabs[i].name.StartsWith(build, System.StringComparison.OrdinalIgnoreCase))
                    matches++;

            if (matches > 0)
            {
                int want = nth % matches;
                for (int i = 0; i < prefabs.Length; i++)
                {
                    if (prefabs[i] == null ||
                        !prefabs[i].name.StartsWith(build, System.StringComparison.OrdinalIgnoreCase)) continue;
                    if (want-- == 0) return prefabs[i];
                }
            }

            for (int i = 0; i < prefabs.Length; i++)
            {
                GameObject any = prefabs[(nth + i) % prefabs.Length];
                if (any != null) return any;
            }
            return null;
        }
    }
}
