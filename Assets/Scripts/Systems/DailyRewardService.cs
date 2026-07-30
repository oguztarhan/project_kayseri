using System;

namespace Game.Systems
{
    /// <summary>
    /// Daily reward (GDD §11) as a 7-day streak ladder. This service owns only the STATE — which
    /// day the player is on and whether today is claimed — tracked in the save (UTC calendar days).
    /// What each day pays lives on the daily-reward screen as an Inspector-editable table, because
    /// day rewards can include "minutes of income", which only the scene can price.
    ///
    /// Missing a full calendar day resets the streak; the save mutates only on claim, so the reset
    /// is computed on read (<see cref="EffectiveStreak"/>) until the next claim stamps it.
    /// </summary>
    public sealed class DailyRewardService
    {
        public const int CycleDays = 7;

        private readonly SaveData _data;
        private readonly TimeService _time;

        public DailyRewardService(SaveData data, TimeService time)
        {
            _data = data; _time = time;
        }

        public bool CanClaim()
        {
            if (_data.lastDailyClaimUnix <= 0L) return true;
            return DaysSinceLastClaim() >= 1;
        }

        /// <summary>Streak with a missed-day reset applied; equals the raw save value otherwise.</summary>
        public int EffectiveStreak
        {
            get
            {
                if (_data.lastDailyClaimUnix <= 0L) return 0;
                return DaysSinceLastClaim() >= 2 ? 0 : _data.dailyStreak;
            }
        }

        /// <summary>
        /// Tile the player is on today, 0-based: the next claimable day, or the day just claimed.
        /// </summary>
        public int DayIndex
        {
            get
            {
                int streak = EffectiveStreak;
                if (CanClaim()) return streak % CycleDays;
                return streak > 0 ? (streak - 1) % CycleDays : 0;
            }
        }

        /// <summary>
        /// Validates, applies any streak reset, stamps the claim time and advances the streak.
        /// Returns the claimed day index (0..6), or -1 when nothing was claimable — the caller
        /// grants the actual reward.
        /// </summary>
        public int Claim()
        {
            if (!CanClaim()) return -1;
            int streak = EffectiveStreak;
            int day = streak % CycleDays;
            _data.dailyStreak = streak + 1;
            _data.lastDailyClaimUnix = _time.NowUnix();
            return day;
        }

        private int DaysSinceLastClaim()
        {
            DateTime last = DateTimeOffset.FromUnixTimeSeconds(_data.lastDailyClaimUnix).UtcDateTime.Date;
            DateTime today = DateTimeOffset.FromUnixTimeSeconds(_time.NowUnix()).UtcDateTime.Date;
            return (int)(today - last).TotalDays;
        }
    }
}
