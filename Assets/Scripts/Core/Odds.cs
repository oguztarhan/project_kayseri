namespace Game.Core
{
    /// <summary>
    /// What the odds sheet is allowed to say. Both stores require the chance of a paid randomised
    /// pull to be shown to the player BEFORE they buy — the master chest is bought with gems and gems
    /// are sold for money, so it is a paid mechanic and the disclosure is not optional. The captain
    /// crate is bought with charts, which cannot be bought at all, so it is outside the requirement;
    /// it is disclosed anyway, because a pity rule nobody can see is the exact thing players assume
    /// is rigged.
    ///
    /// WHY THIS IS NOT A TABLE OF WRITTEN-DOWN PERCENTAGES. A hand-maintained odds sheet is a lie
    /// waiting for the first balance pass — someone retunes <see cref="CaptainCrate.Tuning"/>, the
    /// screen keeps printing last month's numbers, and now the game states a false probability in a
    /// place the platform holds us to. Everything here is DERIVED from the same tuning structs the
    /// roll itself reads, through the same <see cref="CaptainCrate.WeightOf"/> normalisation, so the
    /// only way for the sheet to disagree with the game is for this file to be wrong — which
    /// <c>OddsTests</c> checks by sampling the real roll a hundred thousand times.
    ///
    /// The numbers are the BASE odds: what a pull is worth with no pity owed. That is what "the odds
    /// of a pull" means, and the pity rules are disclosed separately in words rather than folded into
    /// a percentage that would then be true of no particular pull.
    /// </summary>
    public static class Odds
    {
        // ---------------------------------------------------------------- captain crate
        /// <summary>
        /// The chance one pull comes out at <paramref name="grade"/>, given how long the two dry runs
        /// have been going. Mirrors <see cref="CaptainCrate.RollGrade"/> exactly, including its two
        /// edge cases: a grade below the pity floor cannot come out at all, and when every grade at or
        /// above the floor is unpopulated the roll returns the highest populated grade with certainty
        /// rather than rolling.
        /// </summary>
        public static double CaptainGradeChance(int grade, int sinceEpic, int sinceLegendary,
                                                in CaptainCrate.Tuning t)
        {
            if (grade < 0 || grade >= Captains.GradeCount) return 0d;

            int floor = (int)CaptainCrate.Floor(sinceEpic, sinceLegendary, t);

            double total = 0d;
            for (int g = floor; g < Captains.GradeCount; g++)
                total += CaptainCrate.WeightOf(g, sinceLegendary, t);

            if (total <= 0d) return grade == HighestPopulatedGrade() ? 1d : 0d;
            if (grade < floor) return 0d;
            return CaptainCrate.WeightOf(grade, sinceLegendary, t) / total;
        }

        /// <summary>The base table: what a pull is worth with nothing owed. This is the row set the
        /// sheet prints.</summary>
        public static double CaptainGradeChance(int grade, in CaptainCrate.Tuning t)
            => CaptainGradeChance(grade, 0, 0, t);

        /// <summary>
        /// The grade <see cref="CaptainCrate.RollGrade"/> falls back to when the floor has priced
        /// every reachable grade out of the table. Common when the roster is empty, which cannot
        /// happen in a shipped build but is what the roll returns, so it is what this returns too.
        /// </summary>
        private static int HighestPopulatedGrade()
        {
            for (int g = Captains.GradeCount - 1; g >= 0; g--)
                if (Captains.CountOfGrade((Captains.Grade)g) > 0) return g;
            return (int)Captains.Grade.Common;
        }

        // ---------------------------------------------------------------- master chest
        /// <summary>
        /// Cards in one chest that are rolled rather than aimed. The aimed ones are not chance and
        /// must not be presented as if they were — see <see cref="MasterChest.DirectedIn"/>.
        /// </summary>
        public static int MasterRolledCards(in MasterChest.Tuning t)
            => MasterChest.CardsFor(1, t) - MasterChest.DirectedIn(t);

        /// <summary>The chance one rolled card lands on any given master. Flat, matching
        /// <see cref="MasterChest.RollSlot"/>.</summary>
        public static double MasterSlotChance() => Foremen.Count > 0 ? 1d / Foremen.Count : 0d;
    }
}
