using System;

namespace Game.Core
{
    /// <summary>One business's disjoint inventories and jobs. Save fields are not animation state.</summary>
    [Serializable]
    public sealed class MiningShopState
    {
        public string BusinessId;
        public int SpeedLevel = 1;
        public int ValueLevel = 1;
        public double ElapsedSeconds;
        public double PendingSeconds;
        public bool Crafting;
        public double CraftRemaining;
        public double CraftDuration;
        public int OutputStock;
        public int PickupReserved;
        public int Cargo;
        public int DestinationReserved;
        public MiningShopSimulation.CarrierPhase Carrier;
        public double CarrierRemaining;
        public double CarrierDuration;
        public int ShelfStock;
        public int WaitingCustomers;
        public double ArrivalRemaining;
        public bool Serving;
        public double ServiceRemaining;
        public double ServiceDuration;
        public double ServicePrice;
        public long Produced;
        public long Sold;
        public double Earned;
    }
}
