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
                int serving = business.Serving && business.ServiceProductIndex == i ? 1 : 0;
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
            CollectionAssert.AreEqual(new[] { 20d, 60d, 150d, 360d }, new[]
            {
                tuning.Products[0].UnitPrice, tuning.Products[1].UnitPrice,
                tuning.Products[2].UnitPrice, tuning.Products[3].UnitPrice
            });
            CollectionAssert.AreEqual(new[] { 0d, 300d, 1400d, 5000d }, new[]
            {
                tuning.Products[0].TableCost, tuning.Products[1].TableCost,
                tuning.Products[2].TableCost, tuning.Products[3].TableCost
            });
        }

        [Test]
        public void InspectorDefaultsMatchTheFourProductBusinessContract()
        {
            var config = ScriptableObject.CreateInstance<MiningShopConfig>();
            try
            {
                MiningShopBusinessSimulation.Tuning tuning = config.ToBusinessTuning();
                Assert.That(tuning.CarrierLoad, Is.EqualTo(1));
                Assert.That(tuning.Products[0].CraftSeconds, Is.EqualTo(10d));
                Assert.That(tuning.Products[1].CraftSeconds, Is.EqualTo(20d));
                Assert.That(tuning.Products[2].CraftSeconds, Is.EqualTo(40d));
                Assert.That(tuning.Products[3].CraftSeconds, Is.EqualTo(60d));
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
            Assert.That(pickaxe.SpeedLevel, Is.EqualTo(3));
            Assert.That(pickaxe.ValueLevel, Is.EqualTo(2));
            Assert.That(pickaxe.CraftRemaining, Is.EqualTo(3d));
            Assert.That(simulation.View.BuiltTableCount, Is.EqualTo(1));
            Assert.That(simulation.View.AvailableProductCount, Is.EqualTo(1));
            Assert.That(state.SpeedLevel, Is.EqualTo(3));
            Assert.That(state.ValueLevel, Is.EqualTo(2));
            Assert.That(state.CraftRemaining, Is.EqualTo(3d));
            Conserved(state);
        }

        [Test]
        public void TablesUnlockOnlyInOrderAndOnlyWhenTheIslandAllowsThem()
        {
            var state = Fresh();
            MiningShopBusinessSimulation sim = Open(state, 3);
            Assert.That(sim.BuildTable(3), Is.False);
            Assert.That(sim.BuildTable(2), Is.False);
            Assert.That(sim.BuildTable(1), Is.True);
            Assert.That(sim.BuildTable(1), Is.False);
            Assert.That(sim.BuildTable(2), Is.True);
            Assert.That(sim.BuildTable(3), Is.False);
            Assert.That(sim.View.BuiltTableCount, Is.EqualTo(3));
            Assert.That(sim.TableCost(1), Is.EqualTo(300d));
            Assert.That(sim.TableCost(2), Is.EqualTo(1400d));
            Conserved(state);
        }

        [Test]
        public void TwoProductsKeepTheirOwnGoodsAndPayTheirOwnPricesThroughOneReceiptStream()
        {
            var state = Fresh();
            var receipts = new List<MiningShopBusinessSimulation.Sale>();
            MiningShopBusinessSimulation sim = Open(state, 2, receipts);
            Assert.That(sim.BuildTable(1), Is.True);
            for (int i = 0; i < 240; i++) sim.Advance(1d);

            bool pickaxe = false, helmet = false;
            long previous = 0;
            for (int i = 0; i < receipts.Count; i++)
            {
                MiningShopBusinessSimulation.Sale sale = receipts[i];
                Assert.That(sale.Sequence, Is.EqualTo(++previous));
                if (sale.ProductId == "mining-shop.pickaxe")
                {
                    pickaxe = true;
                    Assert.That(sale.Cash, Is.EqualTo(20d));
                }
                else if (sale.ProductId == "mining-shop.helmet")
                {
                    helmet = true;
                    Assert.That(sale.Cash, Is.EqualTo(60d));
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
            MiningShopBusinessSimulation sim = Open(state, 4, receipts);
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
            MiningShopBusinessSimulation.Tuning tuning = MiningShopBusinessSimulation.Tuning.Default;
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
            MiningShopBusinessSimulation sim = Open(state, 2);
            sim.BuildTable(1);
            sim.Advance(5d);
            MiningShopBusinessSimulation.ProductSnapshot before = sim.View.ProductAt(0);
            Assert.That(sim.Upgrade(0, true), Is.True);
            MiningShopBusinessSimulation.ProductSnapshot after = sim.View.ProductAt(0);
            Assert.That(after.CraftRemaining / after.CraftDuration,
                Is.EqualTo(before.CraftRemaining / before.CraftDuration).Within(1e-9));
            Assert.That(sim.View.ProductAt(1).SpeedLevel, Is.EqualTo(1));
            Conserved(state);
        }

        [Test]
        public void ReloadPreservesBuiltTablesCargoAndReceiptIdentityWithoutReplayingSales()
        {
            var state = Fresh();
            var receipts = new List<MiningShopBusinessSimulation.Sale>();
            MiningShopBusinessSimulation sim = Open(state, 2, receipts);
            sim.BuildTable(1);
            sim.Advance(43d);
            string json = JsonUtility.ToJson(state);
            var loaded = JsonUtility.FromJson<MiningShopState>(json);
            long before = loaded.Business.ReceiptSequence;
            var resumed = Open(loaded, 2, receipts);
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
    }
}
