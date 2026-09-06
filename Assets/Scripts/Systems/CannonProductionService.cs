using System;
using System.Collections.Generic;
using Game.Core;
using Game.Data;

namespace Game.Systems
{
    /// <summary>
    /// Persistent production contract for the five ship-item machine families. The historical
    /// CannonProductionService name is retained so the first vertical-slice UI and callers remain
    /// source compatible; every machine now uses the same queue, material, output, and decision path.
    /// </summary>
    public sealed class CannonProductionService
    {
        private readonly SaveData _data;
        private readonly SaveService _save;
        private readonly TimeService _time;
        private readonly WalletService _wallet;
        private readonly ExpeditionService _expeditions;
        private readonly ShipyardRecipeDefinition _recipe;
        private readonly Func<long> _now;
        private readonly Dictionary<string, ShipyardRecipeDefinition> _recipesById =
            new Dictionary<string, ShipyardRecipeDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ShipyardRecipeDefinition>> _recipesByMachine =
            new Dictionary<string, List<ShipyardRecipeDefinition>>(StringComparer.Ordinal);

        private Func<string, double> _sourceAvailable;
        private Func<string, double, double> _sourceTake;

        public event Action Changed;
        public event Action<ShipyardFinishedItemState> Completed;

        public CannonProductionService(SaveData data, SaveService save, TimeService time,
                                       WalletService wallet = null,
                                       ExpeditionService expeditions = null,
                                       ShipyardRecipeDefinition recipe = null,
                                       Func<long> now = null,
                                       IEnumerable<ShipyardRecipeDefinition> recipes = null)
        {
            _data = data ?? new SaveData();
            _save = save;
            _time = time;
            _wallet = wallet;
            _expeditions = expeditions;
            _recipe = recipe != null ? recipe : ShipyardRecipeDefinition.CreateCannonStarterRuntime();
            _now = now;
            if (recipes != null)
            {
                foreach (ShipyardRecipeDefinition definition in recipes) RegisterRecipe(definition);
            }
            else
            {
                ShipyardRecipeDefinition[] starterSet = ShipyardRecipeDefinition.CreateTieredRuntimeSet();
                for (int i = 0; i < starterSet.Length; i++) RegisterRecipe(starterSet[i]);
            }
            RegisterRecipe(_recipe); // An Inspector-authored Cannon definition overrides the fallback.
            _data.shipyard.Normalize();
            Poll(); // Completes queues that crossed their deadlines while the app was closed.
        }

        public ShipyardRecipeDefinition Recipe => _recipe;
        public ShipyardProgression Progress => _data.shipyard;
        public ShipyardMachineState CannonMachine => FindMachine(ShipyardProgression.CannonMachineId);
        public ShipyardCustomerOrderState ActiveOrder => _data.shipyard.cannonOrder;

        public bool IsRunning => CannonMachine != null && !string.IsNullOrEmpty(CannonMachine.activeRecipeId);
        public bool HasFinishedOutput => HasFinishedOutputFor(ShipyardProgression.CannonMachineId);
        public const int MaxWaitingOrders = 3;

        public ShipyardRecipeDefinition RecipeFor(string machineId)
        {
            if (string.IsNullOrEmpty(machineId)) return null;
            List<ShipyardRecipeDefinition> recipes;
            if (!_recipesByMachine.TryGetValue(machineId, out recipes)) return null;
            for (int i = 0; i < recipes.Count; i++)
                if (IsRecipeDiscovered(recipes[i].RecipeId)) return recipes[i];
            return recipes.Count > 0 ? recipes[0] : null;
        }

        public ShipyardRecipeDefinition RecipeFor(string machineId, string recipeId)
        {
            if (string.IsNullOrEmpty(recipeId)) return RecipeFor(machineId);
            ShipyardRecipeDefinition recipe;
            return _recipesById.TryGetValue(recipeId, out recipe) && recipe.MachineFamilyId == machineId
                ? recipe : null;
        }

        public IReadOnlyList<ShipyardRecipeDefinition> RecipesFor(string machineId) 
        {
            List<ShipyardRecipeDefinition> recipes;
            return _recipesByMachine.TryGetValue(machineId, out recipes) ? recipes : Array.Empty<ShipyardRecipeDefinition>();
        }

