using System;

namespace Game.Core
{
    /// <summary>
    /// Compact large-number type for idle-economy values that outgrow double/long.
    /// Value = Mantissa * 10^Exponent, kept normalized to 1 &lt;= |Mantissa| &lt; 10 (or exactly zero).
    /// Value-type semantics, no per-operation heap allocation. Treat instances as immutable.
    /// Public fields so Unity's JsonUtility can serialize it directly inside save data.
    /// </summary>
    [Serializable]
    public struct BigDouble : IComparable<BigDouble>, IEquatable<BigDouble>
    {
        public double Mantissa;
        public long Exponent;

        public static readonly BigDouble Zero = new BigDouble(0d, 0L, false);
        public static readonly BigDouble One = new BigDouble(1d, 0L, false);

        public BigDouble(double value)
        {
            Mantissa = value;
            Exponent = 0L;
            Normalize();
        }

        public BigDouble(double mantissa, long exponent)
        {
            Mantissa = mantissa;
            Exponent = exponent;
            Normalize();
        }

        private BigDouble(double mantissa, long exponent, bool normalize)
        {
            Mantissa = mantissa;
            Exponent = exponent;
            if (normalize) Normalize();
        }

        public bool IsZero => Mantissa == 0d;

        private void Normalize()
        {
            if (Mantissa == 0d || double.IsNaN(Mantissa))
            {
                Mantissa = 0d;
                Exponent = 0L;
                return;
            }
            if (double.IsInfinity(Mantissa)) return;

            double absM = Math.Abs(Mantissa);
            if (absM >= 1d && absM < 10d) return;

            long delta = (long)Math.Floor(Math.Log10(absM));
            Mantissa /= PowTen(delta);
            Exponent += delta;

            // Correct any rounding drift at the boundaries.
            absM = Math.Abs(Mantissa);
            if (absM >= 10d) { Mantissa /= 10d; Exponent += 1L; }
            else if (absM < 1d) { Mantissa *= 10d; Exponent -= 1L; }
        }

        private static double PowTen(long power) => Math.Pow(10d, power);

        public double ToDouble() => Mantissa * PowTen(Exponent);

        public BigDouble Abs() => new BigDouble(Math.Abs(Mantissa), Exponent, false);

        public static BigDouble operator +(BigDouble a, BigDouble b)
        {
            if (a.Mantissa == 0d) return b;
            if (b.Mantissa == 0d) return a;
            if (a.Exponent < b.Exponent) { BigDouble t = a; a = b; b = t; }
            long diff = a.Exponent - b.Exponent;
            if (diff > 17L) return a; // b falls below a's double precision
            double bMant = b.Mantissa / PowTen(diff);
            return new BigDouble(a.Mantissa + bMant, a.Exponent);
        }

        public static BigDouble operator -(BigDouble a) => new BigDouble(-a.Mantissa, a.Exponent, false);
        public static BigDouble operator -(BigDouble a, BigDouble b) => a + (-b);

        public static BigDouble operator *(BigDouble a, BigDouble b)
        {
            if (a.Mantissa == 0d || b.Mantissa == 0d) return Zero;
            return new BigDouble(a.Mantissa * b.Mantissa, a.Exponent + b.Exponent);
        }

        public static BigDouble operator /(BigDouble a, BigDouble b)
        {
            if (a.Mantissa == 0d || b.Mantissa == 0d) return Zero;
            return new BigDouble(a.Mantissa / b.Mantissa, a.Exponent - b.Exponent);
        }

        /// <summary>Raise this value to a (double) power — used for exponential cost curves.</summary>
        public BigDouble Pow(double power)
        {
            if (Mantissa == 0d) return power == 0d ? One : Zero;
            double log10 = Math.Log10(Math.Abs(Mantissa)) + Exponent;
            return FromLog10(log10 * power);
        }

        /// <summary>baseValue ^ power as a BigDouble (baseValue and power fit in double).</summary>
        public static BigDouble Pow(double baseValue, double power)
        {
            if (baseValue == 0d) return Zero;
            return FromLog10(Math.Log10(baseValue) * power);
        }

        private static BigDouble FromLog10(double log10)
        {
            long newExp = (long)Math.Floor(log10);
            double newMant = Math.Pow(10d, log10 - newExp);
            return new BigDouble(newMant, newExp);
        }

        public int CompareTo(BigDouble other)
        {
            int sa = Math.Sign(Mantissa);
            int sb = Math.Sign(other.Mantissa);
            if (sa != sb) return sa.CompareTo(sb);
            if (sa == 0) return 0;
            if (Exponent != other.Exponent)
            {
                int expCmp = Exponent.CompareTo(other.Exponent);
                return sa > 0 ? expCmp : -expCmp;
            }
            return Mantissa.CompareTo(other.Mantissa);
        }

        public bool Equals(BigDouble other) => Mantissa == other.Mantissa && Exponent == other.Exponent;
        public override bool Equals(object obj) => obj is BigDouble b && Equals(b);
        public override int GetHashCode() => Mantissa.GetHashCode() ^ Exponent.GetHashCode();

        public static bool operator ==(BigDouble a, BigDouble b) => a.Equals(b);
        public static bool operator !=(BigDouble a, BigDouble b) => !a.Equals(b);
        public static bool operator <(BigDouble a, BigDouble b) => a.CompareTo(b) < 0;
        public static bool operator >(BigDouble a, BigDouble b) => a.CompareTo(b) > 0;
        public static bool operator <=(BigDouble a, BigDouble b) => a.CompareTo(b) <= 0;
        public static bool operator >=(BigDouble a, BigDouble b) => a.CompareTo(b) >= 0;

        public static implicit operator BigDouble(double value) => new BigDouble(value);

        public override string ToString() => NumberFormatter.Format(this);
    }
}
