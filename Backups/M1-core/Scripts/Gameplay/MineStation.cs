using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Extracts raw ore into its output buffer on each sim tick (auto trickle), plus a burst when
    /// the player taps (GDD §3 — light tapping early, automation via managers later in M3).
    /// </summary>
    public sealed class MineStation : MonoBehaviour, IUpgradable
    {
        [SerializeField] private StationConfig config;
        [SerializeField] private double tapAmount = 5d;

        public int Level { get; private set; }
        public ResourceBuffer Output { get; private set; }

        private GameClock _clock;

        private void Awake()
        {
            Output = new ResourceBuffer(new BigDouble(config.CapacityAtLevel(Level)));
        }

        private void Start()
        {
            _clock = ServiceLocator.Get<GameClock>();
            if (_clock != null) _clock.OnTick += OnTick;
        }

        private void OnDestroy()
        {
            if (_clock != null) _clock.OnTick -= OnTick;
        }

        private void OnTick()
        {
            Output.Add(new BigDouble(config.RateAtLevel(Level) * _clock.TickInterval));
        }

        /// <summary>Player tap — instant ore burst.</summary>
        public void Tap() => Output.Add(new BigDouble(tapAmount));

        public string Label => config.DisplayName;

        public BigDouble UpgradeCost(EconomyService economy) => economy.UpgradeCost(config.BaseUpgradeCost, Level);

        public void ApplyLevel(int level)
        {
            Level = level;
            if (Output != null) Output.Capacity = new BigDouble(config.CapacityAtLevel(Level));
        }
    }
}