        /// <summary>
        /// The contextual machine panel must never advertise a recipe that the player has not
        /// discovered yet. Keep this filtered view beside the discovery rules so UI callers do not
        /// duplicate the save-gate logic.
        /// </summary>
        public IReadOnlyList<ShipyardRecipeDefinition> DiscoveredRecipesFor(string machineId)
        {
            List<ShipyardRecipeDefinition> recipes;
            if (!_recipesByMachine.TryGetValue(machineId, out recipes))
                return Array.Empty<ShipyardRecipeDefinition>();

            List<ShipyardRecipeDefinition> discovered = new List<ShipyardRecipeDefinition>(recipes.Count);
            for (int i = 0; i < recipes.Count; i++)
                if (IsRecipeDiscovered(recipes[i].RecipeId)) discovered.Add(recipes[i]);
            return discovered;
        }

        /// <summary>Discovers only recipes whose machine, material, order, and reputation gates are met.</summary>
        public int DiscoverAvailableRecipes()
        {
            _data.shipyard.Normalize();
            int discovered = 0;
            foreach (List<ShipyardRecipeDefinition> recipes in _recipesByMachine.Values)
                for (int i = 0; i < recipes.Count; i++)
                {
                    ShipyardRecipeDefinition recipe = recipes[i];
                    if (!CanDiscover(recipe) || _data.shipyard.discoveredRecipeIds.Contains(recipe.RecipeId)) continue;
                    _data.shipyard.discoveredRecipeIds.Add(recipe.RecipeId);
                    discovered++;
                }
            if (discovered > 0)
            {
                Commit();
                Changed?.Invoke();
            }
            return discovered;
        }

        public int PendingOrderCount
        {
            get
            {
                int count = _data.shipyard.cannonOrder != null
                    && _data.shipyard.cannonOrder.status == ShipyardCustomerOrderState.Active ? 1 : 0;
                List<ShipyardCustomerOrderState> orders = _data.shipyard.machineOrders;
                for (int i = 0; i < orders.Count; i++)
                    if (orders[i] != null && orders[i].status == ShipyardCustomerOrderState.Active) count++;
                return count;
            }
        }

        /// <summary>
        /// Keeps demand bounded and rotates a completed machine back into the order board. The
        /// board prefers the highest discovered tier for that family, while older families remain
        /// eligible and never disappear just because a later station was built.
        /// </summary>
        public int RefreshOrders()
        {
            DiscoverAvailableRecipes();
            _data.shipyard.Normalize();
            int created = 0;
            int pending = PendingOrderCount;
            for (int i = 0; i < ShipyardProgression.MachineIds.Length; i++)
            {
                if (pending >= MaxWaitingOrders) break;
                string machineId = ShipyardProgression.MachineIds[i];
                ShipyardMachineState machine = FindMachine(machineId);
                if (machine == null || machine.constructionState != ShipyardMachineState.Built) continue;
                ShipyardCustomerOrderState order = FindOrder(machineId);
                if (order != null && order.status == ShipyardCustomerOrderState.Active) continue;
                ShipyardRecipeDefinition recipe = LatestDiscoveredRecipe(machineId);
                if (recipe == null) continue;
                if (order == null) order = EnsureOrder(machineId, recipe);
                else ResetOrder(order, machineId, recipe);
                if (order != null) { pending++; created++; }
            }
            if (created > 0) Commit();
            return created;
        }

        public ShipyardMachineState MachineFor(string machineId) => FindMachine(machineId);

        /// <summary>Connects the live island's mine/depot/refinery buffers to the input bins.</summary>
        public void BindMaterialSource(Func<string, double> available, Func<string, double, double> take)
        {
            _sourceAvailable = available;
            _sourceTake = take;
        }

        public double PullMaterialsFromSource() => PullMaterialsFromSource(ShipyardProgression.CannonMachineId);

        /// <summary>
        /// Moves only the requested machine's ingredients from the bound live source. The same
        /// inventory can feed all machines, and the operation is idempotent once a bin is full.
        /// </summary>
        public double PullMaterialsFromSource(string machineId)
            => PullMaterialsFromSourceForRecipe(machineId, RecipeFor(machineId));

