using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The hired foremen, standing at the stations they run.
    ///
    /// The roster in <see cref="Game.Core.Foremen"/> is a screen full of numbers, and numbers are what
    /// the game already had too many of. A foreman you paid gems for should be somewhere on the island,
    /// visibly in charge of the thing he makes faster — that is the whole reason the genre's managers
    /// are people rather than upgrade rows.
    ///
    /// WHY NOT STATION CREW. <see cref="StationCrew"/> posts staff by DISTRICT — Mine, Refinery, Depot,
    /// Market, Port, Power, Haul, Fleet, Civic — and the foreman roster is by STATION. The two nearly
    /// line up and then do not: TRAIN owns no district at all, and CARGO TRUCKS owns two. So a foreman
    /// hangs off <see cref="CoalOperation.StationAnchor"/> instead, which answers for all eight
    /// stations including the two that are a midpoint between buildings rather than a building.
    ///
    /// WHAT MAKES HIM READ AS A FOREMAN. He is a little larger than the crew, and he does not walk.
    /// Everyone else on the island drifts between posts; he stands where he was put and gestures. That
    /// contrast is doing the work, because there is no foreman ART — these are the same bodies the crew
    /// uses. A proper hard hat and hi-vis would be better and is a job for the art pipeline, not for
    /// code inventing a look out of tint slots and breaking the palette batch to do it.
    ///
    /// Instantiated once and thereafter only shown, hidden or re-posted, so a running island allocates
    /// nothing. A plain class rather than a MonoBehaviour, for the same reason StationCrew and SiteLife
    /// are: CoalOperation already owns the per-island update order.
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

        private sealed class Boss
        {
            public Transform t;
            public PersonAnimator anim;
            public float seed;        // so eight of them never gesture on the same beat
            public float timer;
            public bool gesturing;
            public bool placed;
            public bool active;
        }

        private readonly Boss[] _bosses;
        private float _timeScale = 1f;

        public StationForemen(Transform parent, GameObject[] prefabs, int stationCount, float scale)
        {
            bool haveBodies = false;
            if (prefabs != null)
                for (int i = 0; i < prefabs.Length && !haveBodies; i++) haveBodies = prefabs[i] != null;

            // No bodies wired: no foremen, and every method below is a no-op rather than a null deref.
            _bosses = new Boss[haveBodies ? Mathf.Max(0, stationCount) : 0];

            for (int i = 0; i < _bosses.Length; i++)
            {
                var go = Object.Instantiate(Pick(prefabs, i), parent);
                go.name = "OpForeman_" + i;
                go.transform.localScale = Vector3.one * scale;
                _bosses[i] = new Boss
                {
                    t = go.transform,
                    anim = new PersonAnimator(go.transform),
                    // Golden-angle stagger, the same trick StationCrew uses and for the same reason.
                    seed = (i * 2.39996f) % 1f,
                };
                go.SetActive(false);
            }
        }

        /// <summary>
        /// Prefers the sturdiest build in the pack, so a foreman is not the same silhouette as the
        /// labourer next to him. Falls back to whatever is wired.
        /// </summary>
        private static GameObject Pick(GameObject[] prefabs, int i)
        {
            GameObject fallback = null;
            for (int p = 0; p < prefabs.Length; p++)
            {
                if (prefabs[p] == null) continue;
                if (fallback == null) fallback = prefabs[p];
                if (prefabs[p].name.StartsWith("strong", System.StringComparison.OrdinalIgnoreCase))
                    return prefabs[p];
            }
            return fallback;
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

                if (b.active == want) continue;
                b.active = want;
                b.t.gameObject.SetActive(want);
                if (want) b.anim.Set(PersonAnimator.Idle);
            }
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
