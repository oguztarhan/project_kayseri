using System;
using System.Collections.Generic;
using Game.Systems;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public sealed class RatingPromptServiceTests
    {
        private sealed class Clock
        {
            public long Now;
            public long Read() => Now;
        }

        private sealed class MemoryStore : IRatingPromptStore
        {
            private readonly Dictionary<string, string> _values = new Dictionary<string, string>();
            public int GetInt(string key, int fallback) => _values.TryGetValue(key, out string v) && int.TryParse(v, out int n) ? n : fallback;
            public long GetLong(string key, long fallback) => _values.TryGetValue(key, out string v) && long.TryParse(v, out long n) ? n : fallback;
            public void SetInt(string key, int value) => _values[key] = value.ToString();
            public void SetLong(string key, long value) => _values[key] = value.ToString();
            public void Save() { }
        }

        [Test]
        public void FirstRequestNeedsThreeSessionsTwoDaysAndThreeContracts()
        {
            var clock = new Clock { Now = 1_000_000L };
            var store = new MemoryStore();
            var first = new RatingPromptService(clock.Read, store);
            first.RecordContractSuccess();
            first.RecordContractSuccess();

            clock.Now += RatingPromptService.RequiredInstallAgeSeconds;
            _ = new RatingPromptService(clock.Read, store);
            var thirdSession = new RatingPromptService(clock.Read, store);
            bool requested = false;
            thirdSession.Requested += () => requested = true;

            Assert.That(thirdSession.RecordContractSuccess(), Is.True);
            Assert.That(requested, Is.True);
        }

        [Test]
        public void PostponeBlocksForExactlyTwoDaysThenRequestsAgain()
        {
            var clock = new Clock { Now = 2_000_000L };
            var store = new MemoryStore();
            var service = EligibleService(store, clock);
            Assert.That(service.RecordContractSuccess(), Is.True);
            service.Postpone();

            clock.Now += RatingPromptService.PostponeSeconds - 1L;
            Assert.That(service.TryRequestPostponed(), Is.False);
            clock.Now += 1L;
            Assert.That(service.TryRequestPostponed(), Is.True);
        }

        [Test]
        public void CompletingPermanentlySuppressesRequests()
        {
            var clock = new Clock { Now = 3_000_000L };
            var store = new MemoryStore();
            var service = EligibleService(store, clock);
            Assert.That(service.RecordContractSuccess(), Is.True);
            service.Complete();
            clock.Now += RatingPromptService.PostponeSeconds * 5L;

            Assert.That(service.IsCompleted, Is.True);
            Assert.That(service.RecordContractSuccess(), Is.False);
            Assert.That(service.TryRequestPostponed(), Is.False);
        }

        private static RatingPromptService EligibleService(MemoryStore store, Clock clock)
        {
            var one = new RatingPromptService(clock.Read, store);
            one.RecordContractSuccess();
            one.RecordContractSuccess();
            clock.Now += RatingPromptService.RequiredInstallAgeSeconds;
            _ = new RatingPromptService(clock.Read, store);
            return new RatingPromptService(clock.Read, store);
        }
    }
}
