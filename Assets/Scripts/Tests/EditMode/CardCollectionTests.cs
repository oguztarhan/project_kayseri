using NUnit.Framework;
using Game.Core;

namespace Game.Tests
{
    /// <summary>
    /// The collection's arithmetic: what a level costs, what a card is worth, what a cap holds back
    /// and what an unusable copy turns into. Every function here is pure, so the whole balance table
    /// is asserted rather than described.
    /// </summary>
    public class CardCollectionTests
    {
        private static CardCollection.Tuning T => CardCollection.Tuning.Default;

        private const RosterCardState.Rarity C = RosterCardState.Rarity.Common;
        private const RosterCardState.Rarity R = RosterCardState.Rarity.Rare;
        private const RosterCardState.Rarity E = RosterCardState.Rarity.Epic;
        private const RosterCardState.Rarity L = RosterCardState.Rarity.Legendary;
        private const RosterCardState.Rarity M = RosterCardState.Rarity.Mythic;

        // ---- the duplicate curve ------------------------------------------------------------------

        [Test]
        public void TheCurveIsTheOneWrittenDownInThePlan()
        {
            int[][] expected =
            {
                new[] { 2, 4, 8, 14 },   // Common
                new[] { 2, 4, 7, 12 },   // Rare
                new[] { 1, 3, 5,  9 },   // Epic
                new[] { 1, 2, 4,  7 },   // Legendary
                new[] { 1, 2, 3,  5 },   // Mythic
            };

            for (int r = 0; r < CardCollection.RarityCount; r++)
                for (int level = 1; level < CardCollection.MaxLevel; level++)
                    Assert.That(CardCollection.DuplicatesToLevel((RosterCardState.Rarity)r, level, T),
                                Is.EqualTo(expected[r][level - 1]),
                                $"rarity {r}, level {level} to {level + 1}");
        }

        [Test]
        public void TotalsToMaxMatchTheCurve()
        {
            Assert.That(CardCollection.DuplicatesToMax(C, T), Is.EqualTo(28));
            Assert.That(CardCollection.DuplicatesToMax(R, T), Is.EqualTo(25));
            Assert.That(CardCollection.DuplicatesToMax(E, T), Is.EqualTo(18));
            Assert.That(CardCollection.DuplicatesToMax(L, T), Is.EqualTo(14));
            Assert.That(CardCollection.DuplicatesToMax(M, T), Is.EqualTo(11));
        }

        [Test]
        public void ARarerCardNeverAsksForMoreCopiesThanACommonerOne()
        {
            // The inversion Captains.cs measured: rarity is how hard the card is to FIND, so making
            // the rare one dear as well puts its ceiling out of reach.
            for (int r = 1; r < CardCollection.RarityCount; r++)
            {
                var rarer = (RosterCardState.Rarity)r;
                var commoner = (RosterCardState.Rarity)(r - 1);
                Assert.That(CardCollection.DuplicatesToMax(rarer, T),
                            Is.LessThanOrEqualTo(CardCollection.DuplicatesToMax(commoner, T)),
                            $"{rarer} should not cost more to max than {commoner}");
            }
        }

        [Test]
        public void EveryRungCostsAtLeastOneCopyAndTheCurveNeverFalls()
        {
            for (int r = 0; r < CardCollection.RarityCount; r++)
            {
                var rarity = (RosterCardState.Rarity)r;
                int previous = 0;
                for (int level = 1; level < CardCollection.MaxLevel; level++)
                {
                    int cost = CardCollection.DuplicatesToLevel(rarity, level, T);
                    Assert.That(cost, Is.GreaterThanOrEqualTo(1), "a level that costs nothing is not a level");
                    Assert.That(cost, Is.GreaterThanOrEqualTo(previous), "a later rung should never be cheaper");
                    previous = cost;
                }
            }
        }

