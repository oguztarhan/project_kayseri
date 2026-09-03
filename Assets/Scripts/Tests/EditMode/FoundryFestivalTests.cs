using System;
using System.Collections.Generic;
using NUnit.Framework;
using Game.Core;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>
    /// The Foundry Festival. The slot map and the day maths are asked of <see cref="FoundryFestival"/>
    /// directly, where the clock is an argument; the module tests then move the window around a REAL
    /// clock, because that is the only thing <see cref="TimeService"/> reads.
    ///
    /// Time passing is simulated by rebuilding the services against the same save with the window
    /// shifted — which is also exactly what a real session does, since a definition is read once at
    /// launch and the save outlives it. That makes "the player came back three days later" and "the
    /// festival ended while the app was shut" the same test shape.
    /// </summary>
    public class FoundryFestivalTests
    {
        private const long Day = 86400L;
        private const string Id = "senlik";

        private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private static LiveEvents.Definition Def(long start, long end, int version = 1,
                                                 int slots = FoundryFestival.Slots)
            => new LiveEvents.Definition
            {
                Id = Id,
                Kind = FoundryFestival.Kind,
                StartUnix = start,
                EndUnix = end,
                ConfigVersion = version,
                Slots = slots,
                MinIslands = 0,
            };

        /// <summary>One save, one set of services over it — the four the festival actually joins.</summary>
        private sealed class Rig
        {
            public SaveData Data;
            public WalletService Wallet;
            public GoalService Goals;
            public FoundryFestivalService Festival;
        }

        private static Rig Build(SaveData data, long start, long end, int version = 1,
                                 int slots = FoundryFestival.Slots)
        {
            var wallet = new WalletService(data.wallet);
            var goals = new GoalService(data, wallet, null, new TimeService());
            var events = new LiveEventService(data, new List<LiveEvents.Definition> { Def(start, end, version, slots) },
                                              new TimeService());
            var festival = new FoundryFestivalService(events, goals, wallet, FoundryFestival.Tuning.Default,
                                                      null, null, null, data, null, new TimeService());
            return new Rig { Data = data, Wallet = wallet, Goals = goals, Festival = festival };
        }

        /// <summary>A festival that opened <paramref name="daysAgo"/> days ago and has not closed.</summary>
        private static Rig Running(SaveData data, long daysAgo = 0L, int version = 1,
                                   int slots = FoundryFestival.Slots)
        {
            long now = Now();
            return Build(data, now - daysAgo * Day, now - daysAgo * Day + FoundryFestival.Days * Day,
                         version, slots);
        }

        // ---- the slot map -------------------------------------------------------------------------

        [Test]
        public void EverySlotIsItsOwnAndTheyAllFit()
        {
            var seen = new HashSet<int>();

            for (int day = 0; day < FoundryFestival.Days; day++)
                for (int i = 0; i < FoundryFestival.TasksPerDay; i++)
                    Assert.That(seen.Add(FoundryFestival.TaskSlot(day, i)), Is.True,
                                "görev yuvası " + day + "/" + i + " başka bir şeyle çakışıyor");

            for (int i = 0; i < FoundryFestival.MilestoneCount; i++)
                Assert.That(seen.Add(FoundryFestival.MilestoneSlot(i)), Is.True, "sandık yuvası çakışıyor");

            for (int m = 0; m < Goals.MetricCount; m++)
                Assert.That(seen.Add(FoundryFestival.CursorSlot(m)), Is.True, "imleç yuvası çakışıyor");

            Assert.That(seen.Count, Is.EqualTo(FoundryFestival.Slots));
            Assert.That(FoundryFestival.Slots, Is.LessThanOrEqualTo(LiveEvents.MaxSlots),
                        "yuva haritası tek bir etkinliğin taşıyabileceğinden büyük olamaz");
        }

        [Test]
        public void ATaskSlotKnowsWhichDayItBelongsTo()
        {
            Assert.That(FoundryFestival.DayOf(FoundryFestival.TaskSlot(0, 2)), Is.Zero);
            Assert.That(FoundryFestival.DayOf(FoundryFestival.TaskSlot(3, 0)), Is.EqualTo(3));
            Assert.That(FoundryFestival.DayOf(FoundryFestival.TaskSlot(6, 2)), Is.EqualTo(6));
        }

        // ---- the days -----------------------------------------------------------------------------

        [Test]
        public void TheDayClimbsWithTheWindowAndStopsAtTheLast()
        {
            Assert.That(FoundryFestival.DayIndex(1000L, 1000L), Is.Zero, "açılış saniyesi ilk gündür");
            Assert.That(FoundryFestival.DayIndex(1000L, 1000L + Day - 1L), Is.Zero);
            Assert.That(FoundryFestival.DayIndex(1000L, 1000L + Day), Is.EqualTo(1));
            Assert.That(FoundryFestival.DayIndex(1000L, 1000L + 6L * Day), Is.EqualTo(6));
            Assert.That(FoundryFestival.DayIndex(1000L, 1000L + 40L * Day), Is.EqualTo(6),
                        "yedinci günden sonrası yine yedinci gündür");
            Assert.That(FoundryFestival.DayIndex(1000L, 500L), Is.Zero, "açılmadan önce ilk gün okunur");
        }

        [Test]
        public void TheCountdownToTheNextDayRunsOutOnTheLastOne()
        {
            Assert.That(FoundryFestival.SecondsToNextDay(1000L, 1000L), Is.EqualTo(Day));
            Assert.That(FoundryFestival.SecondsToNextDay(1000L, 1000L + Day - 10L), Is.EqualTo(10L));
            Assert.That(FoundryFestival.SecondsToNextDay(1000L, 1000L + 6L * Day), Is.Zero,
                        "son günün ardından açılacak gün yok");
        }

        // ---- the table ----------------------------------------------------------------------------

        [Test]
        public void TheShippedTableIsWellFormed()
        {
            Assert.That(FoundryFestival.IsWellFormed(FoundryFestival.Tuning.Default), Is.True);
        }

        /// <summary>
        /// The one balance rule worth pinning: the last chest must be reachable. A festival whose
        /// headline prize costs more points than the week can pay is chased for seven days and never
        /// handed over.
        /// </summary>
        [Test]
        public void TheLastChestSitsUnderEveryPointTheWeekPays()
        {
            FoundryFestival.Tuning t = FoundryFestival.Tuning.Default;
            int last = t.Milestones[FoundryFestival.MilestoneCount - 1].Points;

            Assert.That(last, Is.LessThan(FoundryFestival.TotalPoints(t)),
                        "son sandık haftanın verebileceği toplam puanın altında olmalı");
        }

        /// <summary>Money metrics inflate x3.2 per ore tier, which is why Goals keeps them out of
        /// fixed daily targets. A seven-day event has the same problem.</summary>
        [Test]
        public void NoTaskIsPricedInSomethingThatInflates()
        {
            FoundryFestival.Tuning t = FoundryFestival.Tuning.Default;
            for (int i = 0; i < t.Tasks.Length; i++)
            {
                Assert.That(t.Tasks[i].Metric, Is.Not.EqualTo(Goals.BarsSold),
                            i + ". görev külçe sayıyor — kömürde imkânsız, elmasta bedava");
                Assert.That(t.Tasks[i].Metric, Is.Not.EqualTo(Goals.Islands),
                            i + ". görev ada satın almak — bir haftaya sığmaz");
            }
        }

        [Test]
        public void AMalformedTableIsRefused()
        {
            FoundryFestival.Tuning t = FoundryFestival.Tuning.Default;
            Assert.That(FoundryFestival.IsWellFormed(new FoundryFestival.Tuning
            {
                Tasks = new FoundryFestival.Task[3], Milestones = t.Milestones,
            }), Is.False, "eksik görev tablosu");

            var unreachable = (FoundryFestival.Milestone[])t.Milestones.Clone();
            unreachable[FoundryFestival.MilestoneCount - 1].Points = 99999;
            Assert.That(FoundryFestival.IsWellFormed(new FoundryFestival.Tuning
            {
                Tasks = t.Tasks, Milestones = unreachable,
            }), Is.False, "ulaşılamayan sandık");
        }

        [Test]
        public void ChestsOpenInOrderAndTheBarKnowsWhatIsNext()
        {
            FoundryFestival.Tuning t = FoundryFestival.Tuning.Default;
            int first = t.Milestones[0].Points;

            Assert.That(FoundryFestival.MilestonesEarned(t, first - 1), Is.Zero);
            Assert.That(FoundryFestival.MilestonesEarned(t, first), Is.EqualTo(1));
            Assert.That(FoundryFestival.NextMilestonePoints(t, 0), Is.EqualTo(first));
            Assert.That(FoundryFestival.NextMilestonePoints(t, FoundryFestival.TotalPoints(t)), Is.Zero,
                        "hepsi açıldıysa hedef kalmaz");
        }

        // ---- accrual ------------------------------------------------------------------------------

        /// <summary>The cursor's whole reason for existing: an empire that bought four thousand
        /// upgrades before the festival opened must not clear day one on sight.</summary>
        [Test]
        public void AVeteranEmpireDoesNotClearDayOneOnSight()
        {
            var data = new SaveData();
            Rig rig = Running(data);
            rig.Goals.Record(Goals.Upgrades, 4000L);

            Assert.That(rig.Festival.TaskProgress(0), Is.Zero, "şenlikten önceki iş sayılmaz");
            Assert.That(rig.Festival.TaskDone(0), Is.False);
        }

        [Test]
        public void WorkInsideTheWindowCountsTowardTheOpenDay()
        {
            var data = new SaveData();
            Rig rig = Running(data);
            rig.Festival.Sync();                       // first sight seeds the cursors

            rig.Goals.Record(Goals.Upgrades, 3L);
            Assert.That(rig.Festival.TaskProgress(0), Is.EqualTo(3L));
            Assert.That(rig.Festival.TaskDone(0), Is.False);

            rig.Goals.Record(Goals.Upgrades, 2L);
            Assert.That(rig.Festival.TaskDone(0), Is.True, "5 yükseltme birinci günün ilk görevidir");
        }

        [Test]
        public void ProgressIsShownClampedToTheTarget()
        {
            var data = new SaveData();
            Rig rig = Running(data);
            rig.Festival.Sync();

            rig.Goals.Record(Goals.Upgrades, 500L);
            Assert.That(rig.Festival.TaskProgress(0), Is.EqualTo(rig.Festival.TaskAt(0).Target));
        }

        [Test]
        public void ALockedDayAccruesNothing()
        {
            var data = new SaveData();
            Rig rig = Running(data);
            rig.Festival.Sync();
            rig.Goals.Record(Goals.Upgrades, 50L);

            int lastDayFirstTask = FoundryFestival.TaskSlot(FoundryFestival.Days - 1, 0);
            Assert.That(rig.Festival.TaskUnlocked(lastDayFirstTask), Is.False);
            Assert.That(rig.Festival.TaskProgress(lastDayFirstTask), Is.Zero);
            Assert.That(rig.Festival.TaskDone(lastDayFirstTask), Is.False);
        }

        /// <summary>
        /// The rule a per-day festival lives or dies on: work done before a day opens does not count
        /// toward it. Otherwise a player who ground out fifty upgrades on the first morning would open
        /// day four to three finished tasks and nothing to do.
        /// </summary>
        [Test]
        public void WorkDoneBeforeADayUnlocksDoesNotCountTowardIt()
        {
            var data = new SaveData();
            Rig first = Running(data);
            first.Festival.Sync();
            first.Goals.Record(Goals.Upgrades, 50L);
            Assert.That(first.Festival.TaskDone(0), Is.True, "birinci günün görevi bitti");

            // Three days later, same save, same festival.
            Rig later = Running(data, daysAgo: 3L);
            int dayFour = FoundryFestival.TaskSlot(3, 0);

            Assert.That(later.Festival.Day, Is.EqualTo(3));
            Assert.That(later.Festival.TaskUnlocked(dayFour), Is.True);
            Assert.That(later.Festival.TaskProgress(dayFour), Is.Zero,
                        "dördüncü gün, açıldığı andan itibaren sayar");

            later.Goals.Record(Goals.Upgrades, 12L);
            Assert.That(later.Festival.TaskDone(dayFour), Is.True, "açıldıktan sonraki iş sayılır");
        }

        [Test]
        public void OneDeltaFeedsEveryOpenDayThatAsksForIt()
        {
            var data = new SaveData();
            Rig rig = Running(data, daysAgo: 1L);
            rig.Festival.Sync();

            rig.Goals.Record(Goals.Upgrades, 8L);

            Assert.That(rig.Festival.TaskDone(FoundryFestival.TaskSlot(0, 0)), Is.True, "1. gün: 5 yükseltme");
            Assert.That(rig.Festival.TaskDone(FoundryFestival.TaskSlot(1, 0)), Is.True, "2. gün: 8 yükseltme");
        }

        [Test]
        public void NothingUnlocksBeforeTheWindowOpens()
        {
            long now = Now();
            var data = new SaveData();
            Rig rig = Build(data, now + Day, now + 8L * Day);

            Assert.That(rig.Festival.Available, Is.True, "yaklaşan şenlik geri sayımıyla görünür");
            Assert.That(rig.Festival.Phase, Is.EqualTo(LiveEvents.Phase.Upcoming));
            Assert.That(rig.Festival.TaskUnlocked(0), Is.False);

            rig.Goals.Record(Goals.Upgrades, 50L);
            Assert.That(rig.Festival.TaskProgress(0), Is.Zero, "açılmamış pencereye ilerleme yazılamaz");
        }

        /// <summary>
        /// The counters freeze at the closing second. Without the gate this is the leak: progress read
        /// live off lifetime totals would keep finishing tasks for weeks, because the player keeps
        /// buying upgrades.
        /// </summary>
        [Test]
        public void ProgressFreezesWhenTheWindowCloses()
        {
            long now = Now();
            var data = new SaveData();
            Rig during = Running(data);
            during.Festival.Sync();
            during.Goals.Record(Goals.Upgrades, 3L);
            Assert.That(during.Festival.TaskProgress(0), Is.EqualTo(3L));

            Rig after = Build(data, now - 9L * Day, now - 2L * Day);
            after.Goals.Record(Goals.Upgrades, 500L);

            Assert.That(after.Festival.Phase, Is.EqualTo(LiveEvents.Phase.Closed));
            Assert.That(after.Festival.TaskProgress(0), Is.EqualTo(3L), "kapanmış şenlik ilerlemez");
            Assert.That(after.Festival.TaskDone(0), Is.False);
        }

        // ---- claiming -----------------------------------------------------------------------------

        [Test]
        public void ATaskPaysOnceAndOnlyOnce()
        {
            var data = new SaveData();
            Rig rig = Running(data);
            rig.Festival.Sync();
            rig.Goals.Record(Goals.Upgrades, 5L);

            long reward = rig.Festival.TaskAt(0).Gems;
            long before = rig.Wallet.Gems;

            Assert.That(rig.Festival.ClaimTask(0), Is.True);
            Assert.That(rig.Wallet.Gems, Is.EqualTo(before + reward));
            Assert.That(rig.Festival.ClaimTask(0), Is.False, "ikinci çağrı ödeme yetkisi vermez");
            Assert.That(rig.Wallet.Gems, Is.EqualTo(before + reward));
        }

        [Test]
        public void AnUnfinishedTaskPaysNothing()
        {
            var data = new SaveData();
            Rig rig = Running(data);
            rig.Festival.Sync();
            rig.Goals.Record(Goals.Upgrades, 4L);

            Assert.That(rig.Festival.CanClaimTask(0), Is.False);
            Assert.That(rig.Festival.ClaimTask(0), Is.False);
            Assert.That(rig.Wallet.Gems, Is.Zero);
        }

        [Test]
        public void ClaimAllTakesEverythingOwedAndThenNothing()
        {
            var data = new SaveData();
            Rig rig = Running(data);
            rig.Festival.Sync();
            rig.Goals.Record(Goals.Upgrades, 5L);
            rig.Goals.Record(Goals.Contracts, 1L);
            rig.Goals.Record(Goals.Repairs, 2L);

            long expected = rig.Festival.TaskAt(0).Gems + rig.Festival.TaskAt(1).Gems
                          + rig.Festival.TaskAt(2).Gems;

            Assert.That(rig.Festival.ClaimAll(), Is.EqualTo(3));
            Assert.That(rig.Wallet.Gems, Is.EqualTo(expected));
            Assert.That(rig.Festival.ClaimAll(), Is.Zero);
            Assert.That(rig.Wallet.Gems, Is.EqualTo(expected), "boş bir 'hepsini al' bir şey ödemez");
        }

        /// <summary>A chest counts FINISHED tasks, not claimed ones — the player who never opens the
        /// screen until the last day still earned it.</summary>
        [Test]
        public void AChestOpensOnFinishedTasksNotClaimedOnes()
        {
            var data = new SaveData();
            Rig rig = Running(data, daysAgo: 1L);
            rig.Festival.Sync();
            rig.Goals.Record(Goals.Upgrades, 8L);
            rig.Goals.Record(Goals.Contracts, 1L);
            rig.Goals.Record(Goals.Repairs, 2L);

            Assert.That(rig.Festival.Points, Is.GreaterThanOrEqualTo(rig.Festival.MilestoneAt(0).Points));
            Assert.That(rig.Festival.MilestoneEarned(0), Is.True, "hiçbir görev alınmadan da sandık açılır");
            Assert.That(rig.Festival.MilestoneClaimed(0), Is.False);
        }

        [Test]
        public void AChestPaysOnceAndOnlyOnce()
        {
            var data = new SaveData();
            Rig rig = Running(data, daysAgo: 1L);
            rig.Festival.Sync();
            rig.Goals.Record(Goals.Upgrades, 8L);
            rig.Goals.Record(Goals.Contracts, 1L);
            rig.Goals.Record(Goals.Repairs, 2L);

            long before = rig.Wallet.Gems;
            long reward = rig.Festival.MilestoneAt(0).Gems;

            Assert.That(rig.Festival.ClaimMilestone(0), Is.True);
            Assert.That(rig.Wallet.Gems, Is.EqualTo(before + reward));
            Assert.That(rig.Festival.ClaimMilestone(0), Is.False);
            Assert.That(rig.Wallet.Gems, Is.EqualTo(before + reward));
        }

        [Test]
        public void AnUnearnedChestPaysNothing()
        {
            var data = new SaveData();
            Rig rig = Running(data);
            rig.Festival.Sync();

            Assert.That(rig.Festival.MilestoneEarned(FoundryFestival.MilestoneCount - 1), Is.False);
            Assert.That(rig.Festival.ClaimMilestone(FoundryFestival.MilestoneCount - 1), Is.False);
            Assert.That(rig.Wallet.Gems, Is.Zero);
        }

        /// <summary>FIVE_LAYERS.md R3 for timed content: the window closes, the reward does not.</summary>
        [Test]
        public void AFinishedTaskStaysClaimableAfterTheFestivalCloses()
        {
            long now = Now();
            var data = new SaveData();
            Rig during = Running(data);
            during.Festival.Sync();
            during.Goals.Record(Goals.Upgrades, 5L);
            during.Festival.Sync();     // the HUD's quarter-second bank, before the window shuts

            Rig after = Build(data, now - 9L * Day, now - 2L * Day);

            Assert.That(after.Festival.Phase, Is.EqualTo(LiveEvents.Phase.Closed));
            Assert.That(after.Festival.CanClaimTask(0), Is.True, "kapanmış şenlik kazanılmış ödülü yutamaz");
            Assert.That(after.Festival.ClaimTask(0), Is.True);
            Assert.That(after.Wallet.Gems, Is.EqualTo(after.Festival.TaskAt(0).Gems));
        }

        /// <summary>What keeps the board honest: a finished festival that still owes something is the
        /// one the hub shows, ahead of an announcement for the next one.</summary>
        [Test]
        public void AClosedFestivalThatStillOwesIsTheOneOnShow()
        {
            long now = Now();
            var data = new SaveData();
            Rig during = Running(data);
            during.Festival.Sync();
            during.Goals.Record(Goals.Upgrades, 5L);
            during.Festival.Sync();

            Rig after = Build(data, now - 9L * Day, now - 2L * Day);

            Assert.That(after.Festival.Available, Is.True);
            Assert.That(after.Festival.PendingCount(), Is.GreaterThan(0));

            after.Festival.ClaimAll();
            Assert.That(after.Festival.PendingCount(), Is.Zero);
        }

        // ---- the save -----------------------------------------------------------------------------

        /// <summary>The asymmetry the lifecycle layer promises, seen from the module: retuned content
        /// drops the counters and keeps every reward already handed over.</summary>
        [Test]
        public void AVersionBumpDropsProgressButNeverAClaim()
        {
            var data = new SaveData();
            Rig first = Running(data);
            first.Festival.Sync();
            first.Goals.Record(Goals.Upgrades, 5L);
            Assert.That(first.Festival.ClaimTask(0), Is.True);

            Rig second = Running(data, version: 2);

            Assert.That(second.Festival.TaskClaimed(0), Is.True, "ödenmiş ödül geri alınmaz");
            Assert.That(second.Festival.TaskProgress(0), Is.Zero, "yeni sürümde sayaçlar sıfırdan");
            Assert.That(second.Festival.ClaimTask(0), Is.False);
        }

        /// <summary>After a version bump the cursors are cleared with everything else, so they reseed
        /// against the current totals rather than paying the whole lifetime out at once.</summary>
        [Test]
        public void AVersionBumpReseedsRatherThanPayingOutTheLifetime()
        {
            var data = new SaveData();
            Rig first = Running(data);
            first.Festival.Sync();
            first.Goals.Record(Goals.Upgrades, 500L);

            Rig second = Running(data, version: 2);

            Assert.That(second.Festival.TaskProgress(0), Is.Zero);
            second.Goals.Record(Goals.Upgrades, 5L);
            Assert.That(second.Festival.TaskDone(0), Is.True);
        }

        /// <summary>
        /// A festival that has not opened costs an untouched save nothing. A RUNNING one does write a
        /// row on first sight — seeding the cursors is a write, and it has to be, or the delta has
        /// nothing to be measured from. One row per festival is the whole cost.
        /// </summary>
        [Test]
        public void AnUpcomingFestivalWritesNothingToTheSave()
        {
            long now = Now();
            var data = new SaveData();
            Rig rig = Build(data, now + Day, now + 8L * Day);

            Assert.That(rig.Festival.Available, Is.True);
            Assert.That(rig.Festival.PendingCount(), Is.Zero);
            Assert.That(data.liveEvents, Is.Empty, "açılmamış etkinlik kayda satır yazmaz");
        }

        [Test]
        public void TheFestivalAddsNoFieldOfItsOwnToTheSave()
        {
            var data = new SaveData();
            Rig rig = Running(data);
            rig.Festival.Sync();
            rig.Goals.Record(Goals.Upgrades, 5L);
            rig.Festival.ClaimTask(0);

            Assert.That(data.liveEvents.Count, Is.EqualTo(1), "her şey tek bir etkinlik satırında durur");
            Assert.That(data.liveEvents[0].progress.Length, Is.EqualTo(FoundryFestival.Slots));
            Assert.That(data.liveEvents[0].claimed.Length, Is.EqualTo(FoundryFestival.Slots));
        }

        // ---- the schedule row ---------------------------------------------------------------------

        /// <summary>A row authored with too few slots is refused outright. Clipped, its last days
        /// would silently never count and never say why.</summary>
        [Test]
        public void AShortScheduleRowIsNotAFestival()
        {
            var data = new SaveData();
            Rig rig = Running(data, slots: 8);

            Assert.That(rig.Festival.Available, Is.False);
            Assert.That(rig.Festival.PendingCount(), Is.Zero);
            Assert.That(rig.Festival.ClaimAll(), Is.Zero);
        }

        [Test]
        public void AnEventOfAnotherKindIsNotAFestival()
        {
            var data = new SaveData();
            long now = Now();
            var wallet = new WalletService(data.wallet);
            var goals = new GoalService(data, wallet, null, new TimeService());
            var other = new LiveEvents.Definition
            {
                Id = "baska",
                Kind = FoundryFestival.Kind + 1,
                StartUnix = now - Day,
                EndUnix = now + Day,
                ConfigVersion = 1,
                Slots = FoundryFestival.Slots,
            };
            var events = new LiveEventService(data, new List<LiveEvents.Definition> { other }, new TimeService());
            var festival = new FoundryFestivalService(events, goals, wallet, FoundryFestival.Tuning.Default,
                                                      null, null, null, data, null, new TimeService());

            Assert.That(festival.Available, Is.False, "şenlik modülü yalnızca kendi türünü sahiplenir");
        }

        [Test]
        public void NoScheduleIsNoFestival()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var goals = new GoalService(data, wallet, null, new TimeService());
            var events = new LiveEventService(data, new List<LiveEvents.Definition>(), new TimeService());
            var festival = new FoundryFestivalService(events, goals, wallet, FoundryFestival.Tuning.Default,
                                                      null, null, null, data, null, new TimeService());

            Assert.That(festival.Available, Is.False);
            Assert.That(festival.Day, Is.Zero);
            Assert.That(festival.PendingCount(), Is.Zero);
            Assert.That(festival.ClaimAll(), Is.Zero);
        }
    }
}