        private double PullMaterialsFromSourceForRecipe(string machineId, ShipyardRecipeDefinition recipe)
        {
            if (_sourceAvailable == null || _sourceTake == null) return 0d;
            ShipyardMachineState machine = FindMachine(machineId);
            if (recipe == null || machine == null || machine.constructionState != ShipyardMachineState.Built)
                return 0d;

            double moved = 0d;
            ShipyardRecipeDefinition.Ingredient[] ingredients = recipe.Ingredients;
            if (ingredients == null) return moved;
            for (int i = 0; i < ingredients.Length; i++)
            {
                ShipyardRecipeDefinition.Ingredient ingredient = ingredients[i];
                if (string.IsNullOrEmpty(ingredient.ResourceId) || ingredient.Quantity <= 0d) continue;
                double need = ingredient.Quantity - MaterialQuantity(ingredient.ResourceId);
                if (need <= 0d) continue;
                double available = _sourceAvailable(ingredient.ResourceId);
                if (available <= 0d || double.IsNaN(available) || double.IsInfinity(available)) continue;
                double taken = _sourceTake(ingredient.ResourceId, Math.Min(need, available));
                if (taken <= 0d || double.IsNaN(taken) || double.IsInfinity(taken)) continue;
                AddMaterial(ingredient.ResourceId, taken);
                moved += taken;
            }
            if (moved > 0d)
            {
                Commit();
                Changed?.Invoke();
            }
            return moved;
        }

        public double PullMaterialsFromSource(string machineId, string recipeId)
        {
            ShipyardRecipeDefinition selected = RecipeFor(machineId, recipeId);
            if (selected == null) return 0d;
            return PullMaterialsFromSourceForRecipe(machineId, selected);
        }

        public double MaterialQuantity(string resourceId)
        {
            if (string.IsNullOrEmpty(resourceId)) return 0d;
            List<ShipyardMaterialState> materials = _data.shipyard.materialInventory;
            for (int i = 0; i < materials.Count; i++)
                if (materials[i] != null && materials[i].resourceId == resourceId)
                    return materials[i].quantity;
            return 0d;
        }

        public bool TryStartCannon() => TryStart(ShipyardProgression.CannonMachineId);

        public bool TryStart(string machineId)
            => TryStart(machineId, null);

        public bool TryStart(string machineId, string recipeId)
        {
            Poll();
            DiscoverAvailableRecipes();
            ShipyardRecipeDefinition recipe = RecipeFor(machineId, recipeId);
            ShipyardMachineState machine = FindMachine(machineId);
            if (recipe == null || machine == null || machine.constructionState != ShipyardMachineState.Built
                || !string.IsNullOrEmpty(machine.activeRecipeId) || !IsRecipeDiscovered(recipe.RecipeId))
                return false;
            if (machine.finishedOutputs != null && machine.finishedOutputs.Count >= machine.queueCapacity)
                return false;
            if (!HasIngredients(recipe)) return false;

            ShipyardRecipeDefinition.Ingredient[] ingredients = recipe.Ingredients;
            for (int i = 0; ingredients != null && i < ingredients.Length; i++)
                RemoveMaterial(ingredients[i].ResourceId, ingredients[i].Quantity);

            long started = NowUnix();
            long duration = Math.Max(1L, (long)Math.Ceiling(recipe.ProductionDurationSeconds));
            machine.activeRecipeId = recipe.RecipeId;
            machine.queueStartedUnix = started;
            machine.queueFinishUnix = started + duration;
            EnsureOrder(machineId, recipe);
            Commit();
            Changed?.Invoke();
            return true;
        }

        /// <summary>Settles every machine's completed queue once; repeated calls cannot duplicate output.</summary>
        public bool Poll()
        {
            bool changed = false;
            for (int i = 0; i < ShipyardProgression.MachineIds.Length; i++)
                changed |= PollMachine(ShipyardProgression.MachineIds[i]);
            return changed;
        }

        public ShipyardFinishedItemState FinishedOutputAt(int index)
            => FinishedOutputAt(ShipyardProgression.CannonMachineId, index);

        public ShipyardFinishedItemState FinishedOutputAt(string machineId, int index)
        {
            ShipyardMachineState machine = FindMachine(machineId);
            return machine != null && machine.finishedOutputs != null
                   && index >= 0 && index < machine.finishedOutputs.Count
                ? machine.finishedOutputs[index]
                : null;
        }

        public bool HasFinishedOutputFor(string machineId)
        {
            ShipyardMachineState machine = FindMachine(machineId);
            return machine != null && machine.finishedOutputs != null && machine.finishedOutputs.Count > 0;
        }

        public bool SellOutput(string itemId) => SellOutput(ShipyardProgression.CannonMachineId, itemId);

