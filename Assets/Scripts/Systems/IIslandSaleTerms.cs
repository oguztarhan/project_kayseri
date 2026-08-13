namespace Game.Systems
{
    /// <summary>
    /// What an island charges for a bar, and the ceiling it may earn against — the two numbers
    /// <see cref="MarketService"/> needs to turn bars into cash without knowing anything about mines,
    /// trains or trucks.
    ///
    /// It exists to keep the dependency pointing one way. The yard sells; the island only produces.
    /// <c>Game.Gameplay</c> already references <c>Game.Systems</c>, so the island implements this and
    /// hands itself over, and the service never has to reach back into the simulation.
    ///
    /// Both values are RAW — before investors. The prestige multiplier moves the price and the ceiling
    /// together, and it is refreshed once a second in one place, so applying it here as well would
    /// either double it or let a copy of it go stale on an island nobody is standing on.
    /// </summary>
    public interface IIslandSaleTerms
    {
        /// <summary>What one bar sells for at this island's current upgrade levels, before investors.</summary>
        double BarPriceRaw { get; }

        /// <summary>This island's income ceiling per minute, before investors.</summary>
        double IncomeCapPerMinuteRaw { get; }
    }
}
