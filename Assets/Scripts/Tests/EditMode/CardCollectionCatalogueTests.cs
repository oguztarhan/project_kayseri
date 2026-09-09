using System.Collections.Generic;
using NUnit.Framework;
using Game.Core;

namespace Game.Tests
{
    /// <summary>
    /// The authored catalogue, held against the balance table that was approved before it was
    /// written. Docs/PLAN_14 states what the completed collection is worth; these tests DERIVE those
    /// figures from the twenty-four real cards, so the document and the game cannot quietly disagree
    /// — and a card whose rarity or effect is changed by hand will fail here rather than shipping.
    /// </summary>
    public class CardCollectionCatalogueTests
    {
        private static CardCollection.Tuning T => CardCollection.Tuning.Default;

        // ---- identity -----------------------------------------------------------------------------

        [Test]
        public void EveryCardIdIsUniqueWellFormedAndSafeAsASaveKey()
        {
            var seen = new HashSet<string>();
            foreach (var card in CardCollectionCatalogue.Cards)
            {
                Assert.That(card.Id, Is.Not.Null.And.Not.Empty);
                Assert.That(seen.Add(card.Id), Is.True, $"duplicate card id: {card.Id}");

                Assert.That(card.Id, Is.EqualTo(card.Id.ToLowerInvariant()),
                            $"{card.Id} should be lower case — ids are save keys, not display text");
                Assert.That(card.Id.Trim(), Is.EqualTo(card.Id), $"{card.Id} has stray whitespace");

                int separators = 0;
                foreach (char c in card.Id) if (c == CardCollectionCatalogue.IdSeparator) separators++;
                Assert.That(separators, Is.EqualTo(1),
                            $"{card.Id} should be exactly set_id/card_name");
            }
        }

        [Test]
        public void EverySetIdIsUniqueAndWellFormed()
        {
            var seen = new HashSet<string>();
            foreach (var set in CardCollectionCatalogue.Sets)
            {
                Assert.That(set.Id, Is.Not.Null.And.Not.Empty);
                Assert.That(seen.Add(set.Id), Is.True, $"duplicate set id: {set.Id}");
                Assert.That(set.Id.IndexOf(CardCollectionCatalogue.IdSeparator), Is.EqualTo(-1),
                            $"{set.Id} must not contain the card separator");
            }
        }

        [Test]
        public void ACardsIdAndItsSetCanNeverDisagree()
        {
            // Membership has ONE source. If a card's id says one set and its SetId another, the
            // collection screen and the completion check would be reading different catalogues.
            foreach (var card in CardCollectionCatalogue.Cards)
            {
                Assert.That(card.SetId, Is.EqualTo(CardCollectionCatalogue.SetIdIn(card.Id)),
                            $"{card.Id} declares set {card.SetId}");
                Assert.That(CardCollectionCatalogue.SetIndexOf(card.SetId), Is.GreaterThanOrEqualTo(0),
                            $"{card.Id} belongs to a set that does not exist");
            }
        }

        [Test]
        public void LookupsRoundTripAndUnknownIdsAreRefusedRatherThanGuessed()
        {
            for (int i = 0; i < CardCollectionCatalogue.Count; i++)
                Assert.That(CardCollectionCatalogue.IndexOf(CardCollectionCatalogue.IdOf(i)),
                            Is.EqualTo(i));

            // A save may hold a card this build does not carry — a rename, or a downgrade. It must
            // read as "not here", never as card 0.
            Assert.That(CardCollectionCatalogue.IndexOf("coal_industry/does_not_exist"), Is.EqualTo(-1));
            Assert.That(CardCollectionCatalogue.IndexOf(""), Is.EqualTo(-1));
            Assert.That(CardCollectionCatalogue.IndexOf(null), Is.EqualTo(-1));
            Assert.That(CardCollectionCatalogue.SetIndexOf("no_such_set"), Is.EqualTo(-1));
        }

        [Test]
        public void ACardOffTheCatalogueReadsAsNothingRatherThanThrowing()
        {
            Assert.That(CardCollectionCatalogue.Exists(-1), Is.False);
            Assert.That(CardCollectionCatalogue.Exists(CardCollectionCatalogue.Count), Is.False);
            Assert.That(CardCollectionCatalogue.IdOf(-1), Is.Empty);
            Assert.That(CardCollectionCatalogue.EffectValue(-1, 5, T), Is.Zero);
            Assert.That(CardCollectionCatalogue.DuplicatesToLevel(999, 1, T), Is.Zero);
            Assert.That(CardCollectionCatalogue.OverflowGems(999, T), Is.Zero);
            Assert.That(CardCollectionCatalogue.CardsInSet(99), Is.Empty);
        }

