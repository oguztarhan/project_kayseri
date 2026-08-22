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
    ///
    /// Nothing here pushes one body out of another. Bodies that avoid each other by shoving are bodies
    /// that shove, and a queue of them visibly wrestles at the counter. They are kept apart by the
    /// route instead — see <see cref="Step"/> — and where the routes do run close, under the porch
    /// outside the wall, overlapping costs nothing because the camera cannot see it.
    /// </summary>
    public sealed class CustomerQueue : MonoBehaviour
    {
        [Tooltip("İki sıradaki müşteri arasındaki mesafe. Altı yer, tezgâhın önündeki sıra yolunu " +
                 "tam dolduracak kadar: daha genişi kuyruğun sonunu kapının önüne taşır ve girenler " +
                 "bekleyenlerin arasından geçmek zorunda kalır.")]
        [SerializeField, Min(0.5f)] private float spacing = 2.4f;

        [Tooltip("Bir müşterinin alabileceği en az ve en çok külçe. Herkesin bir tane alması " +
                 "kuyruğu bir sayaca çevirir; değişken miktar onu alışverişe çevirir.")]
        [SerializeField, Min(1)] private int minPurchase = 1;
        [SerializeField, Min(1)] private int maxPurchase = 4;

        [Tooltip("Müşterinin yürüme hızı.")]
        [SerializeField, Min(0.5f)] private float walkSpeed = 6.5f;

        [Tooltip("Satın alınan sıra yerinden bağımsız olarak avluda her zaman duran en az müşteri " +
                 "sayısı. Bir dükkânın kalabalık görünmesi ile o kalabalığın ne kadar sattığı ayrı " +
                 "iki şey; bu, birincisini ikincisini beklemeden verir.")]
        [SerializeField, Min(1)] private int minLineSlots = 4;

        [Tooltip("Kapıdaki iki şeridin ortadan uzaklığı. Girenler bir yandan, çıkanlar öbür yandan " +
                 "geçer. Kapı boşluğu altı birim, kasaları çıkınca beş buçuk: 1.8 iki şeridi de " +
                 "çerçeveye sürtmeden içeride tutar.")]
        [SerializeField, Min(0.5f)] private float doorLane = 1.8f;

        [Tooltip("Geliş şeridinin kuyruk çizgisine uzaklığı, kapı tarafında. Yeni müşteri sıranın " +
                 "arkasından geçip yerine oradan girer, bekleyenlerin içinden değil.")]
        [SerializeField, Min(0.5f)] private float approachLane = 2.6f;

        [Tooltip("Gidiş şeridinin kuyruk çizgisine uzaklığı. Geliş şeridinden uzakta, duvara yakın: " +
                 "iki yön hiçbir yerde aynı zemini paylaşmasın diye.")]
        [SerializeField, Min(0.5f)] private float returnLane = 5.6f;

        [Tooltip("Alışverişi biten müşterinin dönüş şeridine inmeden önce sıranın önünden ne kadar " +
                 "açıldığı. Sıfırda geliş şeridini tam sıranın başında keser.")]
        [SerializeField, Min(0f)] private float stepAside = 2.4f;

        [Tooltip("Tezgâhın başındaki müşterinin külçeyi alması süresi.")]
        [SerializeField, Min(0.05f)] private float serveSeconds = 0.32f;

        [Header("VIP")]
        [Tooltip("Kaç müşteride bir VIP gelir. VIP sıradan bir müşteri gibi ödeme yapar ama çok " +
                 "daha fazla külçe alır — kâr farkı satılan külçeden gelir, çarpandan değil.")]
        [SerializeField, Min(2)] private int vipEvery = 9;

        [Tooltip("VIP'nin normal bir müşterinin kaç katı külçe istediği.")]
        [SerializeField, Min(2)] private int vipAppetite = 4;

        [Header("Ruh hâli rozetleri")]
        [Tooltip("Bu kadar saniye bekleyen müşteri memnun olmaktan çıkar.")]
        [SerializeField, Min(1f)] private float moodPatience = 7f;

        [Tooltip("Bu kadar saniye bekleyen müşterinin sabrı taşar. Tezgâh boşsa asıl gördüğün bu olur.")]
        [SerializeField, Min(2f)] private float moodAnger = 16f;

        [Tooltip("Rozetin müşterinin ayağından yüksekliği. Gövdeler yaklaşık 3.1 birim.")]
        [SerializeField] private float badgeHeight = 4.0f;

        [Tooltip("Rozetin çapı, dünya birimi.")]
        [SerializeField, Min(0.2f)] private float badgeSize = 1.0f;

        [Tooltip("Sıra doluyken bile yeni müşteri arası en kısa süre, tam dolu sırada.")]
        [SerializeField, Min(0.2f)] private float arrivalSecondsAtFullQueue = 0.4f;

        [Tooltip("Tek sıralı avluda müşteri arası süre. Sıra yeri aldıkça bu süre kısalır.")]
        [SerializeField, Min(0.3f)] private float arrivalSecondsAtOneSlot = 1.1f;

        /// <summary>
        /// Arriving and leaving are routes, not straight lines, and they are two DIFFERENT routes.
        ///
        /// One shared path is what jams a queue. Two bodies walking the same corridor in opposite
        /// directions meet head on, and with no colliders to sort it out they grind through each other
        /// in full view of the camera. So the traffic is one way: arrivals come in the far half of the
        /// doorway, up to a lane that runs BEHIND the waiting line, and step forward into their slot
        /// from there; departures peel off the front of the line, out past the head onto a second lane
        /// closer to the wall, and along that to the near half of the doorway. Stepping past the head
        /// is what makes the two disjoint — the arrivals' lane stops at the head, so the one crossing
        /// that would otherwise exist happens where no arrival ever walks. They never share floor, the
        /// only place the lanes come near each other is under the porch — outside the wall, where the
        /// camera cannot see and bodies may overlap as much as they like.
        /// </summary>
        private enum Step { Arriving, Waiting, Serving, Departing }

        /// <summary>
        /// What the icon over a customer's head is saying. In the order it degrades — a customer walks
        /// in pleased and gets worse the longer nobody serves them.
        ///
        /// This is a READOUT, not a mechanic. Nobody storms out and nothing is lost by a line full of
        /// red badges; what it does is answer, from across the room, the one question the yard could
        /// never answer before — is the counter keeping up? A stalled queue and a busy one looked
        /// identical, because in both cases the same bodies are standing in the same places. The
        /// badges are the difference, and they are what sends the player back to the stock pad.
        /// </summary>
        private enum Mood { None, Happy, Wait, Cross, Vip }

        private sealed class Customer
        {
            public Transform body;
            public PersonAnimator anim;
            public int slot;          // -1 while leaving
            public Step step;
            public float timer;
            public int wants;         // how many bars this one came in for
            public bool vip;          // buys by the armful; see Take
            public float waited;      // seconds stood in the line, which is what the badge reads
            public Transform badge;   // the icon over their head, pooled with the body
            public MeshRenderer badgeArt;
            public Mood mood;
            // The route being walked. Four is what the longer of the two needs, and the arrival uses
            // three plus the slot itself, which is read live because the line shuffles forward while
            // somebody is still walking in.
            public readonly Vector3[] path = new Vector3[4];
            public int legs, leg;
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
        private int _taken;          // how many have been dealt out, for spacing the VIPs
        private Quaternion _face;    // which way a badge has to be turned to be read

        /// <summary>The art a customer is made of. Set before <see cref="Configure"/> if it is being set at all.</summary>
        public void SetPrefabs(MarketPrefabs prefabs) => _prefabs = prefabs;

        private Vector3 _head;       // where the front of the line stands
        private Vector3 _along;      // unit vector from the head back down the line
        private Vector3 _side;       // unit vector from the line toward the door wall
        private Vector3 _inDoor, _inOutside;    // the arrivals' half of the doorway, and the spawn spot
        private Vector3 _outDoor, _outOutside;  // the departures' half, and where they switch off
        private float _inAlong, _outAlong;      // those two lanes, measured down the line from the head
        private float _arrivalIn;

        /// <summary>
        /// Set every time the yard is switched on, cleared by <see cref="Prime"/>. The queue is enabled
        /// before <see cref="Configure"/> has run on the first build, so the fill cannot happen in
        /// <c>OnEnable</c> itself — it waits for the first tick that has a market to read.
        /// </summary>
        private bool _needsPrime = true;

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

            // Which way the door is off the line, worked out rather than passed in: whatever is left of
            // the head-to-door vector once the along-the-line part is taken out of it. That keeps the
            // two lanes on the door's side of the queue however the yard is turned.
            Vector3 toDoor = door - _head;
            toDoor.y = 0f;
            Vector3 perp = toDoor - _along * Vector3.Dot(toDoor, _along);
            _side = perp.sqrMagnitude > 1e-4f ? perp.normalized : Vector3.Cross(Vector3.up, _along);

            // The doorway split in two. Arrivals take the half further down the line, which is the half
            // they would walk toward anyway; departures take the near half and never cross over.
            _inDoor = door + _along * doorLane;
            _inOutside = outside + _along * doorLane;
            _outDoor = door - _along * doorLane;
            _outOutside = outside - _along * doorLane;
            _inAlong = Vector3.Dot(_inDoor - _head, _along);
            _outAlong = Vector3.Dot(_outDoor - _head, _along);

            _material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var colour = new Color(0.44f, 0.52f, 0.63f);
            _material.color = colour;
            if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", colour);
        }

        /// <summary>
        /// How many people stand in the line. The queue-slot upgrade raises it, but it never drops
        /// below <see cref="minLineSlots"/>.
        ///
        /// The floor is the difference between a shop and a waiting room. A yard on its first slot had
        /// exactly one customer in it, and one customer is not a queue — the body walked in, was served,
        /// walked out, and the room stood empty for the whole of the next walk. What the upgrade buys is
        /// still real: it is <see cref="Game.Core.MarketFlow.SellCapacityPerSecond"/> that turns queue
        /// length into money, and that reads the save row, not this. This is only how many bodies the
        /// camera can see, and there is no reason for the camera to be shown an empty shop.
        /// </summary>
        private int Slots
        {
            get
            {
                int slots = _market != null ? _market.Row(_yardKey).queueSlots : 1;
                if (slots < minLineSlots) slots = minLineSlots;
                return Mathf.Clamp(slots, 1, Game.Core.MarketFlow.MaxQueueSlots);
            }
        }

        private Vector3 SlotPosition(int slot) => _head + _along * (spacing * slot);

        /// <summary>A point on one of the two lanes, measured down the line and out to the side.</summary>
        private Vector3 LanePoint(float along, float side) => _head + _along * along + _side * side;

        private void OnEnable()
        {
            _needsPrime = true;
            // Which way a badge has to face to be read, taken ONCE per opening rather than per frame.
            // The market camera is fixed — it follows the player and never turns — so a badge that
            // billboarded itself every frame would be recomputing a constant for everyone in the room.
            //
            // Here and not in Configure, because the hall builds its yards before it builds the camera:
            // asked any earlier this is null every time, and the fallback would silently become the
            // real answer the moment anyone edited the camera's angle in the Inspector.
            Camera view = Camera.main;
            _face = view != null ? view.transform.rotation : Quaternion.Euler(52f, 45f, 0f);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (_needsPrime && _market != null) Prime();
            Arrivals(dt);
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                Customer c = _live[i];
                Step_(c, i, dt);
                PlaceBadge(c);
            }
        }

        /// <summary>
        /// Fills the line the instant the yard opens, with the bodies standing in their places rather
        /// than walking in from the door.
        ///
        /// Walking them in would be the honest thing and it is the wrong thing. The roof comes off as
        /// the player arrives, and the first thing they would see is an empty shop slowly filling —
        /// which is the whole of the boredom, just moved to the top of every visit. A queue that is
        /// already standing there says the shop was busy before you walked in, which is what a market
        /// is meant to say. Only free places are filled, so re-entering a yard tops the line up rather
        /// than doubling it.
        /// </summary>
        private void Prime()
        {
            _needsPrime = false;
            int slots = Slots;
            for (int slot = 0; slot < slots; slot++)
            {
                if (Taken(slot)) continue;
                Customer c = Take();
                c.slot = slot;
                c.step = Step.Waiting;
                c.body.position = SlotPosition(slot);
                // Facing the counter, like everyone who walked here the long way round.
                c.body.rotation = Quaternion.LookRotation(-_along);
                c.anim.Set(PersonAnimator.Idle);
                _live.Add(c);
            }
        }

        /// <summary>A pooled body, switched on and ready to be placed. Both spawn paths go through this.</summary>
        private Customer Take()
        {
            Customer c = _spare.Count > 0 ? _spare.Pop() : NewCustomer();
            c.body.gameObject.SetActive(true);
            // Random, unlike the looks: a shop where everyone buys exactly one is a counter, not a
            // shop, and the varying order size is what makes the shelf worth stocking deep.
            c.wants = Random.Range(minPurchase, Mathf.Max(minPurchase, maxPurchase) + 1);

            // Every ninth, counted rather than rolled — a random VIP arrives in pairs often enough to
            // read as a bug, and never at all for long enough to make the star badge meaningless.
            c.vip = (++_taken % Mathf.Max(2, vipEvery)) == 0;
            // A VIP buys MORE, and does not pay more per bar. That distinction is the whole design of
            // them: the extra money comes out of SellByHand like every other sale, so it goes through
            // the income ceiling and the meters that feed the next session's offline grant. A per-bar
            // multiplier applied on the way to the floor would have minted cash the ledger never saw.
            if (c.vip) c.wants *= vipAppetite;

            c.waited = 0f;
            SetMood(c, c.vip ? Mood.Vip : Mood.None);
            return c;
        }

        private bool Taken(int slot)
        {
            for (int i = 0; i < _live.Count; i++)
                if (_live[i].slot == slot) return true;
            return false;
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

            Customer c = Take();
            // Outside the wall, so the first thing they do on screen is come through the door.
            c.body.position = _inOutside;
            c.slot = free;
            c.step = Step.Arriving;
            // In through their half of the door, up to the lane behind the line, along it to their own
            // slot's place in the row, and only then forward into the row itself. The third leg is what
            // keeps them off the people already standing there — the lane is behind the line, not in it.
            c.path[0] = _inDoor;
            c.path[1] = LanePoint(_inAlong, approachLane);
            c.path[2] = LanePoint(spacing * free, approachLane);
            c.legs = 3;
            c.leg = 0;
            _live.Add(c);

            // Somebody came in. The chime is throttled hard in the library because a busy yard opens
            // that door every four tenths of a second, and a bell on every one of them is a machine
            // rather than a shop. The VIP's is not throttled the same way: it is the one arrival the
            // player is meant to turn round for, and the badge alone only works if he is looking.
            _audio?.Play(c.vip ? Game.Data.SoundId.MarketVip : Game.Data.SoundId.MarketDoor);
        }

        private int FirstFreeSlot(int slots)
        {
            for (int slot = 0; slot < slots; slot++)
                if (!Taken(slot)) return slot;
            return -1;
        }

        private void Step_(Customer c, int index, float dt)
        {
            switch (c.step)
            {
                case Step.Arriving:
                    // The last leg is the slot itself rather than a stored point: the line shuffles
                    // forward while this one is still walking in, and a stale target would walk them to
                    // where their place used to be.
                    if (!Walk(c, c.leg < c.legs ? c.path[c.leg] : SlotPosition(c.slot), dt)) return;
                    c.leg++;
                    if (c.leg > c.legs) c.step = Step.Waiting;
                    return;

                case Step.Waiting:
                    c.waited += dt;
                    // A VIP keeps their star however long they wait: the badge is telling the player
                    // this is the order worth stocking for, and swapping it for a scowl halfway
                    // through would hide the one customer they most need to see.
                    if (!c.vip)
                        SetMood(c, c.waited < moodPatience ? Mood.Happy
                                 : c.waited < moodAnger ? Mood.Wait : Mood.Cross);
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
                    // Clamped to what will actually fit on the floor. One note is laid down per bar,
                    // and a note that arrives on a full floor is thrown away by CashFloor.Drop — while
                    // the sale that made it has already been banked by the counter. With four-bar
                    // orders that lost a note now and then; a VIP asking for sixteen would lose most
                    // of one. See CashFloor.Free.
                    int room = _cash != null ? _cash.Free : c.wants;
                    double paid = _counter.TakeUpTo(Mathf.Min(c.wants, room), out taken);
                    if (paid > 0d && _cash != null)
                    {
                        // One note per bar, so a big order visibly pays more than a small one.
                        for (int n = 0; n < taken; n++) _cash.Drop(paid / taken, c.body.position);
                        // The library throttles this one itself — it is the sound the game plays most.
                        _audio?.Play(Game.Data.SoundId.Sale);
                        if (_counter.Cashier != null) _counter.Cashier.PlayServe();
                    }
                    c.slot = -1;                       // frees the head so the next one can move up
                    c.step = Step.Departing;
                    SetMood(c, Mood.None);             // served and pleased; nothing left to say
                    // Out around the front of the line rather than back down it: past the head, onto
                    // the far lane along the wall, along that to their half of the door, and out.
                    // Stepping aside first is what keeps this route clear of the arrivals' lane, which
                    // stops at the head and never reaches back this far.
                    c.path[0] = LanePoint(-stepAside, returnLane);
                    c.path[1] = LanePoint(_outAlong, returnLane);
                    c.path[2] = _outDoor;
                    c.path[3] = _outOutside;
                    c.legs = 4;
                    c.leg = 0;
                    return;

                case Step.Departing:
                    if (!Walk(c, c.path[c.leg], dt)) return;
                    c.leg++;
                    if (c.leg < c.legs) return;
                    // Switched off beyond the wall, where nobody can see it happen.
                    c.body.gameObject.SetActive(false);
                    SetMood(c, Mood.None);
                    _live.RemoveAt(index);
                    _spare.Push(c);
                    return;
            }
        }

        /// <summary>
        /// Puts the badge over its owner's head and turns it to be read.
        ///
        /// The badge is parented to the QUEUE, not the body, and is placed by hand every frame. Hung
        /// off the body it would inherit two things it must not: the person scale, which would make
        /// the icon 1.75 times too big, and the body's own turn, which spins as they walk the route
        /// and would show the player the back of the badge for most of the walk in.
        /// </summary>
        private void PlaceBadge(Customer c)
        {
            if (c.badge == null || c.mood == Mood.None) return;
            c.badge.SetPositionAndRotation(c.body.position + Vector3.up * badgeHeight, _face);
        }

        /// <summary>
        /// Shows one of the four icons, or none. Cheap to call every frame — it does nothing at all
        /// unless the mood actually changed, which for a waiting customer is three times in sixteen
        /// seconds.
        /// </summary>
        private void SetMood(Customer c, Mood mood)
        {
            if (c.mood == mood) return;
            c.mood = mood;
            if (c.badge == null) c.badge = NewBadge(c);
            if (c.badge == null) return;

            bool show = mood != Mood.None;
            c.badge.gameObject.SetActive(show);
            if (!show) return;
            // The VIP's is bigger. It is the one badge that is an invitation rather than a complaint,
            // and it has to be picked out of a line of four other people from across the yard.
            c.badge.localScale = Vector3.one * badgeSize * (mood == Mood.Vip ? 1.4f : 1f);
            c.badgeArt.sharedMaterial = MarketSurfaces.Badge(IconOf(mood));
        }

        private static string IconOf(Mood mood)
        {
            switch (mood)
            {
                case Mood.Happy: return "Happy";
                case Mood.Wait: return "Wait";
                case Mood.Cross: return "Cross";
                default: return "Vip";
            }
        }

        private Transform NewBadge(Customer c)
        {
            var go = new GameObject("Rozet");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = MarketBoxMesh.Quad();
            var art = go.AddComponent<MeshRenderer>();
            art.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            art.receiveShadows = false;
            c.badgeArt = art;
            return go.transform;
        }

        /// <summary>Moves a waiting customer into the first free slot ahead of them.</summary>
        private void Shuffle(Customer c)
        {
            for (int ahead = 0; ahead < c.slot; ahead++)
                if (!Taken(ahead)) { c.slot = ahead; return; }
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
