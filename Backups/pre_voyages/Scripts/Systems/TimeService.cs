using System;

namespace Game.Systems
{
    /// <summary>
    /// Current time + elapsed-span validation. M3 uses the device clock but rejects rollback
    /// (negative elapsed → 0), the cheap anti clock-cheat. Server-trusted time lands in M5.
    /// </summary>
    public sealed class TimeService
    {
        public long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        /// <summary>Seconds elapsed since a past timestamp, clamped non-negative (rejects clock rollback).</summary>
        public long ElapsedSince(long pastUnix)
        {
            long e = NowUnix() - pastUnix;
            return e < 0L ? 0L : e;
        }
    }
}
