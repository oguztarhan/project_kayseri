using System;

namespace Game.Core
{
    /// <summary>
    /// The contract customer of one shop business: what they ordered, what they have been handed, and what they pay
    /// when the last item arrives. Saved with the business, so leaving the island leaves the contract behind with it.
    ///
    /// Terms are copied in at accept and never re-read from the bench: a level bought mid-contract changes how fast
    /// the order fills, not what it pays.
    /// </summary>
    [Serializable]
    public sealed class ShopContractState
    {
        /// <summary>True from accept until the last item is delivered or the contract is cancelled.</summary>
        public bool Active;
        /// <summary>The 30-minute slot whose offer was accepted. Kept after the contract ends, as the last slot taken.</summary>
        public long Slot;
        public int ProductIndex = -1;
        public int SizeIndex;
        public int Quantity;
        /// <summary>Items already handed over. Equal to Quantity on a completed contract whose terms are still held.</summary>
        public int Delivered;
        public double Cash;
        public long Gems;
        public int ForemanCards;
        /// <summary>Whose turn it is on the contract's bench when both the contract and a normal customer want goods.</summary>
        public bool ContractTurn;

        /// <summary>
        /// Forgets the contract and everything it owed; <see cref="Slot"/> stays as the last slot taken. Safe on a
        /// business that is not open: a bundle already being handed over still finishes and simply counts for nothing.
        /// </summary>
        public void Clear()
        {
            Active = false;
            ProductIndex = -1;
            SizeIndex = 0;
            Quantity = 0;
            Delivered = 0;
            Cash = 0d;
            Gems = 0L;
            ForemanCards = 0;
            ContractTurn = false;
        }
    }
}
