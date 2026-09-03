using System;
using System.Collections.Generic;
using NUnit.Framework;
using Game.Core;
using Game.Data;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>
    /// The live-event lifecycle. Every window question is asked of <see cref="LiveEvents"/> directly,
    /// where the clock is an argument — which is what lets the exact second an event opens and closes
    /// be asserted instead of waited for. The service tests then use windows measured from the real
    /// clock, because that is the only thing <see cref="TimeService"/> reads.
    /// </summary>
    public class LiveEventsTests
    {
        private const long Day = 86400L;

        private static LiveEvents.Definition Def(long start, long end, int slots = 3,
                                                 int version = 1, int minIslands = 0, string id = "fuar")
            => new LiveEvents.Definition
            {
                Id = id,
                Kind = 0,
                StartUnix = start,
                EndUnix = end,
                ConfigVersion = version,
                Slots = slots,
                MinIslands = minIslands,
            };

        private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // ---- the window ---------------------------------------------------------------------------

        [Test]
        public void BeforeTheStartItIsUpcoming()
        {
            var d = Def(1000L, 2000L);
            Assert.That(LiveEvents.PhaseAt(d, 999L), Is.EqualTo(LiveEvents.Phase.Upcoming));
        }

        /// <summary>The start second is INSIDE the window — the boundary the countdown reaches zero on.</summary>
        [Test]
        public void TheStartSecondIsAlreadyActive()
        {
            var d = Def(1000L, 2000L);
            Assert.That(LiveEvents.PhaseAt(d, 1000L), Is.EqualTo(LiveEvents.Phase.Active));
        }

        /// <summary>The end second is OUTSIDE it. Half-open, so two events scheduled back to back
        /// cannot both be live for the one second they share.</summary>
        [Test]
        public void TheEndSecondIsAlreadyClosed()
        {
            var d = Def(1000L, 2000L);
            Assert.That(LiveEvents.PhaseAt(d, 1999L), Is.EqualTo(LiveEvents.Phase.Active));
            Assert.That(LiveEvents.PhaseAt(d, 2000L), Is.EqualTo(LiveEvents.Phase.Closed));
        }

        [Test]
        public void BackToBackEventsNeverOverlap()
        {
            var first = Def(1000L, 2000L, id: "bir");
            var second = Def(2000L, 3000L, id: "iki");

            for (long t = 999L; t <= 3001L; t++)
            {
                bool bothLive = LiveEvents.PhaseAt(first, t) == LiveEvents.Phase.Active
                             && LiveEvents.PhaseAt(second, t) == LiveEvents.Phase.Active;
                Assert.That(bothLive, Is.False, "iki etkinlik " + t + ". saniyede birlikte açık.");
            }
        }

        [Test]
        public void CountdownsNeverGoNegative()
        {
            var d = Def(1000L, 2000L);
            Assert.That(LiveEvents.SecondsUntilStart(d, 5000L), Is.Zero);
            Assert.That(LiveEvents.SecondsLeft(d, 5000L), Is.Zero);
            Assert.That(LiveEvents.SecondsLeft(d, 500L), Is.Zero, "açılmadan önce 'kalan süre' sıfırdır");
            Assert.That(LiveEvents.SecondsUntilStart(d, 400L), Is.EqualTo(600L));
            Assert.That(LiveEvents.SecondsLeft(d, 1400L), Is.EqualTo(600L));
        }

        // ---- malformed definitions ----------------------------------------------------------------

        [Test]
        public void AnEmptyIdIsNotSchedulable()
        {
            Assert.That(LiveEvents.IsWellFormed(Def(1000L, 2000L, id: "")), Is.False);
            Assert.That(LiveEvents.IsWellFormed(Def(1000L, 2000L, id: null)), Is.False);
        }

        [Test]
        public void AnEndBeforeItsStartIsNotSchedulable()
        {
            Assert.That(LiveEvents.IsWellFormed(Def(2000L, 1000L)), Is.False);
            Assert.That(LiveEvents.IsWellFormed(Def(2000L, 2000L)), Is.False, "sıfır uzunluk da geçersiz");
        }

        [Test]
        public void SlotCountsOutsideTheBoundAreNotSchedulable()
        {
            Assert.That(LiveEvents.IsWellFormed(Def(1000L, 2000L, slots: 0)), Is.False);
            Assert.That(LiveEvents.IsWellFormed(Def(1000L, 2000L, slots: LiveEvents.MaxSlots + 1)), Is.False);
            Assert.That(LiveEvents.IsWellFormed(Def(1000L, 2000L, slots: LiveEvents.MaxSlots)), Is.True);
        }

        // ---- eligibility --------------------------------------------------------------------------

        [Test]
        public void AnEventGatedOnIslandsDoesNotAccrueForANewPlayer()
        {
            var d = Def(1000L, 2000L, minIslands: 3);
            Assert.That(LiveEvents.Accruing(d, 1500L, 1), Is.False);
            Assert.That(LiveEvents.Accruing(d, 1500L, 3), Is.True);
        }

        [Test]
        public void EligibilityDoesNotOpenAClosedWindow()
        {
            var d = Def(1000L, 2000L, minIslands: 0);
            Assert.That(LiveEvents.Accruing(d, 2500L, 99), Is.False);
        }

        // ---- the date parser ----------------------------------------------------------------------

        [Test]
        public void TheConfigParsesItsOwnDateFormat()
        {
            Assert.That(LiveEventConfig.TryParseUtc("2026-10-01 00:00", out long unix), Is.True);
            Assert.That(DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime,
                        Is.EqualTo(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)));
        }

        /// <summary>Exact rather than lenient: a permissive parse reads this as a date too, just not
        /// the one that was meant.</summary>
        [Test]
        public void TheConfigRefusesAnyOtherDateFormat()
        {
            Assert.That(LiveEventConfig.TryParseUtc("01-10-2026 00:00", out _), Is.False);
            Assert.That(LiveEventConfig.TryParseUtc("2026/10/01 00:00", out _), Is.False);
            Assert.That(LiveEventConfig.TryParseUtc("", out _), Is.False);
            Assert.That(LiveEventConfig.TryParseUtc(null, out _), Is.False);
        }

        // ---- the service: progress ----------------------------------------------------------------

        private static SaveData FreshSave() => new SaveData();

        private static LiveEventService Running(SaveData data, int slots = 3, int version = 1)
        {
            long now = Now();
            var defs = new List<LiveEvents.Definition> { Def(now - Day, now + Day, slots, version) };
            return new LiveEventService(data, defs, new TimeService());
        }

        [Test]
        public void AnUntouchedSaveCarriesNoEventRows()
        {
            SaveData data = FreshSave();
            LiveEventService svc = Running(data);

            Assert.That(svc.Progress("fuar", 0), Is.Zero);
            Assert.That(data.liveEvents, Is.Empty, "bakmak kayda satır eklememeli");
        }

        [Test]
        public void RecordingAccruesWhileTheWindowIsOpen()
        {
            SaveData data = FreshSave();
            LiveEventService svc = Running(data);

            Assert.That(svc.Record("fuar", 0, 5L), Is.True);
            Assert.That(svc.Record("fuar", 0, 3L), Is.True);
            Assert.That(svc.Progress("fuar", 0), Is.EqualTo(8L));
        }

        [Test]
        public void RecordingIsRefusedOnceTheWindowHasClosed()
        {
            long now = Now();
            SaveData data = FreshSave();
            var defs = new List<LiveEvents.Definition> { Def(now - 2 * Day, now - Day) };
            var svc = new LiveEventService(data, defs, new TimeService());

            Assert.That(svc.PhaseOf("fuar"), Is.EqualTo(LiveEvents.Phase.Closed));
            Assert.That(svc.Record("fuar", 0, 5L), Is.False);
            Assert.That(svc.Progress("fuar", 0), Is.Zero);
        }

        [Test]
        public void RecordingIsRefusedBeforeTheWindowOpens()
        {
            long now = Now();
            SaveData data = FreshSave();
            var defs = new List<LiveEvents.Definition> { Def(now + Day, now + 2 * Day) };
            var svc = new LiveEventService(data, defs, new TimeService());

            Assert.That(svc.PhaseOf("fuar"), Is.EqualTo(LiveEvents.Phase.Upcoming));
            Assert.That(svc.Record("fuar", 0, 5L), Is.False);
        }

        [Test]
        public void RecordingAgainstAnUnknownEventOrSlotIsRefused()
        {
            SaveData data = FreshSave();
            LiveEventService svc = Running(data, slots: 2);

            Assert.That(svc.Record("yok", 0), Is.False);
            Assert.That(svc.Record("fuar", 2), Is.False, "yuva sayısı 2 ise 2. indeks yoktur");
            Assert.That(svc.Record("fuar", -1), Is.False);
            Assert.That(svc.Record("fuar", 0, 0L), Is.False, "sıfır artış kayıt değildir");
        }

        // ---- the service: claiming ----------------------------------------------------------------

        [Test]
        public void ASlotCanOnlyBeClaimedOnce()
        {
            SaveData data = FreshSave();
            LiveEventService svc = Running(data);

            Assert.That(svc.MarkClaimed("fuar", 1), Is.True);
            Assert.That(svc.MarkClaimed("fuar", 1), Is.False, "ikinci çağrı ödeme yetkisi vermez");
            Assert.That(svc.Claimed("fuar", 1), Is.True);
            Assert.That(svc.Claimed("fuar", 0), Is.False);
        }

        /// <summary>FIVE_LAYERS.md R3, as a test: the window closes, the reward does not.</summary>
        [Test]
        public void AnEarnedSlotStaysClaimableAfterTheEventCloses()
        {
            long now = Now();
            SaveData data = FreshSave();

            var live = new List<LiveEvents.Definition> { Def(now - Day, now + Day) };
            var during = new LiveEventService(data, live, new TimeService());
            Assert.That(during.Record("fuar", 0, 100L), Is.True);

            // The same save, reopened in a build where the event has ended.
            var over = new List<LiveEvents.Definition> { Def(now - 2 * Day, now - Day) };
            var after = new LiveEventService(data, over, new TimeService());

            Assert.That(after.PhaseOf("fuar"), Is.EqualTo(LiveEvents.Phase.Closed));
            Assert.That(after.MarkClaimed("fuar", 0), Is.True, "kapanmış etkinlik kazanılmış ödülü yutamaz");
            Assert.That(after.HasUnclaimed("fuar"), Is.True, "kalan iki yuva hâlâ açık");
        }

        // ---- the service: save shape --------------------------------------------------------------

        [Test]
        public void AShortSavedArrayIsGrownWithoutLosingWhatIsInIt()
        {
            SaveData data = FreshSave();
            data.liveEvents.Add(new LiveEventState
            {
                id = "fuar",
                configVersion = 1,
                progress = new[] { 7L },
                claimed = new[] { true },
            });

            LiveEventService svc = Running(data, slots: 3);

            Assert.That(svc.Progress("fuar", 0), Is.EqualTo(7L));
            Assert.That(svc.Claimed("fuar", 0), Is.True);
            Assert.That(svc.Progress("fuar", 2), Is.Zero);
            Assert.That(data.liveEvents[0].progress.Length, Is.EqualTo(3));
            Assert.That(data.liveEvents[0].claimed.Length, Is.EqualTo(3));
        }

        [Test]
        public void ANullArrayIsFilledRatherThanThrown()
        {
            SaveData data = FreshSave();
            data.liveEvents.Add(new LiveEventState { id = "fuar", configVersion = 1 });

            LiveEventService svc = Running(data, slots: 3);

            Assert.That(svc.Progress("fuar", 0), Is.Zero);
            Assert.That(svc.Claimed("fuar", 0), Is.False);
        }

        [Test]
        public void ARowForAnEventThisBuildNoLongerCarriesIsKept()
        {
            SaveData data = FreshSave();
            data.liveEvents.Add(new LiveEventState
            {
                id = "gecmis",
                configVersion = 1,
                progress = new[] { 3L },
                claimed = new[] { true },
            });

            Running(data, slots: 3);

            Assert.That(data.liveEvents.Count, Is.EqualTo(1),
                        "kaldırılan etkinlik geri gelirse talepleri unutulmuş olmamalı");
        }

        // ---- the service: version changes ---------------------------------------------------------

        [Test]
        public void ABumpedConfigVersionDropsProgressButNeverClaims()
        {
            SaveData data = FreshSave();
            data.liveEvents.Add(new LiveEventState
            {
                id = "fuar",
                configVersion = 1,
                progress = new[] { 40L, 40L, 40L },
                claimed = new[] { true, false, false },
            });

            LiveEventService svc = Running(data, slots: 3, version: 2);

            Assert.That(svc.Progress("fuar", 0), Is.Zero, "emekli hedefe göre sayılmış ilerleme düşer");
            Assert.That(svc.Progress("fuar", 1), Is.Zero);
            Assert.That(svc.Claimed("fuar", 0), Is.True, "verilmiş ödül asla ikinci kez verilemez");
            Assert.That(data.liveEvents[0].configVersion, Is.EqualTo(2));
        }

        [Test]
        public void AMatchingVersionKeepsProgress()
        {
            SaveData data = FreshSave();
            data.liveEvents.Add(new LiveEventState
            {
                id = "fuar",
                configVersion = 1,
                progress = new[] { 40L, 0L, 0L },
                claimed = new[] { false, false, false },
            });

            LiveEventService svc = Running(data, slots: 3, version: 1);

            Assert.That(svc.Progress("fuar", 0), Is.EqualTo(40L));
        }

        /// <summary>A version bump must not resurrect a claim, which is the failure a bump would
        /// otherwise cause the second time it happened.</summary>
        [Test]
        public void ClaimsSurviveTwoConsecutiveVersionBumps()
        {
            SaveData data = FreshSave();
            Running(data, slots: 3, version: 1).MarkClaimed("fuar", 2);

            Running(data, slots: 3, version: 2);
            LiveEventService third = Running(data, slots: 3, version: 3);

            Assert.That(third.Claimed("fuar", 2), Is.True);
            Assert.That(third.MarkClaimed("fuar", 2), Is.False);
        }

        // ---- the service: eligibility -------------------------------------------------------------

        [Test]
        public void CoalCountsAsAnOwnedIsland()
        {
            long now = Now();
            SaveData data = FreshSave();
            var defs = new List<LiveEvents.Definition> { Def(now - Day, now + Day, minIslands: 1) };
            var svc = new LiveEventService(data, defs, new TimeService());

            Assert.That(data.unlockedIslands, Is.Empty);
            Assert.That(svc.Visible("fuar"), Is.True, "kömür listede yoktur ama sahiplenilmiştir");
            Assert.That(svc.Accruing("fuar"), Is.True);
        }

        [Test]
        public void AnIneligiblePlayerCannotAccrue()
        {
            long now = Now();
            SaveData data = FreshSave();
            var defs = new List<LiveEvents.Definition> { Def(now - Day, now + Day, minIslands: 4) };
            var svc = new LiveEventService(data, defs, new TimeService());

            Assert.That(svc.Visible("fuar"), Is.False);
            Assert.That(svc.Record("fuar", 0, 5L), Is.False);
        }
    }
}
