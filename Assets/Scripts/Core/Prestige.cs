namespace Game.Core
{
    /// <summary>
    /// Pure prestige math (GDD §8). Investors are earned by cashing in a run; each one grants
    /// a permanent global income multiplier. Unit-testable, no Unity.
    /// </summary>
    public static class Prestige
    {
        /// <summary>
        /// Investors earned by cashing in a run of <paramref name="lifetimeCash"/>.
        ///
        /// Measured against <paramref name="reference"/> — what a run at the player's CURRENT
        /// tier is worth — so a reset means the same thing wherever they are on the ladder.
        ///
        /// It used to be the raw square root of lifetime cash, and lifetime cash grows with
        /// the tier: at 2% per investor, one reset after finishing Coal was worth 70x
        /// permanent income and one at Diamond nearly 7,000x. Anyone who pressed the button
        /// once had finished the game. Dividing by the tier's own scale first is what makes
        /// the payout a fixed step instead of an exponential one.
        ///
        /// Still a square root, so grinding twice as long before cashing in is worth 1.41x,
        /// not 2x — over-farming a single run stays a bad deal.
        /// </summary>
        public static BigDouble Investors(BigDouble lifetimeCash, double k, BigDouble reference)
        {
            if (lifetimeCash.Mantissa <= 0d || k <= 0d || reference.Mantissa <= 0d) return BigDouble.Zero;
            return (lifetimeCash / reference).Pow(0.5) * k;
        }

        public static double IncomeMultiplier(double investors, double bonusPerInvestor)
            => 1d + (investors > 0d ? investors * bonusPerInvestor : 0d);
    }
}
