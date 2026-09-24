using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// The three-day league, as the game plays it: what the player's score IS, when a season turns
    /// over, what a closed one paid, and handing that over exactly once.
    ///
    /// WHY THIS EXISTS AT ALL, when <see cref="ILeaderboardService"/> is already the ladder. That seam
    /// answers questions — "you finished 4th, that is bracket 3" — and Docs/LEADERBOARDS.md §4 is
    /// explicit that it grants nothing: a leaderboard that pays out is a leaderboard that pays out
    /// twice the first time a response is delivered twice. Somebody above it has to own the score
    /// cursor, the settlement sweep and the claim flag, and no existing service is that somebody —
    /// <c>LiveEventService</c> owns scheduled windows with authored ids, and a league that rolls over
    /// every three days forever is not a schedule anyone can author.
    ///
    /// THE SCORE IS A DELTA OFF COUNTERS THAT ALREADY EXIST. Capped <see cref="Ladder.Scoring"/> points
    /// — upgrades, contracts, repairs and foreman levels — measured from baselines snapshotted when the
    /// season opened: the same shape <c>GoalService</c>'s day and week baselines use, and the reason this
    /// needed no new hook anywhere else. Nothing reports into the league; it reads what the save already
    /// knows, which is also why an existing player's first season starts correctly instead of counting
    /// their whole career.
    ///
    /// A SEASON KEEPS THE UNIT IT OPENED IN. Seasons opened before 2026-09-17 ranked bars sold; the one
    /// running when a player updates finishes in bars and settles against a bars cohort, and every season
    /// this build opens scores points (<see cref="LadderState.points"/>). No cutover date is involved, so
    /// nothing about it depends on when the update lands.
    ///
    /// IT NEVER PAYS CASH. Docs/VOYAGES.md R1 — <c>MarketService</c> is the only faucet.
    /// </summary>
    public sealed class LadderService
    {
        /// <summary>What a pre-points season ranked on. Only a season opened by an older build still
        /// reads it; see the class note. It was replaced because bars inflate x3.2 per ore tier, so it
        /// ranked progress rather than play.</summary>
        public const int ScoreMetric = Goals.BarsSold;

        private readonly SaveData _data;
        private readonly SaveService _save;
        private readonly GoalService _goals;
        private readonly ILeaderboardService _leaderboard;
        private readonly WalletService _wallet;
        private readonly ForemanService _foremen;
        private readonly TimeService _time;
        private readonly IAnalytics _analytics;
        private readonly Ladder.Tuning _tuning;

        private bool _restored;
        private bool _syncing;

        /// <summary>The last score handed to the ladder. What <see cref="Track"/> measures against —
        /// see the note there for why the save's own bestScore cannot be used.</summary>
        private long _submitted;

        /// <summary>When the running season ends, as a unix second. Cached so the hot path below can
        /// notice a rollover with one comparison instead of building a season id — which allocates a
        /// string, and would do it on every bar the player sells.</summary>
        private long _seasonEndsUnix;

        public event Action Changed;

        public LadderService(SaveData data, SaveService save, GoalService goals,
                             ILeaderboardService leaderboard, WalletService wallet = null,
                             ForemanService foremen = null, TimeService time = null,
                             Ladder.Tuning tuning = default, IAnalytics analytics = null)
        {
            _data = data;
            _save = save;
            _goals = goals;
            _leaderboard = leaderboard ?? new StubLeaderboardService();
            _wallet = wallet;
            _foremen = foremen;
            _time = time;
            _analytics = analytics;
            _tuning = Ladder.IsWellFormed(tuning) ? tuning : Ladder.Tuning.Default;
            Sync();

            // THE LEAGUE FOLLOWS THE COUNTER IT MEASURES, rather than waiting to be looked at.
            //
            // Everything else here syncs on read, which is enough for a screen. It is not enough for a
            // score: a player who sells all season and never opens the ladder would have had nothing
            // recorded when the season closed, and would settle as if they had not played. The reward
            // would then go to whoever opened a screen, which is not what the board claims to rank.
            if (_goals != null) _goals.Changed += OnGoalsChanged;
        }

        /// <summary>Detaches from the goal tally. Nothing in the game tears services down today, but
        /// an event subscription with no way out is how a service outlives its own save.</summary>
        public void Dispose()
        {
            if (_goals != null) _goals.Changed -= OnGoalsChanged;
        }

        /// <summary>
        /// The hot path: fires on every bar sold, so it does the least it can. It keeps the season's
        /// best up to date in the save — which is all that has to be true when the app is killed,
        /// since the number lives on <see cref="SaveData"/> and rides out on the next autosave — and
        /// hands over to the full sync only when the season has actually ended.
        ///
        /// No submission, no save and no allocation on this path. The board is told on the next read,
        /// and a board nobody is looking at does not need to be current.
        /// </summary>
        private void OnGoalsChanged()
        {
            if (_data == null || _syncing || !_leaderboard.Available) return;

            if (_seasonEndsUnix > 0L && NowUnix() >= _seasonEndsUnix) { Sync(); return; }

            LadderState ladder = _data.ladder;
            if (ladder == null || string.IsNullOrEmpty(ladder.seasonId)) return;

            long score = CurrentScore();
            if (score <= ladder.bestScore) return;

            ladder.bestScore = score;
            ladder.bestAchievedUnix = NowUnix();
        }

        private long NowUnix()
            => _time != null ? _time.NowUnix() : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // ------------------------------------------------------------------------------- reads
        /// <summary>Whether a league exists in this build at all. False means the opener is not drawn
        /// — no ladder, rather than an empty one (Docs/LEADERBOARDS.md §13).</summary>
        public bool Available => _leaderboard.Available;

        /// <summary>Whether the opponents are generated rather than people. TRUE for the local league,
        /// and every screen showing it must say so — decision D4.</summary>
        public bool Synthetic => _leaderboard.Synthetic;

        public string CurrentSeasonId { get { Sync(); return _leaderboard.CurrentSeasonId; } }

        public long SecondsLeftInSeason { get { Sync(); return _leaderboard.SecondsLeftInSeason; } }

        /// <summary>The player's score in the running season: capped points earned since it opened.</summary>
        public long Score { get { Sync(); return CurrentScore(); } }

        /// <summary>Whether the running season scores <see cref="Ladder.Scoring"/> points. False only for
        /// the one season an older build opened, which still ranks bars sold.</summary>
        public bool ScoresPoints { get { Sync(); return _data != null && _data.ladder != null && _data.ladder.points; } }

        /// <summary>
        /// How many of a rule's actions count this season: taken since the season opened, capped at the
        /// rule's season limit. Zero in a bars season. What the points card shows as "x / cap".
        /// </summary>
        public long CountedActions(in Ladder.ScoringRule rule)
        {
            Sync();
            if (_data == null || _goals == null || _data.ladder == null || !_data.ladder.points) return 0L;

            long[] baselines = _data.ladder.baselines;
            long baseline = baselines != null && rule.Metric < baselines.Length ? baselines[rule.Metric] : 0L;
            long actions = _goals.Lifetime(rule.Metric) - baseline;
            if (actions <= 0L) return 0L;
            return rule.SeasonCap >= 0L && actions > rule.SeasonCap ? rule.SeasonCap : actions;
        }

        /// <summary>Whether the player has closed the points card once — after that it opens only on
        /// request.</summary>
        public bool PointsHelpSeen => _data == null || _data.ladder == null || _data.ladder.pointsHelpSeen;

        public void MarkPointsHelpSeen()
        {
            if (_data == null || _data.ladder == null || _data.ladder.pointsHelpSeen) return;
            _data.ladder.pointsHelpSeen = true;
            Commit();
        }

        /// <summary>Asks the ladder for the board. Straight through — a board is a display shape and
        /// this service has nothing to add to one.</summary>
        public void RequestBoard(Action<LeaderboardBoard> onDone)
        {
            Sync();
            _leaderboard.RequestBoard(onDone);
        }

        /// <summary>Closed seasons waiting to be collected, newest last. The list itself, because it is
        /// short and the screen draws all of it.</summary>
        public List<LadderInboxRow> Inbox
        {
            get
            {
                Sync();
                return _data != null ? _data.ladder.inbox : new List<LadderInboxRow>();
            }
        }

        /// <summary>How many inbox rows still owe the player something — the opener's badge.</summary>
        public int UnclaimedCount
        {
            get
            {
                Sync();
                if (_data == null) return 0;

                int waiting = 0;
                List<LadderInboxRow> inbox = _data.ladder.inbox;
                for (int i = 0; i < inbox.Count; i++)
                    if (!inbox[i].claimed && Ladder.Pays(inbox[i].tier, _tuning)) waiting++;
                return waiting;
            }
        }

        /// <summary>What a bracket hands over, for the reward preview beside each rank.</summary>
        public Ladder.Reward RewardFor(int tier) => Ladder.RewardFor(tier, _tuning);

        // -------------------------------------------------------------------------------- sync
        /// <summary>
        /// Brings the league up to date with the clock: restores what the save knows on the first
        /// call, settles a season that has closed since the last one, and keeps the running score
        /// submitted.
        ///
        /// Called from every public read rather than from an Update, because nothing here is
        /// per-frame work and a league that only moved while a screen was open would settle a season
        /// late. The reentrancy guard is not decoration: settling saves, and a save that woke a
        /// listener which read this service would otherwise recurse.
        /// </summary>
        public void Sync()
        {
            if (_data == null || _goals == null || _syncing) return;
            if (!_leaderboard.Available) return;

            _syncing = true;
            try
            {
                LadderState ladder = _data.ladder;
                if (ladder == null) { _data.ladder = ladder = new LadderState(); }
                RepairBaselines(ladder);

                SyncLocal(ladder);

                string current = _leaderboard.CurrentSeasonId;
                if (string.IsNullOrEmpty(current)) return;

                _seasonEndsUnix = NowUnix() + _leaderboard.SecondsLeftInSeason;

                if (!string.Equals(ladder.seasonId, current, StringComparison.Ordinal))
                    Roll(ladder, current);
                else
                    Track(ladder);
            }
            finally { _syncing = false; }
        }

        /// <summary>
        /// The two things the local double cannot work out for itself.
        ///
        /// WHICH BAND THE PLAYER IS MATCHED IN, kept current because buying an island moves it and a
        /// board built for the wrong band measures the player against a target their island cannot
        /// reach. Banding is a matching input only — it never touches the score.
        ///
        /// AND THE SCORE IT CANNOT REMEMBER ACROSS A RESTART. Without the replay, a season that closed
        /// while the app was shut settles on a zero and pays the player last place for a season they
        /// may have led — see <c>LocalLeaderboardService.Restore</c>.
        ///
        /// Both are behind one type check because both are properties of the offline double and of
        /// nothing else: a real backend bands and stores server-side, and the check simply stops
        /// firing on the day one is registered.
        /// </summary>
        private void SyncLocal(LadderState ladder)
        {
            if (!(_leaderboard is LocalLeaderboardService local)) return;

            int owned = _data.unlockedIslands != null ? _data.unlockedIslands.Count : 0;
            local.IslandsOwned = owned > 0 ? owned : 1;

            // Who the player's row says they are. Read raw rather than through PlayerProfileService so
            // the league needs no second service to be built first; the screen cleans it on the way out.
            PlayerProfileData profile = _data.profile;
            local.PlayerName = profile != null ? profile.name : string.Empty;
            local.PlayerAvatar = profile != null ? PlayerProfiles.ClampAvatar(profile.avatar) : 0;

            if (_restored) return;
            _restored = true;
            // The double forgets which seasons score points across a restart, exactly as it forgets
            // the score. A season that closed while the app was shut must still settle in its own unit.
            if (ladder.points) local.MarkPointsSeason(ladder.seasonId);
            local.Restore(ladder.seasonId, ladder.bestScore, ladder.bestAchievedUnix);
            _submitted = ladder.bestScore;
        }

        /// <summary>
        /// A save from before points has no per-metric baselines. That is only a problem for a points
        /// season, which an older build never opened — but a damaged array is repaired the same way:
        /// from the counters as they are now, so the season counts from here rather than counting a
        /// whole career.
        /// </summary>
        private void RepairBaselines(LadderState ladder)
        {
            if (ladder.baselines != null && ladder.baselines.Length == Goals.MetricCount) return;
            ladder.baselines = new long[Goals.MetricCount];
            if (!ladder.points) return;
            for (int m = 0; m < Goals.MetricCount; m++) ladder.baselines[m] = _goals.Lifetime(m);
        }

        /// <summary>The running season's score, never negative — a counter that somehow went backwards
        /// must read as zero rather than as a debt. Points for a points season, bars for an older one.
        /// No allocation: this runs on the goal tally's hot path.</summary>
        private long CurrentScore()
        {
            if (_data == null || _goals == null) return 0L;
            LadderState ladder = _data.ladder;

            if (!ladder.points)
            {
                long delta = _goals.Lifetime(ScoreMetric) - ladder.baseline;
                return delta > 0L ? delta : 0L;
            }

            long score = 0L;
            Ladder.ScoringRule[] rules = Ladder.Scoring;
            for (int i = 0; i < rules.Length; i++)
            {
                int metric = rules[i].Metric;
                long baseline = ladder.baselines != null && metric < ladder.baselines.Length ? ladder.baselines[metric] : 0L;
                score += Ladder.PointsFor(rules[i], _goals.Lifetime(metric) - baseline);
            }
            return score;
        }

        /// <summary>
        /// Keeps the season's best up to date and offers it to the ladder.
        ///
        /// IT MEASURES AGAINST WHAT WAS SUBMITTED, NOT AGAINST THE SAVE, and that distinction is the
        /// whole correctness of this method. <see cref="OnGoalsChanged"/> already moved
        /// <c>bestScore</c> silently on the hot path, so a check of <c>score &lt;= ladder.bestScore</c>
        /// is always true by the time this runs — it would return early forever, the board would never
        /// be sent the new number, and <see cref="Changed"/> would never fire to redraw the screen.
        /// The player's score then sits frozen at whatever was last submitted while they go on selling
        /// bars, which is exactly how it failed the first time.
        /// </summary>
        private void Track(LadderState ladder)
        {
            long score = CurrentScore();
            if (score > ladder.bestScore)
            {
                ladder.bestScore = score;
                ladder.bestAchievedUnix = NowUnix();
            }

            if (ladder.bestScore <= _submitted) return;
            _submitted = ladder.bestScore;

            _leaderboard.SubmitScore(ladder.bestScore, result =>
            {
                _analytics?.Log("ladder_submit", "season",
                                ladder.seasonId + ":" + ladder.bestScore + ":" + (int)result.Status);
            });

            Changed?.Invoke();
        }

        /// <summary>
        /// A season has ended. Settle the outgoing one if the player was in it, then open the new one
        /// on a fresh baseline.
        ///
        /// The baselines are taken from the lifetime counters AS THEY ARE NOW, so actions taken while no
        /// season was open — or during a season the player never scored in — are not credited to the new
        /// one. Every season opened here scores points; the closing one settles in whatever unit it had.
        /// </summary>
        private void Roll(LadderState ladder, string current)
        {
            string closing = ladder.seasonId;

            // A season the player never scored in is not settled at all. Settling it would hand them
            // the tail bracket for having been asleep, and a reward for absence is the one thing a
            // ranking must not pay: it would make the bottom of the board worth as much as playing.
            bool played = !string.IsNullOrEmpty(closing) && ladder.bestScore > 0L
                          && !AlreadySettled(ladder, closing);

            // SETTLE ON THE SEASON'S BEST, NOT ON THE LAST NUMBER SUBMITTED. The hot path moves
            // bestScore without submitting, so a season that ends mid-session — OnGoalsChanged noticing
            // the rollover, before any read ran Track — would otherwise rank the player on a stale score.
            // It used to be hidden by a stale zero still finishing 30th and being paid; with ranks 31-50
            // unpaid it would cost the player the season. Restore merges, so replaying is harmless.
            if (played && _leaderboard is LocalLeaderboardService local)
                local.Restore(closing, ladder.bestScore, ladder.bestAchievedUnix);

            ladder.seasonId = current;
            ladder.baseline = _goals.Lifetime(ScoreMetric);
            ladder.points = true;
            if (ladder.baselines == null || ladder.baselines.Length != Goals.MetricCount)
                ladder.baselines = new long[Goals.MetricCount];
            for (int m = 0; m < Goals.MetricCount; m++) ladder.baselines[m] = _goals.Lifetime(m);
            if (_leaderboard is LocalLeaderboardService marking) marking.MarkPointsSeason(current);
            ladder.bestScore = 0L;
            ladder.bestAchievedUnix = 0L;
            _submitted = 0L;   // a fresh season has had nothing sent to it yet

            if (played) Settle(ladder, closing);
            else Commit();

            Changed?.Invoke();
        }

        private bool AlreadySettled(LadderState ladder, string seasonId)
        {
            List<string> settled = ladder.settledSeasons;
            for (int i = 0; i < settled.Count; i++)
                if (string.Equals(settled[i], seasonId, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// Asks what a closed season paid and files the answer. The season id is the idempotency key
        /// and it is written BEFORE the row, so a settlement delivered twice — by a retry, or by two
        /// launches racing the same rollover — files one row.
        ///
        /// Filing a row is not paying it. The reward is handed over by <see cref="Claim"/>, behind its
        /// own flag, because a grant that happens on a callback the player never saw is a grant they
        /// cannot tell happened.
        /// </summary>
        private void Settle(LadderState ladder, string seasonId)
        {
            _leaderboard.RequestSettlement(seasonId, settlement =>
            {
                // Anything but a clean answer is left alone: the season stays unsettled and the next
                // sync asks again. The one thing that must not happen is a row filed against a
                // settlement that never arrived.
                if (settlement.Status != LeaderboardStatus.Ok) { Commit(); return; }

                ladder.settledSeasons.Add(seasonId);
                ladder.inbox.Add(new LadderInboxRow
                {
                    seasonId = seasonId,
                    rank = settlement.PlayerRank,
                    tier = settlement.RewardTier,
                    claimed = false,
                });

                Commit();
                _analytics?.Log("ladder_settle", "season",
                                seasonId + ":" + settlement.PlayerRank + ":" + settlement.RewardTier);
            });
        }

        // ------------------------------------------------------------------------------- claim
        /// <summary>
        /// Collects a settled season's reward. Returns false when there is nothing to collect, which
        /// is what a double tap on the button is.
        ///
        /// THE FLAG IS WRITTEN AND SAVED BEFORE THE REWARD IS PAID, the order
        /// <c>ChapterService.Claim</c> and <c>IapTransactionJournal</c> both keep: a crash between the
        /// two costs the player one reward, and the other order costs them the reward every time they
        /// re-open the screen, forever.
        /// </summary>
        public bool Claim(string seasonId)
        {
            Sync();
            if (_data == null || string.IsNullOrEmpty(seasonId)) return false;

            List<LadderInboxRow> inbox = _data.ladder.inbox;
            for (int i = 0; i < inbox.Count; i++)
            {
                LadderInboxRow row = inbox[i];
                if (!string.Equals(row.seasonId, seasonId, StringComparison.Ordinal)) continue;
                if (row.claimed) return false;

                Ladder.Reward reward = Ladder.RewardFor(row.tier, _tuning);
                if (reward.Gems <= 0L && reward.Cards <= 0) return false;

                row.claimed = true;
                Commit();

                if (reward.Gems > 0L) _wallet?.AddGems(reward.Gems);
                if (reward.Cards > 0) _foremen?.GrantRandomDuplicates(reward.Cards);

                _analytics?.Log("ladder_claim", "season", seasonId + ":" + row.tier);
                Changed?.Invoke();
                return true;
            }

            return false;
        }

        /// <summary>Takes every settled season that still owes something. The screen's one button, for
        /// the player who was away a fortnight and has three of them waiting.</summary>
        public int ClaimAll()
        {
            Sync();
            if (_data == null) return 0;

            int paid = 0;
            List<LadderInboxRow> inbox = _data.ladder.inbox;
            // Indexed backwards over a snapshot of the count: Claim writes into the same list, and a
            // foreach over a collection being mutated is the classic way to miss the last row.
            for (int i = inbox.Count - 1; i >= 0; i--)
                if (!inbox[i].claimed && Claim(inbox[i].seasonId)) paid++;
            return paid;
        }

        private void Commit()
        {
            if (_save != null && _data != null) _save.Save(_data);
        }
    }
}
