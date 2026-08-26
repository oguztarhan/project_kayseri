using System;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// The quiet-hour rules are the part of the notification system that cannot be checked by looking
    /// at it: every case involves a player closing the game at an hour nobody thinks to try. These pin
    /// the four that matter, plus the two properties that must hold for all of them — nothing fires
    /// between midnight and 07:00, and nothing fires twice inside three hours.
    /// </summary>
    public class NotificationPlanTests
    {
        private const long Hour = 3600L;
        private const long DefaultCap = 8L * Hour;
        private const long NightShiftCap = 14L * Hour;   // the Gece Vardiyasi offer

        private static NotificationSlot[] Plan(int leaveHour, long cap, out int count)
        {
            var slots = new NotificationSlot[NotificationPlan.MaxSlots];
            var leave = new DateTime(2026, 8, 8, leaveHour, 0, 0);
            count = NotificationPlan.Build(leave, cap, slots);
            return slots;
        }

        [Test]
        public void DaytimeClose_KeepsTheThreeHourRhythm()
        {
            int n;
            var s = Plan(9, DefaultCap, out n);

            Assert.AreEqual(6, n);
            Assert.AreEqual(NotificationKind.Filling, s[0].Kind);
            Assert.AreEqual(3 * Hour, s[0].AfterSeconds);
            Assert.AreEqual(NotificationKind.FillingLate, s[1].Kind);
            Assert.AreEqual(6 * Hour, s[1].AfterSeconds);
            Assert.AreEqual(NotificationKind.Full, s[2].Kind);
            Assert.AreEqual(9 * Hour, s[2].AfterSeconds);
            Assert.AreEqual(NotificationKind.Idle, s[3].Kind);
            Assert.AreEqual(12 * Hour, s[3].AfterSeconds);
            Assert.AreEqual(NotificationKind.NewDay, s[4].Kind);
            Assert.AreEqual(NotificationKind.ComeBack, s[5].Kind);
        }

        [Test]
        public void ClosingAtTen_CollapsesTheNightIntoOneSevenAmMessage()
        {
            // 22:00 + 3h and + 6h both land in the night. They are not dropped: they collapse onto the
            // 09:00 slot, which already sits exactly at 07:00 — and the LAST one wins, so the player
            // wakes to "work has stopped" rather than to "the carts are filling up".
            int n;
            var s = Plan(22, DefaultCap, out n);

            Assert.AreEqual(4, n);
            Assert.AreEqual(NotificationKind.Full, s[0].Kind);
            Assert.AreEqual(9 * Hour, s[0].AfterSeconds);        // 07:00
            Assert.AreEqual(NotificationKind.Idle, s[1].Kind);
            Assert.AreEqual(12 * Hour, s[1].AfterSeconds);       // 10:00
            Assert.AreEqual(NotificationKind.NewDay, s[2].Kind);
            Assert.AreEqual(NotificationKind.ComeBack, s[3].Kind);
        }

        [Test]
        public void ClosingAtNine_DropsTheSlotThatWouldFollowTheRollUpTooSoon()
        {
            // 21:00: the first three slots are all in the night and roll up to 07:00. The 12h slot is
            // due at 09:00, two hours later — close enough to read as the game nagging, so it goes.
            int n;
            var s = Plan(21, DefaultCap, out n);

            Assert.AreEqual(3, n);
            Assert.AreEqual(NotificationKind.Full, s[0].Kind);
            Assert.AreEqual(10 * Hour, s[0].AfterSeconds);       // 07:00
            Assert.AreEqual(NotificationKind.NewDay, s[1].Kind);
            Assert.AreEqual(24 * Hour, s[1].AfterSeconds);
            Assert.AreEqual(NotificationKind.ComeBack, s[2].Kind);
        }

        [Test]
        public void EveningClose_StillGetsItsFirstNudgeThatEvening()
        {
            // 18:00: the 3h slot is at 21:00 and fires normally. Everything from 00:00 to 06:00 rolls
            // up to 07:00, by which point the player has been away thirteen hours — so the morning
            // line is the idle one, not the one about yards filling.
            int n;
            var s = Plan(18, DefaultCap, out n);

            Assert.AreEqual(4, n);
            Assert.AreEqual(NotificationKind.Filling, s[0].Kind);
            Assert.AreEqual(3 * Hour, s[0].AfterSeconds);        // 21:00
            Assert.AreEqual(NotificationKind.Idle, s[1].Kind);
            Assert.AreEqual(13 * Hour, s[1].AfterSeconds);       // 07:00
            Assert.AreEqual(NotificationKind.NewDay, s[2].Kind);
            Assert.AreEqual(NotificationKind.ComeBack, s[3].Kind);
        }

        [Test]
        public void TheFullMessageFollowsThePlayersOwnCap()
        {
            // Telling a Gece Vardiyasi owner their yards are full at hour nine would be a lie the
            // welcome-back screen then contradicts: they have six more hours of earning.
            int n;
            var basic = Plan(7, DefaultCap, out n);
            int full = IndexOf(basic, n, NotificationKind.Full);
            Assert.AreEqual(DefaultCap + Hour, basic[full].AfterSeconds);

            var perk = Plan(7, NightShiftCap, out n);
            full = IndexOf(perk, n, NotificationKind.Full);
            Assert.AreEqual(NightShiftCap + Hour, perk[full].AfterSeconds);
        }

        [Test]
        public void ACapShorterThanTheEarlyNudgesDropsThem()
        {
            // Nothing in the game produces a two-hour cap today, but the slot times are derived from a
            // value the store can move, so the degenerate end has to be defined rather than discovered.
            int n;
            var s = Plan(9, 2L * Hour, out n);

            Assert.AreEqual(4, n);
            Assert.AreEqual(NotificationKind.Full, s[0].Kind);
            Assert.AreEqual(3 * Hour, s[0].AfterSeconds);
            Assert.AreEqual(NotificationKind.Idle, s[1].Kind);
        }

        [Test]
        public void NobodyIsEverWokenUp()
        {
            for (int leaveHour = 0; leaveHour < 24; leaveHour++)
            {
                var leave = new DateTime(2026, 8, 8, leaveHour, 0, 0);
                foreach (long cap in new[] { DefaultCap, NightShiftCap, 2L * Hour })
                {
                    var slots = new NotificationSlot[NotificationPlan.MaxSlots];
                    int n = NotificationPlan.Build(leave, cap, slots);
                    for (int i = 0; i < n; i++)
                    {
                        int hour = leave.AddSeconds(slots[i].AfterSeconds).Hour;
                        Assert.GreaterOrEqual(hour, NotificationPlan.WakeHour,
                            $"cikis {leaveHour}:00, tavan {cap / Hour} sa, yuva {i} saat {hour}:00'de patliyor");
                    }
                }
            }
        }

        [Test]
        public void SlotsAlwaysMoveForwardAndNeverCrowdEachOther()
        {
            for (int leaveHour = 0; leaveHour < 24; leaveHour++)
            {
                var leave = new DateTime(2026, 8, 8, leaveHour, 0, 0);
                foreach (long cap in new[] { DefaultCap, NightShiftCap, 2L * Hour })
                {
                    var slots = new NotificationSlot[NotificationPlan.MaxSlots];
                    int n = NotificationPlan.Build(leave, cap, slots);
                    for (int i = 0; i < n; i++)
                        Assert.AreEqual(slots[i].AfterSeconds, slots[i].AwaySeconds,
                            "normal programda bildirim tam iddia ettigi anda patlamali");
                    for (int i = 1; i < n; i++)
                        Assert.GreaterOrEqual(slots[i].AfterSeconds - slots[i - 1].AfterSeconds, 3 * Hour,
                            $"cikis {leaveHour}:00, tavan {cap / Hour} sa, yuva {i} oncekine cok yakin");
                }
            }
        }

        [Test]
        public void TestSpacingFiresEverySlotWithoutRewritingAnyOfThem()
        {
            // 22:00 is the case that normally collapses four slots into one morning message, so it is
            // the one worth pinning: the bench setting has to bypass that and deliver all six.
            var slots = new NotificationSlot[NotificationPlan.MaxSlots];
            int n = NotificationPlan.Build(new DateTime(2026, 8, 8, 22, 0, 0), DefaultCap, slots, 30);

            Assert.AreEqual(6, n);
            for (int i = 0; i < n; i++)
                Assert.AreEqual(30 * (i + 1), slots[i].AfterSeconds);

            // The clock is compressed; the messages are not. Each still quotes the hour it belongs to.
            Assert.AreEqual(NotificationKind.Filling, slots[0].Kind);
            Assert.AreEqual(3 * Hour, slots[0].AwaySeconds);
            Assert.AreEqual(NotificationKind.FillingLate, slots[1].Kind);
            Assert.AreEqual(6 * Hour, slots[1].AwaySeconds);
            Assert.AreEqual(NotificationKind.Full, slots[2].Kind);
            Assert.AreEqual(9 * Hour, slots[2].AwaySeconds);
            Assert.AreEqual(NotificationKind.Idle, slots[3].Kind);
            Assert.AreEqual(NotificationKind.NewDay, slots[4].Kind);
            Assert.AreEqual(NotificationKind.ComeBack, slots[5].Kind);
            Assert.AreEqual(48 * Hour, slots[5].AwaySeconds);
        }

        [Test]
        public void AnUndersizedBufferSchedulesNothing()
        {
            var tooSmall = new NotificationSlot[NotificationPlan.MaxSlots - 1];
            Assert.AreEqual(0, NotificationPlan.Build(new DateTime(2026, 8, 8, 9, 0, 0), DefaultCap, tooSmall));
            Assert.AreEqual(0, NotificationPlan.Build(new DateTime(2026, 8, 8, 9, 0, 0), DefaultCap, null));
        }

        private static int IndexOf(NotificationSlot[] slots, int count, NotificationKind kind)
        {
            for (int i = 0; i < count; i++) if (slots[i].Kind == kind) return i;
            Assert.Fail("yuva bulunamadi: " + kind);
            return -1;
        }
    }
}
