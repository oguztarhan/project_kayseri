using System;

namespace Game.Systems
{
    /// <summary>
    /// How a leaderboard call ended. Numbered and append-only, like every enum an analytics payload
    /// carries by value.
    /// </summary>
    public enum LeaderboardStatus
    {
        /// <summary>The call did what it said.</summary>
        Ok = 0,

        /// <summary>No leaderboard is configured in this build. The shipping answer until a backend is
        /// approved, and the reason every screen must be able to draw nothing at all.</summary>
        Unavailable = 1,

        /// <summary>The request could not reach the record. NOT an error the player is shown as a
        /// failure: the submission has been kept in the outbox and will go out on the next flush.</summary>
        Offline = 2,

        /// <summary>The season named by the request has closed. A submission into it is dropped; a
        /// settlement lookup for it is still valid, which is why these are separate calls.</summary>
        SeasonClosed = 3,

        /// <summary>The request was malformed, or the record refused it. Nothing was written and a
        /// retry of the same request will be refused again — the one status worth logging.</summary>
        Rejected = 4,
    }

    /// <summary>One row of a board, as a screen wants it. A display shape, not a ranking shape —
    /// <see cref="Game.Core.Leaderboards.Standing"/> is what the ordering is done on.</summary>
    public struct LeaderboardEntry
    {
        /// <summary>1-based, as shown.</summary>
        public int Rank;

        /// <summary>What to print. For a synthetic board this is a generated handle, never a name
        /// belonging to a person.</summary>
        public string Name;

        public long Score;

        /// <summary>Whether this row is the player's — the highlighted row in the reference screens.</summary>
        public bool IsPlayer;
    }

    /// <summary>
    /// A board as returned to a screen. A class rather than a struct because it carries an array and
    /// is handed to a callback: one allocation per REQUEST, never per frame, and a screen that wants a
    /// live countdown recomputes the seconds itself rather than re-requesting.
    /// </summary>
    public sealed class LeaderboardBoard
    {
        public LeaderboardStatus Status;
        public string SeasonId;
        public long SecondsLeft;

        /// <summary>Ranked, best first. Never null; empty when <see cref="Status"/> is not Ok.</summary>
        public LeaderboardEntry[] Entries = new LeaderboardEntry[0];

        /// <summary>The player's 1-based rank, or 0 when they are not on this board.</summary>
        public int PlayerRank;

        public long PlayerScore;

        /// <summary>
        /// TRUE WHEN THE OPPONENTS ARE NOT PEOPLE. It rides on the board rather than being something a
        /// screen has to remember to ask about, because a synthetic board presented as a real one is a
        /// straightforward deception of the player and, for a paid ladder, a consumer-protection
        /// problem. Any UI built on this must label it. See Docs/LEADERBOARDS.md §9, decision D4.
        /// </summary>
        public bool Synthetic;
    }

    /// <summary>The outcome of a score submission.</summary>
    public struct LeaderboardSubmitResult
    {
        public LeaderboardStatus Status;
        public string SeasonId;

        /// <summary>The best the record holds for this season after the call — which is the max of
        /// what was sent and what was already there, so a duplicate submission returns the same
        /// number rather than a larger one.</summary>
        public long AcceptedScore;

        /// <summary>Whether the submission is sitting in the outbox waiting for a flush. True with
        /// <see cref="LeaderboardStatus.Offline"/>; it is a promise, not a failure.</summary>
        public bool Pending;
    }

    /// <summary>What a closed season paid, looked up by id.</summary>
    public struct LeaderboardSettlement
    {
        public LeaderboardStatus Status;
        public string SeasonId;

        /// <summary>Final 1-based rank; 0 when the player did not take part.</summary>
        public int PlayerRank;

        /// <summary>Index into the reward-bracket table, or -1 for no payout. See
        /// <see cref="Game.Core.Leaderboards.RewardTier"/>.</summary>
        public int RewardTier;

        /// <summary>As on <see cref="LeaderboardBoard.Synthetic"/>.</summary>
        public bool Synthetic;
    }