        public bool SellOutput(string machineId, string itemId)
        {
            if (_wallet == null) return false;
            ShipyardFinishedItemState output;
            if (!RemoveOutput(machineId, itemId, out output)) return false;
            _wallet.AddCash(new BigDouble(Math.Max(0d, output.value)));
            Commit();
            Changed?.Invoke();
            return true;
        }

        public bool EquipOutput(string itemId) => EquipOutput(ShipyardProgression.CannonMachineId, itemId);

        public bool EquipOutput(string machineId, string itemId)
        {
            ShipyardFinishedItemState output;
            if (_expeditions == null || !RemoveOutput(machineId, itemId, out output)) return false;
            _expeditions.Equip(ToItem(output));
            Commit();
            Changed?.Invoke();
            return true;
        }

        public bool StoreOutput(string itemId) => StoreOutput(ShipyardProgression.CannonMachineId, itemId);

        public bool StoreOutput(string machineId, string itemId)
        {
            if (_expeditions == null || !_expeditions.StashHasRoom) return false;
            ShipyardFinishedItemState output;
            if (!RemoveOutput(machineId, itemId, out output)) return false;
            if (_expeditions.Stow(ToItem(output)))
            {
                Commit();
                Changed?.Invoke();
                return true;
            }
            RestoreOutput(machineId, output);
            return false;
        }

        public long SalvageOutput(string itemId) => SalvageOutput(ShipyardProgression.CannonMachineId, itemId);

        public long SalvageOutput(string machineId, string itemId)
        {
            ShipyardFinishedItemState output;
            if (!RemoveOutput(machineId, itemId, out output)) return 0L;
            long salvage = SeaCombat.ScrapFor(output.rarity);
            if (_expeditions != null) _expeditions.Scrap(output.rarity);
            else _data.salvage += salvage;
            Commit();
            Changed?.Invoke();
            return salvage;
        }

        public bool FulfillActiveCannonOrder(string itemId)
            => FulfillOrder(ShipyardProgression.CannonMachineId, itemId);

        /// <summary>Completes the current customer order for any built machine family.</summary>
        public bool FulfillOrder(string machineId, string itemId)
        {
            ShipyardMachineState machine = FindMachine(machineId);
            ShipyardFinishedItemState candidate = FindOutput(machine, itemId);
            ShipyardRecipeDefinition recipe = candidate != null
                ? RecipeFor(machineId, candidate.recipeId) : RecipeFor(machineId);
            ShipyardCustomerOrderState order = EnsureOrder(machineId, recipe);
            if (recipe == null || order == null || order.status != ShipyardCustomerOrderState.Active
                || order.fulfilledQuantity >= order.requiredQuantity)
                return false;
            ShipyardFinishedItemState output;
            if (!RemoveOutput(machineId, itemId, out output) || output.recipeId != order.recipeId)
            {
                if (output != null) RestoreOutput(machineId, output);
                return false;
            }
            order.fulfilledQuantity++;
            order.status = order.fulfilledQuantity >= order.requiredQuantity
                ? ShipyardCustomerOrderState.Fulfilled : ShipyardCustomerOrderState.Active;
            _data.shipyard.completedOrders++;
            _data.shipyard.reputation++;
            if (_wallet != null) _wallet.AddCash(new BigDouble(order.rewardCash));
            Commit();
            Changed?.Invoke();
            return true;
        }

        public ShipyardCustomerOrderState OrderFor(string machineId)
            => EnsureOrder(machineId, RecipeFor(machineId));

        private bool PollMachine(string machineId)
        {
            ShipyardMachineState machine = FindMachine(machineId);
            if (machine == null || string.IsNullOrEmpty(machine.activeRecipeId)
                || NowUnix() < machine.queueFinishUnix) return false;
            ShipyardRecipeDefinition recipe = RecipeFor(machineId, machine.activeRecipeId);
            if (recipe == null) return false;

            var output = new ShipyardFinishedItemState
            {
                itemId = "ShipyardItem_" + _data.shipyard.AllocateFinishedItemId(),
                recipeId = recipe.RecipeId,
                machineId = machineId,
                equipmentSlot = recipe.OutputEquipmentSlot,
                rarity = 0,
                hull = recipe.BaseStats.Hull,
                shot = recipe.BaseStats.Shot,
                defence = recipe.BaseStats.Defence,
                speed = recipe.BaseStats.Speed,
                secondaryAmount = recipe.BaseStats.SecondaryAmount,
                value = recipe.BaseStats.BaseValue,
                completedUnix = NowUnix()
            };
            if (machine.finishedOutputs == null)
                machine.finishedOutputs = new List<ShipyardFinishedItemState>();
            machine.finishedOutputs.Add(output);
            machine.activeRecipeId = "";
            machine.queueStartedUnix = 0L;
            machine.queueFinishUnix = 0L;
            EnsureOrder(machineId, recipe);
            Commit();
            Completed?.Invoke(output);
            Changed?.Invoke();
            return true;
        }

