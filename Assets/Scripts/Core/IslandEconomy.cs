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

        // -------------------------------------------------------------- readouts
        //
        //  Everything above is what the SIMULATION reads. What follows is the same numbers
        //  aimed at the player: an upgrade card used to say "RICHNESS · Lv 12 · $1,847" and
        //  nothing else, so the only way to learn what a level of Richness was worth was to
        //  buy one and watch the trains. Every figure needed to answer that already existed
        //  here - it was just never shown.
        //
        //  There is exactly one rule about this section: it must never restate a formula
        //  from above. A card that computes its own preview is a card that will drift from
        //  the economy the first time a coefficient moves, and it would drift silently,
        //  because a wrong number on a card still looks like a number.

        /// <summary>
        /// How a readout's numbers are written on the card. Most units live in the label instead
        /// ("ore per trip", "bars held"), because a noun translates and an abbreviation does not;
        /// the two here are the ones no phrasing gets rid of, and the screen appends a localised
        /// suffix to them.
        /// </summary>
        public enum NumberShape { Whole, Tenth, Money, Times, Seconds, Speed }

        /// <summary>
        /// What one axis is actually worth, in the units the player watches on the island.
        /// <see cref="Now"/> is what they own; <see cref="Next"/> is what the level in the
        /// price button buys.
        /// </summary>
        public readonly struct AxisReadout
        {
            /// <summary>Localisation id for the words in front of the numbers ("Ore per trip").</summary>
            public readonly string Key;
            public readonly float Now, Next;
            public readonly NumberShape Shape;
            /// <summary>True on the three dwell axes, where the number the player wants is SMALLER.</summary>
            public readonly bool LowerIsBetter;

            public AxisReadout(string key, float now, float next, NumberShape shape, bool lowerIsBetter)
            {
                Key = key; Now = now; Next = next; Shape = shape; LowerIsBetter = lowerIsBetter;
            }
        }

        private readonly struct Meaning
        {
            public readonly string Key;
            public readonly NumberShape Shape;
            public readonly bool Lower;
            public Meaning(string key, NumberShape shape, bool lower = false)
            {
                Key = key; Shape = shape; Lower = lower;
            }
        }

        /// <summary>
        /// What each axis moves, laid out exactly like <see cref="Axes"/>. Two axes deliberately
        /// name the same figure: MINE → Richness and TRAIN → Capacity both end up as ore on a
        /// train, and pretending otherwise would have the player buy one expecting the other.
        /// </summary>
        private static readonly Meaning[][] Meanings =
        {
            new[] { new Meaning("tren_cevher", NumberShape.Tenth), new Meaning("maden_bekleme", NumberShape.Seconds, true) },
            new[] { new Meaning("tren_hiz", NumberShape.Speed), new Meaning("tren_cevher", NumberShape.Tenth) },
            new[] { new Meaning("depo_kapasite", NumberShape.Whole), new Meaning("depo_bekleme", NumberShape.Seconds, true) },
            new[] { new Meaning("kamyon_sayi", NumberShape.Whole), new Meaning("kamyon_hiz", NumberShape.Speed), new Meaning("kamyon_cevher", NumberShape.Tenth) },
            new[] { new Meaning("eritme_hiz", NumberShape.Tenth), new Meaning("kulce_kapasite", NumberShape.Whole) },
            new[] { new Meaning("kamyon_sayi", NumberShape.Whole), new Meaning("kamyon_hiz", NumberShape.Speed), new Meaning("kamyon_kulce", NumberShape.Tenth) },
            new[] { new Meaning("kulce_fiyat", NumberShape.Money), new Meaning("pazar_bekleme", NumberShape.Seconds, true) },
            new[] { new Meaning("guc_gelir", NumberShape.Times), new Meaning("guc_hiz", NumberShape.Times) },
        };

        /// <summary>
        /// The figure one axis moves, at the level the player owns and at the next one.
        ///
        /// The next-level number comes from bumping the level, reading the SAME property the
        /// simulation reads, and putting it back - not from a second copy of the formula. It
        /// costs one increment and one decrement on an int the caller already owns, which is
        /// why a preview can be honest about the terms that multiply across stations: a level
        /// of TRAIN → Capacity that happens to cross a phase boundary really does add two
        /// wagons, and the card really does show the jump.
        /// </summary>
        public AxisReadout Readout(int s, int a)
        {
            Meaning m = Meanings[s][a];
            float now = Stat(s, a);
            float next;
            // try/finally, not because Stat can throw today, but because of what happens if it ever
            // does: this array is the player's save. A level that went up and did not come back down
            // is a free upgrade written to disk by the screen that was only supposed to describe one.
            _lv[s][a]++;
            try { next = Stat(s, a); }
            finally { _lv[s][a]--; }
            return new AxisReadout(m.Key, now, next, m.Shape, m.Lower);
        }

        private float Stat(int s, int a)
        {
            switch (s)
            {
                case Mine:        return a == 0 ? TrainOre : MineDwell;
                case Train:       return a == 0 ? TrainSpeed : TrainOre;
                case Storage:     return a == 0 ? StorageFull : StorageDwell;
                case OreTrucks:   return a == 0 ? OreTruckCount : a == 1 ? OreTruckSpeed : OreTruckLoad;
                case Smelter:     return a == 0 ? SmeltRate : BarCap;
                case CargoTrucks: return a == 0 ? CargoTruckCount : a == 1 ? CargoTruckSpeed : CargoTruckLoad;
                case Market:      return a == 0 ? BarPrice : MarketDwell;
                default:          return a == 0 ? PowerIncome : PowerSpeed;
            }
        }

        /// <summary>
        /// The multiplier a one-time expansion applies, or 0 for the ones that buy a body
        /// rather than a number - a mine, a rail line, the power plant's upgrade track.
        ///
        /// This exists because the expansion cards used to carry a hand-written note, and the
        /// notes had gone stale: the shipping table still promised "2x smelt" and "+50% price"
        /// for what the tuning had long since settled at 1.25 and 1.20. A card that quotes the
        /// live tuning cannot lie to the player about what their money buys.
        /// </summary>
        public float UnlockBonus(int u)
        {
            switch (u)
            {
                case UnlockSecondSmelter: return _t.SecondSmelterBonus;
                case UnlockTradePost:     return _t.TradePostBonus;
                case UnlockWarehouse:     return _t.WarehouseBonus;
                case UnlockDepot:         return _t.DepotBonus;
                case UnlockDeepShaft:     return _t.DeepShaftBonus;
                default:                  return 0f;
            }
        }
    }
}
