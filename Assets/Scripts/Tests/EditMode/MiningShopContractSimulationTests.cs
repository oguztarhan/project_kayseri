using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>The contract customer inside the shop simulation: served in turn, paid for nothing until done, saved.</summary>
    public sealed class MiningShopContractSimulationTests
    {
        private const string BusinessId = "mining-shop.island-01-04";

        private static MiningShopState Fresh() => new MiningShopState { BusinessId = BusinessId };

        private static MiningShopBusinessSimulation.Tuning Ungated()
        {
            MiningShopBusinessSimulation.Tuning tuning = MiningShopBusinessSimulation.Tuning.Default;
            tuning.BuildRequiresLevel = BenchMastery.MinLevel;
            return tuning;
        }

        private static MiningShopBusinessSimulation Open(MiningShopState state, int available,
            List<MiningShopBusinessSimulation.Sale> receipts, MiningShopBusinessSimulation.Tuning tuning)
            => new MiningShopBusinessSimulation(state, available, tuning, sale => receipts?.Add(sale));

        private static MiningShopBusinessSimulation Open(MiningShopState state, int available,
            List<MiningShopBusinessSimulation.Sale> receipts = null) => Open(state, available, receipts, Ungated());

        private static ShopContract.Terms Terms(int quantity) => new ShopContract.Terms
        {
            Quantity = quantity,
            Cash = 1234d,
            NormalCash = 1000d,
            Gems = 2L,
            ForemanCards = 1
        };

        /// <summary>Advances in small steps so no single call runs out of event boundaries.</summary>
        private static void Run(MiningShopBusinessSimulation sim, double seconds)
        {
            for (double t = 0d; t < seconds; t += 5d) sim.Advance(5d);
            int calls = 0;
            while (sim.View.PendingSeconds > 0d && calls++ < 100) sim.Advance(0d);
        }

        private static void Conserved(MiningShopState owner)
        {
            MiningShopBusinessState business = owner.Business;
            for (int i = 0; i < business.Lines.Count; i++)
            {
                MiningShopProductLineState line = business.Lines[i];
                int cargo = business.CarrierProductIndex == i ? business.CarrierCount : 0;
                int serving = business.Serving && business.ServiceProductIndex == i ? business.ServiceUnits : 0;
                Assert.That(line.Produced, Is.EqualTo(line.Sold + line.OutputStock + line.PickupReserved +
                    cargo + line.ShelfStock + serving), "product " + i);
            }
        }

        private static int ContractUnits(List<MiningShopBusinessSimulation.Sale> receipts)
        {
            int units = 0;
            for (int i = 0; i < receipts.Count; i++) if (receipts[i].Contract) units += receipts[i].Units;
            return units;
        }

        private static int Completions(List<MiningShopBusinessSimulation.Sale> receipts)
        {
            int n = 0;
            for (int i = 0; i < receipts.Count; i++) if (receipts[i].CompletesContract) n++;
            return n;
        }

        [Test]
        public void AContractStartsOnlyOnABuiltBenchWithRealTermsAndOneAtATime()
        {
            MiningShopBusinessSimulation sim = Open(Fresh(), 4);
            Assert.That(sim.StartContract(7L, 1, ShopContract.SmallSize, Terms(10)), Is.False, "helmet bench not built");
            Assert.That(sim.StartContract(7L, 4, ShopContract.SmallSize, Terms(10)), Is.False, "no such bench");
            Assert.That(sim.StartContract(7L, 0, 3, Terms(10)), Is.False, "no such size");
            Assert.That(sim.StartContract(7L, 0, ShopContract.SmallSize, Terms(0)), Is.False, "nothing ordered");

            Assert.That(sim.StartContract(7L, 0, ShopContract.MediumSize, Terms(10)), Is.True);
            Assert.That(sim.StartContract(8L, 0, ShopContract.SmallSize, Terms(10)), Is.False, "one contract at a time");

            MiningShopBusinessSimulation.Snapshot v = sim.View;
            Assert.That(v.ContractActive, Is.True);
            Assert.That(v.ContractSlot, Is.EqualTo(7L));
            Assert.That(v.ContractProductIndex, Is.EqualTo(0));
            Assert.That(v.ContractSizeIndex, Is.EqualTo(ShopContract.MediumSize));
            Assert.That(v.ContractQuantity, Is.EqualTo(10));
            Assert.That(v.ContractDelivered, Is.EqualTo(0));
            Assert.That(v.ContractCash, Is.EqualTo(1234d));
            Assert.That(v.ContractGems, Is.EqualTo(2L));
            Assert.That(v.ContractForemanCards, Is.EqualTo(1));
        }

        [Test]
        public void TheContractFillsWithoutCashOrAQueueSpotAndCompletesExactlyOnce()
        {
            var state = Fresh();
            var receipts = new List<MiningShopBusinessSimulation.Sale>();
            MiningShopBusinessSimulation sim = Open(state, 1, receipts);
            Assert.That(sim.StartContract(1L, 0, ShopContract.SmallSize, Terms(12)), Is.True);

            int maxQueue = 0;
            for (int i = 0; i < 200 && sim.View.ContractActive; i++)
            {
                Run(sim, 5d);
                maxQueue = Math.Max(maxQueue, sim.View.WaitingCustomerCount);
                Conserved(state);
            }

            Assert.That(sim.View.ContractActive, Is.False, "the contract never filled");
            Assert.That(sim.View.ContractDelivered, Is.EqualTo(12));
            Assert.That(ContractUnits(receipts), Is.EqualTo(12));
            Assert.That(Completions(receipts), Is.EqualTo(1));
            Assert.That(maxQueue, Is.LessThanOrEqualTo(MiningShopBusinessSimulation.Tuning.Default.QueueCapacity));

            MiningShopBusinessSimulation.Sale last = default;
            bool sawNormal = false;
            for (int i = 0; i < receipts.Count; i++)
            {
                MiningShopBusinessSimulation.Sale sale = receipts[i];
                if (sale.Contract)
                {
                    Assert.That(sale.Cash, Is.Zero, "a hand-over is not a sale");
                    Assert.That(sale.Perfect, Is.False);
                    last = sale;
                }
                else
                {
                    Assert.That(sale.Cash, Is.GreaterThan(0d));
                    sawNormal = true;
                }
            }
            Assert.That(last.CompletesContract, Is.True, "only the last hand-over completes it");
            Assert.That(sawNormal, Is.True, "normal customers keep buying during the contract");
            Assert.That(state.Business.Lines[0].Earned, Is.GreaterThan(0d));
        }

        [Test]
        public void OnItsBenchTheContractAndNormalCustomersTakeTurns()
        {
            var state = Fresh();
            var receipts = new List<MiningShopBusinessSimulation.Sale>();
            var waitingAtReceipt = new List<int>();
            var wantedAtReceipt = new List<bool>();
            MiningShopBusinessSimulation sim = null;
            sim = new MiningShopBusinessSimulation(state, 1, Ungated(), sale =>
            {
                receipts.Add(sale);
                waitingAtReceipt.Add(sim.View.ProductAt(0).WaitingCustomers);
                wantedAtReceipt.Add(sim.View.ContractActive);
            });
            Run(sim, 60d);   // let a queue form
            receipts.Clear();
            waitingAtReceipt.Clear();
            wantedAtReceipt.Clear();
            Assert.That(sim.StartContract(1L, 0, ShopContract.LargeSize, Terms(40)), Is.True);
            for (int i = 0; i < 400 && sim.View.ContractActive; i++) Run(sim, 5d);
            Assert.That(sim.View.ContractActive, Is.False);

            int contractFirst = 0, normalFirst = 0;
            for (int i = 0; i + 1 < receipts.Count; i++)
            {
                // A hand-over with someone still waiting must be followed by that someone.
                if (receipts[i].Contract && waitingAtReceipt[i] > 0)
                {
                    Assert.That(receipts[i + 1].Contract, Is.False, "receipt " + i + ": contract served twice while a customer waited");
                    contractFirst++;
                }
                // A normal sale while the contract still wants goods must be followed by the contract.
                if (!receipts[i].Contract && wantedAtReceipt[i])
                {
                    Assert.That(receipts[i + 1].Contract, Is.True, "receipt " + i + ": two normal sales while the contract waited");
                    normalFirst++;
                }
            }
            Assert.That(contractFirst, Is.GreaterThan(2), "the rule after a hand-over was never exercised");
            Assert.That(normalFirst, Is.GreaterThan(2), "the rule after a normal sale was never exercised");
        }

        [Test]
        public void WithNobodyElseWaitingTheContractIsServedBackToBack()
        {
            MiningShopBusinessSimulation.Tuning quiet = Ungated();
            quiet.ArrivalSeconds = 1e9d;   // no normal customer ever arrives
            var state = Fresh();
            var receipts = new List<MiningShopBusinessSimulation.Sale>();
            MiningShopBusinessSimulation sim = Open(state, 1, receipts, quiet);
            Assert.That(sim.StartContract(1L, 0, ShopContract.SmallSize, Terms(10)), Is.True);
            for (int i = 0; i < 100 && sim.View.ContractActive; i++) Run(sim, 5d);

            Assert.That(sim.View.ContractActive, Is.False, "the seller must not stand idle waiting for a turn");
            Assert.That(receipts.Count, Is.GreaterThan(1));
            for (int i = 0; i < receipts.Count; i++) Assert.That(receipts[i].Contract, Is.True);
            Assert.That(ContractUnits(receipts), Is.EqualTo(10));
            Conserved(state);
        }

        [Test]
        public void OtherBenchesKeepSellingNormally()
        {
            var state = Fresh();
            var receipts = new List<MiningShopBusinessSimulation.Sale>();
            MiningShopBusinessSimulation sim = Open(state, 2, receipts);
            Assert.That(sim.BuildTable(1), Is.True);
            Assert.That(sim.StartContract(1L, 0, ShopContract.SmallSize, Terms(20)), Is.True);
            Run(sim, 300d);

            int helmetSales = 0;
            for (int i = 0; i < receipts.Count; i++)
            {
                if (receipts[i].ProductId != MiningShopCampaign.ProductIdAt(1)) continue;
                Assert.That(receipts[i].Contract, Is.False, "the helmet bench has no contract");
                Assert.That(receipts[i].Cash, Is.GreaterThan(0d));
                helmetSales++;
            }
            Assert.That(helmetSales, Is.GreaterThan(0));
            Conserved(state);
        }

        [Test]
        public void LevellingTheBenchMidContractKeepsTheAgreedTerms()
        {
            MiningShopBusinessSimulation sim = Open(Fresh(), 1);
            Assert.That(sim.StartContract(1L, 0, ShopContract.SmallSize, Terms(30)), Is.True);
            Run(sim, 30d);
            sim.BuyLevels(0, 40);
            MiningShopBusinessSimulation.Snapshot v = sim.View;
            Assert.That(v.ContractQuantity, Is.EqualTo(30));
            Assert.That(v.ContractCash, Is.EqualTo(1234d));
            Assert.That(v.ContractGems, Is.EqualTo(2L));
            Assert.That(v.ContractForemanCards, Is.EqualTo(1));
        }

        [Test]
        public void AReloadMidHandOverFinishesItAsADeliveryAndCompletesOnce()
        {
            var state = Fresh();
            var receipts = new List<MiningShopBusinessSimulation.Sale>();
            MiningShopBusinessSimulation sim = Open(state, 1, receipts);
            Assert.That(sim.StartContract(3L, 0, ShopContract.SmallSize, Terms(15)), Is.True);
            for (int i = 0; i < 2000 && !sim.View.ServingContract; i++) sim.Advance(0.25d);
            Assert.That(sim.View.ServingContract, Is.True, "never caught a hand-over in progress");

            var loaded = JsonUtility.FromJson<MiningShopState>(JsonUtility.ToJson(state));
            Assert.That(loaded.Business.ServiceContract, Is.True);
            Assert.That(loaded.Business.Contract.Active, Is.True);
            MiningShopBusinessSimulation resumed = Open(loaded, 1, receipts);
            Assert.That(resumed.View.ContractSlot, Is.EqualTo(3L));
            for (int i = 0; i < 200 && resumed.View.ContractActive; i++) Run(resumed, 5d);

            Assert.That(resumed.View.ContractDelivered, Is.EqualTo(15));
            Assert.That(ContractUnits(receipts), Is.EqualTo(15), "the bundle in hand was delivered exactly once");
            Assert.That(Completions(receipts), Is.EqualTo(1));
            Conserved(loaded);
        }

        [Test]
        public void ACompletedContractKeepsItsTermsUntilClearedAndThenANewOneCanStart()
        {
            MiningShopBusinessSimulation.Tuning quiet = Ungated();
            quiet.ArrivalSeconds = 1e9d;
            MiningShopBusinessSimulation sim = Open(Fresh(), 1, null, quiet);
            Assert.That(sim.ClearCompletedContract(), Is.True, "nothing to clear is fine");
            Assert.That(sim.StartContract(5L, 0, ShopContract.SmallSize, Terms(5)), Is.True);
            Assert.That(sim.ClearCompletedContract(), Is.False, "a running contract is not cleared");
            for (int i = 0; i < 100 && sim.View.ContractActive; i++) Run(sim, 5d);

            MiningShopBusinessSimulation.Snapshot done = sim.View;
            Assert.That(done.ContractActive, Is.False);
            Assert.That(done.ContractDelivered, Is.EqualTo(5));
            Assert.That(done.ContractQuantity, Is.EqualTo(5));
            Assert.That(done.ContractCash, Is.EqualTo(1234d), "terms are held for the caller to pay");

            Assert.That(sim.ClearCompletedContract(), Is.True);
            Assert.That(sim.View.ContractQuantity, Is.Zero);
            Assert.That(sim.View.ContractCash, Is.Zero);
            Assert.That(sim.View.ContractSlot, Is.EqualTo(5L), "the last slot taken is remembered");
            Assert.That(sim.StartContract(6L, 0, ShopContract.SmallSize, Terms(5)), Is.True);
        }

        [Test]
        public void ACancelledContractStopsReceivingAndNeverCompletes()
        {
            var state = Fresh();
            var receipts = new List<MiningShopBusinessSimulation.Sale>();
            MiningShopBusinessSimulation sim = Open(state, 1, receipts);
            Assert.That(sim.CancelContract(), Is.False, "nothing to cancel");
            Assert.That(sim.StartContract(9L, 0, ShopContract.LargeSize, Terms(200)), Is.True);
            for (int i = 0; i < 400 && sim.View.ContractDelivered == 0; i++) Run(sim, 5d);
            Assert.That(sim.View.ContractDelivered, Is.GreaterThan(0));

            Assert.That(sim.CancelContract(), Is.True);
            MiningShopBusinessSimulation.Snapshot v = sim.View;
            Assert.That(v.ContractActive, Is.False);
            Assert.That(v.ContractQuantity, Is.Zero);
            Assert.That(v.ContractDelivered, Is.Zero);
            Assert.That(v.ContractCash, Is.Zero);
            Assert.That(v.ContractGems, Is.Zero);
            Assert.That(v.ContractForemanCards, Is.Zero);
            Assert.That(v.ContractSlot, Is.EqualTo(9L));

            int before = receipts.Count;
            Run(sim, 300d);
            int handOversAfter = 0;
            for (int i = before; i < receipts.Count; i++)
            {
                Assert.That(receipts[i].CompletesContract, Is.False);
                if (receipts[i].Contract) handOversAfter++;
            }
            Assert.That(handOversAfter, Is.LessThanOrEqualTo(1), "at most the bundle already in hand");
            Assert.That(receipts.Count, Is.GreaterThan(before), "normal selling carries on");
            Conserved(state);
        }

        [Test]
        public void ANewContractWaitsForACancelledOnesBundleToLeave()
        {
            var state = Fresh();
            var receipts = new List<MiningShopBusinessSimulation.Sale>();
            MiningShopBusinessSimulation sim = Open(state, 1, receipts);
            Assert.That(sim.StartContract(1L, 0, ShopContract.LargeSize, Terms(200)), Is.True);
            for (int i = 0; i < 2000 && !sim.View.ServingContract; i++) sim.Advance(0.25d);
            Assert.That(sim.View.ServingContract, Is.True);

            Assert.That(sim.CancelContract(), Is.True);
            Assert.That(sim.StartContract(2L, 0, ShopContract.SmallSize, Terms(10)), Is.False,
                "the old bundle is still in hand");
            for (int i = 0; i < 200 && sim.View.ServingContract; i++) sim.Advance(0.25d);
            Assert.That(sim.StartContract(2L, 0, ShopContract.SmallSize, Terms(10)), Is.True);
            Assert.That(sim.View.ContractDelivered, Is.Zero, "nothing from the cancelled order carried over");
            Conserved(state);
        }

        [Test]
        public void ASaveFromBeforeContractsLoadsWithNone()
        {
            var state = Fresh();
            Open(state, 1);
            Run(Open(state, 1), 20d);
            state.Business.Contract = null;
            state.Business.ServiceContract = false;
            MiningShopBusinessSimulation sim = Open(state, 1);
            Assert.That(sim.View.ContractActive, Is.False);
            Assert.That(state.Business.Contract, Is.Not.Null);

            string json = JsonUtility.ToJson(state).Replace("\"Contract\":", "\"Retired\":");
            var loaded = JsonUtility.FromJson<MiningShopState>(json);
            Assert.That(Open(loaded, 1).View.ContractActive, Is.False);
        }

        [Test]
        public void InconsistentContractSavesAreRefusedWithoutBeingCleared()
        {
            var state = Fresh();
            MiningShopBusinessSimulation sim = Open(state, 2);
            Assert.That(sim.StartContract(1L, 0, ShopContract.SmallSize, Terms(10)), Is.True);

            state.Business.Contract.ProductIndex = 1;   // helmet bench is not built
            Assert.Throws<ArgumentException>(() => Open(state, 2));
            Assert.That(state.Business.Contract.ProductIndex, Is.EqualTo(1), "the save is left as it was");
            state.Business.Contract.ProductIndex = 0;

            state.Business.Contract.Delivered = 10;     // a running contract is never already full
            Assert.Throws<ArgumentException>(() => Open(state, 2));
            state.Business.Contract.Delivered = 0;

            state.Business.Contract.Cash = double.NaN;
            Assert.Throws<ArgumentException>(() => Open(state, 2));
            state.Business.Contract.Cash = 1234d;

            state.Business.ServiceContract = true;      // hand-over flag without a hand-over
            Assert.That(state.Business.Serving, Is.False);
            Assert.Throws<ArgumentException>(() => Open(state, 2));
            state.Business.ServiceContract = false;

            Assert.DoesNotThrow(() => Open(state, 2));
        }

        [Test]
        public void AHandOverWithAPriceOrAPerfectRollIsRefused()
        {
            var state = Fresh();
            MiningShopBusinessSimulation sim = Open(state, 1);
            Assert.That(sim.StartContract(1L, 0, ShopContract.SmallSize, Terms(10)), Is.True);
            for (int i = 0; i < 2000 && !sim.View.ServingContract; i++) sim.Advance(0.25d);
            Assert.That(sim.View.ServingContract, Is.True);

            state.Business.ServicePrice = 5d;
            Assert.Throws<ArgumentException>(() => Open(state, 1));
            state.Business.ServicePrice = 0d;
            state.Business.ServicePerfect = true;
            Assert.Throws<ArgumentException>(() => Open(state, 1));
            state.Business.ServicePerfect = false;
            Assert.DoesNotThrow(() => Open(state, 1));
        }
    }
}
