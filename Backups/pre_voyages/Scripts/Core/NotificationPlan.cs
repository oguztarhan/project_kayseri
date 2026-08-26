using System;

namespace Game.Core
{
    /// <summary>Which line a scheduled notification carries. The text itself lives in the TSV.</summary>
    public enum NotificationKind
    {
        /// <summary>Still earning: the yards are filling and there is money to come back to.</summary>
        Filling,
        /// <summary>The same, later. A separate line only so the two morning nudges do not read
        /// as the game sending the identical sentence twice in three hours.</summary>
        FillingLate,
        /// <summary>The offline cap has been reached — production has stopped and every further hour is wasted.</summary>
        Full,
        /// <summary>Hours past the cap, nothing has moved.</summary>
        Idle,
        /// <summary>A new UTC day: daily reward and the rewarded-ad charges have reset.</summary>
        NewDay,
        /// <summary>Two days gone.</summary>
        ComeBack
    }

    public struct NotificationSlot
    {
        public NotificationKind Kind;

        /// <summary>Seconds after the player left, at which the OS should post this.</summary>
        public int AfterSeconds;

        /// <summary>
        /// How long the player has been away as far as the TEXT is concerned — what sizes the money the
        /// line quotes. Identical to <see cref="AfterSeconds"/> in normal use; the two only separate
        /// under the test spacing, where six real messages are fired half a minute apart while still
        /// quoting the figures they would carry hours into a genuine absence.
        /// </summary>
        public int AwaySeconds;
    }

    /// <summary>
    /// Decides WHEN to nudge a player who has closed the game, and WHAT state the nudge should describe.
    /// Pure arithmetic on a leave time — no Unity, no save file, no text — so the awkward cases can be
    /// tested instead of discovered on a device.
    ///
    /// The rhythm is one notification every three hours through the waking day. Two things bend it.
    ///
    /// THE CAP IS NOT A CONSTANT. Offline earning stops at OfflineConfig's cap PLUS whatever the player
    /// bought — the "Gece Vardiyasi" offer moves it from 8 hours to 14. So the "your yards are full"
    /// slot is placed relative to that player's own cap rather than at a fixed hour; for a player who
    /// owns nothing the result is exactly 3/6/9/12, and for one who owns the offer it slides to
    /// 3/6/15/18. Telling a Gece Vardiyasi owner their yards are full at hour nine would be a lie the
    /// welcome-back screen then contradicts.
    ///
    /// NOBODY IS WOKEN UP. Anything landing between midnight and 07:00 is pushed to 07:00 rather than
    /// dropped, and slots that collide there collapse into one — the LAST one wins, because the point
    /// of the morning message is to describe where the player actually stands when they wake, not to
    /// replay every hour they slept through. A minimum three-hour gap then keeps the roll-up from being
    /// followed immediately by the slot that was already due.
    /// </summary>
    public static class NotificationPlan
    {
        /// <summary>Candidates before the quiet-hour pass; the pass only ever removes.</summary>
        public const int MaxSlots = 6;

        /// <summary>Nothing fires from midnight until this hour, local time.</summary>
        public const int WakeHour = 7;

        private const int Hour = 3600;
        private const int MinGapSeconds = 3 * Hour;
        private const long DefaultCapSeconds = 8L * Hour;

        /// <summary>
        /// Fills <paramref name="into"/> (at least <see cref="MaxSlots"/> long) and returns how many
        /// slots were written. <paramref name="capSeconds"/> is the player's own offline cap, perks
        /// included.
        ///
        /// <paramref name="testSpacingSeconds"/> is a bench setting, not a design one: above zero it
        /// fires every slot that many seconds apart, skipping both the quiet hours and the minimum gap,
        /// so the whole sequence can be watched arrive on a device in a couple of minutes. The lines
        /// still say what they would say hours in — only the clock is compressed — because a test that
        /// changes the message is not a test of the message.
        /// </summary>
        public static int Build(DateTime leaveLocal, long capSeconds, NotificationSlot[] into,
                                int testSpacingSeconds = 0)
        {
            if (into == null || into.Length < MaxSlots) return 0;
            long cap = capSeconds > 0L ? capSeconds : DefaultCapSeconds;

            // The two early nudges are only honest while the yards are still filling. A cap short
            // enough to have passed them is handled by the Full slot instead.
            var seconds = new long[MaxSlots];
            var kinds = new NotificationKind[MaxSlots];
            int n = 0;
            if (3L * Hour < cap) { seconds[n] = 3L * Hour; kinds[n] = NotificationKind.Filling; n++; }
            if (6L * Hour < cap) { seconds[n] = 6L * Hour; kinds[n] = NotificationKind.FillingLate; n++; }
            seconds[n] = cap + Hour; kinds[n] = NotificationKind.Full; n++;
            seconds[n] = cap + 4L * Hour; kinds[n] = NotificationKind.Idle; n++;
            seconds[n] = 24L * Hour; kinds[n] = NotificationKind.NewDay; n++;
            seconds[n] = 48L * Hour; kinds[n] = NotificationKind.ComeBack; n++;

            // A large enough cap bonus puts the Idle slot past the next day, so the list is not
            // already sorted. Six items: insertion sort, and no allocation worth avoiding.
            for (int i = 1; i < n; i++)
            {
                long s = seconds[i];
                NotificationKind k = kinds[i];
                int j = i - 1;
                while (j >= 0 && seconds[j] > s) { seconds[j + 1] = seconds[j]; kinds[j + 1] = kinds[j]; j--; }
                seconds[j + 1] = s; kinds[j + 1] = k;
            }

            if (testSpacingSeconds > 0)
            {
                for (int i = 0; i < n; i++)
                {
                    into[i].Kind = kinds[i];
                    into[i].AfterSeconds = (i + 1) * testSpacingSeconds;
                    into[i].AwaySeconds = (int)seconds[i];
                }
                return n;
            }

            int written = 0;
            for (int i = 0; i < n; i++)
            {
                DateTime fire = leaveLocal.AddSeconds(seconds[i]);
                if (fire.Hour < WakeHour) fire = fire.Date.AddHours(WakeHour);   // pushed to the morning
                long s = (long)(fire - leaveLocal).TotalSeconds;
                if (s <= 0L) continue;

                if (written > 0)
                {
                    long prev = into[written - 1].AfterSeconds;
                    // Same morning: this candidate describes a later state than the one already
                    // sitting there, so it replaces it rather than queueing behind it.
                    if (s == prev) { into[written - 1].Kind = kinds[i]; continue; }
                    if (s - prev < MinGapSeconds) continue;
                }

                into[written].Kind = kinds[i];
                into[written].AfterSeconds = (int)s;
                into[written].AwaySeconds = (int)s;   // fires exactly when it claims to
                written++;
            }
            return written;
        }
    }
}
