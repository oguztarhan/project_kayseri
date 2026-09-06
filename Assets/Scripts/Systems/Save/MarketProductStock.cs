using System;

namespace Game.Systems
{
    /// <summary>Disjoint quantities: reserving moves stock into voyageReserved; never count both.</summary>
    [Serializable]
    public sealed class MarketProductStock
    {
        public string productId;
        public double stock; // Available for automatic customer sales.
        public double voyageReserved; // Escrow while loading, excluded from saleable stock.
        public double deliveredPerMin; // Unboosted measured product units/minute, not coins.
    }
}