        // ---- shape --------------------------------------------------------------------------------

        [Test]
        public void TheLaunchCatalogueIsThreeSetsOfEight()
        {
            Assert.That(CardCollectionCatalogue.SetCount, Is.EqualTo(3));
            Assert.That(CardCollectionCatalogue.Count, Is.EqualTo(24));

            for (int s = 0; s < CardCollectionCatalogue.SetCount; s++)
                Assert.That(CardCollectionCatalogue.CardsInSetCount(s), Is.EqualTo(8),
                            $"set {CardCollectionCatalogue.Sets[s].Id} should hold eight cards");
        }

        [Test]
        public void EverySetCarriesTheSameRarityMix()
        {
            // Uniform on purpose: which set a player finishes first should be decided by luck, not
            // by which tab they happened to open.
            int[] expected = { 3, 2, 2, 1, 0 };

            for (int s = 0; s < CardCollectionCatalogue.SetCount; s++)
            {
                var mix = new int[CardCollection.RarityCount];
                foreach (int card in CardCollectionCatalogue.CardsInSet(s))
                    mix[(int)CardCollectionCatalogue.RarityOf(card)]++;

                Assert.That(mix, Is.EqualTo(expected),
                            $"set {CardCollectionCatalogue.Sets[s].Id} has mix "
                            + string.Join("/", mix));
            }
        }

        [Test]
        public void TheCensusIsTheOneThePackWasBalancedAgainst()
        {
            // 9 / 6 / 6 / 3 / 0 is what puts a named Common at 5.81% and a named Legendary at 1.84%,
            // and every pacing figure in Docs/PLAN_14 was simulated against exactly this.
            Assert.That(CardCollectionCatalogue.RarityCensus(), Is.EqualTo(new[] { 9, 6, 6, 3, 0 }));
        }

        [Test]
        public void NoCardCarriesMythicAtLaunchAndThePackKnowsIt()
        {
            Assert.That(CardCollectionCatalogue.CountOfRarity(RosterCardState.Rarity.Mythic), Is.Zero);
            Assert.That(CardCollectionPack.WeightOf(RosterCardState.Rarity.Mythic,
                            CardCollectionCatalogue.RarityCensus(), 0,
                            CardCollectionPack.Tuning.Default),
                        Is.Zero, "a rarity with no card must be unreachable, not merely unlikely");
        }

        [Test]
        public void TheCensusHandedToThePackIsACopy()
        {
            // The pack takes it as an argument, so a caller that writes to it must not be able to
            // rewrite the catalogue's own count.
            var census = CardCollectionCatalogue.RarityCensus();
            census[0] = 999;
            Assert.That(CardCollectionCatalogue.RarityCensus()[0], Is.EqualTo(9));
        }

        [Test]
        public void EveryCardOfARarityIsReachableThroughTheRarityIndex()
        {
            for (int r = 0; r < CardCollection.RarityCount; r++)
            {
                var rarity = (RosterCardState.Rarity)r;
                int n = CardCollectionCatalogue.CountOfRarity(rarity);

                var found = new HashSet<int>();
                for (int nth = 0; nth < n; nth++)
                {
                    int card = CardCollectionCatalogue.OfRarity(rarity, nth);
                    Assert.That(CardCollectionCatalogue.Exists(card), Is.True);
                    Assert.That(CardCollectionCatalogue.RarityOf(card), Is.EqualTo(rarity));
                    Assert.That(found.Add(card), Is.True, "the same card twice in one rarity");
                }
                Assert.That(found.Count, Is.EqualTo(n));

                Assert.That(CardCollectionCatalogue.OfRarity(rarity, n), Is.EqualTo(-1));
                Assert.That(CardCollectionCatalogue.OfRarity(rarity, -1), Is.EqualTo(-1));
            }
        }

        [Test]
        public void EveryCardBelongsToExactlyOneSetAndEverySetKnowsItsCards()
        {
            var counted = new HashSet<int>();
            for (int s = 0; s < CardCollectionCatalogue.SetCount; s++)
                foreach (int card in CardCollectionCatalogue.CardsInSet(s))
                {
                    Assert.That(counted.Add(card), Is.True, $"card {card} is in two sets");
                    Assert.That(CardCollectionCatalogue.SetIndexOfCard(card), Is.EqualTo(s));
                }

            Assert.That(counted.Count, Is.EqualTo(CardCollectionCatalogue.Count),
                        "every card should belong to a set");
        }

        // ---- the balance table --------------------------------------------------------------------