        [Test]
        public void AnUnownedCardAndAMaxedCardBothHaveNoNextRung()
        {
            Assert.That(CardCollection.DuplicatesToLevel(C, CardCollection.NotOwned, T), Is.Zero);
            Assert.That(CardCollection.DuplicatesToLevel(C, CardCollection.MaxLevel, T), Is.Zero);
            Assert.That(CardCollection.DuplicatesToLevel(C, CardCollection.MaxLevel + 3, T), Is.Zero);
            Assert.That(CardCollection.DuplicatesToLevel(C, -2, T), Is.Zero);
        }

        [Test]
        public void CopiesStillOwedCountDownAsTheCardClimbs()
        {
            Assert.That(CardCollection.DuplicatesFrom(C, 1, T), Is.EqualTo(28));
            Assert.That(CardCollection.DuplicatesFrom(C, 2, T), Is.EqualTo(26));
            Assert.That(CardCollection.DuplicatesFrom(C, 4, T), Is.EqualTo(14));
            Assert.That(CardCollection.DuplicatesFrom(C, CardCollection.MaxLevel, T), Is.Zero);
        }

        [Test]
        public void AHalfFilledConfigFallsBackToTheShippedCurve()
        {
            // A ScriptableObject saved before its array was filled must cost the player balance, not
            // a session.
            var missing = T;
            missing.DuplicateCurve = null;
            Assert.That(CardCollection.DuplicatesToMax(C, missing), Is.EqualTo(28));

            var truncated = T;
            truncated.DuplicateCurve = new[] { 9, 9 };
            Assert.That(CardCollection.DuplicatesToLevel(C, 1, truncated), Is.EqualTo(9),
                        "a value that IS configured should still be honoured");
            Assert.That(CardCollection.DuplicatesToLevel(E, 1, truncated), Is.EqualTo(1),
                        "and one past the end should fall back");
        }

        [Test]
        public void ARarityOffTheEnumCostsNothingRatherThanThrowing()
        {
            var bogus = (RosterCardState.Rarity)99;
            Assert.DoesNotThrow(() => CardCollection.DuplicatesToMax(bogus, T));
            Assert.That(CardCollection.DuplicatesToMax(bogus, T), Is.Zero);
            Assert.That(CardCollection.DuplicatesToLevel(bogus, 1, T), Is.Zero);
        }

        // ---- what a card is worth -----------------------------------------------------------------

        [Test]
        public void ACardsWorthIsLinearInItsLevel()
        {
            for (int level = 1; level <= CardCollection.MaxLevel; level++)
                Assert.That(CardCollection.CardEffect(0.006d, level),
                            Is.EqualTo(0.006d * level).Within(1e-12));
        }

        [Test]
        public void AnUnownedCardIsWorthNothing()
        {
            Assert.That(CardCollection.CardEffect(0.010d, CardCollection.NotOwned), Is.Zero);
            Assert.That(CardCollection.CardEffect(0.010d, -4), Is.Zero);
        }

        [Test]
        public void AHandEditedLevelCannotBuyMoreThanAMaxedCard()
        {
            Assert.That(CardCollection.CardEffect(0.010d, 99),
                        Is.EqualTo(CardCollection.CardEffect(0.010d, CardCollection.MaxLevel)));
        }

        [Test]
        public void TheLaunchIncomeCardsLandWhereThePlanSaysTheyDo()
        {
            // Seven income cards: 3 Common at .002, 1 Rare at .004, 2 Epic at .006, 1 Legendary at
            // .010 — all at level 5, plus the coal_industry set bonus of +8%.
            double cards = 3 * CardCollection.CardEffect(0.002d, 5)
                         + 1 * CardCollection.CardEffect(0.004d, 5)
                         + 2 * CardCollection.CardEffect(0.006d, 5)
                         + 1 * CardCollection.CardEffect(0.010d, 5);

            Assert.That(cards, Is.EqualTo(0.16d).Within(1e-9), "cards alone should be +16%");

            double withSet = cards + 0.08d;
            Assert.That(withSet, Is.EqualTo(0.24d).Within(1e-9), "with the set bonus, +24%");
            Assert.That(withSet,
                        Is.LessThan(CardCollection.CapOf(CardCollection.EffectKind.IncomeMultiplier, T)),
                        "the launch catalogue must sit under its own cap, or the cap is doing the balancing");
        }

