using System;
using System.Globalization;

namespace Game.Core
{
    /// <summary>
    /// Seasons, ranking and tie-breaks for a competitive ladder — the third and last of the three
    /// gaps Docs/FIVE_LAYERS.md §1 named ("Chapters, events, leaderboards"). Chapters landed, events
    /// landed with <see cref="LiveEvents"/>, and this is the arithmetic the ladder needs.
    ///
    /// WHAT THIS FILE IS NOT. It is not a leaderboard. A leaderboard is a ranking of OTHER PEOPLE, and
    /// this game has no server that knows any. Everything here is the part that can be settled with
    /// arithmetic and pinned by a test: which season a moment falls in, how a list of standings orders
    /// itself, which bracket a rank pays out in, and whether a submission may be replayed. Who is in
    /// the list, and whether their numbers are true, is a backend question — see Docs/LEADERBOARDS.md,
    /// which must be answered before anything built on top of this is shown to players.
    ///
    /// THE CLOCK IS AN ARGUMENT, exactly as in <see cref="LiveEvents"/> and <see cref="Crafting"/>.
    /// Nothing here reads the system time; the service supplies it. It is what lets a test assert the
    /// second a season rolls over instead of waiting a week for one.
    ///
    /// SEASONS ARE HALF-OPEN, for the reason live-event windows are: season N runs from its start
    /// INCLUSIVE to its end EXCLUSIVE, so the second that closes one season is the first second of the
    /// next and no score can be submitted into two of them.
    ///
    /// SCORES ARE ABSOLUTE, NEVER DELTAS. A submission carries the season's best-so-far, and the
    /// record keeps the larger of what it holds and what it is sent (<see cref="MergeScore"/>). That
    /// one decision is what makes the whole submission path idempotent: a retry after a lost
    /// acknowledgement, a duplicate delivery, a resume from a stale save — all of them re-send a
    /// number that is already there, and the max of a number with itself is that number. A ladder
    /// built on "+37 points" instead would need a de-duplication journal for every packet, and would
    /// pay twice the first time one slipped through.
    /// </summary>
    public static class Leaderboards
    {
        // --------------------------------------------------------------------------- seasons
        /// <summary>
        /// The anchor every season index is measured from: Monday 2026-01-05 00:00 UTC.
        ///
        /// A Monday, and UTC, on purpose. The window has to be the same second everywhere on earth or
        /// two players in different time zones see different countdowns for the same season, and no
        /// screenshot of one can be checked against another. It is the same rule
        /// <see cref="Game.Data.LiveEventConfig"/> keeps for event windows.
        /// </summary>
        public const long SeasonEpochUnix = 1767571200L;

        /// <summary>A week. The only cadence the design calls for today; it is a parameter everywhere
        /// below, so a daily or fortnightly ladder needs no new arithmetic.</summary>
        public const long WeeklyCadenceSeconds = 604800L;

        /// <summary>
        /// How many entrants share one board. Fixed, and fixed at a number that fits a phone screen in
        /// a few flicks: a cohort large enough to be a real contest and small enough that a mid-table
        /// player can see both ends of it. It is a CONSTANT rather than a config because a cohort that
        /// can be resized is a cohort that can be resized MID-SEASON, and moving somebody from a board
        /// of 30 into a board of 50 halfway through invalidates every rank they had earned.
        /// </summary>
        public const int CohortSize = 30;

        /// <summary>The most bands the matcher will split players across. See <see cref="BandOf"/>.</summary>
        public const int BandCount = 4;

        /// <summary>Whether a cadence can be used at all. Checked rather than trusted: a zero cadence
        /// is a division by zero, and a negative one runs seasons backwards.</summary>
        public static bool IsWellFormedCadence(long epochUnix, long cadenceSeconds)
            => epochUnix > 0L && cadenceSeconds > 0L;

