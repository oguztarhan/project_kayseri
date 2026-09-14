using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>Shared logistics and seller state for one four-product mining-shop business.</summary>
    [Serializable]
    public sealed class MiningShopBusinessState
    {
        public int AvailableProductCount = 1;
        public List<MiningShopProductLineState> Lines = new List<MiningShopProductLineState>();
        public double ElapsedSeconds;
        public double PendingSeconds;
        public MiningShopSimulation.CarrierPhase Carrier;
        public int CarrierProductIndex = -1;
        public int CarrierCount;
        public double CarrierRemaining;
        public double CarrierDuration;
        public int CarrierCursor;
        public int DemandCursor;
        public int SellerCursor;
        public double ArrivalRemaining;
        public bool Serving;
        public int ServiceProductIndex = -1;
        public double ServiceRemaining;
        public double ServiceDuration;
        public double ServicePrice;
        public long ReceiptSequence;
    }
}
