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
        [SerializeField, Min(1f)] private double _helmetUnitPrice = 60d;
        [SerializeField, Min(1f)] private double _helmetTableCost = 300d;
        [SerializeField, Min(0.1f)] private double _lanternCraftSeconds = 40d;
        [SerializeField, Min(1f)] private double _lanternUnitPrice = 150d;
        [SerializeField, Min(1f)] private double _lanternTableCost = 1400d;
        [SerializeField, Min(0.1f)] private double _bagCraftSeconds = 60d;
        [SerializeField, Min(1f)] private double _bagUnitPrice = 360d;
        [SerializeField, Min(1f)] private double _bagTableCost = 5000d;
        [Tooltip("Items one shared carrier brings on each trip. One keeps the four product types readable.")]
        [SerializeField, Min(1)] private int _businessCarrierLoad = 1;

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
                        UnitPrice = pickaxe.UnitPrice, TableCost = 0d
                    },
                    new MiningShopBusinessSimulation.ProductTuning
                    {
                        ProductId = MiningShopCampaign.ProductIdAt(1), CraftSeconds = _helmetCraftSeconds,
                        UnitPrice = _helmetUnitPrice, TableCost = _helmetTableCost
                    },
                    new MiningShopBusinessSimulation.ProductTuning
                    {
                        ProductId = MiningShopCampaign.ProductIdAt(2), CraftSeconds = _lanternCraftSeconds,
                        UnitPrice = _lanternUnitPrice, TableCost = _lanternTableCost
                    },
                    new MiningShopBusinessSimulation.ProductTuning
                    {
                        ProductId = MiningShopCampaign.ProductIdAt(3), CraftSeconds = _bagCraftSeconds,
                        UnitPrice = _bagUnitPrice, TableCost = _bagTableCost
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
                SpeedPerLevel = pickaxe.SpeedPerLevel,
                ValuePerLevel = pickaxe.ValuePerLevel,
                UpgradeBaseCost = pickaxe.UpgradeBaseCost,
                UpgradeCostGrowth = pickaxe.UpgradeCostGrowth,
                MaxUpgradeLevel = pickaxe.MaxUpgradeLevel
            };
            tuning.Validate();
            return tuning;
        }
    }
}
