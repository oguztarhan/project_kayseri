using System.Collections.Generic;
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
            data.conditions.Add(new IslandCondition
            {
                id = "coal",
                repairStation = -1,
                repairEndUnix = time.NowUnix() + 4L * 3600L
            });
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
    }
}
