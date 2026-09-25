using System;
using System.Collections.Generic;
using Game.Core;
using Game.Data;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class MiningShopBusinessSimulationTests
    {
        private const string BusinessId = "mining-shop.island-01-04";

        private static MiningShopState Fresh() => new MiningShopState { BusinessId = BusinessId };

        private static MiningShopBusinessSimulation Open(MiningShopState state, int available,
            List<MiningShopBusinessSimulation.Sale> receipts = null)
        {
            return new MiningShopBusinessSimulation(state, available, MiningShopBusinessSimulation.Tuning.Default,
                sale => receipts?.Add(sale));
        }

        /// <summary>The shipped tuning without the build gate, for tests about something other than building.</summary>
        private static MiningShopBusinessSimulation.Tuning Ungated()
        {
            MiningShopBusinessSimulation.Tuning tuning = MiningShopBusinessSimulation.Tuning.Default;
            tuning.BuildRequiresLevel = BenchMastery.MinLevel;
            return tuning;
        }

        private static MiningShopBusinessSimulation OpenUngated(MiningShopState state, int available,
            List<MiningShopBusinessSimulation.Sale> receipts = null)
        {
            return new MiningShopBusinessSimulation(state, available, Ungated(), sale => receipts?.Add(sale));
        }

        /// <summary>What a receipt must say: its items at the bench's price, tripled when it was perfect.</summary>
        private static double Expected(MiningShopBusinessSimulation sim, int product, MiningShopBusinessSimulation.Sale sale)
            => sale.Units * sim.UnitPrice(product) * (sale.Perfect ? BenchMastery.Tuning.Default.PerfectMultiplier : 1d);

        private static void Drain(MiningShopBusinessSimulation simulation)
        {
            int calls = 0;
            while (simulation.View.PendingSeconds > 0d && calls++ < 100) simulation.Advance(0d);
            Assert.That(simulation.View.PendingSeconds, Is.Zero);
        }

        private static void Conserved(MiningShopState owner)
        {
            MiningShopBusinessState business = owner.Business;
            Assert.That(business, Is.Not.Null);
            for (int i = 0; i < business.Lines.Count; i++)
            {
                MiningShopProductLineState line = business.Lines[i];
                int cargo = business.CarrierProductIndex == i ? business.CarrierCount : 0;
                int serving = business.Serving && business.ServiceProductIndex == i ? business.ServiceUnits : 0;
                Assert.That(line.Produced, Is.EqualTo(line.Sold + line.OutputStock + line.PickupReserved +
                    cargo + line.ShelfStock + serving), "product " + i);
                Assert.That(line.DestinationReserved, Is.EqualTo(line.PickupReserved + cargo), "reservations " + i);
            }
        }

        [Test]
        public void ProductDefinitionsUseTheRequestedOrderTimesPricesAndTableCosts()
        {
            MiningShopBusinessSimulation.Tuning tuning = MiningShopBusinessSimulation.Tuning.Default;
            CollectionAssert.AreEqual(new[]
            {
                "mining-shop.pickaxe", "mining-shop.helmet", "mining-shop.lantern", "mining-shop.bag"
            }, new[] { tuning.Products[0].ProductId, tuning.Products[1].ProductId, tuning.Products[2].ProductId, tuning.Products[3].ProductId });
            CollectionAssert.AreEqual(new[] { 10d, 20d, 40d, 60d }, new[]
            {
                tuning.Products[0].CraftSeconds, tuning.Products[1].CraftSeconds,
                tuning.Products[2].CraftSeconds, tuning.Products[3].CraftSeconds
            });
            CollectionAssert.AreEqual(new[] { 20d, 240d, 3200d, 30000d }, new[]
            {
                tuning.Products[0].UnitPrice, tuning.Products[1].UnitPrice,
                tuning.Products[2].UnitPrice, tuning.Products[3].UnitPrice
            });
            CollectionAssert.AreEqual(new[] { 0d, 3000d, 150000d, 6000000d }, new[]
            {
                tuning.Products[0].TableCost, tuning.Products[1].TableCost,
                tuning.Products[2].TableCost, tuning.Products[3].TableCost
            });
            CollectionAssert.AreEqual(new[] { 40d, 400d, 4000d, 40000d }, new[]
            {
                tuning.Products[0].FirstLevelCost, tuning.Products[1].FirstLevelCost,
                tuning.Products[2].FirstLevelCost, tuning.Products[3].FirstLevelCost
            });
            Assert.That(tuning.BuildRequiresLevel, Is.EqualTo(25));
        }

        [Test]
        public void InspectorDefaultsMatchTheFourProductBusinessContract()
        {
            var config = ScriptableObject.CreateInstance<MiningShopConfig>();
            try
            {
                MiningShopBusinessSimulation.Tuning tuning = config.ToBusinessTuning();
                MiningShopBusinessSimulation.Tuning code = MiningShopBusinessSimulation.Tuning.Default;
                Assert.That(tuning.CarrierLoad, Is.EqualTo(1));
                Assert.That(tuning.BuildRequiresLevel, Is.EqualTo(code.BuildRequiresLevel));
                for (int i = 0; i < MiningShopCampaign.ProductCount; i++)
                {
                    Assert.That(tuning.Products[i].CraftSeconds, Is.EqualTo(code.Products[i].CraftSeconds), "craft " + i);
                    Assert.That(tuning.Products[i].UnitPrice, Is.EqualTo(code.Products[i].UnitPrice), "price " + i);
                    Assert.That(tuning.Products[i].TableCost, Is.EqualTo(code.Products[i].TableCost), "table " + i);
                    Assert.That(tuning.Products[i].FirstLevelCost, Is.EqualTo(code.Products[i].FirstLevelCost), "level " + i);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(config); }
        }

        [Test]
        public void LegacyPickaxeRecordBecomesOneProductLineWithoutChangingTheOldFields()
        {
            var state = Fresh();
            state.SpeedLevel = 3;
            state.ValueLevel = 2;
            state.Crafting = true;
            state.CraftDuration = 8d;
            state.CraftRemaining = 3d;
            state.OutputStock = 1;
            state.Produced = 1;
            MiningShopBusinessSimulation simulation = Open(state, 1);
            MiningShopBusinessSimulation.ProductSnapshot pickaxe = simulation.View.ProductAt(0);
            Assert.That(pickaxe.TableBuilt, Is.True);
            Assert.That(state.Business.Lines[0].SpeedLevel, Is.EqualTo(3), "kept for the level migration to read");
            Assert.That(state.Business.Lines[0].ValueLevel, Is.EqualTo(2));
            Assert.That(pickaxe.Level, Is.EqualTo(4), "its cash buys level 3, but level 4 is the first to earn what speed 3 and value 2 did");
            Assert.That(pickaxe.CraftRemaining / pickaxe.CraftDuration, Is.EqualTo(3d / 8d).Within(1e-12),
                "the item on the bench keeps the share of its craft it had left");
            Assert.That(simulation.View.BuiltTableCount, Is.EqualTo(1));
            Assert.That(simulation.View.AvailableProductCount, Is.EqualTo(1));
            Assert.That(state.SpeedLevel, Is.EqualTo(3));
            Assert.That(state.ValueLevel, Is.EqualTo(2));
            Assert.That(state.CraftRemaining, Is.EqualTo(3d));
            Conserved(state);
        }

        [Test]
        public void TablesUnlockInOrderOnceThePreviousBenchIsLevelledAndOnlyWhenTheIslandAllowsThem()
        {
            var state = Fresh();
            MiningShopBusinessSimulation sim = Open(state, 3);
            Assert.That(sim.BuildTable(3), Is.False);
            Assert.That(sim.BuildTable(2), Is.False);
            Assert.That(sim.BuildTable(1), Is.False, "the pickaxe bench is still level 1");
            Assert.That(sim.BuyLevels(0, 23), Is.EqualTo(23));
            Assert.That(sim.BuildRequirementMet(1), Is.False, "level 24 is one short");
            Assert.That(sim.BuyLevels(0, 1), Is.EqualTo(1));
            Assert.That(sim.BuildRequirementMet(1), Is.True);
            Assert.That(sim.BuildTable(2), Is.False, "out of order");
            Assert.That(sim.BuildTable(1), Is.True);
            Assert.That(sim.BuildTable(1), Is.False);
            Assert.That(sim.BuildTable(2), Is.False, "the helmet bench is still level 1");
            Assert.That(sim.BuyLevels(1, 24), Is.EqualTo(24));
            Assert.That(sim.BuildTable(2), Is.True);
            Assert.That(sim.BuyLevels(2, 24), Is.EqualTo(24));
            Assert.That(sim.BuildRequirementMet(3), Is.False, "this island offers three benches");
            Assert.That(sim.BuildTable(3), Is.False);
            Assert.That(sim.View.BuiltTableCount, Is.EqualTo(3));
            Assert.That(sim.TableCost(1), Is.EqualTo(3000d));
            Assert.That(sim.TableCost(2), Is.EqualTo(150000d));
            Conserved(state);
        }

        [Test]
        public void LevelsStopAtTheTopAndRaiseBothPriceAndPace()
        {
            var state = Fresh();
            MiningShopBusinessSimulation sim = Open(state, 1);
            double price = sim.UnitPrice(0), craft = sim.CraftSeconds(0), cost = sim.LevelCost(0);
            Assert.That(price, Is.EqualTo(20d));
            Assert.That(craft, Is.EqualTo(10d));
            Assert.That(cost, Is.EqualTo(40d));
            Assert.That(sim.CostOfLevels(0, 3), Is.EqualTo(BenchMastery.CostOfLevels(40d, 1, 3, BenchMastery.Tuning.Default)));
            Assert.That(sim.BuyLevels(0, 0), Is.Zero);
            Assert.That(sim.BuyLevels(3, 1), Is.Zero, "not built");
            Assert.That(sim.BuyLevels(0, 1), Is.EqualTo(1));
            Assert.That(sim.UnitPrice(0), Is.GreaterThan(price));
            Assert.That(sim.CraftSeconds(0), Is.LessThan(craft));
            Assert.That(sim.BuyLevels(0, 500), Is.EqualTo(BenchMastery.MaxLevel - 2));
            Assert.That(sim.View.ProductAt(0).Level, Is.EqualTo(BenchMastery.MaxLevel));
            Assert.That(sim.View.ProductAt(0).Stars, Is.EqualTo(BenchMastery.StarCount));
            Assert.That(sim.BuyLevels(0, 1), Is.Zero);
            Assert.That(sim.LevelCost(0), Is.Zero);
            Assert.That(sim.CraftSeconds(0), Is.EqualTo(BenchMastery.Tuning.Default.MinCycleSeconds), "held at the floor");
            Conserved(state);
        }

        [Test]
        public void ASavedBusinessIsWidenedToTheCampaignAllowanceButNeverNarrowed()
        {
            var state = Fresh();
            Open(state, 1);
            Assert.That(state.Business.AvailableProductCount, Is.EqualTo(1));
            Assert.That(Open(state, 4).View.AvailableProductCount, Is.EqualTo(4));
            Assert.That(Open(state, 2).View.AvailableProductCount, Is.EqualTo(4));
        }

        [Test]
        public void TwoProductsKeepTheirOwnGoodsAndPayTheirOwnPricesThroughOneReceiptStream()
        {
            var state = Fresh();
            var receipts = new List<MiningShopBusinessSimulation.Sale>();
            MiningShopBusinessSimulation sim = OpenUngated(state, 2, receipts);
            Assert.That(sim.BuildTable(1), Is.True);
            for (int i = 0; i < 240; i++) sim.Advance(1d);

            bool pickaxe = false, helmet = false;
            long previous = 0;
            for (int i = 0; i < receipts.Count; i++)
            {
                MiningShopBusinessSimulation.Sale sale = receipts[i];
                Assert.That(sale.Sequence, Is.EqualTo(++previous));
                Assert.That(sale.Units, Is.GreaterThanOrEqualTo(1));
                if (sale.ProductId == "mining-shop.pickaxe")
                {
                    pickaxe = true;
                    Assert.That(sale.Cash, Is.EqualTo(Expected(sim, 0, sale)).Within(1e-9));
                }
                else if (sale.ProductId == "mining-shop.helmet")
                {
                    helmet = true;
                    Assert.That(sale.Cash, Is.EqualTo(Expected(sim, 1, sale)).Within(1e-9));
                }
                else Assert.Fail("Unexpected product receipt: " + sale.ProductId);
            }
            Assert.That(pickaxe, Is.True);
            Assert.That(helmet, Is.True);
            Assert.That(state.Business.Lines[0].Sold, Is.GreaterThan(0));
            Assert.That(state.Business.Lines[1].Sold, Is.GreaterThan(0));
            Conserved(state);
        }

        [Test]
        public void FourBuiltLinesShareOneCarrierAndOneSellerWithoutStarvingTheBag()
        {
            var state = Fresh();
            var receipts = new List<MiningShopBusinessSimulation.Sale>();
            MiningShopBusinessSimulation sim = OpenUngated(state, 4, receipts);
            Assert.That(sim.BuildTable(1), Is.True);
            Assert.That(sim.BuildTable(2), Is.True);
            Assert.That(sim.BuildTable(3), Is.True);
            for (int i = 0; i < 1800; i++) sim.Advance(1d);

            Assert.That(state.Business.Lines[0].Sold, Is.GreaterThan(0));
            Assert.That(state.Business.Lines[1].Sold, Is.GreaterThan(0));
            Assert.That(state.Business.Lines[2].Sold, Is.GreaterThan(0));
            Assert.That(state.Business.Lines[3].Sold, Is.GreaterThan(0), "The slow bag line must receive carrier and seller turns.");
            Assert.That(state.Business.ReceiptSequence, Is.EqualTo(receipts.Count));
            Conserved(state);
        }

        [Test]
        public void SharedSellerCannotMultiplyItsServiceCapacityWhenTablesAreAdded()
        {
            MiningShopBusinessSimulation.Tuning tuning = Ungated();
            tuning.ServiceSeconds = 5d;
            tuning.ArrivalSeconds = 0.1d;
            tuning.TravelSeconds = 0.1d;
            tuning.HandlingSeconds = 0.1d;
            tuning.OutputCapacity = 30;
            tuning.ShelfCapacityPerProduct = 30;
            tuning.CarrierLoad = 1;
            tuning.QueueCapacity = 30;
            for (int i = 0; i < tuning.Products.Length; i++) tuning.Products[i].CraftSeconds = 0.1d;
            var state = Fresh();
            var receipts = new List<MiningShopBusinessSimulation.Sale>();
            var sim = new MiningShopBusinessSimulation(state, 4, tuning, receipts.Add);
            sim.BuildTable(1);
            sim.BuildTable(2);
            sim.BuildTable(3);
            for (int i = 0; i < 300; i++) sim.Advance(0.1d);
            Assert.That(receipts.Count, Is.LessThanOrEqualTo(6), "One seller has one five-second service budget.");
            Conserved(state);
        }

        [Test]
        public void UpgradeKeepsTheSelectedProductCraftFractionAndDoesNotChangeOtherProducts()
        {
            var state = Fresh();
            MiningShopBusinessSimulation sim = OpenUngated(state, 2);
            sim.BuildTable(1);
            sim.Advance(5d);
            double fraction = sim.View.ProductAt(0).CraftRemaining / sim.View.ProductAt(0).CraftDuration;
            Assert.That(sim.BuyLevels(0, 1), Is.EqualTo(1));
            MiningShopBusinessSimulation.ProductSnapshot after = sim.View.ProductAt(0);
            Assert.That(after.CraftRemaining / after.CraftDuration, Is.EqualTo(fraction).Within(1e-9));
            Assert.That(sim.View.ProductAt(1).Level, Is.EqualTo(1));
            Conserved(state);
        }

        [Test]
        public void ReloadPreservesBuiltTablesCargoAndReceiptIdentityWithoutReplayingSales()
        {
            var state = Fresh();
            var receipts = new List<MiningShopBusinessSimulation.Sale>();
            MiningShopBusinessSimulation sim = OpenUngated(state, 2, receipts);
            sim.BuildTable(1);
            sim.BuyLevels(0, 30);
            sim.Advance(43d);
            string json = JsonUtility.ToJson(state);
            var loaded = JsonUtility.FromJson<MiningShopState>(json);
            long before = loaded.Business.ReceiptSequence;
            var resumed = OpenUngated(loaded, 2, receipts);
            Assert.That(resumed.View.ProductAt(0).Level, Is.EqualTo(31));
            Assert.That(resumed.View.ReceiptSequence, Is.EqualTo(before));
            resumed.Advance(120d);
            Assert.That(resumed.View.ReceiptSequence, Is.GreaterThan(before));
            Assert.That(loaded.Business.Lines[1].TableBuilt, Is.True);
            Conserved(loaded);
        }

        [Test]
        public void UnavailableOrCorruptProductRecordsAreRejectedWithoutClearingTheSave()
        {
            var state = Fresh();
            Open(state, 1);
            state.Business.Lines[3].TableBuilt = true;
            Assert.Throws<ArgumentException>(() => Open(state, 1));
            Assert.That(state.Business.Lines[3].TableBuilt, Is.True);

            MiningShopBusinessSimulation.Tuning bad = MiningShopBusinessSimulation.Tuning.Default;
            bad.Products[3].UnitPrice = double.NaN;
            Assert.Throws<ArgumentException>(() => new MiningShopBusinessSimulation(Fresh(), 4, bad, _ => { }));
        }

        /// <summary>
        /// The logistics promise: whatever the benches make, the carrier, shelves and customers move it all, so the
        /// shop earns exactly the sum of its benches. Perfect sales are taken out of the measurement here (they are a
        /// dice roll, tested on their own below) so this checks the carrying, not the luck.
        /// </summary>
        [Test]
        public void TheLogisticsNeverHoldABenchBackAtAnyLevelOrBenchCount()
        {
            var config = ScriptableObject.CreateInstance<MiningShopConfig>();
            try
            {
                // The code default and the Inspector default are both live somewhere: tests use the first, a
                // Bootstrap with no config asset wired runs the second. Their customer arrivals differ.
                var tunings = new[] { Ungated(), config.ToBusinessTuning() };
                tunings[1].BuildRequiresLevel = BenchMastery.MinLevel;
                int[] levels = { 1, 10, 25, 50, 75, 100 };
                foreach (MiningShopBusinessSimulation.Tuning tuning in tunings)
                    for (int built = 1; built <= MiningShopCampaign.ProductCount; built++)
                        foreach (int level in levels)
                        {
                            double plain = 0d;
                            double perfectMultiplier = tuning.Mastery.PerfectMultiplier;
                            var simulation = new MiningShopBusinessSimulation(Fresh(), MiningShopCampaign.ProductCount, tuning,
                                sale => plain += sale.Perfect ? sale.Cash / perfectMultiplier : sale.Cash);
                            for (int table = 1; table < built; table++) Assert.That(simulation.BuildTable(table), Is.True);
                            for (int table = 0; table < built; table++)
                                Assert.That(simulation.BuyLevels(table, level - 1), Is.EqualTo(level - 1));

                            double formula = 0d;
                            for (int table = 0; table < built; table++)
                                formula += BenchMastery.IncomePerSecond(tuning.Products[table].UnitPrice,
                                    tuning.Products[table].CraftSeconds, level, tuning.Mastery);

                            for (int second = 0; second < 1800; second++) simulation.Advance(1d);
                            double settled = plain;
                            for (int second = 0; second < 7200; second++) simulation.Advance(1d);
                            double measured = (plain - settled) / 7200d;

                            Assert.That(measured, Is.EqualTo(formula).Within(1).Percent,
                                "arrival " + tuning.ArrivalSeconds + "s, built " + built + ", level " + level);
                        }
            }
            finally { UnityEngine.Object.DestroyImmediate(config); }
        }

        [Test]
        public void SteadyStateRateIsTheBenchesWithThePerfectSaleAverage()
        {
            var state = Fresh();
            MiningShopBusinessSimulation sim = OpenUngated(state, 4);
            sim.BuildTable(1);
            sim.BuyLevels(0, 49);
            BenchMastery.Tuning m = BenchMastery.Tuning.Default;
            double expected = BenchMastery.IncomePerSecond(20d, 10d, 50, m) * BenchMastery.PerfectAverage(3, m) +
                              BenchMastery.IncomePerSecond(240d, 20d, 1, m) * BenchMastery.PerfectAverage(0, m);
            Assert.That(sim.SteadyStateRate(), Is.EqualTo(expected).Within(1e-9));
        }

        [Test]
        public void PerfectSalesLandAtTheirChanceAndAReloadNeitherRerollsNorRepeatsThem()
        {
            // Five stars: 9 %. Two copies of the same business, one saved and reloaded halfway, must roll alike.
            var straight = new List<MiningShopBusinessSimulation.Sale>();
            var stateA = Fresh();
            MiningShopBusinessSimulation a = OpenUngated(stateA, 1, straight);
            a.BuyLevels(0, 99);
            var reloaded = new List<MiningShopBusinessSimulation.Sale>();
            var stateB = Fresh();
            MiningShopBusinessSimulation b = OpenUngated(stateB, 1, reloaded);
            b.BuyLevels(0, 99);

            for (int i = 0; i < 20000; i++) a.Advance(1d);
            for (int i = 0; i < 10000; i++) b.Advance(1d);
            var loaded = JsonUtility.FromJson<MiningShopState>(JsonUtility.ToJson(stateB));
            MiningShopBusinessSimulation resumed = OpenUngated(loaded, 1, reloaded);
            for (int i = 0; i < 10000; i++) resumed.Advance(1d);

            Assert.That(reloaded.Count, Is.EqualTo(straight.Count));
            int perfect = 0;
            for (int i = 0; i < straight.Count; i++)
            {
                Assert.That(reloaded[i].Perfect, Is.EqualTo(straight[i].Perfect), "sale " + i);
                // JsonUtility does not round-trip a double exactly, so a price saved mid-service may come back a
                // last digit off; the roll itself must match exactly.
                Assert.That(reloaded[i].Cash, Is.EqualTo(straight[i].Cash).Within(1e-9).Percent, "sale " + i);
                Assert.That(straight[i].Cash, Is.EqualTo(Expected(a, 0, straight[i])).Within(1e-9));
                if (straight[i].Perfect) perfect++;
            }
            Assert.That(straight.Count, Is.GreaterThan(1000));
            Assert.That((double)perfect / straight.Count, Is.InRange(0.07d, 0.11d));
        }

        [Test]
        public void AnOldSaveMidServiceIsReadAsServingOneItem()
        {
            MiningShopBusinessSimulation.Tuning tuning = Ungated();
            tuning.ArrivalSeconds = 3d;   // a bundle of one at level 1
            var state = Fresh();
            var sim = new MiningShopBusinessSimulation(state, 1, tuning, _ => { });
            for (int i = 0; i < 600 && !(state.Business.Serving && state.Business.ServiceUnits == 1); i++) sim.Advance(0.5d);
            Assert.That(state.Business.Serving, Is.True);
            Assert.That(state.Business.ServiceUnits, Is.EqualTo(1));

            state.Business.ServiceUnits = 0;   // what a save written before bundles holds
            var loaded = JsonUtility.FromJson<MiningShopState>(JsonUtility.ToJson(state));
            var resumed = new MiningShopBusinessSimulation(loaded, 1, tuning, _ => { });
            Assert.That(resumed.View.ServiceUnits, Is.EqualTo(1));
            Conserved(loaded);
        }
    }
}
