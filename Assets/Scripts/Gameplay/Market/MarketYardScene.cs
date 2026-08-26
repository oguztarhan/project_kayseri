using Game.Core;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// One island's yard, standing in the row: its floor, its walls, its pads, its counter and its
    /// queue. Everything a yard is made of hangs off this, so the hall is a list of these rather than
    /// one enormous builder that knows about eight of everything.
    ///
    /// THE POINT OF THE SPLIT is <see cref="SetLive"/>. Eight yards of customers walking waypoints and
    /// six pads apiece polling the wallet is eight times the work for seven rooms nobody is standing
    /// in — the same trap the island archipelago avoided by simulating one island at a time. So the
    /// yard the player is in runs, and the rest sit under their roofs while their ledger row keeps
    /// earning. That row does not care whether anything is enabled: it was always a number, and it
    /// goes on being one. The roof is what keeps the two honest — you never see a stopped yard, so
    /// there is nothing to contradict the money it is still making.
    /// </summary>
    public sealed class MarketYardScene : MonoBehaviour
    {
        private MarketService _market;
        private StockPad _pad;
        private SellCounter _counter;
        private CustomerQueue _queue;
        private CashFloor _cash;
        private UpgradePad[] _pads;
        private Material _ore;
        private MarketPrefabs _prefabs;
        private readonly YardWorker[] _staff = new YardWorker[3];   // indexed by YardWorker.Job
        private Vector3 _padSpot, _counterSpot, _cashSpot;
        private Transform _roof;
        private Transform _dressing;
        private GameObject _fittings;
        private GameObject _ceiling;
        /// <summary>False out of the gate: a yard is shut until the hall walks the player into it.</summary>
        private bool _live;

        /// <summary>Which island's market this is.</summary>
        public string IslandKey { get; private set; }

        /// <summary>
        /// Hands the yard something built for it from above — the signage, which is <c>Game.UI</c>'s
        /// because it draws text and this assembly cannot see TextMeshPro.
        ///
        /// A bare GameObject rather than a typed component, and that is the whole point: the yard has
        /// to be able to switch its fittings off with the rest of it when it is parked, and it can do
        /// that without knowing what they are. Anything else means Gameplay referencing UI, which is
        /// the circular asmdef this project has already refused once.
        /// </summary>
        public void AddFitting(GameObject fitting)
        {
            _fittings = fitting;
            if (fitting != null) fitting.SetActive(_live);
        }

        /// <summary>Where the player stands if they arrive in this yard.</summary>
        public Vector3 PlayerStart { get; private set; }

        /// <summary>The pads on this yard's floor, for the HUD to name whichever is underfoot.</summary>
        public UpgradePad[] Pads => _pads;

        /// <summary>
        /// Whether a world point is inside this yard. Only the x axis is asked: the yards are a row,
        /// and the walls stop anyone leaving on the other two.
        /// </summary>
        public bool Contains(Vector3 worldPoint)
            => Mathf.Abs(worldPoint.x - transform.position.x) <= MarketYardBuild.Width * 0.5f;

        /// <summary>
        /// Puts the yard up. <paramref name="westWall"/> is false for every yard but the first, because
        /// its neighbour's east wall is already standing there.
        /// </summary>
        public void Build(MarketService market, string islandKey, Color tint, Material ore,
                          bool westWall, bool eastDoorway, Transform player, CarryStack carry,
                          MarketPrefabs prefabs)
        {
            _market = market;
            _ore = ore;
            _prefabs = prefabs;
            IslandKey = islandKey;

            MarketTheme.Palette theme = MarketTheme.For(islandKey);
            Vector3[] spots = MarketYardBuild.Build(transform, theme, tint, westWall, eastDoorway);
            _roof = transform.Find(MarketYardBuild.RoofName);
            // Last, so the props are laid over a yard that is already standing — the dressing measures
            // nothing and fits into the corners the layout above leaves empty.
            _dressing = MarketYardDressing.Dress(transform, theme);
            // The ceiling goes up with them, and for the same reason: it measures nothing off the yard
            // and only needs the walls to already be at their height.
            _ceiling = MarketYardLighting.Build(transform, tint, theme).gameObject;
            PlayerStart = transform.TransformPoint(spots[(int)MarketYardBuild.Anchor.PlayerStart]);
            _padSpot = spots[(int)MarketYardBuild.Anchor.StockPad];
            _counterSpot = spots[(int)MarketYardBuild.Anchor.Counter];
            _cashSpot = spots[(int)MarketYardBuild.Anchor.CashFloor];

            BuildLoop(spots, ore, player);
            BuildPads(carry);
            BuildDock(carry);
            SpawnExistingStaff();

            // Built shut, and the hall opens exactly one of them straight afterwards. Built running was
            // the old order and it only worked because the hall switched seven of them off again in the
            // same frame — with a roof in the picture that is a frame of eight open rooms, and the yard
            // the player never walks into would have had its counter live under a closed roof.
            Apply(false);
        }

        /// <summary>
        /// Runs or parks the yard: the one the player is standing in, and the seven he is not.
        ///
        /// Parked, nothing in it ticks and the roof goes back on. Both halves matter and they are the
        /// same decision — a stopped yard you can see into is a room full of people standing still,
        /// which says the market is broken. Under its roof it says the shop is shut, while its ledger row
        /// keeps selling and keeps paying. That row never cared whether anything here was enabled.
        /// </summary>
        public void SetLive(bool live)
        {
            if (_live == live) return;
            _live = live;
            Apply(live);
        }

        /// <summary>Puts the state on the yard, without the guard — the one path Build can also use.</summary>
        private void Apply(bool live)
        {
            // Off with the roof for the yard on screen, back on for the yard behind you. The whole slab
            // goes rather than just its renderer: switched off it costs no draw call, casts no shadow
            // into its own room, and cannot occlude anything.
            if (_roof != null) _roof.gameObject.SetActive(!live);
            // Off with the yard. Dressing is the one thing in here that is pure geometry — no component
            // to disable — so a parked yard would go on paying for eight props nobody can see through
            // the roof that just went back on.
            if (_dressing != null) _dressing.gameObject.SetActive(live);
            // Same reasoning as the dressing, and one more besides: the price board on the east wall
            // reads the ledger every half second, and seven shut yards quietly polling for a number
            // nobody can see through their own roof is the exact shape of cost this split exists to
            // avoid. Switched off, its Update does not run at all.
            if (_fittings != null) _fittings.SetActive(live);
            // And the ceiling, which is the one that really has to go: three point lights apiece across
            // eight yards is twenty-four of them in a renderer set to four per object, and the seven the
            // player is not in are lighting the inside of their own closed roofs.
            if (_ceiling != null) _ceiling.SetActive(live);
            if (_pad != null) _pad.enabled = live;
            if (_counter != null) _counter.enabled = live;
            if (_queue != null) _queue.enabled = live;
            if (_cash != null) _cash.enabled = live;
            for (int i = 0; i < _staff.Length; i++)
                if (_staff[i] != null) _staff[i].enabled = live;
            if (_pads == null) return;
            for (int i = 0; i < _pads.Length; i++)
                if (_pads[i] != null) _pads[i].enabled = live;
        }

        /// <summary>
        /// Chains the four stations of the loop: pad → counter → queue → floor. Each one only knows the
        /// next, so a yard is a pipeline rather than a controller holding a list of everything in it.
        /// </summary>
        private void BuildLoop(Vector3[] spots, Material ore, Transform player)
        {
            Vector3 padAt = spots[(int)MarketYardBuild.Anchor.StockPad];
            Vector3 counterAt = spots[(int)MarketYardBuild.Anchor.Counter];
            Vector3 cashAt = spots[(int)MarketYardBuild.Anchor.CashFloor];

            // Narrowed from 18 so it stops short of the two pads now standing against the east wall —
            // a player buying an upgrade should not also be loading bars onto their back.
            Transform padZone = Zone("StokPediAlani", padAt, new Vector3(0f, 1.6f, 0f),
                                     new Vector3(15f, 3.2f, 16f));
            _pad = padZone.gameObject.AddComponent<StockPad>();
            _pad.Configure(_market, IslandKey, transform.Find("StokPedi"), ore);

            // The counter's trigger sits on the PLAYER's side of it — the customers come at it from the
            // other, and a volume covering both would have the queue unloading the player's back.
            Transform counterZone = Zone("TezgahAlani", counterAt, new Vector3(0f, 1.5f, 3.2f),
                                         new Vector3(13f, 3f, 4.4f));
            _counter = counterZone.gameObject.AddComponent<SellCounter>();
            _counter.Configure(_market, IslandKey, ore, _prefabs);

            var floor = new GameObject("ParaZemin").transform;
            floor.SetParent(transform, false);
            floor.localPosition = cashAt;
            _cash = floor.gameObject.AddComponent<CashFloor>();
            _cash.Configure(_market, IslandKey, player, _prefabs);

            // The display rack, standing behind the counter on the player's side. Far enough back that
            // the whole unloading strip in front of the plank is still his: the counter's trigger runs
            // from z = -6.6 to -2.2, and this sits at the far end of it with better than two body
            // widths of clear floor between them.
            CounterShelf.Build(transform, counterAt + new Vector3(0f, 0f, 5f), _counter, ore,
                               MarketTheme.For(IslandKey), _prefabs);

            var lane = new GameObject("Sira").transform;
            lane.SetParent(transform, false);
            _queue = lane.gameObject.AddComponent<CustomerQueue>();
            _queue.SetPrefabs(_prefabs);
            // World space, not local: the queue moves bodies by world position, and this yard is
            // somewhere along a row rather than at the origin.
            Vector3 doorAt = spots[(int)MarketYardBuild.Anchor.QueueDoor];
            _queue.Configure(_market, IslandKey, _counter, _cash,
                             transform.TransformPoint(counterAt + new Vector3(0f, CustomerHeight, -4.4f)),
                             Vector3.right,
                             transform.TransformPoint(doorAt + new Vector3(0f, CustomerHeight, 1.2f)),
                             transform.TransformPoint(doorAt + new Vector3(0f, CustomerHeight, -5.5f)));
        }

        private const float CustomerHeight = 0.95f;

        /// <summary>
        /// Where the dock stands: the north-west quarter, on the one patch of floor nothing else uses.
        ///
        /// Deliberately NOT beside the stock pad, which is the obvious place and the wrong one — the
        /// player crosses that slab constantly with a full back, and a dock there would load the ship
        /// every time he walked past on his way to the counter. Going to sea has to be somewhere he
        /// goes.
        ///
        /// MEASURED, not guessed. This was z 11 first, from a clearance note that read "the ramp is at
        /// z 15" — which is the ramp's ANCHOR. Its geometry runs z 12 to 18, so a 7-deep jetty at z 11
        /// reached z 14.5 and the moored hull stood inside the ramp. Take extents from the renderers,
        /// never from the anchor a piece was placed by.
        ///
        ///   Rampa        x[-14.0, 14.0]  z[12.0, 18.0]   <- the one that bit
        ///   Rampa_Serit                  z[11.8, 12.0]
        ///   StokPedi     x[  1.5, 18.5]  z[-5.5,  9.5]
        ///   pad rank     x -17.5, reaching about x -15.5
        ///   CounterShelf                 z ~ -3
        ///
        /// At (-8, 4.5) the jetty occupies x[-11.5, -4.5] z[1.0, 8.0] and the hull x[-3.9, -1.7],
        /// which clears every one of them.
        /// </summary>
        private static readonly Vector3 DockSpot = new Vector3(-8f, 0f, 4.5f);

        /// <summary>
        /// The mooring post, on the jetty's water edge toward the moored hull. Clear of the pad's
        /// trigger (3.6 wide, so it reaches x 1.8) and inside the deck (7 wide, so it ends at x 3.5).
        /// </summary>
        private static readonly Vector3 BollardSpot = new Vector3(2.7f, 0f, 2.5f);

        /// <summary>
        /// The dock, and the second thing a bar can be. Built like every other station in here: a slab
        /// you can see, a post that tells you something is waiting, and a trigger you stand in.
        /// </summary>
        private void BuildDock(CarryStack carry)
        {
            var voyages = ServiceLocator.Get<VoyageService>();
            if (voyages == null) return;      // the feature is off; the yard is simply the yard

            var go = new GameObject("Rihtim");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = DockSpot;

            // Authored art where it has been wired, the greybox box where it has not — the same
            // bargain MarketPrefabs strikes everywhere else in this yard, and what keeps the dock
            // runnable while the models are still being made.
            Transform post, hull;

            if (_prefabs != null && _prefabs.DockJetty != null)
                Piece(go.transform, _prefabs.DockJetty, "RihtimZemini", Vector3.zero);
            else
                Grey(go.transform, "RihtimZemini", new Vector3(0f, 0.08f, 0f), new Vector3(7f, 0.16f, 7f));

            // Off the centre of the pad, at the water edge where a mooring post actually belongs. It
            // was at (0,0,0), which is also the middle of the trigger the player stands in — so every
            // time he used the dock he was standing inside the post, and the one piece that signals a
            // ship is home was the one piece hidden behind his body.
            post = _prefabs != null && _prefabs.DockBollard != null
                ? Piece(go.transform, _prefabs.DockBollard, "RihtimIsareti", BollardSpot)
                : Grey(go.transform, "RihtimIsareti", BollardSpot + new Vector3(0f, 1.5f, 0f),
                       new Vector3(0.6f, 3f, 0.6f));

            // Sized like an upgrade pad's: the player's capsule adds 1.14 of radius, so this reaches
            // about the edge of the slab and no further. See the note on Pad().
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, 1.5f, 0f);
            box.size = new Vector3(3.6f, 3f, 3.6f);

            // The hull, alongside. Docs/VOYAGES.md §9 put this in the island's harbour, where
            // CoalOperation already berths a contract ship — but the player is standing HERE when he
            // loads her, and the island is a different scene entirely. A ship that is only absent
            // somewhere the player is not is a signal nobody receives. It also keeps the change out of
            // CoalOperation, which hard-disables its whole island if a landmark name is missed.
            //
            // 5.2 out: half the jetty (3.5) plus half her beam (1.1) plus a channel of open floor, so
            // she lies alongside rather than through the deck. The greybox box kept its old 4.6, which
            // is where the two overlapped and why the authored one had to move.
            hull = _prefabs != null && _prefabs.DockLaunch != null
                ? Piece(go.transform, _prefabs.DockLaunch, "Tekne", new Vector3(5.2f, -0.18f, 0f))
                : Grey(go.transform, "Tekne", new Vector3(4.6f, 0.7f, 0f), new Vector3(2.2f, 1.4f, 6.4f));

            var dock = go.AddComponent<DockPad>();
            dock.Configure(voyages, IslandKey, carry, post, hull);
        }

        /// <summary>
        /// Drops an authored model in as a child, stripped of colliders. The dock's own trigger is the
        /// whole interaction, and a mesh collider on a moored boat is something to walk into.
        /// </summary>
        private static Transform Piece(Transform parent, GameObject prefab, string name, Vector3 at)
        {
            GameObject go = Instantiate(prefab, parent, false);
            go.name = name;
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.identity;
            var hits = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < hits.Length; i++) Destroy(hits[i]);
            return go.transform;
        }

        /// <summary>The greybox stand-in for a dock piece nobody has wired art into yet.</summary>
        private static Transform Grey(Transform parent, string name, Vector3 at, Vector3 size)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localScale = size;
            return go.transform;
        }

        /// <summary>
        /// The floor pads: one rank of six down the west wall, three either side of the doorway in it,
        /// and nothing else in the yard stands on that side at all.
        /// </summary>
        private void BuildPads(CarryStack carry)
        {
            // ONE SIDE, and the side matters. Four of these used to stand here and two against the east
            // wall, which is the wall the doorway to the next market is in — so the walk from one yard to
            // the next went straight across a pad that charges you for standing on it. Everything the
            // player does on purpose runs east: the stock pad is east, the counter is mid-floor, the door
            // is east. The west wall is the one line in the room he never has to cross, so that is where
            // the money goes. Buying is somewhere you GO now, not something that happens to you.
            //
            // Where each one stands, and why the rank is in two halves, is <see cref="PadSpot"/>. The
            // order here is the order they stand in, north to south, and it is also what the wiring below
            // indexes — the three hires have to be 2, 3, 4 and the back has to be last.
            _pads = new UpgradePad[6];
            _pads[0] = Pad(YardUpgrade.DepositSlot, PadSpot(0));
            _pads[1] = Pad(YardUpgrade.QueueSlot, PadSpot(1));
            _pads[2] = Pad(YardUpgrade.HireCarry, PadSpot(2));
            _pads[3] = Pad(YardUpgrade.HireServe, PadSpot(3));
            _pads[4] = Pad(YardUpgrade.HireCollect, PadSpot(4));
            _pads[5] = Pad(YardUpgrade.CarryCapacity, PadSpot(5));

            // The back is the one upgrade that has to land mid-purchase: the player is standing on the
            // pad with a stack on their shoulders, and a taller stack they only got on the next scene
            // load would read as the pad having done nothing.
            if (carry != null)
                _pads[5].Bought += kind => carry.SetUpgradeLevel(
                    _market != null ? _market.Level(IslandKey, YardUpgrade.CarryCapacity) : 0);

            // A hire has to show up. Paying for a carrier and seeing nothing change is the fastest way
            // to make the most expensive thing in the yard feel like it did nothing.
            for (int i = 2; i <= 4; i++) _pads[i].Bought += OnHired;
        }

        private void OnHired(YardUpgrade kind)
        {
            YardWorker.Job job = kind == YardUpgrade.HireCarry ? YardWorker.Job.Carry
                               : kind == YardUpgrade.HireServe ? YardWorker.Job.Serve
                               : YardWorker.Job.Collect;
            int level = _market.Level(IslandKey, kind);
            int slot = (int)job;

            // Levels two and up are a raise, not a second body.
            if (_staff[slot] != null) { _staff[slot].SetLevel(level); return; }
            _staff[slot] = SpawnWorker(job, level);
        }

        /// <summary>Puts the bodies back for hires that were already paid for in an earlier session.</summary>
        private void SpawnExistingStaff()
        {
            TryRestore(YardUpgrade.HireCarry, YardWorker.Job.Carry);
            TryRestore(YardUpgrade.HireServe, YardWorker.Job.Serve);
            TryRestore(YardUpgrade.HireCollect, YardWorker.Job.Collect);
        }

        private void TryRestore(YardUpgrade kind, YardWorker.Job job)
        {
            int level = _market.Level(IslandKey, kind);
            if (level <= 0) return;
            _staff[(int)job] = SpawnWorker(job, level);
        }

        private YardWorker SpawnWorker(YardWorker.Job job, int level)
        {
            var go = new GameObject("Eleman_" + job);
            go.transform.SetParent(transform, false);
            var worker = go.AddComponent<YardWorker>();

            // The two ends of the leg each job actually works. The carrier's are the real pad and the
            // real counter, because it really does move bars between them.
            Vector3 pickup, drop;
            switch (job)
            {
                case YardWorker.Job.Carry:
                    pickup = transform.TransformPoint(_padSpot + new Vector3(-5f, WorkerHeight, -3f));
                    drop = transform.TransformPoint(_counterSpot + new Vector3(4f, WorkerHeight, 3.4f));
                    break;
                case YardWorker.Job.Serve:
                    // Behind the counter, on the customers' side — that is where a cashier stands, and
                    // it keeps them out of the lane the player unloads from.
                    pickup = drop = transform.TransformPoint(_counterSpot + new Vector3(0f, WorkerHeight, -2.6f));
                    break;
                default:
                    pickup = drop = transform.TransformPoint(_cashSpot + new Vector3(0f, WorkerHeight, 0f));
                    break;
            }

            Material tint = MarketYardBuild.Mat(
                job == YardWorker.Job.Carry ? new Color(0.20f, 0.55f, 0.36f) :
                job == YardWorker.Job.Serve ? new Color(0.17f, 0.45f, 0.48f)
                                            : new Color(0.32f, 0.52f, 0.24f));

            worker.Configure(job, _market, IslandKey, _counter, _cash, pickup, drop, level,
                             _prefabs, _ore, tint);
            if (job == YardWorker.Job.Serve && _counter != null) _counter.SetCashier(worker);
            return worker;
        }

        private const float WorkerHeight = 0.95f;

        /// <summary>
        /// Where the nth pad in the rank stands: three north of the west doorway and three south of it,
        /// mirrored, so the rank reads as one line down the wall with the way through in the middle.
        ///
        /// It has to be split, because that wall is a wall with a door in it — every yard but the first
        /// has the previous market on the other side of it, through a gap ten wide at z = 0. A pad within
        /// reach of that gap is a pad you buy from on your way in, which is the whole complaint. The
        /// nearest one stands 5.6 out, better than two body widths clear of anyone walking the passage.
        ///
        /// The column is at x = -17.5: three units off the wall, because the camera looks down over it and
        /// a wall hides about its own height of floor in front of itself — a pad in that band has a price
        /// on it nobody can read. The outer pair stop short of the ramp at the north end and of the band
        /// the south wall hides at the other.
        /// </summary>
        private static Vector3 PadSpot(int index)
        {
            int fromEnd = index < 3 ? index : 5 - index;
            float z = PadRankEnd - PadSpacing * fromEnd;
            return new Vector3(-17.5f, 0f, index < 3 ? z : -z);
        }

        /// <summary>How far from the middle of the wall the two ends of the rank sit.</summary>
        private const float PadRankEnd = 14.8f;

        /// <summary>
        /// Gap between two pads in the rank, measured centre to centre.
        ///
        /// Paired with the trigger size in <see cref="Pad"/>; neither can be changed alone. A pad is
        /// entered when the player's CAPSULE touches its trigger box — 1.14 of radius on top of half the
        /// box — so a pad reaches about 2 units past its own centre, and two of them must stand more than
        /// twice that apart or there is floor between them that quietly buys from both at once.
        /// </summary>
        private const float PadSpacing = 4.8f;

        private UpgradePad Pad(YardUpgrade kind, Vector3 at)
        {
            var go = new GameObject("Ped_" + kind);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = at;

            GameObject face = GameObject.CreatePrimitive(PrimitiveType.Cube);
            face.name = "Yuzey";
            Destroy(face.GetComponent<Collider>());     // the trigger below is the whole interaction
            face.transform.SetParent(go.transform, false);
            face.transform.localPosition = new Vector3(0f, 0.09f, 0f);
            face.transform.localScale = new Vector3(3.4f, 0.18f, 3.4f);

            // MUCH smaller than the slab it stands on, and that is the fix for buying by accident.
            //
            // The trigger is met by the player's CAPSULE, not his centre, so what a pad really reaches is
            // half this box plus his 1.14 of radius: 2.04, which is a hair past the edge of the slab. He
            // has to be standing on it. At the old 4.4 it reached 3.34 — a metre and a third of thin air
            // around every pad — which is why walking along the wall bought whatever you walked past, and
            // why the gaps in the rank were not wide enough to be between two pads rather than in both.
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, 1.5f, 0f);
            box.size = new Vector3(1.8f, 3f, 1.8f);

            var pad = go.AddComponent<UpgradePad>();
            pad.Configure(_market, IslandKey, kind, face.GetComponent<MeshRenderer>());
            return pad;
        }

        /// <summary>An empty holder carrying a trigger volume — the shape of every "stand here" in a yard.</summary>
        private Transform Zone(string name, Vector3 at, Vector3 centre, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = at;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = centre;
            box.size = size;
            return go.transform;
        }
    }
}
