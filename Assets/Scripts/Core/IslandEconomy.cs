using System;

namespace Game.Core
{
    /// <summary>
    /// The island economy as pure maths: what a level costs, and what it buys.
    ///
    /// This used to live inside <c>CoalOperation</c> as private properties reading
    /// serialized fields, which meant the only way to find out what a balance change
    /// did was to play the game and watch the cash counter. Pulled out here it can be
    /// driven by a simulator instead — a level vector in, a set of rates out — so the
    /// pacing curve is something we measure rather than something we hope for.
    ///
    /// It is deliberately NOT the whole economy. Income emerges from vehicles driving
    /// real routes: how long a train takes to reach the mine, whether the smelter
    /// stalls waiting on trucks. That part cannot be closed-form, and is measured by
    /// the editor probe instead. What lives here is everything that IS closed-form —
    /// the cost curve, and the multiplier each upgrade applies to a base rate.
    ///
    /// <see cref="Game.Gameplay.CoalOperation"/> holds one of these and delegates to
    /// it, sharing the same level arrays, so there is exactly one copy of every
    /// number.
    /// </summary>
    public sealed class IslandEconomy
    {
        // Station indices. Array positions - saved games address upgrades by number,
        // so these must never be reordered.
        public const int Mine = 0, Train = 1, Storage = 2, OreTrucks = 3,
                         Smelter = 4, CargoTrucks = 5, Market = 6, Power = 7;

        public const int UnlockSecondMine = 0, UnlockSecondSmelter = 1, UnlockTradePost = 2,
                         UnlockThirdMine = 3, UnlockWarehouse = 4, UnlockDepot = 5,
                         UnlockExportDock = 6, UnlockFourthMine = 7, UnlockPowerPlant = 8,
                         UnlockDeepShaft = 9;

        /// <summary>Rolling stock and road fleet the scene actually holds.</summary>
        public const int BaseWagons = 3, MaxWagons = 7, OreBaseTrucks = 2, CargoBaseTrucks = 1;

        public static readonly string[] Stations =
        { "MINE", "TRAIN", "STORAGE", "ORE TRUCKS", "SMELTER", "CARGO TRUCKS", "MARKET", "POWER PLANT" };

        public static readonly string[][] Axes =
        {
            new[] { "Richness", "Load Speed" },
            new[] { "Speed", "Capacity" },
            new[] { "Capacity", "Transfer Speed" },
            new[] { "Trucks", "Speed", "Capacity" },
            new[] { "Smelt Speed", "Bar Storage" },
            new[] { "Trucks", "Speed", "Capacity" },
            new[] { "Price", "Sell Speed" },
            new[] { "Generators", "Turbines" },
        };

        /// <summary>Level-1 prices, before the island's cost multiplier.</summary>
        public static readonly double[][] BaseCost =
        {
            new[] { 500d, 650d },
            new[] { 650d, 800d },
            new[] { 800d, 700d },
            new[] { 8000d, 550d, 700d },
            new[] { 1000d, 900d },
            new[] { 12000d, 700d, 750d },
            new[] { 1200d, 1000d },
            // POWER PLANT used to be 16000 / 12000 — by far the largest base costs in the
            // game, attached to by far the smallest coefficients. That made it 72% of an
            // island's entire upgrade bill for a 1.37x gain, and left MARKET → Price
            // returning 106x more income per dollar. It is the last station the player
            // meets, so the game ended on its worst purchase. Priced into the band now.
            new[] { 900d, 600d },
        };

        /// <summary>
        /// Per-axis hard caps; 0 means "stop at the island-wide cap instead". Only the two
        /// fleet-count axes are capped, because a truck is a body on the road rather than a
        /// number - past a point the map reads as a traffic jam.
        ///
        /// They were 2 and 2 (four ore trucks, three cargo). Three cargo trucks turned out
        /// to be the ceiling on the WHOLE chain: with POWER PLANT priced properly, a maxed
        /// island could produce far more than it could haul to market, so all ten ghost
        /// buildings together - three extra mines, a second smelter, a deep shaft - were
        /// worth 3% between them. They were all pushing on stages that were not the
        /// bottleneck.
        ///
        /// Not limited by the scene, despite what this comment used to claim:
        /// CoalOperation.BuildTruckAgents clones the template up to the cap, so the parked
        /// bodies the map exports are a starting point, not a ceiling.
        ///
        /// FIVE is the measured ceiling, and it is a cliff rather than a slope. Holding every
        /// other axis at level 8 and adding cargo trucks one at a time:
        ///
        ///     1        2        3        4        5   |    6        7
        ///     996    1991     2655     3983     4756  |  2438     2636   $/min
        ///
        /// Linear at about 1000 a truck up to five, then the sixth HALVES the island's
        /// income. Something structural gives way at six on a loop this length — trucks
        /// arriving at a yard that cannot serve them, most likely — and it is worth finding,
        /// because a fleet size that costs half the income is a trap for whoever raises this
        /// next. Until then both fleets stop at five bodies on the road.
        /// </summary>
        public static readonly int[][] MaxLevel =
        {
            new[] { 0, 0 },
            new[] { 0, 0 },
            new[] { 0, 0 },
            new[] { 3, 0, 0 },      // ORE TRUCKS   -> 2 base + 3 = 5 on the road
            new[] { 0, 0 },
            new[] { 4, 0, 0 },      // CARGO TRUCKS -> 1 base + 4 = 5, the leg that was the wall
            new[] { 0, 0 },
            new[] { 0, 0 },
        };

