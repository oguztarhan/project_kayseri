using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class MiningShopSimulationTests
    {
        private const string BusinessId = "mining-shop.island-01-01";
        private static MiningShopState Fresh() => new MiningShopState { BusinessId = BusinessId };

        private static void Conserved(MiningShopState state)
        {
            Assert.That(state.Produced, Is.EqualTo(state.Sold + state.OutputStock + state.PickupReserved +
                state.Cargo + state.ShelfStock + (state.Serving ? 1L : 0L)));
            Assert.That(state.DestinationReserved, Is.EqualTo(state.PickupReserved + state.Cargo));
            Assert.That(state.OutputStock, Is.GreaterThanOrEqualTo(0));
            Assert.That(state.ShelfStock, Is.GreaterThanOrEqualTo(0));
        }

        private static void Drain(MiningShopSimulation simulation)
        {
            int calls = 0;
            while (simulation.View.PendingSeconds > 0d && calls++ < 100) simulation.Advance(0d);
            Assert.That(simulation.View.PendingSeconds, Is.Zero);
        }

        [Test]
        public void FirstPickaxeIsCraftedAtTenSecondsAndPaidOnlyAfterDeliveryAndService()
        {
            var state = Fresh();
            var receipts = new List<MiningShopSimulation.Sale>();
            var sim = new MiningShopSimulation(state, MiningShopSimulation.Tuning.Default, receipts.Add);
            sim.Advance(9.9d);
            Assert.That(state.Produced, Is.Zero);
            sim.Advance(0.1d);
            Assert.That(state.Produced, Is.EqualTo(1));
            Assert.That(state.PickupReserved, Is.EqualTo(1));
            Assert.That(state.Carrier, Is.EqualTo(MiningShopSimulation.CarrierPhase.Loading));
            Assert.That(receipts, Is.Empty);
            sim.Advance(0.5d);
            Assert.That(state.Cargo, Is.EqualTo(1));
            Assert.That(state.PickupReserved, Is.Zero);
            sim.Advance(4.5d);
            Assert.That(state.Serving, Is.True);
            Assert.That(state.Cargo, Is.Zero);
            Assert.That(receipts, Is.Empty);
            sim.Advance(2d);
            Assert.That(receipts.Count, Is.EqualTo(1));
            Assert.That(receipts[0].ProductId, Is.EqualTo("mining-shop.pickaxe"));
            Assert.That(receipts[0].BusinessId, Is.EqualTo(BusinessId));
            Assert.That(receipts[0].Sequence, Is.EqualTo(1));
            Assert.That(receipts[0].Cash, Is.EqualTo(20d));
            Conserved(state);
        }

        [Test]
        public void EquivalentTimePartitionsProduceTheSameJobsStockAndIncome()
        {
            var whole = Fresh();
            var chunks = Fresh();
            double wholeCash = 0d, chunkCash = 0d;
            var one = new MiningShopSimulation(whole, MiningShopSimulation.Tuning.Default, sale => wholeCash += sale.Cash);
            var many = new MiningShopSimulation(chunks, MiningShopSimulation.Tuning.Default, sale => chunkCash += sale.Cash);
            one.Advance(600d);
            Drain(one);
            for (int i = 0; i < 2400; i++) many.Advance(0.25d);
            Assert.That(JsonUtility.ToJson(whole), Is.EqualTo(JsonUtility.ToJson(chunks)));
            Assert.That(wholeCash, Is.EqualTo(chunkCash));
            Assert.That(whole.Sold, Is.EqualTo(59));
            Conserved(whole);
        }

        [Test]
        public void FullShelvesAndOutputPauseProductionWithoutDroppingGoods()
        {
            var tuning = MiningShopSimulation.Tuning.Default;
            tuning.OutputCapacity = 2;
            tuning.ShelfCapacity = 1;
            tuning.ServiceSeconds = 1000d;
            var state = Fresh();
            var sim = new MiningShopSimulation(state, tuning, _ => { });
            sim.Advance(100d);
            Drain(sim);
            Assert.That(state.Serving, Is.True);
            Assert.That(state.ShelfStock, Is.EqualTo(1));
            Assert.That(state.OutputStock, Is.EqualTo(2));
            Assert.That(state.Crafting, Is.False);
            Assert.That(state.Produced, Is.EqualTo(4));
            Assert.That(state.WaitingCustomers, Is.LessThanOrEqualTo(tuning.QueueCapacity));
            Conserved(state);
            sim.Advance(1000d);
            Drain(sim);
            Assert.That(state.Sold, Is.GreaterThan(0));
            Assert.That(state.Produced, Is.GreaterThan(4));
            Conserved(state);
        }

        [Test]
        public void SmallerShelfAfterReloadAcceptsPartialCargoAndKeepsTheRemainder()
        {
            var state = Fresh();
            state.Produced = 3;
            state.Cargo = 3;
            state.DestinationReserved = 3;
            state.Carrier = MiningShopSimulation.CarrierPhase.Unloading;
            state.CarrierDuration = state.CarrierRemaining = 0.5d;
            var tuning = MiningShopSimulation.Tuning.Default;
            tuning.ShelfCapacity = 1;
            var sim = new MiningShopSimulation(state, tuning, _ => { });
            sim.Advance(0.5d);
            Assert.That(state.ShelfStock, Is.EqualTo(1));
            Assert.That(state.Cargo, Is.EqualTo(2));
            Assert.That(state.DestinationReserved, Is.EqualTo(2));
            Assert.That(state.Carrier, Is.EqualTo(MiningShopSimulation.CarrierPhase.WaitingForSpace));
            Conserved(state);
            sim.Advance(8d);
            Assert.That(state.Sold, Is.GreaterThanOrEqualTo(2));
            Assert.That(state.Cargo, Is.Zero);
            Conserved(state);
        }

        [TestCase(5d)]
        [TestCase(10.25d)]
        [TestCase(12d)]
        [TestCase(14.75d)]
        [TestCase(16d)]
        [TestCase(18d)]
        public void ReloadAtEachJobPhaseMatchesAnUninterruptedBusiness(double checkpoint)
        {
            var state = Fresh();
            var control = Fresh();
            double resumedCash = 0d, controlCash = 0d;
            var sim = new MiningShopSimulation(state, MiningShopSimulation.Tuning.Default, sale => resumedCash += sale.Cash);
            var other = new MiningShopSimulation(control, MiningShopSimulation.Tuning.Default, sale => controlCash += sale.Cash);
            sim.Advance(checkpoint);
            var loaded = JsonUtility.FromJson<MiningShopState>(JsonUtility.ToJson(state));
            double beforeOpen = resumedCash;
            var reopened = new MiningShopSimulation(loaded, MiningShopSimulation.Tuning.Default, sale => resumedCash += sale.Cash);
            Assert.That(resumedCash, Is.EqualTo(beforeOpen), "Opening a save does not replay a receipt.");
            reopened.Advance(60d - checkpoint);
            other.Advance(60d);
            Assert.That(JsonUtility.ToJson(loaded), Is.EqualTo(JsonUtility.ToJson(control)));
            Assert.That(resumedCash, Is.EqualTo(controlCash));
            Conserved(loaded);
        }

        [Test]
        public void SpeedUpgradePreservesCurrentCraftFraction()
        {
            var state = Fresh();
            var sim = new MiningShopSimulation(state, MiningShopSimulation.Tuning.Default, _ => { });
            sim.Advance(5d);
            Assert.That(sim.Upgrade(true), Is.True);
            Assert.That(state.CraftDuration, Is.EqualTo(10d / 1.1d).Within(1e-9));
            Assert.That(state.CraftRemaining / state.CraftDuration, Is.EqualTo(0.5d).Within(1e-9));
            sim.Advance(state.CraftRemaining);
            Assert.That(state.Produced, Is.EqualTo(1));
            Conserved(state);
        }

        [Test]
        public void PriceUpgradeDoesNotChangeThePriceOfACustomerAlreadyBeingServed()
        {
            var state = Fresh();
            var receipts = new List<MiningShopSimulation.Sale>();
            var sim = new MiningShopSimulation(state, MiningShopSimulation.Tuning.Default, receipts.Add);
            sim.Advance(16d);
            sim.Upgrade(false);
            Assert.That(sim.UnitPrice, Is.EqualTo(23d).Within(1e-9));
            sim.Advance(11d);
            Assert.That(receipts.Count, Is.EqualTo(2));
            Assert.That(receipts[0].Cash, Is.EqualTo(20d));
            Assert.That(receipts[1].Cash, Is.EqualTo(23d).Within(1e-9));
        }

        [Test]
        public void LongForegroundHitchKeepsUnprocessedTimeInTheSave()
        {
            var state = Fresh();
            double paid = 0d;
            var sim = new MiningShopSimulation(state, MiningShopSimulation.Tuning.Default, sale => paid += sale.Cash);
            sim.Advance(3600d);
            Assert.That(state.PendingSeconds, Is.GreaterThan(0));
            Assert.That(state.ElapsedSeconds + state.PendingSeconds, Is.EqualTo(3600d).Within(1e-9));
            var loaded = JsonUtility.FromJson<MiningShopState>(JsonUtility.ToJson(state));
            var reopened = new MiningShopSimulation(loaded, MiningShopSimulation.Tuning.Default, sale => paid += sale.Cash);
            Drain(reopened);
            Assert.That(loaded.ElapsedSeconds, Is.EqualTo(3600d).Within(1e-9));
            Assert.That(loaded.Sold, Is.EqualTo(359));
            Assert.That(paid, Is.EqualTo(7180d));
            Conserved(loaded);
        }

        [Test]
        public void InvalidTuningAndCorruptStockAreRejectedWithoutResettingTheRecord()
        {
            var tuning = MiningShopSimulation.Tuning.Default;
            tuning.CraftSeconds = double.NaN;
            Assert.Throws<ArgumentException>(() => new MiningShopSimulation(Fresh(), tuning, _ => { }));
            var state = Fresh();
            state.Cargo = 2;
            Assert.Throws<ArgumentException>(() => new MiningShopSimulation(state, MiningShopSimulation.Tuning.Default, _ => { }));
            Assert.That(state.Cargo, Is.EqualTo(2));
        }

        [Test]
        public void InvalidElapsedTimeCannotReverseProgressOrPoisonTheSave()
        {
            var state = Fresh();
            var sim = new MiningShopSimulation(state, MiningShopSimulation.Tuning.Default, _ => { });
            sim.Advance(5d);
            string before = JsonUtility.ToJson(state);
            Assert.Throws<ArgumentOutOfRangeException>(() => sim.Advance(-1d));
            Assert.Throws<ArgumentOutOfRangeException>(() => sim.Advance(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => sim.Advance(double.PositiveInfinity));
            Assert.That(JsonUtility.ToJson(state), Is.EqualTo(before));
        }
    }
}
