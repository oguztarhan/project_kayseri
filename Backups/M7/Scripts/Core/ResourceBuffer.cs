namespace Game.Core
{
    /// <summary>
    /// A capacity-limited resource buffer (e.g. ore waiting at a station). Uses BigDouble so it
    /// scales with the economy. Add/Remove clamp and report how much actually moved, which is how
    /// stations detect back-pressure / bottlenecks (GDD §2).
    /// </summary>
    public sealed class ResourceBuffer
    {
        public BigDouble Amount { get; private set; }
        public BigDouble Capacity { get; set; }

        public ResourceBuffer(BigDouble capacity)
        {
            Capacity = capacity;
            Amount = BigDouble.Zero;
        }

        public BigDouble Space => Capacity - Amount;
        public bool IsFull => Amount >= Capacity;
        public bool IsEmpty => Amount.Mantissa <= 0d;

        /// <summary>Add up to remaining capacity; returns the amount actually accepted.</summary>
        public BigDouble Add(BigDouble amount)
        {
            if (amount.Mantissa <= 0d) return BigDouble.Zero;
            BigDouble space = Space;
            BigDouble accepted = amount > space ? space : amount;
            if (accepted.Mantissa <= 0d) return BigDouble.Zero;
            Amount += accepted;
            return accepted;
        }

        /// <summary>Remove up to the amount present; returns the amount actually removed.</summary>
        public BigDouble Remove(BigDouble amount)
        {
            if (amount.Mantissa <= 0d) return BigDouble.Zero;
            BigDouble removed = amount > Amount ? Amount : amount;
            Amount -= removed;
            return removed;
        }

        /// <summary>Direct set, for restoring from a save.</summary>
        public void SetAmount(BigDouble amount) => Amount = amount;
    }
}
