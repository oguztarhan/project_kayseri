using System;

namespace Game.Core
{
    /// <summary>
    /// Pure rules for the tips a shop customer may leave after a successful sale. A tip is a bonus on top of the sale:
    /// it never changes the price, the items counted as sold or what the bench has earned.
    ///
    /// A TIP IS A SHARE OF THE NORMAL PRICE. The amount is a fixed share of what the bundle sold for before any perfect
    /// multiplier, so it grows with the bench and the island without a scale of its own, and a perfect sale raises only
    /// the chance — never the amount. That, the chance cap and the gap between tips are what keep tips a small extra
    /// (about +3% at no stars, +4.4% at five) rather than a second income.
    /// </summary>
    public static class ShopTips
    {
        /// <summary>Tip, big tip, huge tip.</summary>
        public const int TierCount = 3;
        /// <summary>The tier of a sale that left no tip.</summary>
        public const int None = -1;

        public struct Tuning
        {
            /// <summary>Chance a sale tips with no stars on its bench.</summary>
            public double BaseChance;
            /// <summary>Chance gained per star on the bench.</summary>
            public double ChancePerStar;
            /// <summary>Chance a perfect sale adds.</summary>
            public double PerfectBonus;
            /// <summary>No sale tips more often than this.</summary>
            public double MaxChance;
            /// <summary>Cash sales that must pass after a tip before the next one can tip.</summary>
            public int MinSalesBetween;
            /// <summary>A business's first receipts never tip, so the first-time tutorial sees plain sales.</summary>
            public int UnlockReceipts;
            /// <summary>What each tier pays, as a share of the sale's normal price.</summary>
            public double[] Shares;
            /// <summary>How often each tier comes up, relative to the others.</summary>
            public int[] Weights;

            public static Tuning Default => new Tuning
            {
                BaseChance = 0.10d,
                ChancePerStar = 0.01d,
                PerfectBonus = 0.10d,
                MaxChance = 0.25d,
                MinSalesBetween = 2,
                UnlockReceipts = 10,
                Shares = new[] { 0.25d, 0.6d, 1.5d },
                Weights = new[] { 75, 22, 3 }
            };

            public void Validate()
            {
                if (!Finite(BaseChance) || BaseChance < 0d || !Finite(ChancePerStar) || ChancePerStar < 0d ||
                    !Finite(PerfectBonus) || PerfectBonus < 0d || !Finite(MaxChance) || MaxChance < 0d || MaxChance > 1d ||
                    MinSalesBetween < 0 || UnlockReceipts < 0 ||
                    Shares == null || Shares.Length != TierCount || Weights == null || Weights.Length != TierCount)
                    throw new ArgumentException("Shop tip tuning requires finite odds, a cap that is a probability and " +
                                                "one share and weight per tier.");
                int total = 0;
                for (int i = 0; i < TierCount; i++)
                {
                    if (!Finite(Shares[i]) || Shares[i] <= 0d || Weights[i] < 0)
                        throw new ArgumentException("A tip tier needs a positive share and a weight of at least zero.");
                    total += Weights[i];
                }
                if (total <= 0) throw new ArgumentException("At least one tip tier must be able to come up.");
            }
        }

        /// <summary>Chance a cash sale off a bench with this many stars tips, before the gap between tips.</summary>
        public static double Chance(int stars, bool perfect, in Tuning t)
        {
            double chance = t.BaseChance + t.ChancePerStar * stars + (perfect ? t.PerfectBonus : 0d);
            return chance < t.MaxChance ? chance : t.MaxChance;
        }

        /// <summary>The tier a roll in [0,1) lands on, by the tiers' weights.</summary>
        public static int Tier(double roll, in Tuning t)
        {
            int total = 0;
            for (int i = 0; i < TierCount; i++) total += t.Weights[i];
            double at = roll * total;
            int upTo = 0;
            for (int i = 0; i < TierCount; i++)
            {
                upTo += t.Weights[i];
                if (at < upTo) return i;
            }
            return TierCount - 1;
        }

        /// <summary>What a tier pays on a sale whose normal price, before any perfect multiplier, was <paramref name="normalPrice"/>.</summary>
        public static double Amount(double normalPrice, int tier, in Tuning t) => normalPrice * t.Shares[tier];

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