        private ShipyardCustomerOrderState EnsureOrder(string machineId, ShipyardRecipeDefinition recipe)
        {
            if (recipe == null || string.IsNullOrEmpty(machineId)) return null;
            _data.shipyard.Normalize();
            ShipyardMachineState machine = FindMachine(machineId);
            if (machine == null || machine.constructionState != ShipyardMachineState.Built) return null;
            if (machineId == ShipyardProgression.CannonMachineId)
            {
                ShipyardCustomerOrderState cannon = _data.shipyard.cannonOrder;
                cannon.machineId = machineId;
                cannon.recipeId = recipe.RecipeId;
                cannon.Normalize();
                return cannon;
            }

            List<ShipyardCustomerOrderState> orders = _data.shipyard.machineOrders;
            for (int i = 0; i < orders.Count; i++)
                if (orders[i] != null && orders[i].machineId == machineId)
                    return orders[i];
            var created = new ShipyardCustomerOrderState
            {
                orderId = "Order_" + _data.shipyard.AllocateCustomerOrderId(),
                machineId = machineId,
                recipeId = recipe.RecipeId,
                requiredQuantity = 1,
                rewardCash = 250d + (Array.IndexOf(ShipyardProgression.MachineIds, machineId) * 50d)
            };
            created.Normalize();
            orders.Add(created);
            Commit();
            return created;
        }

        private ShipyardCustomerOrderState FindOrder(string machineId)
        {
            if (machineId == ShipyardProgression.CannonMachineId) return _data.shipyard.cannonOrder;
            List<ShipyardCustomerOrderState> orders = _data.shipyard.machineOrders;
            for (int i = 0; i < orders.Count; i++)
                if (orders[i] != null && orders[i].machineId == machineId) return orders[i];
            return null;
        }

        private ShipyardRecipeDefinition LatestDiscoveredRecipe(string machineId)
        {
            List<ShipyardRecipeDefinition> recipes;
            if (!_recipesByMachine.TryGetValue(machineId, out recipes)) return null;
            for (int i = recipes.Count - 1; i >= 0; i--)
                if (IsRecipeDiscovered(recipes[i].RecipeId)) return recipes[i];
            return null;
        }

        private void ResetOrder(ShipyardCustomerOrderState order, string machineId,
                                ShipyardRecipeDefinition recipe)
        {
            order.orderId = "Order_" + _data.shipyard.AllocateCustomerOrderId();
            order.machineId = machineId;
            order.recipeId = recipe.RecipeId;
            order.requiredQuantity = 1;
            order.fulfilledQuantity = 0;
            order.status = ShipyardCustomerOrderState.Active;
            order.rewardCash = 250d + (Array.IndexOf(ShipyardProgression.MachineIds, machineId) * 50d);
        }

        private static ShipyardFinishedItemState FindOutput(ShipyardMachineState machine, string itemId)
        {
            if (machine == null || machine.finishedOutputs == null || string.IsNullOrEmpty(itemId)) return null;
            for (int i = 0; i < machine.finishedOutputs.Count; i++)
                if (machine.finishedOutputs[i] != null && machine.finishedOutputs[i].itemId == itemId)
                    return machine.finishedOutputs[i];
            return null;
        }

        private bool CanDiscover(ShipyardRecipeDefinition recipe)
        {
            ShipyardMachineState machine = FindMachine(recipe.MachineFamilyId);
            if (machine == null || machine.constructionState != ShipyardMachineState.Built) return false;
            if (_data.shipyard.completedOrders < recipe.RequiredCompletedOrders
                || _data.shipyard.reputation < recipe.RequiredReputation) return false;
            ShipyardRecipeDefinition.MaterialUnlockCondition[] unlocks = recipe.MaterialUnlocks;
            for (int i = 0; unlocks != null && i < unlocks.Length; i++)
            {
                if (string.IsNullOrEmpty(unlocks[i].ResourceId)
                    || MaterialQuantity(unlocks[i].ResourceId) + 0.000001d < unlocks[i].MinimumQuantity)
                    return false;
            }
            return true;
        }

