using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>Multi-ore raw buffer between the trains and the ore trucks. Upgrading raises capacity.</summary>
    public sealed class StorageYard : MonoBehaviour, IUpgradable
    {
        [SerializeField] private StationConfig config;

        public int Level { get; private set; }
        public Inventory<ResourceDef> Ore { get; private set; }

        private void Awake() => Ore = new Inventory<ResourceDef>(new BigDouble(config.CapacityAtLevel(Level)));

        public string Label => config.DisplayName;
        public BigDouble UpgradeCost(EconomyService economy) => economy.UpgradeCost(config.BaseUpgradeCost, Level);
        public void ApplyLevel(int level) { Level = level; if (Ore != null) Ore.Capacity = new BigDouble(config.CapacityAtLevel(Level)); }
    }
}
