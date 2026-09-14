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
    }
}
