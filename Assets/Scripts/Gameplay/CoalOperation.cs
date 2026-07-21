using System.Collections.Generic;
using Game.Core;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Drives the automated production cycle on the player-built <b>Coal</b> map. It reads the map's own
    /// labelled landmark objects <b>by name</b> and never moves the static layout — only the vehicles and
    /// the ore/bar piles animate. Vehicles follow the tiles the designer placed:
    /// <list type="bullet">
    /// <item>Trains run along their <c>SM_Rail_Straight</c> line between a mountain and the storage shed:
    /// hidden inside the mountain while loading, emerge with full wagons, haul along the rails, hide inside
    /// the shed to dump onto the storage pile (waiting while the yard is full), return empty. The second
    /// mine's train exists from the start but only activates when its ghost buildings are unlocked.</item>
    /// <item>Trucks drive one-way around the closed <c>SM_Road_Straight</c> loop they were parked on:
    /// ore trucks load at the storage pile and empty into the smelter, the cargo trucks load bars at the
    /// refined pile and sell at the market. A truck with nothing to haul parks at its wait spot. Locked
    /// fleet trucks sit ghosted in the parking area until bought via the <b>Trucks</b> upgrade axis.</item>
    /// </list>
    /// Tycoon layer (GDD §3): every station has multiple upgrade axes, plus one-time ghost-building unlocks
    /// (second mine line / second smelter / trade post). Income is tracked as a trailing $/min for the HUD.
    /// Self-contained: cash lands in <see cref="WalletService"/>; levels persist in <see cref="SaveData"/>.
    /// </summary>
    public sealed class CoalOperation : MonoBehaviour
    {
        [Header("Tuning (level-0 base rates)")]
        [SerializeField] private float trainSpeed = 18f;
        [SerializeField] private float truckSpeed = 20f;
        [SerializeField] private float trainOrePerTrip = 12f;
        [SerializeField] private float oreTruckCapacity = 6f;
        [SerializeField] private float cargoTruckCapacity = 4f;
        [SerializeField] private float smeltPerSecond = 3f;
        [SerializeField] private float storageCapacity = 60f;
        [SerializeField] private float barCapacity = 40f;
        [SerializeField] private float barPrice = 45f;
        [SerializeField] private float dwellSeconds = 0.7f;   // base pause at every load/unload stop
        [SerializeField] private float wagonGap = 2.2f;
        [SerializeField] private float upgradeCostGrowth = 1.6f;
        [SerializeField] private string islandRootName = "Island_Coal";

        [Header("Ghost-building unlock prices")]
        [SerializeField] private float secondMineCost = 25000f;
        [SerializeField] private float secondSmelterCost = 10000f;
        [SerializeField] private float tradePostCost = 15000f;
        [SerializeField] private float thirdMineCost = 60000f;
        [SerializeField] private float warehouseCost = 20000f;
        [SerializeField] private float depotCost = 35000f;
        [SerializeField] private float exportDockCost = 40000f;
        [SerializeField] private float fourthMineCost = 150000f;
        [SerializeField] private float exportPriceBonus = 1.25f;   // dock sells bars at this multiple

        [Header("Path building")]
        [SerializeField] private float railSnapDistance = 8f;    // rail tile must sit this close to a mountain→storage line to belong to it
        [SerializeField] private float roadLinkDistance = 3.2f;  // max gap between road tiles that still counts as one connected loop
        [SerializeField] private int queueSpacing = 2;           // loop points a queued truck stops short of the truck ahead

        // ---- upgrade catalog (station × axis; ids "coal#<s>#<a>" in SaveData) ----
        private const int StMine = 0, StTrain = 1, StStorage = 2, StOreTrucks = 3, StSmelter = 4, StCargoTrucks = 5, StMarket = 6;
        private static readonly string[] StationList = { "MINE", "TRAIN", "STORAGE", "ORE TRUCKS", "SMELTER", "CARGO TRUCKS", "MARKET" };
        private static readonly string[][] AxisList =
        {
            new[] { "Richness", "Load Speed" },
            new[] { "Speed", "Wagons", "Wagon Cargo" },
            new[] { "Capacity", "Transfer Speed" },
            new[] { "Trucks", "Speed", "Capacity" },
            new[] { "Smelt Speed", "Bar Storage" },
            new[] { "Trucks", "Speed", "Capacity" },
            new[] { "Price", "Sell Speed" },
        };
        private static readonly double[][] AxisBaseCost =
        {
            new[] { 60d, 80d },
            new[] { 80d, 400d, 100d },
            new[] { 100d, 90d },
            new[] { 500d, 70d, 85d },
            new[] { 120d, 110d },
            new[] { 600d, 90d, 95d },
            new[] { 150d, 120d },
        };
        private static readonly int[][] AxisMaxLv =   // 0 = uncapped
        {
            new[] { 0, 0 },
            new[] { 0, 3, 0 },
            new[] { 0, 0 },
            new[] { 2, 0, 0 },
            new[] { 0, 0 },
            new[] { 2, 0, 0 },
            new[] { 0, 0 },
        };
        private readonly int[][] _lv = { new int[2], new int[3], new int[2], new int[3], new int[2], new int[3], new int[2] };

        // ---- ghost-building unlocks (ids "coalu#<u>" in SaveData) ----
        public const int UnlockSecondMine = 0, UnlockSecondSmelter = 1, UnlockTradePost = 2, UnlockThirdMine = 3,
                         UnlockWarehouse = 4, UnlockDepot = 5, UnlockExportDock = 6, UnlockFourthMine = 7;
        private static readonly string[] UnlockList =
        {
            "SECOND MINE + RAIL LINE", "SECOND SMELTER (2x smelt)", "TRADE POST (+50% price)", "THIRD MINE + RAIL LINE",
            "WAREHOUSE (2x storage)", "TRAIN DEPOT (+25% train speed)", "EXPORT DOCK (+25% export price)", "FOURTH MINE + RAIL LINE",
        };
        // scene objects belonging to each unlock, matched by name prefix ("ghostx_*" = placed with real
        // materials; the code ghosts them at runtime until bought)
        private static readonly string[][] UnlockPrefixes =
        {
            null, null, null, null,
            new[] { "ghostx_warehouse" },
            new[] { "ghostx_depot" },
            new[] { "ghostx_dock", "ghostx_roadP" },
            new[] { "ghostx_mine4", "ghostx_rail4" },
        };
        private readonly bool[] _unlocked = new bool[8];
        private Renderer[][] _unlockRends; private Material[][][] _unlockMats;   // per unlock: ghosted renderers + originals

        // ---- landmarks (found by name under the island root) ----
        private Transform _islandRoot;
        private Transform _mountain, _ghostMine, _ghostMine2, _storage, _orePile, _refinery, _ghostRefinery, _refinedPile, _market, _ghostMarket, _waitSpot;
        private Transform _dock, _mine4;
        private Transform _oreHeap, _barHeap; private float _oreHeapY0, _barHeapY0;

        // ---- economy ----
        private double _storeOre, _refOre, _bars;
        private WalletService _wallet;
        private SaveData _data;
        private Material _oreMat, _barMat, _ghostMat;

        // ---- income meter ($ earned per trailing minute) ----
        private readonly double[] _minuteBuckets = new double[60];
        private int _minIdx, _minFilled; private float _minAccum; private double _earnedThisSecond;
        public double CashPerMinute { get; private set; }

        // ---- trains ----
        private enum TR { LoadMountain, Haul, Deposit, Return }
        private sealed class TrainAgent
        {
            public Transform engine;
            public Transform[] wagons;      // full pool (MaxWagons); only the first ActiveWagons show
            public GameObject[] wagonOre;
            public float engineY; public float[] wagonY;
            public Vector3[] path;          // [0]=mountain gate … [n-1]=storage gate
            public int wp;
            public TR state; public float timer; public double carry;
            public bool active;
        }
        private const int BaseWagons = 3, MaxWagons = 6;
        private TrainAgent _train1, _train2, _train3, _train4;   // 1: coal mine · 2: "ghost_mine (1)" · 3: "ghost_mine"+GH rails · 4: "ghostx_mine4"+south line

        // ---- trucks ----
        private enum TK { ToLoad, Loading, ToDrop, Dropping, ToIdle, Idle }
        private enum Route { Ore, Market, Export }   // ore: pile→smelter · market: bars→market · export: bars→dock
        private sealed class TruckAgent
        {
            public Transform body;
            public GameObject load;
            public float y;
            public Vector3[] loop;
            public int wp;
            public int loadIdx, dropIdx, idleIdx;
            public Route route;
            public int slot;                // order within its fleet; slot < fleet count → active
            public int sceneFleet;          // trucks physically placed on this loop (export fleet size)
            public double carry;
            public float timer;
            public TK state;
            public bool active;
            public Renderer[] rends; public Material[][] origMats;   // for the ghost look while locked
            public Vector3 bayPos; public Quaternion bayRot;         // parking-lot spot while locked
        }
        private const int OreBaseTrucks = 2, CargoBaseTrucks = 1;
        private TruckAgent[] _agents;

        private bool _ready;

        // ---- public surface for the HUD ----
        public double StorageOre => _storeOre;
        public double Bars => _bars;
        public int StationCount => StationList.Length;
        public string StationName(int s) => StationList[s];
        public int AxisCount(int s) => AxisList[s].Length;
        public string AxisName(int s, int a) => AxisList[s][a];
        public int AxisLevel(int s, int a) => _lv[s][a];
        public bool AxisMaxed(int s, int a) => AxisMaxLv[s][a] > 0 && _lv[s][a] >= AxisMaxLv[s][a];
        public BigDouble AxisCost(int s, int a) => new BigDouble(AxisBaseCost[s][a] * System.Math.Pow(upgradeCostGrowth, _lv[s][a]));
        public int UnlockCount => UnlockList.Length;
        public string UnlockName(int u) => UnlockList[u];
        public bool IsUnlocked(int u) => _unlocked[u];
        public BigDouble UnlockCost(int u) =>
            new BigDouble(u == UnlockSecondMine ? secondMineCost
                : u == UnlockSecondSmelter ? secondSmelterCost
                : u == UnlockTradePost ? tradePostCost
                : u == UnlockThirdMine ? thirdMineCost
                : u == UnlockWarehouse ? warehouseCost
                : u == UnlockDepot ? depotCost
                : u == UnlockExportDock ? exportDockCost : fourthMineCost);

        /// <summary>Buy one level on a station axis: spends cash, applies the effect live, persists.</summary>
        public bool TryUpgrade(int s, int a)
        {
            if (s < 0 || s >= StationList.Length || a < 0 || a >= AxisList[s].Length || _wallet == null) return false;
            if (AxisMaxed(s, a)) return false;
            if (!_wallet.TrySpendCash(AxisCost(s, a))) return false;
            _lv[s][a]++;
            SaveLevel("coal#" + s + "#" + a, _lv[s][a]);
            if ((s == StOreTrucks || s == StCargoTrucks) && a == 0) ApplyFleetStates();
            return true;
        }

        /// <summary>Buy a one-time ghost-building unlock: turns the ghost solid and applies its bonus.</summary>
        public bool TryUnlock(int u)
        {
            if (u < 0 || u >= _unlocked.Length || _unlocked[u] || _wallet == null) return false;
            if (!_wallet.TrySpendCash(UnlockCost(u))) return false;
            _unlocked[u] = true;
            SaveLevel("coalu#" + u, 1);
            ApplyUnlock(u);
            return true;
        }

        // ---- effective rates (base × axis levels × unlock bonuses) ----
        private float MineDwell => dwellSeconds / (1f + 0.2f * _lv[StMine][1]);
        private float EffTrainOre => trainOrePerTrip * (1f + 0.25f * _lv[StMine][0]) * (ActiveWagons / (float)BaseWagons) * (1f + 0.25f * _lv[StTrain][2]);
        private float EffTrainSpeed => trainSpeed * (1f + 0.15f * _lv[StTrain][0]) * (_unlocked[UnlockDepot] ? 1.25f : 1f);
        private int ActiveWagons => Mathf.Min(BaseWagons + _lv[StTrain][1], MaxWagons);
        private float EffStorageFull => storageCapacity * (1f + 0.5f * _lv[StStorage][0]) * (_unlocked[UnlockWarehouse] ? 2f : 1f);
        private float StorageDwell => dwellSeconds / (1f + 0.2f * _lv[StStorage][1]);
        private int OreTruckCount => OreBaseTrucks + _lv[StOreTrucks][0];
        private float EffOreSpeed => truckSpeed * (1f + 0.15f * _lv[StOreTrucks][1]);
        private float EffOreCap => oreTruckCapacity * (1f + 0.30f * _lv[StOreTrucks][2]);
        private float EffSmelt => smeltPerSecond * (1f + 0.30f * _lv[StSmelter][0]) * (_unlocked[UnlockSecondSmelter] ? 2f : 1f);
        private float EffBarCap => barCapacity * (1f + 0.5f * _lv[StSmelter][1]);
        private int CargoTruckCount => CargoBaseTrucks + _lv[StCargoTrucks][0];
        private float EffCargoSpeed => truckSpeed * (1f + 0.15f * _lv[StCargoTrucks][1]);
        private float EffCargoCap => cargoTruckCapacity * (1f + 0.30f * _lv[StCargoTrucks][2]);
        private float EffBarPrice => barPrice * (1f + 0.40f * _lv[StMarket][0]) * (_unlocked[UnlockTradePost] ? 1.5f : 1f);
        private float MarketDwell => dwellSeconds / (1f + 0.2f * _lv[StMarket][1]);

        private void Start()
        {
            _wallet = ServiceLocator.Get<WalletService>();
            _data = ServiceLocator.Get<SaveData>();
            LoadLevels();
            var root = GameObject.Find(islandRootName);
            if (root == null) { Debug.LogWarning("CoalOperation: '" + islandRootName + "' not found — disabled."); enabled = false; return; }
            _islandRoot = root.transform;

            _mountain = Child(_islandRoot, "mine_Coal");
            _ghostMine = Child(_islandRoot, "ghost_mine");
            _ghostMine2 = Child(_islandRoot, "ghost_mine (1)");
            _storage = Child(_islandRoot, "storage");
            _orePile = Child(_islandRoot, "storage ore pile here");
            _refinery = Child(_islandRoot, "refinery");
            _ghostRefinery = Child(_islandRoot, "ghost_refinery");
            _refinedPile = Child(_islandRoot, "refined ores pile here");
            _market = Child(_islandRoot, "market");
            _ghostMarket = Child(_islandRoot, "ghost_market");
            _waitSpot = Child(_islandRoot, "waiting ore trucks wait here");
            _dock = Child(_islandRoot, "ghostx_dock");
            _mine4 = Child(_islandRoot, "ghostx_mine4");

            if (_mountain == null || _storage == null || _orePile == null ||
                _refinery == null || _refinedPile == null || _market == null)
            { Debug.LogWarning("CoalOperation: missing a core landmark — disabled."); enabled = false; return; }

            Transform engine = Child(_islandRoot, "train");
            if (engine == null) { Debug.LogWarning("CoalOperation: train not found — disabled."); enabled = false; return; }

            // ore/bar/ghost materials (ghost cloned from the map's own ghost buildings so the look matches)
            Renderer refRend = engine.GetComponentInChildren<Renderer>();
            Material src = refRend != null ? refRend.sharedMaterial : null;
            _oreMat = MakeMat(src, new Color(0.10f, 0.10f, 0.12f));   // coal ore (near-black)
            _barMat = MakeMat(src, new Color(0.88f, 0.55f, 0.18f));   // metal bars (amber)
            var ghostRend = _ghostMarket != null ? _ghostMarket.GetComponentInChildren<Renderer>() : null;
            _ghostMat = ghostRend != null ? ghostRend.sharedMaterial : MakeMat(null, new Color(1f, 1f, 1f, 0.35f));

            _oreHeap = MakeHeap(_orePile, _oreMat, out _oreHeapY0);
            _barHeap = MakeHeap(_refinedPile, _barMat, out _barHeapY0);

            _train1 = BuildTrain(engine, _mountain);
            _train1.active = true;
            // "ghost_mine (1)" sits at the head of the second (already-laid) rail line; "ghost_mine" at the
            // head of the GH ghost-rail line — each becomes a live train when its unlock is bought
            if (_ghostMine2 != null) _train2 = BuildTrain(CloneTrainRig(engine, "train2"), _ghostMine2);
            if (_ghostMine != null) _train3 = BuildTrain(CloneTrainRig(engine, "train3"), _ghostMine);
            if (_mine4 != null) _train4 = BuildTrain(CloneTrainRig(engine, "train4"), _mine4);

            BuildTruckAgents();
            BuildUnlockRegistry();
            ApplyFleetStates();
            for (int u = 0; u < _unlocked.Length; u++) if (_unlocked[u]) ApplyUnlock(u);

            _ready = true;
        }

        private void Update() { if (_ready) Tick(Time.deltaTime); }

        private void Tick(float dt)
        {
            if (dt <= 0f) return;
            TrainTick(_train1, dt);
            if (_train2 != null && _train2.active) TrainTick(_train2, dt);
            if (_train3 != null && _train3.active) TrainTick(_train3, dt);
            if (_train4 != null && _train4.active) TrainTick(_train4, dt);
            for (int i = 0; i < _agents.Length; i++) if (_agents[i].active) TruckTick(_agents[i], dt);
            Smelt(dt);
            UpdateHeaps();
            TickIncome(dt);
        }

        // ---------------- trains ----------------

        /// <summary>Wires a train agent from an engine + the shared wagon pool convention, and its rail path.</summary>
        private TrainAgent BuildTrain(Transform engine, Transform mountain)
        {
            var a = new TrainAgent { engine = engine, engineY = engine.position.y };

            // wagon pool: the 3 scene wagons belong to train 1; clones fill each pool up to MaxWagons
            var wagons = new List<Transform>();
            if (engine.name == "train")
            {
                var w0 = Child(_islandRoot, "wagon"); if (w0 != null) wagons.Add(w0);
                var w1 = Child(_islandRoot, "wagon.001"); if (w1 != null) wagons.Add(w1);
                var w2 = Child(_islandRoot, "wagon.002"); if (w2 != null) wagons.Add(w2);
            }
            Transform template = wagons.Count > 0 ? wagons[wagons.Count - 1] : null;
            if (template == null) { var w0 = Child(_islandRoot, "wagon"); template = w0; }
            while (template != null && wagons.Count < MaxWagons)
            {
                Transform w = Instantiate(template.gameObject, _islandRoot).transform;
                w.name = engine.name + "_wagon" + wagons.Count;
                StripOpChildren(w);
                wagons.Add(w);
            }
            a.wagons = wagons.ToArray();
            a.wagonY = new float[a.wagons.Length];
            a.wagonOre = new GameObject[a.wagons.Length];
            for (int i = 0; i < a.wagons.Length; i++)
            {
                a.wagonY[i] = a.wagons[i].position.y;   // clones inherit their template's height
                a.wagonOre[i] = MakeChunk(a.wagons[i], _oreMat, new Vector3(0f, 0.9f, 0f), new Vector3(1.6f, 0.8f, 2.0f));
            }

            a.path = BuildRailPath(mountain, _storage);
            SetTrainVisible(a, false);
            a.state = TR.LoadMountain; a.timer = dwellSeconds;
            return a;
        }

        /// <summary>Clones the engine for an expansion mine's train (one-time, at Start — not a hot path).</summary>
        private Transform CloneTrainRig(Transform engine, string cloneName)
        {
            Transform t = Instantiate(engine.gameObject, _islandRoot).transform;
            t.name = cloneName;
            StripOpChildren(t);
            return t;
        }

        /// <summary>Removes cloned OpLoad/OpHeap children so rig clones start clean.</summary>
        private static void StripOpChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                Transform c = t.GetChild(i);
                if (c.name == "OpLoad" || c.name == "OpHeap") Destroy(c.gameObject);
            }
        }

        /// <summary>Rail tiles near and aligned with the mountain→storage line, ordered: [mountain, tile…, storage].</summary>
        private Vector3[] BuildRailPath(Transform mountain, Transform storage)
        {
            Vector3 a = Flat(mountain.position), b = Flat(storage.position);
            Vector3 abDir = (b - a).normalized;
            var tiles = new List<Vector3>();
            var roots = gameObject.scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                string tn = roots[i].name;
                if (!tn.StartsWith("SM_Rail_") && !tn.StartsWith("GH_Rail_") && !tn.StartsWith("ghostx_rail")) continue;
                Transform tile = roots[i].transform;
                if (DistToSegmentXZ(tile.position, a, b) > railSnapDistance) continue;
                // all three rail lines converge at storage — a tight alignment gate (~11°) keeps only the
                // tiles that belong to THIS run out of the shared corridor
                Vector3 f = Flat(tile.forward).normalized;
                Vector3 r = Flat(tile.right).normalized;
                if (Mathf.Max(Mathf.Abs(Vector3.Dot(f, abDir)), Mathf.Abs(Vector3.Dot(r, abDir))) < 0.98f) continue;
                tiles.Add(tile.position);
            }
            Vector3 ab = b - a;
            tiles.Sort((u, v) => Vector3.Dot(Flat(u) - a, ab).CompareTo(Vector3.Dot(Flat(v) - a, ab)));

            var path = new Vector3[tiles.Count + 2];
            path[0] = mountain.position;
            for (int i = 0; i < tiles.Count; i++) path[i + 1] = tiles[i];
            path[path.Length - 1] = storage.position;
            return path;
        }

        private void TrainTick(TrainAgent a, float dt)
        {
            switch (a.state)
            {
                case TR.LoadMountain:
                    a.timer -= dt;
                    if (a.timer <= 0f)
                    {
                        a.carry = EffTrainOre;
                        ShowTrainAt(a, a.path[0], a.path[1]);
                        SetWagonOre(a, true);
                        a.wp = 1; a.state = TR.Haul;
                    }
                    break;
                case TR.Haul:
                    if (DriveTrain(a, true, dt)) { SetTrainVisible(a, false); a.timer = StorageDwell; a.state = TR.Deposit; }
                    break;
                case TR.Deposit:
                    a.timer -= dt;
                    if (a.timer > 0f) break;
                    double space = EffStorageFull - _storeOre;
                    if (space > 0d)
                    {
                        double dep = System.Math.Min(space, a.carry);
                        _storeOre += dep; a.carry -= dep;
                    }
                    if (a.carry > 0.01d) break;   // yard full — the train waits inside the shed until trucks make room
                    a.carry = 0d;
                    ShowTrainAt(a, a.path[a.path.Length - 1], a.path[a.path.Length - 2]);
                    SetWagonOre(a, false);
                    a.wp = a.path.Length - 2; a.state = TR.Return;
                    break;
                case TR.Return:
                    if (DriveTrain(a, false, dt)) { SetTrainVisible(a, false); a.timer = MineDwell; a.state = TR.LoadMountain; }
                    break;
            }
        }

        /// <summary>Walks the engine along its rail path (forward = toward storage). True on arrival.</summary>
        private bool DriveTrain(TrainAgent a, bool toStorage, float dt)
        {
            Vector3 pos = a.engine.position;
            Vector3 dir = a.engine.forward;
            float budget = EffTrainSpeed * dt;
            bool arrived = false;
            int guard = a.path.Length + 2;
            while (budget > 0f && guard-- > 0)
            {
                Vector3 target = a.path[a.wp]; target.y = a.engineY;
                Vector3 d = target - pos; d.y = 0f; float dist = d.magnitude;
                if (dist > 1e-4f) dir = d / dist;
                if (dist <= budget)
                {
                    pos = target; budget -= dist;
                    bool atEnd = toStorage ? a.wp >= a.path.Length - 1 : a.wp <= 0;
                    if (atEnd) { arrived = true; break; }
                    a.wp += toStorage ? 1 : -1;
                }
                else { pos += dir * budget; budget = 0f; }
            }
            a.engine.position = pos;
            a.engine.rotation = Quaternion.LookRotation(dir, Vector3.up);
            PlaceWagons(a, dir);
            return arrived;
        }

        private void PlaceWagons(TrainAgent a, Vector3 dir)
        {
            int n = ActiveWagons;
            for (int i = 0; i < a.wagons.Length && i < n; i++)
            {
                Vector3 wp = a.engine.position - dir * (wagonGap * (i + 1));
                wp.y = a.wagonY[i];
                a.wagons[i].position = wp;
                a.wagons[i].rotation = a.engine.rotation;
            }
        }

        private void ShowTrainAt(TrainAgent a, Vector3 pos, Vector3 towards)
        {
            Vector3 d = towards - pos; d.y = 0f; if (d.sqrMagnitude < 1e-4f) d = a.engine.forward; d.Normalize();
            SetTrainVisible(a, true);
            a.engine.position = new Vector3(pos.x, a.engineY, pos.z);
            a.engine.rotation = Quaternion.LookRotation(d, Vector3.up);
            PlaceWagons(a, d);
        }

        private void SetTrainVisible(TrainAgent a, bool on)
        {
            a.engine.gameObject.SetActive(on);
            int n = ActiveWagons;
            for (int i = 0; i < a.wagons.Length; i++) a.wagons[i].gameObject.SetActive(on && i < n);
        }

        private void SetWagonOre(TrainAgent a, bool on)
        {
            for (int i = 0; i < a.wagonOre.Length; i++) Show(a.wagonOre[i], on && i < ActiveWagons);
        }

        // ---------------- trucks ----------------

        /// <summary>
        /// Clusters road tiles into closed loops, orients each loop pickup→drop-off the short way round,
        /// then builds the full fleet per loop: scene trucks first, pooled clones after, each with staggered
        /// stop points and a parking-lot bay for its locked/ghost state.
        /// </summary>
        private void BuildTruckAgents()
        {
            var loops = BuildRoadLoops();
            var sceneTrucks = new List<Transform>();
            foreach (Transform t in _islandRoot) if (t.name.StartsWith("truck_road")) sceneTrucks.Add(t);

            // greedy loop→route matching: score = dist(loop, source) + dist(loop, dest); the globally best
            // pairs win, so a loop that skims past a foreign landmark can't steal the wrong route
            int routeCount = _dock != null ? 3 : 2;
            var routeOfLoop = new int[loops.Count];
            for (int i = 0; i < routeOfLoop.Length; i++) routeOfLoop[i] = -1;
            var routeTaken = new bool[3];
            for (int pick = 0; pick < Mathf.Min(loops.Count, routeCount); pick++)
            {
                int bestLoop = -1, bestRoute = -1; float bestScore = float.MaxValue;
                for (int li2 = 0; li2 < loops.Count; li2++)
                {
                    if (routeOfLoop[li2] >= 0) continue;
                    for (int r = 0; r < routeCount; r++)
                    {
                        if (routeTaken[r]) continue;
                        Vector3 src = r == (int)Route.Ore ? _orePile.position : _refinedPile.position;
                        Vector3 dst = r == (int)Route.Ore ? _refinery.position : r == (int)Route.Market ? _market.position : _dock.position;
                        float score = Mathf.Sqrt(MinSqrXZ(loops[li2], src)) + Mathf.Sqrt(MinSqrXZ(loops[li2], dst));
                        if (score < bestScore) { bestScore = score; bestLoop = li2; bestRoute = r; }
                    }
                }
                if (bestLoop < 0) break;
                routeOfLoop[bestLoop] = bestRoute; routeTaken[bestRoute] = true;
            }

            var agents = new List<TruckAgent>();
            for (int li = 0; li < loops.Count; li++)
            {
                if (routeOfLoop[li] < 0) continue;   // more loops than routes — extra loop is decorative
                List<Vector3> loop = loops[li];
                Route route = (Route)routeOfLoop[li];
                Vector3 srcPos = route == Route.Ore ? _orePile.position : _refinedPile.position;
                Vector3 dstPos = route == Route.Ore ? _refinery.position : route == Route.Market ? _market.position : _dock.position;
                int load = NearestIndex(loop, srcPos);
                int drop = NearestIndex(loop, dstPos);
                int n = loop.Count;
                if (((drop - load + n) % n) > n / 2)   // one-way: drive the short way from pickup to drop-off
                {
                    loop.Reverse();
                    load = n - 1 - load; drop = n - 1 - drop;
                }
                int idle = route == Route.Ore && _waitSpot != null && MinSqrXZ(loop, _waitSpot.position) < 400f ? NearestIndex(loop, _waitSpot.position) : load;

                // parking-lot bay row for locked trucks: at the wait marker if this loop has one,
                // otherwise just inside the loop next to the idle stop
                Vector3 centroid = Centroid(loop);
                Vector3 idlePt = loop[idle];
                Vector3 along = Flat(loop[(idle + 1) % n] - idlePt).normalized;
                Vector3 bayBase;
                if (route == Route.Ore && _waitSpot != null) bayBase = _waitSpot.position;
                else { Vector3 inward = Flat(centroid - idlePt).normalized; bayBase = idlePt + inward * 4.5f; }

                // this loop's trucks: scene trucks parked on it first (slot order), clones fill the pool
                // (the export fleet is fixed to its scene trucks — the dock unlock is its gate)
                var fleet = new List<Transform>();
                for (int ti = 0; ti < sceneTrucks.Count; ti++)
                    if (NearestLoop(loops, sceneTrucks[ti].position) == li) fleet.Add(sceneTrucks[ti]);
                if (fleet.Count == 0) continue;
                int sceneFleet = fleet.Count;
                int maxFleet = route == Route.Ore ? OreBaseTrucks + AxisMaxLv[StOreTrucks][0]
                    : route == Route.Market ? CargoBaseTrucks + AxisMaxLv[StCargoTrucks][0] : sceneFleet;
                Transform truckTemplate = fleet[0];
                while (fleet.Count < maxFleet)
                {
                    Transform c = Instantiate(truckTemplate.gameObject, _islandRoot).transform;
                    c.name = truckTemplate.name + "_fleet" + fleet.Count;
                    StripOpChildren(c);
                    fleet.Add(c);
                }

                for (int slot = 0; slot < fleet.Count; slot++)
                {
                    Transform body = fleet[slot];
                    int shift = slot * queueSpacing;
                    var a = new TruckAgent
                    {
                        body = body,
                        y = truckTemplate.position.y,
                        loop = loop.ToArray(),
                        wp = NearestIndex(loop, body.position),
                        loadIdx = (load - shift % n + n) % n,
                        dropIdx = (drop - shift % n + n) % n,
                        idleIdx = (idle - shift % n + n) % n,
                        route = route,
                        slot = slot,
                        sceneFleet = sceneFleet,
                        state = TK.ToIdle,
                        bayPos = bayBase + along * (4.5f * slot),
                        bayRot = along.sqrMagnitude > 0.01f ? Quaternion.LookRotation(along, Vector3.up) : body.rotation,
                    };
                    var rends = body.GetComponentsInChildren<Renderer>(true);
                    a.rends = rends;
                    a.origMats = new Material[rends.Length][];
                    for (int r = 0; r < rends.Length; r++) a.origMats[r] = rends[r].sharedMaterials;
                    a.load = MakeChunk(body, route == Route.Ore ? _oreMat : _barMat, new Vector3(0f, 1.0f, 0f), new Vector3(1.4f, 0.8f, 2.0f));
                    agents.Add(a);
                }
            }
            _agents = agents.ToArray();
            if (_agents.Length == 0) Debug.LogWarning("CoalOperation: no trucks found on any road loop.");
        }

        /// <summary>Active trucks drive; the next locked truck sits ghosted in the parking bay; the rest hide.</summary>
        private void ApplyFleetStates()
        {
            if (_agents == null) return;
            for (int i = 0; i < _agents.Length; i++)
            {
                TruckAgent a = _agents[i];
                int count = a.route == Route.Ore ? OreTruckCount
                    : a.route == Route.Market ? CargoTruckCount
                    : _unlocked[UnlockExportDock] ? a.sceneFleet : 0;   // export fleet gated by the dock
                if (a.slot < count)
                {
                    if (a.active) continue;
                    a.active = true;
                    SetTruckGhost(a, false);
                    a.body.gameObject.SetActive(true);
                    Vector3 p = a.loop[a.idleIdx]; p.y = a.y;
                    a.body.position = p;
                    a.wp = a.idleIdx; a.state = TK.ToIdle; a.carry = 0d;
                    Show(a.load, false);
                }
                else if (a.slot == count)   // next truck to buy: ghosted in the depot bay
                {
                    a.active = false;
                    SetTruckGhost(a, true);
                    a.body.gameObject.SetActive(true);
                    a.body.position = new Vector3(a.bayPos.x, a.y, a.bayPos.z);
                    a.body.rotation = a.bayRot;
                    Show(a.load, false);
                }
                else
                {
                    a.active = false;
                    a.body.gameObject.SetActive(false);
                }
            }
        }

        private void SetTruckGhost(TruckAgent a, bool ghost)
        {
            for (int r = 0; r < a.rends.Length; r++)
            {
                if (a.rends[r] == null) continue;
                if (a.load != null && a.rends[r].transform.IsChildOf(a.load.transform)) continue;
                if (ghost)
                {
                    var mats = new Material[a.origMats[r].Length];
                    for (int m = 0; m < mats.Length; m++) mats[m] = _ghostMat;
                    a.rends[r].sharedMaterials = mats;
                }
                else a.rends[r].sharedMaterials = a.origMats[r];
            }
        }

        /// <summary>Flood-fills root SM_Road_* tiles into connected clusters, each chained into an ordered ring.</summary>
        private List<List<Vector3>> BuildRoadLoops()
        {
            var tiles = new List<Vector3>();
            var roots = gameObject.scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                if (roots[i].name.StartsWith("SM_Road_") || roots[i].name.StartsWith("ghostx_road")) tiles.Add(roots[i].transform.position);

            var loops = new List<List<Vector3>>();
            var used = new bool[tiles.Count];
            float link2 = roadLinkDistance * roadLinkDistance;
            for (int i = 0; i < tiles.Count; i++)
            {
                if (used[i]) continue;
                var cluster = new List<int> { i }; used[i] = true;
                for (int c = 0; c < cluster.Count; c++)
                    for (int j = 0; j < tiles.Count; j++)
                        if (!used[j] && SqrXZ(tiles[cluster[c]], tiles[j]) <= link2) { used[j] = true; cluster.Add(j); }
                if (cluster.Count < 4) continue;   // stray decor tiles are not a drivable loop

                // nearest-neighbour walk chains the cluster into ring order
                var ring = new List<Vector3>(cluster.Count) { tiles[cluster[0]] };
                var taken = new bool[cluster.Count]; taken[0] = true;
                for (int k = 1; k < cluster.Count; k++)
                {
                    int bi = -1; float bd = float.MaxValue;
                    for (int j = 0; j < cluster.Count; j++)
                    {
                        if (taken[j]) continue;
                        float d = SqrXZ(ring[ring.Count - 1], tiles[cluster[j]]);
                        if (d < bd) { bd = d; bi = j; }
                    }
                    taken[bi] = true; ring.Add(tiles[cluster[bi]]);
                }
                loops.Add(ring);
            }
            return loops;
        }

        private static int NearestLoop(List<List<Vector3>> loops, Vector3 p)
        {
            int best = -1; float bd = float.MaxValue;
            for (int li = 0; li < loops.Count; li++)
            {
                float d = MinSqrXZ(loops[li], p);
                if (d < bd) { bd = d; best = li; }
            }
            return best;
        }

        private void TruckTick(TruckAgent a, float dt)
        {
            bool ore = a.route == Route.Ore;
            double avail = ore ? _storeOre : _bars;
            switch (a.state)
            {
                case TK.ToLoad:
                    if (DriveLoop(a, a.loadIdx, dt))
                    {
                        double take = System.Math.Min(ore ? EffOreCap : EffCargoCap, avail);
                        if (take <= 0.01d) { a.state = TK.ToIdle; break; }
                        if (ore) _storeOre -= take; else _bars -= take;
                        a.carry = take; Show(a.load, true);
                        a.timer = ore ? StorageDwell : dwellSeconds; a.state = TK.Loading;
                    }
                    break;
                case TK.Loading:
                    a.timer -= dt;
                    if (a.timer <= 0f) a.state = TK.ToDrop;
                    break;
                case TK.ToDrop:
                    if (DriveLoop(a, a.dropIdx, dt)) { a.timer = ore ? dwellSeconds : MarketDwell; a.state = TK.Dropping; }
                    break;
                case TK.Dropping:
                    a.timer -= dt;
                    if (a.timer > 0f) break;
                    if (ore) _refOre += a.carry;
                    else if (a.carry > 0.001d && _wallet != null)
                    {
                        double sale = a.carry * EffBarPrice * (a.route == Route.Export ? exportPriceBonus : 1f);
                        _wallet.AddCash(new BigDouble(sale));
                        _earnedThisSecond += sale;
                    }
                    a.carry = 0d; Show(a.load, false);
                    a.state = avail > 0.01d ? TK.ToLoad : TK.ToIdle;
                    break;
                case TK.ToIdle:
                    if (avail > 0.01d) { a.state = TK.ToLoad; break; }   // work appeared — head to the pickup instead
                    if (DriveLoop(a, a.idleIdx, dt)) a.state = TK.Idle;
                    break;
                case TK.Idle:
                    if (avail > 0.01d) a.state = TK.ToLoad;              // parked until there is something to haul
                    break;
            }
        }

        /// <summary>Advances a truck forward around its loop toward the stop point. True on arrival.</summary>
        private bool DriveLoop(TruckAgent a, int stopIdx, float dt)
        {
            Vector3 pos = a.body.position;
            Vector3 dir = a.body.forward;
            float budget = (a.route == Route.Ore ? EffOreSpeed : EffCargoSpeed) * dt;
            bool arrived = false;
            int guard = a.loop.Length + 2;
            while (budget > 0f && guard-- > 0)
            {
                Vector3 target = a.loop[a.wp]; target.y = a.y;
                Vector3 d = target - pos; d.y = 0f; float dist = d.magnitude;
                if (dist > 1e-4f) dir = d / dist;
                if (dist <= budget)
                {
                    pos = target; budget -= dist;
                    if (a.wp == stopIdx) { arrived = true; break; }
                    a.wp = (a.wp + 1) % a.loop.Length;
                }
                else { pos += dir * budget; budget = 0f; }
            }
            a.body.position = pos;
            a.body.rotation = Quaternion.LookRotation(dir, Vector3.up);
            return arrived;
        }

        private void Smelt(float dt)
        {
            if (_refOre <= 0d) return;
            double room = EffBarCap - _bars;                  // full bar store pauses the smelter (visible bottleneck)
            if (room <= 0d) return;
            double amt = System.Math.Min(System.Math.Min(_refOre, EffSmelt * dt), room);
            _refOre -= amt; _bars += amt;
        }

        // ---------------- ghost-building unlocks ----------------

        /// <summary>
        /// "ghostx_*" scene objects are placed with their REAL materials; here we cache those and swap in
        /// the ghost material while their unlock is locked. Buying the unlock restores the originals.
        /// </summary>
        private void BuildUnlockRegistry()
        {
            _unlockRends = new Renderer[UnlockList.Length][];
            _unlockMats = new Material[UnlockList.Length][][];
            var rendList = new List<Renderer>();
            var roots = gameObject.scene.GetRootGameObjects();
            for (int u = 0; u < UnlockList.Length; u++)
            {
                string[] prefixes = UnlockPrefixes[u];
                if (prefixes == null) continue;
                rendList.Clear();
                foreach (Transform t in _islandRoot)
                    for (int p = 0; p < prefixes.Length; p++)
                        if (t.name.StartsWith(prefixes[p])) { rendList.AddRange(t.GetComponentsInChildren<Renderer>(true)); break; }
                for (int i = 0; i < roots.Length; i++)
                    for (int p = 0; p < prefixes.Length; p++)
                        if (roots[i].name.StartsWith(prefixes[p])) { rendList.AddRange(roots[i].GetComponentsInChildren<Renderer>(true)); break; }
                var rends = rendList.ToArray();
                var mats = new Material[rends.Length][];
                for (int r = 0; r < rends.Length; r++) mats[r] = rends[r].sharedMaterials;
                _unlockRends[u] = rends;
                _unlockMats[u] = mats;
                if (!_unlocked[u]) SetGhosted(u, true);
            }
        }

        private void SetGhosted(int u, bool ghost)
        {
            Renderer[] rends = _unlockRends != null ? _unlockRends[u] : null;
            if (rends == null) return;
            for (int r = 0; r < rends.Length; r++)
            {
                if (rends[r] == null) continue;
                if (ghost)
                {
                    var arr = new Material[_unlockMats[u][r].Length];
                    for (int m = 0; m < arr.Length; m++) arr[m] = _ghostMat;
                    rends[r].sharedMaterials = arr;
                }
                else rends[r].sharedMaterials = _unlockMats[u][r];
            }
        }

        private void ApplyUnlock(int u)
        {
            SetGhosted(u, false);   // ghostx_* objects get their real materials back
            switch (u)
            {
                case UnlockSecondMine:
                    Solidify(_ghostMine2, _mountain);
                    if (_train2 != null && !_train2.active) { _train2.active = true; _train2.state = TR.LoadMountain; _train2.timer = dwellSeconds; }
                    break;
                case UnlockSecondSmelter:
                    Solidify(_ghostRefinery, _refinery);
                    break;
                case UnlockTradePost:
                    Solidify(_ghostMarket, _market);
                    break;
                case UnlockThirdMine:
                    Solidify(_ghostMine, _mountain);
                    SolidifyGhostRails();
                    if (_train3 != null && !_train3.active) { _train3.active = true; _train3.state = TR.LoadMountain; _train3.timer = dwellSeconds; }
                    break;
                case UnlockExportDock:
                    ApplyFleetStates();   // wakes the export loop's trucks
                    break;
                case UnlockFourthMine:
                    if (_train4 != null && !_train4.active) { _train4.active = true; _train4.state = TR.LoadMountain; _train4.timer = dwellSeconds; }
                    break;
            }
        }

        /// <summary>Swap a ghost building's materials for its real counterpart's — it "gets built".</summary>
        private static void Solidify(Transform ghost, Transform real)
        {
            if (ghost == null || real == null) return;
            var rr = real.GetComponentsInChildren<Renderer>();
            if (rr.Length == 0) return;
            Material[] mats = rr[0].sharedMaterials;
            var gr = ghost.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < gr.Length; i++) gr[i].sharedMaterials = mats;
        }

        private void SolidifyGhostRails()
        {
            var roots = gameObject.scene.GetRootGameObjects();
            Material[] railMats = null;
            for (int i = 0; i < roots.Length && railMats == null; i++)
                if (roots[i].name.StartsWith("SM_Rail_"))
                {
                    var r = roots[i].GetComponentInChildren<Renderer>();
                    if (r != null) railMats = r.sharedMaterials;
                }
            if (railMats == null) return;
            for (int i = 0; i < roots.Length; i++)
            {
                if (!roots[i].name.StartsWith("GH_Rail_")) continue;
                var rs = roots[i].GetComponentsInChildren<Renderer>();
                for (int r = 0; r < rs.Length; r++) rs[r].sharedMaterials = railMats;
            }
        }

        // ---------------- income meter ----------------
        private void TickIncome(float dt)
        {
            _minAccum += dt;
            if (_minAccum < 1f) return;
            _minAccum -= 1f;
            _minuteBuckets[_minIdx] = _earnedThisSecond;
            _earnedThisSecond = 0d;
            _minIdx = (_minIdx + 1) % _minuteBuckets.Length;
            if (_minFilled < _minuteBuckets.Length) _minFilled++;
            double sum = 0d;
            for (int i = 0; i < _minFilled; i++) sum += _minuteBuckets[i];
            CashPerMinute = sum * (60.0 / _minFilled);
        }

        // ---------------- pile visuals ----------------
        private void UpdateHeaps()
        {
            ScaleHeap(_oreHeap, _oreHeapY0, _storeOre, EffStorageFull);
            ScaleHeap(_barHeap, _barHeapY0, _bars, EffBarCap);
        }

        // ---- persistence ----
        private void LoadLevels()
        {
            if (_data == null || _data.islandLevels == null) return;
            for (int s = 0; s < StationList.Length; s++)
                for (int a = 0; a < AxisList[s].Length; a++)
                {
                    StationLevel e = FindLevel("coal#" + s + "#" + a);
                    if (e != null) _lv[s][a] = e.level;
                }
            for (int u = 0; u < _unlocked.Length; u++)
            {
                StationLevel e = FindLevel("coalu#" + u);
                _unlocked[u] = e != null && e.level > 0;
            }
        }

        private StationLevel FindLevel(string id)
        {
            var list = _data.islandLevels;
            for (int i = 0; i < list.Count; i++) if (list[i].id == id) return list[i];
            return null;
        }

        private void SaveLevel(string id, int level)
        {
            if (_data == null || _data.islandLevels == null) return;
            StationLevel e = FindLevel(id);
            if (e == null) { e = new StationLevel { id = id }; _data.islandLevels.Add(e); }
            e.level = level;
        }

        private void ScaleHeap(Transform heap, float baseY, double amount, double full)
        {
            if (heap == null) return;
            float f = Mathf.Clamp01((float)(amount / full));
            bool on = f > 0.02f;
            if (heap.gameObject.activeSelf != on) heap.gameObject.SetActive(on);
            if (!on) return;
            Vector3 s = heap.localScale; s.y = Mathf.Lerp(0.4f, 4.5f, f); heap.localScale = s;
            Vector3 pp = heap.position; pp.y = baseY + s.y * 0.5f; heap.position = pp;
        }

        // ---------------- geometry helpers ----------------
        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        private static float SqrXZ(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        private static float MinSqrXZ(List<Vector3> pts, Vector3 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < pts.Count; i++) { float d = SqrXZ(pts[i], p); if (d < best) best = d; }
            return best;
        }

        private static int NearestIndex(List<Vector3> pts, Vector3 p)
        {
            int bi = 0; float bd = float.MaxValue;
            for (int i = 0; i < pts.Count; i++) { float d = SqrXZ(pts[i], p); if (d < bd) { bd = d; bi = i; } }
            return bi;
        }

        private static Vector3 Centroid(List<Vector3> pts)
        {
            Vector3 c = Vector3.zero;
            for (int i = 0; i < pts.Count; i++) c += pts[i];
            return pts.Count > 0 ? c / pts.Count : c;
        }

        private static float DistToSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
        {
            p.y = 0f;
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2) : 0f;
            return Vector3.Distance(p, a + ab * t);
        }

        // ---------------- builders ----------------
        private static Transform Child(Transform root, string n)
        {
            foreach (Transform t in root) if (t.name == n) return t;
            return null;
        }

        private static void Show(GameObject go, bool on) { if (go != null && go.activeSelf != on) go.SetActive(on); }

        private static Material MakeMat(Material src, Color c)
        {
            Material m = src != null ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.color = c;
            return m;
        }

        private GameObject MakeChunk(Transform parent, Material mat, Vector3 localPos, Vector3 localScale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "OpLoad";
            var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.SetActive(false);
            return go;
        }

        private Transform MakeHeap(Transform pile, Material mat, out float baseY)
        {
            var pr = pile.GetComponentInChildren<Renderer>();
            Vector3 size = pr != null ? pr.bounds.size : new Vector3(6f, 1f, 6f);
            baseY = (pr != null ? pr.bounds.max.y : pile.position.y);

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "OpHeap";
            var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.transform.SetParent(pile, true);
            go.transform.position = new Vector3(pile.position.x, baseY, pile.position.z);
            go.transform.localScale = new Vector3(Mathf.Max(2f, size.x * 0.65f), 1f, Mathf.Max(2f, size.z * 0.65f));
            go.transform.rotation = Quaternion.identity;
            go.SetActive(false);
            return go.transform;
        }
    }
}
