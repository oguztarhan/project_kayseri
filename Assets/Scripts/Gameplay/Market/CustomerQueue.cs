using System.Collections.Generic;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The line of customers, and the thing that turns a stocked counter into money.
    ///
    /// Waypoints, not NavMesh. A queue is a row of fixed spots that shuffle forward — the one movement
    /// problem that needs no pathfinding at all — and eight yards of NavMeshAgents would cost real
    /// frames on a mid-range phone for a walk down a straight line. Bodies are pooled for the same
    /// reason: a busy counter serves one every second or so, forever.
    ///
    /// How many stand in line is the queue-slot upgrade, and how fast they arrive scales with it, so
    /// buying a slot lengthens the line rather than leaving a longer line half empty.
    /// </summary>
    public sealed class CustomerQueue : MonoBehaviour
    {
        [Tooltip("İki sıradaki müşteri arasındaki mesafe. Gövdeler büyüdükçe bu da büyümeli, " +
                 "yoksa sıradakiler birbirinin içine girer.")]
        [SerializeField, Min(0.5f)] private float spacing = 4.2f;

        [Tooltip("Bir müşterinin alabileceği en az ve en çok külçe. Herkesin bir tane alması " +
                 "kuyruğu bir sayaca çevirir; değişken miktar onu alışverişe çevirir.")]
        [SerializeField, Min(1)] private int minPurchase = 1;
        [SerializeField, Min(1)] private int maxPurchase = 4;

        [Tooltip("Müşterinin yürüme hızı.")]
        [SerializeField, Min(0.5f)] private float walkSpeed = 4.5f;

        [Tooltip("İki gövdenin birbirine yaklaşabileceği en kısa mesafe. Çarpıştırıcı yok, " +
                 "iç içe geçmelerini engelleyen tek şey bu.")]
        [SerializeField, Min(0.1f)] private float personalSpace = 2.2f;

        [Tooltip("Ayırma turu kaç kez tekrarlansın. Bir tur iki gövdeyi ayırır ama üçüncüsünün " +
                 "içine itebilir; birkaç tur kalabalık bir kuyruğu tamamen çözer.")]
        [SerializeField, Range(1, 6)] private int separationPasses = 3;

        [Tooltip("Tezgâhın başındaki müşterinin külçeyi alması süresi.")]
        [SerializeField, Min(0.05f)] private float serveSeconds = 0.55f;

        [Tooltip("Sıra doluyken bile yeni müşteri arası en kısa süre, tam dolu sırada.")]
        [SerializeField, Min(0.2f)] private float arrivalSecondsAtFullQueue = 1.1f;

        [Tooltip("Tek sıralı avluda müşteri arası süre. Sıra yeri aldıkça bu süre kısalır.")]
        [SerializeField, Min(0.3f)] private float arrivalSecondsAtOneSlot = 3.4f;

        /// <summary>
        /// Arriving and leaving are two legs, not one: through the door, then to the slot. A customer
        /// walking straight from outside to their place in the line would cut the corner and clip the
        /// wall, and the whole reason the door exists is that the walk through it should be the thing
        /// you see rather than a body switching on.
        /// </summary>
        private enum Step { Entering, Walking, Waiting, Serving, Leaving, Departing }

        private sealed class Customer
        {
            public Transform body;
            public PersonAnimator anim;
            public int slot;          // -1 while leaving
            public Step step;
            public float timer;
            public Vector3 target;
            public int wants;         // how many bars this one came in for
        }

        private readonly List<Customer> _live = new List<Customer>();
        private readonly Stack<Customer> _spare = new Stack<Customer>();

        private MarketService _market;
        private string _yardKey;
        private SellCounter _counter;
        private CashFloor _cash;
        private Material _material;
        private AudioService _audio;
        private MarketPrefabs _prefabs;
        private int _spawned;        // how many bodies have ever been made, for picking their looks

        /// <summary>The art a customer is made of. Set before <see cref="Configure"/> if it is being set at all.</summary>
        public void SetPrefabs(MarketPrefabs prefabs) => _prefabs = prefabs;

        private Vector3 _head;       // where the front of the line stands
        private Vector3 _along;      // unit vector from the head back down the line
        private Vector3 _door;       // the opening in the wall, on the wall line
        private Vector3 _outside;    // beyond it, where the wall hides them
        private float _arrivalIn;

        public void Configure(MarketService market, string yardKey, SellCounter counter, CashFloor cash,
                              Vector3 head, Vector3 along, Vector3 door, Vector3 outside)
        {
            _market = market;
            _yardKey = yardKey;
            _counter = counter;
            _cash = cash;
            _audio = Game.Core.ServiceLocator.Get<AudioService>();
            _head = head;
            _along = along.normalized;
            _door = door;
            _outside = outside;

            _material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var colour = new Color(0.44f, 0.52f, 0.63f);
            _material.color = colour;
            if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", colour);
        }

        private int Slots
        {
            get
            {
                if (_market == null) return 1;
                int slots = _market.Row(_yardKey).queueSlots;
                return Mathf.Clamp(slots, 1, Game.Core.MarketFlow.MaxQueueSlots);
            }
        }

        private Vector3 SlotPosition(int slot) => _head + _along * (spacing * slot);

        private void Update()
        {
            float dt = Time.deltaTime;
            Arrivals(dt);
            for (int i = _live.Count - 1; i >= 0; i--) Step_(_live[i], i, dt);
            for (int pass = 0; pass < separationPasses; pass++) Separate();
        }

        /// <summary>
        /// Pushes bodies that have ended up inside each other apart.
        ///
        /// They carry no colliders — a queue of rigid bodies jams itself the first time two of them
        /// want the same metre of floor — so nothing physical keeps them apart, and at a walking pace
        /// two customers heading for neighbouring slots will happily occupy the same spot. This is the
        /// cheapest honest fix: a nudge along the line joining them, strong enough to unstick an
        /// overlap and far too weak to fight the walk itself.
        /// </summary>
        private void Separate()
        {
            float near = personalSpace * personalSpace;
            for (int i = 0; i < _live.Count; i++)
            {
                for (int j = i + 1; j < _live.Count; j++)
                {
                    Vector3 apart = _live[i].body.position - _live[j].body.position;
                    apart.y = 0f;
                    float d2 = apart.sqrMagnitude;
                    if (d2 >= near || d2 < 1e-6f) continue;

                    // The overlap is resolved in full, this frame. A damped nudge loses: every body
                    // is being driven back toward its own target by Walk on the very next frame, so a
                    // fraction-of-the-correction push just settles into a permanent intersection —
                    // measured at 1.01 apart against a 2.2 space before this was made absolute.
                    float d = Mathf.Sqrt(d2);
                    Vector3 push = apart / d * ((personalSpace - d) * 0.5f);
                    _live[i].body.position += push;
                    _live[j].body.position -= push;
                }
            }
        }

        private void Arrivals(float dt)
        {
            _arrivalIn -= dt;
            if (_arrivalIn > 0f) return;

            int slots = Slots;
            // Longer line, busier shop. Interpolated across the slot range so every slot bought is felt
            // twice: one more body in the queue, and less time between bodies.
            float t = slots <= 1 ? 0f : (slots - 1f) / (Game.Core.MarketFlow.MaxQueueSlots - 1f);
            _arrivalIn = Mathf.Lerp(arrivalSecondsAtOneSlot, arrivalSecondsAtFullQueue, t);

            int free = FirstFreeSlot(slots);
            if (free < 0) return;

            Customer c = _spare.Count > 0 ? _spare.Pop() : NewCustomer();
            c.body.gameObject.SetActive(true);
            // Outside the wall, so the first thing they do on screen is come through the door.
            c.body.position = _outside;
            c.slot = free;
            c.step = Step.Entering;
            c.target = _door;
            // Random, unlike the looks: a shop where everyone buys exactly one is a counter, not a
            // shop, and the varying order size is what makes the shelf worth stocking deep.
            c.wants = Random.Range(minPurchase, Mathf.Max(minPurchase, maxPurchase) + 1);
            _live.Add(c);
        }

        private int FirstFreeSlot(int slots)
        {
            for (int slot = 0; slot < slots; slot++)
            {
                bool taken = false;
                for (int i = 0; i < _live.Count; i++)
                    if (_live[i].slot == slot) { taken = true; break; }
                if (!taken) return slot;
            }
            return -1;
        }

        private void Step_(Customer c, int index, float dt)
        {
            switch (c.step)
            {
                case Step.Entering:
                    if (!Walk(c, _door, dt)) return;
                    c.step = Step.Walking;
                    c.target = SlotPosition(c.slot);
                    return;

                case Step.Walking:
                    if (!Walk(c, c.target, dt)) return;
                    c.step = Step.Waiting;
                    return;

                case Step.Waiting:
                    // Shuffle up as the line moves.
                    Vector3 spot = SlotPosition(c.slot);
                    if ((c.body.position - spot).sqrMagnitude > 0.02f) { Walk(c, spot, dt); return; }
                    if (c.slot != 0) { Shuffle(c); return; }
                    // At the head, and three things have to be true to be served: something on the
                    // counter, somebody behind it, and somewhere for the money to land. The middle one
                    // is what makes the cashier worth hiring — without a server the line just stands
                    // there, however well stocked the shelf is.
                    if (_counter == null || _counter.Stocked == 0 || !_counter.Served) return;
                    if (_cash != null && _cash.IsFull) return;
                    c.step = Step.Serving;
                    c.timer = serveSeconds;
                    // Turn the cashier to face this customer for the length of the transaction.
                    if (_counter.Cashier != null) _counter.Cashier.FacePoint(c.body.position);
                    return;

                case Step.Serving:
                    // Handing money over reads as a wave — the packs ship one, and it is the only
                    // gesture in them that looks like a transaction.
                    c.anim.Set(PersonAnimator.Wave);
                    c.timer -= dt;
                    if (c.timer > 0f) return;
                    int taken;
                    double paid = _counter.TakeUpTo(c.wants, out taken);
                    if (paid > 0d && _cash != null)
                    {
                        // One note per bar, so a big order visibly pays more than a small one.
                        for (int n = 0; n < taken; n++) _cash.Drop(paid / taken, c.body.position);
                        // The library throttles this one itself — it is the sound the game plays most.
                        _audio?.Play(Game.Data.SoundId.Sale);
                        if (_counter.Cashier != null) _counter.Cashier.PlayServe();
                    }
                    c.slot = -1;                       // frees the head so the next one can move up
                    c.step = Step.Leaving;
                    return;

                case Step.Leaving:
                    // Back to the door first, then out through it.
                    if (!Walk(c, _door, dt)) return;
                    c.step = Step.Departing;
                    return;

                case Step.Departing:
                    if (!Walk(c, _outside, dt)) return;
                    // Switched off beyond the wall, where nobody can see it happen.
                    c.body.gameObject.SetActive(false);
                    _live.RemoveAt(index);
                    _spare.Push(c);
                    return;
            }
        }

        /// <summary>Moves a waiting customer into the first free slot ahead of them.</summary>
        private void Shuffle(Customer c)
        {
            for (int ahead = 0; ahead < c.slot; ahead++)
            {
                bool taken = false;
                for (int i = 0; i < _live.Count; i++)
                    if (_live[i].slot == ahead) { taken = true; break; }
                if (!taken) { c.slot = ahead; return; }
            }
        }

        /// <summary>True once the body has arrived. Also decides whether they are walking or standing.</summary>
        private bool Walk(Customer c, Vector3 to, float dt)
        {
            Vector3 delta = to - c.body.position;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance <= 0.05f) { c.anim.Set(PersonAnimator.Idle); return true; }

            float step = walkSpeed * dt;
            c.body.position += delta / distance * Mathf.Min(step, distance);
            c.body.rotation = Quaternion.LookRotation(delta);
            c.anim.Set(PersonAnimator.Walk);
            return step >= distance;
        }

        private Customer NewCustomer()
        {
            // Colliders are stripped by the spawner: customers walk through each other rather than jam
            // the line, and an authored prefab that arrived with one would do exactly that.
            // Each new body takes the next look in the list, so the queue is a crowd rather than one
            // person copied six times.
            GameObject look = _prefabs != null ? _prefabs.CustomerAt(_spawned++) : null;
            Transform body = _prefabs != null
                ? _prefabs.SpawnPerson(look, transform, "Musteri", new Vector3(0.9f, 0.95f, 0.9f), _material)
                : MarketPrefabs.Spawn(look, transform, "Musteri", PrimitiveType.Capsule,
                                      new Vector3(0.9f, 0.95f, 0.9f), _material);
            return new Customer { body = body, anim = new PersonAnimator(body) };
        }
    }
}
