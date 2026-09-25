using System;

namespace Game.Core
{
    /// <summary>One product table's saved work and inventory. The carrier and seller live on the business.</summary>
    [Serializable]
    public sealed class MiningShopProductLineState
    {
        public string ProductId;
        public bool TableBuilt;
        /// <summary>The bench's mastery level, 1-100 (see BenchMastery).</summary>
        public int Level = 1;
        /// <summary>Stars whose gems have been paid, one bit per BenchMastery star. Bits past the last star mean nothing.</summary>
        public int StarsPaid;
        /// <summary>The master (Foremen.Roster index) working this bench, or -1 for the apprentice.</summary>
        public int Worker = -1;
        // The two tracks Level replaced. Read once by the save migration that turns them into a level; nothing else.
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
