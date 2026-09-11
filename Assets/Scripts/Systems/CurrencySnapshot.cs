using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// One point-in-time, read-only currency value for UI. Cash uses <see cref="Current"/> so its
    /// idle-economy precision is retained; whole-number currencies expose their exact value through
    /// <see cref="WholeAmount"/>. Regenerating entries use 0 for both countdown fields at capacity.
    /// </summary>
    public readonly struct CurrencySnapshot
    {
        public CurrencyDefinition Definition { get; }
        public BigDouble Current { get; }
        public long WholeAmount { get; }
        public long Maximum { get; }
        public double SecondsToNextRegeneration { get; }
        public long NextRegenerationUnix { get; }

        public CurrencyId Id => Definition.Id;
        public bool HasMaximum => Definition.HasMaximum;
        public bool IsRegenerating => Definition.Regenerates;

        public CurrencySnapshot(CurrencyDefinition definition, BigDouble current, long wholeAmount,
                                long maximum, double secondsToNextRegeneration,
                                long nextRegenerationUnix)
        {
            Definition = definition;
            Current = current;
            WholeAmount = wholeAmount;
            Maximum = maximum;
            SecondsToNextRegeneration = secondsToNextRegeneration;
            NextRegenerationUnix = nextRegenerationUnix;
        }
    }
}
