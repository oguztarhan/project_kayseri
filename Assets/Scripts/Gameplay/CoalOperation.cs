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
    /// One component per ore island (Coal → Diamond): <c>islandKey</c> scopes the save keys, the tier
    /// multipliers scale prices/costs, and <c>incomeCapPerMin</c> + <c>axisLevelCap</c> cap the island so
    /// buying the next island (via <see cref="WorldIslands"/>) is the only way to keep growing.
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
        [SerializeField] private float upgradeCostGrowth = 1.13f;
        [SerializeField] private string islandRootName = "Island_Coal";
        // The authored island meshes name their primary mine per ore (mine_Copper, mine_Gold, …); every
        // other landmark is generic. Everything else in this class finds objects by exact name and
        // disables the island if one is missing, so this stays a lookup key rather than a rename.
        [SerializeField] private string mineObjectName = "mine_Coal";

        [Header("Upgrade curve")]
        // Scales every per-level gain. At 1.0 the tracks were tuned for ~10 levels and hit the island's
        // income cap by level 8; at 0.1 the same coefficients spread across a 50-level track that lands
        // on the cap at the top, so the last upgrade you buy is still the one that finishes the island.
        [SerializeField] private float axisEffectScale = 0.085f;

        [Header("Ghost-building multipliers")]
        // Softened from 2×/1.5×/2×/1.25×/1.3×: at the old sizes the five unlocks alone were worth ~9.7×,
        // which blew past the income cap on their own and made the whole upgrade track irrelevant.
        [SerializeField] private float secondSmelterBonus = 1.25f;
        [SerializeField] private float tradePostBonus = 1.20f;
        [SerializeField] private float warehouseBonus = 1.15f;
        [SerializeField] private float depotBonus = 1.10f;
        [SerializeField] private float deepShaftBonus = 1.12f;

        [Header("Island identity (world map — one component per ore island)")]
        [SerializeField] private string islandKey = "coal";        // save-key prefix + unlockedIslands id
        [SerializeField] private string displayName = "COAL ISLAND";
        [SerializeField] private string tilesRootName = "";        // "" = tiles at scene root (coal); clones use "Tiles_<Ore>"
        [SerializeField] private Color oreColor = new Color(0.10f, 0.10f, 0.12f);
        [SerializeField] private Color barColor = new Color(0.88f, 0.55f, 0.18f);

        [Header("Tier scaling & caps (archipelago progression)")]
        [SerializeField] private float valueMultiplier = 1f;       // ore tier value (GDD §5: ~×3.2 per tier)
        [SerializeField] private float costMultiplier = 1f;        // every upgrade + unlock cost on this island
        [SerializeField] private double incomeCapPerMin = 50000d;  // island $/min ceiling — the next island is the only way past it
        [SerializeField] private int axisLevelCap = 50;            // per-axis level cap on this island

        [Header("Ghost-building unlock prices")]
        [SerializeField] private float secondMineCost = 25000f;
        [SerializeField] private float secondSmelterCost = 10000f;
        [SerializeField] private float tradePostCost = 15000f;
        [SerializeField] private float thirdMineCost = 60000f;
        [SerializeField] private float warehouseCost = 20000f;
        [SerializeField] private float depotCost = 35000f;
        [SerializeField] private float exportDockCost = 40000f;
        [SerializeField] private float fourthMineCost = 150000f;
        [SerializeField] private float powerPlantCost = 80000f;
        [SerializeField] private float deepShaftCost = 45000f;
        [SerializeField] private float exportPriceBonus = 1.25f;   // dock sells bars at this multiple

        [Header("Path building")]
        [SerializeField] private float routeLaneWidth = 6f;   // gap between the out and back lanes of a truck route
        [SerializeField] private int queueSpacing = 2;           // loop points a queued truck stops short of the truck ahead

        [Header("Site dressing")]
        // The islands ship with painted road and rail, but it was authored against a layout the sim no
        // longer drives, so trucks crossed bare ground beside track that led somewhere else — the main
        // reason the maps read as unfinished. Generating both from the route endpoints means the track can
        // never disagree with the motion, on any island, whatever its mesh happens to carry.
        [SerializeField] private bool generateTrack = true;
        [SerializeField] private float roadWidth = 9f;
        [SerializeField] private float yardOffset = 12f;      // how far a yard pad sits from the building it serves
        [SerializeField] private GameObject portalPrefab;     // tunnel mouth each rail line emerges from
        [SerializeField] private GameObject ridgeRockPrefab;  // massed behind the mine to close off the map edge
        [SerializeField] private int ridgeRocks = 4;
        [SerializeField] private float ridgeSpread = 30f;
        [SerializeField] private float ridgeDistance = 17f;   // outward from the mine, before mine-clearing
        [SerializeField] private float ridgeClearance = 15f;  // keep rocks off any mine head
        [SerializeField] private float ridgeScale = 1.9f;
        [SerializeField] private float portalScale = 3.4f;
        [SerializeField] private Color roadColor = new Color(0.27f, 0.26f, 0.28f);
        [SerializeField] private Color roadLineColor = new Color(0.86f, 0.82f, 0.54f);
        [SerializeField] private Color ballastColor = new Color(0.41f, 0.37f, 0.32f);
        [SerializeField] private Color sleeperColor = new Color(0.23f, 0.17f, 0.12f);
        [SerializeField] private Color steelColor = new Color(0.60f, 0.62f, 0.66f);
        [SerializeField] private Color sitePadColor = new Color(0.33f, 0.30f, 0.26f);

        [Header("Upgrade feedback")]
        // A purchase has to land on the map, not just in the HUD: the station it belongs to grows with the
        // levels bought on it, and pops on the buy itself.
        [SerializeField] private float buildingGrowthPerLevel = 0.004f;
        [SerializeField] private float buildingGrowthCap = 0.22f;
        [SerializeField] private float punchStrength = 0.14f;
        [SerializeField] private float punchSeconds = 0.4f;

        // ---- upgrade catalog (station × axis; ids "coal#<s>#<a>" in SaveData) ----
        private const int StMine = 0, StTrain = 1, StStorage = 2, StOreTrucks = 3, StSmelter = 4, StCargoTrucks = 5, StMarket = 6, StPower = 7;
        private static readonly string[] StationList = { "MINE", "TRAIN", "STORAGE", "ORE TRUCKS", "SMELTER", "CARGO TRUCKS", "MARKET", "POWER PLANT" };
        private static readonly string[][] AxisList =
        {
            new[] { "Richness", "Load Speed" },
            new[] { "Speed", "Wagons", "Wagon Cargo" },
            new[] { "Capacity", "Transfer Speed" },
            new[] { "Trucks", "Speed", "Capacity" },
            new[] { "Smelt Speed", "Bar Storage" },
            new[] { "Trucks", "Speed", "Capacity" },
            new[] { "Price", "Sell Speed" },
            new[] { "Generators", "Turbines" },
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
            new[] { 2000d, 1500d },
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
            new[] { 0, 0 },
        };
        private readonly int[][] _lv = { new int[2], new int[3], new int[2], new int[3], new int[2], new int[3], new int[2], new int[2] };

        // ---- ghost-building unlocks (ids "coalu#<u>" in SaveData) ----
        public const int UnlockSecondMine = 0, UnlockSecondSmelter = 1, UnlockTradePost = 2, UnlockThirdMine = 3,
                         UnlockWarehouse = 4, UnlockDepot = 5, UnlockExportDock = 6, UnlockFourthMine = 7,
                         UnlockPowerPlant = 8, UnlockDeepShaft = 9;
        private static readonly string[] UnlockList =
        {
            "SECOND MINE + RAIL LINE", "SECOND SMELTER (2x smelt)", "TRADE POST (+50% price)", "THIRD MINE + RAIL LINE",
            "WAREHOUSE (2x storage)", "TRAIN DEPOT (+25% train speed)", "EXPORT DOCK (+25% export price)", "FOURTH MINE + RAIL LINE",
            "COAL POWER PLANT (new upgrades)", "DEEP SHAFT (+30% ore per trip)",
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
            new[] { "ghostx_power", "ghostx_roadW" },
            new[] { "ghostx_shaft" },
        };
        private readonly bool[] _unlocked = new bool[10];
        private Renderer[][] _unlockRends; private Material[][][] _unlockMats;   // per unlock: ghosted renderers + originals

        // ---- landmarks (found by name under the island root) ----
        private Transform _islandRoot;
        private Transform _mountain, _ghostMine, _ghostMine2, _storage, _orePile, _refinery, _ghostRefinery, _refinedPile, _market, _ghostMarket, _waitSpot;
        private Transform _dock, _mine4;
        private Transform _dressing;                 // parent for every generated road, rail and rock
        private PileStack _oreYard, _barYard;

        // ---- upgrade feedback (station → the building that grows when you buy on it) ----
        private Transform[] _stationBody;
        private Vector3[] _stationBaseScale;
        private float[] _punch;

        // ---- economy ----
        private double _storeOre, _refOre, _bars;
        private WalletService _wallet;
        private PrestigeService _prestige;
        private BoostService _boost;
        private double _incomeMult = 1d;   // prestige × active boost, refreshed once a second
        private float _deckY;              // ground height every vehicle drives at
        private SaveData _data;
        private Material _oreMat, _barMat, _ghostMat, _srcMat;

        // ---- income meter ($ earned per trailing minute) ----
        private readonly double[] _minuteBuckets = new double[60];
        private int _minIdx, _minFilled; private float _minAccum; private double _earnedThisSecond;
        private double _trailing;          // running sum of the buckets — also enforces incomeCapPerMin
        private int _rateSaveCountdown;
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
            public Transform mountain;      // the mine this line serves — sites the tunnel mouth
            public GameObject portal;       // tunnel mouth this line runs out of
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

        // ---- public surface for the HUD / world map ----
        public double StorageOre => _storeOre;
        public double Bars => _bars;
        public string IslandKey => islandKey;
        public string IslandDisplayName => displayName;
        public string PowerPlantName => OreWord + " POWER PLANT";
        public double IncomeCapPerMinute => incomeCapPerMin;
        private string OreWord => islandKey.ToUpperInvariant();
        public int StationCount => StationList.Length;
        public string StationName(int s) => StationList[s];
        public int AxisCount(int s) => AxisList[s].Length;
        public string AxisName(int s, int a) => AxisList[s][a];
        public int AxisLevel(int s, int a) => _lv[s][a];
        public bool AxisMaxed(int s, int a)
        {
            int cap = AxisMaxLv[s][a] > 0 ? Mathf.Min(AxisMaxLv[s][a], axisLevelCap) : axisLevelCap;
            return _lv[s][a] >= cap;
        }
        /// <summary>The POWER PLANT station only upgrades once its ghost building is bought.</summary>
        public bool AxisLocked(int s, int a) => s == StPower && !_unlocked[UnlockPowerPlant];

        /// <summary>
        /// World point a floating station badge should hover over — the top of the station's silhouette.
        /// Stations with no single building (train, cargo trucks) anchor to the midpoint of the leg they run.
        /// Returns false while the layout hasn't resolved yet.
        /// </summary>
        public bool StationAnchor(int s, out Vector3 world)
        {
            world = Vector3.zero;
            Transform t = null;
            switch (s)
            {
                case StMine: t = _mountain; break;
                case StStorage: t = _storage; break;
                case StOreTrucks: t = _waitSpot; break;
                case StSmelter: t = _refinery; break;
                case StMarket: t = _market; break;
                case StPower: t = _islandRoot != null ? Child(_islandRoot, "ghostx_power") : null; break;
                case StTrain:
                    if (_mountain == null || _storage == null) return false;
                    world = (TopOf(_mountain) + TopOf(_storage)) * 0.5f;
                    return true;
                case StCargoTrucks:
                    if (_refinery == null || _market == null) return false;
                    world = (TopOf(_refinery) + TopOf(_market)) * 0.5f;
                    return true;
            }
            if (t == null) return false;
            world = TopOf(t);
            return true;
        }

        private static Vector3 TopOf(Transform t)
        {
            Bounds b = WorldBounds(t);
            return new Vector3(b.center.x, b.max.y, b.center.z);
        }

        /// <summary>Union of every renderer under <paramref name="t"/>, or a zero box at its pivot.</summary>
        private static Bounds WorldBounds(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(t.position, Vector3.zero);
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        public BigDouble AxisCost(int s, int a) => new BigDouble(AxisBaseCost[s][a] * costMultiplier * System.Math.Pow(upgradeCostGrowth, _lv[s][a]));
        public int UnlockCount => UnlockList.Length;
        public string UnlockName(int u) => UnlockList[u].Replace("COAL", OreWord);
        public bool IsUnlocked(int u) => _unlocked[u];
        /// <summary>Everything bought: every axis at its cap and every ghost building built.</summary>
        public bool FullyMaxed
        {
            get
            {
                for (int s = 0; s < StationList.Length; s++)
                    for (int a = 0; a < AxisList[s].Length; a++)
                        if (!AxisMaxed(s, a)) return false;
                for (int u = 0; u < _unlocked.Length; u++) if (!_unlocked[u]) return false;
                return true;
            }
        }
        public BigDouble UnlockCost(int u) =>
            new BigDouble(costMultiplier * (u == UnlockSecondMine ? secondMineCost
                : u == UnlockSecondSmelter ? secondSmelterCost
                : u == UnlockTradePost ? tradePostCost
                : u == UnlockThirdMine ? thirdMineCost
                : u == UnlockWarehouse ? warehouseCost
                : u == UnlockDepot ? depotCost
                : u == UnlockExportDock ? exportDockCost
                : u == UnlockFourthMine ? fourthMineCost
                : u == UnlockPowerPlant ? powerPlantCost : deepShaftCost));

        /// <summary>Buy one level on a station axis: spends cash, applies the effect live, persists.</summary>
        public bool TryUpgrade(int s, int a)
        {
            if (s < 0 || s >= StationList.Length || a < 0 || a >= AxisList[s].Length || _wallet == null) return false;
            if (AxisMaxed(s, a) || AxisLocked(s, a)) return false;
            if (!_wallet.TrySpendCash(AxisCost(s, a))) return false;
            _lv[s][a]++;
            SaveLevel(islandKey + "#" + s + "#" + a, _lv[s][a]);
            if (_punch != null) _punch[s] = punchSeconds;   // the station pops, then settles at its new size
            if ((s == StOreTrucks || s == StCargoTrucks) && a == 0) ApplyFleetStates();
            return true;
        }

        /// <summary>Buy a one-time ghost-building unlock: turns the ghost solid and applies its bonus.</summary>
        public bool TryUnlock(int u)
        {
            if (u < 0 || u >= _unlocked.Length || _unlocked[u] || _wallet == null) return false;
            if (!_wallet.TrySpendCash(UnlockCost(u))) return false;
            _unlocked[u] = true;
            SaveLevel(islandKey + "u#" + u, 1);
            ApplyUnlock(u);
            return true;
        }

        // ---- effective rates (base × axis levels × unlock bonuses) ----
        // Per-level gains are scaled by axisEffectScale so the same coefficients spread across a long
        // upgrade track instead of a 10-level one. Measured: output ≈ base·(1 + 0.335·L)^2.89 at scale 1,
        // which slammed into the island's income cap around level 8 and wasted everything above it.
        private float PowerIncome => 1f + 0.05f * axisEffectScale * _lv[StPower][0];   // Generators: global income
        private float PowerSpeed => 1f + 0.03f * axisEffectScale * _lv[StPower][1];    // Turbines: every vehicle
        private float MineDwell => dwellSeconds / (1f + 0.2f * axisEffectScale * _lv[StMine][1]);
        private float EffTrainOre => trainOrePerTrip * (1f + 0.25f * axisEffectScale * _lv[StMine][0]) * (ActiveWagons / (float)BaseWagons) * (1f + 0.25f * axisEffectScale * _lv[StTrain][2]) * (_unlocked[UnlockDeepShaft] ? deepShaftBonus : 1f);
        private float EffTrainSpeed => trainSpeed * (1f + 0.15f * axisEffectScale * _lv[StTrain][0]) * (_unlocked[UnlockDepot] ? depotBonus : 1f) * PowerSpeed;
        private int ActiveWagons => Mathf.Min(BaseWagons + _lv[StTrain][1], MaxWagons);
        private float EffStorageFull => storageCapacity * (1f + 0.5f * axisEffectScale * _lv[StStorage][0]) * (_unlocked[UnlockWarehouse] ? warehouseBonus : 1f);
        private float StorageDwell => dwellSeconds / (1f + 0.2f * axisEffectScale * _lv[StStorage][1]);
        private int OreTruckCount => OreBaseTrucks + _lv[StOreTrucks][0];
        private float EffOreSpeed => truckSpeed * (1f + 0.15f * axisEffectScale * _lv[StOreTrucks][1]) * PowerSpeed;
        private float EffOreCap => oreTruckCapacity * (1f + 0.30f * axisEffectScale * _lv[StOreTrucks][2]);
        private float EffSmelt => smeltPerSecond * (1f + 0.30f * axisEffectScale * _lv[StSmelter][0]) * (_unlocked[UnlockSecondSmelter] ? secondSmelterBonus : 1f);
        private float EffBarCap => barCapacity * (1f + 0.5f * axisEffectScale * _lv[StSmelter][1]);
        private int CargoTruckCount => CargoBaseTrucks + _lv[StCargoTrucks][0];
        private float EffCargoSpeed => truckSpeed * (1f + 0.15f * axisEffectScale * _lv[StCargoTrucks][1]) * PowerSpeed;
        private float EffCargoCap => cargoTruckCapacity * (1f + 0.30f * axisEffectScale * _lv[StCargoTrucks][2]);
        private float EffBarPrice => barPrice * valueMultiplier * (1f + 0.40f * axisEffectScale * _lv[StMarket][0]) * (_unlocked[UnlockTradePost] ? tradePostBonus : 1f) * PowerIncome;
        private float MarketDwell => dwellSeconds / (1f + 0.2f * axisEffectScale * _lv[StMarket][1]);

        private void Start()
        {
            _wallet = ServiceLocator.Get<WalletService>();
            _prestige = ServiceLocator.Get<PrestigeService>();
            _boost = ServiceLocator.Get<BoostService>();
            _data = ServiceLocator.Get<SaveData>();
            LoadLevels();
            GameObject root = null;   // scene-root scan (not Find) so an island activated this very frame still resolves
            var sceneRoots = gameObject.scene.GetRootGameObjects();
            for (int i = 0; i < sceneRoots.Length; i++) if (sceneRoots[i].name == islandRootName) { root = sceneRoots[i]; break; }
            if (root == null) { Debug.LogWarning("CoalOperation: '" + islandRootName + "' not found — disabled."); enabled = false; return; }
            _islandRoot = root.transform;

            _mountain = Child(_islandRoot, mineObjectName);
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
            if (_storage != null) _deckY = _storage.position.y;

            if (_mountain == null || _storage == null || _orePile == null ||
                _refinery == null || _refinedPile == null || _market == null)
            { Debug.LogWarning("CoalOperation: missing a core landmark — disabled."); enabled = false; return; }

            Transform engine = Child(_islandRoot, "train");
            if (engine == null) { Debug.LogWarning("CoalOperation: train not found — disabled."); enabled = false; return; }

            // ore/bar/ghost materials (ghost cloned from the map's own ghost buildings so the look matches)
            Renderer refRend = engine.GetComponentInChildren<Renderer>();
            _srcMat = refRend != null ? refRend.sharedMaterial : null;
            _oreMat = MakeMat(_srcMat, oreColor);   // raw ore chunks/heaps, tinted per island tier
            _barMat = MakeMat(_srcMat, barColor);   // refined product
            var ghostRend = _ghostMarket != null ? _ghostMarket.GetComponentInChildren<Renderer>() : null;
            _ghostMat = ghostRend != null ? ghostRend.sharedMaterial : MakeMat(null, new Color(1f, 1f, 1f, 0.35f));

            RelocateYards();   // before anything measures a yard: the roads and the heaps both key off it
            // A level-0 yard reads as ten chunks; a fully upgraded one needs the widest grid to hold what
            // it can now store, which is the whole point of buying Capacity.
            _oreYard = new PileStack(_orePile, _oreMat, storageCapacity / 10f, "OpOreHeap");
            _barYard = new PileStack(_refinedPile, _barMat, barCapacity / 10f, "OpBarHeap");

            _train1 = BuildTrain(engine, _mountain);
            _train1.active = true;
            // "ghost_mine (1)" sits at the head of the second (already-laid) rail line; "ghost_mine" at the
            // head of the GH ghost-rail line — each becomes a live train when its unlock is bought
            if (_ghostMine2 != null) _train2 = BuildTrain(CloneTrainRig(engine, "train2"), _ghostMine2);
            if (_ghostMine != null) _train3 = BuildTrain(CloneTrainRig(engine, "train3"), _ghostMine);
            if (_mine4 != null) _train4 = BuildTrain(CloneTrainRig(engine, "train4"), _mine4);

            BuildTruckAgents();
            BuildUnlockRegistry();
            BuildSiteDressing();     // needs the rail paths the trains just resolved
            CacheStationBodies();
            ApplyFleetStates();
            for (int u = 0; u < _unlocked.Length; u++) if (_unlocked[u]) ApplyUnlock(u);
            ApplyStationScale();     // show the levels already bought, without the purchase pop

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
            TickPunch(dt);
            TickIncome(dt);
        }

        // ---------------- trains ----------------

        /// <summary>Wires a train agent from an engine + the shared wagon pool convention, and its rail path.</summary>
        private TrainAgent BuildTrain(Transform engine, Transform mountain)
        {
            var a = new TrainAgent { engine = engine, engineY = engine.position.y, mountain = mountain };

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

        private GameObject[] _tileScan;

        /// <summary>The objects this island's rail/road tiles live among: the scene roots for the coal
        /// original, or the children of "Tiles_&lt;Ore&gt;" for a cloned island. Cached — Start-time only.</summary>
        private GameObject[] TileScanObjects()
        {
            if (_tileScan != null) return _tileScan;
            var roots = gameObject.scene.GetRootGameObjects();
            if (string.IsNullOrEmpty(tilesRootName)) { _tileScan = roots; return _tileScan; }
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name != tilesRootName) continue;
                Transform tr = roots[i].transform;
                var arr = new GameObject[tr.childCount];
                for (int c = 0; c < tr.childCount; c++) arr[c] = tr.GetChild(c).gameObject;
                _tileScan = arr;
                return _tileScan;
            }
            Debug.LogWarning("CoalOperation(" + islandKey + "): tiles root '" + tilesRootName + "' not found.");
            _tileScan = new GameObject[0];
            return _tileScan;
        }

        /// <summary>
        /// Straight run from a mountain down to storage, sampled into waypoints.
        ///
        /// Previously this scanned for SM_Rail_* tiles near the line and gated them on an ~11° alignment
        /// test, because three rail runs converge on storage and shared a corridor. That made the track a
        /// prerequisite for the sim working at all — a missing or slightly rotated tile silently shortened
        /// a train's route. The islands carry their own painted rail as scenery; the train just drives.
        /// </summary>
        private Vector3[] BuildRailPath(Transform mountain, Transform storage)
        {
            Vector3 a = mountain.position, b = storage.position;
            float len = Flat(b - a).magnitude;
            int n = Mathf.Clamp(Mathf.RoundToInt(len / 6f), 1, 16);
            var path = new Vector3[n + 1];
            for (int i = 0; i <= n; i++) path[i] = Vector3.Lerp(a, b, i / (float)n);
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
            var truckClaimed = new bool[sceneTrucks.Count];

            var agents = new List<TruckAgent>();
            for (int li = 0; li < loops.Count; li++)
            {
                List<Vector3> loop = loops[li];
                if (loop.Count < 2) continue;
                Route route = (Route)li;   // BuildRoadLoops emits exactly one loop per route, in Route order
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
                    if (!truckClaimed[ti] && NearestLoop(loops, sceneTrucks[ti].position) == li)
                    { truckClaimed[ti] = true; fleet.Add(sceneTrucks[ti]); }
                // Routes are synthesised now, so every authored truck can end up nearest the same one.
                // A route with no truck would silently never run, so clone one — never share a Transform
                // between two routes, or a single truck ends up driven by two agents at once.
                if (fleet.Count == 0)
                {
                    if (sceneTrucks.Count == 0) continue;
                    Transform seed = Instantiate(sceneTrucks[0].gameObject, _islandRoot).transform;
                    seed.name = sceneTrucks[0].name + "_route" + li;
                    StripOpChildren(seed);
                    fleet.Add(seed);
                }
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

        /// <summary>
        /// One driving loop per route, synthesised straight from the buildings it connects: out along one
        /// side, back along the other, so trucks visibly circulate instead of stacking on a single line.
        ///
        /// This replaced a flood-fill over scattered SM_Road_* tiles. That approach made the islands read
        /// as a confusing tangle of track, and it was fragile — a ring whose two sides sat further apart
        /// than the tile link distance silently split into two clusters and starved a whole route.
        /// The authored islands already carry their own painted roads as scenery; trucks just drive.
        ///
        /// Index order is the <see cref="Route"/> order, so no loop→route matching is needed.
        /// </summary>
        private List<List<Vector3>> BuildRoadLoops()
        {
            var loops = new List<List<Vector3>>();
            loops.Add(RouteLoop(_orePile, _refinery));                       // Route.Ore
            loops.Add(RouteLoop(_refinedPile, _market));                     // Route.Market
            if (_dock != null) loops.Add(RouteLoop(_refinedPile, _dock));    // Route.Export
            return loops;
        }

        private List<Vector3> RouteLoop(Transform from, Transform to)
        {
            var path = new List<Vector3>();
            if (from == null || to == null) return path;
            Vector3 a = from.position, b = to.position;
            a.y = b.y = _deckY;
            Vector3 dir = b - a; dir.y = 0f;
            float len = dir.magnitude;
            if (len < 0.01f) return path;
            dir /= len;
            Vector3 side = new Vector3(-dir.z, 0f, dir.x) * (routeLaneWidth * 0.5f);
            int n = Mathf.Clamp(Mathf.RoundToInt(len / 4f), 2, 24);
            for (int i = 0; i <= n; i++) path.Add(Vector3.Lerp(a, b, i / (float)n) + side);   // outbound lane
            for (int i = n; i >= 0; i--) path.Add(Vector3.Lerp(a, b, i / (float)n) - side);   // return lane
            return path;
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
                        // Prestige investors and rewarded-ad boosts multiply the sale *before* the cap, so
                        // they speed up the climb without letting an island out-earn its own ceiling.
                        double sale = a.carry * EffBarPrice * (a.route == Route.Export ? exportPriceBonus : 1f) * _incomeMult;
                        // island income ceiling: this island can never out-earn its cap — the next island is the growth path
                        double headroom = incomeCapPerMin - (_trailing + _earnedThisSecond);
                        if (sale > headroom) sale = headroom > 0d ? headroom : 0d;
                        if (sale > 0d)
                        {
                            _wallet.AddCash(new BigDouble(sale));
                            _earnedThisSecond += sale;
                        }
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
            var roots = TileScanObjects();
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
                    ActivateTrain(_train2);
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
                    ActivateTrain(_train3);
                    break;
                case UnlockExportDock:
                    ApplyFleetStates();   // wakes the export loop's trucks
                    break;
                case UnlockFourthMine:
                    ActivateTrain(_train4);
                    break;
            }
        }

        /// <summary>Wakes an expansion line: its train starts running and its tunnel mouth appears.</summary>
        private void ActivateTrain(TrainAgent a)
        {
            if (a == null || a.active) return;
            a.active = true;
            a.state = TR.LoadMountain;
            a.timer = dwellSeconds;
            Show(a.portal, true);
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
            var roots = TileScanObjects();
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
            // once a second is often enough for a boost timer, and keeps service lookups out of the sale path
            _incomeMult = (_prestige != null ? _prestige.IncomeMultiplier : 1d)
                        * (_boost != null ? _boost.ActiveMultiplier : 1d);
            _trailing += _earnedThisSecond - _minuteBuckets[_minIdx];
            _minuteBuckets[_minIdx] = _earnedThisSecond;
            _earnedThisSecond = 0d;
            _minIdx = (_minIdx + 1) % _minuteBuckets.Length;
            if (_minFilled < _minuteBuckets.Length) _minFilled++;
            // clamp the extrapolated warm-up value: earning can never exceed the cap per rolling minute
            CashPerMinute = System.Math.Min(_trailing * (60.0 / _minFilled), incomeCapPerMin);
            // persist the measured rate so this island keeps earning while another one is active (and
            // offline) — only once the window is half-full, so a warm-up spike can't inflate it
            if (--_rateSaveCountdown <= 0 && _minFilled >= RateSaveMinSeconds)
            {
                _rateSaveCountdown = 5;
                PersistRate();
            }
        }

        private const int RateSaveMinSeconds = 15;   // enough to be past the warm-up, short enough for a quick visit

        private void PersistRate()
        {
            SaveLevel("rate#" + islandKey, (int)System.Math.Min(CashPerMinute, incomeCapPerMin));
        }

        // Travelling away freezes this island (visuals off, component disabled); the meter must restart
        // from zero on return or the queued-up truck dumps read as a fake income spike.
        // Persist first: without this, leaving before the periodic save fires left the island earning
        // nothing in the background, which quietly broke the whole passive-empire premise.
        private void OnDisable()
        {
            if (_minFilled >= RateSaveMinSeconds && CashPerMinute > 0d) PersistRate();
            for (int i = 0; i < _minuteBuckets.Length; i++) _minuteBuckets[i] = 0d;
            _minIdx = 0; _minFilled = 0; _minAccum = 0f;
            _trailing = 0d; _earnedThisSecond = 0d;
            CashPerMinute = 0d;
        }

        // ---------------- pile visuals ----------------
        private void UpdateHeaps()
        {
            _oreYard.Set(_storeOre, EffStorageFull);
            _barYard.Set(_bars, EffBarCap);
        }

        /// <summary>
        /// Pulls each yard pad in beside the building it serves. The authored pads sit ~24 units off the
        /// working chain, so the trucks drove out to bare ground and back while the buildings they belong
        /// to sat somewhere else entirely. The side is taken from wherever the artist put the pad, so each
        /// island keeps its own composition.
        /// </summary>
        private void RelocateYards()
        {
            MoveYard(_orePile, _storage);
            MoveYard(_refinedPile, _refinery);
        }

        private void MoveYard(Transform pad, Transform building)
        {
            Vector3 away = Flat(pad.position - building.position);
            if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
            Vector3 p = building.position + away.normalized * yardOffset;
            p.y = pad.position.y;
            pad.position = p;
        }

        // ---- persistence ----
        private void LoadLevels()
        {
            if (_data == null || _data.islandLevels == null) return;
            for (int s = 0; s < StationList.Length; s++)
                for (int a = 0; a < AxisList[s].Length; a++)
                {
                    StationLevel e = FindLevel(islandKey + "#" + s + "#" + a);
                    if (e != null) _lv[s][a] = e.level;
                }
            for (int u = 0; u < _unlocked.Length; u++)
            {
                StationLevel e = FindLevel(islandKey + "u#" + u);
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

        // ---------------- site dressing ----------------

        /// <summary>
        /// Lays the road network, the rail lines and the building aprons. Everything hangs off a
        /// "Dressing_*" child so <see cref="Game.UI.OperationCameraBoot"/> leaves it out of the framing
        /// bounds — the ridge sits well outside the working area and would otherwise pull the camera back.
        /// </summary>
        private void BuildSiteDressing()
        {
            var go = new GameObject("Dressing_Site");
            go.transform.SetParent(_islandRoot, true);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            _dressing = go.transform;

            if (!generateTrack) return;
            HideAuthoredTrack();

            Material road = MakeMat(_srcMat, roadColor), line = MakeMat(_srcMat, roadLineColor);
            Material ballast = MakeMat(_srcMat, ballastColor), sleeper = MakeMat(_srcMat, sleeperColor);
            Material steel = MakeMat(_srcMat, steelColor), apron = MakeMat(_srcMat, sitePadColor);
            float roadY = _deckY + 0.06f;

            // One continuous ribbon through the chain: storage → its yard → refinery → its yard → market.
            // The yard legs are what tie a pile to the building it belongs to; without them the piles read
            // as unrelated props sitting on the grass.
            RouteMesh.Road(_dressing, "OpRoad_OreYard", _storage.position, _orePile.position, roadWidth, roadY, road, line);
            RouteMesh.Road(_dressing, "OpRoad_Ore", _orePile.position, _refinery.position, roadWidth, roadY, road, line);
            RouteMesh.Road(_dressing, "OpRoad_BarYard", _refinery.position, _refinedPile.position, roadWidth, roadY, road, line);
            RouteMesh.Road(_dressing, "OpRoad_Market", _refinedPile.position, _market.position, roadWidth, roadY, road, line);
            if (_dock != null)
                RouteMesh.Road(_dressing, "OpRoad_Export", _refinedPile.position, _dock.position, roadWidth, roadY, road, line);

            LayRail(_train1, "1", ballast, sleeper, steel);
            LayRail(_train2, "2", ballast, sleeper, steel);
            LayRail(_train3, "3", ballast, sleeper, steel);
            LayRail(_train4, "4", ballast, sleeper, steel);

            SitePad(_mountain, apron); SitePad(_storage, apron);
            SitePad(_refinery, apron); SitePad(_market, apron);

            // The authored yard slabs are near-white, which reads as blank paper whenever a yard happens
            // to be empty. Gravel-toned, an empty yard reads as a yard.
            Retint(_orePile, apron); Retint(_refinedPile, apron);

            BuildRidge();
        }

        /// <summary>Track plus the tunnel mouth the line runs out of, hidden until its train is bought.</summary>
        private void LayRail(TrainAgent a, string id, Material ballast, Material sleeper, Material steel)
        {
            if (a == null || a.path == null || a.path.Length < 2) return;
            Vector3 head = a.path[0], tail = a.path[a.path.Length - 1];
            RouteMesh.Rail(_dressing, "OpRail_" + id, head, tail, _deckY, a.engineY, ballast, sleeper, steel);
            if (portalPrefab == null || a.mountain == null) return;

            Vector3 dir = Flat(tail - head).normalized;
            // The mouth belongs on the mine's downhill face, not on its pivot. The engine still spawns at
            // the pivot, deep inside the building where the mesh hides it, so the first thing the player
            // sees of a departing train is it coming out of the tunnel.
            Bounds mb = WorldBounds(a.mountain);
            float face = Mathf.Abs(dir.x) * mb.extents.x + Mathf.Abs(dir.z) * mb.extents.z;
            Vector3 mouth = mb.center + dir * face;

            var p = Instantiate(portalPrefab, _dressing);
            p.name = "OpPortal_" + id;
            p.transform.SetPositionAndRotation(new Vector3(mouth.x, _deckY, mouth.z),
                                               Quaternion.LookRotation(dir, Vector3.up));
            p.transform.localScale = Vector3.one * portalScale;
            a.portal = p;
            p.SetActive(a.active);
        }

        /// <summary>Repaints a landmark's own mesh, leaving the generated children (the heap) alone.</summary>
        private static void Retint(Transform t, Material mat)
        {
            var rs = t.GetComponents<Renderer>();
            for (int i = 0; i < rs.Length; i++)
            {
                var arr = new Material[rs[i].sharedMaterials.Length];
                for (int m = 0; m < arr.Length; m++) arr[m] = mat;
                rs[i].sharedMaterials = arr;
            }
        }

        private void SitePad(Transform building, Material mat)
        {
            if (building == null) return;
            Bounds b = WorldBounds(building);
            RouteMesh.Pad(_dressing, "OpPad_" + building.name,
                          b.center, Mathf.Max(b.extents.x, b.extents.z) * 1.15f, _deckY + 0.03f, mat);
        }

        /// <summary>
        /// Masses rock behind the mine so the map ends in a mountain wall instead of trailing off into
        /// empty terrain, and gives the rail line somewhere to come from.
        /// </summary>
        private void BuildRidge()
        {
            if (ridgeRockPrefab == null || ridgeRocks <= 0) return;
            Vector3 outward = Flat(_mountain.position - _market.position).normalized;
            Vector3 side = new Vector3(-outward.z, 0f, outward.x);
            Transform[] mines = { _mountain, _ghostMine, _ghostMine2, _mine4 };

            for (int i = 0; i < ridgeRocks; i++)
            {
                float t = ridgeRocks == 1 ? 0f : i / (float)(ridgeRocks - 1) - 0.5f;
                Vector3 pos = _mountain.position + outward * (ridgeDistance + Mathf.Abs(t) * 7f)
                                                + side * (t * ridgeSpread);
                // Walk a rock back until it clears every mine head. Expansion mines sit outboard of the
                // primary one, and a rock dropped on top of one would bury the building the player is
                // about to unlock. The uneven distances this produces also stop the ridge reading as a wall.
                for (int guard = 0; guard < 10 && NearAny(mines, pos, ridgeClearance); guard++)
                    pos += outward * 5f;

                var r = Instantiate(ridgeRockPrefab, _dressing);
                r.name = "Dressing_Rock" + i;
                r.transform.position = new Vector3(pos.x, _deckY - 2.5f, pos.z);
                r.transform.rotation = Quaternion.Euler(0f, i * 137f, 0f);
                r.transform.localScale = Vector3.one * (ridgeScale - Mathf.Abs(t) * 0.4f);
            }
        }

        private static bool NearAny(Transform[] ts, Vector3 p, float radius)
        {
            float r2 = radius * radius;
            for (int i = 0; i < ts.Length; i++)
                if (ts[i] != null && SqrXZ(ts[i].position, p) < r2) return true;
            return false;
        }

        /// <summary>Turns off the painted decoration the generated track replaces, so they can't double up.</summary>
        private void HideAuthoredTrack()
        {
            foreach (Transform t in _islandRoot)
            {
                string n = t.name;
                // "edge" is the kerb strip that runs alongside a painted road; on its own it reads as a
                // stray line drawn straight through the buildings.
                if (n.StartsWith("road") || n.StartsWith("rail") || n.StartsWith("tie") || n.StartsWith("edge"))
                    t.gameObject.SetActive(false);
            }
        }

        // ---------------- upgrade feedback ----------------

        private void CacheStationBodies()
        {
            _stationBody = new Transform[StationList.Length];
            _stationBody[StMine] = _mountain;
            _stationBody[StStorage] = _storage;
            _stationBody[StSmelter] = _refinery;
            _stationBody[StMarket] = _market;
            _stationBody[StPower] = Child(_islandRoot, "ghostx_power");
            // TRAIN, ORE TRUCKS and CARGO TRUCKS have no single building — their levels already show as
            // more wagons and more trucks on the road.
            _stationBaseScale = new Vector3[StationList.Length];
            _punch = new float[StationList.Length];
            for (int s = 0; s < _stationBody.Length; s++)
                if (_stationBody[s] != null) _stationBaseScale[s] = _stationBody[s].localScale;
        }

        private int StationLevelSum(int s)
        {
            int n = 0;
            for (int a = 0; a < _lv[s].Length; a++) n += _lv[s][a];
            return n;
        }

        private float StationScale(int s) =>
            1f + Mathf.Min(buildingGrowthCap, buildingGrowthPerLevel * StationLevelSum(s));

        private void ApplyStationScale()
        {
            for (int s = 0; s < _stationBody.Length; s++)
                if (_stationBody[s] != null) _stationBody[s].localScale = _stationBaseScale[s] * StationScale(s);
        }

        // Only the stations mid-pop write a transform; the rest were sized when their level last changed.
        private void TickPunch(float dt)
        {
            for (int s = 0; s < _punch.Length; s++)
            {
                if (_punch[s] <= 0f) continue;
                _punch[s] = Mathf.Max(0f, _punch[s] - dt);
                Transform t = _stationBody[s];
                if (t == null) continue;
                float k = _punch[s] / punchSeconds;
                t.localScale = _stationBaseScale[s] * (StationScale(s) + punchStrength * k * Mathf.Sin(k * Mathf.PI * 3f));
            }
        }
    }
}
