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
        /// <summary>Items in the customer's bundle being served.</summary>
        public int ServiceUnits;
        public bool ServicePerfect;
        /// <summary>The perfect-sale generator's state; 0 until the first roll.</summary>
        public int PerfectSeed;
        public double ServiceRemaining;
        public double ServiceDuration;
        public double ServicePrice;
        /// <summary>The customer being served is the contract customer: the items are delivered, not sold for cash.</summary>
        public bool ServiceContract;
        public long ReceiptSequence;
        /// <summary>
        /// 0 while the lines still carry the old speed and value tracks; MiningShopLevelMigration.Schema once they
        /// have been turned into a single level. A new business starts at 0 too and is migrated from 1/1 on open.
        /// </summary>
        public int LevelSchema;
        /// <summary>The contract customer. A save from before contracts loads an empty one.</summary>
        public ShopContractState Contract = new ShopContractState();
        /// <summary>The tip generator's state; 0 until the first roll. Apart from PerfectSeed so tips shift no perfect sale.</summary>
        public int TipSeed;
        /// <summary>Cash sales since the last tip, counted only up to the gap a tip needs.</summary>
        public int SalesSinceTip;
        /// <summary>Tips left in this business, and what they came to before the wallet's multipliers.</summary>
        public long TipCount;
        public double TipsEarned;
    }
}
