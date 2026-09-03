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

        /// <summary>Rolls a board and returns the service sitting on it.</summary>
        private static ContractService Docked(SaveData data, WalletService wallet,
                                              ForemanService foremen = null)
        {
            var service = new ContractService(wallet, null, data, new TimeService(), foremen);
            service.Tick(61f, 1000d);
            service.Tick(15f, 1000d);
            Assert.That(service.HasOffers, Is.True);
            return service;
        }

        [Test]
        public void OfferIdentitySurvivesServiceRecreation()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var first = Docked(data, wallet);

            var ids = new int[ContractService.TierCount];
            for (int tier = 0; tier < ContractService.TierCount; tier++)
                ids[tier] = first.GetOffer(tier).Id;

            var restored = new ContractService(wallet, null, data, new TimeService());
            for (int tier = 0; tier < ContractService.TierCount; tier++)
            {
                ContractService.Offer before = first.GetOffer(tier);
                ContractService.Offer after = restored.GetOffer(tier);
                Assert.That(after.Id, Is.EqualTo(before.Id));
                Assert.That(after.Tier, Is.EqualTo(tier));
                Assert.That(after.Cards, Is.EqualTo(before.Cards));
            }

            // Ids have to be distinct or matching a tap against one proves nothing.
            Assert.That(ids[0], Is.Not.EqualTo(ids[1]));
            Assert.That(ids[1], Is.Not.EqualTo(ids[2]));
            Assert.That(ids[0], Is.Not.EqualTo(ids[2]));
            Assert.That(ids[0], Is.GreaterThan(0));
        }

        [Test]
        public void ABoardRestoredFromASaveWithoutIdentityGetsItStamped()
        {
            // Exactly the shape a save written before offers carried identity comes back in: three rows
            // with real numbers, no ids, no tiers and no card counts. Bumping the save version would
            // wipe it instead of repairing it, so the board has to be stampable in place.
            var data = new SaveData();
            data.contract.initialized = true;
            data.contract.state = (int)ContractService.PortState.Offering;
            data.contract.streak = 5;
            for (int tier = 0; tier < ContractService.TierCount; tier++)
                data.contract.offers.Add(new ContractOfferSave
                {
                    units = 100d * (tier + 1), seconds = 600f, cash = 500d, gems = 2L,
                });

            var service = new ContractService(new WalletService(data.wallet), null, data,
                                              new TimeService());
            Assert.That(service.HasOffers, Is.True);

            var seen = new System.Collections.Generic.HashSet<int>();
            for (int tier = 0; tier < ContractService.TierCount; tier++)
            {
                ContractService.Offer o = service.GetOffer(tier);
                Assert.That(o.Tier, Is.EqualTo(tier));
                Assert.That(o.Id, Is.GreaterThan(0));
                Assert.That(seen.Add(o.Id), Is.True, "two restored offers were stamped the same id");
                Assert.That(o.Cards, Is.GreaterThan(0));
                Assert.That(o.Units, Is.EqualTo(100d * (tier + 1)));
            }
        }

        [Test]
        public void AcceptIsRefusedWhenTheCardInTheSlotIsNotTheOnePressed()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var service = Docked(data, wallet);
            int real = service.GetOffer(ContractService.NormalTier).Id;

            // A tap carrying the id of a card that is no longer in the slot signs nothing.
            Assert.That(service.Accept(ContractService.NormalTier, real + 1000, "COAL"), Is.False);
            Assert.That(service.State, Is.EqualTo(ContractService.PortState.Offering));

            // Nor does one carrying another slot's id, which is the case a tier-only check would miss.
            Assert.That(service.Accept(ContractService.NormalTier,
                                       service.GetOffer(ContractService.EasyTier).Id, "COAL"), Is.False);
            Assert.That(service.State, Is.EqualTo(ContractService.PortState.Offering));

            Assert.That(service.Accept(ContractService.NormalTier, real, "COAL"), Is.True);
            Assert.That(service.State, Is.EqualTo(ContractService.PortState.Active));
        }

        [Test]
        public void AnAcceptWithNoIdStillSignsTheSlot()
        {
            var data = new SaveData();
            var service = Docked(data, new WalletService(data.wallet));
            Assert.That(service.Accept(ContractService.HardTier, 0, "COAL"), Is.True);
            Assert.That(service.State, Is.EqualTo(ContractService.PortState.Active));
        }

        [Test]
        public void TheClaimPaysTheCardCountTheOfferPromised()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var foremen = new ForemanService(data, wallet, Game.Core.Foremen.Tuning.Default);
            var service = Docked(data, wallet, foremen);

            int promised = service.GetOffer(ContractService.NormalTier).Cards;
            Assert.That(promised, Is.GreaterThan(0));

            Assert.That(service.Accept(ContractService.NormalTier, "COAL"), Is.True);
            service.ReportProcessed(service.TargetUnits);
            service.Tick(0.1f, 1000d);
            Assert.That(service.Claim(), Is.True);

            // The card is a promise the player reads before choosing. Whatever the claim works out
            // later, the number they were shown is the number that has to arrive.
            Assert.That(service.LastCards, Is.EqualTo(promised));
            Assert.That(service.LastCardStation, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void AJobSignedBeforeCardsWereOnTheOfferStillPaysOnClaim()
        {
            // A contract already running when the player updated: the save has the target and the clock
            // but no offer id and no frozen card count, so the claim has nothing to read off the card
            // and must fall back rather than pay nobody.
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            data.contract.initialized = true;
            data.contract.state = (int)ContractService.PortState.Active;
            data.contract.target = 100d;
            data.contract.done = 0d;
            data.contract.secondsLeft = 600f;
            data.contract.rewardCash = 500d;
            data.contract.rewardGems = 2L;

            var foremen = new ForemanService(data, wallet, Game.Core.Foremen.Tuning.Default);
            var service = new ContractService(wallet, null, data, new TimeService(), foremen);
            Assert.That(service.IsRunning, Is.True);

            service.ReportProcessed(100d);
            service.Tick(0.1f, 1000d);
            Assert.That(service.Claim(), Is.True);
            Assert.That(service.LastCards, Is.GreaterThan(0));
            Assert.That(service.LastCardStation, Is.GreaterThanOrEqualTo(0));
        }

        /// <summary>
        /// Drives the service a second at a time, reporting <paramref name="unitsPerSecond"/> each time,
        /// so the throughput meter actually folds samples in. Ticking a whole minute in one call reports
        /// nothing and leaves the meter reading zero, which is the floor case and not this one.
        /// </summary>
        private static void Run(ContractService service, int seconds, double unitsPerSecond,
                                double cashPerMinute)
        {
            for (int i = 0; i < seconds; i++)
            {
                service.ReportProcessed(unitsPerSecond);
                service.Tick(1f, cashPerMinute);
            }
        }

        /// <summary>Runs a service up to a docked board cut against a real, measured throughput.</summary>
        private static ContractService Measured(SaveData data, WalletService wallet, double unitsPerSecond)
        {
            var service = new ContractService(wallet, null, data, new TimeService());
            Run(service, 61, unitsPerSecond, 1000d);
            Run(service, 15, unitsPerSecond, 1000d);
            Assert.That(service.HasOffers, Is.True);
            return service;
        }

        [Test]
        public void TheBoardIsReproducibleFromTheMeterItWasCutAgainst()
        {
            var data = new SaveData();
            var service = Measured(data, new WalletService(data.wallet), 1d);

            // Everything about a card has to follow from numbers the save wrote down. Cutting the board
            // again by hand out of those numbers is the proof: if a roll ever leans on something that is
            // not persisted, the second cut comes out different from the first.
            var floors = new Game.Core.ContractBoard.Floors
            {
                Units = 50d, Cash = 500d, RewardFraction = 0.45d, NormalMinutes = 10f,
            };
            var meter = new Game.Core.ContractBoard.Meter
            {
                ProcPerMinute = data.contract.boardProcPerMinute,
                CashPerMinute = data.contract.boardCashPerMinute,
                Difficulty = data.contract.difficulty,
            };
            Assert.That(meter.ProcPerMinute, Is.GreaterThan(0d), "the meter never read anything");

            var tiers = new Game.Core.ContractBoard.Tier[]
            {
                new Game.Core.ContractBoard.Tier { Rate = 0.6f, Minutes = 15f, Pay = 0.5f, Gems = 1L },
                new Game.Core.ContractBoard.Tier { Rate = 1f,   Minutes = 10f, Pay = 1f,   Gems = 2L },
                new Game.Core.ContractBoard.Tier { Rate = 1.6f, Minutes = 7f,  Pay = 2.2f, Gems = 4L },
            };
            for (int tier = 0; tier < ContractService.TierCount; tier++)
            {
                Game.Core.ContractBoard.Terms cut =
                    Game.Core.ContractBoard.Cut(tiers[tier], meter, floors);
                ContractService.Offer o = service.GetOffer(tier);
                Assert.That(o.Units, Is.EqualTo(cut.Units));
                Assert.That(o.Cash, Is.EqualTo(cut.Cash));
                Assert.That(o.Seconds, Is.EqualTo(cut.Seconds));
                Assert.That(o.Gems, Is.EqualTo(cut.Gems));
            }
        }

        [Test]
        public void TheBoardIsNotRepricedWhileTheShipWaits()
        {
            var data = new SaveData();
            var service = Docked(data, new WalletService(data.wallet));

            var before = new double[ContractService.TierCount];
            for (int tier = 0; tier < ContractService.TierCount; tier++)
                before[tier] = service.GetOffer(tier).Cash;

            // Income multiplied a hundredfold while the ship sits there. The cards must not move: the
            // player is choosing between three jobs, and three jobs priced at three different instants
            // are not a choice.
            for (int i = 0; i < 30; i++) service.Tick(1f, 100000d);

            for (int tier = 0; tier < ContractService.TierCount; tier++)
                Assert.That(service.GetOffer(tier).Cash, Is.EqualTo(before[tier]));
        }

        [Test]
        public void ABoardTheEmpireHasOutgrownIsRecut()
        {
            var data = new SaveData();
            var service = Measured(data, new WalletService(data.wallet), 1d);
            ContractService.Offer before = service.GetOffer(ContractService.NormalTier);

            // Ten times the throughput. A board asking for a minute of the old empire is not a contract
            // any more, it is a formality, so it gets cut again rather than left on the table.
            Run(service, 40, 10d, 1000d);

            ContractService.Offer after = service.GetOffer(ContractService.NormalTier);
            Assert.That(service.State, Is.EqualTo(ContractService.PortState.Offering));
            Assert.That(after.Id, Is.Not.EqualTo(before.Id));
            Assert.That(after.Units, Is.GreaterThan(before.Units));
            Assert.That(service.BoardRefreshed, Is.True);
        }

        [Test]
        public void TimePassingAloneNeverRecutsTheBoard()
        {
            // The refresh is deliberately keyed to growth rather than to age, so there is nothing here
            // for a device clock to buy. Half an hour at a flat throughput has to leave the board alone.
            var data = new SaveData();
            var service = Measured(data, new WalletService(data.wallet), 1d);
            ContractService.Offer before = service.GetOffer(ContractService.NormalTier);

            Run(service, 1800, 1d, 1000d);

            ContractService.Offer after = service.GetOffer(ContractService.NormalTier);
            Assert.That(after.Id, Is.EqualTo(before.Id));
            Assert.That(after.Units, Is.EqualTo(before.Units));
            Assert.That(service.BoardRefreshed, Is.False);
        }

        [Test]
        public void ARunningJobIsNeverRecutUnderThePlayer()
        {
            var data = new SaveData();
            var service = Measured(data, new WalletService(data.wallet), 1d);
            Assert.That(service.Accept(ContractService.NormalTier, "COAL"), Is.True);
            double target = service.TargetUnits;
            double reward = service.Reward.ToDouble();

            // The same tenfold growth that would re-cut a board on the table. A signed job is a promise
            // in both directions: the player cannot be asked for more than they agreed to.
            Run(service, 60, 10d, 1000d);

            Assert.That(service.TargetUnits, Is.EqualTo(target));
            Assert.That(service.Reward.ToDouble(), Is.EqualTo(reward));
            Assert.That(service.BoardRefreshed, Is.False);
        }

        [Test]
        public void TheRefreshLatchIsClearedOnceRead()
        {
            var data = new SaveData();
            var service = Measured(data, new WalletService(data.wallet), 1d);
            Run(service, 40, 10d, 1000d);

            Assert.That(service.ConsumeBoardRefreshed(), Is.True);
            Assert.That(service.ConsumeBoardRefreshed(), Is.False);
            Assert.That(service.BoardRefreshed, Is.False);
        }

        [Test]
        public void TheMeterABoardWasCutAgainstSurvivesServiceRecreation()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var first = Measured(data, wallet, 1d);
            double proc = data.contract.boardProcPerMinute;
            Assert.That(proc, Is.GreaterThan(0d));

            var restored = new ContractService(wallet, null, data, new TimeService());
            for (int tier = 0; tier < ContractService.TierCount; tier++)
                Assert.That(restored.GetOffer(tier).Units,
                            Is.EqualTo(first.GetOffer(tier).Units));

            // Restored with its own meter intact, the board still knows when it has been outgrown —
            // otherwise a restart would quietly make every stale board permanent.
            Run(restored, 40, 10d, 1000d);
            Assert.That(restored.BoardRefreshed, Is.True);
        }

        [Test]
        public void ASwapReplacesTheCardInPlaceWithADifferentJobOfTheSameTier()
        {
            var data = new SaveData();
            var service = Measured(data, new WalletService(data.wallet), 1d);
            ContractService.Offer before = service.GetOffer(ContractService.NormalTier);
            ContractService.Offer easy = service.GetOffer(ContractService.EasyTier);
            ContractService.Offer hard = service.GetOffer(ContractService.HardTier);

            Assert.That(service.CanSwap, Is.True);
            Assert.That(service.Swap(ContractService.NormalTier, before.Id), Is.True);

            ContractService.Offer after = service.GetOffer(ContractService.NormalTier);
            Assert.That(after.Id, Is.Not.EqualTo(before.Id));
            Assert.That(after.Tier, Is.EqualTo(ContractService.NormalTier));
            Assert.That(after.Units, Is.GreaterThan(0d));
            Assert.That(after.Seconds, Is.Not.EqualTo(before.Seconds), "a swap has to change something");
            Assert.That(after.Gems, Is.EqualTo(before.Gems));
            Assert.That(service.State, Is.EqualTo(ContractService.PortState.Offering));

            // The other two cards are not part of the swap.
            Assert.That(service.GetOffer(ContractService.EasyTier).Id, Is.EqualTo(easy.Id));
            Assert.That(service.GetOffer(ContractService.HardTier).Id, Is.EqualTo(hard.Id));
        }

        [Test]
        public void ASwapCannotRaiseTheCardsPayPerMinute()
        {
            // The one exploit a swap could open: press it until a card pays more for the same time. The
            // tier's rate and pay-per-minute are fixed by design and only the window moves, so cash per
            // second has to come out identical to the card it replaced.
            var data = new SaveData();
            var service = Measured(data, new WalletService(data.wallet), 1d);
            ContractService.Offer before = service.GetOffer(ContractService.HardTier);
            Assert.That(service.Swap(ContractService.HardTier, before.Id), Is.True);
            ContractService.Offer after = service.GetOffer(ContractService.HardTier);

            // Relative, because Seconds is a float: 7 x 1.3 minutes carries float32 rounding that a
            // double-precision cash figure does not, and that is precision, not pay.
            Assert.That(after.Cash / after.Seconds,
                        Is.EqualTo(before.Cash / before.Seconds).Within(1e-4d).Percent);
        }

        [Test]
        public void ASwapCannotRaiseThePayPerMinuteOfAFloorPricedCardEither()
        {
            // A board rolled before the meter reads anything sits on the cash floor, which is a flat
            // number: seen on a real device, a swap to a shorter window kept the whole $500 for fifteen
            // percent less time. The floor has to follow the window like everything else on the card.
            var data = new SaveData();
            var service = new ContractService(new WalletService(data.wallet), null, data, new TimeService());
            service.Tick(61f, 1d);   // a dollar a minute: every card lands on its floor
            service.Tick(15f, 1d);
            Assert.That(service.HasOffers, Is.True);
            ContractService.Offer before = service.GetOffer(ContractService.NormalTier);
            Assert.That(before.Cash, Is.EqualTo(500d), "not on the floor — the test would prove nothing");

            Assert.That(service.Swap(ContractService.NormalTier, before.Id), Is.True);
            ContractService.Offer after = service.GetOffer(ContractService.NormalTier);

            Assert.That(after.Seconds, Is.Not.EqualTo(before.Seconds));
            Assert.That(after.Cash / after.Seconds,
                        Is.EqualTo(before.Cash / before.Seconds).Within(1e-4d).Percent);
        }

        [Test]
        public void TheSwapBudgetIsSpentOncePerVisitAndSurvivesRecreation()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var first = Measured(data, wallet, 1d);
            Assert.That(first.Swap(ContractService.EasyTier, first.GetOffer(ContractService.EasyTier).Id),
                        Is.True);
            Assert.That(first.CanSwap, Is.False);
            int swapped = first.GetOffer(ContractService.EasyTier).Id;
            Assert.That(first.Swap(ContractService.EasyTier, swapped), Is.False);
            Assert.That(first.GetOffer(ContractService.EasyTier).Id, Is.EqualTo(swapped));

            // Relaunching is the obvious way to try for a second one.
            var restored = new ContractService(wallet, null, data, new TimeService());
            Assert.That(restored.CanSwap, Is.False);
            Assert.That(restored.Swap(ContractService.NormalTier,
                                      restored.GetOffer(ContractService.NormalTier).Id), Is.False);
        }

        [Test]
        public void ANewShipRefillsTheSwapBudget()
        {
            var data = new SaveData();
            var service = Measured(data, new WalletService(data.wallet), 1d);
            Assert.That(service.Swap(ContractService.EasyTier,
                                     service.GetOffer(ContractService.EasyTier).Id), Is.True);
            Assert.That(service.CanSwap, Is.False);

            Assert.That(service.Accept(ContractService.EasyTier, "COAL"), Is.True);
            service.ReportProcessed(service.TargetUnits);
            service.Tick(0.1f, 1000d);
            Assert.That(service.Claim(), Is.True);

            // Depart, cool down, sail back in: the next ship is a fresh visit.
            Run(service, 20, 1d, 1000d);
            Run(service, 61, 1d, 1000d);
            Run(service, 15, 1d, 1000d);
            Assert.That(service.HasOffers, Is.True);
            Assert.That(service.CanSwap, Is.True);
        }

        [Test]
        public void AStalenessRefreshDoesNotRefillTheSwapBudget()
        {
            var data = new SaveData();
            var service = Measured(data, new WalletService(data.wallet), 1d);
            Assert.That(service.Swap(ContractService.EasyTier,
                                     service.GetOffer(ContractService.EasyTier).Id), Is.True);

            // The empire outgrows the board and it re-cuts itself. That is the board's doing, not the
            // player spending anything, so the budget stays spent — otherwise growth buys re-rolls.
            Run(service, 40, 10d, 1000d);
            Assert.That(service.BoardRefreshed, Is.True);
            Assert.That(service.CanSwap, Is.False);
        }

        [Test]
        public void ASwapIsRefusedForACardThatIsNoLongerThere()
        {
            var data = new SaveData();
            var service = Measured(data, new WalletService(data.wallet), 1d);
            int real = service.GetOffer(ContractService.NormalTier).Id;

            Assert.That(service.Swap(ContractService.NormalTier, real + 1000), Is.False);
            Assert.That(service.CanSwap, Is.True, "a refused swap must not cost the budget");
            Assert.That(service.GetOffer(ContractService.NormalTier).Id, Is.EqualTo(real));
        }

        [Test]
        public void ASwapOutsideOfferingIsRefused()
        {
            var data = new SaveData();
            var service = new ContractService(new WalletService(data.wallet), null, data, new TimeService());
            Assert.That(service.CanSwap, Is.False);
            Assert.That(service.Swap(ContractService.NormalTier, 0), Is.False);

            Run(service, 61, 1d, 1000d);
            Run(service, 15, 1d, 1000d);
            Assert.That(service.Accept(ContractService.NormalTier, "COAL"), Is.True);
            Assert.That(service.CanSwap, Is.False);
            Assert.That(service.Swap(ContractService.EasyTier, 0), Is.False);
        }

        [Test]
        public void ASwappedCardIsReproducibleFromTheSave()
        {
            var data = new SaveData();
            var service = Measured(data, new WalletService(data.wallet), 1d);
            Assert.That(service.Swap(ContractService.NormalTier,
                                     service.GetOffer(ContractService.NormalTier).Id), Is.True);
            ContractService.Offer o = service.GetOffer(ContractService.NormalTier);

            // Same re-cut as for a rolled card, with the window shape taken from the id the save holds.
            float scale = Game.Core.ContractBoard.WindowScale(o.Id, 1f);
            Game.Core.ContractBoard.Terms cut = Game.Core.ContractBoard.Cut(
                new Game.Core.ContractBoard.Tier { Rate = 1f, Minutes = 10f * scale, Pay = 1f, Gems = 2L },
                new Game.Core.ContractBoard.Meter
                {
                    ProcPerMinute = data.contract.boardProcPerMinute,
                    CashPerMinute = data.contract.boardCashPerMinute,
                    Difficulty = data.contract.difficulty,
                },
                new Game.Core.ContractBoard.Floors
                {
                    Units = 50d, Cash = 500d, RewardFraction = 0.45d, NormalMinutes = 10f,
                },
                scale);
            Assert.That(o.Units, Is.EqualTo(cut.Units));
            Assert.That(o.Seconds, Is.EqualTo(cut.Seconds));
            Assert.That(o.Cash, Is.EqualTo(cut.Cash));
        }

        [Test]
        public void TwoSwapsInARowNeverHandBackTheSameJob()
        {
            // With a budget above one the second swap replaces an already-swapped card, and four shapes
            // means a one-in-four chance of landing on the same one — stepped past by design.
            for (int seed = 1; seed < 200; seed++)
            {
                float first = Game.Core.ContractBoard.WindowScale(seed, 1f);
                Assert.That(first, Is.Not.EqualTo(1f));
                Assert.That(Game.Core.ContractBoard.WindowScale(seed + 1, first), Is.Not.EqualTo(first));
            }
        }
    }
}