        /// <summary>
        /// Which season <paramref name="nowUnix"/> falls in, 0-based. Clamped to 0 before the epoch —
        /// a device whose clock says 1974 gets season zero rather than a negative index, which would
        /// build a season id no record has ever heard of.
        /// </summary>
        public static long SeasonIndex(long epochUnix, long cadenceSeconds, long nowUnix)
        {
            if (!IsWellFormedCadence(epochUnix, cadenceSeconds)) return 0L;
            if (nowUnix <= epochUnix) return 0L;
            return (nowUnix - epochUnix) / cadenceSeconds;
        }

        /// <summary>First second of a season, inclusive.</summary>
        public static long SeasonStartUnix(long epochUnix, long cadenceSeconds, long index)
        {
            if (!IsWellFormedCadence(epochUnix, cadenceSeconds) || index < 0L) return epochUnix;
            return epochUnix + index * cadenceSeconds;
        }

        /// <summary>First second AFTER a season — the same second the next season starts on.</summary>
        public static long SeasonEndUnix(long epochUnix, long cadenceSeconds, long index)
            => SeasonStartUnix(epochUnix, cadenceSeconds, index) + (cadenceSeconds > 0L ? cadenceSeconds : 0L);

        /// <summary>Seconds until the running season closes. Never negative, and never zero while a
        /// season is open — the countdown a board header shows.</summary>
        public static long SecondsLeftInSeason(long epochUnix, long cadenceSeconds, long nowUnix)
        {
            if (!IsWellFormedCadence(epochUnix, cadenceSeconds)) return 0L;
            long index = SeasonIndex(epochUnix, cadenceSeconds, nowUnix);
            long left = SeasonEndUnix(epochUnix, cadenceSeconds, index) - nowUnix;
            return left > 0L ? left : 0L;
        }

