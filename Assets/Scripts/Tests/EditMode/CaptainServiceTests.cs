using NUnit.Framework;
using Game.Core;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>
    /// The half of the captain roster that touches the save, plus the seam where it meets the dock.
    /// The rules are covered in CaptainsTests and the odds in CaptainCrateTests; what is tested here
    /// is the spending, the padding, and that a voyage actually pays what the roster is bought with.
    /// </summary>
    public class CaptainServiceTests
    {
        private const string Coal = "coal";
        private const double NoCeiling = 1e12d;

        private static Captains.Tuning T => Captains.Tuning.Default;
        private static CaptainCrate.Tuning C => CaptainCrate.Tuning.Default;

        /// <summary>A seeded generator, so a crate test asserts a fact rather than a coin flip.</summary>
        private static CaptainService Make(SaveData data, int seed = 12345)
            => new CaptainService(data, T, C, new System.Random(seed));

        private sealed class Terms : IIslandSaleTerms
        {
            public double BarPriceRaw { get; set; }
            public double IncomeCapPerMinuteRaw { get; set; }
            public double UpgradeTreeCostRaw { get; set; }
        }

        /// <summary>The dock, wired to a roster, exactly as GameBootstrap wires them.</summary>
        private static VoyageService Dock(out SaveData data, out MarketService market,
                                          out ForemanService foremen, out CaptainService captains)
        {
            data = new SaveData();
            var wallet = new WalletService(data.wallet);
            market = new MarketService(data, wallet, null);
            market.Register(Coal, new Terms { BarPriceRaw = 10d, IncomeCapPerMinuteRaw = NoCeiling });
            market.SetActiveIsland(Coal);
            market.Row(Coal).deliveredPerMin = 600d;
            foremen = new ForemanService(data, wallet, Foremen.Tuning.Default);
            captains = Make(data);
            return new VoyageService(data, market, foremen, wallet, new TimeService(),
                                     Voyages.Tuning.Default, captains);
        }

        private static void Sail(VoyageService dock, MarketService market)
        {
            if (dock.At(0) == null) dock.TryStart(Coal, 0);
            market.Deliver(Coal, dock.At(0).holdSize * 2d);
            dock.Tick((float)Voyages.SecondsToFill(0, Voyages.Tuning.Default) + 1f);
        }

        /// <summary>Brings whatever is at sea home and settles it.</summary>
        private static void BringHome(VoyageService dock, SaveData data)
        {
            data.voyages[0].returnsUnix = 1L;
            dock.Tick(1f);
        }

        // ---- the save contract -------------------------------------------------------------------

        [Test]
        public void ASaveFromBeforeCaptainsExistedWorks()
        {
            var data = new SaveData();
            data.captainLevels = null;
            data.captainDuplicates = null;

            CaptainService s = Make(data);
            Assert.That(data.captainLevels.Length, Is.EqualTo(Captains.Count));
            Assert.That(data.captainDuplicates.Length, Is.EqualTo(Captains.Count));
            Assert.That(s.OwnedCount, Is.Zero);
            Assert.That(s.Charts, Is.Zero);
        }

        [Test]
        public void AShortArrayIsPaddedAndKeepsWhatItHad()
        {
            // This is what makes appending a captain to the roster free.
            var data = new SaveData();
            data.captainLevels = new[] { 4, 2 };
            data.captainDuplicates = new[] { 9, 1 };

            CaptainService s = Make(data);
            Assert.That(data.captainLevels.Length, Is.EqualTo(Captains.Count));
            Assert.That(s.Level(0), Is.EqualTo(4));
            Assert.That(s.Level(1), Is.EqualTo(2));
            Assert.That(s.Duplicates(0), Is.EqualTo(9));
            for (int c = 2; c < Captains.Count; c++)
                Assert.That(s.Level(c), Is.EqualTo(Captains.NotOwned), "captain " + c);
        }

        [Test]
        public void ANullSaveIsSurvivable()
        {
            var s = new CaptainService(null, T, C);
            Assert.That(s.Charts, Is.Zero);
            Assert.That(s.OwnedCount, Is.Zero);
            Assert.That(s.TryOpen(1), Is.Null);
            Assert.That(s.TryLevelUp(0), Is.False);
            Assert.That(s.PendingCount(), Is.Zero);
            Assert.DoesNotThrow(() => s.AddCharts(50L));
        }

        // ---- charts ------------------------------------------------------------------------------

        [Test]
        public void ChartsBankAndNegativeGrantsAreIgnored()
        {
            var data = new SaveData();
            CaptainService s = Make(data);
            s.AddCharts(120L);
            s.AddCharts(-50L);
            s.AddCharts(0L);
            Assert.That(s.Charts, Is.EqualTo(120L));
        }

        // ---- the crate ---------------------------------------------------------------------------

        [Test]
        public void OpeningWithoutEnoughChartsChangesNothing()
        {
            var data = new SaveData();
            CaptainService s = Make(data);
            s.AddCharts(C.ChartCost - 1);

            Assert.That(s.CanOpen(1), Is.False);
            Assert.That(s.TryOpen(1), Is.Null);
            Assert.That(s.Charts, Is.EqualTo(C.ChartCost - 1));
            Assert.That(s.OwnedCount, Is.Zero);
            Assert.That(s.CratesOpened, Is.Zero);
        }

        [Test]
        public void OpeningSpendsTheChartsAndHandsSomebodyOver()
        {
            var data = new SaveData();
            CaptainService s = Make(data);
            s.AddCharts(C.ChartCost * 3);

            int[] got = s.TryOpen(1);
            Assert.That(got, Is.Not.Null);
            Assert.That(got.Length, Is.EqualTo(1));
            Assert.That(Captains.Exists(got[0]), Is.True);
            Assert.That(s.Charts, Is.EqualTo(C.ChartCost * 2));
            Assert.That(s.CratesOpened, Is.EqualTo(1));
            Assert.That(s.Owned(got[0]), Is.True);
            Assert.That(s.Level(got[0]), Is.EqualTo(1));
        }

        [Test]
        public void TheFirstCopyIsTheCaptainAndEveryOneAfterIsADuplicate()
        {
            // A crate that paid a new player "1 duplicate of a captain you do not have" would be
            // paying them in something they cannot look at.
            var data = new SaveData();
            CaptainService s = Make(data);
            s.AddCharts(C.ChartCost * 400);

            int target = -1;
            for (int i = 0; i < 400 && target < 0; i++)
            {
                int[] got = s.TryOpen(1);
                if (s.Duplicates(got[0]) > 0) target = got[0];
            }

            Assert.That(target, Is.Not.EqualTo(-1), "400 pulls produced no duplicate at all");
            Assert.That(s.Level(target), Is.EqualTo(1), "a duplicate must not level a captain on its own");
            Assert.That(s.Duplicates(target), Is.GreaterThan(0));
        }

        [Test]
        public void ABulkOpenCostsTheBulkPriceAndPaysTheBulkCount()
        {
            var data = new SaveData();
            CaptainService s = Make(data);
            s.AddCharts(C.BulkChartCost);

            int[] got = s.TryOpen(C.BulkCount);
            Assert.That(got, Is.Not.Null);
            Assert.That(got.Length, Is.EqualTo(C.BulkCount));
            Assert.That(s.Charts, Is.Zero);
            Assert.That(s.CratesOpened, Is.EqualTo(C.BulkCount));
        }

        [Test]
        public void ABulkOpenAlwaysContainsAnEpic()
        {
            // The pity counters advance across the batch exactly as they would across ten presses.
            for (int seed = 1; seed <= 40; seed++)
            {
                var data = new SaveData();
                CaptainService s = Make(data, seed);
                s.AddCharts(C.BulkChartCost);

                int[] got = s.TryOpen(C.BulkCount);
                bool any = false;
                for (int i = 0; i < got.Length; i++)
                    if (Captains.RankOf(got[i]) >= Captains.Grade.Epic) any = true;
                Assert.That(any, Is.True, "seed " + seed);
            }
        }

        [Test]
        public void ThePityCountersSurviveInTheSave()
        {
            var data = new SaveData();
            CaptainService s = Make(data);
            s.AddCharts(C.ChartCost * 5);
            s.TryOpen(5);

            Assert.That(data.crateSinceEpic, Is.EqualTo(s.SinceEpic));
            Assert.That(data.crateSinceLegendary, Is.EqualTo(s.SinceLegendary));

            // A second service over the same save reads the same counters — a pity that reset on
            // launch would be a pity the player could farm by closing the app.
            CaptainService reloaded = Make(data);
            Assert.That(reloaded.SinceEpic, Is.EqualTo(s.SinceEpic));
            Assert.That(reloaded.SinceLegendary, Is.EqualTo(s.SinceLegendary));
        }

        // ---- levelling ---------------------------------------------------------------------------

        [Test]
        public void LevellingSpendsExactlyTheDuplicatesItQuoted()
        {
            var data = new SaveData();
            CaptainService s = Make(data);
            data.captainLevels[0] = 1;

            int need = s.DuplicatesNeeded(0);
            Assert.That(need, Is.GreaterThan(0));

            data.captainDuplicates[0] = need - 1;
            Assert.That(s.CanLevel(0), Is.False);
            Assert.That(s.TryLevelUp(0), Is.False);

            data.captainDuplicates[0] = need + 3;
            Assert.That(s.TryLevelUp(0), Is.True);
            Assert.That(s.Level(0), Is.EqualTo(2));
            Assert.That(s.Duplicates(0), Is.EqualTo(3));
        }

        [Test]
        public void ACaptainYouDoNotOwnCannotBeLevelled()
        {
            var data = new SaveData();
            CaptainService s = Make(data);
            data.captainDuplicates[0] = 9999;
            Assert.That(s.DuplicatesNeeded(0), Is.Zero);
            Assert.That(s.CanLevel(0), Is.False);
            Assert.That(s.TryLevelUp(0), Is.False);
        }

        [Test]
        public void TheCeilingHolds()
        {
            var data = new SaveData();
            CaptainService s = Make(data);
            data.captainLevels[0] = Captains.MaxLevel;
            data.captainDuplicates[0] = 9999;

            Assert.That(s.DuplicatesNeeded(0), Is.Zero);
            Assert.That(s.TryLevelUp(0), Is.False);
            Assert.That(s.Level(0), Is.EqualTo(Captains.MaxLevel));
        }

        [Test]
        public void PendingCountIsWhatTheBadgeShows()
        {
            var data = new SaveData();
            CaptainService s = Make(data);
            Assert.That(s.PendingCount(), Is.Zero);

            data.captainLevels[0] = 1;
            data.captainDuplicates[0] = s.DuplicatesNeeded(0);
            Assert.That(s.PendingCount(), Is.EqualTo(1));
        }

        // ---- the dock ----------------------------------------------------------------------------

        [Test]
        public void AVoyagePaysChartsIntoTheRoster()
        {
            // Charts are earned by sailing and by nothing else — that is what keeps the collection
            // sealed off from the cash economy.
            SaveData data; MarketService market; ForemanService foremen; CaptainService captains;
            VoyageService dock = Dock(out data, out market, out foremen, out captains);

            Sail(dock, market);
            BringHome(dock, data);

            int owed = data.voyages[0].payoutCharts;
            Assert.That(owed, Is.GreaterThan(0), "a voyage that sailed must bring charts home");

            long before = captains.Charts;
            dock.TryClaim(0);
            Assert.That(captains.Charts, Is.EqualTo(before + owed),
                        "the dock paid a different number of charts than the voyage was carrying");
        }

        [Test]
        public void ACaptainWhoWasNeverPulledCannotBePutAboard()
        {
            SaveData data; MarketService market; ForemanService foremen; CaptainService captains;
            VoyageService dock = Dock(out data, out market, out foremen, out captains);

            Assert.That(dock.CaptainAvailable(0), Is.False);
            dock.TryStart(Coal, 0, -1, 0);
            Assert.That(dock.At(0).captain, Is.EqualTo(-1), "somebody who does not exist was pressed into the job");

            data.captainLevels[0] = 1;
            Assert.That(dock.CaptainAvailable(0), Is.True);
            Assert.That(dock.TrySetCaptain(0, 0), Is.True);
            Assert.That(dock.At(0).captain, Is.Zero);
        }

        [Test]
        public void ACaptainAlreadyAtSeaCannotSailTwice()
        {
            SaveData data; MarketService market; ForemanService foremen; CaptainService captains;
            VoyageService dock = Dock(out data, out market, out foremen, out captains);
            data.captainLevels[0] = 1;
            data.shipLevels[Voyages.Berths] = 1;          // a second berth to try it from

            dock.TryStart(Coal, 0, -1, 0);
            Assert.That(captains.Busy(0), Is.True);
            Assert.That(dock.CaptainAvailable(0), Is.False);

            dock.TryStart(Coal, 0, -1, 0);
            Assert.That(dock.At(1).captain, Is.EqualTo(-1));
        }

        [Test]
        public void TheCaptainIsFixedOnceTheShipHasSailed()
        {
            // The same rule the foreman follows: a crew list settled after the outcome is not a
            // decision, it is a look at the answer first.
            SaveData data; MarketService market; ForemanService foremen; CaptainService captains;
            VoyageService dock = Dock(out data, out market, out foremen, out captains);
            data.captainLevels[0] = 1;

            dock.TryStart(Coal, 0);
            Sail(dock, market);
            Assert.That(dock.At(0).sailedUnix, Is.GreaterThan(0L));
            Assert.That(dock.TrySetCaptain(0, 0), Is.False);
        }

        [Test]
        public void AQuartermasterBringsHomeMoreChartsThanNobody()
        {
            int qm = -1;
            for (int i = 0; i < Captains.Count; i++)
                if (Captains.RoleOf(i) == Captains.Quartermaster) { qm = i; break; }
            Assert.That(qm, Is.Not.EqualTo(-1));

            SaveData a; MarketService ma; ForemanService fa; CaptainService ca;
            VoyageService bare = Dock(out a, out ma, out fa, out ca);
            bare.TryStart(Coal, 0);
            Sail(bare, ma);
            BringHome(bare, a);
            int plain = a.voyages[0].payoutCharts;

            SaveData b; MarketService mb; ForemanService fb; CaptainService cb;
            VoyageService crewed = Dock(out b, out mb, out fb, out cb);
            b.captainLevels[qm] = Captains.MaxLevel;
            crewed.TryStart(Coal, 0, -1, qm);
            Sail(crewed, mb);
            BringHome(crewed, b);

            Assert.That(b.voyages[0].payoutCharts, Is.GreaterThan(plain));
        }

        [Test]
        public void APurserAimsCardsAtTheForemanFurthestBehind()
        {
            int purser = -1;
            for (int i = 0; i < Captains.Count; i++)
                if (Captains.RoleOf(i) == Captains.Purser) { purser = i; break; }
            Assert.That(purser, Is.Not.EqualTo(-1));

            SaveData data; MarketService market; ForemanService foremen; CaptainService captains;
            VoyageService dock = Dock(out data, out market, out foremen, out captains);

            // Two hired foremen, one clearly further along than the other.
            data.foremanLevels[IslandEconomy.Train] = 5;
            data.foremanLevels[IslandEconomy.Storage] = 1;
            data.captainLevels[purser] = Captains.MaxLevel;

            dock.TryStart(Coal, 0, -1, purser);
            Sail(dock, market);
            BringHome(dock, data);
            dock.TryClaim(0);

            Assert.That(data.foremanDuplicates[IslandEconomy.Storage], Is.GreaterThan(0),
                        "the purser did not aim at the foreman furthest behind");
        }

        [Test]
        public void APurserIsNeverInertOnTheShortRoutes()
        {
            // The floor that exists because this test failed without it. A tier-0 voyage pays one
            // card and a Common purser's share of it is 0.4, which rounds to nothing — so the role
            // did exactly nothing on the only route a new player has open.
            int common = -1;
            for (int i = 0; i < Captains.Count; i++)
                if (Captains.RoleOf(i) == Captains.Purser && Captains.RankOf(i) == Captains.Grade.Common)
                { common = i; break; }
            Assert.That(common, Is.Not.EqualTo(-1));

            SaveData data; MarketService market; ForemanService foremen; CaptainService captains;
            VoyageService dock = Dock(out data, out market, out foremen, out captains);
            data.foremanLevels[IslandEconomy.Storage] = 1;
            data.captainLevels[common] = 1;                 // level 1, the weakest purser there is

            dock.TryStart(Coal, 0, -1, common);
            Sail(dock, market);
            BringHome(dock, data);
            Assert.That(data.voyages[0].payoutCards, Is.EqualTo(1), "the premise: a tier-0 hold pays one card");

            dock.TryClaim(0);
            Assert.That(data.foremanDuplicates[IslandEconomy.Storage], Is.EqualTo(1),
                        "the one card a short route pays was not placed");
        }

        [Test]
        public void APurserMovesWhereCardsLandAndNeverHowMany()
        {
            // The count is the balance; the aim is not. This is why the purser needed no balance pass.
            int purser = -1;
            for (int i = 0; i < Captains.Count; i++)
                if (Captains.RoleOf(i) == Captains.Purser) { purser = i; break; }

            SaveData a; MarketService ma; ForemanService fa; CaptainService ca;
            VoyageService bare = Dock(out a, out ma, out fa, out ca);
            a.foremanLevels[IslandEconomy.Train] = 3;
            bare.TryStart(Coal, 0);
            Sail(bare, ma);
            BringHome(bare, a);
            int plain = a.voyages[0].payoutCards;

            SaveData b; MarketService mb; ForemanService fb; CaptainService cb;
            VoyageService crewed = Dock(out b, out mb, out fb, out cb);
            b.foremanLevels[IslandEconomy.Train] = 3;
            b.captainLevels[purser] = Captains.MaxLevel;
            crewed.TryStart(Coal, 0, -1, purser);
            Sail(crewed, mb);
            BringHome(crewed, b);

            Assert.That(b.voyages[0].payoutCards, Is.EqualTo(plain));
        }

        [Test]
        public void ADockWithNoRosterBehavesExactlyAsItDidBefore()
        {
            // CaptainService is an optional collaborator, so every construction site that predates it
            // still works — including the fifty voyage tests.
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var market = new MarketService(data, wallet, null);
            market.Register(Coal, new Terms { BarPriceRaw = 10d, IncomeCapPerMinuteRaw = NoCeiling });
            market.SetActiveIsland(Coal);
            market.Row(Coal).deliveredPerMin = 600d;
            var foremen = new ForemanService(data, wallet, Foremen.Tuning.Default);
            var dock = new VoyageService(data, market, foremen, wallet, new TimeService(),
                                         Voyages.Tuning.Default);

            dock.TryStart(Coal, 0);
            Sail(dock, market);
            BringHome(dock, data);
            Assert.DoesNotThrow(() => dock.TryClaim(0));
            Assert.That(dock.CaptainAvailable(0), Is.False);
            Assert.That(dock.TrySetCaptain(0, 0), Is.False);
        }
    }
}
