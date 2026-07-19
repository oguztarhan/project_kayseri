using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Extracts one specific <see cref="OreTier"/> into its output buffer each tick (auto trickle),
    /// plus a burst on tap (GDD §3). The train reads <see cref="Ore"/> to know what it's hauling.
    /// </summary>
    public sealed class MineStation : MonoBehaviour, IUpgradable
    {
        [SerializeField] private StationConfig config;
        [SerializeField] private OreTier ore;
        [SerializeField] private double tapAmount = 5d;

        public int Level { get; private set; }
        public ResourceBuffer Output { get; private set; }
        public OreTier Ore => ore;

        private GameClock _clock;

        private void Awake() => Output = new ResourceBuffer(new BigDouble(config.CapacityAtLevel(Level)));
        private void Start() { _clock = ServiceLocator.Get<GameClock>(); if (_clock != null) _clock.OnTick += OnTick; }
        private void OnDestroy() { if (_clock != null) _clock.OnTick -= OnTick; }
        private void OnTick() => Output.Add(new BigDouble(config.RateAtLevel(Level) * _clock.TickInterval));

        public void Tap() => Output.Add(new BigDouble(tapAmount));

        public string Label => config.DisplayName;
        public BigDouble UpgradeCost(EconomyService economy) => economy.UpgradeCost(config.BaseUpgradeCost, Level);
        public void ApplyLevel(int level) { Level = level; if (Output != null) Output.Capacity = new BigDouble(config.CapacityAtLevel(Level)); }
    }
}
