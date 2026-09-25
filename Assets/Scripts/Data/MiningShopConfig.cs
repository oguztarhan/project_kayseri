using Game.Core;
using UnityEngine;

namespace Game.Data
{
    [CreateAssetMenu(fileName = "MiningShop", menuName = "Ore Empire/Mining Shop")]
    public sealed class MiningShopConfig : ScriptableObject
    {
        [SerializeField, Min(0.1f)] private double _craftSeconds = 10d;
        [SerializeField, Min(1f)] private double _unitPrice = 20d;
        [SerializeField, Min(1)] private int _outputCapacity = 4;
        [SerializeField, Min(1)] private int _shelfCapacity = 4;
        [SerializeField, Min(1)] private int _carrierLoad = 2;
        [SerializeField, Min(1)] private int _queueCapacity = 6;
        [Tooltip("One-way route duration. Match view movement to this clock; measure the authored path.")]
        [SerializeField, Min(0.1f)] private double _travelSeconds = 4d;
        [SerializeField, Min(0.1f)] private double _handlingSeconds = 0.5d;
        [SerializeField, Min(0.1f)] private double _arrivalSeconds = 3d;
        [SerializeField, Min(0.1f)] private double _serviceSeconds = 2d;
        [SerializeField, Min(0.01f)] private double _speedPerLevel = 0.1d;
        [SerializeField, Min(0.01f)] private double _valuePerLevel = 0.15d;
        [SerializeField, Min(1f)] private double _upgradeBaseCost = 40d;
        [SerializeField, Min(1f)] private double _upgradeCostGrowth = 1.18d;
        [SerializeField, Min(1)] private int _maxUpgradeLevel = 21;

        [Header("Additional product tables")]
        [SerializeField, Min(0.1f)] private double _helmetCraftSeconds = 20d;
        [SerializeField, Min(1f)] private double _helmetUnitPrice = 240d;
        [SerializeField, Min(1f)] private double _helmetTableCost = 3000d;
        [SerializeField, Min(0.1f)] private double _lanternCraftSeconds = 40d;
        [SerializeField, Min(1f)] private double _lanternUnitPrice = 3200d;
        [SerializeField, Min(1f)] private double _lanternTableCost = 150000d;
        [SerializeField, Min(0.1f)] private double _bagCraftSeconds = 60d;
        [SerializeField, Min(1f)] private double _bagUnitPrice = 30000d;
        [SerializeField, Min(1f)] private double _bagTableCost = 6000000d;
        [Tooltip("The fewest items the shared carrier brings on a trip. Faster benches raise it (see the business model).")]
        [SerializeField, Min(1)] private int _businessCarrierLoad = 1;

        [Header("Bench mastery")]
        [Tooltip("What each bench's level 1 → 2 costs; every later level grows from it by the cost growth below.")]
        [SerializeField, Min(1f)] private double _pickaxeFirstLevelCost = 40d;
        [SerializeField, Min(1f)] private double _helmetFirstLevelCost = 400d;
        [SerializeField, Min(1f)] private double _lanternFirstLevelCost = 4000d;
        [SerializeField, Min(1f)] private double _bagFirstLevelCost = 40000d;
        [Tooltip("The level the previous bench must reach before the next one can be built.")]
        [SerializeField, Range(1, 100)] private int _buildRequiresLevel = 25;
        [SerializeField, Min(0.001f)] private double _masteryValuePerLevel = 0.10d;
        [SerializeField, Min(0.001f)] private double _masterySpeedPerLevel = 0.03d;
        [Tooltip("What one star multiplies its axis by (value at 10/50/100, speed at 25/75).")]
        [SerializeField, Min(1f)] private double _masteryStarMultiplier = 2d;
        [SerializeField, Min(1.001f)] private double _masteryCostGrowth = 1.14d;
        [Tooltip("Shortest cycle a bench is drawn at. Faster than this turns into value per item; income is unchanged.")]
        [SerializeField, Min(0.1f)] private double _masteryMinCycleSeconds = 2d;
        [Tooltip("Chance with no stars. Base plus five stars' worth must stay at or under 1.")]
        [SerializeField, Min(0f)] private double _perfectBaseChance = 0.04d;
        [SerializeField, Min(0f)] private double _perfectChancePerStar = 0.01d;
        [SerializeField, Min(1f)] private double _perfectMultiplier = 3d;
        [Tooltip("Gems each star pays once, stars 1-5. Counted by RewardBudgetTests.")]
        [SerializeField] private long[] _starGems = { 3L, 5L, 7L, 10L, 15L };
        [Tooltip("Tezgâhtaki ustanın, istasyondaki üretim bonusunun ne kadarını getirdiği. 0.5 = yarısı.")]
        [SerializeField, Min(0f)] private double _workerShare = 0.5d;

