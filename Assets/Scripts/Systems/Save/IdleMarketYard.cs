using System;
using System.Collections.Generic;

namespace Game.Systems
{
    /// <summary>
    /// Package-B save contract. Not wired into SaveData until all scalar-stock consumers switch.
    /// One row per island; stages are content phases of that same business.
    /// </summary>
    [Serializable]
    public sealed class IdleMarketYard
    {
        public int schemaVersion;
        public string id;
        public int depositSlots = 1;
        public int queueSlots = 1;
        public int hireCarry;
        public int hireServe;
        public int dispatchLevel; // Former hireCollect: third throughput bottleneck, paid immediately.
        public List<MarketProductStock> products = new List<MarketProductStock>();
    }
}