        // ---- caps ---------------------------------------------------------------------------------

        [Test]
        public void AnAggregateIsHeldToItsCap()
        {
            var kind = CardCollection.EffectKind.IncomeMultiplier;
            Assert.That(CardCollection.Capped(kind, 0.10d, T), Is.EqualTo(0.10d).Within(1e-12));
            Assert.That(CardCollection.Capped(kind, 5d, T), Is.EqualTo(CardCollection.CapOf(kind, T)));
        }

        [Test]
        public void ANegativeOrNonsenseAggregateReadsAsNothing()
        {
            var kind = CardCollection.EffectKind.SeaChartMultiplier;
            Assert.That(CardCollection.Capped(kind, -1d, T), Is.Zero);
            Assert.That(CardCollection.Capped(kind, double.NaN, T), Is.Zero);
        }

        [Test]
        public void EveryEffectKindHasACap()
        {
            for (int k = 0; k < CardCollection.EffectKindCount; k++)
                Assert.That(CardCollection.CapOf((CardCollection.EffectKind)k, T),
                            Is.GreaterThan(0d), $"effect {k} has no cap");
        }

        [Test]
        public void EveryCapLeavesTheLaunchCatalogueRoomToBreathe()
        {
            // A cap exists to bound what a content set added LATER can compound into. The moment it
            // clips a shipped value it has quietly become the thing doing the balancing, and the
            // published balance table stops describing the game. Every one of these was checked by
            // hand once; this is what stops the next edit closing the gap without anyone noticing.
            var launchTotals = new (CardCollection.EffectKind Kind, double Completed, string Where)[]
            {
                (CardCollection.EffectKind.IncomeMultiplier,     0.24d, "7 cards + coal_industry"),
                (CardCollection.EffectKind.CraftXpMultiplier,    0.50d, "6 cards, no set bonus"),
                (CardCollection.EffectKind.CraftPointDropChance, 0.16d, "3 cards + workshop_guild"),
                (CardCollection.EffectKind.SeaSalvageMultiplier, 0.34d, "4 cards + deep_waters"),
                (CardCollection.EffectKind.SeaChartMultiplier,   0.28d, "4 cards, no set bonus"),
            };

            foreach (var row in launchTotals)
            {
                double cap = CardCollection.CapOf(row.Kind, T);
                Assert.That(row.Completed, Is.LessThan(cap),
                            $"{row.Kind} at full completion ({row.Where}) is clipped by its own cap");
                Assert.That(CardCollection.Capped(row.Kind, row.Completed, T),
                            Is.EqualTo(row.Completed).Within(1e-12),
                            $"{row.Kind} should pass through the cap untouched at launch");
            }
        }

        [Test]
        public void AConfigMissingItsCapsFallsBackToTheShippedOnes()
        {
            var missing = T;
            missing.EffectCaps = null;
            Assert.That(CardCollection.CapOf(CardCollection.EffectKind.IncomeMultiplier, missing),
                        Is.EqualTo(0.30d));
            Assert.That(CardCollection.Capped(CardCollection.EffectKind.IncomeMultiplier, 9d, missing),
                        Is.EqualTo(0.30d));
        }

        [Test]
        public void OnlyTheDropChanceIsNotAMultiplier()
        {
            Assert.That(CardCollection.IsMultiplier(CardCollection.EffectKind.CraftPointDropChance), Is.False);

            foreach (var kind in new[]
            {
                CardCollection.EffectKind.IncomeMultiplier,
                CardCollection.EffectKind.CraftXpMultiplier,
                CardCollection.EffectKind.SeaSalvageMultiplier,
                CardCollection.EffectKind.SeaChartMultiplier,
            })
                Assert.That(CardCollection.IsMultiplier(kind), Is.True, $"{kind} should read as a multiplier");
        }

