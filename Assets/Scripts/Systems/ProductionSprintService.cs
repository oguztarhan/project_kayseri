using System;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// Personal Production Sprint progress and rewards. Ranking is an optional read/write adapter;
    /// it grants nothing and remains unavailable in the shipping configuration until approved.
    /// </summary>
    public sealed class ProductionSprintService
    {
        private readonly LiveEventService _events;
        private readonly GoalService _goals;
        private readonly WalletService _wallet;
        private readonly ForemanService _foremen;
        private readonly SaveData _data;
        private readonly SaveService _save;
        private readonly TimeService _time;
        private readonly ILeaderboardService _leaderboard;
        private readonly IAnalytics _analytics;
        private readonly ProductionSprint.Tuning _tuning;

        private LiveEvents.Definition _definition;
        private bool _hasDefinition;
        private bool _syncing;
        private long _pickedUnix = long.MinValue;
        private long _latestSeenUnix = long.MinValue;

        public event Action Changed;

        public ProductionSprintService(LiveEventService events, GoalService goals, WalletService wallet,
            ProductionSprint.Tuning tuning, ForemanService foremen = null,
            SaveData data = null, SaveService save = null,
            TimeService time = null, ILeaderboardService leaderboard = null, IAnalytics analytics = null)
        {
            _events = events;
            _goals = goals;
            _wallet = wallet;
            _foremen = foremen;
            _data = data;
            _save = save;
            _time = time;
            _leaderboard = leaderboard ?? new StubLeaderboardService();
            _analytics = analytics;
            _tuning = ProductionSprint.IsWellFormed(tuning) ? tuning : ProductionSprint.Tuning.Default;
            Sync();
        }

        private long NowUnix()
        {
            long now = _time != null ? _time.NowUnix() : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (_latestSeenUnix == long.MinValue || now > _latestSeenUnix) _latestSeenUnix = now;
            return _latestSeenUnix;
        }

        private bool Fits(in LiveEvents.Definition definition)
            => definition.Kind == ProductionSprint.Kind && definition.Slots >= ProductionSprint.Slots;

        private void Pick(long now)
        {
            _hasDefinition = false;
            if (_events == null) return;

            LiveEvents.Definition owed = default, upcoming = default, latest = default;
            bool hasOwed = false, hasUpcoming = false, hasLatest = false;
            for (int i = 0; i < _events.Count; i++)
            {
                LiveEvents.Definition definition = _events.At(i);
                if (!Fits(definition) || !_events.Visible(definition.Id)) continue;
                LiveEvents.Phase phase = LiveEvents.PhaseAt(definition, now);
                if (phase == LiveEvents.Phase.Active)
                {
                    _definition = definition;
                    _hasDefinition = true;
                    return;
                }
                if (phase == LiveEvents.Phase.Upcoming)
                {
                    if (!hasUpcoming || definition.StartUnix < upcoming.StartUnix)
                    {
                        upcoming = definition;
                        hasUpcoming = true;
                    }
                    continue;
                }
                if (!hasLatest || definition.StartUnix > latest.StartUnix)
                {
                    latest = definition;
                    hasLatest = true;
                }
                if (Pending(definition) > 0 && (!hasOwed || definition.StartUnix > owed.StartUnix))
                {
                    owed = definition;
                    hasOwed = true;
                }
            }

            if (hasOwed) { _definition = owed; _hasDefinition = true; }
            else if (hasUpcoming) { _definition = upcoming; _hasDefinition = true; }
            else if (hasLatest) { _definition = latest; _hasDefinition = true; }
        }

        public void Sync()
        {
            if (_syncing) return;
            _syncing = true;
            try
            {
                long now = NowUnix();
                if (now != _pickedUnix)
                {
                    _pickedUnix = now;
                    Pick(now);
                }
                Accrue(now);
            }
            finally { _syncing = false; }
        }

        private void Accrue(long now)
        {
            if (!_hasDefinition || _goals == null || _events == null) return;
            if (LiveEvents.PhaseAt(_definition, now) != LiveEvents.Phase.Active ||
                !_events.Accruing(_definition.Id)) return;

            bool moved = false;
            for (int metric = 0; metric < Goals.MetricCount; metric++)
            {
                int cursorSlot = ProductionSprint.CursorSlot(metric);
                long stored = _events.Progress(_definition.Id, cursorSlot);
                long lifetime = _goals.Lifetime(metric);
                if (stored <= 0L)
                {
                    _events.Record(_definition.Id, cursorSlot, lifetime + 1L);
                    continue;
                }

                long delta = lifetime - (stored - 1L);
                if (delta <= 0L) continue;
                for (int ruleIndex = 0; ruleIndex < ProductionSprint.RuleCount; ruleIndex++)
                {
                    ProductionSprint.ScoringRule rule = _tuning.Rules[ruleIndex];
                    if (rule.Metric != metric) continue;
                    long progress = _events.Progress(_definition.Id, ProductionSprint.RuleSlot(ruleIndex));
                    long remaining = rule.ActionLimit - progress;
                    if (remaining <= 0L) continue;
                    long amount = delta < remaining ? delta : remaining;
                    if (_events.Record(_definition.Id, ProductionSprint.RuleSlot(ruleIndex), amount)) moved = true;
                }
                _events.Record(_definition.Id, cursorSlot, delta);
            }
            if (moved)
            {
                SubmitScoreIfAvailable();
                Changed?.Invoke();
            }
        }

        public bool Available { get { Sync(); return _hasDefinition; } }
        public string SeasonId { get { Sync(); return _hasDefinition ? _definition.Id : null; } }
        public LiveEvents.Phase Phase { get { Sync(); return _hasDefinition ? LiveEvents.PhaseAt(_definition, NowUnix()) : LiveEvents.Phase.Closed; } }
        public long SecondsLeft { get { Sync(); return _hasDefinition ? LiveEvents.SecondsLeft(_definition, NowUnix()) : 0L; } }
        public ProductionSprint.ScoringRule RuleAt(int index) => index >= 0 && index < ProductionSprint.RuleCount ? _tuning.Rules[index] : default;
        public ProductionSprint.Milestone MilestoneAt(int index) => index >= 0 && index < ProductionSprint.MilestoneCount ? _tuning.Milestones[index] : default;

        public long RuleProgress(int index)
        {
            Sync();
            if (!_hasDefinition || index < 0 || index >= ProductionSprint.RuleCount) return 0L;
            long progress = _events.Progress(_definition.Id, ProductionSprint.RuleSlot(index));
            return progress < _tuning.Rules[index].ActionLimit ? progress : _tuning.Rules[index].ActionLimit;
        }

        public long Score
        {
            get
            {
                Sync();
                return _hasDefinition ? ScoreFor(_definition) : 0L;
            }
        }

        private long ScoreFor(in LiveEvents.Definition definition)
        {
            long score = 0L;
            for (int i = 0; i < ProductionSprint.RuleCount; i++)
                score += ProductionSprint.RuleScore(_tuning.Rules[i],
                    _events.Progress(definition.Id, ProductionSprint.RuleSlot(i)));
            return score;
        }

        public bool MilestoneClaimed(int index)
        {
            Sync();
            return _hasDefinition && index >= 0 && index < ProductionSprint.MilestoneCount &&
                _events.Claimed(_definition.Id, ProductionSprint.MilestoneSlot(index));
        }

        public bool CanClaimMilestone(int index)
            => index >= 0 && index < ProductionSprint.MilestoneCount &&
               Score >= _tuning.Milestones[index].Score && !MilestoneClaimed(index);

        public bool ClaimMilestone(int index)
        {
            if (!CanClaimMilestone(index)) return false;
            if (!_events.MarkClaimed(_definition.Id, ProductionSprint.MilestoneSlot(index))) return false;
            Pay(_tuning.Milestones[index].Reward);
            Commit("sprint_milestone_claim", index);
            return true;
        }

        public int PendingCount()
        {
            Sync();
            return _hasDefinition ? Pending(_definition) : 0;
        }

        private int Pending(in LiveEvents.Definition definition)
        {
            long score = ScoreFor(definition);
            int pending = 0;
            for (int i = 0; i < ProductionSprint.MilestoneCount; i++)
                if (score >= _tuning.Milestones[i].Score &&
                    !_events.Claimed(definition.Id, ProductionSprint.MilestoneSlot(i))) pending++;
            return pending;
        }

        /// <summary>Only an approved adapter serving this exact immutable season may receive score.</summary>
        public bool RankingAvailable
        {
            get
            {
                Sync();
                return _hasDefinition && _leaderboard.Available &&
                    string.Equals(_leaderboard.CurrentSeasonId, _definition.Id, StringComparison.Ordinal);
            }
        }

        public void SubmitScoreIfAvailable()
        {
            if (!_hasDefinition || LiveEvents.PhaseAt(_definition, NowUnix()) != LiveEvents.Phase.Active) return;
            if (!_leaderboard.Available ||
                !string.Equals(_leaderboard.CurrentSeasonId, _definition.Id, StringComparison.Ordinal)) return;
            _leaderboard.SubmitScore(ScoreFor(_definition), result =>
            {
                _analytics?.Log("sprint_score_submit", "event", _definition.Id + ":" + (int)result.Status);
            });
        }

        private void Pay(in ProductionSprint.Reward reward)
        {
            if (reward.Gems > 0L) _wallet?.AddGems(reward.Gems);
            if (reward.Cards > 0) _foremen?.GrantRandomDuplicates(reward.Cards);
            if (reward.CashMinutes > 0d && _data != null && _data.incomeRatePerSec > 0d)
                _wallet?.AddCash(new BigDouble(_data.incomeRatePerSec * reward.CashMinutes * 60d));
        }

        private void Commit(string eventName, object value)
        {
            if (_save != null && _data != null) _save.Save(_data);
            _analytics?.Log(eventName, "event", _definition.Id + ":" + value);
            Changed?.Invoke();
        }
    }
}
