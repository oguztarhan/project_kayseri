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

            var keys = new List<TKey>(src.Amounts.Keys); // snapshot: we mutate src below
            if (keys.Count == 0) return BigDouble.Zero;

            // Total available across all keys, so we can move a *representative mix* rather than draining
            // one key first — otherwise a truck grabs only the first ore type and combine recipes (Steel =
            // Iron + Coal) never get their second input (GDD §4).
            BigDouble total = BigDouble.Zero;
            for (int i = 0; i < keys.Count; i++) total += src.Get(keys[i]);
            if (total.Mantissa <= 0d) return BigDouble.Zero;

            BigDouble take = maxTotal < total ? maxTotal : total;
            BigDouble moved = BigDouble.Zero;
            for (int i = 0; i < keys.Count; i++)
            {
                BigDouble avail = src.Get(keys[i]);
                if (avail.Mantissa <= 0d) continue;

                BigDouble share = take * (avail / total);   // proportional to this key's share
                if (share > avail) share = avail;
                if (share.Mantissa <= 0d) continue;

                BigDouble accepted = dst.Add(keys[i], share); // clamped by dst space
                src.Remove(keys[i], accepted);
                moved += accepted;
            }
            return moved;
        }
    }
}