        /// <summary>Everything the collection adds to one effect at full completion: every card of
        /// that kind at max level, plus the set bonus if a set carries it.</summary>
        private static double CompletedTotal(CardCollection.EffectKind kind, bool withSetBonus)
        {
            double total = 0d;
            for (int card = 0; card < CardCollectionCatalogue.Count; card++)
                if (CardCollectionCatalogue.EffectOf(card) == kind)
                    total += CardCollectionCatalogue.EffectValue(card, CardCollection.MaxLevel, T);

            if (withSetBonus)
                foreach (var set in CardCollectionCatalogue.Sets)
                    if (set.Bonus == kind) total += set.BonusValue;

            return total;
        }

        [Test]
        public void TheCardsAloneAreWorthWhatThePlanSaysTheyAre()
        {
            Assert.That(CompletedTotal(CardCollection.EffectKind.IncomeMultiplier, false),
                        Is.EqualTo(0.160d).Within(1e-9));
            Assert.That(CompletedTotal(CardCollection.EffectKind.CraftXpMultiplier, false),
                        Is.EqualTo(0.500d).Within(1e-9));
            Assert.That(CompletedTotal(CardCollection.EffectKind.CraftPointDropChance, false),
                        Is.EqualTo(0.100d).Within(1e-9));
            Assert.That(CompletedTotal(CardCollection.EffectKind.SeaSalvageMultiplier, false),
                        Is.EqualTo(0.190d).Within(1e-9));
            Assert.That(CompletedTotal(CardCollection.EffectKind.SeaChartMultiplier, false),
                        Is.EqualTo(0.280d).Within(1e-9));
        }

        [Test]
        public void TheCompletedCollectionIsWorthWhatThePlanSaysItIs()
        {
            Assert.That(CompletedTotal(CardCollection.EffectKind.IncomeMultiplier, true),
                        Is.EqualTo(0.240d).Within(1e-9), "+24% income");
            Assert.That(CompletedTotal(CardCollection.EffectKind.CraftXpMultiplier, true),
                        Is.EqualTo(0.500d).Within(1e-9), "+50% craft XP");
            Assert.That(CompletedTotal(CardCollection.EffectKind.CraftPointDropChance, true),
                        Is.EqualTo(0.160d).Within(1e-9), "+0.16 drop chance");
            Assert.That(CompletedTotal(CardCollection.EffectKind.SeaSalvageMultiplier, true),
                        Is.EqualTo(0.340d).Within(1e-9), "+34% salvage");
            Assert.That(CompletedTotal(CardCollection.EffectKind.SeaChartMultiplier, true),
                        Is.EqualTo(0.280d).Within(1e-9), "+28% charts");
        }

        [Test]
        public void NoCapClipsTheRealCatalogue()
        {
            // The same invariant CardCollectionTests asserts against the published figures, checked
            // here against the cards that actually exist. A cap that bites at launch has stopped
            // bounding future content and started doing the balancing.
            for (int k = 0; k < CardCollection.EffectKindCount; k++)
            {
                var kind = (CardCollection.EffectKind)k;
                double completed = CompletedTotal(kind, true);
                Assert.That(completed, Is.LessThan(CardCollection.CapOf(kind, T)),
                            $"{kind} is clipped by its own cap at full completion");
            }
        }

        [Test]
        public void TheCompletedDropChanceStaysUnderItsCeiling()
        {
            double chance = CardCollection.CraftPointChance(
                Crafting.Tuning.Default.PointDropChance,
                CompletedTotal(CardCollection.EffectKind.CraftPointDropChance, true), T);

            Assert.That(chance, Is.EqualTo(0.36d).Within(1e-9),
                        "0.20 base plus the completed collection");
            Assert.That(chance, Is.LessThan(T.CraftPointChanceCeiling),
                        "the ceiling should still have room above the finished collection");
        }

        [Test]
        public void EveryEffectKindIsCarriedByAtLeastOneCard()
        {
            for (int k = 0; k < CardCollection.EffectKindCount; k++)
            {
                var kind = (CardCollection.EffectKind)k;
                bool carried = false;
                for (int card = 0; card < CardCollectionCatalogue.Count && !carried; card++)
                    carried = CardCollectionCatalogue.EffectOf(card) == kind;

                Assert.That(carried, Is.True, $"nothing in the catalogue does {kind}");
            }
        }

        [Test]
        public void EveryCardIsWorthMoreAtEveryLevel()
        {
            for (int card = 0; card < CardCollectionCatalogue.Count; card++)
            {
                Assert.That(CardCollectionCatalogue.EffectValue(card, CardCollection.NotOwned, T),
                            Is.Zero, "an unowned card is worth nothing");

                double previous = 0d;
                for (int level = 1; level <= CardCollection.MaxLevel; level++)
                {
                    double now = CardCollectionCatalogue.EffectValue(card, level, T);
                    Assert.That(now, Is.GreaterThan(previous),
                                $"{CardCollectionCatalogue.IdOf(card)} at level {level}");
                    previous = now;
                }
            }
        }

