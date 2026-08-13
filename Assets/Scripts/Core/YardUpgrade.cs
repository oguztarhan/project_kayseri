namespace Game.Core
{
    /// <summary>
    /// Everything a market yard sells the player, one entry per floor pad.
    ///
    /// The order is load-bearing: saved yards address hires by the numbers in
    /// <see cref="MarketFlow.Carry"/> and friends, and the three hire entries here are deliberately
    /// laid out in that same order so one can be turned into the other by arithmetic rather than by a
    /// switch that could be got wrong.
    /// </summary>
    public enum YardUpgrade
    {
        /// <summary>Another pad on the floor: the yard holds more before deliveries start spilling.</summary>
        DepositSlot = 0,

        /// <summary>Another place in the line: the counter can move more, and customers arrive faster.</summary>
        QueueSlot = 1,

        /// <summary>Somebody to run bars from the pads to the counter.</summary>
        HireCarry = 2,

        /// <summary>Somebody to work the counter.</summary>
        HireServe = 3,

        /// <summary>Somebody to sweep the cash off the floor.</summary>
        HireCollect = 4,

        /// <summary>A taller stack on the player's own back. One body, so this is not per yard.</summary>
        CarryCapacity = 5,
    }
}
