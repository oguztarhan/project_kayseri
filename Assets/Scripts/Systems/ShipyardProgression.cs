using System;
using System.Collections.Generic;

namespace Game.Systems
{
    /// <summary>
    /// Additive save payload for ship-item machine families; never remaps legacy equipment slots
    /// or island progress. The serialized field name <c>unlockedStations</c> is retained so old
    /// prototype saves continue to load, but its values are machine-family IDs, not global hubs.
    /// </summary>
    [Serializable]
    public sealed class ShipyardProgression
    {
        public const string CannonMachineId = "Station_Cannon";
        public const string HullMachineId = "Station_Hull";
        public const string RiggingMachineId = "Station_Rigging";
        public const string NavigationMachineId = "Station_Navigation";
        public const string FigureheadMachineId = "Station_Figurehead";
        public const string CannonStarterRecipeId = "Recipe_Cannon_01";
        public const string HullStarterRecipeId = "Recipe_Hull_01";
        public const string RiggingStarterRecipeId = "Recipe_Rigging_01";
        public const string NavigationStarterRecipeId = "Recipe_Navigation_01";
        public const string FigureheadStarterRecipeId = "Recipe_Figurehead_01";

        public List<string> unlockedStations = new List<string>();
        public int completedOrders;
        public int reputation;
        public long nextFinishedItemId;
        public long nextCustomerOrderId;
        public List<ShipyardMaterialState> materialInventory = new List<ShipyardMaterialState>();
        public List<ShipyardMachineState> machines = new List<ShipyardMachineState>();
        public List<string> discoveredRecipeIds = new List<string>();
        public ShipyardCustomerOrderState cannonOrder = new ShipyardCustomerOrderState();
        public List<ShipyardCustomerOrderState> machineOrders = new List<ShipyardCustomerOrderState>();

        public static readonly string[] MachineIds =
        {
            CannonMachineId, HullMachineId, RiggingMachineId,
            NavigationMachineId, FigureheadMachineId
        };

        public static readonly string[] StarterRecipeIds =
        {
            CannonStarterRecipeId, HullStarterRecipeId, RiggingStarterRecipeId,
            NavigationStarterRecipeId, FigureheadStarterRecipeId
        };

        public void Normalize()
        {
            if (unlockedStations == null) unlockedStations = new List<string>();
            if (!unlockedStations.Contains(CannonMachineId)) unlockedStations.Add(CannonMachineId);
            completedOrders = Math.Max(0, completedOrders);
            reputation = Math.Max(0, reputation);
            if (nextFinishedItemId < 0L) nextFinishedItemId = 0L;
            if (nextCustomerOrderId < 0L) nextCustomerOrderId = 0L;
            if (materialInventory == null) materialInventory = new List<ShipyardMaterialState>();
            NormalizeMaterials();
            if (machines == null) machines = new List<ShipyardMachineState>();
            NormalizeMachines();
            if (discoveredRecipeIds == null) discoveredRecipeIds = new List<string>();
            if (!discoveredRecipeIds.Contains(CannonStarterRecipeId)) discoveredRecipeIds.Add(CannonStarterRecipeId);
            if (cannonOrder == null) cannonOrder = new ShipyardCustomerOrderState();
            cannonOrder.machineId = CannonMachineId;
            cannonOrder.Normalize();
            if (machineOrders == null) machineOrders = new List<ShipyardCustomerOrderState>();
            NormalizeOrders();
            for (int i = 0; i < unlockedStations.Count; i++)
            {
                EnsureMachineState(unlockedStations[i], ShipyardMachineState.Built);
                DiscoverStarterRecipe(unlockedStations[i]);
            }
        }

        public bool IsUnlocked(string id)
        {
            return id == CannonMachineId || (unlockedStations != null && unlockedStations.Contains(id));
        }

        public bool IsBuilt(string id)
        {
            if (id == CannonMachineId) return true;
            if (machines == null) return false;
            for (int i = 0; i < machines.Count; i++)
                if (machines[i] != null && machines[i].machineId == id)
                    return machines[i].constructionState == ShipyardMachineState.Built;
            return false;
        }

