using System;

namespace Game.Core
{
    public struct NotificationCandidate
    {
        public string Id;
        public string Title;
        public string Message;
        public string Target;
        public int AfterSeconds;
        public int Priority;
    }

    /// <summary>Shared quiet-hour, spacing and daily-cap rules for every notification source.</summary>
    public static class NotificationSchedulePlanner
    {
        public const int MaxCandidates = 24;
        public const int MaxScheduled = 8;
        public const int WakeHour = 7;
        public const int MinGapSeconds = 3 * 60 * 60;
        public const int MaxPerLocalDay = 3;

        public static int Build(DateTime leaveLocal, NotificationCandidate[] candidates, int count,
                                NotificationCandidate[] output)
        {
            if (candidates == null || output == null || output.Length < MaxScheduled || count <= 0) return 0;
            if (count > candidates.Length) count = candidates.Length;
            if (count > MaxCandidates) count = MaxCandidates;

            var work = new NotificationCandidate[count];
            for (int i = 0; i < count; i++)
            {
                work[i] = candidates[i];
                work[i].AfterSeconds = QuietAdjusted(leaveLocal, work[i].AfterSeconds);
            }

            for (int i = 1; i < count; i++)
            {
                NotificationCandidate value = work[i];
                int j = i - 1;
                while (j >= 0 && (work[j].AfterSeconds > value.AfterSeconds
                                  || (work[j].AfterSeconds == value.AfterSeconds
                                      && work[j].Priority < value.Priority)))
                {
                    work[j + 1] = work[j];
                    j--;
                }
                work[j + 1] = value;
            }

            int written = 0;
            for (int i = 0; i < count && written < MaxScheduled; i++)
            {
                NotificationCandidate candidate = work[i];
                if (candidate.AfterSeconds <= 0) continue;

                if (written > 0 && candidate.AfterSeconds - output[written - 1].AfterSeconds < MinGapSeconds)
                {
                    if (candidate.Priority > output[written - 1].Priority)
                        output[written - 1] = candidate;
                    continue;
                }

                DateTime day = leaveLocal.AddSeconds(candidate.AfterSeconds).Date;
                int onDay = 0;
                int weakestOnDay = -1;
                for (int o = 0; o < written; o++)
                {
                    if (leaveLocal.AddSeconds(output[o].AfterSeconds).Date != day) continue;
                    onDay++;
                    if (weakestOnDay < 0 || output[o].Priority < output[weakestOnDay].Priority)
                        weakestOnDay = o;
                }
                if (onDay >= MaxPerLocalDay)
                {
                    if (weakestOnDay < 0 || candidate.Priority <= output[weakestOnDay].Priority) continue;
                    for (int o = weakestOnDay; o < written - 1; o++) output[o] = output[o + 1];
                    written--;
                }

                output[written++] = candidate;
            }
            return written;
        }

        private static int QuietAdjusted(DateTime leaveLocal, int afterSeconds)
        {
            if (afterSeconds <= 0) return 0;
            DateTime fire = leaveLocal.AddSeconds(afterSeconds);
            if (fire.Hour < WakeHour) fire = fire.Date.AddHours(WakeHour);
            long adjusted = (long)(fire - leaveLocal).TotalSeconds;
            return adjusted <= 0L ? 0 : adjusted > int.MaxValue ? int.MaxValue : (int)adjusted;
        }
    }
}
