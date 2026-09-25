using System;

namespace Game.Core
{
    /// <summary>
    /// How one shop contract is picked, sized and priced: a customer who orders a batch of one product, is served in
    /// turn with the normal customers, and pays everything — the price, its premium, gems and foreman cards — only
    /// when the last item is delivered.
    ///
    /// THE OFFER BELONGS TO A SLOT OF WALL-CLOCK TIME. Time is cut into fixed <see cref="Tuning.IntervalSeconds"/>
    /// slots and each slot's pick comes from a seed of the business and the slot number, so the same slot always
    /// offers the same job on any launch and nothing has to be saved until the player accepts. Moving the clock
    /// forward only trades one fair offer for another.
    ///
    /// QUANTITY IS SIZED IN TIME, NOT ITEMS. A size is "about this many minutes of the contract's share of the bench",
    /// so a small contract is ten minutes whether the bench is level 1 or level 100; only the item count grows.
    ///
    /// THE PRICE BEATS SELLING NORMALLY. The normal price per item includes what perfect sales add on average: a
    /// starred bench already sells above its list price, and a premium on the list price alone could pay less than
    /// the counter does.
    ///
    /// Nothing here reads a clock, a wallet or a save; callers pass the bench numbers in.
    /// </summary>
    public static class ShopContract
    {
        public const int SmallSize = 0, MediumSize = 1, LargeSize = 2;
        public const int SizeCount = 3;

        /// <summary>One contract size: how long it runs, how often it is rolled, and what it pays on completion.</summary>
        public struct Size
        {
            /// <summary>Expected minutes with the contract customer served in turn with normal customers.</summary>
            public double Minutes;
            /// <summary>Relative chance this size is rolled.</summary>
            public double Weight;
            /// <summary>What the contract pays over selling the same items normally; 0.1 = +10%.</summary>
            public double Premium;
            public long Gems;
            public int ForemanCards;
        }

        public struct Tuning
        {
            public double IntervalSeconds;
            public Size[] Sizes;
            /// <summary>Share of the bench's output the contract customer receives. 0.5 = served 1:1 with normal customers.</summary>
            public double ContractShare;
            public int MinQuantity;
            public int MaxQuantity;

            public static Tuning Default => new Tuning
            {
                IntervalSeconds = 1800d,
                Sizes = new[]
                {
                    new Size { Minutes = 10d, Weight = 40d, Premium = 0.10d, Gems = 1L, ForemanCards = 1 },
                    new Size { Minutes = 20d, Weight = 40d, Premium = 0.15d, Gems = 2L, ForemanCards = 1 },
                    new Size { Minutes = 40d, Weight = 20d, Premium = 0.20d, Gems = 3L, ForemanCards = 2 }
                },
                ContractShare = 0.5d,
                MinQuantity = 5,
                MaxQuantity = 1000000
            };

            public void Validate()
            {
                if (!Positive(IntervalSeconds) || Sizes == null || Sizes.Length != SizeCount ||
                    !Positive(ContractShare) || ContractShare > 1d || MinQuantity < 1 || MaxQuantity < MinQuantity)
                    throw new ArgumentException("Shop contract tuning requires a positive interval, three sizes, a share in (0,1] and a quantity range.");
                for (int i = 0; i < Sizes.Length; i++)
                {
                    Size s = Sizes[i];
                    if (!Positive(s.Minutes) || !Positive(s.Weight) || !Finite(s.Premium) || s.Premium < 0d ||
                        s.Gems < 0L || s.ForemanCards < 0)
                        throw new ArgumentException("Shop contract sizes require positive minutes and weights and non-negative rewards.");
                }
            }
        }

        /// <summary>Which bench a slot's contract orders from and how big it is. The numbers come from <see cref="TermsFor"/>.</summary>
        public struct Pick
        {
            public long Slot;
            public int ProductIndex;
            public int SizeIndex;
        }

        /// <summary>Everything the contract promises, fixed when the player accepts.</summary>
        public struct Terms
        {
            public int Quantity;
            /// <summary>Paid on completion: the items at the normal price, plus the premium.</summary>
            public double Cash;
            /// <summary>What the same items would have sold for normally, for the "+15% vs selling normally" line.</summary>
            public double NormalCash;
            public long Gems;
            public int ForemanCards;
            /// <summary>Expected seconds to fill it at the current bench speed and the tuning's share.</summary>
            public double EstimatedSeconds;
        }

        /// <summary>The slot a moment of wall-clock time falls in. Floors toward negative infinity, so it never repeats a slot.</summary>
        public static long SlotAt(long unixSeconds, in Tuning t)
            => (long)Math.Floor(unixSeconds / t.IntervalSeconds);

        /// <summary>Seconds until the next slot's offer replaces this one.</summary>
        public static double SecondsToNextSlot(long unixSeconds, in Tuning t)
            => (SlotAt(unixSeconds, t) + 1L) * t.IntervalSeconds - unixSeconds;

