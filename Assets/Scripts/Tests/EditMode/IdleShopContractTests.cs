using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Core;
using Game.Data;
using Game.Systems;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class IdleShopContractTests
    {
        private readonly List<UnityEngine.Object> _assets = new List<UnityEngine.Object>();

        private T Asset<T>() where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            _assets.Add(asset);
            return asset;
        }

        private static void Set(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        [TearDown]
        public void Cleanup()
        {
            foreach (var asset in _assets) UnityEngine.Object.DestroyImmediate(asset);
            _assets.Clear();
        }

        private StageDefinition Stage(string island, int beat, OreTier ore, Product product, Recipe recipe)
        {
            var stage = Asset<StageDefinition>();
            Set(stage, "stageId", island + ".stage." + beat);
            Set(stage, "islandId", island);
            Set(stage, "completionBeat", beat);
            Set(stage, "resources", new[]
            {
                new StageDefinition.ResourceBinding { id = "ore.coal", resource = ore, extracted = true },
                new StageDefinition.ResourceBinding { id = "product.coke", resource = product }
            });
            Set(stage, "workstations", new[] { new StageDefinition.Workstation { anchorId = "refinery.1", recipe = recipe } });
            return stage;
        }

        private StageDefinition[] Catalogue()
        {
            var ore = Asset<OreTier>();
            var product = Asset<Product>();
            var recipe = Asset<Recipe>();
            Set(recipe, "inputs", new[] { new Recipe.Ingredient { resource = ore, amount = 1 } });
            Set(recipe, "output", product);
            return new[]
            {
                Stage("coal", 1, ore, product, recipe), Stage("coal", 2, ore, product, recipe),
                Stage("coal", 3, ore, product, recipe), Stage("coal", 4, ore, product, recipe),
                Stage("copper", 1, ore, product, recipe)
            }; // Deliberately shared fixture recipe: labels/progression are independent of product choice.
        }

        private static StageService Service(SaveData data, StageDefinition[] catalogue)
            => new StageService(new ChapterService(data, new WalletService(data.wallet), null, Chapters.Tuning.Default),
                                catalogue, new[] { "refinery.1", "refinery.2" });

        [Test]
        public void StageLabelsUseExistingChapterCoordinates()
        {
            var stages = Catalogue();
            CollectionAssert.AreEqual(new[] { "1-1", "1-2", "1-3", "1-4", "2-1" },
                Array.ConvertAll(stages, StageService.Label));
            var service = Service(new SaveData(), stages);
            Assert.That(service.IsUnlocked(stages[0].StageId), Is.True);
            Assert.That(service.IsUnlocked(stages[1].StageId), Is.False);
            Assert.That(service.IsUnlocked(stages[4].StageId), Is.False);
        }

        [Test]
        public void ExistingEarnedProgressUnlocksNextStageWithoutSecondRewardLedger()
        {
            var data = new SaveData();
            data.islandLevels.Add(new StationLevel { id = "coal#0#0", level = Chapters.Tuning.Default.FirstSmokeLevels });
            var stages = Catalogue();
            var service = Service(data, stages);
            Assert.That(service.IsComplete(stages[0].StageId), Is.True);
            Assert.That(service.IsUnlocked(stages[1].StageId), Is.True);
            Assert.That(service.IsUnlocked(stages[2].StageId), Is.False);
            Assert.That(data.wallet.gems, Is.Zero);
        }

        [Test]
        public void ClaimedBeatsSurviveRetuningAndReload()
        {
            var data = new SaveData();
            data.chapters.Add(new ChapterState { id = "coal", claimed = new[] { true, true, true, true, true } });
            data.unlockedIslands.Add("copper");
            var stages = Catalogue();
            var loaded = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(data));
            var service = Service(loaded, stages);
            Assert.That(service.IsComplete(stages[3].StageId), Is.True);
            Assert.That(service.IsUnlocked(stages[4].StageId), Is.True);
            Assert.That(loaded.chapters[0].claimed, Is.EqualTo(data.chapters[0].claimed));
        }

        [Test]
        public void OutOfOrderBeatDoesNotSkipEarlierWork()
        {
            var data = new SaveData();
            data.chapters.Add(new ChapterState { id = "coal", claimed = new[] { true, false, false, true, false } });
            var stages = Catalogue();
            Assert.That(Service(data, stages).IsUnlocked(stages[3].StageId), Is.False);
        }

        [Test]
        public void MissingEarlierStageIsRejected()
        {
            var stages = Catalogue();
            Assert.Throws<ArgumentException>(() => Service(new SaveData(), new[] { stages[1] }));
        }

        [Test]
        public void DuplicateStageIdIsRejected()
        {
            var stages = Catalogue();
            Assert.Throws<ArgumentException>(() => Service(new SaveData(), new[] { stages[0], stages[0] }));
        }

        [Test]
        public void MissingMapAnchorIsRejected()
        {
            var stages = Catalogue();
            Set(stages[0], "workstations", new[] { new StageDefinition.Workstation { anchorId = "absent", recipe = stages[0].WorkstationAt(0).recipe } });
            Assert.Throws<ArgumentException>(() => Service(new SaveData(), stages));
        }

        [Test]
        public void RecipeWithoutReachableMineInputIsRejected()
        {
            var stages = Catalogue();
            var first = stages[0].ResourceAt(0);
            first.extracted = false;
            Set(stages[0], "resources", new[] { first, stages[0].ResourceAt(1) });
            Assert.Throws<ArgumentException>(() => Service(new SaveData(), stages));
        }

        [Test]
        public void StableResourceIdsCannotChangeAcrossStages()
        {
            var stages = Catalogue();
            var binding = stages[1].ResourceAt(1);
            binding.id = "renamed.product";
            Set(stages[1], "resources", new[] { stages[1].ResourceAt(0), binding });
            Assert.Throws<ArgumentException>(() => Service(new SaveData(), stages));
        }

        [Test]
        public void DistinctCraftableProductsAreValidatedInReverseDependencyOrder()
        {
            var stages = Catalogue();
            var stage = stages[0];
            var second = Asset<Product>();
            var recipe = Asset<Recipe>();
            Set(recipe, "inputs", new[] { new Recipe.Ingredient { resource = stage.ResourceAt(1).resource, amount = 2 } });
            Set(recipe, "output", second);
            var oldWork = stage.WorkstationAt(0);
            Set(stage, "resources", new[] { stage.ResourceAt(0), stage.ResourceAt(1),
                new StageDefinition.ResourceBinding { id = "product.fixture.second", resource = second } });
            Set(stage, "workstations", new[] { new StageDefinition.Workstation { anchorId = "refinery.2", recipe = recipe }, oldWork });
            Assert.DoesNotThrow(() => Service(new SaveData(), new[] { stage }));
            Assert.That(stage.WorkstationCount, Is.EqualTo(2));
        }

        private static MarketYard Legacy() => new MarketYard
        {
            id = "coal", stock = 12.5, deliveredPerMin = 6.25,
            hireCarry = 2, hireServe = 3, hireCollect = 4, depositSlots = 2, queueSlots = 3
        };

        [Test]
        public void MigrationCopiesFractionalStockAndEveryYardInvestmentWithoutMutatingSource()
        {
            var legacy = Legacy();
            var before = JsonUtility.ToJson(legacy);
            var row = IdleMarketMigration.Convert(legacy, "product.coke");
            Assert.That(row.products[0].stock, Is.EqualTo(12.5));
            Assert.That(row.products[0].deliveredPerMin, Is.EqualTo(6.25));
            Assert.That(row.products[0].voyageReserved, Is.Zero);
            Assert.That(new[] { row.depositSlots, row.queueSlots, row.hireCarry, row.hireServe, row.dispatchLevel }, Is.EqualTo(new[] { 2, 3, 2, 3, 4 }));
            Assert.That(JsonUtility.ToJson(legacy), Is.EqualTo(before));
        }

        [Test]
        public void MigrationRetryAfterSelloutAndReloadCannotRecreditLegacyStock()
        {
            var old = Legacy();
            var row = IdleMarketMigration.Convert(old, "product.coke");
            row.products[0].stock = 0;
            row = JsonUtility.FromJson<IdleMarketYard>(JsonUtility.ToJson(row));
            Assert.That(IdleMarketMigration.Convert(old, "product.coke", row), Is.SameAs(row));
            Assert.That(row.products[0].stock, Is.Zero);
        }

        [Test]
        public void MigrationDoesNotMergeDifferentProductBalances()
        {
            var old = Legacy();
            var row = IdleMarketMigration.Convert(old, "product.coke");
            row.products.Add(new MarketProductStock { productId = "product.copper-bar", stock = 3, voyageReserved = 2 });
            var loaded = JsonUtility.FromJson<IdleMarketYard>(JsonUtility.ToJson(row));
            IdleMarketMigration.Convert(old, "product.coke", loaded);
            Assert.That(loaded.products[0].stock, Is.EqualTo(12.5));
            Assert.That(loaded.products[1].stock, Is.EqualTo(3));
            Assert.That(loaded.products[1].voyageReserved, Is.EqualTo(2));
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(-1d)]
        public void InvalidLegacyStockFailsWithoutReplacingSave(double stock)
        {
            var row = Legacy();
            row.stock = stock;
            Assert.Throws<ArgumentException>(() => IdleMarketMigration.Convert(row, "product.coke"));
        }

        [Test]
        public void UnknownFutureSchemaIsNotDowngraded()
        {
            var old = Legacy();
            var row = IdleMarketMigration.Convert(old, "product.coke");
            row.schemaVersion++;
            Assert.Throws<ArgumentException>(() => IdleMarketMigration.Convert(old, "product.coke", row));
        }

        [Test]
        public void DuplicateProductIdsAreRejected()
        {
            var row = IdleMarketMigration.Convert(Legacy(), "product.coke");
            row.products.Add(new MarketProductStock { productId = "product.coke" });
            Assert.Throws<ArgumentException>(() => IdleMarketMigration.Validate(row));
        }

        [Test]
        public void DispatchInvestmentPreservesExistingServiceCurveForEveryHireCombination()
        {
            for (int c = 0; c <= 5; c++)
                for (int s = 0; s <= 5; s++)
                    for (int d = 0; d <= 5; d++)
                        Assert.That(IdleCrewRules.ServiceRate(c, s, d), Is.EqualTo(MarketFlow.ServiceRate(new[] { c, s, d })));
        }

        [TestCase(0, 1d)]
        [TestCase(5, 1.5d)]
        [TestCase(8, 1.8d)]
        [TestCase(10, 1.8d)]
        [TestCase(-1, 1d)]
        public void GlobalCarryInvestmentHasExplicitNpcBenefit(int level, double expected)
            => Assert.That(IdleCrewRules.PorterLoadMultiplier(level), Is.EqualTo(expected));

        [TestCase(IslandEconomy.Train, 0)]
        [TestCase(IslandEconomy.Train, 1)]
        [TestCase(IslandEconomy.OreTrucks, 0)]
        [TestCase(IslandEconomy.OreTrucks, 1)]
        [TestCase(IslandEconomy.OreTrucks, 2)]
        [TestCase(IslandEconomy.CargoTrucks, 0)]
        [TestCase(IslandEconomy.CargoTrucks, 1)]
        [TestCase(IslandEconomy.CargoTrucks, 2)]
        public void EveryTransportAxisStillImprovesItsAssignedWorkerBudget(int station, int axis)
        {
            var levels = IslandEconomy.NewLevels();
            var economy = new IslandEconomy(IslandEconomy.Tuning.Default, levels, null);
            var before = Budget(economy, station);
            levels[station][axis] = 1;
            var after = Budget(economy, station);
            Assert.That(after.Teams * after.Speed * after.LoadPerTeam,
                Is.GreaterThan(before.Teams * before.Speed * before.LoadPerTeam));
            Assert.That(economy.Levels[station][axis], Is.EqualTo(1));
        }

        private static IdleTransportRules.CrewBudget Budget(IslandEconomy economy, int station)
            => station == IslandEconomy.Train ? IdleTransportRules.MineToDepot(economy, 0)
             : station == IslandEconomy.OreTrucks ? IdleTransportRules.DepotToRefinery(economy, 0)
             : IdleTransportRules.RefineryToCounter(economy, 0);
    }
}