        [Test]
        public void ARarerCardIsAlwaysWorthMoreThanACommonerOneDoingTheSameJob()
        {
            for (int k = 0; k < CardCollection.EffectKindCount; k++)
            {
                var kind = (CardCollection.EffectKind)k;
                for (int r = 1; r < CardCollection.RarityCount; r++)
                    Assert.That(CardCollection.PerLevel(kind, (RosterCardState.Rarity)r, T),
                                Is.GreaterThan(CardCollection.PerLevel(kind, (RosterCardState.Rarity)(r - 1), T)),
                                $"{kind} at rarity {r}");
            }
        }

        // ---- sets ---------------------------------------------------------------------------------

        [Test]
        public void TheThreeSetBonusesAreTheApprovedOnesAndTouchThreeDifferentLoops()
        {
            var byId = new Dictionary<string, CardCollectionCatalogue.Set>();
            foreach (var set in CardCollectionCatalogue.Sets) byId[set.Id] = set;

            Assert.That(byId["coal_industry"].Bonus,
                        Is.EqualTo(CardCollection.EffectKind.IncomeMultiplier));
            Assert.That(byId["coal_industry"].BonusValue, Is.EqualTo(0.08d).Within(1e-9));

            Assert.That(byId["workshop_guild"].Bonus,
                        Is.EqualTo(CardCollection.EffectKind.CraftPointDropChance));
            Assert.That(byId["workshop_guild"].BonusValue, Is.EqualTo(0.06d).Within(1e-9));

            Assert.That(byId["deep_waters"].Bonus,
                        Is.EqualTo(CardCollection.EffectKind.SeaSalvageMultiplier));
            Assert.That(byId["deep_waters"].BonusValue, Is.EqualTo(0.15d).Within(1e-9));

            // Three loops, three numbers. Two sets moving the same one would stack invisibly.
            var kinds = new HashSet<CardCollection.EffectKind>();
            foreach (var set in CardCollectionCatalogue.Sets)
                Assert.That(kinds.Add(set.Bonus), Is.True, $"{set.Id} duplicates another set's bonus");
        }

        [Test]
        public void TheThreeOneTimeRewardsAreTheApprovedOnes()
        {
            var byId = new Dictionary<string, CardCollectionCatalogue.Set>();
            foreach (var set in CardCollectionCatalogue.Sets) byId[set.Id] = set;

            Assert.That(byId["coal_industry"].RewardKind,
                        Is.EqualTo(CardCollection.SetRewardKind.Gems));
            Assert.That(byId["coal_industry"].RewardAmount, Is.EqualTo(400L));

            Assert.That(byId["workshop_guild"].RewardKind,
                        Is.EqualTo(CardCollection.SetRewardKind.CraftPoints));
            Assert.That(byId["workshop_guild"].RewardAmount, Is.EqualTo(40L));

            Assert.That(byId["deep_waters"].RewardKind,
                        Is.EqualTo(CardCollection.SetRewardKind.Charts));
            Assert.That(byId["deep_waters"].RewardAmount, Is.EqualTo(250L));
        }

        [Test]
        public void EverySetPaysSomething()
        {
            foreach (var set in CardCollectionCatalogue.Sets)
            {
                Assert.That(set.RewardAmount, Is.GreaterThan(0L), $"{set.Id} pays nothing");
                Assert.That(set.BonusValue, Is.GreaterThan(0d), $"{set.Id} turns nothing on");
            }
        }

        // ---- localization -------------------------------------------------------------------------

        [Test]
        public void LocKeysAreDerivedFromIdsAndAreAllDistinct()
        {
            var keys = new HashSet<string>();
            for (int card = 0; card < CardCollectionCatalogue.Count; card++)
            {
                Assert.That(CardCollectionCatalogue.NameKey(card),
                            Is.EqualTo("koleksiyon.kart." + CardCollectionCatalogue.IdOf(card) + ".ad"));
                Assert.That(keys.Add(CardCollectionCatalogue.NameKey(card)), Is.True);
                Assert.That(keys.Add(CardCollectionCatalogue.DescriptionKey(card)), Is.True);
            }

            for (int s = 0; s < CardCollectionCatalogue.SetCount; s++)
            {
                Assert.That(keys.Add(CardCollectionCatalogue.SetNameKey(s)), Is.True);
                Assert.That(keys.Add(CardCollectionCatalogue.SetDescriptionKey(s)), Is.True);
            }

            // 24 cards and 3 sets, a name and a description each.
            Assert.That(keys.Count, Is.EqualTo(24 * 2 + 3 * 2));
        }
    }
}
