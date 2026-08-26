using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The small, steady judder of machinery that is running: crushers, conveyors, excavators, gantries
    /// and winders settling on a short stroke and easing back up, a few centimetres at a time.
    ///
    /// Two rules decide everything here, and both were learned the hard way.
    ///
    /// ONE: only MACHINES move. A concrete hall does not squash, a warehouse does not breathe, and
    /// animating them said "this island is made of rubber" far louder than it said "this island is
    /// working". So the structures are matched against a list of things that genuinely have moving
    /// parts, and everything else — halls, offices, sheds, silos, stalls, the whole built environment —
    /// is left perfectly still. That stillness is what the motion reads against.
    ///
    /// TWO: the travel is measured in METRES, not in percent. A percentage makes a big gantry lurch and
    /// a small loader twitch off the same number, which is exactly backwards — real machinery of any
    /// size shakes by about the same small distance. A few centimetres on a six-metre crusher reads as
    /// a machine under load. The same fraction expressed as scale read as a bouncy toy.
    ///
    /// The stroke is asymmetric: a quick press down, a slower ride back up. A sine gives both halves
    /// equal time, which reads as breathing rather than as a machine completing a cycle.
    ///
    /// It animates the district ART rather than the station landmarks <see cref="CoalOperation"/> scales
    /// on purchase, because on an authored island those landmarks are EMPTY anchors — objects created at
    /// an exported point so the simulation has somewhere to drive to. Scaling one moves nothing a player
    /// can see. The machines are the phase roots' district children, which is also what
    /// <see cref="StationCrew"/> measures its posts against.
    ///
    /// Only a busy district runs, which is the point: a still market means bars are not arriving, so the
    /// bottleneck is readable off the map instead of only off the HUD.
    ///
    /// Everything is collected on a phase change and only ever written to, so a running island allocates
    /// nothing. A plain class rather than a MonoBehaviour, for the same reason <see cref="SiteLife"/> is
    /// one: <see cref="CoalOperation"/> already owns the per-island update order.
    /// </summary>
    public sealed class BuildingPulse
    {
        /// <summary>
        /// The districts whose art can be at work. The same names
        /// <see cref="Kayseri.Island.IslandPhaseController.ActiveDistrict"/> hands back, and the same set
        /// <see cref="StationCrew"/> posts staff at, minus Civic — the town has no machinery in it.
        /// </summary>
        private static readonly string[] Districts =
        { "Mine", "Refinery", "Depot", "Market", "Port", "Power", "Haul", "Fleet" };

        /// <summary>
        /// The only things that move. An ALLOW list rather than a skip list, and that is the whole design:
        /// with a skip list every structure the exporter ever adds animates by default and has to be
        /// argued out of it one name at a time, which is how offices and warehouses ended up pumping.
        /// Named after the exporter's own objects — Mine.Crusher, Depot.ConvMain, Port.Derrick.
        /// </summary>
        private static readonly string[] MachineWords =
        { "Crusher", "Winder", "Headframe", "Excav", "Loader", "Conv", "Gantry",
          "Hopper", "Crane", "Derrick", "Reach", "Fork", "Intake", "Pump", "Drill", "Mill" };

        /// <summary>Below this in any horizontal direction, or in height, it is a decal or a marker.</summary>
        private const float MinSize = 1.5f;

        /// <summary>Fraction of each cycle spent on the down-stroke. The rest is the ride back up, so the
        /// press is roughly three times faster than the release.</summary>
        private const float Stroke = 0.28f;

        /// <summary>How often the busy flags are re-read. They change on the scale of smelter cycles and
        /// truck arrivals, so polling them per frame buys nothing — same reason
        /// <see cref="StationCrew"/> polls.</summary>
        private const float PollSeconds = 0.4f;

        private struct Body
        {
            public Transform t;
            public Vector3 basePos;   // its resting localPosition — the only thing this class writes
            public float unit;        // local units per world metre, so travel is the same on any rig
            public int district;
            public float seed;        // phase offset, so a yard's machines do not stamp in unison
            public float amp;         // eased 0..1 — how much of the stroke is currently showing
            public float surge;       // seconds of "just produced something" left
            public bool posed;        // it is currently off its resting spot, so it needs restoring
        }

        /// <summary>"Is this district actually running right now?" — the only thing that starts the
        /// machines. A delegate rather than a reference to the operation, so this class knows nothing
        /// about the simulation, exactly like <see cref="StationCrew.Busy"/>.</summary>
        public System.Func<string, bool> Busy;

        private readonly Kayseri.Island.IslandPhaseController _phases;
        private readonly Body[] _bodies;
        private readonly bool[] _busy;
        private readonly int _perDistrict;
        private readonly float _travel, _rate;
        private readonly float _surgeTravel, _surgeSeconds;
        private readonly float _rampSeconds;

        private int _count;
        private float _poll;
        private bool _dirty;

        /// <param name="travel">How far a machine settles, in WORLD METRES. Centimetres, not a fraction.</param>
        public BuildingPulse(Kayseri.Island.IslandPhaseController phases, int maxBodies, int perDistrict,
                             float travel, float rate, float surgeTravel, float surgeSeconds,
                             float rampSeconds)
        {
            _phases = phases;
            _perDistrict = Mathf.Max(1, perDistrict);
            _travel = Mathf.Max(0f, travel);
            _rate = Mathf.Max(0.01f, rate);
            _surgeTravel = Mathf.Max(0f, surgeTravel);
            _surgeSeconds = Mathf.Max(0.05f, surgeSeconds);
            _rampSeconds = Mathf.Max(0.05f, rampSeconds);

            // No phase art means a generated island, whose buildings are the station landmarks the
            // operation already scales itself. Everything below then turns into a no-op rather than a
            // null reference.
            _bodies = new Body[phases != null ? Mathf.Max(0, maxBodies) : 0];
            _busy = new bool[Districts.Length];

            Refresh();
            if (_phases != null) _phases.PhaseChanged += OnPhaseChanged;
        }

        /// <summary>Drops the phase subscription. The island root going away takes the art with it.</summary>
        public void Dispose()
        {
            if (_phases != null) _phases.PhaseChanged -= OnPhaseChanged;
        }

        /// <summary>
        /// Marks the collection stale rather than rebuilding on the spot. One purchase can advance
        /// several districts at once, and each would otherwise walk the island's renderers again inside
        /// the same frame — the same reason <see cref="StationCrew"/> defers its own re-measure.
        /// </summary>
        private void OnPhaseChanged(string district, int phase) => _dirty = true;

        /// <summary>
        /// Re-collects the machines from whichever phase of each district is on show.
        ///
        /// Called on a phase change and a few times over the first seconds of an island: the mine yard is
        /// arranged at runtime, so the art a district measures at Start is not always where it settles.
        /// </summary>
        public void Refresh()
        {
            if (_bodies.Length == 0) return;

            // Anything about to be dropped from the collection is put back where it was found first, or a
            // district that rebuilds mid-stroke leaves its old machines sunk into the ground.
            for (int i = 0; i < _count; i++) Restore(ref _bodies[i]);
            _count = 0;

            for (int d = 0; d < Districts.Length && _count < _bodies.Length; d++)
            {
                Transform art = _phases.ActiveDistrict(Districts[d]);
                if (art == null) continue;

                int taken = 0;
                for (int c = 0; c < art.childCount && taken < _perDistrict && _count < _bodies.Length; c++)
                {
                    Transform kid = art.GetChild(c);
                    if (!kid.gameObject.activeInHierarchy || !IsMachine(kid)) continue;

                    // The travel is a world distance, but localPosition is what gets written, so it has
                    // to be converted through whatever scaling sits above this object. Islands are
                    // authored at scale 1 today; this is what stops that being load-bearing.
                    float lossy = kid.parent != null ? kid.parent.lossyScale.y : 1f;

                    _bodies[_count] = new Body
                    {
                        t = kid,
                        basePos = kid.localPosition,
                        unit = Mathf.Abs(lossy) > 1e-4f ? 1f / lossy : 1f,
                        district = d,
                        // Golden-angle stagger: cheaper than Random, and it repeats slowly enough that a
                        // yard never falls into step with itself. Every machine on one beat would read as
                        // the ground shaking rather than as separate machines running.
                        seed = (_count * 2.39996f) % 1f,
                    };
                    _count++;
                    taken++;
                }
            }
        }

        /// <summary>
        /// Whether this child is a machine with moving parts, and big enough to be the machine rather
        /// than a marker standing next to it.
        /// </summary>
        private static bool IsMachine(Transform kid)
        {
            if (!Matches(kid.name, MachineWords)) return false;

            var rends = kid.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return false;

            Bounds b = rends[0].bounds;
            for (int r = 1; r < rends.Length; r++) b.Encapsulate(rends[r].bounds);
            return b.size.y >= MinSize && Mathf.Max(b.size.x, b.size.z) >= MinSize;
        }

        private static bool Matches(string name, string[] words)
        {
            for (int i = 0; i < words.Length; i++)
                if (name.IndexOf(words[i], System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>
        /// A production beat in one district — one conversion smelted, one load tipped, one sale made.
        /// The machines there take the stroke a little deeper for a moment rather than doing something
        /// else: a second gesture layered on top of the first is what made this read as wobble.
        /// </summary>
        public void Punch(string district)
        {
            if (_count == 0 || string.IsNullOrEmpty(district)) return;
            int d = -1;
            for (int i = 0; i < Districts.Length; i++)
                if (Districts[i] == district) { d = i; break; }
            if (d < 0) return;

            for (int i = 0; i < _count; i++)
                if (_bodies[i].district == d) _bodies[i].surge = _surgeSeconds;
        }

        public void Tick(float dt)
        {
            // Before the empty check, not after: a district whose art was not up yet when this was built
            // collects nothing, and testing the count first would mean that island never re-collected on
            // the phase change that finally builds it.
            if (_dirty) { _dirty = false; Refresh(); }
            if (_count == 0) return;

            _poll -= dt;
            if (_poll <= 0f)
            {
                _poll = PollSeconds;
                for (int d = 0; d < Districts.Length; d++)
                    _busy[d] = Busy != null && Busy(Districts[d]);
            }

            float now = Time.time;
            for (int i = 0; i < _count; i++) Step(ref _bodies[i], dt, now);
        }

        private void Step(ref Body b, float dt, float now)
        {
            if (b.t == null) return;

            // The stroke fades in and out rather than switching, so a district that stops working winds
            // down instead of stopping dead with its machines half sunk.
            float want = _busy[b.district] ? 1f : 0f;
            b.amp = Mathf.MoveTowards(b.amp, want, dt / _rampSeconds);
            if (b.surge > 0f) b.surge = Mathf.Max(0f, b.surge - dt);

            if (b.amp <= 0.0001f)
            {
                Restore(ref b);
                return;
            }

            float metres = (_travel + _surgeTravel * (b.surge / _surgeSeconds)) * b.amp * Stroke01(now, b.seed);

            // Straight down and back, and nothing else. Down rather than up because a machine under load
            // settles onto its mounts — and because it keeps the resting pose the one the art was
            // authored at, so a machine that stops working is exactly where the modeller put it.
            b.t.localPosition = new Vector3(b.basePos.x,
                                            b.basePos.y - metres * b.unit,
                                            b.basePos.z);
            b.posed = true;
        }

        /// <summary>
        /// The stroke, 0 at rest and 1 fully settled. Quick in over <see cref="Stroke"/> of the cycle,
        /// slow back out over the remainder — the asymmetry is what makes it a machine cycling rather
        /// than something breathing.
        /// </summary>
        private float Stroke01(float now, float seed)
        {
            float u = now * _rate + seed;
            u -= Mathf.Floor(u);                       // Repeat(u, 1) without the call
            return u < Stroke
                 ? Mathf.Sin(u / Stroke * Mathf.PI * 0.5f)
                 : Mathf.Cos((u - Stroke) / (1f - Stroke) * Mathf.PI * 0.5f);
        }

        /// <summary>Puts a machine back exactly where it was found — and only once, so a district that
        /// has been idle for an hour costs nothing but the ramp check above.</summary>
        private static void Restore(ref Body b)
        {
            if (!b.posed || b.t == null) return;
            b.t.localPosition = b.basePos;
            b.posed = false;
        }
    }
}
