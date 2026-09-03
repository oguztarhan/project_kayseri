using System;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// The Foundry Festival module: seven days of tasks, five chests, one <see cref="LiveEventService"/>
    /// row. The rules are in <see cref="FoundryFestival"/>; the schedule is in
    /// <see cref="Game.Data.LiveEventConfig"/>; this is what joins them to the rest of the game.
    ///
    /// IT ADDS NO COUNTER AND NO CURRENCY. Every task target is a metric <see cref="GoalService"/>
    /// already tallies, and the festival's job is only to turn a lifetime total into "how much of that
    /// happened inside my window, after this day unlocked". That conversion is the cursor slots, and
    /// it is the whole of <see cref="Sync"/>.
    ///
    /// EVERY WRITE GOES THROUGH <see cref="LiveEventService.Record"/>, which refuses once the window
    /// shuts. That is not politeness, it is the design: progress computed live from lifetime totals
    /// would keep completing tasks for weeks after the festival ended, because the player keeps
    /// buying upgrades. Pushing deltas through the gate means the counters FREEZE at the closing
    /// second, and the claim flags — which deliberately ignore the window — keep what was earned
    /// claimable forever. That pair is FIVE_LAYERS.md R3 for timed content: the window closes, the
    /// reward does not.
    ///
    /// SYNC IS LAZY, like <c>GoalService.Roll</c>. There is no subscription to the goal counters:
    /// every read syncs first, so the numbers are right whenever anything looks at them, and a device
    /// that was asleep for three days catches up in one pass on the next glance. Subscribing instead
    /// would run this on every bar sold, and every one of those would raise
    /// <see cref="LiveEventService.Changed"/> under a screen that has nothing new to draw.
    ///
    /// The cost of laziness is that work nobody has looked at yet is not banked, and the accrual gate
    /// drops it once the window shuts — so a task finished in the last seconds of the festival with
    /// every festival screen closed would be lost. Two syncs close that: <c>HudUI</c> ticks this four
    /// times a second, which bounds the loss to a quarter of one, and <see cref="GameBootstrap"/>
    /// syncs on the way out, before the save.
    /// </summary>
    public sealed class FoundryFestivalService
    {
        private readonly LiveEventService _events;
        private readonly GoalService _goals;
        private readonly WalletService _wallet;
        private readonly ForemanService _foremen;
        private readonly CaptainService _captains;
        private readonly BoostService _boost;
        private readonly SaveService _save;
        private readonly SaveData _data;
        private readonly TimeService _time;
        private readonly FoundryFestival.Tuning _tuning;

        /// <summary>The festival the screen is currently about, and whether there is one at all.</summary>
        private LiveEvents.Definition _def;
        private bool _has;

        /// <summary>Which second <see cref="Pick"/> last ran in. Which event is current can only change
        /// when the clock does, so the scan is worth doing once a second rather than once a read;
        /// the accrual below is cheap enough to run every time and must, because a test moves the
        /// goal counters and looks again inside the same second.</summary>
        private long _pickedUnix = long.MinValue;

        /// <summary>Guards the one re-entrant path: a Record raises <see cref="LiveEventService.Changed"/>,
        /// a screen refreshes on it, and refreshing reads this service — which would sync inside the
        /// sync it was called from.</summary>
        private bool _syncing;

        /// <summary>Raised when anything the festival screen shows has moved.</summary>
        public event Action Changed;

        public FoundryFestivalService(LiveEventService events, GoalService goals, WalletService wallet,
                                      FoundryFestival.Tuning tuning, ForemanService foremen = null,
                                      CaptainService captains = null, BoostService boost = null,
                                      SaveData data = null, SaveService save = null, TimeService time = null)
        {
            _events = events;
            _goals = goals;
            _wallet = wallet;
            _foremen = foremen;
            _captains = captains;
            _boost = boost;
            _data = data;
            _save = save;
            _time = time;

            if (FoundryFestival.IsWellFormed(tuning)) _tuning = tuning;
            else
            {
                _tuning = FoundryFestival.Tuning.Default;
                UnityEngine.Debug.LogWarning("[Şenlik] Geçersiz ayar tablosu verildi, varsayılan tablo " +
                                             "kullanılıyor.");
            }
        }

        /// <summary>The same fallback every other service keeps: built without a clock in a test, it
        /// still reads a real one rather than sitting at the epoch.</summary>
        private long NowUnix()
            => _time != null ? _time.NowUnix() : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // ------------------------------------------------------------------ which festival
        /// <summary>
        /// Whether a schedule row can carry a festival: it must be the right kind and it must have
        /// been authored with enough slots for the map in <see cref="FoundryFestival"/>. A short row
        /// is refused outright rather than clipped — <see cref="LiveEventService.Record"/> would
        /// silently drop the writes past the end, which is a festival whose last two days never
        /// count and never say why.
        /// </summary>
        private bool Fits(in LiveEvents.Definition d)
            => d.Kind == FoundryFestival.Kind && d.Slots >= FoundryFestival.Slots;

        /// <summary>
        /// Picks the festival the player is meant to be looking at: the one running, else the finished
        /// one that still owes a reward, else the next one coming, else the last one that ran.
        ///
        /// The order is what the player would ask for. A closed festival holding an unclaimed chest
        /// outranks an announcement for next month, because one of those is a thing they can act on.
        /// </summary>
        private void Pick(long now)
        {
            _has = false;
            if (_events == null) return;

            LiveEvents.Definition owed = default, soon = default, past = default;
            bool hasOwed = false, hasSoon = false, hasPast = false;

            for (int i = 0; i < _events.Count; i++)
            {
                LiveEvents.Definition d = _events.At(i);
                if (!Fits(d)) continue;
                if (!_events.Visible(d.Id)) continue;

                switch (LiveEvents.PhaseAt(d, now))
                {
                    case LiveEvents.Phase.Active:
                        _def = d;
                        _has = true;
                        return;                       // a running festival always wins

                    case LiveEvents.Phase.Upcoming:
                        if (!hasSoon || d.StartUnix < soon.StartUnix) { soon = d; hasSoon = true; }
                        break;

                    default:
                        if (!hasPast || d.StartUnix > past.StartUnix) { past = d; hasPast = true; }
                        if (Pending(d) > 0 && (!hasOwed || d.StartUnix > owed.StartUnix))
                        {
                            owed = d;
                            hasOwed = true;
                        }
                        break;
                }
            }

            if (hasOwed) { _def = owed; _has = true; return; }
            if (hasSoon) { _def = soon; _has = true; return; }
            if (hasPast) { _def = past; _has = true; }
        }

        // ------------------------------------------------------------------ sync
        /// <summary>
        /// Brings the festival's counters up to date with the goal tallies. Called by every read; safe
        /// to call as often as you like.
        /// </summary>
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
                Accrue();
            }
            finally { _syncing = false; }
        }

        /// <summary>
        /// Moves each metric's delta into every unlocked, unfinished task that asks for it.
        ///
        /// THE CURSOR IS SEEDED, NOT ZEROED. First contact writes <c>lifetime + 1</c> and counts
        /// nothing: an empire that has bought four thousand upgrades before the festival opened must
        /// not clear day one on sight, and the +1 is what separates "nobody has looked yet" from a
        /// player whose genuine total is zero.
        ///
        /// TASKS ARE WRITTEN BEFORE THE CURSOR. Both go through the same gate, and the window can shut
        /// between two calls. Tasks first means a delta at the closing second is paid to the player
        /// and the cursor is left behind; the other order would drop it. The cursor being behind costs
        /// nothing afterwards, because the next pass is refused by the same gate.
        /// </summary>
        private void Accrue()
        {
            if (!_has || _goals == null || _events == null) return;
            if (!_events.Accruing(_def.Id)) return;

            int unlockedDays = FoundryFestival.DayIndex(_def.StartUnix, NowUnix()) + 1;
            bool moved = false;

            for (int metric = 0; metric < Goals.MetricCount; metric++)
            {
                int cursorSlot = FoundryFestival.CursorSlot(metric);
                long stored = _events.Progress(_def.Id, cursorSlot);
                long lifetime = _goals.Lifetime(metric);

                if (stored <= 0L)
                {
                    _events.Record(_def.Id, cursorSlot, lifetime + 1L);
                    continue;
                }

                long delta = lifetime - (stored - 1L);
                if (delta <= 0L) continue;

                for (int slot = 0; slot < unlockedDays * FoundryFestival.TasksPerDay; slot++)
                {
                    if (_tuning.Tasks[slot].Metric != metric) continue;
                    if (_events.Progress(_def.Id, slot) >= _tuning.Tasks[slot].Target) continue;
                    _events.Record(_def.Id, slot, delta);
                    moved = true;
                }

                _events.Record(_def.Id, cursorSlot, delta);
            }

            if (moved) Changed?.Invoke();
        }

        // ------------------------------------------------------------------ read
        /// <summary>Whether there is a festival to show at all.</summary>
        public bool Available { get { Sync(); return _has; } }

        public string Id { get { Sync(); return _has ? _def.Id : null; } }

        public LiveEvents.Phase Phase
            { get { Sync(); return _has ? LiveEvents.PhaseAt(_def, NowUnix()) : LiveEvents.Phase.Closed; } }

        public bool Live => Phase == LiveEvents.Phase.Active;

        /// <summary>Today's festival day, 0-based. Reads 0 before the window opens.</summary>
        public int Day
        {
            get
            {
                Sync();
                if (!_has) return 0;
                return LiveEvents.PhaseAt(_def, NowUnix()) == LiveEvents.Phase.Upcoming
                    ? 0 : FoundryFestival.DayIndex(_def.StartUnix, NowUnix());
            }
        }

        public long SecondsLeft { get { Sync(); return _has ? LiveEvents.SecondsLeft(_def, NowUnix()) : 0L; } }

        public long SecondsUntilStart
            { get { Sync(); return _has ? LiveEvents.SecondsUntilStart(_def, NowUnix()) : 0L; } }

        /// <summary>Seconds until the next day's tasks open; 0 on the last day and before the start.</summary>
        public long SecondsToNextDay
        {
            get
            {
                Sync();
                if (!_has || LiveEvents.PhaseAt(_def, NowUnix()) != LiveEvents.Phase.Active) return 0L;
                return FoundryFestival.SecondsToNextDay(_def.StartUnix, NowUnix());
            }
        }

        public FoundryFestival.Task TaskAt(int slot)
            => slot >= 0 && slot < FoundryFestival.TaskCount ? _tuning.Tasks[slot] : default;

        public FoundryFestival.Milestone MilestoneAt(int index)
            => index >= 0 && index < FoundryFestival.MilestoneCount ? _tuning.Milestones[index] : default;

        /// <summary>
        /// Whether a task's day has opened. Nothing is unlocked before the window does; once it has
        /// closed everything reads unlocked, which is only what the frozen counters already say.
        /// </summary>
        public bool TaskUnlocked(int slot)
        {
            Sync();
            if (!_has || slot < 0 || slot >= FoundryFestival.TaskCount) return false;
            if (LiveEvents.PhaseAt(_def, NowUnix()) == LiveEvents.Phase.Upcoming) return false;
            return FoundryFestival.DayOf(slot) <= FoundryFestival.DayIndex(_def.StartUnix, NowUnix());
        }

        /// <summary>Progress toward a task, clamped to its target — the counter accrues in whole
        /// deltas and the last one usually overshoots.</summary>
        public long TaskProgress(int slot)
        {
            Sync();
            if (!_has || slot < 0 || slot >= FoundryFestival.TaskCount) return 0L;
            long have = _events.Progress(_def.Id, slot);
            long target = _tuning.Tasks[slot].Target;
            return have > target ? target : have;
        }

        public bool TaskDone(int slot)
        {
            Sync();
            if (!_has || slot < 0 || slot >= FoundryFestival.TaskCount) return false;
            return _events.Progress(_def.Id, slot) >= _tuning.Tasks[slot].Target;
        }

        public bool TaskClaimed(int slot)
        {
            Sync();
            return _has && slot >= 0 && slot < FoundryFestival.TaskCount && _events.Claimed(_def.Id, slot);
        }

        public bool CanClaimTask(int slot) => TaskUnlocked(slot) && TaskDone(slot) && !TaskClaimed(slot);

        /// <summary>Points earned toward the chests: every FINISHED task, claimed or not.</summary>
        public int Points { get { Sync(); return _has ? PointsOf(_def) : 0; } }

        public int TotalPoints => FoundryFestival.TotalPoints(_tuning);

        /// <summary>The next chest's price, or 0 when they are all open.</summary>
        public int NextMilestonePoints => FoundryFestival.NextMilestonePoints(_tuning, Points);

        public bool MilestoneEarned(int index)
        {
            Sync();
            if (!_has || index < 0 || index >= FoundryFestival.MilestoneCount) return false;
            return PointsOf(_def) >= _tuning.Milestones[index].Points;
        }

        public bool MilestoneClaimed(int index)
        {
            Sync();
            return _has && index >= 0 && index < FoundryFestival.MilestoneCount
                && _events.Claimed(_def.Id, FoundryFestival.MilestoneSlot(index));
        }

        public bool CanClaimMilestone(int index) => MilestoneEarned(index) && !MilestoneClaimed(index);

        /// <summary>How many rewards are waiting — the number on the events badge.</summary>
        public int PendingCount() { Sync(); return _has ? Pending(_def) : 0; }

        private int PointsOf(in LiveEvents.Definition d)
        {
            int points = 0;
            for (int slot = 0; slot < FoundryFestival.TaskCount; slot++)
                if (_events.Progress(d.Id, slot) >= _tuning.Tasks[slot].Target) points += _tuning.Tasks[slot].Points;
            return points;
        }

        /// <summary>Claimable rewards on one festival, whichever one it is — <see cref="Pick"/> asks
        /// this of events it has not chosen yet.</summary>
        private int Pending(in LiveEvents.Definition d)
        {
            int waiting = 0, points = 0;
            for (int slot = 0; slot < FoundryFestival.TaskCount; slot++)
            {
                if (_events.Progress(d.Id, slot) < _tuning.Tasks[slot].Target) continue;
                points += _tuning.Tasks[slot].Points;
                if (!_events.Claimed(d.Id, slot)) waiting++;
            }

            for (int i = 0; i < FoundryFestival.MilestoneCount; i++)
                if (points >= _tuning.Milestones[i].Points
                    && !_events.Claimed(d.Id, FoundryFestival.MilestoneSlot(i))) waiting++;

            return waiting;
        }

        // ----------------------------------------------------------------- claim
        /// <summary>
        /// Takes one task's reward. False means there was nothing to take — including the case where
        /// this call lost a race to another one, because <see cref="LiveEventService.MarkClaimed"/> is
        /// what decides, not the check above it.
        /// </summary>
        public bool ClaimTask(int slot)
        {
            if (!CanClaimTask(slot)) return false;
            if (!_events.MarkClaimed(_def.Id, slot)) return false;

            FoundryFestival.Task t = _tuning.Tasks[slot];
            Pay(t.Gems, t.Cards, 0L, 0d, 0d);
            Commit();
            return true;
        }

        public bool ClaimMilestone(int index)
        {
            if (!CanClaimMilestone(index)) return false;
            if (!_events.MarkClaimed(_def.Id, FoundryFestival.MilestoneSlot(index))) return false;

            FoundryFestival.Milestone m = _tuning.Milestones[index];
            Pay(m.Gems, m.Cards, m.Charts, m.BoostMult, m.BoostSeconds);
            Commit();
            return true;
        }

        /// <summary>
        /// Takes everything owed in one press: finished tasks first, then the chests they opened.
        ///
        /// One payment and one save for the lot. A player who comes back on the last day has a dozen
        /// of these waiting, and paying them one tap at a time is a dozen taps for one decision — the
        /// same reasoning as <c>ChapterService.ClaimChapter</c>. The order does not affect the total:
        /// a chest counts FINISHED tasks, so claiming one does not open another.
        /// </summary>
        public int ClaimAll()
        {
            Sync();
            if (!_has) return 0;

            long gems = 0L, charts = 0L;
            int cards = 0, taken = 0;

            for (int slot = 0; slot < FoundryFestival.TaskCount; slot++)
            {
                if (!CanClaimTask(slot)) continue;
                if (!_events.MarkClaimed(_def.Id, slot)) continue;
                gems += _tuning.Tasks[slot].Gems;
                cards += _tuning.Tasks[slot].Cards;
                taken++;
            }

            for (int i = 0; i < FoundryFestival.MilestoneCount; i++)
            {
                if (!CanClaimMilestone(i)) continue;
                if (!_events.MarkClaimed(_def.Id, FoundryFestival.MilestoneSlot(i))) continue;
                FoundryFestival.Milestone m = _tuning.Milestones[i];
                gems += m.Gems;
                cards += m.Cards;
                charts += m.Charts;
                // Boosts are handed over as they are found rather than summed: AddBoost already knows
                // how to fold one into another without either being lost.
                if (m.BoostMult > 1d && m.BoostSeconds > 0d) _boost?.AddBoost(m.BoostMult, m.BoostSeconds);
                taken++;
            }

            if (taken == 0) return 0;
            Pay(gems, cards, charts, 0d, 0d);
            Commit();
            return taken;
        }

        /// <summary>
        /// Hands over a reward. Every line goes through the service that owns the thing being paid —
        /// there is no second wallet, no second roster and no second boost clock here.
        /// </summary>
        private void Pay(long gems, int cards, long charts, double boostMult, double boostSeconds)
        {
            if (gems > 0L) _wallet?.AddGems(gems);
            if (cards > 0) _foremen?.GrantRandomDuplicates(cards);
            if (charts > 0L) _captains?.AddCharts(charts);
            if (boostMult > 1d && boostSeconds > 0d) _boost?.AddBoost(boostMult, boostSeconds);
        }

        /// <summary>
        /// Writes the claim down. The flag and the reward land in the SAME save, which is what makes
        /// the claim idempotent under a crash: the file either holds both or neither, and neither is
        /// simply an unclaimed reward the player takes again.
        /// </summary>
        private void Commit()
        {
            if (_save != null && _data != null) _save.Save(_data);
            Changed?.Invoke();
        }
    }
}
