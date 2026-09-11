namespace Game.Systems
{
    /// <summary>
    /// Immutable display metadata for one currency. IconKey is an asset-independent reference which
    /// Phase 2's UI can map to an existing sprite without putting a presentation reference in saves.
    /// </summary>
    public sealed class CurrencyDefinition
    {
        public CurrencyId Id { get; }
        public string LocalizedNameKey { get; }
        /// <summary>One-line "where it comes from, what it buys" text for the wallet row.</summary>
        public string LocalizedDescriptionKey => LocalizedNameKey + ".desc";
        public string IconKey { get; }
        public CurrencyCategory Category { get; }
        public CurrencyNumberFormat NumberFormat { get; }
        public bool HasMaximum { get; }
        public bool Regenerates { get; }

        public CurrencyDefinition(CurrencyId id, string localizedNameKey, string iconKey,
                                  CurrencyCategory category, CurrencyNumberFormat numberFormat,
                                  bool hasMaximum = false, bool regenerates = false)
        {
            Id = id;
            LocalizedNameKey = localizedNameKey;
            IconKey = iconKey;
            Category = category;
            NumberFormat = numberFormat;
            HasMaximum = hasMaximum;
            Regenerates = regenerates;
        }
    }
}