        public string NextMachine
        {
            get
            {
                foreach (var id in MachineIds) if (!IsUnlocked(id)) return id;
                return null;
            }
        }

        // This is the commit boundary, not a shop purchase. The future order/economy service
        // must validate its costs/milestone before calling it. Missing art can never be bought.
        public bool TryUnlockNext(string id, bool milestoneSatisfied, bool artReady)
        {
            Normalize();
            if (!milestoneSatisfied || !artReady || id == null || id != NextMachine) return false;
            unlockedStations.Add(id);
            EnsureMachineState(id, ShipyardMachineState.Built);
            DiscoverStarterRecipe(id);
            return true;
        }

        public long AllocateFinishedItemId()
        {
            Normalize();
            nextFinishedItemId = nextFinishedItemId < 1L ? 1L : nextFinishedItemId + 1L;
            return nextFinishedItemId;
        }

        public long AllocateCustomerOrderId()
        {
            Normalize();
            nextCustomerOrderId = nextCustomerOrderId < 1L ? 1L : nextCustomerOrderId + 1L;
            return nextCustomerOrderId;
        }

        private void NormalizeMaterials()
        {
            for (int i = materialInventory.Count - 1; i >= 0; i--)
            {
                ShipyardMaterialState material = materialInventory[i];
                if (material == null)
                {
                    materialInventory.RemoveAt(i);
                    continue;
                }
                if (material.quantity < 0d || double.IsNaN(material.quantity) || double.IsInfinity(material.quantity))
                    material.quantity = 0d;
            }
        }

        private void NormalizeMachines()
        {
            for (int i = machines.Count - 1; i >= 0; i--)
            {
                ShipyardMachineState machine = machines[i];
                if (machine == null || string.IsNullOrEmpty(machine.machineId))
                {
                    machines.RemoveAt(i);
                    continue;
                }
                if (machine.constructionState < ShipyardMachineState.Locked)
                    machine.constructionState = ShipyardMachineState.Locked;
                if (machine.constructionState > ShipyardMachineState.Built)
                    machine.constructionState = ShipyardMachineState.Built;
                if (machine.workerCapacity < 1) machine.workerCapacity = 1;
                if (machine.queueCapacity < 1) machine.queueCapacity = 1;
                if (machine.constructionState == ShipyardMachineState.Constructing
                    && (machine.constructionStartedUnix < 0L
                        || machine.constructionFinishUnix < machine.constructionStartedUnix))
                {
                    machine.constructionState = ShipyardMachineState.Pad;
                    machine.constructionStartedUnix = 0L;
                    machine.constructionFinishUnix = 0L;
                }
                else if (machine.constructionState != ShipyardMachineState.Constructing)
                {
                    machine.constructionStartedUnix = 0L;
                    machine.constructionFinishUnix = 0L;
                }
                if (string.IsNullOrEmpty(machine.activeRecipeId)
                    || machine.queueStartedUnix < 0L
                    || machine.queueFinishUnix < machine.queueStartedUnix)
                {
                    machine.activeRecipeId = "";
                    machine.queueStartedUnix = 0L;
                    machine.queueFinishUnix = 0L;
                }
                if (machine.finishedOutputs == null)
                    machine.finishedOutputs = new List<ShipyardFinishedItemState>();
                for (int o = machine.finishedOutputs.Count - 1; o >= 0; o--)
                {
                    ShipyardFinishedItemState output = machine.finishedOutputs[o];
                    if (output == null || string.IsNullOrEmpty(output.itemId))
                        machine.finishedOutputs.RemoveAt(o);
                }
            }
        }

