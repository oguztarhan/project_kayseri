using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>Raw-ore buffer between the train and the market. Upgrading raises its capacity.</summary>
    public sealed class StorageYard : MonoBehaviour, IUpgradable
    {
        [SerializeField] private StationConfig config;

        public int Level { get; private set; }
        public ResourceBuffer Buffer { get; private set; }

        private void Awake()
        {
            Buffer = new ResourceBuffer(new BigDouble(config.CapacityAtLevel(Level)));
        }

        public string Label => config.DisplayName;

        public BigDouble UpgradeCost(EconomyService economy) => economy.UpgradeCost(config.BaseUpgradeCost, Level);

        public void ApplyLevel(int level)
        {
            Level = level;
            if (Buffer != null) Buffer.Capacity = new BigDouble(config.CapacityAtLevel(Level));
        }
    }
}
