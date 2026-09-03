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

        [Test]
        public void TwoClaimsInTheSameFramePayOnce()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var service = new ContractService(wallet, null, data, new TimeService());
            service.Tick(61f, 1000d);
            service.Tick(15f, 1000d);
            Assert.That(service.Accept(ContractService.NormalTier, "COAL"), Is.True);
            service.ReportProcessed(service.TargetUnits);
            service.Tick(0.1f, 1000d);
            Assert.That(service.Claim(), Is.True);

            // A double tap is the whole simultaneous-claim vector here — the port runs exactly one
            // contract, so there is no second one to race. The return value alone is not the assertion:
            // a refused claim that still moved the wallet is the bug this is looking for.
            double cash = wallet.Cash.ToDouble();
            long gems = wallet.Gems;
            Assert.That(service.Claim(), Is.False);
            Assert.That(wallet.Cash.ToDouble(), Is.EqualTo(cash));
            Assert.That(wallet.Gems, Is.EqualTo(gems));
        }

        [Test]
        public void AClaimedContractIsNotClaimableAfterServiceRecreation()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var first = new ContractService(wallet, null, data, new TimeService());
            first.Tick(61f, 1000d);
            first.Tick(15f, 1000d);
            Assert.That(first.Accept(ContractService.NormalTier, "COAL"), Is.True);
            first.ReportProcessed(first.TargetUnits);
            first.Tick(0.1f, 1000d);
            Assert.That(first.Claim(), Is.True);

            double cash = wallet.Cash.ToDouble();
            long gems = wallet.Gems;
            var restored = new ContractService(wallet, null, data, new TimeService());
            Assert.That(restored.Claimable, Is.False);
            Assert.That(restored.Claim(), Is.False);
            Assert.That(wallet.Cash.ToDouble(), Is.EqualTo(cash));
            Assert.That(wallet.Gems, Is.EqualTo(gems));
        }

        [Test]
        public void ClaimBeforeTheTargetIsMetIsRefusedAndPaysNothing()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var service = new ContractService(wallet, null, data, new TimeService());
            service.Tick(61f, 1000d);
            service.Tick(15f, 1000d);
            Assert.That(service.Accept(ContractService.NormalTier, "COAL"), Is.True);
            double cash = wallet.Cash.ToDouble();
            long gems = wallet.Gems;

            service.ReportProcessed(service.TargetUnits * 0.5d);
            service.Tick(1f, 1000d);

            Assert.That(service.Claimable, Is.False);
            Assert.That(service.Claim(), Is.False);
            Assert.That(wallet.Cash.ToDouble(), Is.EqualTo(cash));
            Assert.That(wallet.Gems, Is.EqualTo(gems));
        }

        [Test]
        public void AMissedContractPaysNothingAndWalksDifficultyBack()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var service = new ContractService(wallet, null, data, new TimeService());
            service.Tick(61f, 1000d);
            service.Tick(15f, 1000d);
            Assert.That(service.Accept(ContractService.EasyTier, "COAL"), Is.True);
            double cash = wallet.Cash.ToDouble();
            long gems = wallet.Gems;

            service.Tick(service.SecondsLeft + 1f, 1000d);

            Assert.That(service.LastResult, Is.EqualTo(ContractService.Result.Failed));
            Assert.That(service.Streak, Is.EqualTo(0));
            Assert.That(service.State, Is.EqualTo(ContractService.PortState.Departing));
            Assert.That(service.Claimable, Is.False);
            Assert.That(wallet.Cash.ToDouble(), Is.EqualTo(cash));
            Assert.That(wallet.Gems, Is.EqualTo(gems));
        }

        [Test]
        public void AcceptOutsideOfferingIsRefused()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var service = new ContractService(wallet, null, data, new TimeService());
            Assert.That(service.State, Is.EqualTo(ContractService.PortState.Away));
            Assert.That(service.Accept(ContractService.NormalTier, "COAL"), Is.False);

            service.Tick(61f, 1000d);
            service.Tick(15f, 1000d);
            Assert.That(service.Accept(ContractService.NormalTier, "COAL"), Is.True);
            Assert.That(service.Accept(ContractService.EasyTier, "COAL"), Is.False);

            service.ReportProcessed(service.TargetUnits);
            service.Tick(0.1f, 1000d);
            Assert.That(service.Accept(ContractService.EasyTier, "COAL"), Is.False);

            Assert.That(service.Claim(), Is.True);
            Assert.That(service.Accept(ContractService.EasyTier, "COAL"), Is.False);
        }

        [Test]
        public void AnEmptySlotCannotBeAccepted()
        {
            var data = new SaveData();
            data.contract.initialized = true;
            data.contract.state = (int)ContractService.PortState.Offering;
            data.contract.offers.Add(new ContractOfferSave { units = 0d, seconds = 600f, cash = 500d, gems = 2L });

            var service = new ContractService(new WalletService(data.wallet), null, data, new TimeService());
            Assert.That(service.HasOffers, Is.True);
            Assert.That(service.Accept(0, "COAL"), Is.False);
        }

        [Test]
        public void AnAwayWindowAdvancesNeitherTheContractClockNorItsProgress()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var first = new ContractService(wallet, null, data, new TimeService());
            first.Tick(61f, 1000d);
            first.Tick(15f, 1000d);
            Assert.That(first.Accept(ContractService.NormalTier, "COAL"), Is.True);
            first.ReportProcessed(first.TargetUnits * 0.25d);
            first.Tick(2f, 1000d);
            float left = first.SecondsLeft;
            double done = first.DoneUnits;

            // A running job is play-time, not wall clock: a ship deadline six hours stale must not reach
            // into it. Offline earnings credit no contract units either, and that pairing is what makes
            // an away window neither punish the player nor advance the job for free.
            data.contract.stateEndUnix = new TimeService().NowUnix() - 6L * 3600L;
            var restored = new ContractService(wallet, null, data, new TimeService());

            Assert.That(restored.IsRunning, Is.True);
            Assert.That(restored.SecondsLeft, Is.EqualTo(left).Within(0.01f));
            Assert.That(restored.DoneUnits, Is.EqualTo(done).Within(0.0001d));
        }

        [Test]
        public void ABoardRolledDuringABoostIsNotPricedAtTheBoostedRate()
        {
            // The market multiplies every sale by the running boost while the smelters that have to
            // fill the job do not speed up at all, so a x2 boost reaches Tick as a doubled income. Both
            // boards below are handed what the meter would really read, and must price alike: otherwise
            // a boost ad watched a minute before the ship docks doubles every offer on the table.
            var plainData = new SaveData();
            var plain = new ContractService(new WalletService(plainData.wallet), null, plainData,
                                            new TimeService());
            plain.Tick(61f, 1000d);
            plain.Tick(15f, 1000d);

            var boostData = new SaveData();
            var boost = new BoostService(boostData, new TimeService());
            boost.AddRewardedAdBoost(2d);
            var boosted = new ContractService(new WalletService(boostData.wallet), null, boostData,
                                              new TimeService(), null, null, null, null, boost);
            boosted.Tick(61f, 2000d);
            boosted.Tick(15f, 2000d);

            Assert.That(boost.ActiveMultiplier, Is.EqualTo(2d));
            Assert.That(plain.HasOffers, Is.True);
            Assert.That(boosted.HasOffers, Is.True);
            for (int tier = 0; tier < ContractService.TierCount; tier++)
                Assert.That(boosted.GetOffer(tier).Cash,
                            Is.EqualTo(plain.GetOffer(tier).Cash).Within(0.0001d));
        }
    }
}
