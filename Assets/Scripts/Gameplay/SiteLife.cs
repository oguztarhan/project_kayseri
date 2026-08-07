using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The signs of life on an island: workers walking the site, and smoke rising off the smelter.
    ///
    /// Neither affects the simulation. They exist because a factory where only the vehicles move reads as
    /// a diagram — the eye needs small, continuous motion between the big scripted movements to believe
    /// the place is running. Both are also tied to progress: the crew grows as you buy levels, and the
    /// smoke thickens as the smelter speeds up, so the map keeps reflecting what you have spent.
    ///
    /// Everything is pooled at construction and only ever moved, so a running island allocates nothing.
    /// A plain class rather than a MonoBehaviour: <see cref="CoalOperation"/> already owns the per-island
    /// update order, and this has no independent lifecycle.
    /// </summary>
    public sealed class SiteLife
    {
        private sealed class Walker
        {
            public Transform t;
            public Vector3 a, b;      // the leg this worker paces
            public float u;           // 0..1 along the leg
            public float dir;         // +1 / -1
            public float speed;
            public float wait;        // pause at each end
            public float bob;         // phase offset so the crew doesn't bob in lockstep
            public float held;        // how long this one has been stood at the kerb
        }

        /// <summary>
        /// "Is it unsafe to walk to here?" — asked before a worker takes its next step, and
        /// answered by <see cref="CoalOperation"/>, which is the only thing that knows where
        /// the trucks are.
        ///
        /// A delegate rather than a reference to the operation because this class deliberately
        /// knows nothing about the simulation. It also needs no notion of where the crossings
        /// are: trucks only ever exist on roads, so "a truck is close to my next step" is true
        /// exactly at the places a pedestrian has to give way, and nowhere else.
        /// </summary>
        public System.Func<Vector3, bool> Hazard;

        /// <summary>
        /// How long a worker will hold at the kerb before crossing anyway. Without a limit a
        /// truck parked in its bay next to the pavement would freeze that worker for the rest
        /// of the session.
        /// </summary>
        private const float MaxHold = 6f;

        private readonly Walker[] _walkers;
        private readonly Transform[] _puffs;
        private readonly float[] _puffAge;
        private readonly Vector3 _chimney;
        private readonly float _deckY;
        private readonly float _puffLife, _puffRise, _puffSpread;

        private int _crew;
        private float _spawnTimer;

        public SiteLife(Transform parent, GameObject workerPrefab, GameObject puffPrefab, Material puffMat,
                        Vector3[] patrol, Vector3 chimney, float deckY, float workerScale,
                        int maxWorkers, int maxPuffs, float puffLife, float puffRise, float puffSpread)
        {
            _chimney = chimney;
            _deckY = deckY;
            _puffLife = Mathf.Max(0.2f, puffLife);
            _puffRise = puffRise;
            _puffSpread = puffSpread;

            // ---- crew ----
            _walkers = new Walker[workerPrefab != null && patrol != null && patrol.Length >= 2 ? maxWorkers : 0];
            for (int i = 0; i < _walkers.Length; i++)
            {
                var go = Object.Instantiate(workerPrefab, parent);
                go.name = "OpWorker" + i;
                go.transform.localScale = Vector3.one * workerScale;

                // Spread the crew over the legs between patrol points, and stagger where each one starts
                // so they never line up into a marching column.
                //
                // Spread, not wrapped: with a long path (the authored pavement is 50 points) `i % legs`
                // put the whole crew on the first few legs, all bunched in one corner. Walking short
                // consecutive legs also keeps them ON the pavement - stretching 8 legs around a rounded
                // circuit made each one a chord that cut the corners by up to 2.6m.
                int leg = _walkers.Length <= 1 ? 0
                        : (i * (patrol.Length - 1)) / _walkers.Length;
                var w = new Walker
                {
                    t = go.transform,
                    a = patrol[leg],
                    b = patrol[leg + 1],
                    u = (i * 0.37f) % 1f,
                    dir = (i % 2 == 0) ? 1f : -1f,
                    speed = 0.055f + (i % 3) * 0.012f,
                    bob = i * 1.7f,
                };
                _walkers[i] = w;
                go.SetActive(false);
            }

            // ---- smoke ----
            _puffs = new Transform[puffPrefab != null ? maxPuffs : 0];
            _puffAge = new float[_puffs.Length];
            for (int i = 0; i < _puffs.Length; i++)
            {
                var go = Object.Instantiate(puffPrefab, parent);
                go.name = "OpSmoke" + i;
                if (puffMat != null)
                {
                    var rs = go.GetComponentsInChildren<Renderer>(true);
                    for (int r = 0; r < rs.Length; r++) rs[r].sharedMaterial = puffMat;
                }
                _puffs[i] = go.transform;
                _puffAge[i] = _puffLife;      // start expired so nothing pops in on frame one
                go.SetActive(false);
            }
        }

        /// <summary>How many of the pooled workers are on shift. Driven by the island's upgrade levels.</summary>
        public void SetCrew(int n)
        {
            n = Mathf.Clamp(n, 0, _walkers.Length);
            if (n == _crew) return;
            for (int i = 0; i < _walkers.Length; i++)
            {
                bool on = i < n;
                if (_walkers[i].t.gameObject.activeSelf != on) _walkers[i].t.gameObject.SetActive(on);
            }
            _crew = n;
        }

        /// <param name="puffsPerSecond">Smelter output, so a faster smelter visibly smokes harder.</param>
        public void Tick(float dt, float puffsPerSecond)
        {
            TickWalkers(dt);
            TickSmoke(dt, puffsPerSecond);
        }

        private void TickWalkers(float dt)
        {
            for (int i = 0; i < _crew; i++)
            {
                Walker w = _walkers[i];
                if (w.wait > 0f) { w.wait -= dt; continue; }

                // Look where the next step lands before taking it. The ring pavement crosses
                // both arterials, so a worker who never checked walked straight through the
                // traffic - and out the other side of it.
                float next = Mathf.Clamp01(w.u + w.dir * w.speed * dt);
                if (Hazard != null && w.held < MaxHold && Hazard(Vector3.Lerp(w.a, w.b, next)))
                {
                    w.held += dt;
                    continue;                      // stand at the kerb and let it pass
                }
                w.held = 0f;

                w.u = next;
                if (w.u >= 1f) { w.u = 1f; w.dir = -1f; w.wait = 0.6f + (i % 4) * 0.35f; }
                else if (w.u <= 0f) { w.u = 0f; w.dir = 1f; w.wait = 0.6f + (i % 4) * 0.35f; }

                Vector3 p = Vector3.Lerp(w.a, w.b, w.u);
                // Height comes from the leg being walked, not from one island-wide deck value:
                // the pavement climbs with the ground now, and _deckY is a single sample of one
                // building's pivot. A small vertical bob on top sells "walking" without a rig.
                p.y += Mathf.Abs(Mathf.Sin(Time.time * 6f + w.bob)) * 0.16f;
                w.t.position = p;

                Vector3 face = (w.b - w.a) * w.dir;
                face.y = 0f;
                if (face.sqrMagnitude > 0.0001f) w.t.rotation = Quaternion.LookRotation(face.normalized, Vector3.up);
            }
        }

        private void TickSmoke(float dt, float puffsPerSecond)
        {
            if (_puffs.Length == 0) return;

            // Age and drift everything already in the air.
            for (int i = 0; i < _puffs.Length; i++)
            {
                if (_puffAge[i] >= _puffLife) continue;
                _puffAge[i] += dt;
                float k = _puffAge[i] / _puffLife;          // 0 = just born, 1 = gone
                if (k >= 1f)
                {
                    _puffs[i].gameObject.SetActive(false);
                    continue;
                }
                Transform t = _puffs[i];
                Vector3 p = t.position;
                p.y += _puffRise * dt;
                t.position = p;
                // Billow out as it rises, then thin away to nothing so there is no hard pop on recycle.
                float s = Mathf.Lerp(0.35f, 1.6f, k) * (1f - k * k * 0.55f);
                t.localScale = Vector3.one * s;
                t.Rotate(0f, 22f * dt, 0f, Space.Self);
            }

            if (puffsPerSecond <= 0.01f) return;
            _spawnTimer -= dt;
            if (_spawnTimer > 0f) return;
            _spawnTimer = 1f / puffsPerSecond;

            for (int i = 0; i < _puffs.Length; i++)
            {
                if (_puffAge[i] < _puffLife) continue;       // still airborne
                _puffAge[i] = 0f;
                Transform t = _puffs[i];
                // Deterministic scatter around the stack: cheaper than Random and repeats slowly enough
                // that the eye reads it as turbulence rather than a pattern.
                float ang = i * 2.39996f;
                Vector3 off = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (_puffSpread * ((i % 3) * 0.4f + 0.3f));
                t.position = _chimney + off;
                t.localScale = Vector3.one * 0.35f;
                t.gameObject.SetActive(true);
                return;                                     // one puff per interval
            }
        }
    }
}