        public MiningShopSimulation.Tuning ToTuning()
        {
            var tuning = new MiningShopSimulation.Tuning
            {
                CraftSeconds = _craftSeconds, UnitPrice = _unitPrice, OutputCapacity = _outputCapacity,
                ShelfCapacity = _shelfCapacity, CarrierLoad = _carrierLoad, QueueCapacity = _queueCapacity,
                TravelSeconds = _travelSeconds, HandlingSeconds = _handlingSeconds, ArrivalSeconds = _arrivalSeconds,
                ServiceSeconds = _serviceSeconds, SpeedPerLevel = _speedPerLevel, ValuePerLevel = _valuePerLevel,
                UpgradeBaseCost = _upgradeBaseCost, UpgradeCostGrowth = _upgradeCostGrowth, MaxUpgradeLevel = _maxUpgradeLevel
            };
            tuning.Validate();
            return tuning;
        }

        /// <summary>Four-product business tuning. The existing pickaxe fields remain the source for table one.</summary>
        public MiningShopBusinessSimulation.Tuning ToBusinessTuning()
        {
            MiningShopSimulation.Tuning pickaxe = ToTuning();
            var tuning = new MiningShopBusinessSimulation.Tuning
            {
                Products = new[]
                {
                    new MiningShopBusinessSimulation.ProductTuning
                    {
                        ProductId = MiningShopCampaign.ProductIdAt(0), CraftSeconds = pickaxe.CraftSeconds,
                        UnitPrice = pickaxe.UnitPrice, TableCost = 0d, FirstLevelCost = _pickaxeFirstLevelCost
                    },
                    new MiningShopBusinessSimulation.ProductTuning
                    {
                        ProductId = MiningShopCampaign.ProductIdAt(1), CraftSeconds = _helmetCraftSeconds,
                        UnitPrice = _helmetUnitPrice, TableCost = _helmetTableCost, FirstLevelCost = _helmetFirstLevelCost
                    },
                    new MiningShopBusinessSimulation.ProductTuning
                    {
                        ProductId = MiningShopCampaign.ProductIdAt(2), CraftSeconds = _lanternCraftSeconds,
                        UnitPrice = _lanternUnitPrice, TableCost = _lanternTableCost, FirstLevelCost = _lanternFirstLevelCost
                    },
                    new MiningShopBusinessSimulation.ProductTuning
                    {
                        ProductId = MiningShopCampaign.ProductIdAt(3), CraftSeconds = _bagCraftSeconds,
                        UnitPrice = _bagUnitPrice, TableCost = _bagTableCost, FirstLevelCost = _bagFirstLevelCost
                    }
                },
                OutputCapacity = pickaxe.OutputCapacity,
                ShelfCapacityPerProduct = pickaxe.ShelfCapacity,
                CarrierLoad = _businessCarrierLoad,
                QueueCapacity = pickaxe.QueueCapacity,
                TravelSeconds = pickaxe.TravelSeconds,
                HandlingSeconds = pickaxe.HandlingSeconds,
                ArrivalSeconds = pickaxe.ArrivalSeconds,
                ServiceSeconds = pickaxe.ServiceSeconds,
                BuildRequiresLevel = _buildRequiresLevel,
                Mastery = ToMasteryTuning()
            };
            tuning.Validate();
            return tuning;
        }

        public BenchMastery.Tuning ToMasteryTuning()
        {
            var tuning = new BenchMastery.Tuning
            {
                ValuePerLevel = _masteryValuePerLevel, SpeedPerLevel = _masterySpeedPerLevel,
                StarMultiplier = _masteryStarMultiplier, CostGrowth = _masteryCostGrowth,
                MinCycleSeconds = _masteryMinCycleSeconds, PerfectBaseChance = _perfectBaseChance,
                PerfectChancePerStar = _perfectChancePerStar, PerfectMultiplier = _perfectMultiplier,
                StarGems = _starGems != null ? (long[])_starGems.Clone() : null,
                WorkerShare = _workerShare
            };
            tuning.Validate();
            return tuning;
        }
    }
}
