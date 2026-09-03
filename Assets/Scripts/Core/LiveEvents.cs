using System;

namespace Game.Core
{
    /// <summary>
    /// The live-event lifecycle: when an event is coming, when it is running, and when it has closed.
    ///
    /// WHY IT IS ITS OWN FILE. Docs/FIVE_LAYERS.md §1 named the wrapper the reference game runs and we
    /// do not own: "Chapters, events, leaderboards." Chapters landed. This is the second of the three,
    /// and it is deliberately the LIFECYCLE ONLY — no tasks, no rewards, no seasons. Five separate
    /// features are meant to sit on top of it, and every one of them needs the same four answers: has
    /// it started, has it ended, which slot has been earned, and which has been taken. Answering those
    /// five times in five services is how a live-ops layer rots.
    ///
    /// THE CLOCK IS AN ARGUMENT, NOT A CALL. Nothing here reads the system time — every function takes
    /// nowUnix, the same contract <see cref="Crafting.RollGrade"/> and <see cref="CaptainCrate.RollGrade"/>
    /// keep with their dice. It is what lets a test assert the exact second an event opens instead of
    /// waiting for one. The service supplies the clock.
    ///
    /// THE WINDOW CLOSES; THE REWARD DOES NOT. This is the one rule worth reading twice, and it is
    /// FIVE_LAYERS.md R3 applied to timed content: "Nothing expires — an idle game must never punish a
    /// player for looking away." So <see cref="Phase.Closed"/> stops PROGRESS and nothing else. A slot
    /// whose work was finished while the event ran stays claimable forever, exactly as an unclaimed
    /// contract reward waits at the port. An event that took a reward back for being on the wrong side
    /// of a midnight would be the first thing in this game that punished absence.
    ///
    /// HALF-OPEN, LIKE EVERY OTHER WINDOW. An event is Active from StartUnix INCLUSIVE to EndUnix
    /// EXCLUSIVE. The second EndUnix names is the first second of Closed, so two events scheduled back
    /// to back cannot both be live for one shared second — the bug that would let one task count twice.
    /// </summary>
    public static class LiveEvents
    {
        /// <summary>Where an event sits relative to a moment. Analytics payloads carry these by NUMBER,
        /// so the order is append-only like every other enum the save layer leans on.</summary>
        public enum Phase
        {
            Upcoming = 0,   // scheduled, not yet open: the hub shows a countdown
            Active   = 1,   // running: the only phase in which progress may accrue
            Closed   = 2,   // the window has passed. Earned-but-unclaimed slots remain claimable.
        }

        /// <summary>The most slots one event may carry. A bound rather than a guess: the arrays are
        /// saved per event, and an unbounded count in a config is an unbounded array in every save.</summary>
        public const int MaxSlots = 64;

        /// <summary>
        /// One scheduled event, as configured. Everything here is locked in when the definition is
        /// read; nothing in this struct is derived from the save.
        /// </summary>
        public struct Definition
        {
            /// <summary>Immutable identity. It keys the save row, so it must never be reused for
            /// different content — a renamed event is a NEW id, not an edited one.</summary>
            public string Id;

            /// <summary>Which module owns the content. Opaque here on purpose: this file schedules
            /// events, it does not know what any of them do.</summary>
            public int Kind;

            /// <summary>The window, in unix seconds. Wall clock, so it survives a restart.</summary>
            public long StartUnix;
            public long EndUnix;

            /// <summary>Bumped by whoever edits the event's content after it has shipped. Progress
            /// earned under an older version is not carried into a newer one — see
            /// <see cref="ProgressSurvives"/> for why that is the safe direction.</summary>
            public int ConfigVersion;

            /// <summary>Claimable slots this event carries — milestones, task rows, tiers. The save
            /// row's arrays are this long.</summary>
            public int Slots;

            /// <summary>Islands the player must own before the event is offered at all. 0 = everyone.
            /// The one eligibility axis the hub needs; anything richer belongs to the module.</summary>
            public int MinIslands;
        }

        /// <summary>
        /// Whether a definition can be scheduled at all. Checked on load rather than trusted: a config
        /// is edited by hand in an Inspector, and every one of these mistakes is silent at runtime — an
        /// empty id collides with the next empty id in the save, and an end before its start is an
        /// event that is Closed the moment it is written.
        /// </summary>
        public static bool IsWellFormed(in Definition d)
        {
            if (string.IsNullOrEmpty(d.Id)) return false;
            if (d.Slots <= 0 || d.Slots > MaxSlots) return false;
            if (d.StartUnix <= 0L || d.EndUnix <= 0L) return false;
            return d.EndUnix > d.StartUnix;
        }

        /// <summary>Where <paramref name="nowUnix"/> falls in the window. Half-open: see the class note.</summary>
        public static Phase PhaseAt(in Definition d, long nowUnix)
        {
            if (nowUnix < d.StartUnix) return Phase.Upcoming;
            return nowUnix < d.EndUnix ? Phase.Active : Phase.Closed;
        }

        /// <summary>Seconds until the window opens; 0 once it has. Never negative.</summary>
        public static long SecondsUntilStart(in Definition d, long nowUnix)
        {
            long s = d.StartUnix - nowUnix;
            return s > 0L ? s : 0L;
        }

        /// <summary>Seconds until the window closes; 0 before it opens and 0 once it has closed. Two
        /// different zeroes on purpose — a countdown asks <see cref="PhaseAt"/> which one it is rather
        /// than reading a negative number as "lots of time left".</summary>
        public static long SecondsLeft(in Definition d, long nowUnix)
        {
            if (nowUnix < d.StartUnix) return 0L;
            long s = d.EndUnix - nowUnix;
            return s > 0L ? s : 0L;
        }

        /// <summary>Whether the player is far enough in to be shown the event at all.</summary>
        public static bool Eligible(in Definition d, int islandsOwned) => islandsOwned >= d.MinIslands;

        /// <summary>Whether progress may accrue right now: the window is open AND the player qualifies.
        /// The single question every module asks before recording anything.</summary>
        public static bool Accruing(in Definition d, long nowUnix, int islandsOwned)
            => PhaseAt(d, nowUnix) == Phase.Active && Eligible(d, islandsOwned);

        /// <summary>
        /// Whether progress saved under <paramref name="savedVersion"/> still counts for a definition.
        /// It does when the versions match, and it does not when the config has moved on.
        ///
        /// The direction matters and it is the cautious one. Carrying old progress into re-tuned
        /// content silently pays out against targets that no longer exist — a milestone at 100 that
        /// became a milestone at 10 hands over every tier at once. Dropping it costs a player who was
        /// mid-event their counters, which is why the version is bumped for CONTENT changes and not for
        /// a fixed typo. What is never dropped is a slot already CLAIMED: see the note on
        /// <c>LiveEventState.claimed</c> in the save.
        /// </summary>
        public static bool ProgressSurvives(in Definition d, int savedVersion) => savedVersion == d.ConfigVersion;
    }
}
