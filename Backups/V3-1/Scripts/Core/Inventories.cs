using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>Helpers for moving resources between inventories — used by the truck haulers (GDD §3).</summary>
    public static class Inventories
    {
        /// <summary>
        /// Move up to <paramref name="maxTotal"/> total (across all resource keys) from <paramref name="src"/>
        /// into <paramref name="dst"/>, clamped by dst's free space. Returns the total amount moved.
        /// </summary>
        public static BigDouble Transfer<TKey>(Inventory<TKey> src, Inventory<TKey> dst, BigDouble maxTotal)
            where TKey : class
        {
            if (src == null || dst == null || maxTotal.Mantissa <= 0d) return BigDouble.Zero;

            BigDouble moved = BigDouble.Zero;
            var keys = new List<TKey>(src.Amounts.Keys); // snapshot: we mutate src below
            for (int i = 0; i < keys.Count; i++)
            {
                BigDouble budget = maxTotal - moved;
                if (budget.Mantissa <= 0d) break;

                BigDouble avail = src.Get(keys[i]);
                BigDouble want = avail < budget ? avail : budget;
                if (want.Mantissa <= 0d) continue;

                BigDouble accepted = dst.Add(keys[i], want); // clamped by dst space
                src.Remove(keys[i], accepted);
                moved += accepted;
            }
            return moved;
        }
    }
}
