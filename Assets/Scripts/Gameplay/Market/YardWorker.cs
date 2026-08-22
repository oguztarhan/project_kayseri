using Game.Core;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// A hired body doing one of the yard's three jobs — for real, not as decoration.
    ///
    /// A carrier walks to the pad, lifts a bar off the stock the island delivered, walks it to the
    /// counter and puts it down. A cashier stands at the counter and is the reason the queue moves at
    /// all. A collector sweeps the notes off the floor. Between them they run the same loop the player
    /// runs, which is the whole point of paying for them: the yard keeps working while you watch, and
    /// then while you don't.
    ///
    /// This is why <see cref="MarketService.SetSimulatedYard"/> exists. The ledger sells a yard's stock
    /// once a second at <see cref="MarketFlow.ServiceRate"/> — that is how the seven yards you are NOT
    /// in keep earning. In the one you are in, these workers are moving the same bars by hand, so the
    /// ledger stands down and what happens on screen is what gets paid. Without that flag every bar
    /// would be sold twice: once by a worker and once by arithmetic.
    /// </summary>
    public sealed class YardWorker : MonoBehaviour
    {
        /// <summary>Which of the three jobs this body is doing. Matches <see cref="YardUpgrade"/>'s hires.</summary>
        public enum Job { Carry, Serve, Collect }

        [Tooltip("Yürüme hızı. Seviyeyle artar, böylece iyi ödenen eleman gözle görülür hızlanır.")]
        [SerializeField, Min(0.5f)] private float baseSpeed = 4.5f;

        [Tooltip("Her seviyenin eklediği hız.")]
        [SerializeField, Min(0f)] private float speedPerLevel = 0.9f;

        [Tooltip("Külçeyi alıp bırakma süresi. Seviyeyle kısalır.")]
        [SerializeField, Min(0.02f)] private float handleSeconds = 0.32f;

        [Tooltip("Aynı anda taşıyabileceği külçe. Seviyeyle artar.")]
        [SerializeField, Min(1)] private int baseLoad = 2;

        [Tooltip("Yapacak iş bulamayınca bu kadar bekleyip yeniden bakar.")]
        [SerializeField, Min(0.1f)] private float idleSeconds = 0.6f;

        private enum Step { ToPickup, Loading, ToDrop, Unloading, Idle }

        private Job _job;
        private Transform _body;
        private MarketService _market;
        private string _yardKey;
        private SellCounter _counter;
        private CashFloor _cash;

        private Vector3 _pickup, _drop;
        private float _speed, _handle;
        private int _capacity, _held;
        private Step _step = Step.ToPickup;
        private float _timer;
        private Transform[] _load;        // the bars visibly on the worker's shoulder
        private PersonAnimator _anim;
        private Vector3? _facing;         // cashier only: who they are dealing with
        private float _serving;           // cashier only: how long the handover gesture has left

        /// <summary>How many bars this worker is holding. The yard's readouts can ask.</summary>
        public int Held => _held;

        /// <summary>Puts a worker to work. The spots are world space — a yard sits along a row.</summary>
        public void Configure(Job job, MarketService market, string yardKey, SellCounter counter,
                              CashFloor cash, Vector3 pickup, Vector3 drop, int level,
                              MarketPrefabs prefabs, Material ore, Material tint)
        {
            _job = job;
            _market = market;
            _yardKey = yardKey;
            _counter = counter;
            _cash = cash;
            _pickup = pickup;
            _drop = drop;

            _body = prefabs != null
                ? prefabs.SpawnPerson(prefabs.Worker, transform, "Eleman_" + job,
                                      new Vector3(0.85f, 0.9f, 0.85f), tint)
                : MarketPrefabs.Spawn(null, transform, "Eleman_" + job,
                                      PrimitiveType.Capsule, new Vector3(0.85f, 0.9f, 0.85f), tint);
            _body.position = job == Job.Carry ? pickup : drop;
            _anim = new PersonAnimator(_body);

            BuildLoadVisual(prefabs, ore);
            SetLevel(level);

            // Registered HERE, not only in OnEnable. Unity raises OnEnable the instant the component is
            // added, which is before this method has run — so that pass saw a null floor and a null
            // body and silently did nothing, and the collector you paid for never picked up a note.
            if (_job == Job.Collect && _cash != null) _cash.AddCollector(_body);
        }

        /// <summary>A raise: faster on their feet, quicker with their hands, and carrying more.</summary>
        public void SetLevel(int level)
        {
            int steps = Mathf.Max(0, level - 1);
            _speed = baseSpeed + speedPerLevel * steps;
            _handle = handleSeconds / (1f + 0.35f * steps);
            _capacity = baseLoad + steps;
            if (_load != null && _capacity > _load.Length) _capacity = _load.Length;
        }

        /// <summary>
        /// Re-registers a collector when its yard is woken up again. Both halves guard on
        /// <see cref="_body"/>, because this pair also runs once before <see cref="Configure"/> has
        /// filled anything in.
        /// </summary>
        private void OnEnable()
        {
            if (_job == Job.Collect && _cash != null && _body != null) _cash.AddCollector(_body);
        }

        private void OnDisable()
        {
            if (_job == Job.Collect && _cash != null && _body != null) _cash.RemoveCollector(_body);
        }

        private void Update()
        {
            if (_body == null || _market == null) return;
            float dt = Time.deltaTime;

            switch (_job)
            {
                case Job.Carry: TickCarrier(dt); break;
                case Job.Serve: TickCashier(dt); break;
                default: TickCollector(dt); break;
            }
        }

        // ---------------------------------------------------------------- the carrier
        /// <summary>
        /// Pad → counter, over and over. The bar leaves the ledger's stock the moment it is lifted and
        /// reaches the counter's shelf when it is set down — exactly the two calls the player's own
        /// hands make. There is no separate path for staff, which is why they cannot drift apart.
        /// </summary>
        private void TickCarrier(float dt)
        {
            switch (_step)
            {
                case Step.ToPickup:
                    if (!Walk(_pickup, dt)) return;
                    _step = Step.Loading;
                    _timer = 0f;
                    return;

                case Step.Loading:
                    _timer -= dt;
                    if (_timer > 0f) return;
                    if (_held >= _capacity) { _step = Step.ToDrop; return; }
                    // Nothing on the pad. Take what we already have, or wait for the lorries.
                    if (_market.TakeFromStock(_yardKey, 1d) <= 0d)
                    {
                        if (_held > 0) _step = Step.ToDrop; else Idle();
                        return;
                    }
                    ShowLoad(++_held);
                    _timer = _handle;
                    return;

                case Step.ToDrop:
                    if (!Walk(_drop, dt)) return;
                    _step = Step.Unloading;
                    _timer = 0f;
                    return;

                case Step.Unloading:
                    _timer -= dt;
                    if (_timer > 0f) return;
                    if (_held <= 0) { _step = Step.ToPickup; return; }
                    // A full shelf is not a reason to tip the load on the floor — hold it and wait.
                    if (_counter == null || !_counter.TryStock()) { _timer = idleSeconds; return; }
                    ShowLoad(--_held);
                    _timer = _handle;
                    return;

                case Step.Idle:
                    _timer -= dt;
                    if (_timer <= 0f) _step = Step.ToPickup;
                    return;
            }
        }

        // ---------------------------------------------------------------- the cashier
        /// <summary>
        /// Stands at the counter and works it. Being there IS the job: <see cref="CustomerQueue"/> will
        /// not serve anyone unless somebody is behind the counter, and this is the somebody you paid
        /// for so that it need not be you.
        /// </summary>
        private void TickCashier(float dt)
        {
            if (!Walk(_drop, dt)) return;

            // At their post. Facing whoever they are dealing with, rather than facing wherever they
            // happened to arrive from — a cashier staring at a wall while the queue moves behind them
            // is the single clearest way to make a hire you paid for look broken.
            if (_facing.HasValue)
            {
                Vector3 toward = _facing.Value - _body.position;
                toward.y = 0f;
                if (toward.sqrMagnitude > 0.01f)
                    _body.rotation = Quaternion.Slerp(_body.rotation, Quaternion.LookRotation(toward), 8f * dt);
            }

            // Idle between customers and wave through a sale, so the gesture means something. Waving
            // non-stop reads as a mannequin on a loop.
            _serving -= dt;
            _anim.Set(_serving > 0f ? PersonAnimator.Wave : PersonAnimator.Idle);
        }

        /// <summary>Who the cashier should be looking at — the customer at the head of the queue.</summary>
        public void FacePoint(Vector3 world) => _facing = world;

        /// <summary>A sale just happened at this counter: hand it over.</summary>
        public void PlayServe() => _serving = 0.8f;

        /// <summary>Whether a cashier is at their post, which is what lets the queue move.</summary>
        public bool AtCounter => _job == Job.Serve && _body != null &&
                                 (_body.position - _drop).sqrMagnitude < 4f;

        // ---------------------------------------------------------------- the collector
        /// <summary>Walks to the nearest note; the floor's own magnet does the banking.</summary>
        private void TickCollector(float dt)
        {
            Vector3 note = _drop;
            if (_cash != null && _cash.TryNearestNote(_body.position, out note)) Walk(note, dt);
            else Walk(_drop, dt);
        }

        // ---------------------------------------------------------------- shared
        private void Idle()
        {
            _step = Step.Idle;
            _timer = idleSeconds;
        }

        /// <summary>True once the body has arrived.</summary>
        private bool Walk(Vector3 to, float dt)
        {
            Vector3 delta = to - _body.position;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance <= 0.18f) { _anim.Set(PersonAnimator.Idle); return true; }

            _body.position += delta / distance * Mathf.Min(_speed * dt, distance);
            _body.rotation = Quaternion.Slerp(_body.rotation, Quaternion.LookRotation(delta), 12f * dt);
            _anim.Set(PersonAnimator.Walk);
            return false;
        }

        /// <summary>The bars on the shoulder: one object per unit of capacity, shown as far as held.</summary>
        private void BuildLoadVisual(MarketPrefabs prefabs, Material ore)
        {
            if (_job != Job.Carry) return;
            const int most = 8;
            _load = new Transform[most];
            for (int i = 0; i < most; i++)
            {
                Transform bar = MarketPrefabs.SpawnCargo(prefabs != null ? prefabs.Bar : null, _body,
                                                         "Yuk", new Vector3(1.1f, 0.3f, 0.65f), ore);
                bar.localPosition = new Vector3(0f, 1.15f + i * 0.32f, 0f);
                bar.gameObject.SetActive(false);
                _load[i] = bar;
            }
        }

        private void ShowLoad(int held)
        {
            if (_load == null) return;
            for (int i = 0; i < _load.Length; i++)
                if (_load[i] != null) _load[i].gameObject.SetActive(i < held);
        }
    }
}
