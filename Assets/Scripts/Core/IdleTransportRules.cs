using System;

namespace Game.Core
{
    /// <summary>
    /// Maps existing purchased transport effects to worker budgets without changing saved indices.
    /// These are economic crews; a view may render fewer bodies. Routing/timers belong to production.
    /// </summary>
    public static class IdleTransportRules
    {
        public struct CrewBudget
        {
            public int Teams;
            public double Speed;
            public double LoadPerTeam;
        }

        public static CrewBudget MineToDepot(IslandEconomy economy, int globalCarryLevel)
        {
            if (economy == null) throw new ArgumentNullException(nameof(economy));
            return new CrewBudget
            {
                Teams = 1, Speed = economy.TrainSpeed,
                LoadPerTeam = economy.TrainOre * IdleCrewRules.PorterLoadMultiplier(globalCarryLevel)
            };
        }

        public static CrewBudget DepotToRefinery(IslandEconomy economy, int globalCarryLevel)
        {
            if (economy == null) throw new ArgumentNullException(nameof(economy));
            return new CrewBudget
            {
                Teams = economy.OreTruckCount, Speed = economy.OreTruckSpeed,
                LoadPerTeam = economy.OreTruckLoad * IdleCrewRules.PorterLoadMultiplier(globalCarryLevel)
            };
        }

        public static CrewBudget RefineryToCounter(IslandEconomy economy, int globalCarryLevel)
        {
            if (economy == null) throw new ArgumentNullException(nameof(economy));
            return new CrewBudget
            {
                Teams = economy.CargoTruckCount, Speed = economy.CargoTruckSpeed,
                LoadPerTeam = economy.CargoTruckLoad * IdleCrewRules.PorterLoadMultiplier(globalCarryLevel)
            };
        }
    }
}