        [Test]
        public void AnAggregateReadsAsAMultiplierOnTopOfOne()
        {
            Assert.That(CardCollection.AsMultiplier(0d), Is.EqualTo(1d));
            Assert.That(CardCollection.AsMultiplier(-1d), Is.EqualTo(1d));
            Assert.That(CardCollection.AsMultiplier(0.24d), Is.EqualTo(1.24d).Within(1e-12));
        }

        // ---- the craft point ceiling --------------------------------------------------------------

        [Test]
        public void TheDropChanceAddsToTheWorkshopsBase()
        {
            double baseChance = Crafting.Tuning.Default.PointDropChance;   // 0.20
            Assert.That(CardCollection.CraftPointChance(baseChance, 0d, T),
                        Is.EqualTo(baseChance).Within(1e-12));
            Assert.That(CardCollection.CraftPointChance(baseChance, 0.16d, T),
                        Is.EqualTo(0.36d).Within(1e-12), "the fully completed launch collection");
        }

        [Test]
        public void TheDropChanceIsHeldByBothItsCapAndItsCeiling()
        {
            double baseChance = Crafting.Tuning.Default.PointDropChance;

            // The cap bounds what the collection may contribute. Asserted from a base low enough
            // that the cap, not the ceiling, is the thing biting — otherwise the two clamps are
            // indistinguishable and either could rot unnoticed.
            double capped = CardCollection.CraftPointChance(0.10d, 9d, T);
            Assert.That(capped, Is.EqualTo(0.10d + CardCollection.CapOf(
                            CardCollection.EffectKind.CraftPointDropChance, T)).Within(1e-12));
            Assert.That(capped, Is.LessThan(T.CraftPointChanceCeiling),
                        "this case must be bounded by the cap, not by the ceiling");

            // …and the ceiling bounds the number the dice are rolled against, whatever the base is.
            Assert.That(CardCollection.CraftPointChance(0.95d, 9d, T),
                        Is.EqualTo(T.CraftPointChanceCeiling).Within(1e-12));
        }

        [Test]
        public void TheDropChanceCanNeverReachCertainty()
        {
            var reckless = T;
            reckless.CraftPointChanceCeiling = 5d;
            Assert.That(CardCollection.CraftPointChance(0.99d, 9d, reckless), Is.EqualTo(1d));
        }

        [Test]
        public void NonsenseInputsToTheDropChanceReadAsZero()
        {
            Assert.That(CardCollection.CraftPointChance(double.NaN, double.NaN, T), Is.Zero);
            Assert.That(CardCollection.CraftPointChance(-1d, -1d, T), Is.Zero);
        }

        // ---- overflow -----------------------------------------------------------------------------

        [Test]
        public void AnUnusableCopyIsWorthGemsByRarity()
        {
            Assert.That(CardCollection.OverflowGems(C, T), Is.EqualTo(5L));
            Assert.That(CardCollection.OverflowGems(R, T), Is.EqualTo(10L));
            Assert.That(CardCollection.OverflowGems(E, T), Is.EqualTo(20L));
            Assert.That(CardCollection.OverflowGems(L, T), Is.EqualTo(40L));
            Assert.That(CardCollection.OverflowGems(M, T), Is.EqualTo(75L));
        }

        [Test]
        public void OverflowNeverPaysLessForARarerCard()
        {
            for (int r = 1; r < CardCollection.RarityCount; r++)
                Assert.That(CardCollection.OverflowGems((RosterCardState.Rarity)r, T),
                            Is.GreaterThan(CardCollection.OverflowGems((RosterCardState.Rarity)(r - 1), T)));
        }

