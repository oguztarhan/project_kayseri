using System;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class NotificationSchedulePlannerTests
    {
        [Test]
        public void RepairBeatsNearbyGenericReminder()
        {
            var input = new[]
            {
                Candidate("generic", 3 * 3600, 10),
                Candidate("repair:coal", 4 * 3600, 100),
            };
            var output = new NotificationCandidate[NotificationSchedulePlanner.MaxScheduled];
            int n = NotificationSchedulePlanner.Build(new DateTime(2026, 8, 18, 9, 0, 0), input, input.Length, output);

            Assert.That(n, Is.EqualTo(1));
            Assert.That(output[0].Id, Is.EqualTo("repair:coal"));
        }

        [Test]
        public void NightRepairMovesToSevenInTheMorning()
        {
            var input = new[] { Candidate("repair:coal", 2 * 3600, 100) };
            var output = new NotificationCandidate[NotificationSchedulePlanner.MaxScheduled];
            int n = NotificationSchedulePlanner.Build(new DateTime(2026, 8, 18, 23, 0, 0), input, 1, output);

            Assert.That(n, Is.EqualTo(1));
            Assert.That(output[0].AfterSeconds, Is.EqualTo(8 * 3600));
        }

        [Test]
        public void DailyCapKeepsAtMostThree()
        {
            var input = new[]
            {
                Candidate("a", 1 * 3600, 10), Candidate("b", 4 * 3600, 10),
                Candidate("c", 7 * 3600, 10), Candidate("d", 10 * 3600, 10),
            };
            var output = new NotificationCandidate[NotificationSchedulePlanner.MaxScheduled];
            int n = NotificationSchedulePlanner.Build(new DateTime(2026, 8, 18, 8, 0, 0), input, input.Length, output);
            Assert.That(n, Is.EqualTo(3));
        }

        [Test]
        public void HighPriorityRepairReplacesAWeakerDailySlot()
        {
            var input = new[]
            {
                Candidate("a", 1 * 3600, 10), Candidate("b", 4 * 3600, 10),
                Candidate("c", 7 * 3600, 10), Candidate("repair:coal", 10 * 3600, 100),
            };
            var output = new NotificationCandidate[NotificationSchedulePlanner.MaxScheduled];
            int n = NotificationSchedulePlanner.Build(new DateTime(2026, 8, 18, 8, 0, 0), input, input.Length, output);

            Assert.That(n, Is.EqualTo(3));
            Assert.That(System.Array.Exists(output, x => x.Id == "repair:coal"), Is.True);
        }

        private static NotificationCandidate Candidate(string id, int after, int priority)
            => new NotificationCandidate { Id = id, AfterSeconds = after, Priority = priority };
    }
}
