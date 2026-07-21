namespace Game.Core
{
    /// <summary>
    /// Pure prestige math (GDD §8). Investors gained scale with the square root of lifetime cash;
    /// each investor grants a permanent global income multiplier. Unit-testable, no Unity.
    /// </summary>
    public static class Prestige
    {
        public static BigDouble Investors(BigDouble lifetimeCash, double k)
        {
            if (lifetimeCash.Mantissa <= 0d || k <= 0d) return BigDouble.Zero;
            return lifetimeCash.Pow(0.5) * k;
        }

        public static double IncomeMultiplier(double investors, double bonusPerInvestor)
            => 1d + (investors > 0d ? investors * bonusPerInvestor : 0d);
    }
}