        private bool IsRecipeDiscovered(string recipeId)
            => !string.IsNullOrEmpty(recipeId) && _data.shipyard.discoveredRecipeIds.Contains(recipeId);

        private bool HasIngredients(ShipyardRecipeDefinition recipe)
        {
            ShipyardRecipeDefinition.Ingredient[] ingredients = recipe.Ingredients;
            if (ingredients == null || ingredients.Length == 0) return false;
            for (int i = 0; i < ingredients.Length; i++)
                if (string.IsNullOrEmpty(ingredients[i].ResourceId)
                    || MaterialQuantity(ingredients[i].ResourceId) + 0.000001d < ingredients[i].Quantity)
                    return false;
            return true;
        }

        private void AddMaterial(string id, double amount)
        {
            if (string.IsNullOrEmpty(id) || amount <= 0d) return;
            List<ShipyardMaterialState> materials = _data.shipyard.materialInventory;
            for (int i = 0; i < materials.Count; i++)
                if (materials[i] != null && materials[i].resourceId == id)
                {
                    materials[i].quantity += amount;
                    return;
                }
            materials.Add(new ShipyardMaterialState { resourceId = id, quantity = amount });
        }

        private void RemoveMaterial(string id, double amount)
        {
            for (int i = 0; i < _data.shipyard.materialInventory.Count; i++)
            {
                ShipyardMaterialState material = _data.shipyard.materialInventory[i];
                if (material == null || material.resourceId != id) continue;
                material.quantity = Math.Max(0d, material.quantity - amount);
                return;
            }
        }

        private bool RemoveOutput(string machineId, string itemId, out ShipyardFinishedItemState output)
        {
            output = null;
            if (string.IsNullOrEmpty(itemId)) return false;
            List<ShipyardFinishedItemState> outputs = FindMachine(machineId)?.finishedOutputs;
            if (outputs == null) return false;
            for (int i = 0; i < outputs.Count; i++)
                if (outputs[i] != null && outputs[i].itemId == itemId)
                {
                    output = outputs[i];
                    outputs.RemoveAt(i);
                    return true;
                }
            return false;
        }

        private void RestoreOutput(string machineId, ShipyardFinishedItemState output)
        {
            if (output == null) return;
            ShipyardMachineState machine = FindMachine(machineId);
            if (machine == null) return;
            if (machine.finishedOutputs == null) machine.finishedOutputs = new List<ShipyardFinishedItemState>();
            machine.finishedOutputs.Add(output);
        }

        private static SeaCombat.Item ToItem(ShipyardFinishedItemState output)
            => new SeaCombat.Item
            {
                Slot = output.equipmentSlot,
                Grade = output.rarity,
                Hull = output.hull,
                Shot = output.shot,
                Def = output.defence,
                Spd = output.speed,
                Sec = SeaCombat.SecNone,
                SecAmt = output.secondaryAmount
            };

        private void RegisterRecipe(ShipyardRecipeDefinition recipe)
        {
            if (recipe == null || string.IsNullOrEmpty(recipe.MachineFamilyId)
                || string.IsNullOrEmpty(recipe.RecipeId)) return;
            _recipesById[recipe.RecipeId] = recipe;
            List<ShipyardRecipeDefinition> recipes;
            if (!_recipesByMachine.TryGetValue(recipe.MachineFamilyId, out recipes))
            {
                recipes = new List<ShipyardRecipeDefinition>();
                _recipesByMachine.Add(recipe.MachineFamilyId, recipes);
            }
            for (int i = 0; i < recipes.Count; i++)
                if (recipes[i].RecipeId == recipe.RecipeId)
                {
                    recipes[i] = recipe;
                    return;
                }
            recipes.Add(recipe);
        }

        private ShipyardMachineState FindMachine(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            List<ShipyardMachineState> machines = _data.shipyard.machines;
            for (int i = 0; i < machines.Count; i++)
                if (machines[i] != null && machines[i].machineId == id) return machines[i];
            return null;
        }

        private long NowUnix() => _now != null ? _now() : (_time != null ? _time.NowUnix() : DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        private void Commit() => _save?.Save(_data);
    }
}
