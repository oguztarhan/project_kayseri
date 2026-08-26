using NUnit.Framework;
using Game.Core;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>
    /// The dock end to end, with no scene: bars diverted off the pads, a hold filling, a ship sailing,
    /// and cards landing on the roster. <see cref="VoyageService"/> takes plain C# collaborators the
    /// same way <see cref="MarketService"/> does, so the whole loop can be driven from here.
    ///
    /// The wall clock is not mocked — <see cref="TimeService"/> is sealed and reads the device. Where a
    /// test needs a ship to be home it reaches into the <see cref="VoyageState"/> and moves
    /// <c>returnsUnix</c> into the past, which is exactly what the passage of time would have done.
    /// </summary>
    public class VoyageTests
    {
        private const string Coal = "coal";
        private const double NoCeiling = 1e12d;

        private static Voyages.Tuning T => Voyages.Tuning.Default;

        private sealed class Terms : IIslandSaleTerms
        {
            public double BarPriceRaw { get; set; }
            public double IncomeCapPerMinuteRaw { get; set; }
            public double UpgradeTreeCostRaw { get; set; }
        }

        private static VoyageService Build(out SaveData data, out MarketService market,
                                           out ForemanService foremen, double deliveredPerMin = 600d)
        {
            WalletService wallet;
            return Build(out data, out market, out foremen, out wallet, deliveredPerMin);
        }

        private static VoyageService Build(out SaveData data, out MarketService market,
                                           out ForemanService foremen, out WalletService wallet,
                                           double deliveredPerMin = 600d)
        {
            data = new SaveData();
            wallet = new WalletService(data.wallet);
            market = new MarketService(data, wallet, null);
            market.Register(Coal, new Terms { BarPriceRaw = 10d, IncomeCapPerMinuteRaw = NoCeiling });
            market.SetActiveIsland(Coal);
            market.Row(Coal).deliveredPerMin = deliveredPerMin;
            foremen = new ForemanService(data, wallet, Foremen.Tuning.Default);
            return new VoyageService(data, market, foremen, wallet, new TimeService(), T);
        }

        /// <summary>Opens a tier-0 voyage on an empty berth, fills it, and gets it to sea.</summary>
        private static void Sail(VoyageService service, MarketService market)
        {
            if (service.At(0) == null) service.TryStart(Coal, 0);
            market.Deliver(Coal, service.At(0).holdSize * 2d);
            service.Tick((float)Voyages.SecondsToFill(0, T) + 1f);
        }

        private static int TotalCards(SaveData data)
        {
            int n = 0;
            for (int i = 0; i < data.foremanDuplicates.Length; i++) n += data.foremanDuplicates[i];
            return n;
        }

        // ---- the maths --------------------------------------------------------------------------

        [Test]
        public void HoldSize_ScalesWithDelivery_AndIsZeroWithoutAMeter()
        {
            Assert.That(Voyages.HoldSize(600d, 0, T), Is.EqualTo(600d * T.HoldMinutesBase).Within(1e-9));
            Assert.That(Voyages.HoldSize(1200d, 0, T), Is.EqualTo(2d * Voyages.HoldSize(600d, 0, T)).Within(1e-9));
            Assert.That(Voyages.HoldSize(0d, 0, T), Is.Zero, "a yard that has never shipped has no hold");
        }

        [Test]
        public void HoldSize_GrowsWithTheHoldTrack()
        {
            Assert.That(Voyages.HoldSize(600d, 1, T), Is.GreaterThan(Voyages.HoldSize(600d, 0, T)));
        }

        /// <summary>
        /// Rule 4. Both sides of the division scale with the delivery rate, so the WAIT is identical on
        /// coal and on diamond and only the bar count differs. If this ever fails, the feature has
        /// acquired an absolute bar number somewhere and will be wrong by a factor of 3.2 per ore tier.
        /// </summary>
        [Test]
        public void TimeToFill_DoesNotDependOnWhatTheIslandDelivers()
        {
            double small = Voyages.HoldSize(600d, 0, T) / Voyages.FillPerSecond(600d, 1, T);
            double large = Voyages.HoldSize(6e9d, 0, T) / Voyages.FillPerSecond(6e9d, 1, T);
            Assert.That(small, Is.EqualTo(large).Within(1e-6));
            Assert.That(small, Is.EqualTo(Voyages.SecondsToFill(0, T)).Within(1e-6));
        }

        [Test]
        public void VoyageLength_FollowsTheTierTable_AndShortensWithSpeed()
        {
            double t0 = Voyages.VoyageSeconds(0, 0, T);
            Assert.That(t0, Is.EqualTo(T.BaseVoyageMinutes * 60d).Within(1e-6));
            for (int tier = 1; tier < Voyages.TierCount; tier++)
                Assert.That(Voyages.VoyageSeconds(tier, 0, T),
                            Is.EqualTo(t0 * Voyages.DurationMult[tier]).Within(1e-6), "tier " + tier);
            Assert.That(Voyages.VoyageSeconds(0, 4, T), Is.LessThan(t0));
        }

        /// <summary>
        /// The trade the whole feature turns on: a longer route must pay MORE per unit of time, or
        /// there is no reason to ever take the risk that comes with it.
        /// </summary>
        [Test]
        public void LongerRoutes_PayBetterPerMinute()
        {
            double previous = 0d;
            for (int tier = 0; tier < Voyages.TierCount; tier++)
            {
                double perSecond = Voyages.Cards(tier, 1d, 0, 0, T) / Voyages.VoyageSeconds(tier, 0, T);
                Assert.That(perSecond, Is.GreaterThan(previous), "tier " + tier + " must beat the one below");
                previous = perSecond;
            }
        }

        [Test]
        public void Cards_ScaleWithLoad_ButAVoyageThatSailedNeverPaysNothing()
        {
            // Derived from the table rather than written out, so a balance pass retunes the numbers
            // without falsifying the rule the test is actually about.
            int top = (int)(T.CardRate * Voyages.PayoutMult[3]);
            Assert.That(Voyages.Cards(0, 1d, 0, 0, T), Is.EqualTo((int)(T.CardRate * Voyages.PayoutMult[0])));
            Assert.That(Voyages.Cards(3, 1d, 0, 0, T), Is.EqualTo(top));
            Assert.That(Voyages.Cards(3, 0.5d, 0, 0, T), Is.EqualTo(top / 2));
            Assert.That(Voyages.Cards(0, 0.01d, 0, 0, T), Is.EqualTo(1), "rounding must never hand back a wasted wait");
            Assert.That(Voyages.Cards(0, 0d, 0, 0, T), Is.Zero, "but a ship that never loaded pays nothing");
        }

        [Test]
        public void Berths_StartAtOne_AndAreCapped()
        {
            Assert.That(Voyages.BerthCount(0), Is.EqualTo(1));
            Assert.That(Voyages.BerthCount(99), Is.EqualTo(Voyages.MaxBerths));
        }

        [Test]
        public void OnlyTierZero_IsOpenToAFleetThatHasNeverSailed()
        {
            Assert.That(Voyages.TierUnlocked(0, 0), Is.True);
            for (int tier = 1; tier < Voyages.TierCount; tier++)
                Assert.That(Voyages.TierUnlocked(tier, 0), Is.False, "tier " + tier);
        }

        [Test]
        public void SailingOpensTheFurtherRoutes()
        {
            for (int tier = 0; tier < Voyages.TierCount; tier++)
            {
                int need = Voyages.TierVoyagesRequired[tier];
                Assert.That(Voyages.TierUnlocked(tier, need), Is.True, "tier " + tier + " at " + need);
                if (need > 0)
                    Assert.That(Voyages.TierUnlocked(tier, need - 1), Is.False, "tier " + tier + " early");
            }
        }

        // ---- risk ------------------------------------------------------------------------------

        [Test]
        public void TierZero_IsNeverARisk()
        {
            Assert.That(Voyages.RiskFor(0, 0, T), Is.Zero);
            Assert.That(Voyages.RiskFor(0, Foremen.MaxLevel, T), Is.Zero);
        }

        [Test]
        public void AForemanCutsRisk_ButCannotEraseTheFarReach()
        {
            double bare = Voyages.RiskFor(3, 0, T);
            double crewed = Voyages.RiskFor(3, Foremen.MaxLevel, T);
            Assert.That(bare, Is.EqualTo(Voyages.RiskChance[3]).Within(1e-9));
            Assert.That(crewed, Is.LessThan(bare));
            Assert.That(crewed, Is.GreaterThan(0d), "a route a foreman makes free is not a decision");
        }

        [Test]
        public void RiskNeverGoesNegative()
        {
            Assert.That(Voyages.RiskFor(1, Foremen.MaxLevel * 10, T), Is.Zero);
        }

        /// <summary>
        /// Two rules meet here and only one can win when the full payout is a single card: "never pay
        /// nothing" and "a loss must cost something". The floor wins, deliberately — and it costs
        /// nothing to let it, because the only route whose full payout is 1 is the coastal run, whose
        /// risk is 0. A voyage that cannot fail cannot be shortchanged by the rule that says what a
        /// failure pays. So the reduction is asserted where a failure is actually reachable.
        /// </summary>
        [Test]
        public void AFailedVoyage_StillPaysSomething_ButLess()
        {
            for (int tier = 0; tier < Voyages.TierCount; tier++)
            {
                int win = Voyages.Cards(tier, 1d, 0, 0, T);
                int lose = Voyages.CardsOnFailure(tier, 1d, 0, 0, T);
                Assert.That(lose, Is.GreaterThan(0), "tier " + tier + " must never pay nothing");

                if (Voyages.RiskChance[tier] <= 0d)
                {
                    Assert.That(lose, Is.EqualTo(win), "tier " + tier + " cannot fail, so nothing is lost");
                    continue;
                }
                Assert.That(lose, Is.LessThan(win), "tier " + tier + " must cost something to lose");
            }
        }

        /// <summary>
        /// The repair window has to be proportional to the route. A flat one punishes the cheap gamble
        /// hardest and the expensive one not at all, which is exactly backwards.
        /// </summary>
        [Test]
        public void AWreckCostsMoreTime_TheFurtherOutItHappened()
        {
            double previous = 0d;
            for (int tier = 0; tier < Voyages.TierCount; tier++)
            {
                double repair = Voyages.RepairSeconds(tier, 0, T);
                Assert.That(repair, Is.GreaterThan(previous), "tier " + tier);
                Assert.That(repair, Is.LessThan(Voyages.VoyageSeconds(tier, 0, T)),
                            "a repair must never cost more than the voyage did");
                previous = repair;
            }
        }

        // ---- V4: the dock ------------------------------------------------------------------------

        [Test]
        public void BarsCarriedToTheDock_GoIntoTheHold()
        {
            SaveData data; MarketService market; ForemanService foremen;
            VoyageService service = Build(out data, out market, out foremen);
            service.TryStart(Coal, 0);

            Assert.That(service.LoadingBerthOn(Coal), Is.Zero);
            double took = service.DepositByHand(Coal, 5d);

            Assert.That(took, Is.EqualTo(5d).Within(1e-9));
            Assert.That(service.At(0).held, Is.EqualTo(5d).Within(1e-9));
        }

        /// <summary>
        /// The hold is asked before the bar leaves the player's back. The other order drops a bar into
        /// a full ship and it is simply gone — the same rule the sell counter follows.
        /// </summary>
        [Test]
        public void AFullHold_TakesNothingMore_AndTheCarrierKeepsIt()
        {
            SaveData data; MarketService market; ForemanService foremen;
            VoyageService service = Build(out data, out market, out foremen);
            service.TryStart(Coal, 0);

            double hold = service.At(0).holdSize;
            Assert.That(service.DepositByHand(Coal, hold), Is.EqualTo(hold).Within(1e-9));
            Assert.That(service.IsAtSea(0), Is.True, "a full hold sails");
            Assert.That(service.DepositByHand(Coal, 5d), Is.Zero, "and takes nothing after that");
        }

        [Test]
        public void TheDockTakesNothing_WhenNoShipIsLoading()
        {
            SaveData data; MarketService market; ForemanService foremen;
            VoyageService service = Build(out data, out market, out foremen);

            Assert.That(service.LoadingBerthOn(Coal), Is.EqualTo(-1));
            Assert.That(service.DepositByHand(Coal, 5d), Is.Zero);
        }

        [Test]
        public void HandLoading_IsOnTopOfWhatTheYardDivertsByItself()
        {
            SaveData data; MarketService market; ForemanService foremen;
            VoyageService service = Build(out data, out market, out foremen);
            service.TryStart(Coal, 0);
            market.Deliver(Coal, 500d);

            service.Tick(1f);
            double automatic = service.At(0).held;
            Assert.That(automatic, Is.GreaterThan(0d));

            service.DepositByHand(Coal, 4d);
            Assert.That(service.At(0).held, Is.EqualTo(automatic + 4d).Within(1e-9),
                        "being there adds; not being there still works");
        }

        [Test]
        public void TheDockKnowsWhichShipCameHomeFromThisYard()
        {
            SaveData data; MarketService market; ForemanService foremen;
            VoyageService service = Build(out data, out market, out foremen);
            Assert.That(service.SettledBerthOn(Coal), Is.EqualTo(-1));

            Sail(service, market);
            service.At(0).returnsUnix = 0L;
            service.Tick(1f);

            Assert.That(service.SettledBerthOn(Coal), Is.Zero);
            service.TryClaim(0);
            Assert.That(service.SettledBerthOn(Coal), Is.EqualTo(-1));
        }

        // ---- V3: the gear loop -------------------------------------------------------------------

        /// <summary>
        /// The guardrail on berths. Four holds each taking the full share would take 1.4x of everything
        /// the island makes, the counter would sell nothing, and buying a berth would read as switching
        /// the game off.
        /// </summary>
        [Test]
        public void HoldsShareOneCappedBudget_HoweverManyBerthsAreOpen()
        {
            Assert.That(Voyages.DivertShareEach(1, T), Is.EqualTo(T.DivertShare).Within(1e-9));
            for (int loading = 1; loading <= Voyages.MaxBerths; loading++)
            {
                double total = Voyages.DivertShareEach(loading, T) * loading;
                Assert.That(total, Is.LessThan(T.MaxDivertShare + 1e-9), loading + " loading");
            }
        }

        [Test]
        public void ASecondHold_DoesNotDrainTheYardFaster()
        {
            double one = Voyages.DivertShareEach(1, T) * 1;
            double four = Voyages.DivertShareEach(4, T) * 4;
            Assert.That(four, Is.LessThan(one * 4d), "berths buy pipelining, not more diversion");
        }

        /// <summary>
        /// The V3 acceptance criterion, and the test that caught the Hold track being a trap: payout
        /// used to key off the load FRACTION alone, so a bigger hold took longer to fill and paid the
        /// same. A maxed ship came out slower than a stock one.
        /// </summary>
        [Test]
        public void AMaxedShip_OutrunsAStockOne_OnTheSameRoute()
        {
            const int max = Voyages.MaxShipLevel;
            double stockCycle = Voyages.SecondsToFill(0, 1, T) + Voyages.VoyageSeconds(0, 0, T);
            double maxedCycle = Voyages.SecondsToFill(max, 1, T) + Voyages.VoyageSeconds(0, max, T);

            double stockRate = Voyages.Cards(0, 1d, 0, 0, T) / stockCycle;
            double maxedRate = Voyages.Cards(0, 1d, max, max, T) / maxedCycle;

            Assert.That(maxedRate, Is.GreaterThan(stockRate * 2d),
                        "a fully bought fleet must be worth far more than a stock one, not merely equal");
        }

        /// <summary>
        /// Measured on the FURTHEST route, not the nearest, and that is the point rather than a dodge.
        /// A coastal run pays one card, and one card cannot be increased by a multiplier of 1.4 — the
        /// integer floor eats it. Asserting there tests rounding, not design. It is also harmless in
        /// play: nobody who has bought a maxed crew is sailing the tutorial route.
        /// </summary>
        [Test]
        public void EachTrack_PullsItsOwnWeight()
        {
            const int max = Voyages.MaxShipLevel;
            const int far = Voyages.TierCount - 1;
            int stock = Voyages.Cards(far, 1d, 0, 0, T);

            // Crew: more cards for the same cargo and the same wait.
            Assert.That(Voyages.Cards(far, 1d, 0, max, T), Is.GreaterThan(stock));
            // Hold: more cargo, and more cards for it.
            Assert.That(Voyages.Cards(far, 1d, max, 0, T), Is.GreaterThan(stock));
            // Speed: the same voyage, sooner.
            Assert.That(Voyages.VoyageSeconds(far, max, T), Is.LessThan(Voyages.VoyageSeconds(far, 0, T)));
        }

        [Test]
        public void ShipUpgrades_GetDearerEachLevel()
        {
            for (int level = 1; level <= Voyages.MaxShipLevel; level++)
                Assert.That(Voyages.ShipCost(level, T), Is.GreaterThan(Voyages.ShipCost(level - 1, T)),
                            "level " + level);
        }

        [Test]
        public void SalvageArrivesWithTheCards_AndOnlyOnClaim()
        {
            SaveData data; MarketService market; ForemanService foremen;
            VoyageService service = Build(out data, out market, out foremen);

            Sail(service, market);
            service.At(0).returnsUnix = 0L;
            service.Tick(1f);
            Assert.That(service.At(0).payoutSalvage, Is.GreaterThan(0));
            Assert.That(service.Salvage, Is.Zero, "nothing banks itself");

            int owed = service.At(0).payoutSalvage;
            service.TryClaim(0);
            Assert.That(service.Salvage, Is.EqualTo(owed));
        }

        [Test]
        public void ATrackIsBought_WithSalvage_AndTheSalvageIsSpent()
        {
            SaveData data; MarketService market; ForemanService foremen;
            VoyageService service = Build(out data, out market, out foremen);

            Assert.That(service.CanBuyShip(Voyages.Hold), Is.False, "nothing in the pot yet");
            long cost = service.SalvageCostOf(Voyages.Hold);
            data.salvage = cost;

            Assert.That(service.CanBuyShip(Voyages.Hold), Is.True);
            Assert.That(service.TryBuyShip(Voyages.Hold), Is.True);
            Assert.That(service.LevelOf(Voyages.Hold), Is.EqualTo(1));
            Assert.That(service.Salvage, Is.Zero);
        }

        [Test]
        public void TheSecondBerthIsSalvage_TheThirdAndFourthAreGems()
        {
            SaveData data; MarketService market; ForemanService foremen; WalletService wallet;
            VoyageService service = Build(out data, out market, out foremen, out wallet);

            Assert.That(service.SalvageCostOf(Voyages.Berths), Is.GreaterThan(0L));
            Assert.That(service.GemCostOf(Voyages.Berths), Is.Zero);
            data.salvage = service.SalvageCostOf(Voyages.Berths);
            Assert.That(service.TryBuyShip(Voyages.Berths), Is.True);
            Assert.That(service.BerthCount, Is.EqualTo(2));

            Assert.That(service.SalvageCostOf(Voyages.Berths), Is.Zero, "the third is not sold for salvage");
            long gems = service.GemCostOf(Voyages.Berths);
            Assert.That(gems, Is.GreaterThan(0L));
            Assert.That(service.TryBuyShip(Voyages.Berths), Is.False, "and not for nothing either");
            wallet.AddGems(gems);
            Assert.That(service.TryBuyShip(Voyages.Berths), Is.True);
            Assert.That(service.BerthCount, Is.EqualTo(3));
        }

        [Test]
        public void BerthsStopAtFour()
        {
            SaveData data; MarketService market; ForemanService foremen; WalletService wallet;
            VoyageService service = Build(out data, out market, out foremen, out wallet);
            data.shipLevels[Voyages.Berths] = Voyages.MaxBerths - 1;

            Assert.That(service.BerthCount, Is.EqualTo(Voyages.MaxBerths));
            Assert.That(service.IsShipMaxed(Voyages.Berths), Is.True);
            wallet.AddGems(1000000L);
            Assert.That(service.TryBuyShip(Voyages.Berths), Is.False);
        }

        /// <summary>The ad shortcut must resolve through the same roll a served wait does — one place
        /// decides the odds, or there are two places to get them wrong.</summary>
        [Test]
        public void FinishingEarly_SettlesThroughTheOrdinaryPath()
        {
            SaveData data; MarketService market; ForemanService foremen;
            VoyageService service = Build(out data, out market, out foremen);
            Sail(service, market);
            Assert.That(service.IsAtSea(0), Is.True);

            Assert.That(service.TryFinishNow(0), Is.True);
            Assert.That(service.IsWaiting(0), Is.True);
            Assert.That(service.At(0).payoutCards, Is.GreaterThan(0));
            Assert.That(service.Completed, Is.EqualTo(1), "and it counts, like any other voyage");
        }

        [Test]
        public void SkippingARepair_CostsGems()
        {
            SaveData data; MarketService market; ForemanService foremen; WalletService wallet;
            VoyageService service = Build(out data, out market, out foremen, out wallet);
            data.hullReadyUnix[0] = long.MaxValue / 2L;

            Assert.That(service.TryRepairNow(0), Is.False, "not for free");
            wallet.AddGems(service.RepairSkipGems);
            Assert.That(service.TryRepairNow(0), Is.True);
            Assert.That(service.BerthDamaged(0), Is.False);
            Assert.That(wallet.Gems, Is.Zero);
        }

        // ---- the service, V2 -------------------------------------------------------------------

        [Test]
        public void AFreshFleet_MaySailOnlyTheCoastalRun()
        {
            SaveData data; MarketService market; ForemanService foremen;
            VoyageService service = Build(out data, out market, out foremen);

            Assert.That(service.MaxTier(), Is.Zero);
            Assert.That(service.TryStart(Coal, 3), Is.False, "the far reach is not open yet");
            Assert.That(service.TryStart(Coal, 0), Is.True);
        }

        [Test]
        public void ARouteOpens_OnceEnoughVoyagesHaveSailed()
        {
            SaveData data; MarketService market; ForemanService foremen;
            VoyageService service = Build(out data, out market, out foremen);

            data.voyagesCompleted = Voyages.TierVoyagesRequired[1];
            Assert.That(service.TierUnlocked(1), Is.True);
            Assert.That(service.VoyagesToUnlock(1), Is.Zero);
            Assert.That(service.TryStart(Coal, 1), Is.True);
        }

        /// <summary>A voyage counts toward the ladder whether it landed or not — one bad roll must not
        /// cost the player twice.</summary>
        [Test]
        public void EveryVoyageCounts_WonOrLost()
        {
            SaveData data; MarketService market; ForemanService foremen;
            VoyageService service = Build(out data, out market, out foremen);
            Assert.That(service.Completed, Is.Zero);

            Sail(service, market);
            service.At(0).returnsUnix = 0L;
            service.Tick(1f);

            Assert.That(service.Completed, Is.EqualTo(1));
        }

        [Test]
        public void AnUnhiredForeman_IsNeverPutAboard()
        {
            SaveData data; MarketService market; ForemanService foremen;
            VoyageService service = Build(out data, out market, out foremen);

            service.TryStart(Coal, 0, IslandEconomy.Mine);       // nobody is hired yet
            Assert.That(service.At(0).foreman, Is.EqualTo(-1));
        }

        [Test]
        public void AForemanAboard_ShowsUpInTheOddsThePlayerIsQuoted()
        {
            SaveData data; MarketService market; ForemanService foremen;
            VoyageService service = Build(out data, out market, out foremen);
            data.foremanLevels[IslandEconomy.Mine] = 5;

            double bare = service.RiskFor(3, -1);
            double crewed = service.RiskFor(3, IslandEconomy.Mine);
            Assert.That(crewed, Is.LessThan(bare));
        }

        [Test]
        public void AForemanAtSea_CannotBeSentOnAnother()
        {
            SaveData data; MarketService market; ForemanService foremen;
            VoyageService service = Build(out data, out market, out foremen);
            data.foremanLevels[IslandEconomy.Mine] = 3;
            data.shipLevels[Voyages.Berths] = 1;                 // two berths, so there is a second to try

            Assert.That(service.TryStart(Coal, 0, IslandEconomy.Mine), Is.True);
            Assert.That(service.ForemanBusy(IslandEconomy.Mine), Is.True);
            Assert.That(service.TryStart(Coal, 0, IslandEconomy.Mine), Is.True, "the voyage still opens");
            Assert.That(service.At(1).foreman, Is.EqualTo(-1), "but he is not on it twice");
        }

        [Test]
        public void TheCrewList_IsFixedOnceSheHasSailed()
        {
            SaveData data; MarketService market; ForemanService foremen;
            VoyageService service = Build(out data, out market, out foremen);
            data.foremanLevels[IslandEconomy.Mine] = 3;

            service.TryStart(Coal, 0);
            Assert.That(service.TrySetForeman(0, IslandEconomy.Mine), Is.True, "settable at the dock");

            Sail(service, market);
            Assert.That(service.TrySetForeman(0, -1), Is.False, "not once the risk is real");
        }

        [Test]
        public void AWreck_PutsTheBerthOutOfUse_ThenGivesItBack()
        {
            SaveData data; MarketService market; ForemanService foremen;
            VoyageService service = Build(out data, out market, out foremen);

            // Force the loss rather than rolling for it: this is about what a failure DOES.
            Sail(service, market);
            VoyageState v = service.At(0);
            v.returnsUnix = 0L;
            service.Tick(1f);
            v.succeeded = false;
            data.hullReadyUnix[0] = long.MaxValue / 2L;          // as a real failure would have set it

            Assert.That(service.BerthDamaged(0), Is.True);
            service.TryClaim(0);
            Assert.That(service.CanStart(Coal), Is.False, "nothing sails from a wrecked berth");
            Assert.That(service.TryStart(Coal, 0), Is.False);

            data.hullReadyUnix[0] = 0L;                          // the repair window passes
            Assert.That(service.BerthDamaged(0), Is.False);
            Assert.That(service.TryStart(Coal, 0), Is.True);
        }

        // ---- the service ------------------------------------------------------------------------

        [Test]
        public void ASaveWrittenBeforeVoyagesExisted_IsPaddedRatherThanCrashing()
        {
            var data = new SaveData { voyages = null, shipLevels = null };
            var wallet = new WalletService(data.wallet);
            var market = new MarketService(data, wallet, null);
            var foremen = new ForemanService(data, wallet, Foremen.Tuning.Default);

            var service = new VoyageService(data, market, foremen, wallet, new TimeService(), T);

            Assert.That(data.voyages, Is.Not.Null);
            Assert.That(data.shipLevels.Length, Is.EqualTo(Voyages.ShipTrackCount));
            Assert.That(service.BerthCount, Is.EqualTo(1));
        }

        [Test]
        public void TheDock_RefusesAYardThatHasNeverShippedAnything()
        {
            SaveData data; MarketService market; ForemanService foremen;
            VoyageService service = Build(out data, out market, out foremen, deliveredPerMin: 0d);

            Assert.That(service.CanStart(Coal), Is.False);
            Assert.That(service.TryStart(Coal, 0), Is.False);
            Assert.That(data.voyages, Is.Empty);
        }

        /// <summary>
        /// The hold is fixed when loading starts. If it tracked the live rate, buying a station upgrade
        /// mid-load would make the progress bar the player is watching go backwards — the exact trap the
        /// ore piles fell into when they keyed off fill fraction (REMAKE_PLAN §P9).
        /// </summary>
        [Test]
        public void HoldSize_IsLockedInWhenLoadingStarts()
        {
            SaveData data; MarketService market; ForemanService foremen;
            VoyageService service = Build(out data, out market, out foremen);

            Assert.That(service.TryStart(Coal, 0), Is.True);
            double locked = service.At(0).holdSize;

            market.Row(Coal).deliveredPerMin *= 10d;
            Assert.That(service.At(0).holdSize, Is.EqualTo(locked).Within(1e-9));
        }

        [Test]
        public void OneBerth_MeansOneVoyage()
        {
            SaveData data; MarketService market; ForemanService foremen;
            VoyageService service = Build(out data, out market, out foremen);

            Assert.That(service.TryStart(Coal, 0), Is.True);
            Assert.That(service.TryStart(Coal, 0), Is.False, "there is only one berth on a fresh fleet");
            Assert.That(service.FreeBerth(), Is.EqualTo(-1));
        }

        [Test]
        public void LoadingTakesBarsOffThePads()
        {
            SaveData data; MarketService market; ForemanService foremen;
            VoyageService service = Build(out data, out market, out foremen);
            market.Deliver(Coal, 500d);
            double before = market.Stock(Coal);

            service.TryStart(Coal, 0);
            service.Tick(1f);

            Assert.That(service.At(0).held, Is.GreaterThan(0d));
            Assert.That(market.Stock(Coal), Is.LessThan(before), "the bars came off the yard, not from nowhere");
            Assert.That(market.Stock(Coal) + service.At(0).held, Is.EqualTo(before).Within(1e-6));
        }

        [Test]
        public void AStarvedYard_LoadsNothingRatherThanGoingNegative()
        {
            SaveData data; MarketService market; ForemanService foremen;
            VoyageService service = Build(out data, out market, out foremen);

            service.TryStart(Coal, 0);
            service.Tick(5f);

            Assert.That(market.Stock(Coal), Is.Zero.Within(1e-9));
            Assert.That(service.At(0).held, Is.Zero.Within(1e-9));
            Assert.That(service.IsLoading(0), Is.True, "it waits at the dock; it does not sail empty");
        }

        [Test]
        public void AFullHold_SailsOnItsOwn()
        {
            SaveData data; MarketService market; ForemanService foremen;
            VoyageService service = Build(out data, out market, out foremen);
            service.TryStart(Coal, 0);
            market.Deliver(Coal, service.At(0).holdSize * 2d);

            service.Tick((float)Voyages.SecondsToFill(0, T) + 1f);

            Assert.That(service.IsAtSea(0), Is.True);
            Assert.That(service.At(0).sailedUnix, Is.GreaterThan(0L));
            Assert.That(service.At(0).held, Is.EqualTo(service.At(0).holdSize).Within(1e-6));
        }

        [Test]
        public void APartLoadedShip_MaySailEarly_ButNotNearlyEmpty()
        {
            SaveData data; MarketService market; ForemanService foremen;
            VoyageService service = Build(out data, out market, out foremen);
            service.TryStart(Coal, 0);

            Assert.That(service.TrySail(0), Is.False, "nothing aboard at all");

            market.Deliver(Coal, service.At(0).holdSize);
            service.Tick((float)(Voyages.SecondsToFill(0, T) * 0.5d));

            Assert.That(service.HoldFraction(0), Is.GreaterThan(T.MinLaunchFraction));
            Assert.That(service.TrySail(0), Is.True);
            Assert.That(service.IsAtSea(0), Is.True);
        }

        [Test]
        public void Abandoning_PutsEveryBarBack()
        {
            SaveData data; MarketService market; ForemanService foremen;
            VoyageService service = Build(out data, out market, out foremen);
            market.Deliver(Coal, 500d);
            double before = market.Stock(Coal);

            service.TryStart(Coal, 0);
            service.Tick(2f);
            Assert.That(service.At(0).held, Is.GreaterThan(0d));

            Assert.That(service.TryAbandon(0), Is.True);
            Assert.That(market.Stock(Coal), Is.EqualTo(before).Within(1e-6));
            Assert.That(service.At(0), Is.Null);
        }

        /// <summary>
        /// A refund must not look like a lorry arriving. The delivery meter is the ONLY thing the next
        /// launch's offline grant is computed from, so routing abandoned bars through Deliver would bank
        /// them as income a second time. This is the test that pins that distinction down.
        /// </summary>
        [Test]
        public void ARefund_DoesNotRegisterAsADelivery()
        {
            SaveData data; MarketService market; ForemanService foremen;
            Build(out data, out market, out foremen);
            market.Row(Coal).deliveredPerMin = 0d;      // start the meter from nothing

            market.ReturnToStock(Coal, 600d);
            for (int i = 0; i < 30; i++) market.Tick(1f);

            Assert.That(market.Stock(Coal), Is.GreaterThan(0d), "the bars are on the pads");
            Assert.That(market.Row(Coal).deliveredPerMin, Is.Zero.Within(1e-9),
                        "but the island never delivered them");
        }

        [Test]
        public void AShipComesHome_AndItsCardsLandOnTheRoster()
        {
            SaveData data; MarketService market; ForemanService foremen;
            VoyageService service = Build(out data, out market, out foremen);
            service.TryStart(Coal, 0);
            market.Deliver(Coal, service.At(0).holdSize * 2d);
            service.Tick((float)Voyages.SecondsToFill(0, T) + 1f);
            Assert.That(service.IsAtSea(0), Is.True);

            service.At(0).returnsUnix = 0L;            // the passage of time, without the waiting
            service.Tick(1f);

            Assert.That(service.IsWaiting(0), Is.True, "home, rolled, and sitting on the dock");
            Assert.That(service.At(0).succeeded, Is.True, "tier 0 carries no risk");
            Assert.That(TotalCards(data), Is.Zero, "nothing banks itself — claiming is the player's move");

            int cards = service.TryClaim(0);
            Assert.That(cards, Is.EqualTo(Voyages.Cards(0, 1d, 0, 0, T)));
            Assert.That(TotalCards(data), Is.EqualTo(cards));
            Assert.That(service.At(0), Is.Null, "and the berth is free again");
        }

        /// <summary>Rule 3. A reward that rots while the player is at work is not a reward.</summary>
        [Test]
        public void AReturnedVoyage_WaitsForever()
        {
            SaveData data; MarketService market; ForemanService foremen;
            VoyageService service = Build(out data, out market, out foremen);
            service.TryStart(Coal, 0);
            market.Deliver(Coal, service.At(0).holdSize * 2d);
            service.Tick((float)Voyages.SecondsToFill(0, T) + 1f);
            service.At(0).returnsUnix = 0L;
            service.Tick(1f);

            int cardsWaiting = service.At(0).payoutCards;
            for (int i = 0; i < 500; i++) service.Tick(1f);

            Assert.That(service.IsWaiting(0), Is.True);
            Assert.That(service.At(0).payoutCards, Is.EqualTo(cardsWaiting));
        }
    }
}
