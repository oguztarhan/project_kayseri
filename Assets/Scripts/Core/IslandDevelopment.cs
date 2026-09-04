using System;

namespace Game.Core
{
    /// <summary>
    /// Derived island progression and recommendation rules for the station screen.
    /// Nothing here is persisted: existing station levels and expansion flags remain the authority,
    /// so an old save immediately receives the correct development level after an update.
    /// </summary>
    public static class IslandDevelopment
    {
        public const int PointsPerAxisLevel = 1;
        public const int PointsPerUnlock = 5;
        public const int PointsPerDevelopmentLevel = 25;
        public const int FirstLevel = 1;
        public const int RecommendedCount = 4;

        public struct Progress
        {
            public int Level;
            public int MaxLevel;
            public int Points;
            public int MaxPoints;
            public int PointsIntoLevel;
            public int PointsForNextLevel;
            public float Normalized;
            public bool IsMaxed;
        }

        public struct Recommendation
        {
            public int Station;
            public int Axis;
            public int Level;
            public int Cap;
            public BigDouble Cost;
            public bool Affordable;
        }

        public static Progress Measure(IslandEconomy economy)
        {
            var result = new Progress();
            if (economy == null) return result;

            int points = 0;
            int maxPoints = 0;
            int[][] levels = economy.Levels;
            for (int station = 0; station < levels.Length; station++)
                for (int axis = 0; axis < levels[station].Length; axis++)
                {
                    int cap = economy.AxisCap(station, axis);
                    points += Math.Max(0, Math.Min(levels[station][axis], cap)) * PointsPerAxisLevel;
                    maxPoints += cap * PointsPerAxisLevel;
                }

            bool[] unlocks = economy.Unlocked;
            if (unlocks != null)
                for (int i = 0; i < unlocks.Length; i++)
                {
                    if (unlocks[i]) points += PointsPerUnlock;
                    maxPoints += PointsPerUnlock;
                }

            bool maxed = maxPoints > 0 && points >= maxPoints;
            int maxLevel = FirstLevel + (maxPoints / PointsPerDevelopmentLevel);
            int level = FirstLevel + (points / PointsPerDevelopmentLevel);
            if (level > maxLevel) level = maxLevel;

            int into = maxed ? PointsPerDevelopmentLevel : points % PointsPerDevelopmentLevel;
            result.Level = level;
            result.MaxLevel = maxLevel;
            result.Points = points;
            result.MaxPoints = maxPoints;
            result.PointsIntoLevel = into;
            result.PointsForNextLevel = PointsPerDevelopmentLevel;
            result.Normalized = maxPoints > 0 ? Math.Min(1f, points / (float)maxPoints) : 0f;
            result.IsMaxed = maxed;
            return result;
        }

        /// <summary>
        /// Affordable upgrades come first. Within each affordability band, the least-developed
        /// station wins before price, which spreads progress around the production chain instead of
        /// repeatedly recommending the same cheap axis. Final index ordering makes ties stable.
        /// </summary>
        public static int Compare(in Recommendation left, in Recommendation right)
        {
            if (left.Affordable != right.Affordable) return left.Affordable ? -1 : 1;

            long leftCompletion = (long)Math.Max(0, left.Level) * Math.Max(1, right.Cap);
            long rightCompletion = (long)Math.Max(0, right.Level) * Math.Max(1, left.Cap);
            if (leftCompletion != rightCompletion) return leftCompletion < rightCompletion ? -1 : 1;

            int cost = left.Cost.CompareTo(right.Cost);
            if (cost != 0) return cost;
            if (left.Station != right.Station) return left.Station.CompareTo(right.Station);
            return left.Axis.CompareTo(right.Axis);
        }

        /// <summary>The next island needs both the previous rung and its chapter objectives.</summary>
        public static bool CanUnlockNext(int destinationIndex, bool previousOwned, bool previousObjectivesComplete)
            => destinationIndex > 0 && previousOwned && previousObjectivesComplete;
    }
}
