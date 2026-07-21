using System;
using System.Globalization;

namespace Game.Core
{
    /// <summary>
    /// Formats <see cref="BigDouble"/> values into short human-readable strings
    /// (e.g. 1.5K, 2.3M, 4.1B, 9.9T, then 1.2aa, 3.4ab ...).
    /// Culture-invariant so output is identical on every device locale.
    /// Not a per-frame hot path — displayed strings are cached by the UI.
    /// </summary>
    public static class NumberFormatter
    {
        private static readonly string[] ShortSuffixes = { "", "K", "M", "B", "T" };

        public static string Format(BigDouble value, int decimals = 2)
        {
            if (value.Mantissa == 0d) return "0";

            bool negative = value.Mantissa < 0d;
            double mant = Math.Abs(value.Mantissa);
            long exp = value.Exponent;

            string body;
            if (exp < 3L)
            {
                double plain = mant * Math.Pow(10d, exp);
                body = plain.ToString("0.##", CultureInfo.InvariantCulture);
            }
            else
            {
                long group = exp / 3L;
                int remainder = (int)(exp % 3L);
                double display = mant * Math.Pow(10d, remainder); // 1..1000
                string format = decimals > 0 ? "0." + new string('#', decimals) : "0";
                body = display.ToString(format, CultureInfo.InvariantCulture) + SuffixFor(group);
            }

            return negative ? "-" + body : body;
        }

        /// <summary>Suffix for a power-of-1000 group: 1=K, 2=M, 3=B, 4=T, 5=aa, 6=ab ...</summary>
        public static string SuffixFor(long group)
        {
            if (group <= 0L) return string.Empty;
            if (group < ShortSuffixes.Length) return ShortSuffixes[group];

            long idx = group - ShortSuffixes.Length; // 0 -> "aa"
            int first = (int)(idx / 26L);
            int second = (int)(idx % 26L);
            char c1 = (char)('a' + first);
            char c2 = (char)('a' + second);
            return new string(new[] { c1, c2 });
        }
    }
}
