using System;
using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Systems;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>Shop contracts end to end: the 30-minute offer, accept, one payment on completion, cancel, reload.</summary>
    public sealed class ShopContractServiceTests
    {
        private const string BusinessId = "mining-shop.island-01-01";
        private static readonly ShopContract.Tuning T = ShopContract.Tuning.Default;

        private MiningShopCampaignConfig _campaignConfig;
        private SaveData _data;
        private WalletService _wallet;
        private MarketService _market;
        private ForemanService _foremen;
        private GoalService _goals;
        private ShopContractService _contracts;
        private long _now;

        [SetUp]
        public void SetUp()
        {
            _campaignConfig = ScriptableObject.CreateInstance<MiningShopCampaignConfig>();
            _data = new SaveData { tutorialStep = TutorialProgress.StepDone };
            _now = 994444L * 1800L + 5L;
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(_campaignConfig);

        /// <summary>The shipped shop with the build gate off; <paramref name="quiet"/> keeps normal customers away.</summary>
        private static MiningShopBusinessSimulation.Tuning ShopTuning(bool quiet)
        {
            MiningShopBusinessSimulation.Tuning tuning = MiningShopBusinessSimulation.Tuning.Default;
            tuning.BuildRequiresLevel = BenchMastery.MinLevel;
            if (quiet) tuning.ArrivalSeconds = 1e9d;
            return tuning;
        }

        /// <summary>Everything a launch builds, over whatever <see cref="_data"/> holds.</summary>
        private MiningShopBusinessService Launch(bool quiet = true)
        {
            _wallet = new WalletService(_data.wallet);
            _foremen = new ForemanService(_data, _wallet, Foremen.Tuning.Default);
            _goals = new GoalService(_data, _wallet, null, new TimeService());
            _market = new MarketService(_data, _wallet, null, null, _foremen, _goals);
            MiningShopBusinessService shop = _market.OpenMiningShopBusiness(_campaignConfig.CreateCampaign(), BusinessId,
                ShopTuning(quiet));
            _contracts = new ShopContractService(_market, _wallet, T, _data, () => _now, _foremen, _goals);
            return shop;
        }

        /// <summary>Runs the shop's clock until the contract is done, or gives up.</summary>
        private void RunUntilDone(int maxSeconds = 20000)
        {
            for (int i = 0; i < maxSeconds && _market.MiningShopBusiness.View.ContractActive; i++) _market.Tick(1f);
            Assert.That(_market.MiningShopBusiness.View.ContractActive, Is.False, "the contract never filled");
        }

        private int CardsBanked()
        {
            int n = 0;
            for (int i = 0; i < _data.masterCards.Length; i++) n += _data.masterCards[i];
            return n;
        }

        private ShopContractService.Offer OfferNow()
        {
            Assert.That(_contracts.TryGetOffer(out ShopContractService.Offer offer), Is.True, "no offer");
            return offer;
        }

        [Test]
        public void NothingIsOfferedUntilTheOpeningTutorialIsDone()
        {
            _data.tutorialStep = 0;
            Launch();
            Assert.That(_contracts.Unlocked, Is.False);
            Assert.That(_contracts.TryGetOffer(out _), Is.False);

            _data.tutorialStep = TutorialProgress.StepDone;
            Assert.That(_contracts.Unlocked, Is.True);
            Assert.That(_contracts.TryGetOffer(out _), Is.True, "the first offer is there at once, not after 30 minutes");
        }

        [Test]
        public void TheOfferTurnsOverEveryThirtyMinutes()
        {
            Launch();
            ShopContractService.Offer first = OfferNow();
            Assert.That(first.Slot, Is.EqualTo(994444L));
            Assert.That(_contracts.SecondsToNextOffer, Is.EqualTo(1795d));

            _now += 1794L;
            Assert.That(OfferNow().Slot, Is.EqualTo(994444L));
            _now += 1L;
            Assert.That(OfferNow().Slot, Is.EqualTo(994445L));
            Assert.That(_contracts.SecondsToNextOffer, Is.EqualTo(1800d));
        }

        [Test]
        public void TheSlotsProductStaysFrozenWhenABenchIsBuiltMidSlot()
        {
            MiningShopBusinessService shop = Launch();
            // A slot that would order helmets once the helmet bench exists.
            long slot = 994444L;
            bool[] both = { true, true, false, false };
            ShopContract.Pick pick;
            while (!ShopContract.TryPick(BusinessId, slot, both, T, out pick) || pick.ProductIndex != 1) slot++;
            _now = slot * 1800L + 5L;

            ShopContractService.Offer before = OfferNow();
            Assert.That(before.ProductIndex, Is.EqualTo(0), "only the pickaxe bench is built");
            _wallet.AddCash(new BigDouble(1e7));
            Assert.That(shop.TryBuildTable(1), Is.True);

            ShopContractService.Offer after = OfferNow();
            Assert.That(after.ProductIndex, Is.EqualTo(0), "the slot keeps the job it showed");
            Assert.That(after.SizeIndex, Is.EqualTo(before.SizeIndex));
            Assert.That(_data.shopContract.pickSlot, Is.EqualTo(slot));
        }

        [Test]
        public void ThePriceFollowsTheBenchUntilAcceptAndThenNeverMoves()
        {
            MiningShopBusinessService shop = Launch();
            ShopContractService.Offer atOne = OfferNow();
            Assert.That(atOne.Terms.Cash, Is.GreaterThan(atOne.Terms.NormalCash), "the contract pays a premium");
            Assert.That(atOne.Terms.Cash, Is.EqualTo(atOne.Terms.Quantity * _contracts.NormalUnitPrice(0) *
                (1d + T.Sizes[atOne.SizeIndex].Premium)).Within(1e-6));

            _wallet.AddCash(new BigDouble(1e9));
            Assert.That(shop.TryBuyLevels(0, 30), Is.EqualTo(30));
            ShopContractService.Offer atThirtyOne = OfferNow();
            Assert.That(atThirtyOne.Terms.Cash, Is.GreaterThan(atOne.Terms.Cash), "a better bench is a better offer");

            Assert.That(_contracts.Accept(atThirtyOne.Slot), Is.True);
            Assert.That(shop.TryBuyLevels(0, 30), Is.EqualTo(30));
            Assert.That(shop.View.ContractCash, Is.EqualTo(atThirtyOne.Terms.Cash), "agreed at accept");
            Assert.That(shop.View.ContractQuantity, Is.EqualTo(atThirtyOne.Terms.Quantity));
        }

        [Test]
        public void AcceptingPaysNothingAndTakesTheSlot()
        {
            Launch();
            double cash = _wallet.Cash.ToDouble();
            long gems = _wallet.Gems;
            int cards = CardsBanked();
            int changed = 0;
            _contracts.Changed += () => changed++;
            ShopContractService.Offer offer = OfferNow();

            Assert.That(_contracts.Accept(offer.Slot + 1), Is.False, "not the slot on show");
            Assert.That(_contracts.Accept(offer.Slot), Is.True);
            Assert.That(changed, Is.EqualTo(1));
            Assert.That(_wallet.Cash.ToDouble(), Is.EqualTo(cash), "nothing is paid at accept");
            Assert.That(_wallet.Gems, Is.EqualTo(gems));
            Assert.That(CardsBanked(), Is.EqualTo(cards));
            Assert.That(_contracts.HasActive, Is.True);

            Assert.That(_contracts.TryGetOffer(out _), Is.False, "this slot's offer is taken");
            Assert.That(_contracts.Accept(offer.Slot), Is.False);

            _now += 1800L;
            ShopContractService.Offer next = OfferNow();
            Assert.That(_contracts.CanAccept(next), Is.False, "one contract at a time");
            Assert.That(_contracts.Accept(next.Slot), Is.False);
        }

        [Test]
        public void SettingTheClockBackShowsNoOfferUntilTimeCatchesUp()
        {
            Launch();
            ShopContractService.Offer offer = OfferNow();
            Assert.That(_contracts.Accept(offer.Slot), Is.True);
            RunUntilDone();
            _contracts.MarkCelebrationShown();

            _now -= 3L * 1800L;
            Assert.That(_contracts.TryGetOffer(out _), Is.False);
            _now += 3L * 1800L;
            Assert.That(_contracts.TryGetOffer(out _), Is.False, "the slot taken is still taken");
            _now += 1800L;
            Assert.That(_contracts.TryGetOffer(out _), Is.True);
        }

        [Test]
        public void CompletionPaysEverythingOnceAndRecordsIt()
        {
            Launch();
            ShopContractService.Offer offer = OfferNow();
            double cash = _wallet.Cash.ToDouble();
            long gems = _wallet.Gems;
            int cards = CardsBanked();
            long contractsDone = _data.goals.lifetime[Goals.Contracts];
            long itemsSold = _data.goals.lifetime[Goals.BarsSold];
            var heard = new List<ShopContractService.Completion>();
            _contracts.Completed += heard.Add;

            Assert.That(_contracts.Accept(offer.Slot), Is.True);
            for (int i = 0; i < 60; i++) _market.Tick(1f);
            Assert.That(_market.MiningShopBusiness.View.ContractDelivered, Is.GreaterThan(0), "deliveries under way");
            Assert.That(_wallet.Cash.ToDouble(), Is.EqualTo(cash), "hand-overs pay nothing");
            RunUntilDone();

            Assert.That(heard.Count, Is.EqualTo(1));
            ShopContractService.Completion done = heard[0];
            Assert.That(_wallet.Cash.ToDouble(), Is.EqualTo(cash + offer.Terms.Cash).Within(1e-6));
            Assert.That(_wallet.Gems, Is.EqualTo(gems + offer.Terms.Gems));
            Assert.That(CardsBanked(), Is.EqualTo(cards + offer.Terms.ForemanCards));
            Assert.That(_data.goals.lifetime[Goals.Contracts], Is.EqualTo(contractsDone + 1));
            Assert.That(_data.goals.lifetime[Goals.BarsSold], Is.EqualTo(itemsSold + offer.Terms.Quantity),
                "delivered items count as sold");

            Assert.That(done.ProductIndex, Is.EqualTo(offer.ProductIndex));
            Assert.That(done.SizeIndex, Is.EqualTo(offer.SizeIndex));
            Assert.That(done.Quantity, Is.EqualTo(offer.Terms.Quantity));
            Assert.That(done.Cash, Is.EqualTo(offer.Terms.Cash));
            Assert.That(done.NormalCash, Is.EqualTo(offer.Terms.NormalCash).Within(1e-6));
            Assert.That(done.Gems, Is.EqualTo(offer.Terms.Gems));
            Assert.That(done.ForemanCards, Is.EqualTo(offer.Terms.ForemanCards));
            Assert.That(done.Foreman, Is.GreaterThanOrEqualTo(0), "the cards went to a foreman");

            Assert.That(_contracts.HasActive, Is.False);
            Assert.That(_market.MiningShopBusiness.View.ContractCash, Is.Zero, "paid terms are cleared");
            for (int i = 0; i < 300; i++) _market.Tick(1f);
            Assert.That(heard.Count, Is.EqualTo(1), "never paid twice");
        }

        [Test]
        public void TheCelebrationWaitsUntilShown()
        {
            Launch();
            Assert.That(_contracts.CelebrationPending, Is.False);
            ShopContractService.Offer offer = OfferNow();
            Assert.That(_contracts.Accept(offer.Slot), Is.True);
            RunUntilDone();

            Assert.That(_contracts.CelebrationPending, Is.True);
            ShopContractService.Completion owed = _contracts.PendingCelebration;
            Assert.That(owed.Quantity, Is.EqualTo(offer.Terms.Quantity));
            Assert.That(owed.Cash, Is.EqualTo(offer.Terms.Cash));
            Assert.That(owed.Gems, Is.EqualTo(offer.Terms.Gems));
            Assert.That(owed.ForemanCards, Is.EqualTo(offer.Terms.ForemanCards));

            _contracts.MarkCelebrationShown();
            Assert.That(_contracts.CelebrationPending, Is.False);
        }

        [Test]
        public void ChangingIslandCancelsAndPaysNothing()
        {
            Launch();
            ShopContractService.Offer offer = OfferNow();
            double cash = _wallet.Cash.ToDouble();
            long gems = _wallet.Gems;
            int cards = CardsBanked();
            var heard = new List<ShopContractService.Completion>();
            _contracts.Completed += heard.Add;

            Assert.That(_contracts.CancelForIslandChange(), Is.False, "nothing running");
            Assert.That(_contracts.Accept(offer.Slot), Is.True);
            for (int i = 0; i < 60; i++) _market.Tick(1f);
            Assert.That(_market.MiningShopBusiness.View.ContractDelivered, Is.GreaterThan(0));

            Assert.That(_contracts.CancelForIslandChange(), Is.True);
            for (int i = 0; i < 600; i++) _market.Tick(1f);
            Assert.That(heard, Is.Empty);
            Assert.That(_contracts.CelebrationPending, Is.False);
            Assert.That(_wallet.Cash.ToDouble(), Is.EqualTo(cash), "delivered items are not paid for");
            Assert.That(_wallet.Gems, Is.EqualTo(gems));
            Assert.That(CardsBanked(), Is.EqualTo(cards));
            Assert.That(_contracts.TryGetOffer(out _), Is.False, "the cancelled slot stays taken");
        }

        [Test]
        public void AContractLeftRunningOnAnotherIslandIsCancelledAtLaunch()
        {
            var elsewhere = new MiningShopState
            {
                BusinessId = "mining-shop.island-01-02",
                Business = new MiningShopBusinessState()
            };
            elsewhere.Business.Contract.Active = true;
            elsewhere.Business.Contract.Slot = 7L;
            elsewhere.Business.Contract.ProductIndex = 0;
            elsewhere.Business.Contract.Quantity = 30;
            elsewhere.Business.Contract.Delivered = 12;
            elsewhere.Business.Contract.Cash = 5000d;
            elsewhere.Business.Contract.Gems = 2L;
            _data.miningShopBusinesses.Add(elsewhere);
            long gems = _data.wallet.gems;

            Launch();
            Assert.That(elsewhere.Business.Contract.Active, Is.False);
            Assert.That(elsewhere.Business.Contract.Cash, Is.Zero);
            Assert.That(elsewhere.Business.Contract.Slot, Is.EqualTo(7L));
            Assert.That(_wallet.Gems, Is.EqualTo(gems), "nothing is paid for it");
        }

        [Test]
        public void AReloadMidContractFinishesAndPaysOnce()
        {
            Launch();
            ShopContractService.Offer offer = OfferNow();
            Assert.That(_contracts.Accept(offer.Slot), Is.True);
            for (int i = 0; i < 60; i++) _market.Tick(1f);
            double cash = _wallet.Cash.ToDouble();

            _data = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(_data));
            Launch();
            Assert.That(_contracts.HasActive, Is.True);
            var heard = new List<ShopContractService.Completion>();
            _contracts.Completed += heard.Add;
            RunUntilDone();

            Assert.That(heard.Count, Is.EqualTo(1));
            Assert.That(_wallet.Cash.ToDouble(), Is.EqualTo(cash + offer.Terms.Cash).Within(1e-6));
        }

        [Test]
        public void AReloadBeforeTheCelebrationStillShowsIt()
        {
            Launch();
            ShopContractService.Offer offer = OfferNow();
            Assert.That(_contracts.Accept(offer.Slot), Is.True);
            RunUntilDone();
            double cash = _wallet.Cash.ToDouble();

            _data = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(_data));
            Launch();
            Assert.That(_contracts.CelebrationPending, Is.True);
            Assert.That(_contracts.PendingCelebration.Cash, Is.EqualTo(offer.Terms.Cash));
            Assert.That(_wallet.Cash.ToDouble(), Is.EqualTo(cash), "a reload does not pay it again");
            Assert.That(_contracts.HasActive, Is.False);
        }

        [Test]
        public void ASaveFromBeforeShopContractsLoads()
        {
            _data.shopContract = null;
            Launch();
            Assert.That(_data.shopContract, Is.Not.Null);
            Assert.That(_contracts.CelebrationPending, Is.False);
            Assert.That(_contracts.TryGetOffer(out _), Is.True);
        }

        [Test]
        public void WithNormalCustomersTheContractStillFillsAndOnlyItIsPaidAtTheEnd()
        {
            Launch(quiet: false);
            ShopContractService.Offer offer = OfferNow();
            var heard = new List<ShopContractService.Completion>();
            _contracts.Completed += heard.Add;
            Assert.That(_contracts.Accept(offer.Slot), Is.True);
            RunUntilDone(offer.Terms.Quantity * 200);
            Assert.That(heard.Count, Is.EqualTo(1));
            Assert.That(heard[0].Cash, Is.EqualTo(offer.Terms.Cash));
        }
    }
}
