using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Extracts one specific <see cref="OreTier"/> into its output buffer each tick (auto trickle), plus a
    /// burst on tap (GDD §3). Upgrade tracks: <b>Mine Speed</b> (rate) and <b>Ore Yield</b> (buffer + tap).
    /// A hired manager multiplies its rate (GDD §6).
    /// </summary>
    public sealed class MineStation : UpgradableStation, IProducer
    {
        [SerializeField] private StationConfig config;
        [SerializeField] private OreTier ore;
        [SerializeField] private double tapAmount = 5d;

        private const int Speed = 0, Yield = 1;
        private static readonly string[] TrackList = { "Mine Speed", "Ore Yield" };

        public ResourceBuffer Output { get; private set; }
        public OreTier Ore => ore;
        public double RateMultiplier { get; set; } = 1d;

        private GameClock _clock;
        private double _rate, _tap;

        protected override string StationLabel => config.DisplayName;
        protected override string[] TrackNames => TrackList;
        protected override double TrackBaseCost(int track) => config.BaseUpgradeCost;

        private void Awake() { Output = new ResourceBuffer(new BigDouble(config.CapacityAtLevel(0))); OnUpgraded(); }
        private void Start() { _clock = ServiceLocator.Get<GameClock>(); if (_clock != null) _clock.OnTick += OnTick; }
        private void OnDestroy() { if (_clock != null) _clock.OnTick -= OnTick; }

        protected override void OnUpgraded()
        {
            _rate = config.RateAtLevel(TrackLevel(Speed));
            _tap = tapAmount * (1d + 0.5d * TrackLevel(Yield));
            if (Output != null) Output.Capacity = new BigDouble(config.CapacityAtLevel(TrackLevel(Yield)));
        }

        private void OnTick() => Output.Add(new BigDouble(_rate * RateMultiplier * _clock.TickInterval));
        public void Tap() => Output.Add(new BigDouble(_tap));
    }
}