        /// <summary>
        /// How strongly each axis pulls on its rate. These are the shape of the
        /// economy: a level always adds <c>coefficient × scale</c> of the BASE value,
        /// so within one axis the gain is a straight line, but the terms multiply each
        /// other across stations - which is what makes the whole curve steep.
        /// </summary>
        public const float MineRichness = 0.25f, MineDwellC = 0.2f;
        public const float TrainSpeedC = 0.15f, TrainCapacity = 0.25f;
        public const float StorageCapacity = 0.5f, StorageDwellC = 0.2f;
        public const float OreSpeed = 0.15f, OreCapacity = 0.30f;
        public const float SmeltSpeed = 0.30f, BarStorage = 0.5f;
        public const float CargoSpeed = 0.15f, CargoCapacity = 0.30f;
        public const float MarketPrice = 0.40f, MarketDwellC = 0.2f;
        // Raised from 0.05 / 0.03 along with the price cut above. The two changes together
        // move both axes to 3.3e-4 multiplier per dollar — the middle of the band the other
        // fourteen axes occupy (2.0e-4 to 6.3e-4). It is gated behind its ghost building
        // and touches every other station, so a capstone worth saving for is the intent;
        // what it must not be is the worst buy in the game by two orders of magnitude.
        public const float PowerGenerators = 0.30f, PowerTurbines = 0.20f;

        /// <summary>Everything the designer sets per island, in one place.</summary>
        public struct Tuning
        {
            public float TrainSpeed, TruckSpeed, TrainOrePerTrip;
            public float OreTruckCapacity, CargoTruckCapacity;
            public float SmeltPerSecond, StorageFull, BarCapacity, BarPrice, DwellSeconds;
            public float AxisEffectScale;
            public double CostGrowth, CostMultiplier;
            public float ValueMultiplier;
            public int AxisLevelCap;
            public float SecondSmelterBonus, TradePostBonus, WarehouseBonus, DepotBonus, DeepShaftBonus;

            /// <summary>The values a level-0 coal island ships with.</summary>
            public static Tuning Default => new Tuning
            {
                TrainSpeed = 18f, TruckSpeed = 20f, TrainOrePerTrip = 12f,
                OreTruckCapacity = 6f, CargoTruckCapacity = 4f,
                SmeltPerSecond = 3f, StorageFull = 60f, BarCapacity = 40f,
                BarPrice = 45f, DwellSeconds = 0.7f,
                AxisEffectScale = 0.085f,
                CostGrowth = 1.06d, CostMultiplier = 1d,
                ValueMultiplier = 1f, AxisLevelCap = 50,
                SecondSmelterBonus = 1.25f, TradePostBonus = 1.20f,
                WarehouseBonus = 1.15f, DepotBonus = 1.10f, DeepShaftBonus = 1.12f,
            };
        }

        private readonly Tuning _t;
        private readonly int[][] _lv;
        private readonly bool[] _un;

        /// <summary>
        /// Shares the caller's arrays rather than copying them, so the operation and
        /// this object can never disagree about what the player owns.
        /// </summary>
        public IslandEconomy(Tuning tuning, int[][] levels, bool[] unlocked)
        {
            _t = tuning;
            _lv = levels ?? NewLevels();
            _un = unlocked ?? new bool[10];
        }

        /// <summary>A fresh level vector, with the row lengths <see cref="Axes"/> needs.</summary>
        public static int[][] NewLevels() => new[]
        {
            new int[2], new int[2], new int[2], new int[3],
            new int[2], new int[3], new int[2], new int[2],
        };

        public Tuning Config => _t;
        public int[][] Levels => _lv;
        public bool[] Unlocked => _un;

        // ------------------------------------------------------------------ cost
        public double AxisCost(int s, int a)
            => BaseCost[s][a] * _t.CostMultiplier * Math.Pow(_t.CostGrowth, _lv[s][a]);

        public int AxisCap(int s, int a)
            => MaxLevel[s][a] > 0 ? Math.Min(MaxLevel[s][a], _t.AxisLevelCap) : _t.AxisLevelCap;

