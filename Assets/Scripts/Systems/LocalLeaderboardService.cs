using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// The offline double for <see cref="ILeaderboardService"/>: a whole ladder — seasons, a cohort,
    /// submissions, an outbox, settlements — with no server, no network code and no package.
    ///
    /// WHAT IT IS FOR. Two things, and neither of them is shipping a ladder to players. It lets the
    /// submission and settlement contracts be exercised in EditMode tests, at the exact second a
    /// season rolls over and with the network switched off, which is where every bug in this area
    /// actually lives. And it gives whoever builds the screen a service that answers, so the UI work
    /// does not wait on the backend decision in Docs/LEADERBOARDS.md.
    ///
    /// ITS OPPONENTS ARE NOT PEOPLE, and it says so: <see cref="Synthetic"/> is true and every board
    /// it returns carries the flag. A generated cohort presented to a player as a real one is a
    /// deception, and attaching a paid entry fee or a reward to it makes it a consumer-protection
    /// problem rather than a design opinion. Decision D4 in the document has to be answered before any
    /// screen shows this to anybody.
    ///
    /// THE COHORT DOES NOT CHASE THE PLAYER. Opponent scores are drawn from the season id, the
    /// player's progression band and the clock, and from nothing else — never from the player's own
    /// score. A ladder that quietly rescales itself so the player always sits eighth is a slot machine
    /// with a rank painted on it, and it would make every test of the ranking meaningless besides.
    ///
    /// THE OPPONENTS ARE THE SAME 49 EVERY SEASON (<see cref="LeagueRivals"/>), each with a handle and
    /// an avatar. The season seed decides only which of them draws which target.
    ///
    /// THEY PLAY THROUGH THE SEASON. A rival's score is their season target scaled by how much of the
    /// season has gone, along a pace curve of their own — some start fast, some finish late — and it
    /// moves in <see cref="ProgressSteps"/> steps, so the board changes about hourly on a three-day
    /// season instead of every second. A closed season is always at the full target, so a settlement
    /// ranks against exactly what it did before the curve existed. Every input is the season, the band
    /// and the clock, so reloading a save rebuilds the same board.
    ///
    /// IT PERSISTS NOTHING. No SaveData field, no migration, not one line in SaveMigration. That is
    /// deliberate: a save schema for an unapproved feature is a schema that has to be supported
    /// forever, and the real one depends on which backend is chosen. The document specifies the fields
    /// this will need; they land with the backend, not before. Restarting the app resets this double,
    /// which is the honest behaviour for something that is not a record of anything.
    /// </summary>
    public sealed class LocalLeaderboardService : ILeaderboardService
    {
        /// <summary>The player's own entrant id. Lower-case and fixed, so the ordinal tie-break is
        /// stable against the generated handles rather than depending on a display name.</summary>
        public const string PlayerEntrantId = "oyuncu";

        private const string SeasonPrefix = "lig";

        /// <summary>What one board's worth of standings is built into. Allocated once and reused for
        /// every request, so a screen refreshing on a countdown does not hand the GC fifty structs a
        /// second.</summary>
        private readonly Leaderboards.Standing[] _standings = new Leaderboards.Standing[Leaderboards.CohortSize];

        /// <summary>Per-season best the double has accepted, keyed by season id. In memory only; see
        /// the class note.</summary>
        private readonly Dictionary<string, long> _accepted = new Dictionary<string, long>();

        /// <summary>Per-season second at which that best was first reached — the tie-break input.</summary>
        private readonly Dictionary<string, long> _achieved = new Dictionary<string, long>();

        private readonly TimeService _time;
        private readonly long _epochUnix;
        private readonly long _cadenceSeconds;

        /// <summary>The outbox, and it is ONE slot rather than a queue — see
        /// <see cref="Leaderboards.Supersedes"/> for why a queue here is a bug waiting for a long
        /// flight.</summary>
        private Leaderboards.Submission _pending;
        private bool _hasPending;
        private long _sequence;

        public event Action Changed;

        public LocalLeaderboardService(TimeService time = null,
                                       long epochUnix = Leaderboards.SeasonEpochUnix,
                                       long cadenceSeconds = Leaderboards.WeeklyCadenceSeconds)
        {
            _time = time;
            _epochUnix = epochUnix;
            _cadenceSeconds = cadenceSeconds;
        }

        /// <summary>
        /// The player's profile as the board prints it. Set by whatever wires this up
        /// (<c>LadderService</c>, from the save), for the same reason <see cref="IslandsOwned"/> is:
        /// this double persists nothing and must not reach into the save. Empty means no profile yet,
        /// and the screen prints its own "YOU".
        /// </summary>
        public string PlayerName { get; set; } = string.Empty;

        public int PlayerAvatar { get; set; }

        /// <summary>How many steps a rival's score climbs in over one season: hourly on three days.</summary>
        public const int ProgressSteps = 72;

        /// <summary>
        /// A fixed "now" for this double, or 0 for the real clock. The same test-only seam as
        /// <see cref="TimeOffsetSeconds"/>: rival scores now move with the clock, and a test that
        /// compares two boards must not straddle a step boundary between them.
        /// </summary>
        public long ClockOverrideUnix { get; set; }

        /// <summary>
        /// Whether the imaginary record can be reached. A field a test flips, and the only way to
        /// exercise the outbox: every interesting failure in a submission path happens on the side of
        /// the network that is down, and waiting for a real one to fail is not a test.
        /// </summary>
        public bool Reachable { get; set; } = true;

        /// <summary>
        /// Islands owned, which picks the matching band. Set by whatever wires this up; it is not read
        /// from SaveData here, because this class persists nothing and a double that reaches into the
        /// save is a double that can corrupt one.
        /// </summary>
        public int IslandsOwned { get; set; } = 1;

        /// <summary>The strongest generated entrant's target in a points season, as a share of
        /// <see cref="Ladder.MaxSeasonPoints"/>.</summary>
        public const double PointsTopShare = 0.95d;

        /// <summary>Seasons scored in <see cref="Ladder.Scoring"/> points rather than bars. The owner
        /// marks them (LadderService knows which unit each season opened in); anything unmarked is a
        /// pre-points season and gets the bar-scaled cohort.</summary>
        private readonly HashSet<string> _pointsSeasons = new HashSet<string>(StringComparer.Ordinal);

        public void MarkPointsSeason(string seasonId)
        {
            if (!string.IsNullOrEmpty(seasonId)) _pointsSeasons.Add(seasonId);
        }

        /// <summary>
        /// Seconds added to this double's clock. The seam that lets a test roll a season over instead
        /// of waiting a week for one: <see cref="TimeService"/> is sealed and reads the device clock,
        /// so there is nothing to fake underneath it. It exists on the DOUBLE and on nothing else — no
        /// shipping service may have an offset like this, because a settable clock in a service that
        /// grants rewards is a cheat with a property name.
        /// </summary>
        public long TimeOffsetSeconds { get; set; }

        public bool Available => true;
        public bool Synthetic => true;

        private long NowUnix()
        {
            long now = ClockOverrideUnix > 0L ? ClockOverrideUnix
                     : _time != null ? _time.NowUnix() : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return now + TimeOffsetSeconds;
        }

        private long CurrentIndex() => Leaderboards.SeasonIndex(_epochUnix, _cadenceSeconds, NowUnix());

        public string CurrentSeasonId => Leaderboards.SeasonId(SeasonPrefix, CurrentIndex());

        public long SecondsLeftInSeason
            => Leaderboards.SecondsLeftInSeason(_epochUnix, _cadenceSeconds, NowUnix());

        /// <summary>The best this double has accepted for a season; 0 for one it has never seen.</summary>
        public long AcceptedScore(string seasonId)
            => seasonId != null && _accepted.TryGetValue(seasonId, out long best) ? best : 0L;

        /// <summary>
        /// Hands a season's score back to the double after an app restart — the one thing it cannot
        /// work out for itself, because it persists nothing (see the class note).
        ///
        /// IT IS NOT A BACK DOOR AROUND THE SEASON CHECK, and the distinction matters. A closed season
        /// is exactly what this is for: the owner (<c>LadderService</c>) keeps the score in the save,
        /// and on the launch that discovers the season has ended it replays the number so the
        /// settlement ranks the player on what they actually earned rather than on the zero a fresh
        /// dictionary would report. <see cref="SubmitScore"/> cannot do that job — it refuses a closed
        /// season by design, which is right for a score being EARNED and wrong for one being RESTORED.
        ///
        /// It merges rather than overwrites, so replaying a stale snapshot over a live one cannot
        /// lower a score, and replaying the same one twice is the no-op that <see cref="MergeScore"/>
        /// makes it everywhere else.
        /// </summary>
        public void Restore(string seasonId, long score, long achievedUnix)
        {
            if (string.IsNullOrEmpty(seasonId) || score <= 0L || achievedUnix <= 0L) return;

            long best = AcceptedScore(seasonId);
            long merged = Leaderboards.MergeScore(best, score);
            bool moved = merged > best || !_accepted.ContainsKey(seasonId);

            _accepted[seasonId] = merged;
            // The achievement stamp only moves when the score does — the same rule Commit keeps, and
            // for the same reason: a restore must not push the player down the tie-break.
            if (moved) _achieved[seasonId] = achievedUnix;
        }

        /// <summary>Whether a submission is waiting for a flush. Read by tests, and by any UI that
        /// wants to show "gönderiliyor" rather than a stale number.</summary>
        public bool HasPending => _hasPending;

        // ----------------------------------------------------------------------- submission
        public void SubmitScore(long score, Action<LeaderboardSubmitResult> onDone)
        {
            string season = CurrentSeasonId;
            long now = NowUnix();

            long best = AcceptedScore(season);
            long merged = Leaderboards.MergeScore(best, score);

            // The achievement time only moves when the score does. A resubmission of the same number
            // must not push the player back down the tie-break for having tapped refresh.
            long achievedAt = merged > best || !_achieved.ContainsKey(season) ? now : _achieved[season];

            var submission = new Leaderboards.Submission
            {
                SeasonId = season,
                Score = merged,
                AchievedUnix = achievedAt,
                Sequence = ++_sequence,
            };

            if (!Leaderboards.IsWellFormed(submission))
            {
                onDone?.Invoke(new LeaderboardSubmitResult
                {
                    Status = LeaderboardStatus.Rejected,
                    SeasonId = season,
                    AcceptedScore = best,
                });
                return;
            }

            if (!Reachable)
            {
                // Collapse into the outbox rather than queueing. Supersedes is asked rather than
                // assumed so the rule lives in one place: a pending submission from a season that has
                // since rolled over is replaced outright, not merged into.
                if (!_hasPending || Leaderboards.Supersedes(_pending, submission) ||
                    !string.Equals(_pending.SeasonId, submission.SeasonId, StringComparison.Ordinal))
                {
                    _pending = submission;
                    _hasPending = true;
                }

                // The outbox holds the best of everything submitted while offline, so IT is what the
                // player's own row should read — not this call's number, which may be the lower of two.
                onDone?.Invoke(new LeaderboardSubmitResult
                {
                    Status = LeaderboardStatus.Offline,
                    SeasonId = season,
                    AcceptedScore = _pending.Score,
                    Pending = true,
                });
                return;
            }

            LeaderboardStatus status = Commit(submission, out long acceptedNow);
            onDone?.Invoke(new LeaderboardSubmitResult
            {
                Status = status,
                SeasonId = submission.SeasonId,
                AcceptedScore = acceptedNow,
            });
        }

        /// <summary>
        /// Writes a submission into the record. The one place a score is accepted, so the season check
        /// and the merge cannot be skipped by a caller in a hurry.
        /// </summary>
        private LeaderboardStatus Commit(in Leaderboards.Submission submission, out long accepted)
        {
            accepted = AcceptedScore(submission.SeasonId);

            if (!string.Equals(submission.SeasonId, CurrentSeasonId, StringComparison.Ordinal))
                return LeaderboardStatus.SeasonClosed;

            long merged = Leaderboards.MergeScore(accepted, submission.Score);
            bool moved = merged > accepted || !_accepted.ContainsKey(submission.SeasonId);

            _accepted[submission.SeasonId] = merged;
            if (moved) _achieved[submission.SeasonId] = submission.AchievedUnix;

            accepted = merged;
            if (moved) Changed?.Invoke();
            return LeaderboardStatus.Ok;
        }

        public void Flush()
        {
            if (!_hasPending || !Reachable) return;

            // A submission whose season has closed is DROPPED, never re-aimed at the current one. The
            // score was earned inside a window that has ended; carrying it forward would hand a player
            // who was offline over a Sunday night a head start in a season they had not played.
            LeaderboardStatus status = Commit(_pending, out long _);

            _hasPending = false;
            _pending = default;

            if (status == LeaderboardStatus.SeasonClosed) Changed?.Invoke();
        }

        // ---------------------------------------------------------------------------- board
        public void RequestBoard(Action<LeaderboardBoard> onDone)
        {
            if (onDone == null) return;

            string season = CurrentSeasonId;

            if (!Reachable)
            {
                onDone.Invoke(new LeaderboardBoard
                {
                    Status = LeaderboardStatus.Offline,
                    SeasonId = season,
                    SecondsLeft = SecondsLeftInSeason,
                    PlayerScore = AcceptedScore(season),
                    Synthetic = true,
                });
                return;
            }

            long index = CurrentIndex();
            BuildStandings(season, index);

            var entries = new LeaderboardEntry[Leaderboards.CohortSize];
            for (int i = 0; i < Leaderboards.CohortSize; i++)
            {
                bool isPlayer = string.Equals(_standings[i].EntrantId, PlayerEntrantId, StringComparison.Ordinal);
                int rival = isPlayer ? -1 : LeagueRivals.IndexOf(_standings[i].EntrantId);
                entries[i] = new LeaderboardEntry
                {
                    Rank = i + 1,
                    Name = isPlayer ? PlayerName ?? string.Empty : LeagueRivals.HandleOf(rival),
                    Avatar = isPlayer ? PlayerProfiles.ClampAvatar(PlayerAvatar) : LeagueRivals.AvatarOf(rival),
                    Score = _standings[i].Score,
                    IsPlayer = isPlayer,
                };
            }

            onDone.Invoke(new LeaderboardBoard
            {
                Status = LeaderboardStatus.Ok,
                SeasonId = season,
                SecondsLeft = SecondsLeftInSeason,
                Entries = entries,
                PlayerRank = Leaderboards.RankOf(_standings, Leaderboards.CohortSize, PlayerEntrantId),
                PlayerScore = AcceptedScore(season),
                Synthetic = true,
            });
        }

        // ----------------------------------------------------------------------- settlement
        public void RequestSettlement(string seasonId, Action<LeaderboardSettlement> onDone)
        {
            if (onDone == null) return;

            var result = new LeaderboardSettlement { SeasonId = seasonId, RewardTier = -1, Synthetic = true };

            if (!Leaderboards.TryParseSeasonIndex(SeasonPrefix, seasonId, out long index))
            {
                result.Status = LeaderboardStatus.Rejected;
                onDone.Invoke(result);
                return;
            }

            // A season that has not finished has nothing to settle. Refused rather than answered with
            // a provisional rank, because a provisional rank is the thing a player screenshots and
            // then argues about when the real one lands.
            if (index >= CurrentIndex())
            {
                result.Status = LeaderboardStatus.Rejected;
                onDone.Invoke(result);
                return;
            }

            if (!Reachable)
            {
                result.Status = LeaderboardStatus.Offline;
                onDone.Invoke(result);
                return;
            }

            BuildStandings(seasonId, index);

            int rank = Leaderboards.RankOf(_standings, Leaderboards.CohortSize, PlayerEntrantId);
            result.Status = LeaderboardStatus.Ok;
            result.PlayerRank = rank;
            result.RewardTier = Leaderboards.RewardTier(rank, Leaderboards.DefaultBracketEnds);
            onDone.Invoke(result);
        }

        // --------------------------------------------------------------------------- cohort
        /// <summary>
        /// Fills and ranks <see cref="_standings"/> for one season: the player, plus generated
        /// opponents drawn from the season id and the band. Same season, same band, same board — every
        /// time, on every device, which is what makes a test of a rank meaningful at all.
        /// </summary>
        private void BuildStandings(string seasonId, long index)
        {
            int band = Leaderboards.BandOf(IslandsOwned);
            int seed = Leaderboards.CohortSeed(seasonId, band);
            uint state = (uint)seed;
            if (state == 0u) state = 1u;

            long start = Leaderboards.SeasonStartUnix(_epochUnix, _cadenceSeconds, index);
            long now = NowUnix();
            long span = now - start;
            if (span < 1L) span = 1L;
            if (span > _cadenceSeconds) span = _cadenceSeconds;

            double top;
            if (_pointsSeasons.Contains(seasonId))
            {
                // A points season: counts do not inflate by ore tier, so every band chases the same
                // target. Just under the season's ceiling, so a player who maxes every rule takes first.
                top = Ladder.MaxSeasonPoints * PointsTopShare;
            }
            else
            {
                // A pre-points season, ranked on bars. The x6 per band is the shape of the game's own
                // output curve — roughly x3.2 per ore tier, two tiers to a band — so a coal player and
                // a diamond player are each measured against a target their island can actually reach.
                long scale = 1000L;
                for (int i = 0; i < band; i++) scale *= 6L;
                top = scale * 3d;
            }

            // How far through the season the rivals are, in whole steps and never zero, so the first
            // hour already has a board to read. A closed season is always the whole way through.
            long elapsed = now - start;
            double progress = 1d;
            if (elapsed < _cadenceSeconds)
            {
                long step = (elapsed > 0L ? elapsed : 0L) * ProgressSteps / _cadenceSeconds + 1L;
                if (step < ProgressSteps) progress = step / (double)ProgressSteps;
            }

            // A SECOND STREAM for the pace curves. Drawing them from the stream above would move every
            // later rival's target, and the targets are what the reward budget was measured against.
            uint pace = (uint)seed ^ 0x9E3779B9u;
            if (pace == 0u) pace = 1u;

            for (int i = 0; i < LeagueRivals.Count; i++)
            {
                double decay = 1d;
                for (int d = 0; d < i; d++) decay *= 0.93d;

                // 0.55..1.0 of the decayed target, so the ladder has a gradient rather than steps.
                double jitter = 0.55d + 0.45d * NextUnit(ref state);
                long target = (long)(top * decay * jitter);
                if (target < 0L) target = 0L;
                long achieved = start + (long)(span * NextUnit(ref state));

                // Exponent 0.7..1.3: under 1 banks points early, over 1 closes late. Every curve ends
                // at the full target when the season does.
                double curve = 0.7d + 0.6d * NextUnit(ref pace);
                long score = progress >= 1d ? target : (long)(target * Math.Pow(progress, curve));

                _standings[i] = new Leaderboards.Standing
                {
                    EntrantId = LeagueRivals.IdOf(LeagueRivals.RivalInSlot(seed, i)),
                    Score = score,
                    AchievedUnix = achieved,
                };
            }

            // A player with no points ranks BEHIND every rival on zero, not ahead of them on the
            // earliest-first tie-break — otherwise a season's first hour shows an idle player in first.
            long playerScore = AcceptedScore(seasonId);
            _standings[Leaderboards.CohortSize - 1] = new Leaderboards.Standing
            {
                EntrantId = PlayerEntrantId,
                Score = playerScore,
                AchievedUnix = playerScore <= 0L ? long.MaxValue
                             : _achieved.TryGetValue(seasonId, out long at) ? at : start,
            };

            Leaderboards.Rank(_standings, Leaderboards.CohortSize);
        }

        /// <summary>A deterministic 0..1 draw. A plain LCG: the numbers only have to be spread out and
        /// the same on every machine, and UnityEngine.Random is neither seedable per call nor
        /// available in a plain EditMode test without dragging the engine in.</summary>
        private static double NextUnit(ref uint state)
        {
            unchecked
            {
                state = state * 1664525u + 1013904223u;
                return (state >> 8) / 16777216d;
            }
        }
    }
}
