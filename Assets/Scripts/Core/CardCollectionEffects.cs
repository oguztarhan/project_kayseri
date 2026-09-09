using System;

namespace Game.Core
{
    /// <summary>
    /// What the whole collection is currently worth, as one immutable snapshot.
    ///
    /// WHY A SNAPSHOT AND NOT A QUERY. Every consumer of this reads it on a hot path —
    /// <see cref="MarketFlow"/>'s till settles once a second, the workshop's point drop rolls on every
    /// won fight — and the honest answer to "what is the collection worth" involves walking
    /// twenty-four cards and three sets. So the service walks them once, when something actually
    /// changes, and hands out the answer. Nothing here allocates and nothing here is computed twice.
    ///
    /// EVERY VALUE IS ALREADY CAPPED. The service applies <see cref="CardCollection.Capped"/> before
    /// building this, so a consumer can never accidentally read past a cap by forgetting it exists.
    /// That is the only way a cap survives the next person to add a content set.
    ///
    /// FOUR OF THE FIVE ARE FRACTIONS ON TOP OF 1.0 and are meant to be read through
    /// <see cref="Multiplier"/>. The fifth, <see cref="CraftPointDrop"/>, is absolute probability and
    /// must go through <see cref="CardCollection.CraftPointChance"/> instead — see
    /// <see cref="CardCollection.IsMultiplier"/>, which is what tells the two apart.
    /// </summary>
    public readonly struct CardCollectionEffects
    {
        public readonly double Income;
        public readonly double CraftXp;
        public readonly double CraftPointDrop;
        public readonly double SeaSalvage;
        public readonly double SeaChart;

        public CardCollectionEffects(double income, double craftXp, double craftPointDrop,
                                     double seaSalvage, double seaChart)
        {
            Income = Clean(income);
            CraftXp = Clean(craftXp);
            CraftPointDrop = Clean(craftPointDrop);
            SeaSalvage = Clean(seaSalvage);
            SeaChart = Clean(seaChart);
        }

        /// <summary>An empty collection: every effect zero, every multiplier 1.0. What every consumer
        /// sees when the service is absent, which is what lets the whole feature be switched off by
        /// simply not constructing it.</summary>
        public static CardCollectionEffects None => default;

        /// <summary>The raw contribution of one effect kind. A fraction for the four multipliers,
        /// absolute probability for the drop chance.</summary>
        public double Of(CardCollection.EffectKind kind)
        {
            switch (kind)
            {
                case CardCollection.EffectKind.IncomeMultiplier:     return Income;
                case CardCollection.EffectKind.CraftXpMultiplier:    return CraftXp;
                case CardCollection.EffectKind.CraftPointDropChance: return CraftPointDrop;
                case CardCollection.EffectKind.SeaSalvageMultiplier: return SeaSalvage;
                case CardCollection.EffectKind.SeaChartMultiplier:   return SeaChart;
                default:                                             return 0d;
            }
        }

        /// <summary>
        /// One effect as the number its consumer multiplies by — 1.0 for an empty collection, so a
        /// system that reads this before any card is owned behaves exactly as it did before the
        /// feature existed.
        ///
        /// Refuses <see cref="CardCollection.EffectKind.CraftPointDropChance"/> by returning 1.0,
        /// because multiplying a probability by "1 plus a probability" is a bug that would look
        /// plausible for a long time. That one goes through
        /// <see cref="CardCollection.CraftPointChance"/>.
        /// </summary>
        public double Multiplier(CardCollection.EffectKind kind)
            => CardCollection.IsMultiplier(kind) ? CardCollection.AsMultiplier(Of(kind)) : 1d;

        public double IncomeMultiplier => CardCollection.AsMultiplier(Income);
        public double CraftXpMultiplier => CardCollection.AsMultiplier(CraftXp);
        public double SeaSalvageMultiplier => CardCollection.AsMultiplier(SeaSalvage);
        public double SeaChartMultiplier => CardCollection.AsMultiplier(SeaChart);

        /// <summary>True when the collection is currently worth nothing at all — no card owned, or no
        /// service running.</summary>
        public bool IsEmpty => Income <= 0d && CraftXp <= 0d && CraftPointDrop <= 0d
                            && SeaSalvage <= 0d && SeaChart <= 0d;

        /// <summary>A NaN reaching a multiplier would poison every number downstream of it silently.
        /// Catch it here, where there is still one place to look.</summary>
        private static double Clean(double value)
            => double.IsNaN(value) || value <= 0d ? 0d : value;
    }
}