        private void NormalizeOrders()
        {
            for (int i = machineOrders.Count - 1; i >= 0; i--)
            {
                ShipyardCustomerOrderState order = machineOrders[i];
                if (order == null || string.IsNullOrEmpty(order.machineId)
                    || order.machineId == CannonMachineId || !IsMachineId(order.machineId))
                {
                    machineOrders.RemoveAt(i);
                    continue;
                }
                order.Normalize();
            }
        }

        private static bool IsMachineId(string id)
        {
            for (int i = 0; i < MachineIds.Length; i++)
                if (MachineIds[i] == id) return true;
            return false;
        }

        private void DiscoverStarterRecipe(string machineId)
        {
            string recipeId = null;
            switch (machineId)
            {
                case HullMachineId: recipeId = HullStarterRecipeId; break;
                case RiggingMachineId: recipeId = RiggingStarterRecipeId; break;
                case NavigationMachineId: recipeId = NavigationStarterRecipeId; break;
                case FigureheadMachineId: recipeId = FigureheadStarterRecipeId; break;
                default: recipeId = CannonStarterRecipeId; break;
            }
            if (!discoveredRecipeIds.Contains(recipeId)) discoveredRecipeIds.Add(recipeId);
        }

        private void EnsureMachineState(string id, int defaultState)
        {
            if (string.IsNullOrEmpty(id)) return;
            for (int i = 0; i < machines.Count; i++)
                if (machines[i] != null && machines[i].machineId == id) return;
            machines.Add(new ShipyardMachineState
            {
                machineId = id,
                constructionState = defaultState,
                activeRecipeId = "",
                workerCapacity = 1,
                queueCapacity = 1,
                finishedOutputs = new List<ShipyardFinishedItemState>()
            });
        }
    }

    [Serializable]
    public sealed class ShipyardMaterialState
    {
        public string resourceId;
        public double quantity;
    }

    [Serializable]
    public sealed class ShipyardMachineState
    {
        public const int Locked = 0;
        public const int Pad = 1;
        public const int Constructing = 2;
        public const int Built = 3;

        public string machineId;
        public int constructionState = Locked;
        public string activeRecipeId = "";
        public long queueStartedUnix;
        public long queueFinishUnix;
        public int workerCapacity = 1;
        public int queueCapacity = 1;
        public long constructionStartedUnix;
        public long constructionFinishUnix;
        public List<ShipyardFinishedItemState> finishedOutputs = new List<ShipyardFinishedItemState>();
    }

    [Serializable]
    public sealed class ShipyardFinishedItemState
    {
        public string itemId;
        public string recipeId;
        public string machineId;
        public int equipmentSlot;
        public int rarity;
        public double hull;
        public double shot;
        public double defence;
        public double speed;
        public double secondaryAmount;
        public double value;
        public long completedUnix;
    }

    [Serializable]
    public sealed class ShipyardCustomerOrderState
    {
        public const int Active = 0;
        public const int Fulfilled = 1;

        public string orderId = "Order_Cannon_01";
        public string machineId = ShipyardProgression.CannonMachineId;
        public string recipeId = ShipyardProgression.CannonStarterRecipeId;
        public int requiredQuantity = 1;
        public int fulfilledQuantity;
        public int status = Active;
        public double rewardCash = 250d;

        public void Normalize()
        {
            if (string.IsNullOrEmpty(orderId)) orderId = "Order_Cannon_01";
            if (string.IsNullOrEmpty(machineId)) machineId = ShipyardProgression.CannonMachineId;
            if (string.IsNullOrEmpty(recipeId)) recipeId = ShipyardProgression.CannonStarterRecipeId;
            if (requiredQuantity < 1) requiredQuantity = 1;
            if (fulfilledQuantity < 0) fulfilledQuantity = 0;
            if (fulfilledQuantity > requiredQuantity) fulfilledQuantity = requiredQuantity;
            if (status != Active && status != Fulfilled) status = Active;
            if (fulfilledQuantity >= requiredQuantity) status = Fulfilled;
            if (double.IsNaN(rewardCash) || double.IsInfinity(rewardCash) || rewardCash < 0d)
                rewardCash = 0d;
        }
    }
}
