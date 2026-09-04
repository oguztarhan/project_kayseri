using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// Owns the goal state: lifetime tallies, today's three tasks, and which achievement tiers have
    /// been collected. The maths is in <see cref="Goals"/>; this counts things and pays out.
    ///
    /// ONE CALL, SIX PLACES. <see cref="Record"/> is the whole write surface, and it is invoked from
    /// exactly one line each in MarketService (bars sold), CoalOperation (upgrades and unlocks),
    /// ContractService (a contract claimed), MaintenanceService (a repair started), WorldIslands (an
    /// island bought) and ForemanService (a foreman hired or levelled). Nothing else has to know the
    /// goal system exists, which is what keeps it from leaking into the simulation.
    ///
    /// THE DAY ROLL is lazy rather than scheduled. There is no timer and nothing to tick: the first
    /// read after midnight UTC notices the day number moved, snapshots the lifetime totals as the new
    /// baseline and clears the claims. That means a session left open across midnight rolls the moment
    /// the player next looks, and a device that was asleep for a week rolls exactly once — the same
    /// shape DailyRewardService and FreeRewardService already use.
    /// </summary>
    public sealed class GoalService
    {
        /// <summary>
        /// Presentation-safe description of a completed claim. It is created only after validation,
        /// grant and persistence; UI may animate it but cannot use it to grant anything.
        /// </summary>
        public readonly struct ClaimReceipt
        {
            public readonly int Items;
            public readonly long Gems;
            public readonly int Cards;

            public ClaimReceipt(int items, long gems, int cards)
            {
                Items = items;
                Gems = gems;
                Cards = cards;
            }

            public bool Any => Items > 0;
        }

        private readonly SaveData _data;
        private readonly WalletService _wallet;
        private readonly ForemanService _foremen;
        private readonly TimeService _time;
        private readonly SaveService _save;

        /// <summary>Raised when anything a goal screen shows has moved. No argument: the screen is six
        /// rows and three cards, and refreshing all of it is cheaper than working out what changed.</summary>
        public event Action Changed;

        public GoalService(SaveData data, WalletService wallet, ForemanService foremen = null,
                           TimeService time = null, SaveService save = null)
        {
            _data = data;
            _wallet = wallet;
            _foremen = foremen;
            _time = time;
            _save = save;
            Normalise();
            Roll();
            RollWeek();

            // The roster reports its own levels rather than being asked, so ForemanService never has
            // to know this class exists — see ForemanService.Levelled.
            if (_foremen != null) _foremen.Levelled += OnForemanLevelled;
        }

        private void OnForemanLevelled(int station) => Record(Goals.ForemanLevels);

        private long NowUnix() => _time != null
            ? _time.NowUnix()
            : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        /// <summary>
        /// A save written before the goal system existed arrives with these missing or short — the
        /// same padding-on-load contract the foreman roster uses, and for the same reason: adding
        /// fields must not cost a save version bump, because a bump wipes progress.
        /// </summary>
        private void Normalise()
        {
            if (_data == null) return;
            if (_data.goals == null) _data.goals = new GoalSaveData();
            var g = _data.goals;
            g.lifetime = Fit(g.lifetime, Goals.MetricCount);
            g.dayBaseline = Fit(g.dayBaseline, Goals.MetricCount);
            g.weekBaseline = Fit(g.weekBaseline, Goals.MetricCount);
            g.tiersClaimed = Fit(g.tiersClaimed, Goals.Ladder.Length);
            if (g.weeklyMilestonesClaimed == null) g.weeklyMilestonesClaimed = new string[0];
            if (g.dailyClaimed == null || g.dailyClaimed.Length != Goals.DailySlots)
            {
                var claimed = new bool[Goals.DailySlots];
                if (g.dailyClaimed != null)
                {
                    int n = Math.Min(g.dailyClaimed.Length, Goals.DailySlots);
                    for (int i = 0; i < n; i++) claimed[i] = g.dailyClaimed[i];
                }
                g.dailyClaimed = claimed;
            }
        }

        private static long[] Fit(long[] src, int len)
        {
            if (src != null && src.Length == len) return src;
            var fitted = new long[len];
            if (src != null) { int n = Math.Min(src.Length, len); for (int i = 0; i < n; i++) fitted[i] = src[i]; }
            return fitted;
        }

        private static int[] Fit(int[] src, int len)
        {
            if (src != null && src.Length == len) return src;
            var fitted = new int[len];
            if (src != null) { int n = Math.Min(src.Length, len); for (int i = 0; i < n; i++) fitted[i] = src[i]; }
            return fitted;
        }

        // ------------------------------------------------------------------ day
        public int Today => Goals.DayNumber(NowUnix());

        /// <summary>Rolls the dailies if the UTC day has moved. Safe to call as often as you like.</summary>
        private bool Roll()
        {
            if (_data == null) return false;
            var g = _data.goals;
            int today = Today;
            if (g.day == today) return false;

            g.day = today;
            for (int m = 0; m < Goals.MetricCount; m++) g.dayBaseline[m] = g.lifetime[m];
            for (int i = 0; i < Goals.DailySlots; i++) g.dailyClaimed[i] = false;
            return true;
        }

        public int ThisWeek => Goals.WeekNumber(NowUnix());

        /// <summary>
        /// Starts a new Monday-based UTC week from the current lifetime totals. An older save has no
        /// weekly baseline, so its first load starts clean instead of retroactively completing tiers.
        /// </summary>
        private bool RollWeek()
        {
            if (_data == null) return false;
            GoalSaveData g = _data.goals;
            int week = ThisWeek;
            if (g.week == week) return false;

            g.week = week;
            for (int m = 0; m < Goals.MetricCount; m++) g.weekBaseline[m] = g.lifetime[m];
            g.weeklyMilestonesClaimed = new string[0];
            return true;
        }

        // ------------------------------------------------------------------ write
        /// <summary>
        /// Count something the player did. The only way anything enters this system.
        /// </summary>
        public void Record(int metric, long amount = 1L)
        {
            if (_data == null || amount <= 0L) return;
            if (metric < 0 || metric >= Goals.MetricCount) return;
            Roll();
            RollWeek();
            _data.goals.lifetime[metric] += amount;
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ read
        public long Lifetime(int metric)
            => _data != null && metric >= 0 && metric < Goals.MetricCount ? _data.goals.lifetime[metric] : 0L;

        /// <summary>Today's progress on a metric: how far the lifetime total has moved since the roll.</summary>
        public long TodayProgress(int metric)
        {
            if (_data == null || metric < 0 || metric >= Goals.MetricCount) return 0L;
            Roll();
            long delta = _data.goals.lifetime[metric] - _data.goals.dayBaseline[metric];
            return delta > 0L ? delta : 0L;
        }

        public Goals.Task DailyTask(int slot) { Roll(); return Goals.DailyTask(_data.goals.day, slot); }

        public long DailyProgress(int slot)
        {
            if (slot < 0 || slot >= Goals.DailySlots) return 0L;
            Goals.Task t = DailyTask(slot);
            return TodayProgress(t.Metric);
        }

        public bool DailyDone(int slot)
        {
            if (slot < 0 || slot >= Goals.DailySlots) return false;
            Goals.Task t = DailyTask(slot);
            return TodayProgress(t.Metric) >= t.Target;
        }

        public bool DailyClaimed(int slot)
        {
            if (_data == null || slot < 0 || slot >= Goals.DailySlots) return false;
            Roll();
            return _data.goals.dailyClaimed[slot];
        }

        public bool CanClaimDaily(int slot)
            => slot >= 0 && slot < Goals.DailySlots && DailyDone(slot) && !DailyClaimed(slot);

        public long WeekProgress(int metric)
        {
            if (_data == null || metric < 0 || metric >= Goals.MetricCount) return 0L;
            RollWeek();
            long delta = _data.goals.lifetime[metric] - _data.goals.weekBaseline[metric];
            return delta > 0L ? delta : 0L;
        }

        public long WeeklyProgress(int slot)
        {
            if (slot < 0 || slot >= Goals.WeeklyTasks.Length) return 0L;
            return WeekProgress(Goals.WeeklyTasks[slot].Metric);
        }

        public bool WeeklyDone(int slot)
            => slot >= 0 && slot < Goals.WeeklyTasks.Length
               && WeeklyProgress(slot) >= Goals.WeeklyTasks[slot].Target;

        public int WeeklyPoints()
        {
            RollWeek();
            int points = 0;
            for (int i = 0; i < Goals.WeeklyTasks.Length; i++)
                if (WeeklyDone(i)) points += Goals.WeeklyTasks[i].Points;
            return points;
        }

        public bool WeeklyMilestoneClaimed(int index)
        {
            if (_data == null || index < 0 || index >= Goals.WeeklyMilestones.Length) return false;
            RollWeek();
            string id = Goals.WeeklyMilestones[index].Id;
            string[] claimed = _data.goals.weeklyMilestonesClaimed;
            for (int i = 0; i < claimed.Length; i++) if (claimed[i] == id) return true;
            return false;
        }

        public bool CanClaimWeeklyMilestone(int index)
            => index >= 0 && index < Goals.WeeklyMilestones.Length
               && WeeklyPoints() >= Goals.WeeklyMilestones[index].Points
               && !WeeklyMilestoneClaimed(index);

        /// <summary>Tiers of achievement <paramref name="index"/> earned but not yet collected.</summary>
        public int UnclaimedTiers(int index)
        {
            if (_data == null || index < 0 || index >= Goals.Ladder.Length) return 0;
            int reached = Goals.TiersReached(Goals.Ladder[index], Lifetime(Goals.Ladder[index].Metric));
            int claimed = _data.goals.tiersClaimed[index];
            return reached > claimed ? reached - claimed : 0;
        }

        /// <summary>How many things are waiting to be collected — the number on the HUD badge.</summary>
        public int PendingCount()
        {
            int n = 0;
            for (int i = 0; i < Goals.DailySlots; i++) if (CanClaimDaily(i)) n++;
            for (int i = 0; i < Goals.WeeklyMilestones.Length; i++)
                if (CanClaimWeeklyMilestone(i)) n++;
            for (int i = 0; i < Goals.Ladder.Length; i++) if (UnclaimedTiers(i) > 0) n++;
            return n;
        }

        // ----------------------------------------------------------------- claim
        public bool ClaimDaily(int slot) => ClaimDaily(slot, out _);

        public bool ClaimDaily(int slot, out ClaimReceipt receipt)
        {
            receipt = default;
            if (!CanClaimDaily(slot)) return false;
            Goals.Task t = DailyTask(slot);
            _data.goals.dailyClaimed[slot] = true;
            Pay(t.Gems, t.Cards);
            Commit();
            receipt = new ClaimReceipt(1, t.Gems, t.Cards);
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Collects every tier of one achievement that has been earned, in one press. Paying them one
        /// at a time would mean tapping six times on a ladder the player passed while offline, and the
        /// reward is per-tier either way.
        /// </summary>
        public bool ClaimAchievement(int index) => ClaimAchievement(index, out _);

        public bool ClaimAchievement(int index, out ClaimReceipt receipt)
        {
            receipt = default;
            int owed = UnclaimedTiers(index);
            if (owed <= 0) return false;

            Goals.Achievement a = Goals.Ladder[index];
            int claimed = _data.goals.tiersClaimed[index];
            long gems = 0L;
            int cards = 0;
            for (int t = claimed + 1; t <= claimed + owed; t++)
            {
                gems += Goals.TierGems(a, t);
                cards += Goals.TierCards(a, t);
            }
            _data.goals.tiersClaimed[index] = claimed + owed;
            Pay(gems, cards);
            Commit();
            receipt = new ClaimReceipt(1, gems, cards);
            Changed?.Invoke();
            return true;
        }

        public bool ClaimWeeklyMilestone(int index) => ClaimWeeklyMilestone(index, out _);

        public bool ClaimWeeklyMilestone(int index, out ClaimReceipt receipt)
        {
            receipt = default;
            if (!CanClaimWeeklyMilestone(index)) return false;
            Goals.WeeklyMilestone milestone = Goals.WeeklyMilestones[index];

            var claimed = new List<string>(_data.goals.weeklyMilestonesClaimed);
            if (claimed.Contains(milestone.Id)) return false;
            claimed.Add(milestone.Id);
            _data.goals.weeklyMilestonesClaimed = claimed.ToArray();

            Pay(milestone.Gems, milestone.Cards);
            Commit();
            receipt = new ClaimReceipt(1, milestone.Gems, milestone.Cards);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Claims every ready daily, weekly and achievement reward in one saved operation.</summary>
        public int ClaimAll() => ClaimAll(out ClaimReceipt receipt) ? receipt.Items : 0;

        public bool ClaimAll(out ClaimReceipt receipt)
        {
            receipt = default;
            if (_data == null) return false;
            Roll();
            RollWeek();

            long gems = 0L;
            int cards = 0;
            int taken = 0;

            for (int i = 0; i < Goals.DailySlots; i++)
            {
                if (!CanClaimDaily(i)) continue;
                Goals.Task task = DailyTask(i);
                _data.goals.dailyClaimed[i] = true;
                gems += task.Gems;
                cards += task.Cards;
                taken++;
            }

            var weeklyClaimed = new List<string>(_data.goals.weeklyMilestonesClaimed);
            int weeklyPoints = WeeklyPoints();
            for (int i = 0; i < Goals.WeeklyMilestones.Length; i++)
            {
                Goals.WeeklyMilestone milestone = Goals.WeeklyMilestones[i];
                if (weeklyPoints < milestone.Points || weeklyClaimed.Contains(milestone.Id)) continue;
                weeklyClaimed.Add(milestone.Id);
                gems += milestone.Gems;
                cards += milestone.Cards;
                taken++;
            }
            _data.goals.weeklyMilestonesClaimed = weeklyClaimed.ToArray();

            for (int i = 0; i < Goals.Ladder.Length; i++)
            {
                int owed = UnclaimedTiers(i);
                if (owed <= 0) continue;
                Goals.Achievement achievement = Goals.Ladder[i];
                int claimedTiers = _data.goals.tiersClaimed[i];
                for (int tier = claimedTiers + 1; tier <= claimedTiers + owed; tier++)
                {
                    gems += Goals.TierGems(achievement, tier);
                    cards += Goals.TierCards(achievement, tier);
                }
                _data.goals.tiersClaimed[i] = claimedTiers + owed;
                taken++;
            }

            if (taken == 0) return false;
            Pay(gems, cards);
            Commit();
            receipt = new ClaimReceipt(taken, gems, cards);
            Changed?.Invoke();
            return true;
        }

        private void Pay(long gems, int cards)
        {
            if (gems > 0L && _wallet != null) _wallet.AddGems(gems);
            if (cards > 0 && _foremen != null) _foremen.GrantRandomDuplicates(cards);
        }

        private void Commit()
        {
            if (_save != null && _data != null) _save.Save(_data);
        }
    }
}
