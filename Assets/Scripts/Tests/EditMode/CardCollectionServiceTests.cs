using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Game.Core;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>
    /// The service. Because <see cref="CardCollectionService"/> takes its randomness injected, a pack
    /// can be made to hand over one NAMED card — so every test here asserts an exact outcome rather
    /// than opening packs until something happens and hoping.
    ///
    /// The scripted rolls assume the Legendary soft-pity ramp has not started, which is true for the
    /// first 35 packs of a run. No test here opens that many.
    /// </summary>
    public class CardCollectionServiceTests
    {
        /// <summary>A <see cref="Random"/> that hands back exactly what the test queued. The service
        /// asks for two doubles per pack: rarity first, then which card of that rarity.</summary>
        private sealed class ScriptedRandom : System.Random
        {
            private readonly Queue<double> _values = new Queue<double>();
            public ScriptedRandom(params double[] values)
            {
                for (int i = 0; i < values.Length; i++) _values.Enqueue(values[i]);
            }
            public void Queue(double value) => _values.Enqueue(value);
            public override double NextDouble() => _values.Count > 0 ? _values.Dequeue() : 0d;
        }

        private static SaveData _data;
        private static WalletService _wallet;

        [SetUp]
        public void SetUp()
        {
            _data = new SaveData();
            _wallet = new WalletService(_data.wallet);
        }

        private static CardCollectionService Service(System.Random random = null)
            => new CardCollectionService(_data, null, new TimeService(), _wallet, null, random);

        // ---- scripting a draw ---------------------------------------------------------------------

        /// <summary>The middle of a rarity's band, so a small change to the weights does not
        /// invalidate every test that targets a card.</summary>
        private static double RarityRoll(RosterCardState.Rarity rarity)
        {
            var t = CardCollectionPack.Tuning.Default;
            int[] census = CardCollectionCatalogue.RarityCensus();

            double total = 0d, before = 0d, mine = 0d;
            for (int r = 0; r < CardCollection.RarityCount; r++)
            {
                double w = CardCollectionPack.WeightOf((RosterCardState.Rarity)r, census, 0, t);
                if (r < (int)rarity) before += w;
                if (r == (int)rarity) mine = w;
                total += w;
            }
            return (before + mine * 0.5d) / total;
        }

        /// <summary>The two rolls that make the next pack hand over exactly this card.</summary>
        private static double[] Draw(int card)
        {
            RosterCardState.Rarity rarity = CardCollectionCatalogue.RarityOf(card);
            int count = CardCollectionCatalogue.CountOfRarity(rarity);

            int nth = -1;
            for (int i = 0; i < count; i++)
                if (CardCollectionCatalogue.OfRarity(rarity, i) == card) { nth = i; break; }

            Assert.That(nth, Is.GreaterThanOrEqualTo(0), "card is not in its own rarity pool");
            return new[] { RarityRoll(rarity), (nth + 0.5d) / count };
        }

        private static ScriptedRandom Draws(params int[] cards)
        {
            var rolls = new List<double>();
            for (int i = 0; i < cards.Length; i++) rolls.AddRange(Draw(cards[i]));
            return new ScriptedRandom(rolls.ToArray());
        }

        private static int CardId(string id)
        {
            int card = CardCollectionCatalogue.IndexOf(id);
            Assert.That(card, Is.GreaterThanOrEqualTo(0), id);
            return card;
        }

        [Test]
        public void TheTestHarnessCanAskForANamedCard()
        {
            // If this fails, every targeted test below is meaningless.
            int wanted = CardId("coal_industry/pit_charter");
            var service = Service(Draws(wanted));
            service.GrantPacks(1, CardCollection.PackSource.Daily);

            Assert.That(service.TryOpenPack(out var receipt), Is.True);
            Assert.That(receipt.Card, Is.EqualTo(wanted));
            Assert.That(receipt.Rarity, Is.EqualTo(RosterCardState.Rarity.Legendary));
        }

        // ---- a fresh collection -------------------------------------------------------------------

        [Test]
        public void AFreshCollectionOwnsNothingAndIsWorthNothing()
        {
            var service = Service();

            Assert.That(service.OwnedCardCount, Is.Zero);
            Assert.That(service.CompletedSetCount, Is.Zero);
            Assert.That(service.UnopenedPackCount, Is.Zero);
            Assert.That(service.UpgradeReadyCount(), Is.Zero);
            Assert.That(service.ClaimableSetRewardCount(), Is.Zero);
            Assert.That(service.Effects.IsEmpty, Is.True);
            Assert.That(service.Effects.IncomeMultiplier, Is.EqualTo(1d));
        }

        [Test]
        public void ASaveWrittenBeforeTheFeatureExistedIsNormalisedOnConstruction()
        {
            _data.cardCollection = null;

            CardCollectionService service = null;
            Assert.DoesNotThrow(() => service = Service());

            Assert.That(_data.cardCollection, Is.Not.Null);
            Assert.That(service.OwnedCardCount, Is.Zero);
            Assert.That(service.Effects.IsEmpty, Is.True);
        }

        [Test]
        public void AHandEditedSaveCannotBuyABiggerBonusThanTheGameSells()
        {
            foreach (var card in CardCollectionCatalogue.Cards)
                _data.cardCollection.FindOrAdd(card.Id).level = 99;

            var service = Service();

            Assert.That(service.OwnedCardCount, Is.EqualTo(CardCollectionCatalogue.Count));
            Assert.That(service.Effects.Income,
                        Is.LessThanOrEqualTo(CardCollection.CapOf(
                            CardCollection.EffectKind.IncomeMultiplier, service.Tuning)));
            Assert.That(service.Effects.Income, Is.EqualTo(0.24d).Within(1e-9),
                        "levels clamp to 5, so this is the honest completed value");
        }

        // ---- the daily pack -----------------------------------------------------------------------

        [Test]
        public void TheFirstEverDailyPackIsWaiting()
        {
            var service = Service();
            Assert.That(service.DailyPackReady, Is.True);
            Assert.That(service.DailyPackSecondsLeft, Is.Zero);
        }

        [Test]
        public void ClaimingTheDailyPackBanksItRatherThanOpeningIt()
        {
            var service = Service();

            Assert.That(service.TryClaimDailyPack(), Is.True);
            Assert.That(service.UnopenedPackCount, Is.EqualTo(1));
            Assert.That(service.PacksOpened, Is.Zero, "the reveal is the player's moment");
            Assert.That(service.OwnedCardCount, Is.Zero);
        }

        [Test]
        public void TheDailyPackCannotBeTakenTwiceInOneDay()
        {
            var service = Service();

            Assert.That(service.TryClaimDailyPack(), Is.True);
            Assert.That(service.DailyPackReady, Is.False);
            Assert.That(service.TryClaimDailyPack(), Is.False);
            Assert.That(service.UnopenedPackCount, Is.EqualTo(1));
            Assert.That(service.DailyPackSecondsLeft, Is.GreaterThan(0L).And.LessThanOrEqualTo(86400L));
        }

        [Test]
        public void ANewDayMakesTheDailyPackReadyAgain()
        {
            var service = Service();
            service.TryClaimDailyPack();

            _data.cardCollection.dailyPackDay = service.Today - 1;

            Assert.That(service.DailyPackReady, Is.True);
            Assert.That(service.TryClaimDailyPack(), Is.True);
            Assert.That(service.UnopenedPackCount, Is.EqualTo(2));
        }

        [Test]
        public void AClockRolledBackwardsDelaysTheDailyPackAndNeverPaysTwice()
        {
            var service = Service();

            // A device whose clock says tomorrow, claimed, then put back to today.
            _data.cardCollection.dailyPackDay = service.Today + 3;

            Assert.That(service.DailyPackReady, Is.False);
            Assert.That(service.TryClaimDailyPack(), Is.False);
            Assert.That(service.UnopenedPackCount, Is.Zero);
        }

        [Test]
        public void DaysAwayBankAtMostOnePack()
        {
            var service = Service();
            _data.cardCollection.dailyPackDay = service.Today - 30;

            Assert.That(service.TryClaimDailyPack(), Is.True);
            Assert.That(service.UnopenedPackCount, Is.EqualTo(1), "a week away is one pack, not seven");
        }

        // ---- opening ------------------------------------------------------------------------------

        [Test]
        public void APackCannotBeOpenedWithoutOne()
        {
            var service = Service();

            Assert.That(service.CanOpenPack, Is.False);
            Assert.That(service.TryOpenPack(out var receipt), Is.False);

            // A refused open hands back default(PackOpenReceipt), which never went through the
            // constructor — so CardId is null, not "". The contract is "false means ignore this",
            // and what actually matters is that nothing moved.
            Assert.That(receipt.CardId, Is.Null.Or.Empty);
            Assert.That(receipt.WasNew, Is.False);
            Assert.That(service.PacksOpened, Is.Zero);
            Assert.That(service.OwnedCardCount, Is.Zero);
            Assert.That(service.UnopenedPackCount, Is.Zero);
        }

        [Test]
        public void TheFirstCopyUnlocksACardAtLevelOne()
        {
            int card = CardId("deep_waters/tide_table");
            var service = Service(Draws(card));
            service.GrantPacks(1, CardCollection.PackSource.Daily);

            Assert.That(service.TryOpenPack(out var receipt), Is.True);

            Assert.That(receipt.WasNew, Is.True);
            Assert.That(receipt.WasDuplicate, Is.False);
            Assert.That(receipt.Level, Is.EqualTo(1));
            Assert.That(receipt.Duplicates, Is.Zero);
            Assert.That(service.LevelOf(card), Is.EqualTo(1));
            Assert.That(service.OwnedCardCount, Is.EqualTo(1));
            Assert.That(service.UnopenedPackCount, Is.Zero);
            Assert.That(service.PacksOpened, Is.EqualTo(1));
        }

        [Test]
        public void ASecondCopyBecomesADuplicateAndDoesNotRaiseTheLevel()
        {
            int card = CardId("deep_waters/tide_table");
            var service = Service(Draws(card, card));
            service.GrantPacks(2, CardCollection.PackSource.Daily);

            service.TryOpenPack(out _);
            Assert.That(service.TryOpenPack(out var second), Is.True);

            Assert.That(second.WasNew, Is.False);
            Assert.That(second.WasDuplicate, Is.True);
            Assert.That(second.Level, Is.EqualTo(1));
            Assert.That(second.Duplicates, Is.EqualTo(1));
            Assert.That(service.OwnedCardCount, Is.EqualTo(1), "still one card, not two");
        }

        [Test]
        public void ANewCardArrivesUnseenSoItCanCarryTheNewBadge()
        {
            int card = CardId("workshop_guild/bellows");
            var service = Service(Draws(card));
            service.GrantPacks(1, CardCollection.PackSource.Daily);
            service.TryOpenPack(out _);

            Assert.That(service.IsNew(card), Is.True);
            Assert.That(service.MarkSeen(card), Is.True);
            Assert.That(service.IsNew(card), Is.False);
            Assert.That(service.MarkSeen(card), Is.False, "marking it twice is not a change");
        }

        [Test]
        public void OpeningAPackSpendsExactlyOne()
        {
            int card = CardId("workshop_guild/bellows");
            var service = Service(Draws(card, card, card));
            service.GrantPacks(3, CardCollection.PackSource.GoalMilestone);

            service.TryOpenPack(out _);
            Assert.That(service.UnopenedPackCount, Is.EqualTo(2));
            service.TryOpenPack(out _);
            Assert.That(service.UnopenedPackCount, Is.EqualTo(1));
            service.TryOpenPack(out _);
            Assert.That(service.UnopenedPackCount, Is.Zero);
            Assert.That(service.TryOpenPack(out _), Is.False);
        }

        [Test]
        public void ThePityCountersMoveWithEveryPack()
        {
            int common = CardId("workshop_guild/bellows");
            int legendary = CardId("workshop_guild/guild_charter");

            var service = Service(Draws(common, common, legendary));
            service.GrantPacks(3, CardCollection.PackSource.Daily);

            service.TryOpenPack(out _);
            Assert.That(service.PullsSinceEpic, Is.EqualTo(1));
            Assert.That(service.PullsSinceLegendary, Is.EqualTo(1));

            service.TryOpenPack(out _);
            Assert.That(service.PullsSinceEpic, Is.EqualTo(2));

            service.TryOpenPack(out _);
            Assert.That(service.PullsSinceEpic, Is.Zero, "a Legendary clears both");
            Assert.That(service.PullsSinceLegendary, Is.Zero);
        }

        // ---- overflow -----------------------------------------------------------------------------

        [Test]
        public void ACopyOfAMaxedCardPaysGemsInsteadOfBeingWasted()
        {
            int card = CardId("coal_industry/ore_scale");
            _data.cardCollection.FindOrAdd(CardCollectionCatalogue.IdOf(card)).level =
                CardCollection.MaxLevel;

            var service = Service(Draws(card));
            service.GrantPacks(1, CardCollection.PackSource.Daily);

            long before = _wallet.Gems;
            Assert.That(service.TryOpenPack(out var receipt), Is.True);

            Assert.That(receipt.WasOverflow, Is.True);
            Assert.That(receipt.WasNew, Is.False);
            Assert.That(receipt.WasDuplicate, Is.False);
            Assert.That(receipt.GemsPaid, Is.EqualTo(5L), "a Common overflow copy");
            Assert.That(_wallet.Gems - before, Is.EqualTo(5L));
            Assert.That(receipt.Duplicates, Is.Zero, "overflow must not bank a copy that can never be spent");
            Assert.That(service.LevelOf(card), Is.EqualTo(CardCollection.MaxLevel));
        }

        // ---- levelling ----------------------------------------------------------------------------

        [Test]
        public void ACardLevelsOnceItHoldsEnoughCopiesAndTheCostIsSpent()
        {
            int card = CardId("deep_waters/rope_coil");   // Common: 2 copies for level 2
            var service = Service(Draws(card, card, card));
            service.GrantPacks(3, CardCollection.PackSource.Daily);

            service.TryOpenPack(out _);                    // unlock
            Assert.That(service.CanUpgrade(card), Is.False);

            service.TryOpenPack(out _);                    // 1 duplicate
            Assert.That(service.CanUpgrade(card), Is.False, "one short");

            service.TryOpenPack(out var third);            // 2 duplicates
            Assert.That(third.UpgradeReady, Is.True);
            Assert.That(service.CanUpgrade(card), Is.True);

            Assert.That(service.TryUpgrade(card), Is.True);
            Assert.That(service.LevelOf(card), Is.EqualTo(2));
            Assert.That(service.DuplicatesOf(card), Is.Zero, "the cost is spent, not merely checked");
            Assert.That(service.CanUpgrade(card), Is.False);
        }

        [Test]
        public void AnUnownedCardCannotBeLevelledHoweverManyCopiesTheSaveClaims()
        {
            int card = CardId("coal_industry/pit_charter");
            var row = _data.cardCollection.FindOrAdd(CardCollectionCatalogue.IdOf(card));
            row.level = CardCollection.NotOwned;
            row.duplicates = 500;

            var service = Service();

            Assert.That(service.CanUpgrade(card), Is.False);
            Assert.That(service.TryUpgrade(card), Is.False);
            Assert.That(service.LevelOf(card), Is.EqualTo(CardCollection.NotOwned));
        }

        [Test]
        public void AMaxedCardRefusesToLevelHoweverManyCopiesItHolds()
        {
            int card = CardId("coal_industry/pit_charter");
            var row = _data.cardCollection.FindOrAdd(CardCollectionCatalogue.IdOf(card));
            row.level = CardCollection.MaxLevel;
            row.duplicates = 500;

            var service = Service();

            Assert.That(service.CanUpgrade(card), Is.False);
            Assert.That(service.TryUpgrade(card), Is.False);
            Assert.That(service.LevelOf(card), Is.EqualTo(CardCollection.MaxLevel));
            Assert.That(service.DuplicatesOf(card), Is.EqualTo(500), "and nothing is taken");
        }

        [Test]
        public void TheWholeLadderCostsExactlyWhatTheCurveSays()
        {
            int card = CardId("deep_waters/admiralty_seal");   // Legendary: 1, 2, 4, 7 = 14
            var row = _data.cardCollection.FindOrAdd(CardCollectionCatalogue.IdOf(card));
            row.level = 1;
            row.duplicates = CardCollection.DuplicatesToMax(RosterCardState.Rarity.Legendary,
                                                            CardCollection.Tuning.Default);

            var service = Service();

            for (int level = 1; level < CardCollection.MaxLevel; level++)
                Assert.That(service.TryUpgrade(card), Is.True, $"level {level} to {level + 1}");

            Assert.That(service.LevelOf(card), Is.EqualTo(CardCollection.MaxLevel));
            Assert.That(service.DuplicatesOf(card), Is.Zero, "the curve should be spent exactly");
        }

        [Test]
        public void UpgradeReadyCountIsWhatTheOpenerBadgeShows()
        {
            var service = Service();
            Assert.That(service.UpgradeReadyCount(), Is.Zero);

            foreach (string id in new[] { "deep_waters/rope_coil", "workshop_guild/bellows" })
            {
                var row = _data.cardCollection.FindOrAdd(id);
                row.level = 1;
                row.duplicates = 2;
            }

            Assert.That(Service().UpgradeReadyCount(), Is.EqualTo(2));
        }

        // ---- effects ------------------------------------------------------------------------------

        [Test]
        public void ACardIsWorthSomethingFromLevelOneOnward()
        {
            int card = CardId("coal_industry/ore_scale");   // Common income, .002 per level
            var service = Service(Draws(card));
            service.GrantPacks(1, CardCollection.PackSource.Daily);
            service.TryOpenPack(out _);

            Assert.That(service.Effects.Income, Is.EqualTo(0.002d).Within(1e-12));
            Assert.That(service.Effects.IncomeMultiplier, Is.EqualTo(1.002d).Within(1e-12));
        }

        [Test]
        public void LevellingACardRecomputesWhatItIsWorth()
        {
            int card = CardId("deep_waters/rope_coil");     // Common salvage, .006 per level
            var row = _data.cardCollection.FindOrAdd(CardCollectionCatalogue.IdOf(card));
            row.level = 1;
            row.duplicates = 2;

            var service = Service();
            Assert.That(service.Effects.SeaSalvage, Is.EqualTo(0.006d).Within(1e-12));

            service.TryUpgrade(card);
            Assert.That(service.Effects.SeaSalvage, Is.EqualTo(0.012d).Within(1e-12));
        }

        [Test]
        public void EffectsOfTheSameKindAddUpAcrossCards()
        {
            // Two Common salvage cards at level 1 = .006 + .006.
            foreach (string id in new[] { "deep_waters/tide_table", "deep_waters/rope_coil" })
                _data.cardCollection.FindOrAdd(id).level = 1;

            Assert.That(Service().Effects.SeaSalvage, Is.EqualTo(0.012d).Within(1e-12));
        }

        [Test]
        public void ReadingTheSnapshotTwiceGivesTheSameAnswerWithoutRecomputing()
        {
            _data.cardCollection.FindOrAdd("coal_industry/ore_scale").level = 3;
            var service = Service();

            CardCollectionEffects first = service.Effects;
            CardCollectionEffects again = service.Effects;

            Assert.That(again.Income, Is.EqualTo(first.Income));
            Assert.That(first.Income, Is.EqualTo(0.006d).Within(1e-12));
        }

        [Test]
        public void TheDropChanceIsNotAMultiplierAndSaysSo()
        {
            _data.cardCollection.FindOrAdd("workshop_guild/guild_charter").level = 5;
            var service = Service();

            Assert.That(service.Effects.CraftPointDrop, Is.EqualTo(0.05d).Within(1e-12));
            Assert.That(service.Effects.Multiplier(CardCollection.EffectKind.CraftPointDropChance),
                        Is.EqualTo(1d), "multiplying a probability by 1+p is a bug that looks plausible");
        }

        // ---- set completion -----------------------------------------------------------------------

        /// <summary>Owns every card of a set but one, and returns the one still missing.</summary>
        private static int AlmostComplete(int set)
        {
            int[] cards = CardCollectionCatalogue.CardsInSet(set);
            for (int i = 0; i < cards.Length - 1; i++)
                _data.cardCollection.FindOrAdd(CardCollectionCatalogue.IdOf(cards[i])).level = 1;
            return cards[cards.Length - 1];
        }

        [Test]
        public void ASetIsIncompleteUntilItsLastCardArrives()
        {
            int set = CardCollectionCatalogue.SetIndexOf("coal_industry");
            int missing = AlmostComplete(set);

            var service = Service(Draws(missing));
            CollectionSetState before = service.SetState(set);

            Assert.That(before.Complete, Is.False);
            Assert.That(before.OwnedCount, Is.EqualTo(7));
            Assert.That(before.TotalCount, Is.EqualTo(8));
            Assert.That(before.Remaining, Is.EqualTo(1));
            Assert.That(before.BonusActive, Is.False);
            Assert.That(before.RewardClaimable, Is.False);

            service.GrantPacks(1, CardCollection.PackSource.Daily);
            Assert.That(service.TryOpenPack(out var receipt), Is.True);

            Assert.That(receipt.CompletedSet, Is.True);
            Assert.That(receipt.Set, Is.EqualTo(set));

            CollectionSetState after = service.SetState(set);
            Assert.That(after.Complete, Is.True);
            Assert.That(after.BonusActive, Is.True);
            Assert.That(after.RewardClaimable, Is.True);
        }

        [Test]
        public void ThePermanentBonusIsLiveBeforeTheRewardIsClaimed()
        {
            // The whole reason the two are separate facts: forgetting to press Claim must never cost
            // a player a bonus they earned.
            int set = CardCollectionCatalogue.SetIndexOf("coal_industry");
            foreach (int card in CardCollectionCatalogue.CardsInSet(set))
                _data.cardCollection.FindOrAdd(CardCollectionCatalogue.IdOf(card)).level = 1;

            var service = Service();

            Assert.That(service.SetState(set).RewardClaimed, Is.False);
            Assert.That(service.SetState(set).BonusActive, Is.True);

            // Six income cards at level 1 (.002 x3, .004, .006, .010) plus the +0.08 set bonus.
            Assert.That(service.Effects.Income, Is.EqualTo(0.006d + 0.004d + 0.006d + 0.010d + 0.08d)
                                                  .Within(1e-12));
        }

        [Test]
        public void ClaimingASetRewardPaysItExactlyOnce()
        {
            int set = CardCollectionCatalogue.SetIndexOf("coal_industry");
            foreach (int card in CardCollectionCatalogue.CardsInSet(set))
                _data.cardCollection.FindOrAdd(CardCollectionCatalogue.IdOf(card)).level = 1;

            var service = Service();
            long before = _wallet.Gems;

            Assert.That(service.CanClaimSetReward(set), Is.True);
            Assert.That(service.TryClaimSetReward(set, out var receipt), Is.True);

            Assert.That(receipt.Kind, Is.EqualTo(CardCollection.SetRewardKind.Gems));
            Assert.That(receipt.Amount, Is.EqualTo(400L));
            Assert.That(receipt.Paid, Is.True);
            Assert.That(_wallet.Gems - before, Is.EqualTo(400L));

            // And never again.
            Assert.That(service.CanClaimSetReward(set), Is.False);
            Assert.That(service.TryClaimSetReward(set, out _), Is.False);
            Assert.That(_wallet.Gems - before, Is.EqualTo(400L));
        }

        [Test]
        public void ClaimingSurvivesAReloadAndStillCannotBeRepeated()
        {
            int set = CardCollectionCatalogue.SetIndexOf("coal_industry");
            foreach (int card in CardCollectionCatalogue.CardsInSet(set))
                _data.cardCollection.FindOrAdd(CardCollectionCatalogue.IdOf(card)).level = 1;

            Service().TryClaimSetReward(set, out _);

            // Round trip through the real serialiser, then a fresh service on the loaded save.
            _data = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(_data));
            _wallet = new WalletService(_data.wallet);
            var reloaded = Service();

            Assert.That(reloaded.SetState(set).Complete, Is.True);
            Assert.That(reloaded.SetState(set).BonusActive, Is.True, "the bonus survives too");
            Assert.That(reloaded.CanClaimSetReward(set), Is.False);
            Assert.That(reloaded.TryClaimSetReward(set, out _), Is.False);
        }

        [Test]
        public void AnIncompleteSetPaysNothing()
        {
            int set = CardCollectionCatalogue.SetIndexOf("deep_waters");
            AlmostComplete(set);

            var service = Service();
            long before = _wallet.Gems;

            Assert.That(service.CanClaimSetReward(set), Is.False);
            Assert.That(service.TryClaimSetReward(set, out _), Is.False);
            Assert.That(_wallet.Gems, Is.EqualTo(before));
        }

        [Test]
        public void ARewardWithNobodyToPayItIsReportedUnpaidRatherThanCelebrated()
        {
            // deep_waters pays charts, and CaptainService is not wired in this test.
            int set = CardCollectionCatalogue.SetIndexOf("deep_waters");
            foreach (int card in CardCollectionCatalogue.CardsInSet(set))
                _data.cardCollection.FindOrAdd(CardCollectionCatalogue.IdOf(card)).level = 1;

            var service = Service();
            Assert.That(service.TryClaimSetReward(set, out var receipt), Is.True);
            Assert.That(receipt.Paid, Is.False);
            Assert.That(receipt.Kind, Is.EqualTo(CardCollection.SetRewardKind.Charts));
        }

        [Test]
        public void AnUnknownSetIdIsRefusedRatherThanResolvingToSetZero()
        {
            var service = Service();

            Assert.That(service.SetState("no_such_set").Exists, Is.False);
            Assert.That(service.SetState("no_such_set").Complete, Is.False);
            Assert.That(service.CanClaimSetReward("no_such_set"), Is.False);
            Assert.That(service.TryClaimSetReward("no_such_set", out _), Is.False);
        }

        [Test]
        public void ClaimableRewardCountIsWhatTheOpenerBadgeShows()
        {
            var service = Service();
            Assert.That(service.ClaimableSetRewardCount(), Is.Zero);

            foreach (int card in CardCollectionCatalogue.CardsInSet(
                         CardCollectionCatalogue.SetIndexOf("workshop_guild")))
                _data.cardCollection.FindOrAdd(CardCollectionCatalogue.IdOf(card)).level = 1;

            Assert.That(Service().ClaimableSetRewardCount(), Is.EqualTo(1));
        }

        // ---- persistence --------------------------------------------------------------------------

        [Test]
        public void EverythingTheServiceDidSurvivesAReload()
        {
            int card = CardId("deep_waters/rope_coil");
            var service = Service(Draws(card, card, card));
            service.TryClaimDailyPack();
            service.GrantPacks(3, CardCollection.PackSource.AchievementMilestone);
            service.TryOpenPack(out _);
            service.TryOpenPack(out _);
            service.TryOpenPack(out _);
            service.TryUpgrade(card);

            int packsLeft = service.UnopenedPackCount;
            int opened = service.PacksOpened;
            double salvage = service.Effects.SeaSalvage;

            _data = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(_data));
            _wallet = new WalletService(_data.wallet);
            var reloaded = Service();

            Assert.That(reloaded.UnopenedPackCount, Is.EqualTo(packsLeft));
            Assert.That(reloaded.PacksOpened, Is.EqualTo(opened));
            Assert.That(reloaded.LevelOf(card), Is.EqualTo(2));
            Assert.That(reloaded.DuplicatesOf(card), Is.Zero);
            Assert.That(reloaded.Effects.SeaSalvage, Is.EqualTo(salvage).Within(1e-12));
            Assert.That(reloaded.DailyPackReady, Is.False, "today's pack is still spent");
        }

        // ---- the shared card grammar --------------------------------------------------------------

        [Test]
        public void CardStateSpeaksTheGrammarEveryRosterScreenAlreadyUses()
        {
            int card = CardId("deep_waters/rope_coil");
            var row = _data.cardCollection.FindOrAdd(CardCollectionCatalogue.IdOf(card));
            row.level = 1;
            row.duplicates = 2;

            RosterCardState state = Service().CardState(card);

            Assert.That(state.Owned, Is.True);
            Assert.That(state.IsMaxed, Is.False);
            Assert.That(state.CanUpgrade, Is.True);
            Assert.That(state.Level, Is.EqualTo(1));
            Assert.That(state.MaxLevel, Is.EqualTo(CardCollection.MaxLevel));
            Assert.That(state.DuplicatesRequired, Is.EqualTo(2));
            Assert.That(state.Tier, Is.EqualTo(RosterCardState.Rarity.Common));
            Assert.That(state.Role, Is.EqualTo((int)CardCollection.EffectKind.SeaSalvageMultiplier),
                        "Role carries the effect kind — what the card DOES");
            Assert.That(state.Busy, Is.False);
            Assert.That(state.Progress, Is.EqualTo(1f));
        }

        [Test]
        public void AnUnknownCardIdReadsAsLockedRatherThanAsCardZero()
        {
            RosterCardState state = Service().CardState("no_such_set/no_such_card");

            Assert.That(state.Owned, Is.False);
            Assert.That(state.CanUpgrade, Is.False);
            Assert.That(state.Level, Is.Zero);
        }

        [Test]
        public void TheExistingRosterQueryCanFilterAndSortTheCollection()
        {
            // The reason CardState returns RosterCardState at all: slice 7 gets its filters and sorts
            // without a line of new code.
            _data.cardCollection.FindOrAdd("deep_waters/rope_coil").level = 1;
            _data.cardCollection.Find("deep_waters/rope_coil").duplicates = 2;
            _data.cardCollection.FindOrAdd("coal_industry/ore_scale").level = 1;

            var service = Service();
            var cards = new RosterCardState[CardCollectionCatalogue.Count];
            for (int i = 0; i < cards.Length; i++) cards[i] = service.CardState(i);

            var order = new int[cards.Length];

            int owned = RosterCardQuery.Fill(cards, cards.Length, RosterSortMode.Default,
                                             RosterFilterMode.Owned, order);
            Assert.That(owned, Is.EqualTo(2));

            int ready = RosterCardQuery.Fill(cards, cards.Length, RosterSortMode.UpgradeReady,
                                             RosterFilterMode.UpgradeReady, order);
            Assert.That(ready, Is.EqualTo(1));
            Assert.That(CardCollectionCatalogue.IdOf(order[0]), Is.EqualTo("deep_waters/rope_coil"));

            int locked = RosterCardQuery.Fill(cards, cards.Length, RosterSortMode.Default,
                                              RosterFilterMode.Locked, order);
            Assert.That(locked, Is.EqualTo(CardCollectionCatalogue.Count - 2));
        }

        // ---- events -------------------------------------------------------------------------------

        [Test]
        public void ChangedFiresOnceForEachRealChangeAndNotForARefusedOne()
        {
            int card = CardId("workshop_guild/bellows");
            var service = Service(Draws(card));

            int fired = 0;
            service.Changed += () => fired++;

            service.TryClaimDailyPack();
            Assert.That(fired, Is.EqualTo(1));

            service.TryClaimDailyPack();
            Assert.That(fired, Is.EqualTo(1), "a refused claim is not a change");

            service.TryOpenPack(out _);
            Assert.That(fired, Is.EqualTo(2));

            service.TryOpenPack(out _);
            Assert.That(fired, Is.EqualTo(2), "no pack left, so nothing happened");
        }
    }
}