        /// <summary>
        /// The season's immutable identity: prefix, a hyphen, the index. It keys the outbox entry, the
        /// settlement lookup and the reward-inbox row, so it must be built the same way on every
        /// device and in every locale.
        ///
        /// INVARIANT CULTURE, and this is not decoration. The team develops on Turkish Windows, where
        /// a careless number format has already produced one shipped bug (see ForemanRosterUI). An id
        /// that reads "lig-1.234" on one machine and "lig-1,234" on another is two different rows in
        /// the same table, and the player who crosses that boundary loses a season's reward.
        /// </summary>
        public static string SeasonId(string prefix, long index)
        {
            if (string.IsNullOrEmpty(prefix)) prefix = "lig";
            if (index < 0L) index = 0L;
            return prefix + "-" + index.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The inverse, for settling a season the client has an id for but no longer has a
        /// clock reason to compute. It refuses anything it did not build itself rather than
        /// guessing.</summary>
        public static bool TryParseSeasonIndex(string prefix, string seasonId, out long index)
        {
            index = 0L;
            if (string.IsNullOrEmpty(prefix) || string.IsNullOrEmpty(seasonId)) return false;

            int cut = prefix.Length;
            if (seasonId.Length <= cut + 1) return false;
            if (string.CompareOrdinal(seasonId, 0, prefix, 0, cut) != 0) return false;
            if (seasonId[cut] != '-') return false;

            return long.TryParse(seasonId.Substring(cut + 1), NumberStyles.None,
                                 CultureInfo.InvariantCulture, out index) && index >= 0L;
        }

        // ------------------------------------------------------------------------ standings
        /// <summary>
        /// One entrant's position, as the ranker sees it. Deliberately not a display row: no name, no
        /// avatar, no level. Ordering must not depend on anything a client could vary.
        /// </summary>
        public struct Standing
        {
            /// <summary>Opaque, stable, and NOT a display name. The last tie-break reads it, so it has
            /// to be comparable; nothing else here may. A backend supplies an account-scoped id that
            /// carries no personal data — see Docs/LEADERBOARDS.md §7.</summary>
            public string EntrantId;

            /// <summary>The season's best, never a delta. See the class note.</summary>
            public long Score;

            /// <summary>The unix second that score was FIRST reached. The first tie-break, and the
            /// reason a submission carries a timestamp at all.</summary>
            public long AchievedUnix;
        }

        /// <summary>
        /// The total order two entrants are ranked in. Negative when <paramref name="a"/> places ahead.
        ///
        /// Score descending, then EARLIEST achievement, then entrant id. Three levels because the
        /// order must be TOTAL: any two distinct entrants have a definite winner, on every client, in
        /// every rebuild of the list. A comparator that stops at the score leaves ties to the sort's
        /// internal order, which means the same board renders in two orders on two devices and the
        /// screenshot arguments start.
        ///
        /// Earliest-first, rather than splitting the reward between tied players: whoever reached the
        /// number first held it longest, it is the answer a server can settle from its own write log,
        /// and it needs no special payout arithmetic for the four-way tie at zero points that every
        /// season's tail contains.
        /// </summary>
        public static int Compare(in Standing a, in Standing b)
        {
            if (a.Score != b.Score) return a.Score > b.Score ? -1 : 1;
            if (a.AchievedUnix != b.AchievedUnix) return a.AchievedUnix < b.AchievedUnix ? -1 : 1;
            return string.CompareOrdinal(a.EntrantId, b.EntrantId);
        }

        /// <summary>
        /// Orders the first <paramref name="count"/> standings in place. Insertion sort, and not as an
        /// apology: the cohort is thirty entries, it is already nearly sorted every time it is rebuilt
        /// from the previous board, and it allocates nothing — which the comparator-taking overloads
        /// of Array.Sort cannot say.
        /// </summary>
        public static void Rank(Standing[] standings, int count)
        {
            if (standings == null) return;
            if (count > standings.Length) count = standings.Length;

            for (int i = 1; i < count; i++)
            {
                Standing key = standings[i];
                int j = i - 1;
                while (j >= 0 && Compare(key, standings[j]) < 0)
                {
                    standings[j + 1] = standings[j];
                    j--;
                }
                standings[j + 1] = key;
            }
        }

        /// <summary>An entrant's 1-based rank in an already-ranked list; 0 when they are not in it.
        /// One-based because it is the number the player is shown, and an off-by-one between the
        /// array index and the medal is the classic bug here.</summary>
        public static int RankOf(Standing[] ranked, int count, string entrantId)
        {
            if (ranked == null || string.IsNullOrEmpty(entrantId)) return 0;
            if (count > ranked.Length) count = ranked.Length;

            for (int i = 0; i < count; i++)
                if (string.Equals(ranked[i].EntrantId, entrantId, StringComparison.Ordinal)) return i + 1;
            return 0;
        }

        // -------------------------------------------------------------------------- rewards
        /// <summary>
        /// The default payout brackets, as the LAST RANK each one covers: 1st, 2nd, 3rd, 4-10, 11-20,
        /// 21-30. Six tiers over a cohort of thirty, which is the shape the reference screens showed —
        /// a podium worth chasing and a tail that still pays, so a player who finishes 27th has been
        /// given a reason to come back rather than a reason to stop.
        /// </summary>
        public static readonly int[] DefaultBracketEnds = { 1, 2, 3, 10, 20, CohortSize };

        /// <summary>
        /// Which bracket a rank falls in, or -1 for a rank outside every bracket (including rank 0,
        /// which means "not on the board"). The bracket index is what a reward table is keyed by; this
        /// file deliberately knows nothing about what any bracket PAYS, the same split
        /// <see cref="LiveEvents"/> keeps from the modules above it.
        /// </summary>
        public static int RewardTier(int rank, int[] bracketEnds)
        {
            if (rank <= 0 || bracketEnds == null) return -1;
            for (int i = 0; i < bracketEnds.Length; i++)
                if (rank <= bracketEnds[i]) return i;
            return -1;
        }

        // --------------------------------------------------------------------------- cohort
        /// <summary>
        /// Which progression band an entrant is matched in. Two islands per band, capped — the same
        /// eligibility axis <see cref="LiveEvents.Eligible"/> gates on, for the same reason: output
        /// inflates roughly x3.2 per ore tier, so a raw score puts a diamond island and a coal one on
        /// the same board and the coal player never sees a rank above 30th.
        ///
        /// Banding is a MATCHING input, not a score adjustment. Normalising scores across islands
        /// instead would mean the number on the board is not the number the player earned.
        /// </summary>
        public static int BandOf(int islandsOwned)
        {
            if (islandsOwned < 1) islandsOwned = 1;
            int band = (islandsOwned - 1) / 2;
            return band >= BandCount ? BandCount - 1 : band;
        }

        /// <summary>
        /// A deterministic seed for a (season, band) pair. FNV-1a, chosen because it is four lines, has
        /// no state, and gives the same answer on every platform and every run — which is the whole
        /// requirement. It is NOT a security primitive and must never be used as one; a backend that
        /// needs an unguessable cohort assignment makes it server-side.
        /// </summary>
        public static int CohortSeed(string seasonId, int band)
        {
            unchecked
            {
                const uint Offset = 2166136261u;
                const uint Prime = 16777619u;

                uint hash = Offset;
                if (!string.IsNullOrEmpty(seasonId))
                    for (int i = 0; i < seasonId.Length; i++)
                    {
                        hash ^= seasonId[i];
                        hash *= Prime;
                    }

                hash ^= (uint)band;
                hash *= Prime;
                return (int)(hash & 0x7FFFFFFFu);
            }
        }

        // ----------------------------------------------------------------------- submission
        /// <summary>
        /// One score submission, as it sits in the outbox waiting for an acknowledgement. It is a
        /// value, not a command: re-sending it can only ever be harmless, which is what
        /// <see cref="MergeScore"/> guarantees and what <see cref="Supersedes"/> keeps true over time.
        /// </summary>
        public struct Submission
        {
            /// <summary>The season this score belongs to. A submission is NEVER re-targeted at a newer
            /// season when one rolls over — it is stale, and it is refused.</summary>
            public string SeasonId;

            /// <summary>The season's best so far. Absolute; see the class note.</summary>
            public long Score;

            /// <summary>When that best was first reached. Feeds the tie-break.</summary>
            public long AchievedUnix;

            /// <summary>Monotonic per-client counter, for the write log and for telling two otherwise
            /// identical retries apart in a trace. It is NOT what makes the write idempotent — the
            /// max-merge is.</summary>
            public long Sequence;
        }

        /// <summary>Whether a submission may be sent at all. A malformed one is dropped at the client
        /// rather than becoming a server error nobody reads.</summary>
        public static bool IsWellFormed(in Submission s)
            => !string.IsNullOrEmpty(s.SeasonId) && s.Score >= 0L && s.AchievedUnix > 0L && s.Sequence > 0L;

        /// <summary>
        /// What the authoritative score becomes when <paramref name="incoming"/> arrives at a record
        /// holding <paramref name="best"/>: the larger. Negative inputs are floored at zero, so a
        /// corrupt packet cannot delete a real score.
        ///
        /// This one line is the idempotency guarantee. Every retry path leans on it.
        /// </summary>
        public static long MergeScore(long best, long incoming)
        {
            if (best < 0L) best = 0L;
            if (incoming < 0L) incoming = 0L;
            return incoming > best ? incoming : best;
        }

        /// <summary>
        /// Whether <paramref name="incoming"/> replaces <paramref name="pending"/> in the outbox
        /// instead of queueing behind it.
        ///
        /// It replaces it whenever it is for the same season and is not worse, and that is why the
        /// outbox is ONE slot per season rather than a queue. A player who earns points for two hours
        /// on a plane comes back online owing one number, not four hundred; and a queue that can grow
        /// while offline is a queue that can outlive the season it belongs to.
        /// </summary>
        public static bool Supersedes(in Submission pending, in Submission incoming)
            => string.Equals(pending.SeasonId, incoming.SeasonId, StringComparison.Ordinal)
               && incoming.Score >= pending.Score;

        /// <summary>
        /// What the client believes after an acknowledgement: the larger of what it sent and what the
        /// record says it holds. Adopting the remote number blindly would drop a score earned while
        /// the acknowledgement was in flight; ignoring it would hide a score submitted from a second
        /// device. The max is the only answer that loses neither.
        /// </summary>
        public static long AdoptAck(long localBest, long remoteBest) => MergeScore(localBest, remoteBest);
    }
}
