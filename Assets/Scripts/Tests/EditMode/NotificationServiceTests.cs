using System.Collections.Generic;
using Game.Core;
using Game.Systems;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public sealed class NotificationServiceTests
    {
        private sealed class Sink : INotifications
        {
            public readonly List<LocalNotificationRequest> Requests = new List<LocalNotificationRequest>();
            public void Schedule(LocalNotificationRequest request) => Requests.Add(request);
            public void CancelAll() => Requests.Clear();
            public void RequestPermission() { }
            public void RefreshOpenedTarget() { }
            public string PollOpenedTarget() => null;
        }

        [Test]
        public void RepairNotificationHasNoDeepLink()
        {
            var data = new SaveData();
            var time = new TimeService();
            var row = new IslandCondition { id = "coal", station = Maintenance.NewConditions() };
            row.repairEnd = new long[Maintenance.Stations];
            row.repairSecs = new int[Maintenance.Stations];
            row.repairFrom = new float[Maintenance.Stations];
            row.station[IslandEconomy.Mine] = 0.6f;
            row.repairEnd[IslandEconomy.Mine] = time.NowUnix() + 4L * 3600L;
            data.conditions.Add(row);
            var sink = new Sink();
            new NotificationService(data, null, time, sink).ScheduleAway();

            Assert.That(sink.Requests.Exists(n => n.Id == "repair:coal" && n.Target == string.Empty), Is.True);
        }

        [Test]
        public void WaitingContractRewardLinksToContractScreen()
        {
            var data = new SaveData();
            data.contract.initialized = true;
            data.contract.state = (int)ContractService.PortState.Reward;
            var time = new TimeService();
            var contract = new ContractService(new WalletService(data.wallet), null, data, time);
            var sink = new Sink();
            new NotificationService(data, null, time, sink, contract).ScheduleAway();

            Assert.That(sink.Requests.Exists(n => n.Id == "contract:reward" && n.Target == "contract"), Is.True);
        }

        [Test]
        public void NewDayNotificationLinksToDailyGoalsTab()
        {
            var data = new SaveData();
            var sink = new Sink();
            new NotificationService(data, null, new TimeService(), sink, null, 1).ScheduleAway();

            Assert.That(sink.Requests.Exists(n => n.Id == "away:NewDay" && n.Target == "goals:daily"), Is.True);
        }

        /// <summary>A coin service on the real clock, with <paramref name="collect"/> coins already taken this cycle.</summary>
        private static ShopCoinService Coins(SaveData data, int collect)
        {
            var coins = new ShopCoinService(new WalletService(data.wallet), ShopCoins.Tuning.Default, data,
                () => System.DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            for (int i = 0; i < collect; i++)
            {
                Assert.That(coins.Tick(1e6d), Is.EqualTo(ShopCoinService.TickResult.Spawned));
                Assert.That(coins.TryCollect(100d, out _), Is.True);
            }
            return coins;
        }

        [Test]
        public void ACoinCollectorIsToldOnceWhenTheNextCycleOpens()
        {
            var data = new SaveData { tutorialStep = TutorialProgress.StepDone };
            ShopCoinService coins = Coins(data, 1);
            var sink = new Sink();
            new NotificationService(data, null, new TimeService(), sink, null, 0, true, coins).ScheduleAway();

            List<LocalNotificationRequest> lines = sink.Requests.FindAll(n => n.Id == "shopcoins:cycle");
            Assert.That(lines.Count, Is.EqualTo(1));
            LocalNotificationRequest line = lines[0];
            // Quiet hours can only push it later, never earlier than the turn of the cycle.
            Assert.That(line.AfterSeconds, Is.GreaterThanOrEqualTo(coins.SecondsToReset));
            Assert.That(line.Title, Is.EqualTo("bildirim.sikke_baslik"));
            Assert.That(line.Message, Is.EqualTo("bildirim.sikke"));
            Assert.That(line.Target, Is.EqualTo(string.Empty));
        }

        [Test]
        public void NobodyIsCalledBackForCoinsTheyHaveNotBeenCollecting()
        {
            var fresh = new SaveData { tutorialStep = TutorialProgress.StepDone };
            var none = new Sink();
            new NotificationService(fresh, null, new TimeService(), none, null, 0, true, Coins(fresh, 0)).ScheduleAway();
            Assert.That(none.Requests.Exists(n => n.Id == "shopcoins:cycle"), Is.False);

            var learning = new SaveData { tutorialStep = TutorialProgress.StepDone };
            ShopCoinService coins = Coins(learning, 1);
            learning.tutorialStep = 0;
            var locked = new Sink();
            new NotificationService(learning, null, new TimeService(), locked, null, 0, true, coins).ScheduleAway();
            Assert.That(locked.Requests.Exists(n => n.Id == "shopcoins:cycle"), Is.False);
        }

        [Test]
        public void SwitchedOffOfflineEarningsAreNeverQuoted()
        {
            var config = UnityEngine.ScriptableObject.CreateInstance<Game.Data.OfflineConfig>();
            try
            {
                var data = new SaveData { incomeRatePerSec = 100d };
                var quoted = new Sink();
                new NotificationService(data, config, new TimeService(), quoted, null, 1).ScheduleAway();
                var silent = new Sink();
                new NotificationService(data, config, new TimeService(), silent, null, 1, false).ScheduleAway();

                // No localization service is registered here, so a line reads as its key: the quoting keys
                // carry the figure, their "_sade" twins do not.
                System.Predicate<LocalNotificationRequest> quotes = n =>
                    n.Message == "bildirim.dolduruyor" || n.Message == "bildirim.dolduruyor_gec" ||
                    n.Message == "bildirim.doldu";
                Assert.That(quoted.Requests.Exists(quotes), Is.True);
                Assert.That(silent.Requests.Exists(quotes), Is.False);
                Assert.That(silent.Requests.Count, Is.EqualTo(quoted.Requests.Count), "the lines still go out");
            }
            finally { UnityEngine.Object.DestroyImmediate(config); }
        }
    }
}
