using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// A multi-resource buffer keyed by <typeparamref name="TKey"/> (an ore/product definition), with a
    /// shared total capacity. Storage, refinery I/O, and the market each hold one. Add/Remove clamp and
    /// report how much moved, which is how bottlenecks surface (GDD §2). Main-thread only.
    /// </summary>
    public sealed class Inventory<TKey> where TKey : class
    {
        private readonly Dictionary<TKey, BigDouble> _amounts = new Dictionary<TKey, BigDouble>();

        public BigDouble Capacity { get; set; }

        public Inventory(BigDouble capacity)
        {
            Capacity = capacity;
        }

        public IReadOnlyDictionary<TKey, BigDouble> Amounts => _amounts;

        public BigDouble Get(TKey key) => _amounts.TryGetValue(key, out var v) ? v : BigDouble.Zero;

        public BigDouble Total
        {
            get
            {
                BigDouble total = BigDouble.Zero;
                foreach (var v in _amounts.Values) total += v;
                return total;
            }
        }

        public BigDouble Space => Capacity - Total;
        public bool IsFull => Total >= Capacity;

        /// <summary>Add up to remaining total capacity; returns the amount accepted.</summary>
        public BigDouble Add(TKey key, BigDouble amount)
        {
            if (key == null || amount.Mantissa <= 0d) return BigDouble.Zero;
            BigDouble space = Space;
            BigDouble accepted = amount > space ? space : amount;
            if (accepted.Mantissa <= 0d) return BigDouble.Zero;
            _amounts[key] = Get(key) + accepted;
            return accepted;
        }

        /// <summary>Remove up to the amount held of that key; returns the amount removed.</summary>
        public BigDouble Remove(TKey key, BigDouble amount)
        {
            if (key == null || amount.Mantissa <= 0d) return BigDouble.Zero;
            BigDouble have = Get(key);
            BigDouble removed = amount > have ? have : amount;
            if (removed.Mantissa <= 0d) return BigDouble.Zero;
            _amounts[key] = have - removed;
            return removed;
        }
    }
}
