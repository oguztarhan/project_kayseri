using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Turns raw ore into products via recipes each tick (GDD §4). Input filled by ore trucks, output
    /// drained by cargo trucks. Upgrade tracks: <b>Process Speed</b> (batch rate) and <b>Capacity</b>
    /// (in/out buffers). A manager multiplies its rate (GDD §6).
    /// </summary>
    public sealed class Refinery : UpgradableStation, IProducer
    {
        [SerializeField] private StationConfig config;
        [SerializeField] private Recipe[] recipes;

        private const int Speed = 0, Cap = 1;
        private static readonly string[] TrackList = { "Process Speed", "Capacity" };

        public Inventory<ResourceDef> Input { get; private set; }
        public Inventory<ResourceDef> Output { get; private set; }
        public double RateMultiplier { get; set; } = 1d;

        private GameClock _clock;
        private (ResourceDef key, BigDouble amount)[][] _recipeInputs;
        private double _rate;

        protected override string StationLabel => config.DisplayName;
        protected override string[] TrackNames => TrackList;
        protected override double TrackBaseCost(int track) => config.BaseUpgradeCost;

        private void Awake()
        {
            Input = new Inventory<ResourceDef>(new BigDouble(config.CapacityAtLevel(0)));
            Output = new Inventory<ResourceDef>(new BigDouble(config.CapacityAtLevel(0)));
            BuildRecipeCache();
            OnUpgraded();
        }

        private void Start() { _clock = ServiceLocator.Get<GameClock>(); if (_clock != null) _clock.OnTick += OnTick; }
        private void OnDestroy() { if (_clock != null) _clock.OnTick -= OnTick; }

        protected override void OnUpgraded()
        {
            _rate = config.RateAtLevel(TrackLevel(Speed));
            if (Input != null) { Input.Capacity = new BigDouble(config.CapacityAtLevel(TrackLevel(Cap))); Output.Capacity = new BigDouble(config.CapacityAtLevel(TrackLevel(Cap))); }
        }

        private void BuildRecipeCache()
        {
            int count = recipes != null ? recipes.Length : 0;
            _recipeInputs = new (ResourceDef, BigDouble)[count][];
            for (int r = 0; r < count; r++)
            {
                var ins = recipes[r] != null ? recipes[r].Inputs : null;
                int n = ins != null ? ins.Length : 0;
                var arr = new (ResourceDef, BigDouble)[n];
                for (int i = 0; i < n; i++) arr[i] = (ins[i].resource, new BigDouble(ins[i].amount));
                _recipeInputs[r] = arr;
            }
        }

        private void OnTick()
        {
            if (recipes == null) return;
            BigDouble batchBudget = new BigDouble(_rate * RateMultiplier * _clock.TickInterval);
            for (int r = 0; r < recipes.Length; r++)
            {
                Recipe recipe = recipes[r];
                if (recipe == null || recipe.Output == null) continue;
                Refining.Process(Input, Output, _recipeInputs[r], recipe.Output, new BigDouble(recipe.OutputAmount), batchBudget);
            }
        }
    }
}
