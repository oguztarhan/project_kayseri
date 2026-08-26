using Game.Systems;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public sealed class ContractPersistenceTests
    {
        [Test]
        public void RewardAndOffersSurviveServiceRecreation()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var first = new ContractService(wallet, null, data, new TimeService());
            first.Seed(1000d);
            first.Tick(61f, 1000d);
            first.Tick(15f, 1000d);
            Assert.That(first.HasOffers, Is.True);
            Assert.That(first.Accept(ContractService.NormalTier, "COAL"), Is.True);
            first.ReportProcessed(first.TargetUnits);
            first.Tick(0.1f, 1000d);
            Assert.That(first.Claimable, Is.True);

            var restored = new ContractService(wallet, null, data, new TimeService());
            Assert.That(restored.Claimable, Is.True);
            Assert.That(restored.TargetUnits, Is.EqualTo(first.TargetUnits));
            Assert.That(restored.RewardGems, Is.EqualTo(first.RewardGems));
        }

        [Test]
        public void ActiveContractKeepsRemainingPlayTimeAcrossRecreation()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var first = new ContractService(wallet, null, data, new TimeService());
            first.Tick(61f, 500d);
            first.Tick(15f, 500d);
            Assert.That(first.Accept(ContractService.EasyTier, "COAL"), Is.True);
            first.Tick(5f, 500d);
            float left = first.SecondsLeft;

            var restored = new ContractService(wallet, null, data, new TimeService());
            Assert.That(restored.IsRunning, Is.True);
            Assert.That(restored.SecondsLeft, Is.EqualTo(left).Within(0.01f));
        }

        [Test]
        public void ExpiredAwayDeadlineRestoresAsOffersReady()
        {
            var data = new SaveData();
            data.contract.initialized = true;
            data.contract.state = (int)ContractService.PortState.Away;
            data.contract.stateEndUnix = new TimeService().NowUnix() - 1L;
            var service = new ContractService(new WalletService(data.wallet), null, data, new TimeService());
            Assert.That(service.HasOffers, Is.True);
        }
    }
}
