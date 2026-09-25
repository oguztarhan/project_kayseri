using System;

namespace Game.Core
{
    /// <summary>
    /// The flying reward character as pure rules: when it may cross the shop, what one catch pays, and when the
    /// catch streak pays a master card. No clock, no wallet, no dice — callers pass the time that passed, the
    /// state of the screen and their own rolls, the same split <see cref="BenchMastery"/> keeps.
    ///
    /// IT NEVER COMPETES WITH THE BALLOON. The HUD balloon is the older, bigger cash ad; the flier only crosses
    /// while the balloon cannot be claimed, so at most one cash-ad offer is on screen at a time. It also stays away
    /// from the tutorial, from any open screen and from the moments just after another reward, and it pays no
    /// gems: the day's gem budget (RewardBudgetTests) does not move for it.
    ///
    /// MISSING IT COSTS NOTHING. A crossing that is not tapped simply ends; the next one comes after another gap.
    /// </summary>
    public static class Flier
    {
        public struct Tuning
        {
            /// <summary>Foreground seconds between crossings, drawn evenly between these two.</summary>
            public double MinGapSeconds, MaxGapSeconds;
            /// <summary>How long one crossing takes.</summary>
            public double FlightSeconds;
            /// <summary>Catches a UTC day allows; after that it stops crossing until the reset.</summary>
            public int ChargesPerDay;
            /// <summary>Income-minutes one catch pays, and the least it ever pays.</summary>
            public double CashMinutes, CashFloor;
            /// <summary>Catches in a row that pay one master card.</summary>
            public int StreakLength;
            /// <summary>Seconds after any other reward before it may cross.</summary>
            public double QuietSeconds;

            public static Tuning Default => new Tuning
            {
                MinGapSeconds = 240d,
                MaxGapSeconds = 420d,
                FlightSeconds = 12d,
                ChargesPerDay = 8,
                CashMinutes = 4d,
                CashFloor = 100d,
                StreakLength = 5,
                QuietSeconds = 90d
            };

            public void Validate()
            {
                if (!(MinGapSeconds > 0d) || !(MaxGapSeconds >= MinGapSeconds) || double.IsInfinity(MaxGapSeconds) ||
                    !(FlightSeconds > 0d) || double.IsInfinity(FlightSeconds) || ChargesPerDay < 0 ||
                    !(CashMinutes >= 0d) || double.IsInfinity(CashMinutes) || !(CashFloor >= 0d) ||
                    double.IsInfinity(CashFloor) || StreakLength < 1 || !(QuietSeconds >= 0d))
                    throw new ArgumentException("Flier tuning needs positive gaps and flight, a streak of at least one " +
                                                "and non-negative pay.");
            }
        }

        /// <summary>What the screen is doing at the moment a crossing is due.</summary>
        public struct Conditions
        {
            public int ChargesLeft;
            /// <summary>The HUD balloon could be claimed right now.</summary>
            public bool BalloonReady;
            /// <summary>An ad can be shown, or the player owns Remove Ads.</summary>
            public bool AdReady;
            public bool TutorialBlocking;
            /// <summary>A full screen or the bench card covers the shop.</summary>
            public bool PanelOpen;
            /// <summary>Seconds since the last reward of any kind was claimed.</summary>
            public double SecondsSinceReward;
        }

        /// <summary>Whether a crossing that is due may start now. When it may not, it waits rather than skipping.</summary>
        public static bool MaySpawn(in Conditions c, in Tuning t)
            => c.ChargesLeft > 0 && !c.BalloonReady && c.AdReady && !c.TutorialBlocking && !c.PanelOpen &&
               c.SecondsSinceReward >= t.QuietSeconds;

        /// <summary>The foreground wait before the next crossing, for a roll in [0, 1).</summary>
        public static double Gap(double roll, in Tuning t)
        {
            if (!(roll >= 0d)) roll = 0d;
            if (roll > 1d) roll = 1d;
            return t.MinGapSeconds + (t.MaxGapSeconds - t.MinGapSeconds) * roll;
        }

        /// <summary>Cash one catch pays at this income: the income-minutes, never under the floor.</summary>
        public static double Payout(double incomePerMinute, in Tuning t)
        {
            double scaled = incomePerMinute > 0d && !double.IsInfinity(incomePerMinute) ? incomePerMinute * t.CashMinutes : 0d;
            return scaled > t.CashFloor ? scaled : t.CashFloor;
        }

        /// <summary>
        /// The streak after one more catch, and whether that catch completes it. A completed streak starts again
        /// from zero, so every <see cref="Tuning.StreakLength"/>th catch pays the card.
        /// </summary>
        public static int NextStreak(int streak, in Tuning t, out bool paysCard)
        {
            if (streak < 0) streak = 0;
            int next = streak + 1;
            paysCard = next >= t.StreakLength;
            return paysCard ? 0 : next;
        }
    }
}