        /// <summary>
        /// The job a slot offers: a built bench and a size, both drawn from the business and slot. False when no
        /// bench is built. <paramref name="built"/> is indexed by product, in MiningShopCampaign order.
        /// </summary>
        public static bool TryPick(string businessId, long slot, bool[] built, in Tuning t, out Pick pick)
        {
            pick = default;
            if (built == null) return false;
            int builtCount = 0;
            for (int i = 0; i < built.Length; i++) if (built[i]) builtCount++;
            if (builtCount == 0) return false;

            uint x = Seed(businessId, slot);
            x = Next(x);
            int nth = (int)(Unit(x) * builtCount);
            if (nth >= builtCount) nth = builtCount - 1;
            int product = -1;
            for (int i = 0; i < built.Length; i++)
            {
                if (!built[i]) continue;
                if (nth-- == 0) { product = i; break; }
            }

            x = Next(x);
            pick = new Pick { Slot = slot, ProductIndex = product, SizeIndex = SizeFor(Unit(x), t) };
            return true;
        }

        /// <summary>
        /// Sizes and prices a picked contract against the bench as it is now. <paramref name="craftSeconds"/> is one
        /// item's cycle at the bench's level; <paramref name="normalUnitPrice"/> is <see cref="NormalUnitPrice"/>.
        /// </summary>
        public static Terms TermsFor(int sizeIndex, double craftSeconds, double normalUnitPrice, in Tuning t)
        {
            if (sizeIndex < 0 || sizeIndex >= SizeCount) throw new ArgumentOutOfRangeException(nameof(sizeIndex));
            if (!Positive(craftSeconds)) throw new ArgumentOutOfRangeException(nameof(craftSeconds));
            if (!Finite(normalUnitPrice) || normalUnitPrice < 0d) throw new ArgumentOutOfRangeException(nameof(normalUnitPrice));
            Size size = t.Sizes[sizeIndex];
            int quantity = Quantity(size.Minutes, craftSeconds, t);
            double normal = quantity * normalUnitPrice;
            return new Terms
            {
                Quantity = quantity,
                Cash = Cash(quantity, normalUnitPrice, size.Premium),
                NormalCash = normal,
                Gems = size.Gems,
                ForemanCards = size.ForemanCards,
                EstimatedSeconds = quantity * craftSeconds / t.ContractShare
            };
        }

        /// <summary>Items that fill <paramref name="minutes"/> of the contract's share of the bench, rounded to two significant digits.</summary>
        public static int Quantity(double minutes, double craftSeconds, in Tuning t)
        {
            double raw = Math.Ceiling(minutes * 60d / craftSeconds * t.ContractShare);
            double nice = ContractBoard.RoundNice(raw);
            if (nice < t.MinQuantity) return t.MinQuantity;
            if (nice > t.MaxQuantity) return t.MaxQuantity;
            return (int)nice;
        }

        /// <summary>The contract's cash: <paramref name="quantity"/> items at the normal price, plus the premium.</summary>
        public static double Cash(int quantity, double normalUnitPrice, double premium)
            => quantity * normalUnitPrice * (1d + premium);

        /// <summary>
        /// What one item earns selling normally, on average: its price at the bench's level and worker, what perfect
        /// sales add at its stars, and the permanent sale multipliers. The timed boost is left out on purpose, so a
        /// contract accepted during a boost is not priced at double.
        /// </summary>
        public static double NormalUnitPrice(double unitPrice, int stars, double standingMultiplier, in BenchMastery.Tuning mastery)
            => unitPrice * BenchMastery.PerfectAverage(stars, mastery) * standingMultiplier;

        private static int SizeFor(double roll, in Tuning t)
        {
            double total = 0d;
            for (int i = 0; i < t.Sizes.Length; i++) total += t.Sizes[i].Weight;
            double at = roll * total;
            for (int i = 0; i < t.Sizes.Length; i++)
            {
                at -= t.Sizes[i].Weight;
                if (at < 0d) return i;
            }
            return t.Sizes.Length - 1;
        }

        /// <summary>FNV-1a over the business ID, folded with the slot, so neighbouring slots do not walk together.</summary>
        private static uint Seed(string businessId, long slot)
        {
            uint hash = 2166136261u;
            if (businessId != null)
                for (int i = 0; i < businessId.Length; i++) hash = unchecked((hash ^ businessId[i]) * 16777619u);
            hash = unchecked((hash ^ (uint)slot) * 16777619u);
            hash = unchecked((hash ^ (uint)(slot >> 32)) * 16777619u);
            hash = unchecked(hash * 2654435761u);
            hash ^= hash >> 16;
            return hash == 0u ? 1u : hash;
        }

        private static uint Next(uint x)
        {
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            return x;
        }

        private static double Unit(uint x) => (x >> 8) * (1d / 16777216d);

        private static bool Positive(double v) => v > 0d && Finite(v);
        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