        [Test]
        public void AMisconfiguredOverflowTableFallsBackRatherThanPayingNothing()
        {
            var missing = T;
            missing.OverflowGems = null;
            Assert.That(CardCollection.OverflowGems(E, missing), Is.EqualTo(20L));

            var negative = T;
            negative.OverflowGems = new[] { -5L, 10L, 20L, 40L, 75L };
            Assert.That(CardCollection.OverflowGems(C, negative), Is.Zero, "never a negative payout");
        }

        // ---- levelling ----------------------------------------------------------------------------

        [Test]
        public void ACardLevelsOnlyWhenOwnedBelowMaxAndHoldingEnough()
        {
            Assert.That(CardCollection.CanLevel(C, 1, 1, T), Is.False, "one short of the two it needs");
            Assert.That(CardCollection.CanLevel(C, 1, 2, T), Is.True);
            Assert.That(CardCollection.CanLevel(C, 1, 40, T), Is.True, "spare copies do not hurt");
        }

        [Test]
        public void DuplicatesAloneCanNeverUnlockACard()
        {
            // Only a pack draw creates level 1. A save arriving with copies on an unowned card must
            // not be able to promote itself.
            Assert.That(CardCollection.CanLevel(C, CardCollection.NotOwned, 999, T), Is.False);
            Assert.That(CardCollection.IsOwned(CardCollection.NotOwned), Is.False);
        }

        [Test]
        public void AMaxedCardHasNoUpgradeLeftHoweverManyCopiesItHolds()
        {
            Assert.That(CardCollection.CanLevel(C, CardCollection.MaxLevel, 999, T), Is.False);
            Assert.That(CardCollection.IsMaxed(CardCollection.MaxLevel), Is.True);
            Assert.That(CardCollection.IsMaxed(CardCollection.MaxLevel + 5), Is.True);
        }

        [Test]
        public void LevelsAreClampedToSomethingACardCanActuallyBeAt()
        {
            Assert.That(CardCollection.ClampLevel(-3), Is.EqualTo(CardCollection.NotOwned));
            Assert.That(CardCollection.ClampLevel(0), Is.EqualTo(0));
            Assert.That(CardCollection.ClampLevel(3), Is.EqualTo(3));
            Assert.That(CardCollection.ClampLevel(99), Is.EqualTo(CardCollection.MaxLevel));
        }

        [Test]
        public void SpendingTheWholeCurveTakesACardFromOneToMax()
        {
            // Walks the ladder the way the service will: bank the cost, spend it, climb one.
            foreach (var rarity in new[] { C, R, E, L, M })
            {
                int level = 1, spent = 0;
                while (level < CardCollection.MaxLevel)
                {
                    int need = CardCollection.DuplicatesToLevel(rarity, level, T);
                    Assert.That(CardCollection.CanLevel(rarity, level, need, T), Is.True);
                    spent += need;
                    level++;
                }
                Assert.That(level, Is.EqualTo(CardCollection.MaxLevel));
                Assert.That(spent, Is.EqualTo(CardCollection.DuplicatesToMax(rarity, T)));
            }
        }

        // ---- the shared ladder --------------------------------------------------------------------

        [Test]
        public void TheCollectionSpeaksTheSameFiveRungLadderAsEveryOtherRoster()
        {
            // Not a fourth copy of the same five names — the rarity type IS RosterCardState.Rarity,
            // which is what lets the collection reuse kaptan.derece.* in eleven languages.
            Assert.That(CardCollection.RarityCount, Is.EqualTo(5));
            Assert.That((int)RosterCardState.Rarity.Mythic, Is.EqualTo(CardCollection.RarityCount - 1));
            Assert.That(CardCollection.MaxLevel, Is.EqualTo(Captains.MaxLevel),
                        "three rosters with three ladders is three things to learn");
            Assert.That(CardCollection.MaxLevel, Is.EqualTo(Foremen.MaxStars));
        }
    }
}
