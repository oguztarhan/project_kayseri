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
    /// static, with one deliberate exception: <see cref="ArrangeMines"/> and <see cref="ArrangeChain"/>
    /// rebuild the site at startup — mines into one back row, the working chain onto one straight spine —
    /// because the authored positions scattered them and the map read as a knot.</para>
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
        [SerializeField] private float siteSpread = 1.7f;        // push landmarks out from the site centre
        [SerializeField] private float groundScale = 2.1f;       // grow isle/lagoon so the spread stays on land
        [SerializeField] private float railSeparation = 13f;     // side-by-side gap where rail lines reach storage
        [SerializeField] private float routeClearance = 6f;      // gap a building keeps off a road or rail centreline
        [SerializeField] private float tidyGap = 8f;             // gap TidySite opens between two overlapping buildings
        [SerializeField] private float mineRowSpacing = 36f;     // shoulder gap between mines in the back row

        [Header("Site dressing")]
        // The islands ship with painted road and rail, but it was authored against a layout the sim no
        // longer drives, so trucks crossed bare ground beside track that led somewhere else — the main
        // reason the maps read as unfinished. Generating both from the route endpoints means the track can
        // never disagree with the motion, on any island, whatever its mesh happens to carry.
        [SerializeField] private bool generateTrack = true;
        [SerializeField] private float roadWidth = 9f;
        // Each island's signature: how far the haul road bows sideways, as a fraction of the chain
        // length. 0 is a straight spine; the sign picks the side. The smelter holds the corner.
        [SerializeField] private float chainBend = 0f;
        [SerializeField] private float yardOut = 13f;          // how far the stock yards sit beside the road
        [SerializeField] private float mineUnlockHaulBonus = 2f;   // a bought mine unlock multiplies that line's haul
        [SerializeField] private float seaTradeDistance = 42f; // how far offshore the floating trade post anchors
        [SerializeField] private float shipSpeed = 5f;
        [SerializeField] private float shipYawOffset = 0f;     // authored ship meshes may not face +Z
        [SerializeField] private int scatterProps = 16;        // cloned scenery pieces that fill the empty grass
        [SerializeField] private GameObject portalPrefab;     // tunnel mouth each rail line emerges from
        [SerializeField] private GameObject ridgeRockPrefab;  // massed behind the mine to close off the map edge
        [SerializeField] private int ridgeRocks = 6;
        [SerializeField] private float ridgeDistance = 26f;   // behind the mine row, before mine-clearing
        [SerializeField] private float ridgeClearance = 15f;  // keep rocks off any mine head
        [SerializeField] private float ridgeScale = 1.35f;
        [SerializeField] private float portalScale = 3.4f;
        [SerializeField] private Color roadColor = new Color(0.47f, 0.40f, 0.32f);   // packed-dirt haul road
        [SerializeField] private Color roadLineColor = new Color(0.93f, 0.88f, 0.72f);
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
        // Renamed from expansionClearance, which measured pivot to pivot and so let a small building sit
        // inside a large one. This is the gap left between the two FOOTPRINTS, so it means the same thing
        // whatever the buildings' sizes — and needs to be far smaller than the old number.
        [SerializeField] private float expansionGap = 9f;
        [SerializeField] private float landInset = 0.78f;   // how far out toward the shoreline a shoved building may go

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
            "MINE 2 RICH VEIN (2x haul)", "SECOND SMELTER (2x smelt)", "TRADE POST (+50% price)", "MINE 3 RICH VEIN (2x haul)",
            "WAREHOUSE (2x storage)", "TRAIN DEPOT (+25% train speed)", "EXPORT DOCK (+25% export price)", "MINE 4 RICH VEIN (2x haul)",
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
            null,   // mine 4 runs from day one; its unlock enriches the haul instead
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

        /// <summary>
        /// One planned centreline between two landmarks. Roads carry a <see cref="roadName"/> and get
        /// tarmac drawn along them; rail legs are keep-clear corridors only, since the rail geometry is
        /// built from the train's own path.
        ///
        /// The endpoints are held as Transforms rather than positions because the site is still being
        /// arranged while the plan is in use — <see cref="TidySite"/> moves things after the legs are
        /// planned, and reading the position on demand means a leg can never go stale.
        /// </summary>
        private sealed class RouteLeg
        {
            public Transform a, b;
            public Vector3 bOffset;  // rail lines stop at an arrival bay beside the shed, not on its pivot
            public float clear;      // half-width plus the margin a building has to keep off the line
            public string roadName;  // null on rail corridors

            public Vector3 A { get { return new Vector3(a.position.x, 0f, a.position.z); } }
            public Vector3 B { get { return new Vector3(b.position.x + bOffset.x, 0f, b.position.z + bOffset.z); } }
        }
        private readonly List<RouteLeg> _legs = new List<RouteLeg>();
        private Transform _exportBend;   // waypoint the export run swings around the market on
        private Transform _elbow;        // corner of a bent chain; null on straight islands
        private Transform _viaOre, _viaBar;   // where each yard's driveway meets the haul road
        private float _yardSign = 1f;    // which flank of the road the stock yards live on

        // ---- harbour life: authored ships shuttling between the pier and the offshore trade post ----
        private struct Ship { public Transform t; public Vector3 pier, sea; public float prog, dwell, phase; public bool toSea; }
        private readonly List<Ship> _ships = new List<Ship>();
        private float _waterY;
        private Vector3 _landCentre;     // island footprint, inset — the area a shoved building may use
        private float _landHalfX, _landHalfZ;
        private Vector3 _mineRow;        // direction the back row of mines extends in (zero until arranged)
        private int _mineRowNextSlot = 1;   // where the fourth mine joins the row

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
            public float cargoMult = 1f;    // mine unlocks enrich a line's haul instead of switching it on
            public GameObject track;        // its rail line - hidden with the portal until the mine is bought
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

        /// <summary>
        /// Radius of a circle that fully contains a footprint — half the diagonal, not half the longest
        /// side. Two boxes can clear each other's longest side and still overlap at the corners, which is
        /// where the last stubborn metre of building-inside-building kept coming from.
        /// </summary>
        private static float FootprintRadius(Bounds b)
        {
            return new Vector2(b.size.x, b.size.z).magnitude * 0.5f;
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
        /// <item><see cref="ArrangeChain"/> must run BEFORE the piles or the roads are built — both
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

            MeasureLand();   // after the spread grew the ground: every shove below is clamped to it
            ArrangeChain();  // the working chain first — its first arm dictates the mine row's facing
            ArrangeMines();  // then every mine into one back row at the head of that arm

            // The route plan comes first and is then read by everything that places geometry. Rails are
            // planned before the yards move, because a yard picks which side of its building to sit on
            // partly by which side keeps it off the track.
            PlanRails();
            PlanRoads();       // yards were placed on the spine by ArrangeChain, so the plan is final

            // After the yards move (so expansions never land on one) and before the dock / fourth-mine
            // lookups, which resolve buildings this may have just created.
            SpawnExpansions();
            _dock = Child(_islandRoot, "ghostx_dock");
            _mine4 = Child(_islandRoot, "ghostx_mine4");
            // Buildings grow with the levels already bought — by up to a fifth — so they have to reach
            // their real size BEFORE anything measures them. Settling the layout first and inflating the
            // buildings afterwards is what left the depot clipping the track on a well-developed island.
            CacheStationBodies();
            ApplyStationScale();

            // The designed skeleton is already final - chain on the spine, mines in the row - so the
            // bays and corridors are computed once and everything loose settles against them.
            AssignRailLanes();
            ReplanRails();
            TidySite();

            // A level-0 yard reads as ten chunks; a fully upgraded one needs the widest grid to hold what
            // it can now store, which is the whole point of buying Capacity.
            _oreYard = new PileStack(_orePile, _oreMat, storageCapacity / 10f, "OpOreHeap", MeshOf(oreChunkPrefab));
            _barYard = new PileStack(_refinedPile, _barMat, barCapacity / 10f, "OpBarHeap", MeshOf(barChunkPrefab));

            // Lanes were assigned above, between the two placement passes, and the mines have been pinned
            // ever since — so the paths built here land exactly on the corridors that were kept clear.
            _train1 = BuildTrain(engine, _mountain);
            _train1.active = true;
            // "ghost_mine (1)" sits at the head of the second (already-laid) rail line; "ghost_mine" at the
            // head of the GH ghost-rail line — each becomes a live train when its unlock is bought
            if (_ghostMine2 != null) _train2 = BuildTrain(CloneTrainRig(engine, "train2"), _ghostMine2);
            if (_ghostMine != null) _train3 = BuildTrain(CloneTrainRig(engine, "train3"), _ghostMine);
            if (_mine4 != null) _train4 = BuildTrain(CloneTrainRig(engine, "train4"), _mine4);

            // Every mountain works from day one: all four lines run, and buying a mine unlock enriches
            // that line's haul rather than switching it on. Eight peaks with one moving train read as a
            // broken site, and dead grey track is a promise, not a feature.
            ActivateTrain(_train2);
            ActivateTrain(_train3);
            ActivateTrain(_train4);
            Solidify(_ghostMine2, _mountain);
            Solidify(_ghostMine, _mountain);
            SolidifyGhostRails();

            BuildTruckAgents();
            BuildUnlockRegistry();
            BuildSiteDressing();     // needs the rail paths the trains just resolved
            BuildSiteLife();
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

        /// <summary>
        /// Where a mine's line meets the shed, relative to the shed's pivot.
        ///
        /// Every mine hauls to the same building, so aiming all of them at its pivot drew four lines
        /// converging on one point — the single worst knot on the map. Each gets its own bay instead, and
        /// the bays are ordered by where the mine actually sits across the approach, so the leftmost mine
        /// gets the leftmost bay. Numbering them in construction order made the lines swap sides and
        /// cross in an X right in front of the shed.
        ///
        /// The route plan reads this too, so the keep-clear corridor sits over the track rather than over
        /// the shed's pivot — with four mines the outermost bay is a good 16 m away from it.
        /// </summary>
        private Vector3 RailBay(Transform mountain)
        {
            int lane;
            if (_railLaneCount <= 1 || !_railLanes.TryGetValue(mountain, out lane)) return Vector3.zero;
            Vector3 axis = Flat(_storage.position - _mountain.position).normalized;
            Vector3 side = new Vector3(-axis.z, 0f, axis.x);
            return side * (railSeparation * (lane - (_railLaneCount - 1) * 0.5f));
        }

        private Vector3[] BuildRailPath(Transform mountain, Transform storage)
        {
            Vector3 a = mountain.position, b = storage.position + RailBay(mountain);
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
                        a.carry = EffTrainOre * a.cargoMult;        // one trip's worth of ore, decided at load time
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
            loops.Add(RouteLoop(_orePile, _viaOre, _refinery));              // Route.Ore
            loops.Add(RouteLoop(_refinedPile, _viaBar, _market));            // Route.Market
            // Export bends around the market rather than driving through it — see SpawnExpansions.
            if (_dock != null) loops.Add(RouteLoop(_refinedPile, _exportBend, _dock));   // Route.Export
            return loops;
        }

        /// <summary>
        /// An out-and-back driving loop between two buildings, optionally bending around a waypoint.
        ///
        /// The export run is the one that needs the bend: the dock sits beyond the market on the same
        /// axis, so a straight line from the refined yard runs through the market building — and the road
        /// drawn along that line did exactly that. Routing via a waypoint set off to the market's side
        /// keeps both the trucks and the tarmac outside the walls.
        /// </summary>
        private List<Vector3> RouteLoop(Transform from, Transform via, Transform to)
        {
            var outbound = new List<Vector3>();
            var back = new List<Vector3>();
            if (via == null) LoopRun(outbound, back, from, to);
            else { LoopRun(outbound, back, from, via); LoopRun(outbound, back, via, to); }

            var path = new List<Vector3>();
            if (outbound.Count < 2) return path;
            path.AddRange(outbound);
            for (int i = back.Count - 1; i >= 0; i--) path.Add(back[i]);   // return lane, other way round
            return path;
        }

        /// <summary>One straight run of a loop: appends its outbound points and its return points.</summary>
        private void LoopRun(List<Vector3> outbound, List<Vector3> back, Transform from, Transform to)
        {
            if (from == null || to == null) return;
            Vector3 a = from.position, b = to.position;
            a.y = b.y = _deckY;
            Vector3 dir = b - a; dir.y = 0f;
            float len = dir.magnitude;
            if (len < 0.01f) return;
            dir /= len;
            // Pull both ends back to the buildings' walls. Driving to the pivot means driving INTO the
            // building — and the generated road stops at the wall too, so an un-inset route would also
            // leave trucks running along bare ground for the last few metres. A bare waypoint has no
            // walls, so StopInset returns 0 there and the two runs meet exactly on it.
            a += dir * StopInset(from, dir);
            b -= dir * StopInset(to, dir);
            len = Flat(b - a).magnitude;
            if (len < 1f) return;
            Vector3 side = new Vector3(-dir.z, 0f, dir.x) * (routeLaneWidth * 0.5f);
            int n = Mathf.Clamp(Mathf.RoundToInt(len / 4f), 2, 24);
            for (int i = 0; i <= n; i++) outbound.Add(Vector3.Lerp(a, b, i / (float)n) + side);
            for (int i = 0; i <= n; i++) back.Add(Vector3.Lerp(a, b, i / (float)n) - side);
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
                    if (_train2 != null) _train2.cargoMult = mineUnlockHaulBonus;
                    break;
                case UnlockSecondSmelter:
                    Solidify(_ghostRefinery, _refinery);
                    break;
                case UnlockTradePost:
                    Solidify(_ghostMarket, _market);
                    break;
                case UnlockThirdMine:
                    if (_train3 != null) _train3.cargoMult = mineUnlockHaulBonus;
                    break;
                case UnlockExportDock:
                    ApplyFleetStates();   // wakes the export loop's trucks
                    break;
                case UnlockFourthMine:
                    if (_train4 != null) _train4.cargoMult = mineUnlockHaulBonus;
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
            Show(a.track, true);
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

        /// <summary>
        /// Pulls every mine into one row along the back of the site, flanking the primary.
        ///
        /// The authored islands scatter the locked mines around the map - one behind storage, one out by
        /// the refinery - so each unlock added a rail line arriving from a new direction and the middle
        /// of the island read as a knot of track. In a row, the map reads top to bottom the way the ore
        /// flows: mountains at the back, a symmetric fan of rails down to the shed, the haul road
        /// through the middle, the market and the water at the front.
        ///
        /// Slots alternate flanks - first one side of the primary, then the other, then two out - so
        /// the row stays centred over the spine instead of marching off toward one coast. A first
        /// version extended one way only, and three slots of that walked the far mines into the sea.
        /// </summary>
        private void ArrangeMines()
        {
            if (_mountain == null || _market == null) return;
            Vector3 back = Flat(_mountain.position - (_elbow != null ? _elbow.position : _market.position));
            if (back.sqrMagnitude < 0.01f) return;
            back.Normalize();
            _mineRow = new Vector3(-back.z, 0f, back.x);
            _mineRowNextSlot = 0;

            // Nearest authored mine takes the first slot, so nothing swaps sides on the way in.
            Transform[] secondaries = { _ghostMine2, _ghostMine };
            if (_ghostMine != null && _ghostMine2 != null &&
                SqrXZ(_ghostMine.position, _mountain.position) < SqrXZ(_ghostMine2.position, _mountain.position))
            { secondaries[0] = _ghostMine; secondaries[1] = _ghostMine2; }

            for (int i = 0; i < secondaries.Length; i++)
            {
                if (secondaries[i] == null) continue;
                Vector3 want = NextRowSlot();
                want.y = secondaries[i].position.y;
                secondaries[i].position = want;
            }
        }

        /// <summary>
        /// The next free position in the mine row, skipping any slot the shoreline has no room for.
        /// Also feeds the fourth mine and the shaft when <see cref="SpawnExpansions"/> places them, so
        /// the whole mining district ends up in one line.
        /// </summary>
        private readonly List<Vector3> _rowUsed = new List<Vector3>();

        /// <summary>A candidate is dead if it would stand inside a mine the row already placed.</summary>
        private bool RowSlotClear(Vector3 p)
        {
            float min = mineRowSpacing * 0.75f;
            if (SqrXZ(p, _mountain.position) < min * min) return false;
            for (int i = 0; i < _rowUsed.Count; i++)
                if (SqrXZ(p, _rowUsed[i]) < min * min) return false;
            return true;
        }

        private Vector3 NextRowSlot()
        {
            int[] pattern = { 1, -1, 2, -2, 3, -3 };
            while (_mineRowNextSlot < pattern.Length)
            {
                Vector3 pos = _mountain.position + _mineRow * (mineRowSpacing * pattern[_mineRowNextSlot]);
                _mineRowNextSlot++;
                // Mines may stand right up at the coast — a mountain at the waterline reads as terrain —
                // so the test is the near-full footprint, not the cautious inset the buildings use.
                if (OnLand(pos, 0.95f) && RowSlotClear(pos)) { _rowUsed.Add(pos); return pos; }
                // Slot hangs over the water anyway (the primary itself can sit near the shore): bow it
                // toward the island centre until it lands. The row bends instead of stacking every
                // rejected mine on one fallback point, which is exactly what happened on Iron. The bow
                // must still clear the mines already placed — on a bent island the centre can lie along
                // the row, and an unchecked bow curled a slot straight back into the primary.
                for (int g = 0; g < 8; g++)
                {
                    pos += (new Vector3(_landCentre.x, pos.y, _landCentre.z) - pos) * 0.18f;
                    if (OnLand(pos, 0.95f) && RowSlotClear(pos)) { _rowUsed.Add(pos); return pos; }
                }
            }
            Vector3 last = _mountain.position + _mineRow * (mineRowSpacing * 2.5f);   // no land data at all
            _rowUsed.Add(last);
            return last;
        }

        /// <summary>
        /// Lays the working chain out along one straight spine from the primary mine to the market.
        ///
        /// The authored islands park storage, the refinery and the yards wherever the composition looked
        /// nice, so the haul road between them was a chain of slabs at odd angles - every leg its own
        /// direction, a kink at every joint. On a straight spine the legs are collinear, so the five
        /// roads render as one continuous ribbon down the island and the trucks drive it end to end.
        /// The mine and the market stay where the artist put them, which keeps each island's own
        /// orientation and coastline composition.
        ///
        /// The yard pads go ON the spine, halfway to the next building, so a pile reads as a loading
        /// bay on the road rather than a prop dropped beside it.
        /// </summary>
        private void ArrangeChain()
        {
            Vector3 a = _mountain.position, b = _market.position;
            Vector3 whole = Flat(b - a);
            if (whole.sqrMagnitude < 1f) return;
            float len = whole.magnitude;
            Vector3 dir = whole / len;
            Vector3 side = new Vector3(-dir.z, 0f, dir.x);

            // The bend is the island's signature. Zero keeps a straight spine; a bent island puts its
            // smelter at the corner, so the building itself fills the junction.
            _elbow = null;
            if (Mathf.Abs(chainBend) > 0.02f)
            {
                Vector3 e = Vector3.Lerp(a, b, 0.55f) + side * (chainBend * len);
                if (!OnLand(e, 0.9f)) e = Vector3.Lerp(a, b, 0.55f) + side * (chainBend * len * 0.5f);
                _elbow = Waypoint("OpElbow", e);
            }

            // Yards go on the outside of the bend (open ground); on a straight island, on whichever
            // flank still has land a yard-width out.
            Vector3 mid = _elbow != null ? _elbow.position : Vector3.Lerp(a, b, 0.55f);
            _yardSign = _elbow != null ? Mathf.Sign(chainBend)
                      : OnLand(mid + side * (yardOut + 8f), 0.9f) ? 1f
                      : OnLand(mid - side * (yardOut + 8f), 0.9f) ? -1f : 1f;

            PlaceOnChain(_storage, 0.30f, 0f);
            if (_elbow != null)
            {
                Vector3 e = _elbow.position;
                _refinery.position = new Vector3(e.x, _refinery.position.y, e.z);
            }
            else PlaceOnChain(_refinery, 0.62f, 0f);

            // The stock yards sit BESIDE the road in their own aprons, joined to it by a short driveway
            // — a pile in the middle of the carriageway read as an obstruction, not a depot.
            PlaceOnChain(_orePile, 0.45f, _yardSign * yardOut);
            PlaceOnChain(_refinedPile, 0.80f, _yardSign * yardOut);
            _viaOre = Waypoint("OpVia_Ore", ChainPoint(0.45f, 0f));
            _viaBar = Waypoint("OpVia_Bar", ChainPoint(0.80f, 0f));
        }

        /// <summary>Finds or creates a named routing marker under the island root, at deck height.</summary>
        private Transform Waypoint(string name, Vector3 pos)
        {
            Transform t = Child(_islandRoot, name);
            if (t == null)
            {
                var go = new GameObject(name);
                go.transform.SetParent(_islandRoot, true);
                t = go.transform;
            }
            t.position = new Vector3(pos.x, _deckY, pos.z);
            return t;
        }

        /// <summary>
        /// A point at fraction <paramref name="t"/> of the chain's arc length, offset sideways by
        /// <paramref name="lateral"/> from the local road direction. The chain is the one- or two-segment
        /// polyline mine — (elbow) — market.
        /// </summary>
        private Vector3 ChainPoint(float t, float lateral)
        {
            Vector3 a = Flat(_mountain.position), b = Flat(_market.position);
            if (_elbow == null)
            {
                Vector3 d = (b - a).normalized;
                Vector3 sd = new Vector3(-d.z, 0f, d.x);
                return a + (b - a) * t + sd * lateral;
            }
            Vector3 e = Flat(_elbow.position);
            float l1 = (e - a).magnitude, l2 = (b - e).magnitude;
            float along = t * (l1 + l2);
            if (along <= l1)
            {
                Vector3 d1 = (e - a) / Mathf.Max(0.01f, l1);
                Vector3 s1 = new Vector3(-d1.z, 0f, d1.x);
                return a + d1 * along + s1 * lateral;
            }
            Vector3 d2 = (b - e) / Mathf.Max(0.01f, l2);
            Vector3 s2 = new Vector3(-d2.z, 0f, d2.x);
            return e + d2 * (along - l1) + s2 * lateral;
        }

        private void PlaceOnChain(Transform t, float f, float lateral)
        {
            if (t == null) return;
            Vector3 p = ChainPoint(f, lateral);
            t.position = new Vector3(p.x, t.position.y, p.z);
        }

        // ═══════════════════════════════════════════════════════════════════════════════════════════
        //  ROUTE PLAN
        //
        //  Every road and rail line is planned here BEFORE any building is placed, so the expansions and
        //  the tidy pass can both be told to keep off them. The corridor test used to read the trains'
        //  paths instead — but those are not built until much later in Start, so it was testing against
        //  four null agents and never rejected anything. That is how buildings ended up on the track.
        // ═══════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Measures the ground the site may be arranged on, so the outward shoves have somewhere to stop.
        /// Without it a building with nowhere to go simply kept walking, and the power plant ended up
        /// standing in the sea off the north shore.
        /// </summary>
        private void MeasureLand()
        {
            foreach (Transform t in _islandRoot)
            {
                if (!t.name.StartsWith("isle_")) continue;
                Bounds b = WorldBounds(t);
                _landCentre = new Vector3(b.center.x, 0f, b.center.z);
                // Stored raw; each test applies its own inset, because "how close to the shore may this
                // stand" is not one number — a mountain at the waterline reads as coast, a warehouse
                // there reads as a mistake.
                _landHalfX = b.extents.x;
                _landHalfZ = b.extents.z;
                return;
            }
        }

        /// <summary>
        /// True if a point is still comfortably inland. Tested as an ellipse rather than the mesh's
        /// bounding box, because the island meshes are rounded — the box corners are open water.
        /// </summary>
        private bool OnLand(Vector3 p) { return OnLand(p, landInset); }

        private bool OnLand(Vector3 p, float inset)
        {
            if (_landHalfX <= 0.01f || _landHalfZ <= 0.01f) return true;   // no island mesh: don't constrain
            Vector3 d = Flat(p - _landCentre);
            float nx = d.x / (_landHalfX * inset), nz = d.z / (_landHalfZ * inset);
            return nx * nx + nz * nz <= 1f;
        }

        /// <summary>The rail corridors, one per mine that exists before the expansions are spawned.</summary>
        private void PlanRails()
        {
            AddRailLeg(_mountain);
            AddRailLeg(_ghostMine2);
            AddRailLeg(_ghostMine);
        }

        /// <summary>
        /// Reserves the corridor a mine's rail line runs down. Called before the arrival bays are handed
        /// out, so it reserves the whole spread of bays rather than one line — a mine's bay still shifts
        /// by up to half the separation once the fourth mine joins the ordering.
        /// </summary>
        private void AddRailLeg(Transform mine)
        {
            if (mine == null || _storage == null) return;
            _legs.Add(new RouteLeg { a = mine, b = _storage, clear = railSeparation * 0.5f + routeClearance });
        }

        /// <summary>
        /// Replaces the provisional rail corridors with the real ones, once the arrival bays are known.
        ///
        /// Until the bays are handed out a corridor can only guess where its line will end up, so it
        /// reserves a wide approximate band. That band is both too wide along most of the run and — with
        /// four mines fanning out to ±16 m at the shed — too narrow at the end that matters. Re-planned
        /// against the actual centreline, a corridor is exactly as wide as the ballast plus its margin.
        /// </summary>
        private void ReplanRails()
        {
            for (int i = _legs.Count - 1; i >= 0; i--) if (_legs[i].roadName == null) _legs.RemoveAt(i);

            Transform[] mines = { _mountain, _ghostMine2, _ghostMine, _mine4 };
            for (int i = 0; i < mines.Length; i++)
            {
                if (mines[i] == null) continue;
                _legs.Add(new RouteLeg
                {
                    a = mines[i], b = _storage, bOffset = RailBay(mines[i]),
                    clear = 2.1f + routeClearance   // 2.1 is the ballast half-width RouteMesh.Rail lays
                });
            }
        }

        /// <summary>
        /// The haul roads, which are the truck routes made visible — so this list has to stay in step with
        /// <see cref="BuildRoadLoops"/>, or trucks drive on bare ground again. The two yard spurs are not
        /// driven by anything; they are there so a pile reads as belonging to the building beside it.
        /// </summary>
        private void PlanRoads()
        {
            AddRoadLeg("OpRoad_Haul1", _storage, _refinery);
            AddRoadLeg("OpRoad_Haul2", _refinery, _market);
            // Short driveways from each yard to its junction on the road — the trucks pull off, load,
            // and rejoin, and the main carriageway stays a clean ribbon.
            AddRoadLeg("OpRoad_OreDrive", _orePile, _viaOre);
            AddRoadLeg("OpRoad_BarDrive", _refinedPile, _viaBar);
        }

        /// <summary>Reserves a road leg and marks it to be drawn by <see cref="BuildSiteDressing"/>.</summary>
        private void AddRoadLeg(string name, Transform from, Transform to)
        {
            if (from == null || to == null) return;
            _legs.Add(new RouteLeg { a = from, b = to, clear = roadWidth * 0.5f + routeClearance, roadName = name });
        }

        /// <summary>
        /// True if a point lies inside any planned corridor. Legs that end on <paramref name="self"/> are
        /// skipped: a yard is supposed to sit on its own spur, and the shed at the end of the rail line.
        /// </summary>
        private bool OnLeg(Vector3 p, float radius, Transform self)
        {
            for (int i = 0; i < _legs.Count; i++)
            {
                RouteLeg leg = _legs[i];
                if (leg.a == null || leg.b == null || leg.a == self || leg.b == self) continue;
                Vector3 s = leg.A, d = leg.B - s;
                float len = d.magnitude;
                if (len < 0.01f) continue;
                d /= len;
                float t = Mathf.Clamp(Vector3.Dot(Flat(p) - s, d), 0f, len);
                float need = leg.clear + radius;
                if (SqrXZ(s + d * t, p) < need * need) return true;
            }
            return false;
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
            // Expansions live on the opposite flank from the yards. The side comes from ArrangeChain's
            // stored decision — re-deriving it by projecting yard-minus-storage onto the straight chord
            // flips sign on large bent islands, because both landmarks sit on a tilted arm.
            Vector3 free = side * -_yardSign;

            // The two that bring their own routes go first, and register those routes as they land, so
            // the other four are placed knowing where the fourth rail line and the export road will run.
            // A fourth mine is another mine: clone the real one rather than inventing a lookalike. It
            // takes the next slot in the back row, exactly - the shove walk must not touch it, because
            // two mountains legitimately stand closer than the walk's clearance would ever allow.
            Vector3 mine4Want = _mineRow.sqrMagnitude > 0.01f ? NextRowSlot()
                                                              : _mountain.position + free * 30f;
            Expansion("ghostx_mine4", _mountain != null ? _mountain.gameObject : null, mine4Want, true, true);
            AddRailLeg(Child(_islandRoot, "ghostx_mine4"));

            // The dock joins the authored port pier when the island has one — the harbour becomes one
            // working cluster — and falls back to the old spot past the market when it does not.
            Transform authoredPort = null;
            foreach (Transform t in _islandRoot) if (t.name.StartsWith("port_")) { authoredPort = t; break; }
            bool dockAtPort = authoredPort != null && SqrXZ(authoredPort.position, _market.position) < 90f * 90f;
            Vector3 dockWant = dockAtPort
                ? authoredPort.position + Flat(_market.position - authoredPort.position).normalized
                                          * (FootprintRadius(WorldBounds(authoredPort)) + 9f)
                : _market.position + chain * 20f - free * 6f;
            Expansion("ghostx_dock", dockPrefab, dockWant, false, dockAtPort);
            Transform dock = Child(_islandRoot, "ghostx_dock");
            if (dock != null)
            {
                var bend = new GameObject("OpBend_Export");
                bend.transform.SetParent(_islandRoot, true);
                // Far enough off the market for a full road width to pass outside its wall. "Op" prefixed
                // so the placement passes skip it — it is a waypoint, not a building.
                Bounds mb = WorldBounds(_market);
                // Wide swing: the bend point itself clears the market, but the bend-to-dock SEGMENT can
                // still cut the corner when the dock sits at an angle - so the swing takes the full
                // half-diagonal plus well over a road-width of margin.
                float clear = FootprintRadius(mb) + roadWidth * 1.5f;
                Vector3 pos = _market.position - free * clear;
                bend.transform.position = new Vector3(pos.x, _deckY, pos.z);
                _exportBend = bend.transform;

                AddRoadLeg("OpRoad_ExportA", _refinedPile, _exportBend);
                AddRoadLeg("OpRoad_ExportB", _exportBend, dock);
            }

            // Down-chain of the shed, not level with it: every rail line converges on storage from the
            // mine side, so the ground straight out to the shed's flank is the one place a building is
            // guaranteed to foul the track.
            Expansion("ghostx_warehouse", warehousePrefab, _storage.position + free * 14f + chain * 10f);
            // The depot and the shaft are the two that start life closest to a mine, and the mines are by
            // far the widest things on the island — 22 m across on some. Offsets that clear a refinery
            // leave these two inside the mountain, so both start further out.
            Expansion("ghostx_depot", depotPrefab, Vector3.Lerp(_mountain.position, _storage.position, 0.55f) + free * 27f);
            Expansion("ghostx_power", powerPrefab, _refinery.position + free * 16f);
            // Second warehouse and a market-side depot ride along on the same unlocks (the registry
            // matches by name prefix), so WAREHOUSE builds a complex and TRAIN DEPOT gains an out-station
            // — more future on the map for the same purchases.
            Expansion("ghostx_warehouse2", warehousePrefab, _storage.position + free * 26f + chain * 18f);
            Expansion("ghostx_depot2", depotPrefab, _market.position + free * 24f - chain * 12f);
            // The shaft is another mine mouth, so it joins the row like the mines do - the whole
            // mining district in one line, everything below it working ground.
            Vector3 shaftWant = _mineRow.sqrMagnitude > 0.01f ? NextRowSlot()
                                                              : _mountain.position + free * 28f;
            Expansion("ghostx_shaft", shaftPrefab, shaftWant);
        }

        /// <summary>
        /// Drops one expansion building, unless the island already authors it (some do) or no prefab is
        /// wired. <paramref name="asClone"/> copies an in-scene object instead of instantiating an asset,
        /// which is how the fourth mine reuses the island's own mine model at its own scale.
        /// </summary>
        private void Expansion(string name, GameObject prefab, Vector3 want, bool asClone = false,
                               bool fixedSpot = false)
        {
            if (prefab == null || Child(_islandRoot, name) != null) return;

            var go = Instantiate(prefab, _islandRoot);
            go.name = name;
            if (asClone) StripOpChildren(go.transform);
            else go.transform.localScale = Vector3.one * expansionScale;

            // Walk it outward from the island centre until it stops overlapping anything already placed.
            // The building's own footprint goes into the clearance, so a wide one gets out of the way by
            // its own width rather than by whatever a fixed radius happened to be.
            Vector3 outward = Flat(want - _islandRoot.position);
            outward = outward.sqrMagnitude < 0.01f ? Vector3.forward : outward.normalized;
            Bounds own = WorldBounds(go.transform);
            float half = FootprintRadius(own);
            // Lean the shove outward as well, so a building that has a choice ends up clear of the working
            // chain rather than tucked in behind it. Designed positions skip the walk entirely.
            Vector3 pos = fixedSpot ? want
                                    : ShoveClear(want, half, expansionGap, go.transform, outward * 0.4f, false);

            go.transform.position = new Vector3(pos.x, _deckY, pos.z);
            go.transform.rotation = Quaternion.LookRotation(Flat(_islandRoot.position - pos).normalized, Vector3.up);
        }

        /// <summary>
        /// The direction to shove a building of half-width <paramref name="half"/> standing at
        /// <paramref name="p"/> so it stops overlapping whatever it is currently in, or zero if the spot
        /// is already clear. <paramref name="gap"/> is the clear ground wanted between two footprints;
        /// route corridors carry their own margin instead, so they do not also charge the gap.
        ///
        /// The direction matters as much as the test. Shoving radially away from the island centre —
        /// which is what this used to do — walked a building along a line that mostly does not lead out
        /// of the thing it is stuck in, so it ran out of island and gave up still overlapping. Away from
        /// the obstruction itself clears the same overlap in a few metres and stays inland.
        /// </summary>
        private Vector3 PushOut(Vector3 p, float half, float gap, Transform ignore, bool legsOnly)
        {
            Vector3 best = Vector3.zero;
            float worst = 0f;

            foreach (Transform t in _islandRoot)
            {
                if (legsOnly) break;
                if (t == ignore || t.name.StartsWith("Dressing") || t.name.StartsWith("Op")) continue;
                // The island and its lagoon are the ground everything stands on, not obstacles on it.
                if (t.name.StartsWith("isle_") || t.name.StartsWith("lagoon_")) continue;
                if (t.GetComponentsInChildren<Renderer>(true).Length == 0) continue;
                Bounds b = WorldBounds(t);
                if (b.size.y < 1.5f) continue;                              // flat pads: fine to stand near
                if (Mathf.Max(b.size.x, b.size.z) < 6f) continue;           // props and trees: not obstacles

                // Measured against the neighbour's own footprint, not its pivot: a flat radius let a 4 m
                // shaft sit 13 m from the centre of a 22 m mine — which is to say, inside it.
                float need = gap + half + FootprintRadius(b);
                Vector3 away = Flat(p - b.center);
                float pen = need - away.magnitude;
                if (pen <= worst) continue;
                worst = pen;
                best = away.sqrMagnitude < 0.01f ? Vector3.right : away.normalized;
            }

            for (int i = 0; i < _legs.Count; i++)
            {
                RouteLeg leg = _legs[i];
                if (leg.a == null || leg.b == null || leg.a == ignore || leg.b == ignore) continue;
                Vector3 s = leg.A, d = leg.B - s;
                float len = d.magnitude;
                if (len < 0.01f) continue;
                d /= len;
                float t = Mathf.Clamp(Vector3.Dot(Flat(p) - s, d), 0f, len);
                Vector3 away = Flat(p) - (s + d * t);
                float pen = leg.clear + half - away.magnitude;
                if (pen <= worst) continue;
                worst = pen;
                // Sitting exactly on the centreline gives no direction to run: step off sideways.
                best = away.sqrMagnitude < 0.01f ? new Vector3(-d.z, 0f, d.x) : away.normalized;
            }
            return best;
        }

        /// <summary>
        /// Walks a building out of whatever it is standing in, and returns where it ended up.
        ///
        /// The walk is not a straight line, because a straight line runs out of island. When a step would
        /// leave the land the direction is fanned sideways until one is found that stays inland, so a
        /// building pinned between an obstruction and the shore bends along the coast rather than
        /// stopping dead. Without the fan it stopped dead — which is what left the fourth mine standing
        /// inside the third one on five of the eight islands.
        /// </summary>
        private Vector3 ShoveClear(Vector3 start, float half, float gap, Transform self, Vector3 bias, bool legsOnly)
        {
            Vector3 pos = start;
            for (int guard = 0; guard < 20; guard++)
            {
                Vector3 push = PushOut(pos, half, gap, self, legsOnly);
                if (push == Vector3.zero) return pos;
                Vector3 leaned = push + bias;
                if (leaned.sqrMagnitude > 0.01f) push = leaned.normalized;

                bool moved = false;
                for (int fan = 0; fan < 7 && !moved; fan++)
                {
                    // Straight out first, then ±30°, ±60°, ±90°.
                    float deg = fan == 0 ? 0f : (fan + 1) / 2 * 30f * (fan % 2 == 1 ? 1f : -1f);
                    Vector3 next = pos + (fan == 0 ? push : Quaternion.Euler(0f, deg, 0f) * push) * 4f;
                    if (!OnLand(next)) continue;
                    pos = next;
                    moved = true;
                }
                if (!moved) return pos;   // boxed in against the shore: crowded on grass beats tidy at sea
            }
            return pos;
        }

        /// <summary>
        /// The last word on placement: shoves every station and expansion that is not a route endpoint
        /// out of the other buildings and off the roads and rail corridors.
        ///
        /// The authored islands ship with some of this already fouled — on every one of them the second
        /// refinery stands part-way inside the second mine — and a generated route then runs through
        /// whatever is left in the way. Two things are deliberately exempt. Route endpoints, because
        /// moving one moves its route with it and the whole chain would chase itself around the island;
        /// and scenery, because the trees and rocks are meant to sit wherever the artist put them.
        /// </summary>
        private void TidySite()
        {
            // The whole designed skeleton is pinned: the chain is the spine, and the mines are the row
            // the rail bays derive from. Two mountains legitimately stand closer together than the
            // footprint test would ever allow, so letting the settle rounds touch them dragged the row
            // apart and the rails off their bays. Only expansions, ghost stations and scenery move.
            Transform[] anchors = { _mountain, _storage, _refinery, _market, _orePile, _refinedPile,
                                    _ghostMine, _ghostMine2, _mine4, _dock };

            // Several rounds, because shoving one building clear can push it into another that has
            // already been settled. Converges in two or three on every island; the early-out means an
            // island that was never fouled costs one pass.
            for (int round = 0; round < 8; round++)
            {
                bool moved = false;
                foreach (Transform t in _islandRoot)
                {
                    if (t.name.StartsWith("Dressing") || t.name.StartsWith("Op")) continue;
                    if (t.name.StartsWith("isle_") || t.name.StartsWith("lagoon_")) continue;
                    bool anchor = false;
                    for (int i = 0; i < anchors.Length; i++) if (anchors[i] == t) { anchor = true; break; }
                    if (anchor) continue;

                    Bounds b = WorldBounds(t);
                    float half = FootprintRadius(b);
                    if (b.size.y < 1.5f || half < 3f) continue;

                    // A station already standing off the land can never shove its way back, because every
                    // outward step is the one thing the walk refuses to take. Pull it ashore first. A
                    // couple of the authored islands park a locked station out past their own coastline.
                    if (t.name.StartsWith("ghost") && !OnLand(b.center))
                    {
                        Vector3 inward = Flat(_landCentre - b.center);
                        if (inward.sqrMagnitude > 0.01f)
                        {
                            inward.Normalize();
                            Vector3 ashore = b.center;
                            for (int g = 0; g < 20 && !OnLand(ashore); g++) ashore += inward * 4f;
                            t.position += Flat(ashore - b.center);
                            b = WorldBounds(t);
                            moved = true;
                        }
                    }

                    // Stations and expansions get pulled out of everything. Scenery is only moved when it
                    // stands in a road or a rail corridor — a palm in the middle of the haul road reads as
                    // badly as a building would, but a palm merely near one is the composition, not a bug.
                    bool station = t.name.StartsWith("ghost");
                    Vector3 delta = Flat(ShoveClear(b.center, half, tidyGap, t, Vector3.zero, !station) - b.center);
                    if (delta.sqrMagnitude < 0.25f) continue;
                    t.position += delta;   // keep whatever offset the pivot has from the mesh
                    moved = true;
                }
                if (!moved) break;
            }
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

            // Straight off the route plan, so the tarmac lands exactly where the trucks drive and exactly
            // where the buildings were told to keep clear. Anything that moved during TidySite is picked
            // up here too, because a leg reads its endpoints' positions now rather than when it was planned.
            for (int i = 0; i < _legs.Count; i++)
                if (_legs[i].roadName != null) HaulRoad(_legs[i], roadY, road, line);

            // Discs of the same tarmac fill every corner and junction — two straight legs meeting at an
            // angle otherwise leave a notch at the joint.
            if (_exportBend != null)
                RouteMesh.Pad(_dressing, "OpJunction_Export", _exportBend.position, roadWidth * 0.62f, roadY + 0.005f, road);
            JunctionDisc(_viaOre, road, roadY);
            JunctionDisc(_viaBar, road, roadY);

            LayRail(_train1, "1", ballast, sleeper, steel);
            LayRail(_train2, "2", ballast, sleeper, steel);
            LayRail(_train3, "3", ballast, sleeper, steel);
            LayRail(_train4, "4", ballast, sleeper, steel);

            SitePad(_mountain, apron); SitePad(_storage, apron);
            SitePad(_refinery, apron); SitePad(_market, apron);

            // The authored yard slabs are near-white, which reads as blank paper whenever a yard happens
            // to be empty. Gravel-toned, an empty yard reads as a yard.
            Retint(_orePile, apron); Retint(_refinedPile, apron);
            // Each yard gets its own working apron — a wide pad that reads as "this is the depot area",
            // now that the piles live beside the road instead of on it.
            YardApron(_orePile, apron);
            YardApron(_refinedPile, apron);

            BuildRidge();
            BuildHarbor();
            ScatterProps();
        }

        private void JunctionDisc(Transform at, Material road, float roadY)
        {
            if (at == null) return;
            RouteMesh.Pad(_dressing, "OpJunction_" + at.name, at.position, roadWidth * 0.62f, roadY + 0.004f, road);
        }

        private void YardApron(Transform yard, Material mat)
        {
            if (yard == null) return;
            Bounds b = WorldBounds(yard);
            RouteMesh.Pad(_dressing, "OpYard_" + yard.name, b.center,
                          Mathf.Max(b.extents.x, b.extents.z) * 1.5f + 2f, _deckY + 0.035f, mat);
        }

        /// <summary>
        /// The harbour: authored ships come alive and shuttle between the pier and a floating trade post
        /// anchored offshore — the island sells to the sea, visibly. Islands without authored ships
        /// simply skip all of it.
        /// </summary>
        private void BuildHarbor()
        {
            var found = new List<Transform>();
            foreach (Transform t in _islandRoot) if (t.name.StartsWith("ship")) found.Add(t);
            if (found.Count == 0) return;
            _waterY = found[0].position.y;

            Transform port = null;
            foreach (Transform t in _islandRoot) if (t.name.StartsWith("port_")) { port = t; break; }
            Vector3 coast = port != null ? port.position : (_dock != null ? _dock.position : _market.position);
            Vector3 seaward = Flat(coast - _landCentre);
            seaward = seaward.sqrMagnitude < 0.01f ? Vector3.forward : seaward.normalized;

            Vector3 raft = Flat(coast) + seaward * seaTradeDistance;
            raft.y = _waterY;
            BuildSeaMarket(raft, seaward);

            Vector3 pier = Flat(coast) + seaward * 9f;
            pier.y = _waterY;
            Vector3 lat = new Vector3(-seaward.z, 0f, seaward.x);
            for (int i = 0; i < found.Count && i < 3; i++)
            {
                Vector3 off = lat * ((i - 1) * 9f);
                var sh = new Ship
                {
                    t = found[i],
                    pier = pier + off,
                    sea = raft - seaward * 8f + off,
                    prog = (i * 0.37f) % 1f,
                    dwell = 0f,
                    phase = i * 2.1f,
                    toSea = i % 2 == 0,
                };
                sh.t.position = Vector3.Lerp(sh.toSea ? sh.pier : sh.sea, sh.toSea ? sh.sea : sh.pier, sh.prog);
                _ships.Add(sh);
            }
        }

        /// <summary>A wooden trading raft out at sea: deck, posts, hut, mast, and a lane of buoys.</summary>
        private void BuildSeaMarket(Vector3 at, Vector3 facing)
        {
            var mb = new BoxMeshBuilder();
            Vector3 f = facing, r = new Vector3(-facing.z, 0f, facing.x), up = Vector3.up;
            mb.AddBox(at + up * 0.25f, r, up, f, new Vector3(6.5f, 0.35f, 5f), 0);            // deck
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    mb.AddBox(at + r * (sx * 5.8f) + f * (sz * 4.3f) - up * 0.7f, r, up, f,
                              new Vector3(0.35f, 1.5f, 0.35f), 1);                            // piles
            mb.AddBox(at + up * 1.7f - f * 2.2f, r, up, f, new Vector3(2.2f, 1.1f, 1.6f), 1); // hut
            mb.AddBox(at + up * 3.1f - f * 2.2f, r, up, f, new Vector3(2.7f, 0.18f, 2.1f), 2); // roof
            mb.AddBox(at + up * 0.95f + r * 3.2f + f * 2.3f, r, up, f, new Vector3(0.8f, 0.5f, 0.8f), 2);  // crate
            mb.AddBox(at + up * 3.1f + r * 5.4f + f * 3.9f, r, up, f, new Vector3(0.12f, 2.7f, 0.12f), 1); // mast
            mb.AddBox(at + up * 5.2f + r * 4.8f + f * 3.9f, r, up, f, new Vector3(0.7f, 0.4f, 0.06f), 2);  // flag
            for (int i = 0; i < 4; i++)   // buoys marking the shipping lane back to the pier
            {
                Vector3 bp = at - f * (10f + i * 8f) + r * ((i % 2 == 0 ? 1f : -1f) * 7.5f);
                mb.AddBox(bp + up * 0.3f, r, up, f, new Vector3(0.5f, 0.5f, 0.5f), 2);
            }

            var go = new GameObject("OpSeaMarket");
            go.transform.SetParent(_dressing, true);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var mesh = new Mesh { name = "OpSeaMarket" };
            mb.Apply(mesh);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[]
            {
                MakeMat(_srcMat, new Color(0.55f, 0.42f, 0.28f)),   // deck planks
                MakeMat(_srcMat, new Color(0.38f, 0.28f, 0.19f)),   // dark timber
                MakeMat(_srcMat, new Color(0.82f, 0.28f, 0.24f)),   // roof, flag, buoys
            };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Advances the harbour ships: sail, dwell, turn, with a slow bob so they sit in water.</summary>
        private void TickShips(float dt)
        {
            for (int i = 0; i < _ships.Count; i++)
            {
                Ship sh = _ships[i];
                if (sh.t == null) continue;
                Vector3 from = sh.toSea ? sh.pier : sh.sea;
                Vector3 to = sh.toSea ? sh.sea : sh.pier;
                if (sh.dwell > 0f) sh.dwell -= dt;
                else
                {
                    sh.prog += dt * shipSpeed / Mathf.Max(1f, Vector3.Distance(from, to));
                    if (sh.prog >= 1f)
                    {
                        sh.prog = 0f; sh.toSea = !sh.toSea; sh.dwell = 5f;
                        // Re-read the endpoints, or the Lerp below evaluates against the OLD leg at
                        // prog 0 and the ship pops back across the whole crossing for one frame.
                        from = sh.toSea ? sh.pier : sh.sea;
                        to = sh.toSea ? sh.sea : sh.pier;
                    }
                }
                Vector3 p = Vector3.Lerp(from, to, sh.prog);
                p.y = _waterY + Mathf.Sin(Time.time * 1.1f + sh.phase) * 0.15f;
                Vector3 head = Flat(to - from);
                if (sh.dwell <= 0f && head.sqrMagnitude > 0.01f)
                    sh.t.rotation = Quaternion.Slerp(sh.t.rotation,
                        Quaternion.LookRotation(head.normalized, Vector3.up) * Quaternion.Euler(0f, shipYawOffset, 0f),
                        dt * 2f);
                sh.t.position = p;
                _ships[i] = sh;
            }
        }

        /// <summary>
        /// Clones the island's own scenery — its trees, rocks and bushes — across the empty grass, off
        /// every road, rail and building. Deterministic golden-angle placement, so no RNG state and the
        /// same island always fills in the same way. Each island scatters its own flora, which keeps the
        /// theme without any per-island asset wiring.
        /// </summary>
        private void ScatterProps()
        {
            if (scatterProps <= 0) return;
            var templates = new List<Transform>();
            foreach (Transform t in _islandRoot)
            {
                string n = t.name.ToLowerInvariant();
                if (!(n.StartsWith("tree") || n.StartsWith("palm") || n.StartsWith("bush") ||
                      n.StartsWith("rock") || n.StartsWith("stone") || n.StartsWith("grass") ||
                      n.StartsWith("plant") || n.StartsWith("flower") || n.StartsWith("crate") ||
                      n.StartsWith("barrel") || n.StartsWith("cactus") || n.StartsWith("mushroom"))) continue;
                if (t.GetComponentsInChildren<Renderer>(true).Length == 0) continue;
                Bounds tb = WorldBounds(t);
                if (Mathf.Max(tb.size.x, tb.size.z) > 9f) continue;
                templates.Add(t);
            }
            if (templates.Count == 0) return;

            int placed = 0;
            for (int i = 0; i < scatterProps * 4 && placed < scatterProps; i++)
            {
                float ang = i * 2.399963f;                       // golden angle fills the disc evenly
                float u = Mathf.Sqrt((i * 29 % 97) / 97f);
                Vector3 pos = _landCentre + new Vector3(Mathf.Cos(ang) * _landHalfX * 0.85f * u, 0f,
                                                        Mathf.Sin(ang) * _landHalfZ * 0.85f * u);
                if (OnLeg(pos, 5f, null)) continue;              // never in a road or under a rail line
                if (PushOut(pos, 2.5f, 2f, null, false) != Vector3.zero) continue;   // never in a building

                Transform tpl = templates[i * 7 % templates.Count];
                var prop = Instantiate(tpl.gameObject, _dressing);
                prop.name = "Dressing_Prop" + placed;
                prop.transform.position = new Vector3(pos.x, tpl.position.y, pos.z);
                prop.transform.rotation = Quaternion.Euler(0f, i * 77f, 0f);
                prop.transform.localScale = tpl.localScale * (0.75f + i * 13 % 5 * 0.11f);
                placed++;
            }
        }

        /// <summary>
        /// Lays one leg of the haul road, stopping it at each endpoint's wall rather than its pivot.
        /// Only ends that finish in the open get an overrun for the truck turnaround.
        /// </summary>
        private void HaulRoad(RouteLeg leg, float roadY, Material road, Material line)
        {
            Transform from = leg.a, to = leg.b;
            if (from == null || to == null) return;
            Vector3 dir = Flat(to.position - from.position);
            if (dir.sqrMagnitude < 0.01f) return;
            dir.Normalize();
            float insetA = StopInset(from, dir), insetB = StopInset(to, dir);
            Vector3 a = from.position + dir * insetA, b = to.position - dir * insetB;
            a.y = b.y = _deckY;
            if (Flat(b - a).magnitude < 1f) return;   // buildings too close to fit a road between them
            RouteMesh.Road(_dressing, leg.roadName, a, b, roadWidth, roadY,
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
            a.track = RouteMesh.Rail(_dressing, "OpRail_" + id, head, tail, _deckY, a.engineY, ballast, sleeper, steel);
            if (portalPrefab == null || a.mountain == null) return;

            Vector3 dir = Flat(tail - head).normalized;
            // The mouth belongs on the mine's downhill face, not on its pivot. The engine still spawns at
            // the pivot, deep inside the building where the mesh hides it, so the first thing the player
            // sees of a departing train is it coming out of the tunnel.
            Bounds mb = WorldBounds(a.mountain);
            float face = Mathf.Abs(dir.x) * mb.extents.x + Mathf.Abs(dir.z) * mb.extents.z;
            // 0.75, not 1.0: the full face distance is the mountain's base edge, and the portal prefab
            // is itself a chunk of pale rock — parked fully outside the slope it reads as a fifth white
            // peak, worst on the small ghost mines. Sunk a quarter in, only the doorway shows.
            Vector3 mouth = mb.center + dir * (face * 0.66f);

            var p = Instantiate(portalPrefab, _dressing);
            p.name = "OpPortal_" + id;
            p.transform.SetPositionAndRotation(new Vector3(mouth.x, _deckY, mouth.z),
                                               Quaternion.LookRotation(dir, Vector3.up));
            // Sized to the mountain it is cut into, not one fixed number: at the full scale the pale
            // portal rock is wider than a whole ghost mine and swallowed it — a white peak with a door.
            p.transform.localScale = Vector3.one * (portalScale * Mathf.Clamp(face / 12f, 0.45f, 1f));
            a.portal = p;
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
            Vector3 back = Flat(_mountain.position - _market.position).normalized;
            Vector3 row = _mineRow.sqrMagnitude > 0.01f ? _mineRow : new Vector3(-back.z, 0f, back.x);
            Transform[] mines = { _mountain, _ghostMine, _ghostMine2, _mine4 };

            // The rocks are the same mesh as the mines, so scale is what separates scenery from
            // station: at 2.2 they read as six more mines nobody can use. Kept visibly smaller and a
            // step behind the row, they read as the range the mines are dug into.
            for (int i = 0; i < ridgeRocks; i++)
            {
                float t = ridgeRocks == 1 ? 0.5f : i / (float)(ridgeRocks - 1);
                float slotPos = -2.75f + t * 5.5f;   // just past each end of the widest possible row
                Vector3 pos = _mountain.position + row * (slotPos * mineRowSpacing)
                                                + back * (ridgeDistance + (i % 2) * 9f);
                // Walk a rock back until it clears every mine head - a rock dropped on one would bury
                // the building the player is about to unlock.
                for (int guard = 0; guard < 10 && NearAny(mines, pos, ridgeClearance); guard++)
                    pos += back * 5f;

                var r = Instantiate(ridgeRockPrefab, _dressing);
                r.name = "Dressing_Rock" + i;
                r.transform.position = new Vector3(pos.x, _deckY - 2.5f, pos.z);
                r.transform.rotation = Quaternion.Euler(0f, i * 137f, 0f);
                r.transform.localScale = Vector3.one * (ridgeScale * (0.85f + 0.05f * (i * 37 % 7)));
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
            // Same flank decision ArrangeChain made — deriving it from the chord flips on bent islands.
            Vector3 pathOff = kerb * (-_yardSign * (roadWidth * 0.5f + 3f));

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
            TickShips(dt);
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
