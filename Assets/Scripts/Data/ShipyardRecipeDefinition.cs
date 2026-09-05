using System;
using Game.Core;
using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Designer-authored recipe data for ship-item machines. This is deliberately separate from
    /// <see cref="Recipe"/>, which describes refinery products and has a different output contract.
    /// Runtime saves use the stable IDs here rather than Unity object references.
    /// </summary>
    [CreateAssetMenu(fileName = "ShipyardRecipe", menuName = "Ore Empire/Shipyard Recipe", order = 29)]
    public sealed class ShipyardRecipeDefinition : ScriptableObject
    {
        [Serializable]
        public struct Ingredient
        {
            [SerializeField] private string resourceId;
            [SerializeField] private ResourceDef resource;
            [SerializeField, Min(0f)] private double quantity;

            public string ResourceId => resourceId;
            public ResourceDef Resource => resource;
            public double Quantity => quantity < 0d ? 0d : quantity;

            public Ingredient(string id, double amount)
            {
                resourceId = id ?? string.Empty;
                resource = null;
                quantity = amount;
            }
        }

        [Serializable]
        public struct MaterialUnlockCondition
        {
            [SerializeField] private string resourceId;
            [SerializeField, Min(0f)] private double minimumQuantity;

            public string ResourceId => resourceId;
            public double MinimumQuantity => minimumQuantity < 0d ? 0d : minimumQuantity;

            public MaterialUnlockCondition(string id, double amount)
            {
                resourceId = id ?? string.Empty;
                minimumQuantity = amount;
            }
        }

        [Serializable]
        public struct IslandUnlockCondition
        {
            [SerializeField] private string islandId;
            [SerializeField, Min(0)] private int minimumLevel;

            public string IslandId => islandId;
            public int MinimumLevel => minimumLevel < 0 ? 0 : minimumLevel;
        }

        [Serializable]
        public struct RarityRule
        {
            [SerializeField] private string rarityId;
            [SerializeField, Min(0f)] private double statMultiplier;
            [SerializeField, Min(0f)] private double valueMultiplier;

            public string RarityId => rarityId;
            public double StatMultiplier => statMultiplier < 0d ? 0d : statMultiplier;
            public double ValueMultiplier => valueMultiplier < 0d ? 0d : valueMultiplier;

            public RarityRule(string id, double stats, double value)
            {
                rarityId = id ?? string.Empty;
                statMultiplier = stats;
                valueMultiplier = value;
            }
        }

        [Serializable]
        public struct EquipmentStats
        {
            [SerializeField] private double hull;
            [SerializeField] private double shot;
            [SerializeField] private double defence;
            [SerializeField] private double speed;
            [SerializeField] private double secondaryAmount;
            [SerializeField] private double baseValue;

            public double Hull => hull;
            public double Shot => shot;
            public double Defence => defence;
            public double Speed => speed;
            public double SecondaryAmount => secondaryAmount;
            public double BaseValue => baseValue;

            public EquipmentStats(double hull, double shot, double defence, double speed,
                                  double secondaryAmount, double baseValue)
            {
                this.hull = hull;
                this.shot = shot;
                this.defence = defence;
                this.speed = speed;
                this.secondaryAmount = secondaryAmount;
                this.baseValue = baseValue;
            }
        }

        [Header("Identity")]
        [SerializeField] private string recipeId = "Recipe_Cannon_01";
        [SerializeField] private string machineFamilyId = "Station_Cannon";
        [SerializeField] private string displayName = "Cannon";
        [SerializeField] private string localizationKey = "shipyard.recipe.cannon.01";

        [Header("Production")]
        [SerializeField] private Ingredient[] ingredients = new Ingredient[0];
        [SerializeField] private double productionDurationSeconds = 1d;
        [SerializeField] private string outputEquipmentId = "Equipment_Cannon_01";
        [SerializeField, Min(0)] private int outputEquipmentSlot;
        [SerializeField] private EquipmentStats baseStats;
        [SerializeField] private RarityRule[] rarityRules = new RarityRule[0];

        [Header("Unlock conditions")]
        [SerializeField] private MaterialUnlockCondition[] materialUnlocks = new MaterialUnlockCondition[0];
        [SerializeField] private IslandUnlockCondition[] islandUnlocks = new IslandUnlockCondition[0];
        [SerializeField, Min(0)] private int requiredCompletedOrders;
        [SerializeField, Min(0)] private int requiredReputation;

        [Header("Presentation")]
        [SerializeField] private GameObject modelPrefab;
        [SerializeField] private Sprite icon;
        [SerializeField] private string inputSocketName = "Input";
        [SerializeField] private string workSocketName = "Work";
        [SerializeField] private string outputSocketName = "Output";
        [SerializeField] private string workerSocketName = "Worker";
        [SerializeField] private string vfxSocketName = "VFX";

        public string RecipeId => recipeId;
        public string MachineFamilyId => machineFamilyId;
        public string DisplayName => displayName;
        public string LocalizationKey => localizationKey;
        public Ingredient[] Ingredients => ingredients;
        public double ProductionDurationSeconds => productionDurationSeconds < 0.1d ? 0.1d : productionDurationSeconds;
        public string OutputEquipmentId => outputEquipmentId;
        public int OutputEquipmentSlot => outputEquipmentSlot < 0 ? 0 : outputEquipmentSlot;
        public EquipmentStats BaseStats => baseStats;
        public RarityRule[] RarityRules => rarityRules;
        public MaterialUnlockCondition[] MaterialUnlocks => materialUnlocks;
        public IslandUnlockCondition[] IslandUnlocks => islandUnlocks;
        public int RequiredCompletedOrders => requiredCompletedOrders < 0 ? 0 : requiredCompletedOrders;
        public int RequiredReputation => requiredReputation < 0 ? 0 : requiredReputation;
        public GameObject ModelPrefab => modelPrefab;
        public Sprite Icon => icon;
        public string InputSocketName => inputSocketName;
        public string WorkSocketName => workSocketName;
        public string OutputSocketName => outputSocketName;
        public string WorkerSocketName => workerSocketName;
        public string VfxSocketName => vfxSocketName;

        /// <summary>
        /// Runtime fallback for a new save while the designer asset is not wired yet. The same
        /// stable IDs are what the authored asset uses, so tests and the first playable slice do
        /// not invent a second recipe contract.
        /// </summary>
        public static ShipyardRecipeDefinition CreateCannonStarterRuntime()
        {
            return CreateRuntime("Recipe_Cannon_01", "Station_Cannon", "Cannon", "shipyard.recipe.cannon.01",
                new[] { new Ingredient("coal", 2d), new Ingredient("steel_beam", 1d) },
                5d, "Equipment_Cannon_01", SeaCombat.SlotCannon,
                new EquipmentStats(6d, 3.5d, 0.5d, 1d, 0d, 100d));
        }

        public static ShipyardRecipeDefinition CreateHullStarterRuntime()
        {
            return CreateRuntime("Recipe_Hull_01", "Station_Hull", "Hull Plating", "shipyard.recipe.hull.01",
                new[] { new Ingredient("steel_beam", 2d), new Ingredient("copper_bar", 1d) },
                7d, "Equipment_Hull_01", SeaCombat.SlotPlating,
                new EquipmentStats(26d, 0.6d, 3d, 0.5d, 0d, 160d));
        }

        public static ShipyardRecipeDefinition CreateRiggingStarterRuntime()
        {
            return CreateRuntime("Recipe_Rigging_01", "Station_Rigging", "Rigging", "shipyard.recipe.rigging.01",
                new[] { new Ingredient("steel_beam", 1d), new Ingredient("copper_bar", 2d) },
                8d, "Equipment_Rigging_01", SeaCombat.SlotRigging,
                new EquipmentStats(4d, 0.4d, 0.4d, 1.2d, 0d, 180d));
        }

        public static ShipyardRecipeDefinition CreateNavigationStarterRuntime()
        {
            return CreateRuntime("Recipe_Navigation_01", "Station_Navigation", "Spyglass", "shipyard.recipe.navigation.01",
                new[] { new Ingredient("silver_bar", 1d), new Ingredient("steel_beam", 1d) },
                9d, "Equipment_Spyglass_01", SeaCombat.SlotSpyglass,
                new EquipmentStats(10d, 1.8d, 1d, 4d, 0d, 220d));
        }

        public static ShipyardRecipeDefinition CreateFigureheadStarterRuntime()
        {
            return CreateRuntime("Recipe_Figurehead_01", "Station_Figurehead", "Charm", "shipyard.recipe.figurehead.01",
                new[] { new Ingredient("gold_bar", 1d), new Ingredient("cut_ruby", 1d) },
                10d, "Equipment_Figurehead_01", SeaCombat.SlotCharm,
                new EquipmentStats(20d, 1.1d, 2d, 2d, 0d, 260d));
        }

        public static ShipyardRecipeDefinition[] CreateStarterRuntimeSet()
        {
            return new[]
            {
                CreateCannonStarterRuntime(), CreateHullStarterRuntime(),
                CreateRiggingStarterRuntime(), CreateNavigationStarterRuntime(),
                CreateFigureheadStarterRuntime()
            };
        }

        /// <summary>
        /// The functional tier catalogue used until authored recipe assets arrive. Tier 1 recipes
        /// are the only entries discovered on a fresh save; these later entries stay hidden until
        /// their machine, order/reputation, and material gates are all satisfied.
        /// </summary>
        public static ShipyardRecipeDefinition[] CreateTieredRuntimeSet()
        {
            ShipyardRecipeDefinition[] starters = CreateStarterRuntimeSet();
            var all = new ShipyardRecipeDefinition[10];
            for (int i = 0; i < starters.Length; i++) all[i] = starters[i];

            all[5] = CreateRuntime("Recipe_Cannon_02", "Station_Cannon", "Cannon II", "shipyard.recipe.cannon.02",
                new[] { new Ingredient("coke", 2d), new Ingredient("copper_bar", 2d) },
                8d, "Equipment_Cannon_02", SeaCombat.SlotCannon,
                new EquipmentStats(10d, 6d, 1d, 2d, 0d, 180d),
                new[] { new MaterialUnlockCondition("coal", 1d), new MaterialUnlockCondition("copper", 1d) }, 2, 2);
            all[6] = CreateRuntime("Recipe_Hull_02", "Station_Hull", "Hull Plating II", "shipyard.recipe.hull.02",
                new[] { new Ingredient("steel_beam", 3d), new Ingredient("silver_bar", 1d) },
                10d, "Equipment_Hull_02", SeaCombat.SlotPlating,
                new EquipmentStats(44d, 1d, 5d, 1d, 0d, 280d),
                new[] { new MaterialUnlockCondition("iron", 1d), new MaterialUnlockCondition("silver", 1d) }, 4, 4);
            all[7] = CreateRuntime("Recipe_Rigging_02", "Station_Rigging", "Rigging II", "shipyard.recipe.rigging.02",
                new[] { new Ingredient("copper_bar", 3d), new Ingredient("gold_bar", 1d) },
                11d, "Equipment_Rigging_02", SeaCombat.SlotRigging,
                new EquipmentStats(7d, 0.7d, 0.7d, 2d, 0d, 320d),
                new[] { new MaterialUnlockCondition("gold", 1d) }, 6, 6);
            all[8] = CreateRuntime("Recipe_Navigation_02", "Station_Navigation", "Spyglass II", "shipyard.recipe.navigation.02",
                new[] { new Ingredient("silver_bar", 2d), new Ingredient("cut_emerald", 1d) },
                12d, "Equipment_Spyglass_02", SeaCombat.SlotSpyglass,
                new EquipmentStats(17d, 3d, 2d, 7d, 0d, 380d),
                new[] { new MaterialUnlockCondition("ruby", 1d), new MaterialUnlockCondition("emerald", 1d) }, 8, 8);
            all[9] = CreateRuntime("Recipe_Figurehead_02", "Station_Figurehead", "Charm II", "shipyard.recipe.figurehead.02",
                new[] { new Ingredient("gold_bar", 2d), new Ingredient("polished_diamond", 1d) },
                14d, "Equipment_Figurehead_02", SeaCombat.SlotCharm,
                new EquipmentStats(34d, 2d, 3d, 3d, 0d, 460d),
                new[] { new MaterialUnlockCondition("diamond", 1d) }, 12, 12);
            return all;
        }

        private static ShipyardRecipeDefinition CreateRuntime(string id, string machine, string name,
                                                              string localization, Ingredient[] needs,
                                                              double duration, string outputId, int slot,
                                                              EquipmentStats stats,
                                                              MaterialUnlockCondition[] unlocks = null,
                                                              int requiredOrders = 0,
                                                              int requiredReputation = 0)
        {
            var definition = CreateInstance<ShipyardRecipeDefinition>();
            definition.recipeId = id;
            definition.machineFamilyId = machine;
            definition.displayName = name;
            definition.localizationKey = localization;
            definition.ingredients = needs ?? new Ingredient[0];
            definition.productionDurationSeconds = duration;
            definition.outputEquipmentId = outputId;
            definition.outputEquipmentSlot = slot;
            definition.baseStats = stats;
            definition.rarityRules = new[] { new RarityRule("Common", 1d, 1d) };
            definition.materialUnlocks = unlocks ?? new MaterialUnlockCondition[0];
            definition.requiredCompletedOrders = requiredOrders;
            definition.requiredReputation = requiredReputation;
            return definition;
        }
    }
}
