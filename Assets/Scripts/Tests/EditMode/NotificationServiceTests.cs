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
        public void RepairNotificationCarriesItsOwnIslandTarget()
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

            Assert.That(sink.Requests.Exists(n => n.Id == "repair:coal" && n.Target == "island:coal"), Is.True);
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
    }
}
