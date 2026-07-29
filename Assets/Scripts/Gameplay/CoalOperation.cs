using System.Collections.Generic;
using Game.Core;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The whole game loop for one ore island. Eight copies of this component live on <c>CoalController</c>
    /// — one per island, Coal through Diamond — and only the island you are standing on is enabled.
    ///
    /// <para><b>The production chain.</b> Ore moves along a fixed path, and every stage is a station the
    /// player can upgrade:</para>
    /// <code>
    ///   MINE ──train──▶ STORAGE ──ore truck──▶ SMELTER ──cargo truck──▶ MARKET ──▶ $
    ///                   (ore yard)             (bars)    (bar yard)
    /// </code>
    /// <list type="bullet">
    /// <item><b>Trains</b> shuttle mine → storage. The engine spawns inside the mine building (hidden by the
    /// mesh), drives out through the tunnel portal with loaded wagons, disappears into the storage shed to
    /// dump, and returns empty. If the ore yard is full it waits inside the shed — a visible bottleneck.</item>
    /// <item><b>Trucks</b> drive a two-lane oval: out on one side, back on the other. Ore trucks carry
    /// storage → smelter, cargo trucks carry smelter → market (or → export dock, once unlocked). A truck
    /// with nothing to haul parks at its wait spot. Trucks you have not bought yet sit greyed-out in the
    /// depot bay so you can see what the next <b>Trucks</b> upgrade will give you.</item>
    /// </list>
    ///
    /// <para><b>How to read this file.</b> It is long, but it is in fixed order:</para>
    /// <list type="number">
    /// <item><b>Inspector fields</b> — every tunable number, grouped by <c>[Header]</c>.</item>
    /// <item><b>Upgrade catalog</b> — the static tables defining stations, axes and prices.</item>
    /// <item><b>Public surface</b> — what the HUD and the badges call (costs, levels, <c>TryUpgrade</c>).</item>
    /// <item><b>Effective rates</b> — the <c>Eff*</c> properties: base value × upgrades × unlocks.</item>
    /// <item><b>Start / Tick</b> — boot order, then the per-frame update order.</item>
    /// <item><b>Trains, then trucks</b> — the two vehicle state machines.</item>
    /// <item><b>Unlocks, income, piles, dressing, upgrade feedback</b> — the rest, each behind a banner.</item>
    /// </list>
    ///
    /// <para><b>Two things worth knowing before you edit.</b> First, landmarks are found <b>by exact object
    /// name</b> under the island root (<c>"storage"</c>, <c>"refinery"</c>, <c>"market"</c>…). Rename one in
    /// the scene and this component logs a warning and disables itself. Second, the layout is <i>mostly</i>
    /// static, with one deliberate exception: <see cref="RelocateYards"/> moves the two pile pads next to
    /// the buildings they serve at startup, because the authored positions sat far off the working chain.</para>
    ///
    /// <para><b>Roads and rails are generated, not authored.</b> Vehicles used to follow scattered
    /// <c>SM_Road_*</c> / <c>SM_Rail_*</c> tiles; that was fragile and the track no longer matched where the
    /// sim drove. Now routes are synthesised from the buildings themselves and the visible track is built to
    /// match (see <see cref="BuildSiteDressing"/>), so the two can never disagree.</para>
    ///
    /// <para>Cash goes to <see cref="WalletService"/>; levels persist in <see cref="SaveData"/> under keys
    /// prefixed by <c>islandKey</c>. <c>incomeCapPerMin</c> and <c>axisLevelCap</c> deliberately cap each
    /// island, so buying the <i>next</i> island (via <see cref="WorldIslands"/>) is the only way to grow.</para>
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

        [Header("Layout spread")]
        // The authored islands were composed for the starting chain alone. Everything added since —
        // yards pulled in beside their buildings, six expansion buildings, a rock ridge, tunnel portals,
        // generated roads and rails — competes for the same strip of land, and the result reads as one
        // pile of overlapping geometry. So the site is spread apart at startup and the ground grown to
        // match, which fixes every island at once instead of hand-editing eight compositions.
        [SerializeField] private float siteSpread = 1.35f;       // push landmarks out from the site centre
        [SerializeField] private float groundScale = 1.7f;       // grow isle/lagoon so the spread stays on land
        [SerializeField] private float railSeparation = 11f;     // side-by-side gap where rail lines reach storage

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

        [Header("Expansion buildings")]
        // The authored island meshes only carry the starting chain. Without these, six of the ten one-time
        // unlocks were pure text: you paid for a WAREHOUSE and nothing appeared. Spawned at Start named
        // "ghostx_*" so BuildUnlockRegistry finds them by prefix and ghosts them until they are bought —
        // the player can see the whole future of the island laid out from day one.
        [SerializeField] private GameObject warehousePrefab;
        [SerializeField] private GameObject depotPrefab;
        [SerializeField] private GameObject dockPrefab;
        [SerializeField] private GameObject powerPrefab;
        [SerializeField] private GameObject shaftPrefab;
        [SerializeField] private float expansionScale = 2.6f;
        [SerializeField] private float expansionClearance = 13f;   // keep new buildings off the existing ones

        [Header("Yard contents")]
        // What a single unit of stock looks like on the yard. Leave empty and the piles fall back to plain
        // boxes. Setting them per island is also cheap differentiation: coal yards stack coke, ruby yards
        // stack cut gems, diamond yards stack diamonds.
        [SerializeField] private GameObject oreChunkPrefab;
        [SerializeField] private GameObject barChunkPrefab;

        [Header("Site life (workers + smelter smoke)")]
        // Purely cosmetic, but both scale with progress: the crew grows as you buy levels and the smoke
        // thickens as the smelter speeds up, so the island keeps showing what your money bought.
        [SerializeField] private GameObject workerPrefab;
        [SerializeField] private GameObject smokePuffPrefab;
        [SerializeField] private float workerScale = 2.2f;
        [SerializeField] private int maxWorkers = 8;
        [SerializeField] private int workerLevelsPer = 24;    // one extra worker per this many axis levels
        [SerializeField] private int maxSmokePuffs = 10;
        [SerializeField] private float smokePuffLife = 3.2f;
        [SerializeField] private float smokePuffRise = 3.4f;
        [SerializeField] private float smokePuffSpread = 1.3f;
        [SerializeField] private Color smokeColor = new Color(0.86f, 0.86f, 0.88f, 1f);
        [SerializeField] private float smeltGlowSeconds = 1.5f;   // how long one conversion keeps the stack smoking

        [Header("Upgrade feedback")]
        // A purchase has to land on the map, not just in the HUD: the station it belongs to grows with the
        // levels bought on it, and pops on the buy itself.
        [SerializeField] private float buildingGrowthPerLevel = 0.004f;
        [SerializeField] private float buildingGrowthCap = 0.22f;
        [SerializeField] private float punchStrength = 0.14f;
        [SerializeField] private float punchSeconds = 0.4f;

        // ═══════════════════════════════════════════════════════════════════════════════════════════
        //  UPGRADE CATALOG
        //
        //  Every upgrade in the game is addressed by two numbers: a STATION (which building) and an
        //  AXIS (which stat on that building). "Mine → Richness" is station 0, axis 0.
        //
        //  The five tables below are PARALLEL ARRAYS — index [s][a] means the same upgrade in all of
        //  them. Keep them in step: adding an axis means adding an entry to AxisList, AxisBaseCost,
        //  AxisMaxLv and to the matching row length in _lv, or you get an IndexOutOfRange at runtime.
        //
        //      StMine = 0 ─┐
        //                  ├─ AxisList[0]     = { "Richness", "Load Speed" }   ← names shown in the UI
        //                  ├─ AxisBaseCost[0] = { 60, 80 }                     ← price of level 1
        //                  ├─ AxisMaxLv[0]    = { 0, 0 }                       ← 0 means "no special cap"
        //                  └─ _lv[0]          = new int[2]                     ← levels the player owns
        //
        //  Saved as "<islandKey>#<station>#<axis>" (e.g. "coal#0#0") in SaveData.islandLevels.
        // ═══════════════════════════════════════════════════════════════════════════════════════════

        // Station indices. These are array positions, so never reorder them — saved games address
        // upgrades by number, and renumbering would silently move the player's levels onto other stations.
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
        // Per-axis hard caps. 0 means "no special cap" — that axis then stops at the island-wide
        // axisLevelCap instead. The non-zero entries are the axes limited by physical scene objects:
        // there are only so many wagon slots on a train (3) and so many parked truck bodies to wake (2).
        private static readonly int[][] AxisMaxLv =
        {
            new[] { 0, 0 },
            new[] { 0, 3, 0 },      // TRAIN → Wagons caps at 3 (BaseWagons 3 + 3 = MaxWagons 6)
            new[] { 0, 0 },
            new[] { 2, 0, 0 },      // ORE TRUCKS → Trucks caps at 2 (2 base + 2 = 4 on the road)
            new[] { 0, 0 },
            new[] { 2, 0, 0 },      // CARGO TRUCKS → same
            new[] { 0, 0 },
            new[] { 0, 0 },
        };

        // The levels this island's player actually owns. Row lengths MUST match AxisList above.
        private readonly int[][] _lv = { new int[2], new int[3], new int[2], new int[3], new int[2], new int[3], new int[2], new int[2] };

        // ═══════════════════════════════════════════════════════════════════════════════════════════
        //  GHOST-BUILDING UNLOCKS — the one-time purchases
        //
        //  Separate from the upgrade axes: these are bought once, never levelled. The buildings are
        //  ALREADY in the scene with their real materials; the code swaps in a translucent "ghost"
        //  material while they are locked, and swaps the originals back when you buy. So the player can
        //  always see the shape of what they are saving up for, which is the point.
        //
        //  Saved as "<islandKey>u#<index>" — note the "u", which keeps these from colliding with the
        //  axis keys ("coal#0#0" vs "coalu#0").
        // ═══════════════════════════════════════════════════════════════════════════════════════════
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
        private SiteLife _life;
        private float _smeltGlow;        // seconds of smoke left after the last conversion
        // Which arrival bay each mine's line uses at the shed, ordered left-to-right across the approach.
        private readonly Dictionary<Transform, int> _railLanes = new Dictionary<Transform, int>();
        private int _railLaneCount;

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

        // ═══════════════════════════════════════════════════════════════════════════════════════════
        //  TRAINS — the mine → storage leg
        //
        //  Each train is a plain C# object (not a MonoBehaviour) driving Transforms that already exist
        //  in the scene. One agent per mine, up to four; trains 2-4 stay asleep until their mine is
        //  unlocked. The loop never ends:
        //
        //      LoadMountain ──▶ Haul ──▶ Deposit ──▶ Return ──┐
        //      (hidden inside   (visible  (hidden inside  (visible, empty)
        //       the mine,        on the    the shed,      │
        //       timer runs)      rails)    dumping ore)   │
        //           ▲                                     │
        //           └─────────────────────────────────────┘
        //
        //  "Hidden" is literal — SetTrainVisible(false) switches the GameObjects off, so the engine only
        //  exists on screen while it is actually travelling. That is why it appears to come out of the
        //  tunnel portal: it is switched on at the mine's pivot, which sits inside the building mesh.
        //
        //  If the ore yard is full, Deposit simply does not finish — the train waits inside the shed.
        //  The player sees the trains stop, which is the intended signal to upgrade Storage.
        // ═══════════════════════════════════════════════════════════════════════════════════════════

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

        // ═══════════════════════════════════════════════════════════════════════════════════════════
        //  TRUCKS — the two road legs
        //
        //  Same idea as trains, but trucks stay visible the whole time and drive a closed oval: out
        //  along one lane, back along the other. Their cycle:
        //
        //      ToLoad ──▶ Loading ──▶ ToDrop ──▶ Dropping ──┐
        //      (drive to  (pause at   (drive to  (pause, hand over cargo:
        //       pickup)    the pile)   target)    ore → smelter, bars → cash)
        //          ▲                                        │
        //          │                                        │
        //          └──── if there is more to haul ◀──────────┤
        //                                                   │
        //      ToIdle ──▶ Idle  ◀───── if the source is empty┘
        //      (drive to  (parked; leaves the moment work appears)
        //       wait spot)
        //
        //  Idle trucks are the other visible bottleneck signal: a row of parked cargo trucks means the
        //  smelter is not producing fast enough, so the player knows which station to upgrade next.
        // ═══════════════════════════════════════════════════════════════════════════════════════════

        private enum TK { ToLoad, Loading, ToDrop, Dropping, ToIdle, Idle }

        /// <summary>
        /// Which leg a truck runs. The order matters — <see cref="BuildRoadLoops"/> emits exactly one
        /// loop per route in this order, so the loop index IS the route.
        /// </summary>
        private enum Route { Ore, Market, Export }   // ore: yard→smelter · market: bars→market · export: bars→dock
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

        // ═══════════════════════════════════════════════════════════════════════════════════════════
        //  PUBLIC SURFACE — everything the UI is allowed to touch
        //
        //  CoalHud, StationBadges, HudJuice and IslandMapUI all talk to the island through these members
        //  and nothing else. The UI never reads the sim's internals, and the sim never reaches into the
        //  UI — so you can rebuild the whole interface without touching a line of gameplay code.
        //
        //  The important ones: AxisCost/AxisLevel/AxisMaxed to draw a button, TryUpgrade to press it,
        //  StationAnchor to know where in the world to float it, and CashPerMinute for the top bar.
        // ═══════════════════════════════════════════════════════════════════════════════════════════
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

        /// <summary>First mesh inside a prefab, or null — lets the yard prefabs be left empty safely.</summary>
        private static Mesh MeshOf(GameObject prefab)
        {
            if (prefab == null) return null;
            var mf = prefab.GetComponentInChildren<MeshFilter>(true);
            return mf != null ? mf.sharedMesh : null;
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

        // ═══════════════════════════════════════════════════════════════════════════════════════════
        //  EFFECTIVE RATES — the actual economy
        //
        //  Nothing in the sim reads a raw Inspector value directly; it reads one of these instead.
        //  Every one follows the same shape:
        //
        //      base value  ×  (1 + coefficient × axisEffectScale × level)  ×  unlock bonuses
        //      └─ Inspector   └────────── the upgrade the player bought ──┘  └─ one-time buildings
        //
        //  So a level is always a straight-line gain on one term, but the terms MULTIPLY each other.
        //  That is what makes the curve steep: upgrading the mine and the train and the trucks together
        //  compounds, which is the core idle-tycoon feeling.
        //
        //  Why axisEffectScale exists: the coefficients below were originally tuned for a 10-level
        //  track. Measured output came out at base·(1 + 0.335·L)^2.89 — which hit the island's income
        //  cap around level 8 and made every level after that worthless. Rather than re-tune ~20
        //  coefficients by hand, one shared scale (0.085) stretches the same curve across 50 levels.
        //  Turn it UP for a faster, shorter game; DOWN for a longer grind.
        // ═══════════════════════════════════════════════════════════════════════════════════════════
        // POWER PLANT — the only station that touches everything else. Both feed into the formulas below,
        // which is why it is the most expensive unlock: it multiplies gains you already bought.
        private float PowerIncome => 1f + 0.05f * axisEffectScale * _lv[StPower][0];   // Generators: every sale
        private float PowerSpeed => 1f + 0.03f * axisEffectScale * _lv[StPower][1];    // Turbines: every vehicle

        // MINE — how much ore exists per trip, and how long the train sits inside the mountain loading.
        // "Dwell" values DIVIDE, so a higher level means a shorter pause. That is why they read inverted.
        private float MineDwell => dwellSeconds / (1f + 0.2f * axisEffectScale * _lv[StMine][1]);
        private float EffTrainOre => trainOrePerTrip
            * (1f + 0.25f * axisEffectScale * _lv[StMine][0])        // Mine → Richness: ore in the ground
            * (ActiveWagons / (float)BaseWagons)                     // more wagons = proportionally more cargo
            * (1f + 0.25f * axisEffectScale * _lv[StTrain][2])       // Train → Wagon Cargo: per-wagon load
            * (_unlocked[UnlockDeepShaft] ? deepShaftBonus : 1f);

        // TRAIN — the mine→storage leg. Wagons are the one upgrade you can literally count on screen.
        private float EffTrainSpeed => trainSpeed * (1f + 0.15f * axisEffectScale * _lv[StTrain][0]) * (_unlocked[UnlockDepot] ? depotBonus : 1f) * PowerSpeed;
        private int ActiveWagons => Mathf.Min(BaseWagons + _lv[StTrain][1], MaxWagons);

        // STORAGE — the ore yard. EffStorageFull is both the economic buffer and the size of the visible
        // pile: PileStack widens its grid to match, so buying Capacity enlarges the heap on screen.
        private float EffStorageFull => storageCapacity * (1f + 0.5f * axisEffectScale * _lv[StStorage][0]) * (_unlocked[UnlockWarehouse] ? warehouseBonus : 1f);
        private float StorageDwell => dwellSeconds / (1f + 0.2f * axisEffectScale * _lv[StStorage][1]);

        // ORE TRUCKS — storage→smelter. Count is capped by AxisMaxLv because each truck is a real body
        // parked in the scene; ApplyFleetStates wakes them one at a time as you buy.
        private int OreTruckCount => OreBaseTrucks + _lv[StOreTrucks][0];
        private float EffOreSpeed => truckSpeed * (1f + 0.15f * axisEffectScale * _lv[StOreTrucks][1]) * PowerSpeed;
        private float EffOreCap => oreTruckCapacity * (1f + 0.30f * axisEffectScale * _lv[StOreTrucks][2]);

        // SMELTER — turns ore into bars at EffSmelt per second. If EffBarCap fills, smelting STOPS until
        // cargo trucks clear it, so an under-upgraded market throttles the whole chain from the far end.
        private float EffSmelt => smeltPerSecond * (1f + 0.30f * axisEffectScale * _lv[StSmelter][0]) * (_unlocked[UnlockSecondSmelter] ? secondSmelterBonus : 1f);
        private float EffBarCap => barCapacity * (1f + 0.5f * axisEffectScale * _lv[StSmelter][1]);

        // CARGO TRUCKS — smelter→market (or →dock on the export route, which pays exportPriceBonus more).
        private int CargoTruckCount => CargoBaseTrucks + _lv[StCargoTrucks][0];
        private float EffCargoSpeed => truckSpeed * (1f + 0.15f * axisEffectScale * _lv[StCargoTrucks][1]) * PowerSpeed;
        private float EffCargoCap => cargoTruckCapacity * (1f + 0.30f * axisEffectScale * _lv[StCargoTrucks][2]);

        // MARKET — where cash is actually made. valueMultiplier is the island's tier (diamond bars are worth
        // far more than coal), which is why later islands feel like a different scale of money.
        private float EffBarPrice => barPrice * valueMultiplier * (1f + 0.40f * axisEffectScale * _lv[StMarket][0]) * (_unlocked[UnlockTradePost] ? tradePostBonus : 1f) * PowerIncome;
        private float MarketDwell => dwellSeconds / (1f + 0.2f * axisEffectScale * _lv[StMarket][1]);

        /// <summary>
        /// One-time setup, and the order matters a lot. Roughly: get services → load saved levels → find
        /// every landmark by name → move the yards → build the vehicles → build the visible track →
        /// re-apply everything the player already owns.
        ///
        /// Two ordering traps, both of which caused real bugs:
        /// <list type="bullet">
        /// <item><see cref="RelocateYards"/> must run BEFORE the piles or the roads are built — both
        /// measure the pad's final position, and a road built first would point at the old spot.</item>
        /// <item><see cref="BuildSiteDressing"/> must run AFTER the trains exist, because it reads each
        /// train's resolved rail path to lay track and place that line's tunnel portal.</item>
        /// </list>
        /// If any core landmark is missing this disables itself rather than half-running — a silently
        /// broken island is far harder to diagnose than one that says why it stopped.
        /// </summary>
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
            if (_storage != null) _deckY = _storage.position.y;

            // First thing after the landmarks resolve, before anything measures a position: give the site
            // room. Everything downstream (yards, roads, rails, expansions, ridge) keys off these.
            SpreadSite();

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

            // After the yards move (so expansions never land on one) and before the dock / fourth-mine
            // lookups, which resolve buildings this may have just created.
            SpawnExpansions();
            _dock = Child(_islandRoot, "ghostx_dock");
            _mine4 = Child(_islandRoot, "ghostx_mine4");

            // A level-0 yard reads as ten chunks; a fully upgraded one needs the widest grid to hold what
            // it can now store, which is the whole point of buying Capacity.
            _oreYard = new PileStack(_orePile, _oreMat, storageCapacity / 10f, "OpOreHeap", MeshOf(oreChunkPrefab));
            _barYard = new PileStack(_refinedPile, _barMat, barCapacity / 10f, "OpBarHeap", MeshOf(barChunkPrefab));

            AssignRailLanes();       // before any rail path is built — they all read the lane table
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
            BuildSiteLife();
            CacheStationBodies();
            ApplyFleetStates();
            for (int u = 0; u < _unlocked.Length; u++) if (_unlocked[u]) ApplyUnlock(u);
            ApplyStationScale();     // show the levels already bought, without the purchase pop

            _ready = true;
        }

        private void Update() { if (_ready) Tick(Time.deltaTime); }

        /// <summary>
        /// One frame of the whole island. The order follows the ore's own journey — trains deliver into
        /// storage, trucks move it on, the smelter converts what arrived, then the visuals and the income
        /// meter catch up on the result. Running it in this order means ore delivered this frame can be
        /// picked up this frame, instead of always lagging one frame behind.
        /// </summary>
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
            TickLife(dt);
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
        /// <summary>
        /// Hands each mine an arrival bay at the shed, numbered left-to-right by where the mine actually
        /// sits across the approach. Sorting by geometry rather than by build order is what stops the
        /// lines swapping sides and crossing.
        /// </summary>
        private void AssignRailLanes()
        {
            Transform[] mines = { _mountain, _ghostMine2, _ghostMine, _mine4 };
            Vector3 axis = Flat(_storage.position - _mountain.position);
            _railLanes.Clear();
            _railLaneCount = 0;
            if (axis.sqrMagnitude < 0.01f) return;
            axis.Normalize();
            Vector3 side = new Vector3(-axis.z, 0f, axis.x);

            var present = new List<Transform>();
            for (int i = 0; i < mines.Length; i++) if (mines[i] != null) present.Add(mines[i]);
            // Insertion sort by lateral offset: at most four entries, and it keeps ties stable.
            for (int i = 1; i < present.Count; i++)
            {
                Transform key = present[i];
                float k = Vector3.Dot(Flat(key.position - _storage.position), side);
                int j = i - 1;
                while (j >= 0 && Vector3.Dot(Flat(present[j].position - _storage.position), side) > k)
                { present[j + 1] = present[j]; j--; }
                present[j + 1] = key;
            }
            for (int i = 0; i < present.Count; i++) _railLanes[present[i]] = i;
            _railLaneCount = present.Count;
        }

        private Vector3[] BuildRailPath(Transform mountain, Transform storage)
        {
            Vector3 a = mountain.position, b = storage.position;
            // Every mine hauls to the same shed, so aiming all of them at its pivot drew three lines
            // crossing into one point — the single worst knot on the map. Give each line its own bay,
            // offset sideways from the shed, so they run in parallel and arrive side by side.
            int lane;
            if (_railLaneCount > 1 && _railLanes.TryGetValue(mountain, out lane))
            {
                // Bays are ordered by where each mine actually sits across the approach, so the leftmost
                // mine gets the leftmost bay. Numbering them in construction order instead made the lines
                // swap sides and cross in an X right in front of the shed.
                Vector3 axis = Flat(_storage.position - _mountain.position).normalized;
                Vector3 side = new Vector3(-axis.z, 0f, axis.x);
                b += side * (railSeparation * (lane - (_railLaneCount - 1) * 0.5f));
            }
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
                // Sitting inside the mountain being filled. Faster with Mine → Load Speed (MineDwell).
                case TR.LoadMountain:
                    a.timer -= dt;
                    if (a.timer <= 0f)
                    {
                        a.carry = EffTrainOre;                      // one trip's worth of ore, decided at load time
                        ShowTrainAt(a, a.path[0], a.path[1]);       // pop into existence at the railhead, facing storage
                        SetWagonOre(a, true);                       // show the ore cubes sitting in the wagons
                        a.wp = 1; a.state = TR.Haul;
                    }
                    break;

                // Driving down the rails, visible.
                case TR.Haul:
                    if (DriveTrain(a, true, dt)) { SetTrainVisible(a, false); a.timer = StorageDwell; a.state = TR.Deposit; }
                    break;

                // Hidden inside the storage shed, tipping ore onto the yard.
                case TR.Deposit:
                    a.timer -= dt;
                    if (a.timer > 0f) break;
                    double space = EffStorageFull - _storeOre;
                    if (space > 0d)
                    {
                        double dep = System.Math.Min(space, a.carry);   // only as much as the yard can still take
                        _storeOre += dep; a.carry -= dep;
                    }
                    // Still holding ore means the yard filled up. Staying in this state keeps the train
                    // parked in the shed and stops the whole mine — the intended "upgrade Storage" signal.
                    if (a.carry > 0.01d) break;
                    a.carry = 0d;
                    ShowTrainAt(a, a.path[a.path.Length - 1], a.path[a.path.Length - 2]);   // reappear facing back
                    SetWagonOre(a, false);                      // wagons are empty now
                    a.wp = a.path.Length - 2; a.state = TR.Return;
                    break;

                // Driving back up the rails empty, then straight into the next load.
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
            // Pull both ends back to the buildings' walls. Driving to the pivot means driving INTO the
            // building — and the generated road stops at the wall too, so an un-inset route would also
            // leave trucks running along bare ground for the last few metres.
            a += dir * StopInset(from, dir);
            b -= dir * StopInset(to, dir);
            len = Flat(b - a).magnitude;
            if (len < 1f) return path;
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

        /// <summary>
        /// One frame for one truck. Ore trucks and cargo trucks run identical logic — the only difference
        /// is which stockpile they draw from and what "dropping" means (tip into the smelter vs. sell).
        /// The <c>ore</c> flag below picks between the two everywhere.
        /// </summary>
        private void TruckTick(TruckAgent a, float dt)
        {
            bool ore = a.route == Route.Ore;
            double avail = ore ? _storeOre : _bars;   // what this truck's pickup pile currently holds
            switch (a.state)
            {
                // Driving to the pickup. Cargo is taken the instant it arrives, so two trucks can never
                // load the same ore — whoever gets there first subtracts it from the pile.
                case TK.ToLoad:
                    if (DriveLoop(a, a.loadIdx, dt))
                    {
                        double take = System.Math.Min(ore ? EffOreCap : EffCargoCap, avail);
                        if (take <= 0.01d) { a.state = TK.ToIdle; break; }   // beaten to it — go park
                        if (ore) _storeOre -= take; else _bars -= take;
                        a.carry = take; Show(a.load, true);                  // show the cargo block on the flatbed
                        a.timer = ore ? StorageDwell : dwellSeconds; a.state = TK.Loading;
                    }
                    break;

                // Paused at the pile being filled.
                case TK.Loading:
                    a.timer -= dt;
                    if (a.timer <= 0f) a.state = TK.ToDrop;
                    break;

                // Driving the loaded half of the oval to the smelter / market.
                case TK.ToDrop:
                    if (DriveLoop(a, a.dropIdx, dt)) { a.timer = ore ? dwellSeconds : MarketDwell; a.state = TK.Dropping; }
                    break;

                // Handing the cargo over. For ore that is just a transfer; for bars this is the moment
                // the player actually gets paid, and the only place cash enters the game.
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
            // Remember that the furnace ran. _refOre is an input buffer that trucks fill and Smelt drains
            // in the same frame, so testing it directly made the smoke stack cough once per delivery
            // instead of running continuously. This keeps it lit for a moment after each conversion.
            if (amt > 0d) _smeltGlow = smeltGlowSeconds;
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

        // ═══════════════════════════════════════════════════════════════════════════════════════════
        //  INCOME METER — the "$ / min" figure in the top bar
        //
        //  Money arrives in irregular lumps (one truck selling one load), so a naive rate would jump
        //  around wildly. Instead this keeps a 60-slot ring buffer, one slot per second, and reports the
        //  trailing sum. _trailing is maintained incrementally — add the new second, subtract the second
        //  falling out of the window — so it costs the same no matter how long you play.
        //
        //  It is also the value SAVED for offline earnings: an island you are not standing on keeps
        //  paying out at its last measured rate. That is why it is only persisted once the window is at
        //  least RateSaveMinSeconds full — saving during the warm-up would bank a misleading spike.
        // ═══════════════════════════════════════════════════════════════════════════════════════════

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
        /// Grows the island and pushes everything on it apart from the site centre, so the buildings,
        /// yards, track and expansions each get clear ground instead of overlapping.
        ///
        /// Two different operations, because the ground and the props need opposite treatment. The
        /// isle/lagoon meshes are centred discs, so they are <b>scaled</b> in place. Everything else is a
        /// prop standing on that ground, so it is <b>moved outward</b> — scaling those would inflate the
        /// buildings themselves. Scenery moves with the rest, which keeps the artist's composition
        /// intact rather than leaving trees sitting where the buildings used to be.
        /// </summary>
        private void SpreadSite()
        {
            if (siteSpread <= 1.001f && groundScale <= 1.001f) return;

            // Centre on the working chain, not the mesh, so the spread pushes away from where the player
            // is actually looking rather than from an arbitrary island origin.
            Vector3 centre = _mountain != null && _market != null
                ? (Flat(_mountain.position) + Flat(_market.position)) * 0.5f
                : Flat(_islandRoot.position);
            centre.y = 0f;

            foreach (Transform t in _islandRoot)
            {
                string n = t.name;
                bool isGround = n.StartsWith("isle_") || n.StartsWith("lagoon_") || n.StartsWith("edge");
                if (isGround)
                {
                    Vector3 s = t.localScale;
                    t.localScale = new Vector3(s.x * groundScale, s.y, s.z * groundScale);
                    // A disc scaled about its own pivot also drifts if that pivot is off-centre; re-anchor
                    // it so the enlarged ground still sits under the site.
                    Vector3 gp = t.position;
                    t.position = new Vector3(centre.x + (gp.x - centre.x) * groundScale, gp.y,
                                             centre.z + (gp.z - centre.z) * groundScale);
                    continue;
                }
                if (n.StartsWith("Dressing") || n.StartsWith("Op")) continue;   // generated later
                Vector3 p = t.position;
                t.position = new Vector3(centre.x + (p.x - centre.x) * siteSpread, p.y,
                                         centre.z + (p.z - centre.z) * siteSpread);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════════════════════
        //  EXPANSION BUILDINGS
        //
        //  Everything the player can unlock but the island mesh does not author. Each is dropped in as a
        //  child named "ghostx_<thing>", which is the prefix BuildUnlockRegistry looks for — so simply
        //  existing under that name is enough to get it ghosted while locked and solid once bought.
        //  Nothing else has to be wired up.
        //
        //  Placement is relative to the building each expansion belongs to (a warehouse next to storage,
        //  a depot beside the rail line, and so on), then nudged outward until it clears everything
        //  already standing. That keeps it working on all eight islands without hand-placing 48 objects.
        // ═══════════════════════════════════════════════════════════════════════════════════════════

        private void SpawnExpansions()
        {
            Vector3 chain = Flat(_market.position - _mountain.position).normalized;   // mine → market axis
            Vector3 side = new Vector3(-chain.z, 0f, chain.x);
            // Put expansions on the opposite side of the chain from the yards, so the two never compete.
            float yardSign = Mathf.Sign(Vector3.Dot(Flat(_orePile.position - _storage.position), side));
            if (yardSign == 0f) yardSign = 1f;
            Vector3 free = side * -yardSign;

            Expansion("ghostx_warehouse", warehousePrefab, _storage.position + free * 16f);
            Expansion("ghostx_depot", depotPrefab, Vector3.Lerp(_mountain.position, _storage.position, 0.55f) + free * 15f);
            Expansion("ghostx_power", powerPrefab, _refinery.position + free * 16f);
            Expansion("ghostx_shaft", shaftPrefab, _mountain.position + free * 15f);
            // The dock belongs at the water, so it runs past the market and off the end of the chain.
            Expansion("ghostx_dock", dockPrefab, _market.position + chain * 20f + free * 6f);
            // A fourth mine is another mine: clone the real one rather than inventing a lookalike.
            Expansion("ghostx_mine4", _mountain != null ? _mountain.gameObject : null,
                      _mountain.position + free * 30f, true);
        }

        /// <summary>
        /// Drops one expansion building, unless the island already authors it (some do) or no prefab is
        /// wired. <paramref name="asClone"/> copies an in-scene object instead of instantiating an asset,
        /// which is how the fourth mine reuses the island's own mine model at its own scale.
        /// </summary>
        private void Expansion(string name, GameObject prefab, Vector3 want, bool asClone = false)
        {
            if (prefab == null || Child(_islandRoot, name) != null) return;

            var go = Instantiate(prefab, _islandRoot);
            go.name = name;
            if (asClone) StripOpChildren(go.transform);
            else go.transform.localScale = Vector3.one * expansionScale;

            // Walk it outward from the island centre until it stops overlapping anything already placed.
            Vector3 outward = Flat(want - _islandRoot.position);
            outward = outward.sqrMagnitude < 0.01f ? Vector3.forward : outward.normalized;
            Vector3 pos = want;
            for (int guard = 0; guard < 12 && Occupied(pos, expansionClearance, go.transform); guard++)
                pos += outward * 5f;

            go.transform.position = new Vector3(pos.x, _deckY, pos.z);
            go.transform.rotation = Quaternion.LookRotation(Flat(_islandRoot.position - pos).normalized, Vector3.up);
        }

        /// <summary>
        /// True if a real building already stands within <paramref name="radius"/> of a point.
        ///
        /// Only buildings count. The islands are covered in scenery — thirty-odd dead trees, rocks and
        /// bushes — and treating those as obstacles pushed every expansion out to the coastline, because
        /// each one shoved the building another five metres looking for a gap that scenery never leaves.
        /// </summary>
        private bool Occupied(Vector3 p, float radius, Transform ignore)
        {
            float r2 = radius * radius;
            foreach (Transform t in _islandRoot)
            {
                if (t == ignore || t.name.StartsWith("Dressing") || t.name.StartsWith("Op")) continue;
                if (t.GetComponentsInChildren<Renderer>(true).Length == 0) continue;
                Bounds b = WorldBounds(t);
                if (b.size.y < 1.5f) continue;                              // flat pads: fine to stand near
                if (Mathf.Max(b.size.x, b.size.z) < 6f) continue;           // props and trees: not obstacles
                if (SqrXZ(b.center, p) < r2) return true;
            }
            // Also keep clear of the rail corridors. Buildings were the only thing tested before, which
            // is how expansions ended up sitting across the lines coming down from the mines.
            return OnRailCorridor(p, radius * 0.7f);
        }

        /// <summary>True if a point lies within <paramref name="clear"/> of any train's line.</summary>
        private bool OnRailCorridor(Vector3 p, float clear)
        {
            TrainAgent[] all = { _train1, _train2, _train3, _train4 };
            for (int i = 0; i < all.Length; i++)
            {
                TrainAgent a = all[i];
                if (a == null || a.path == null || a.path.Length < 2) continue;
                Vector3 s = a.path[0], e = a.path[a.path.Length - 1];
                Vector3 d = Flat(e - s);
                float len = d.magnitude;
                if (len < 0.01f) continue;
                d /= len;
                float t = Mathf.Clamp(Vector3.Dot(Flat(p - s), d), 0f, len);
                if (SqrXZ(s + d * t, p) < clear * clear) return true;
            }
            return false;
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
            HaulRoad("OpRoad_OreYard", _storage, _orePile, roadY, road, line);
            HaulRoad("OpRoad_Ore", _orePile, _refinery, roadY, road, line);
            HaulRoad("OpRoad_BarYard", _refinery, _refinedPile, roadY, road, line);
            HaulRoad("OpRoad_Market", _refinedPile, _market, roadY, road, line);
            if (_dock != null) HaulRoad("OpRoad_Export", _refinedPile, _dock, roadY, road, line);

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

        /// <summary>
        /// Lays one leg of the haul road, stopping it at each endpoint's wall rather than its pivot.
        /// Only ends that finish in the open get an overrun for the truck turnaround.
        /// </summary>
        private void HaulRoad(string name, Transform from, Transform to, float roadY, Material road, Material line)
        {
            if (from == null || to == null) return;
            Vector3 dir = Flat(to.position - from.position);
            if (dir.sqrMagnitude < 0.01f) return;
            dir.Normalize();
            float insetA = StopInset(from, dir), insetB = StopInset(to, dir);
            Vector3 a = from.position + dir * insetA, b = to.position - dir * insetB;
            a.y = b.y = _deckY;
            if (Flat(b - a).magnitude < 1f) return;   // buildings too close to fit a road between them
            RouteMesh.Road(_dressing, name, a, b, roadWidth, roadY,
                           insetA > 0f ? 0f : roadWidth * 0.5f,
                           insetB > 0f ? 0f : roadWidth * 0.5f,
                           road, line);
        }

        /// <summary>
        /// How far short of an object's pivot a road or a truck should stop. Solid buildings return the
        /// distance out to their wall; flat yard pads return 0, because driving onto those is the point.
        /// </summary>
        private static float StopInset(Transform t, Vector3 dir)
        {
            Bounds b = WorldBounds(t);
            if (b.size.y < 1.5f) return 0f;
            return Mathf.Abs(dir.x) * b.extents.x + Mathf.Abs(dir.z) * b.extents.z;
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

        // ---------------- site life ----------------

        /// <summary>
        /// Sets up the crew and the smelter smoke. Workers pace the legs between the buildings they'd
        /// plausibly walk — mine to storage, storage to refinery, refinery to market — which happens to be
        /// alongside the haul road, so the whole chain reads as one worked site rather than four props.
        /// </summary>
        private void BuildSiteLife()
        {
            // A footpath running alongside the haul road rather than through the buildings: offset to the
            // far side from the yards, and inset at each end so nobody walks into a wall.
            Vector3 axis = Flat(_market.position - _mountain.position).normalized;
            Vector3 kerb = new Vector3(-axis.z, 0f, axis.x);
            float yardSide = Mathf.Sign(Vector3.Dot(Flat(_orePile.position - _storage.position), kerb));
            if (yardSide == 0f) yardSide = 1f;
            Vector3 pathOff = kerb * (-yardSide * (roadWidth * 0.5f + 3f));

            Transform[] stops = { _mountain, _storage, _refinery, _market };
            var patrol = new Vector3[stops.Length];
            for (int i = 0; i < stops.Length; i++)
            {
                Vector3 p = Flat(stops[i].position) + pathOff;
                p.y = _deckY;
                patrol[i] = p;
            }
            // Smoke leaves from the top of the refinery's silhouette, wherever the artist put the stack.
            Bounds rb = WorldBounds(_refinery);
            Vector3 chimney = new Vector3(rb.center.x, rb.max.y * 0.96f, rb.center.z);
            Material smoke = MakeMat(_srcMat, smokeColor);

            _life = new SiteLife(_islandRoot, workerPrefab, smokePuffPrefab, smoke,
                                 patrol, chimney, _deckY, workerScale,
                                 maxWorkers, maxSmokePuffs, smokePuffLife, smokePuffRise, smokePuffSpread);
        }

        private void TickLife(float dt)
        {
            if (_life == null) return;
            // Crew size follows total investment in the island, so hiring is a visible side effect of
            // every purchase rather than something the player has to manage.
            int levels = 0;
            for (int s = 0; s < _lv.Length; s++) levels += StationLevelSum(s);
            _life.SetCrew(1 + levels / Mathf.Max(1, workerLevelsPer));
            // Puff rate tracks smelting throughput: an idle smelter barely smokes, a maxed one billows.
            if (_smeltGlow > 0f) _smeltGlow -= dt;
            float rate = _smeltGlow > 0f ? Mathf.Clamp(EffSmelt * 0.55f, 0.8f, 6f) : 0f;
            _life.Tick(dt, rate);
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