    /// <summary>
    /// The client's whole view of a ranked ladder. One seam, so the local double, an approved backend
    /// and the "there is no ladder" build are the same shape to everything above them.
    ///
    /// CALLBACK-SHAPED, LIKE <see cref="IIAPService"/>, and for the same reason: every real
    /// implementation of this is a network round trip, and an interface whose methods return values
    /// can only ever be implemented by one that is not. Getting that wrong is a rewrite of every call
    /// site the day a backend is chosen, which is precisely the day this seam exists to survive.
    ///
    /// IT GRANTS NOTHING. Not one method here touches a wallet, a save or an inbox. A settlement is
    /// an ANSWER — "you finished 4th, that is bracket 3" — and the granting belongs to whichever
    /// service owns the reward, behind its own idempotent claim flag, exactly as
    /// <c>LiveEventService.MarkClaimed</c> gates a live-event payout. A leaderboard that pays out is a
    /// leaderboard that pays out twice the first time a response is delivered twice.
    ///
    /// NOTHING HERE IS WIRED INTO THE GAME YET. <see cref="StubLeaderboardService"/> is what a build
    /// would register today, and it answers Unavailable to everything. See Docs/LEADERBOARDS.md for
    /// what has to be decided before that changes.
    /// </summary>
    public interface ILeaderboardService
    {
        /// <summary>Whether a ladder exists at all in this build. False means every screen that would
        /// show one stays closed — not an empty board, no board.</summary>
        bool Available { get; }

        /// <summary>Whether the opponents this service reports are generated rather than people. See
        /// <see cref="LeaderboardBoard.Synthetic"/>.</summary>
        bool Synthetic { get; }

        /// <summary>The season a score submitted right now would land in. Empty when unavailable.</summary>
        string CurrentSeasonId { get; }

        /// <summary>Seconds until the running season closes; 0 when unavailable.</summary>
        long SecondsLeftInSeason { get; }

        /// <summary>Raised when the local view has moved — a flush landed, a season rolled over.</summary>
        event Action Changed;

        /// <summary>
        /// Offers a season score. Absolute, never a delta: the record keeps the larger of this and
        /// what it holds, which is what makes the call safe to repeat. The callback may fire
        /// synchronously; a caller that assumes otherwise is the bug.
        /// </summary>
        void SubmitScore(long score, Action<LeaderboardSubmitResult> onDone);

        /// <summary>Asks for the current board. Allocates a board per call, so a screen requests on
        /// open and on <see cref="Changed"/> — never in Update.</summary>
        void RequestBoard(Action<LeaderboardBoard> onDone);

        /// <summary>
        /// Asks what a CLOSED season paid. Separate from <see cref="RequestBoard"/> because it must
        /// work long after the season ended and while the current one is running: a player who was
        /// away for a fortnight still has two settlements owed to them, and an idle game never lets a
        /// reward expire for being looked at late.
        /// </summary>
        void RequestSettlement(string seasonId, Action<LeaderboardSettlement> onDone);

        /// <summary>Retries whatever is in the outbox. Called when the app comes back to the
        /// foreground, alongside the other resume work; safe to call when there is nothing to
        /// send.</summary>
        void Flush();
    }

    /// <summary>
    /// The null object, and what ships until a backend is approved: no ladder, no season, no board.
    /// It never fails and never blocks — the same contract <see cref="StubIAPService"/> keeps for a
    /// platform with no store.
    /// </summary>
    public sealed class StubLeaderboardService : ILeaderboardService
    {
        private static readonly LeaderboardEntry[] NoEntries = new LeaderboardEntry[0];

        public bool Available => false;
        public bool Synthetic => false;
        public string CurrentSeasonId => "";
        public long SecondsLeftInSeason => 0L;

        public event Action Changed { add { } remove { } }

        public void SubmitScore(long score, Action<LeaderboardSubmitResult> onDone)
            => onDone?.Invoke(new LeaderboardSubmitResult { Status = LeaderboardStatus.Unavailable });

        public void RequestBoard(Action<LeaderboardBoard> onDone)
            => onDone?.Invoke(new LeaderboardBoard
            {
                Status = LeaderboardStatus.Unavailable,
                SeasonId = "",
                Entries = NoEntries,
            });

        public void RequestSettlement(string seasonId, Action<LeaderboardSettlement> onDone)
            => onDone?.Invoke(new LeaderboardSettlement
            {
                Status = LeaderboardStatus.Unavailable,
                SeasonId = seasonId,
                RewardTier = -1,
            });

        public void Flush() { }
    }
}