        public bool AxisMaxed(int s, int a) => _lv[s][a] >= AxisCap(s, a);

        public int StationLevelTotal(int s)
        {
            int n = 0;
            for (int a = 0; a < _lv[s].Length; a++) n += _lv[s][a];
            return n;
        }

        public int StationLevelCap(int s)
        {
            int cap = 0;
            for (int a = 0; a < _lv[s].Length; a++) cap += AxisCap(s, a);
            return cap;
        }

        /// <summary>A station's phase: its total level against its own cap, in thirds.</summary>
        public int PhaseForStation(int s)
        {
            if (s < 0 || s >= Stations.Length) return 1;
            int cap = StationLevelCap(s);
            if (cap <= 0) return 1;
            int level = StationLevelTotal(s);
            float third = cap / 3f;
            if (level < third) return 1;
            if (level < third * 2f) return 2;
            return 3;
        }

        /// <summary>Total cash to take every axis from where it is now to its cap.</summary>
        public double CostToMax()
        {
            double total = 0d;
            for (int s = 0; s < _lv.Length; s++)
                for (int a = 0; a < _lv[s].Length; a++)
                {
                    int cap = AxisCap(s, a);
                    for (int l = _lv[s][a]; l < cap; l++)
                        total += BaseCost[s][a] * _t.CostMultiplier * Math.Pow(_t.CostGrowth, l);
                }
            return total;
        }

        // --------------------------------------------------------------- effects
        /// <summary>The shared shape: base × (1 + coefficient × scale × level).</summary>
        private float Ax(int s, int a, float coeff) => 1f + coeff * _t.AxisEffectScale * _lv[s][a];

        // POWER PLANT - the only station that touches everything else.
        public float PowerIncome => Ax(Power, 0, PowerGenerators);
        public float PowerSpeed => Ax(Power, 1, PowerTurbines);

        // MINE. Dwell values DIVIDE, so a higher level means a shorter pause.
        public float MineDwell => _t.DwellSeconds / Ax(Mine, 1, MineDwellC);
        public float TrainOre => _t.TrainOrePerTrip
            * Ax(Mine, 0, MineRichness)
            * (ActiveWagons / (float)BaseWagons)
            * Ax(Train, 1, TrainCapacity)
            * (_un[UnlockDeepShaft] ? _t.DeepShaftBonus : 1f);

        // TRAIN. The rake follows the station's own phase - 3, then 5, then 7.
        public float TrainSpeed => _t.TrainSpeed * Ax(Train, 0, TrainSpeedC)
            * (_un[UnlockDepot] ? _t.DepotBonus : 1f) * PowerSpeed;
        public int ActiveWagons => Math.Min(BaseWagons + (PhaseForStation(Train) - 1) * 2, MaxWagons);

        // STORAGE - the ore yard, and the size of the visible pile.
        public float StorageFull => _t.StorageFull * Ax(Storage, 0, StorageCapacity)
            * (_un[UnlockWarehouse] ? _t.WarehouseBonus : 1f);
        public float StorageDwell => _t.DwellSeconds / Ax(Storage, 1, StorageDwellC);

        // ORE TRUCKS - storage to smelter.
        public int OreTruckCount => OreBaseTrucks + _lv[OreTrucks][0];
        public float OreTruckSpeed => _t.TruckSpeed * Ax(OreTrucks, 1, OreSpeed) * PowerSpeed;
        public float OreTruckLoad => _t.OreTruckCapacity * Ax(OreTrucks, 2, OreCapacity);

        // SMELTER. If bar storage fills, smelting STOPS until cargo clears it.
        public float SmeltRate => _t.SmeltPerSecond * Ax(Smelter, 0, SmeltSpeed)
            * (_un[UnlockSecondSmelter] ? _t.SecondSmelterBonus : 1f);
        public float BarCap => _t.BarCapacity * Ax(Smelter, 1, BarStorage);

        // CARGO TRUCKS - smelter to market, or to the dock on the export route.
        public int CargoTruckCount => CargoBaseTrucks + _lv[CargoTrucks][0];
        public float CargoTruckSpeed => _t.TruckSpeed * Ax(CargoTrucks, 1, CargoSpeed) * PowerSpeed;
        public float CargoTruckLoad => _t.CargoTruckCapacity * Ax(CargoTrucks, 2, CargoCapacity);

        // MARKET - where cash is actually made.
        public float BarPrice => _t.BarPrice * _t.ValueMultiplier * Ax(Market, 0, MarketPrice)
            * (_un[UnlockTradePost] ? _t.TradePostBonus : 1f) * PowerIncome;
        public float MarketDwell => _t.DwellSeconds / Ax(Market, 1, MarketDwellC);
    }
}
