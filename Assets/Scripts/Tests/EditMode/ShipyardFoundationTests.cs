using System.Linq;
using Game.Core;
using Game.Data;
using Game.Systems;
using Game.UI;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public class ShipyardFoundationTests
    {
        [Test] public void NewRunOnlyOpensCannon()
        {
            var p = new ShipyardProgression(); p.Normalize();
            Assert.That(p.unlockedStations, Is.EquivalentTo(new[] { "Station_Cannon" }));
            Assert.That(p.NextMachine, Is.EqualTo("Station_Hull"));
            Assert.That(p.discoveredRecipeIds, Is.EquivalentTo(new[] { ShipyardProgression.CannonStarterRecipeId }));
            Assert.That(p.machines.Count, Is.EqualTo(1));
            Assert.That(p.machines[0].machineId, Is.EqualTo(ShipyardProgression.CannonMachineId));
            Assert.That(p.machines[0].constructionState, Is.EqualTo(ShipyardMachineState.Built));
        }

        [Test] public void PortraitSwitchPreservesFreshAndLegacySavesAcrossOnOffOn()
        {
            var fresh = new SaveData();
            fresh.shipyard.Normalize();
            string freshShipyard = JsonUtility.ToJson(fresh.shipyard);

            Assert.That(ShipyardFeatureSwitch.IsEnabled(fresh), Is.True);
            Assert.That(ShipyardFeatureSwitch.Set(fresh, false), Is.True);
            Assert.That(ShipyardFeatureSwitch.IsEnabled(fresh), Is.False);
            Assert.That(ShipyardFeatureSwitch.Set(fresh, true), Is.True);
            Assert.That(ShipyardFeatureSwitch.IsEnabled(fresh), Is.True);
            Assert.That(JsonUtility.ToJson(fresh.shipyard), Is.EqualTo(freshShipyard));
            Assert.That(ShipyardFeatureSwitch.PresentationScene(fresh, "Shipyard", "Main"),
                        Is.EqualTo("Shipyard"));

            var legacy = JsonUtility.FromJson<SaveData>(
                "{\"UsePortraitShipyard\":false,\"tutorialStep\":100,\"unlockedIslands\":[\"coal\"]}");
            legacy.shipyard.Normalize();
            legacy.shipyard.completedOrders = 4;
            string legacyShipyard = JsonUtility.ToJson(legacy.shipyard);
            Assert.That(ShipyardFeatureSwitch.IsEnabled(legacy), Is.False);
            Assert.That(ShipyardFeatureSwitch.PresentationScene(legacy, "Shipyard", "Main"),
                        Is.EqualTo("Main"));

            Assert.That(ShipyardFeatureSwitch.Set(legacy, true), Is.True);
            Assert.That(ShipyardFeatureSwitch.Set(legacy, false), Is.True);
            Assert.That(ShipyardFeatureSwitch.Set(legacy, true), Is.True);
            Assert.That(legacy.tutorialStep, Is.EqualTo(100));
            Assert.That(legacy.unlockedIslands, Is.EqualTo(new[] { "coal" }));
            Assert.That(JsonUtility.ToJson(legacy.shipyard), Is.EqualTo(legacyShipyard));

            var saveService = new SaveService("shipyard-feature-switch-unused.dat");
            var restored = saveService.Decrypt(saveService.Encrypt(legacy), out bool tampered);
            Assert.That(tampered, Is.False);
            Assert.That(restored.UsePortraitShipyard, Is.True);
            Assert.That(restored.shipyard.completedOrders, Is.EqualTo(4));
        }

        [Test] public void OldSaveWithoutSwitchFieldDefaultsToPortraitMode()
        {
            var old = JsonUtility.FromJson<SaveData>("{\"tutorialStep\":100}");
            Assert.That(ShipyardFeatureSwitch.IsEnabled(old), Is.True);
        }

        [Test] public void ShipyardRecipeDefaultsToFreshCannonDefinition()
        {
            ShipyardRecipeDefinition recipe = ScriptableObject.CreateInstance<ShipyardRecipeDefinition>();
            try
            {
                Assert.That(recipe.RecipeId, Is.EqualTo(ShipyardProgression.CannonStarterRecipeId));
                Assert.That(recipe.MachineFamilyId, Is.EqualTo(ShipyardProgression.CannonMachineId));
                Assert.That(recipe.ProductionDurationSeconds, Is.GreaterThan(0d));
                Assert.That(recipe.OutputEquipmentSlot, Is.EqualTo(SeaCombat.SlotCannon));
                Assert.That(recipe.InputSocketName, Is.EqualTo("Input"));
                Assert.That(recipe.WorkSocketName, Is.EqualTo("Work"));
                Assert.That(recipe.OutputSocketName, Is.EqualTo("Output"));
                Assert.That(recipe.VfxSocketName, Is.EqualTo("VFX"));
            }
            finally { Object.DestroyImmediate(recipe); }
        }

        [Test] public void GlobalUpgradeCatalogContainsOnlyMajorStations()
        {
            Assert.That(IslandEconomy.PlayerStations, Is.EqualTo(new[]
            {
                IslandEconomy.Mine, IslandEconomy.Storage, IslandEconomy.Smelter,
                IslandEconomy.Market, IslandEconomy.Power
            }));
            Assert.That(IslandEconomy.MajorStationIds, Is.EqualTo(new[]
            {
                IslandEconomy.Hub_Mine, IslandEconomy.Hub_Deposit, IslandEconomy.Hub_Refinery,
                IslandEconomy.Hub_Market, IslandEconomy.Hub_Port
            }));
        }

        [Test] public void TransportSlotsNeverAppearInGlobalUpgradeCatalog()
        {
            Assert.That(IslandEconomy.IsMajorStation(IslandEconomy.Train), Is.False);
            Assert.That(IslandEconomy.IsMajorStation(IslandEconomy.OreTrucks), Is.False);
            Assert.That(IslandEconomy.IsMajorStation(IslandEconomy.CargoTrucks), Is.False);
        }

        [Test] public void CannotSkipMachinesOrUnlockMissingArt()
        {
            var p = new ShipyardProgression();
            Assert.That(p.TryUnlockNext("Station_Rigging", true, true), Is.False);
            Assert.That(p.TryUnlockNext("Station_Hull", false, true), Is.False);
            Assert.That(p.TryUnlockNext("Station_Hull", true, false), Is.False);
            Assert.That(p.TryUnlockNext("Station_Hull", true, true), Is.True);
            Assert.That(p.TryUnlockNext("Station_Hull", true, true), Is.False);
        }

        [Test] public void FigureheadRemainsLockedAfterFirstFourMachines()
        {
            var p = new ShipyardProgression();
            for (int i = 1; i < 4; i++) Assert.That(p.TryUnlockNext(ShipyardProgression.MachineIds[i], true, true), Is.True);
            Assert.That(p.TryUnlockNext("Station_Figurehead", true, false), Is.False);
            Assert.That(p.NextMachine, Is.EqualTo("Station_Figurehead"));
        }

        [Test] public void MigrationIsAdditiveAndIdempotent()
        {
            var save = JsonUtility.FromJson<SaveData>("{\"unlockedIslands\":[\"coal\",\"iron\"],\"tutorialStep\":100}");
            save.shipyard = save.shipyard ?? new ShipyardProgression();
            save.shipyard.unlockedStations.Add("future_station");
            save.shipyard.Normalize(); save.shipyard.Normalize();
            Assert.That(save.unlockedIslands, Is.EqualTo(new[] { "coal", "iron" }));
            Assert.That(save.tutorialStep, Is.EqualTo(100));
            Assert.That(save.shipyard.unlockedStations.Count(x => x == "Station_Cannon"), Is.EqualTo(1));
            Assert.That(save.shipyard.unlockedStations, Does.Contain("future_station"));
        }

        [Test] public void ShipyardProgressSurvivesEncryptedSaveRoundTrip()
        {
            var save = new SaveData();
            save.shipyard.TryUnlockNext("Station_Hull", true, true);
            save.shipyard.materialInventory.Add(new ShipyardMaterialState { resourceId = "iron", quantity = 12.5d });
            save.shipyard.machines[0].activeRecipeId = ShipyardProgression.CannonStarterRecipeId;
            save.shipyard.machines[0].queueStartedUnix = 100L;
            save.shipyard.machines[0].queueFinishUnix = 160L;
            save.shipyard.machines[0].finishedOutputs.Add(new ShipyardFinishedItemState
            {
                itemId = "shipyard-item-1", recipeId = ShipyardProgression.CannonStarterRecipeId,
                machineId = ShipyardProgression.CannonMachineId, equipmentSlot = SeaCombat.SlotCannon,
                rarity = 1, shot = 7.5d, value = 42d, completedUnix = 170L
            });
            var service = new SaveService("shipyard-test-unused.dat");
            var restored = service.Decrypt(service.Encrypt(save), out bool tampered);
            Assert.That(tampered, Is.False);
            Assert.That(restored.shipyard.IsUnlocked("Station_Hull"), Is.True);
            Assert.That(restored.shipyard.NextMachine, Is.EqualTo("Station_Rigging"));
            Assert.That(restored.shipyard.materialInventory[0].quantity, Is.EqualTo(12.5d));
            Assert.That(restored.shipyard.machines[0].activeRecipeId, Is.EqualTo(ShipyardProgression.CannonStarterRecipeId));
            Assert.That(restored.shipyard.machines[0].queueFinishUnix, Is.EqualTo(160L));
            Assert.That(restored.shipyard.machines[0].finishedOutputs[0].itemId, Is.EqualTo("shipyard-item-1"));
            Assert.That(restored.shipyard.machines[0].finishedOutputs[0].shot, Is.EqualTo(7.5d));
        }

        [Test] public void ShipyardStateNormalizationRepairsMissingCollectionsAndInvalidQueue()
        {
            var p = new ShipyardProgression
            {
                materialInventory = null,
                machines = new System.Collections.Generic.List<ShipyardMachineState>
                {
                    new ShipyardMachineState
                    {
                        machineId = ShipyardProgression.CannonMachineId,
                        activeRecipeId = ShipyardProgression.CannonStarterRecipeId,
                        queueStartedUnix = 200L,
                        queueFinishUnix = 100L,
                        workerCapacity = 0,
                        queueCapacity = 0,
                        finishedOutputs = null
                    }
                },
                discoveredRecipeIds = null,
                reputation = -4,
                nextFinishedItemId = -8L
            };
            p.Normalize();
            Assert.That(p.materialInventory, Is.Not.Null);
            Assert.That(p.discoveredRecipeIds, Does.Contain(ShipyardProgression.CannonStarterRecipeId));
            Assert.That(p.reputation, Is.Zero);
            Assert.That(p.nextFinishedItemId, Is.Zero);
            Assert.That(p.machines[0].activeRecipeId, Is.Empty);
            Assert.That(p.machines[0].queueStartedUnix, Is.Zero);
            Assert.That(p.machines[0].workerCapacity, Is.EqualTo(1));
            Assert.That(p.machines[0].queueCapacity, Is.EqualTo(1));
            Assert.That(p.machines[0].finishedOutputs, Is.Not.Null);
        }

        [Test] public void FinishedItemIdsAreMonotonicAndAppendOnly()
        {
            var p = new ShipyardProgression();
            Assert.That(p.AllocateFinishedItemId(), Is.EqualTo(1L));
            Assert.That(p.AllocateFinishedItemId(), Is.EqualTo(2L));
            Assert.That(p.nextFinishedItemId, Is.EqualTo(2L));
        }

        [Test] public void CannonPullsMineAndRefineryStockThenConsumesItOnce()
        {
            var save = new SaveData();
            var recipe = ShipyardRecipeDefinition.CreateCannonStarterRuntime();
            long now = 100L;
            double coal = 4d, beams = 2d;
            var service = new CannonProductionService(save, null, null, null, null, recipe, () => now);
            service.BindMaterialSource(
                id => id == "coal" ? coal : id == "steel_beam" ? beams : 0d,
                (id, amount) =>
                {
                    if (id == "coal") { double n = System.Math.Min(coal, amount); coal -= n; return n; }
                    if (id == "steel_beam") { double n = System.Math.Min(beams, amount); beams -= n; return n; }
                    return 0d;
                });

            Assert.That(service.PullMaterialsFromSource(), Is.EqualTo(3d));
            Assert.That(coal, Is.EqualTo(2d));
            Assert.That(beams, Is.EqualTo(1d));
            Assert.That(service.TryStartCannon(), Is.True);
            Assert.That(service.MaterialQuantity("coal"), Is.Zero);
            Assert.That(service.MaterialQuantity("steel_beam"), Is.Zero);
            Assert.That(service.IsRunning, Is.True);
            now = 105L;
            Assert.That(service.Poll(), Is.True);
            Assert.That(service.HasFinishedOutput, Is.True);
            Assert.That(service.Poll(), Is.False);
            Assert.That(service.FinishedOutputAt(0).itemId, Is.EqualTo("ShipyardItem_1"));
            Object.DestroyImmediate(recipe);
        }

        [Test] public void CannonCompletesOfflineQueueWithoutDoubleReward()
        {
            var recipe = ShipyardRecipeDefinition.CreateCannonStarterRuntime();
            var save = new SaveData();
            save.shipyard.Normalize();
            var machine = save.shipyard.machines[0];
            machine.activeRecipeId = recipe.RecipeId;
            machine.queueStartedUnix = 10L;
            machine.queueFinishUnix = 20L;
            long now = 100L;

            var service = new CannonProductionService(save, null, null, null, null, recipe, () => now);
            Assert.That(service.HasFinishedOutput, Is.True);
            Assert.That(service.FinishedOutputAt(0).itemId, Is.EqualTo("ShipyardItem_1"));
            Assert.That(service.Poll(), Is.False);
            Assert.That(save.shipyard.machines[0].finishedOutputs.Count, Is.EqualTo(1));
            Object.DestroyImmediate(recipe);
        }

        [Test] public void CannonOrderAndSellAreIdempotentAndPersistProgress()
        {
            var recipe = ShipyardRecipeDefinition.CreateCannonStarterRuntime();
            var save = new SaveData();
            var wallet = new WalletService(save.wallet);
            long now = 100L;
            var service = new CannonProductionService(save, null, null, wallet, null, recipe, () => now);
            save.shipyard.materialInventory.Add(new ShipyardMaterialState { resourceId = "coal", quantity = 2d });
            save.shipyard.materialInventory.Add(new ShipyardMaterialState { resourceId = "steel_beam", quantity = 1d });
            Assert.That(service.TryStartCannon(), Is.True);
            now = 105L;
            service.Poll();
            string itemId = service.FinishedOutputAt(0).itemId;
            Assert.That(service.FulfillActiveCannonOrder(itemId), Is.True);
            Assert.That(service.FulfillActiveCannonOrder(itemId), Is.False);
            Assert.That(save.shipyard.completedOrders, Is.EqualTo(1));
            Assert.That(save.shipyard.cannonOrder.status, Is.EqualTo(ShipyardCustomerOrderState.Fulfilled));
            Assert.That(wallet.Cash.ToDouble(), Is.EqualTo(250d).Within(0.0001d));
            Object.DestroyImmediate(recipe);
        }

        [Test] public void ShipyardUnlockNeedsOrderReputationCashAndArtBeforeSpending()
        {
            var save = new SaveData();
            save.shipyard.completedOrders = 1;
            save.shipyard.reputation = 1;
            var wallet = new WalletService(save.wallet);
            wallet.AddCash(new BigDouble(300d));
            long now = 100L;
            var unlock = new ShipyardUnlockService(save, null, null, wallet,
                id => id != ShipyardProgression.HullMachineId, () => now);

            Assert.That(unlock.TryBeginNextConstruction(), Is.False);
            Assert.That(wallet.Cash.ToDouble(), Is.EqualTo(300d).Within(0.0001d));

            var ready = new ShipyardUnlockService(save, null, null, wallet,
                id => true, () => now);
            Assert.That(ready.TryBeginNextConstruction(), Is.True);
            Assert.That(wallet.Cash.ToDouble(), Is.Zero);
            Assert.That(save.shipyard.IsBuilt(ShipyardProgression.HullMachineId), Is.False);
            Assert.That(ready.CurrentConstruction.machineId, Is.EqualTo(ShipyardProgression.HullMachineId));
        }

        [Test] public void ShipyardConstructionCompletesOfflineAndRequestsOneFocus()
        {
            var save = new SaveData();
            save.shipyard.completedOrders = 1;
            save.shipyard.reputation = 1;
            var wallet = new WalletService(save.wallet);
            wallet.AddCash(new BigDouble(300d));
            long now = 100L;
            var unlock = new ShipyardUnlockService(save, null, null, wallet, id => true, () => now);
            Assert.That(unlock.TryBeginNextConstruction(), Is.True);
            now = 105L;
            Assert.That(unlock.Poll(), Is.True);
            Assert.That(save.shipyard.IsBuilt(ShipyardProgression.HullMachineId), Is.True);
            Assert.That(unlock.FocusTargetMachineId, Is.EqualTo(ShipyardProgression.HullMachineId));
            Assert.That(unlock.TryConsumeFocusTarget(out string id), Is.True);
            Assert.That(id, Is.EqualTo(ShipyardProgression.HullMachineId));
            Assert.That(unlock.TryConsumeFocusTarget(out id), Is.False);
        }

        [Test] public void ShipyardUnlockLadderCannotSkipAndReachesEveryReadyMachine()
        {
            var save = new SaveData();
            save.shipyard.completedOrders = 10;
            save.shipyard.reputation = 10;
            var wallet = new WalletService(save.wallet);
            wallet.AddCash(new BigDouble(5550d));
            long now = 100L;
            var unlock = new ShipyardUnlockService(save, null, null, wallet, id => true, () => now);

            string[] expected =
            {
                ShipyardProgression.HullMachineId,
                ShipyardProgression.RiggingMachineId,
                ShipyardProgression.NavigationMachineId,
                ShipyardProgression.FigureheadMachineId
            };
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(unlock.NextMachine, Is.EqualTo(expected[i]));
                Assert.That(unlock.TryBeginNextConstruction(), Is.True);
                now += unlock.RuleFor(expected[i]).ConstructionSeconds;
                Assert.That(unlock.Poll(), Is.True);
                Assert.That(save.shipyard.IsBuilt(expected[i]), Is.True);
            }
            Assert.That(unlock.NextMachine, Is.Null);
        }

        [Test] public void ManifestHasCompleteUniqueContractAndIndependentBuildings()
        {
            var manifest = JsonUtility.FromJson<ShipyardMapManifest>(Resources.Load<TextAsset>("Shipyard/Map").text);
            Assert.That(manifest.anchors.Length, Is.EqualTo(45));
            Assert.That(manifest.anchors.Select(x => x.id).Distinct().Count(), Is.EqualTo(45));
            Assert.That(manifest.routes.Length, Is.EqualTo(17));
            Assert.That(manifest.zones.Length, Is.EqualTo(5));
            Assert.That(manifest.zones.Where(x => !x.needsArt).Select(x => x.artGroup).Distinct().Count(), Is.EqualTo(4));
            Assert.That(manifest.zones[4].needsArt, Is.True);
            foreach (var r in manifest.routes)
            {
                Assert.That(r.points.Length, Is.GreaterThanOrEqualTo(2));
                Assert.That(manifest.anchors.Any(a => a.id == r.from), Is.True, r.id);
                Assert.That(manifest.anchors.Any(a => a.id == r.to), Is.True, r.id);
            }
        }

        [Test] public void CameraPanCannotDriftHorizontallyOrExceedStops()
        {
            var go = new GameObject("test camera");
            try
            {
                var c = go.AddComponent<Camera>(); c.orthographicSize = 10;
                go.transform.rotation = Quaternion.LookRotation(new Vector3(0, -.72f, .69f));
                var p = go.AddComponent<PortraitShipyardCamera>();
                p.origin = new Vector3(0, 43, -38); p.minTravel = -12; p.maxTravel = 12;
                p.PanPixels(100000, 2340); Assert.That(p.travel, Is.EqualTo(-12));
                p.PanPixels(-100000, 2340); Assert.That(p.travel, Is.EqualTo(12));
                p.Focus(new Vector3(400, 5, 6), true);
                Assert.That(go.transform.position.x, Is.EqualTo(0).Within(.00001));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test] public void RemainingMachineStartersUseStableSlotsAndStayHiddenUntilUnlocked()
        {
            var save = new SaveData();
            save.shipyard.Normalize();
            Assert.That(save.shipyard.discoveredRecipeIds,
                Is.EqualTo(new[] { ShipyardProgression.CannonStarterRecipeId }));

            var recipes = ShipyardRecipeDefinition.CreateStarterRuntimeSet();
            try
            {
                Assert.That(recipes.Length, Is.EqualTo(5));
                Assert.That(recipes.Select(x => x.OutputEquipmentSlot), Is.EqualTo(new[]
                {
                    SeaCombat.SlotCannon, SeaCombat.SlotPlating, SeaCombat.SlotRigging,
                    SeaCombat.SlotSpyglass, SeaCombat.SlotCharm
                }));
                Assert.That(recipes.Select(x => x.RecipeId).Distinct().Count(), Is.EqualTo(5));

                for (int i = 1; i < ShipyardProgression.MachineIds.Length; i++)
                    Assert.That(save.shipyard.TryUnlockNext(ShipyardProgression.MachineIds[i], true, true), Is.True);
                Assert.That(save.shipyard.discoveredRecipeIds, Does.Contain(ShipyardProgression.HullStarterRecipeId));
                Assert.That(save.shipyard.discoveredRecipeIds, Does.Contain(ShipyardProgression.RiggingStarterRecipeId));
                Assert.That(save.shipyard.discoveredRecipeIds, Does.Contain(ShipyardProgression.NavigationStarterRecipeId));
                Assert.That(save.shipyard.discoveredRecipeIds, Does.Contain(ShipyardProgression.FigureheadStarterRecipeId));
            }
            finally
            {
                for (int i = 0; i < recipes.Length; i++) Object.DestroyImmediate(recipes[i]);
            }
        }

        [Test] public void RiggingAppendsSlotFourWithoutRemappingLegacyGear()
        {
            var save = new SaveData
            {
                seaGearGrade = new[] { 1, 2, 3, 4 },
                seaGearPower = new[] { 10, 20, 30, 40 },
                seaGearHull = new[] { 1d, 2d, 3d, 4d },
                seaGearShot = new[] { 5d, 6d, 7d, 8d },
                seaGearSec = new[] { 0, 0, 0, 0 },
                seaGearSecAmt = new double[4],
                seaGearDef = new[] { 1d, 2d, 3d, 4d },
                seaGearSpd = new[] { 1d, 2d, 3d, 4d }
            };
            var sea = new ExpeditionService(null, null, save);

            Assert.That(SeaCombat.LegacySlotCount, Is.EqualTo(4));
            Assert.That(SeaCombat.SlotRigging, Is.EqualTo(4));
            Assert.That(SeaCombat.SlotCount, Is.EqualTo(5));
            Assert.That(save.seaGearGrade.Length, Is.EqualTo(5));
            Assert.That(save.seaGearGrade.Take(4), Is.EqualTo(new[] { 1, 2, 3, 4 }));
            Assert.That(save.seaGearGrade[SeaCombat.SlotRigging], Is.Zero);
            Assert.That(sea.Equip(SeaCombat.ItemFor(SeaCombat.SlotRigging, 0, 0, 0d,
                                                     SeaCombat.Tuning.Default)), Is.Zero);
            Assert.That(sea.GearGrade(SeaCombat.SlotRigging), Is.Zero);
        }

        [Test] public void AllMachineFamiliesShareQueueOutputAndCustomerContract()
        {
            var save = new SaveData();
            for (int i = 1; i < ShipyardProgression.MachineIds.Length; i++)
                Assert.That(save.shipyard.TryUnlockNext(ShipyardProgression.MachineIds[i], true, true), Is.True);
            foreach (string id in new[] { "coal", "steel_beam", "copper_bar", "silver_bar", "gold_bar", "cut_ruby" })
                save.shipyard.materialInventory.Add(new ShipyardMaterialState { resourceId = id, quantity = 20d });

            long now = 100L;
            var service = new CannonProductionService(save, null, null, null, null, null, () => now);
            try
            {
                string[] machines =
                {
                    ShipyardProgression.CannonMachineId, ShipyardProgression.HullMachineId,
                    ShipyardProgression.RiggingMachineId, ShipyardProgression.NavigationMachineId,
                    ShipyardProgression.FigureheadMachineId
                };
                for (int i = 0; i < machines.Length; i++)
                {
                    string machineId = machines[i];
                    ShipyardRecipeDefinition recipe = service.RecipeFor(machineId);
                    Assert.That(recipe, Is.Not.Null, machineId);
                    Assert.That(service.TryStart(machineId), Is.True, machineId);
                    now += (long)System.Math.Ceiling(recipe.ProductionDurationSeconds);
                    Assert.That(service.Poll(), Is.True, machineId);
                    ShipyardFinishedItemState output = service.FinishedOutputAt(machineId, 0);
                    Assert.That(output, Is.Not.Null, machineId);
                    Assert.That(output.machineId, Is.EqualTo(machineId));
                    Assert.That(output.equipmentSlot, Is.EqualTo(recipe.OutputEquipmentSlot));
                    Assert.That(service.OrderFor(machineId), Is.Not.Null);
                    Assert.That(service.FulfillOrder(machineId, output.itemId), Is.True, machineId);
                }
                Assert.That(save.shipyard.completedOrders, Is.EqualTo(5));
                Assert.That(save.shipyard.reputation, Is.EqualTo(5));
            }
            finally
            {
                // Runtime recipe instances are not assets and are safe to release after the contract test.
                foreach (string id in new[] { ShipyardProgression.CannonMachineId, ShipyardProgression.HullMachineId,
                                               ShipyardProgression.RiggingMachineId, ShipyardProgression.NavigationMachineId,
                                               ShipyardProgression.FigureheadMachineId })
                    Object.DestroyImmediate(service.RecipeFor(id));
            }
        }

        [Test] public void RecipeTiersStayHiddenUntilTheirMachineAndMaterialGatesAreMet()
        {
            var save = new SaveData();
            var service = new CannonProductionService(save, null, null, null, null, null, () => 100L);
            try
            {
                Assert.That(service.RecipesFor(ShipyardProgression.CannonMachineId).Count, Is.EqualTo(2));
                Assert.That(save.shipyard.discoveredRecipeIds,
                    Is.EqualTo(new[] { ShipyardProgression.CannonStarterRecipeId }));
                Assert.That(service.DiscoveredRecipesFor(ShipyardProgression.CannonMachineId).Count, Is.EqualTo(1));
                Assert.That(service.DiscoverAvailableRecipes(), Is.Zero);

                save.shipyard.completedOrders = 2;
                save.shipyard.reputation = 2;
                save.shipyard.materialInventory.Add(new ShipyardMaterialState { resourceId = "coal", quantity = 1d });
                save.shipyard.materialInventory.Add(new ShipyardMaterialState { resourceId = "copper", quantity = 1d });
                Assert.That(service.DiscoverAvailableRecipes(), Is.EqualTo(1));
                Assert.That(save.shipyard.discoveredRecipeIds, Does.Contain("Recipe_Cannon_02"));
                Assert.That(service.DiscoveredRecipesFor(ShipyardProgression.CannonMachineId).Count, Is.EqualTo(2));

                Assert.That(save.shipyard.TryUnlockNext(ShipyardProgression.HullMachineId, true, true), Is.True);
                Assert.That(save.shipyard.TryUnlockNext(ShipyardProgression.RiggingMachineId, true, true), Is.True);
                Assert.That(save.shipyard.TryUnlockNext(ShipyardProgression.NavigationMachineId, true, true), Is.True);
                Assert.That(save.shipyard.TryUnlockNext(ShipyardProgression.FigureheadMachineId, true, true), Is.True);
                save.shipyard.completedOrders = 4;
                save.shipyard.reputation = 4;
                save.shipyard.materialInventory.Add(new ShipyardMaterialState { resourceId = "iron", quantity = 1d });
                save.shipyard.materialInventory.Add(new ShipyardMaterialState { resourceId = "silver", quantity = 1d });
                Assert.That(service.DiscoverAvailableRecipes(), Is.EqualTo(1));
                Assert.That(save.shipyard.discoveredRecipeIds, Does.Contain("Recipe_Hull_02"));
            }
            finally
            {
                foreach (string machine in ShipyardProgression.MachineIds)
                    foreach (ShipyardRecipeDefinition recipe in service.RecipesFor(machine)) Object.DestroyImmediate(recipe);
            }
        }

        [Test] public void DemandIsBoundedAndCanRotateBackToAnOlderMachine()
        {
            var save = new SaveData();
            for (int i = 1; i < ShipyardProgression.MachineIds.Length; i++)
                Assert.That(save.shipyard.TryUnlockNext(ShipyardProgression.MachineIds[i], true, true), Is.True);
            var service = new CannonProductionService(save, null, null, null, null, null, () => 100L);
            try
            {
                Assert.That(service.RefreshOrders(), Is.EqualTo(CannonProductionService.MaxWaitingOrders - 1));
                Assert.That(service.PendingOrderCount, Is.EqualTo(CannonProductionService.MaxWaitingOrders));
                save.shipyard.cannonOrder.status = ShipyardCustomerOrderState.Fulfilled;
                Assert.That(service.RefreshOrders(), Is.EqualTo(1));
                Assert.That(save.shipyard.cannonOrder.status, Is.EqualTo(ShipyardCustomerOrderState.Active));
                Assert.That(save.shipyard.cannonOrder.machineId, Is.EqualTo(ShipyardProgression.CannonMachineId));
            }
            finally
            {
                foreach (string machine in ShipyardProgression.MachineIds)
                    foreach (ShipyardRecipeDefinition recipe in service.RecipesFor(machine)) Object.DestroyImmediate(recipe);
            }
        }

        [Test] public void TieredEquipmentDemandKeepsSeaCombatStateSeparate()
        {
            var save = new SaveData();
            save.shipyard.completedOrders = 2;
            save.shipyard.reputation = 2;
            save.shipyard.materialInventory.Add(new ShipyardMaterialState { resourceId = "coal", quantity = 1d });
            save.shipyard.materialInventory.Add(new ShipyardMaterialState { resourceId = "copper", quantity = 1d });
            long now = 100L;
            var service = new CannonProductionService(save, null, null, null, null, null, () => now);
            try
            {
                Assert.That(service.DiscoverAvailableRecipes(), Is.EqualTo(1));
                var recipe = service.RecipeFor(ShipyardProgression.CannonMachineId, "Recipe_Cannon_02");
                save.shipyard.materialInventory.Add(new ShipyardMaterialState { resourceId = "coke", quantity = 2d });
                save.shipyard.materialInventory.Add(new ShipyardMaterialState { resourceId = "copper_bar", quantity = 2d });
                Assert.That(service.TryStart(ShipyardProgression.CannonMachineId, recipe.RecipeId), Is.True);
                now += (long)System.Math.Ceiling(recipe.ProductionDurationSeconds);
                Assert.That(service.Poll(), Is.True);
                string itemId = service.FinishedOutputAt(ShipyardProgression.CannonMachineId, 0).itemId;
                int seaTier = save.seaTier;
                long salvage = save.salvage;
                Assert.That(service.FulfillOrder(ShipyardProgression.CannonMachineId, itemId), Is.True);
                Assert.That(save.seaTier, Is.EqualTo(seaTier));
                Assert.That(save.salvage, Is.EqualTo(salvage));
            }
            finally
            {
                foreach (string machine in ShipyardProgression.MachineIds)
                    foreach (ShipyardRecipeDefinition recipe in service.RecipesFor(machine)) Object.DestroyImmediate(recipe);
            }
        }

        [Test] public void CompactHudRejectsLegacyExtraOpeners()
        {
            var go = new GameObject("compact hud");
            try
            {
                var hud = go.AddComponent<HudUI>();
                Assert.That(hud.AttachBottomButton(0, "unused", null, null), Is.Null);
                Assert.That(go.transform.childCount, Is.Zero);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test] public void HudSafeAreaNormalizesNotchAndGestureInsets()
        {
            Rect normalized = HudUI.SafeAreaNormalized(new Rect(54f, 96f, 972f, 1728f),
                                                       new Vector2(1080f, 1920f));
            Assert.That(normalized.xMin, Is.EqualTo(0.05f).Within(0.0001f));
            Assert.That(normalized.yMin, Is.EqualTo(0.05f).Within(0.0001f));
            Assert.That(normalized.xMax, Is.EqualTo(0.95f).Within(0.0001f));
            Assert.That(normalized.yMax, Is.EqualTo(0.95f).Within(0.0001f));
        }
    }
}
