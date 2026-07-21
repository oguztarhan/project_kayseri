using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Multi-ore raw buffer between the trains and the ore trucks. Upgrade tracks: <b>Capacity</b> (buffer
    /// size) and <b>Loading</b> (ore trucks fetch more per trip via <see cref="LoadMultiplier"/>).
    /// </summary>
    public sealed class StorageYard : UpgradableStation
    {
        [SerializeField] private StationConfig config;

        private const int Cap = 0, Load = 1;
        private static readonly string[] TrackList = { "Capacity", "Loading" };

        public Inventory<ResourceDef> Ore { get; private set; }
        public double LoadMultiplier { get; private set; } = 1d;

        protected override string StationLabel => config.DisplayName;
        protected override string[] TrackNames => TrackList;
        protected override double TrackBaseCost(int track) => config.BaseUpgradeCost;

        private void Awake() { Ore = new Inventory<ResourceDef>(new BigDouble(config.CapacityAtLevel(0))); OnUpgraded(); }

        protected override void OnUpgraded()
        {
            if (Ore != null) Ore.Capacity = new BigDouble(config.CapacityAtLevel(TrackLevel(Cap)));
            LoadMultiplier = 1d + 0.25d * TrackLevel(Load);
        }
    }
}
