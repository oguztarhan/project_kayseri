using System;

namespace Game.Core
{
    /// <summary>One product table's saved work and inventory. The carrier and seller live on the business.</summary>
    [Serializable]
    public sealed class MiningShopProductLineState
    {
        public string ProductId;
        public bool TableBuilt;
        public int SpeedLevel = 1;
        public int ValueLevel = 1;
        public bool Crafting;
        public double CraftRemaining;
        public double CraftDuration;
        public int OutputStock;
        public int PickupReserved;
        public int DestinationReserved;
        public int ShelfStock;
        public int WaitingCustomers;
        public long Produced;
        public long Sold;
        public double Earned;
    }
}
