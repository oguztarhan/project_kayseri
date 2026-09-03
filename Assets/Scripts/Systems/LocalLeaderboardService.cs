using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// THE COHORT DOES NOT CHASE THE PLAYER. Opponent scores are drawn from the season id and the
    /// player's progression band and from nothing else — never from the player's own score. A ladder
    /// that quietly rescales itself so the player always sits eighth is a slot machine with a rank
    /// painted on it, and it would make every test of the ranking meaningless besides.
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
        /// every request, so a screen refreshing on a countdown does not hand the GC thirty structs a
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
        private readonly string _playerName;

        /// <summary>The outbox, and it is ONE slot rather than a queue — see
        /// <see cref="Leaderboards.Supersedes"/> for why a queue here is a bug waiting for a long
        /// flight.</summary>
        private Leaderboards.Submission _pending;
        private bool _hasPending;
        private long _sequence;

        public event Action Changed;

        public LocalLeaderboardService(TimeService time = null,
                                       long epochUnix = Leaderboards.SeasonEpochUnix,
                                       long cadenceSeconds = Leaderboards.WeeklyCadenceSeconds,
                                       string playerName = "Sen")
        {
            _time = time;
            _epochUnix = epochUnix;
            _cadenceSeconds = cadenceSeconds;
            _playerName = string.IsNullOrEmpty(playerName) ? "Sen" : playerName;
        }

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
            => (_time != null ? _time.NowUnix() : DateTimeOffset.UtcNow.ToUnixTimeSeconds()) + TimeOffsetSeconds;

        private long CurrentIndex() => Leaderboards.SeasonIndex(_epochUnix, _cadenceSeconds, NowUnix());

        public string CurrentSeasonId => Leaderboards.SeasonId(SeasonPrefix, CurrentIndex());

        public long SecondsLeftInSeason
            => Leaderboards.SecondsLeftInSeason(_epochUnix, _cadenceSeconds, NowUnix());

        /// <summary>The best this double has accepted for a season; 0 for one it has never seen.</summary>
        public long AcceptedScore(string seasonId)
            => seasonId != null && _accepted.TryGetValue(seasonId, out long best) ? best : 0L;

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
                entries[i] = new LeaderboardEntry
                {
                    Rank = i + 1,
                    Name = isPlayer ? _playerName : _standings[i].EntrantId,
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

            // What a strong week looks like in this band. The x6 per band is the shape of the game's
            // own output curve — roughly x3.2 per ore tier, two tiers to a band — so a coal player and
            // a diamond player are each measured against a target their island can actually reach.
            long scale = 1000L;
            for (int i = 0; i < band; i++) scale *= 6L;

            double top = scale * 3d;

            for (int i = 0; i < Leaderboards.CohortSize - 1; i++)
            {
                // i * 7919 mod 9000 is a bijection over the cohort (7919 is prime, and shares no
                // factor with 9000), so the generated handles cannot collide — two entrants with one
                // id would break RankOf, which matches on it.
                int suffix = 1000 + (int)((seed + i * 7919L) % 9000L);

                double decay = 1d;
                for (int d = 0; d < i; d++) decay *= 0.93d;

                // 0.55..1.0 of the decayed target, so the ladder has a gradient rather than steps.
                double jitter = 0.55d + 0.45d * NextUnit(ref state);
                long score = (long)(top * decay * jitter);
                if (score < 0L) score = 0L;

                _standings[i] = new Leaderboards.Standing
                {
                    EntrantId = "Denizci-" + suffix.ToString(CultureInfo.InvariantCulture),
                    Score = score,
                    AchievedUnix = start + (long)(span * NextUnit(ref state)),
                };
            }

            long playerScore = AcceptedScore(seasonId);
            _standings[Leaderboards.CohortSize - 1] = new Leaderboards.Standing
            {
                EntrantId = PlayerEntrantId,
                Score = playerScore,
                AchievedUnix = _achieved.TryGetValue(seasonId, out long at) ? at : start,
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
