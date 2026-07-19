using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Turns raw ore into products via recipes each tick (GDD §4). Input filled by ore trucks, output
    /// drained by cargo trucks. Handles 1:1 (Coke) and combine (Steel). A manager multiplies its rate.
    /// </summary>
    public sealed class Refinery : MonoBehaviour, IUpgradable, IProducer
    {
        [SerializeField] private StationConfig config;
        [SerializeField] private Recipe[] recipes;

        public int Level { get; private set; }
        public Inventory<ResourceDef> Input { get; private set; }
        public Inventory<ResourceDef> Output { get; private set; }
        public double RateMultiplier { get; set; } = 1d;

        private GameClock _clock;
        private (ResourceDef key, BigDouble amount)[][] _recipeInputs;

        private void Awake()
        {
            Input = new Inventory<ResourceDef>(new BigDouble(config.CapacityAtLevel(Level)));
            Output = new Inventory<ResourceDef>(new BigDouble(config.CapacityAtLevel(Level)));
            BuildRecipeCache();
        }

        private void Start() { _clock = ServiceLocator.Get<GameClock>(); if (_clock != null) _clock.OnTick += OnTick; }
        private void OnDestroy() { if (_clock != null) _clock.OnTick -= OnTick; }

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
            BigDouble batchBudget = new BigDouble(config.RateAtLevel(Level) * RateMultiplier * _clock.TickInterval);
            for (int r = 0; r < recipes.Length; r++)
            {
                Recipe recipe = recipes[r];
                if (recipe == null || recipe.Output == null) continue;
                Refining.Process(Input, Output, _recipeInputs[r], recipe.Output, new BigDouble(recipe.OutputAmount), batchBudget);
            }
        }

        public string Label => config.DisplayName;
        public BigDouble UpgradeCost(EconomyService economy) => economy.UpgradeCost(config.BaseUpgradeCost, Level);
        public void ApplyLevel(int level)
        {
            Level = level;
            if (Input != null) { Input.Capacity = new BigDouble(config.CapacityAtLevel(Level)); Output.Capacity = new BigDouble(config.CapacityAtLevel(Level)); }
        }
    }
}
