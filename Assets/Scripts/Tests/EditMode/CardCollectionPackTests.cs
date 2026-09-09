using NUnit.Framework;
using Game.Core;

namespace Game.Tests
{
    /// <summary>
    /// The pack. Because <see cref="CardCollectionPack"/> takes its roll and its pool as arguments,
    /// the whole distribution can be walked exactly rather than sampled and hoped over — every test
    /// here is deterministic, and the pity tests simulate thousands of packs in milliseconds.
    ///
    /// The pool used throughout is the LAUNCH catalogue: three sets of eight, each 3 Common, 2 Rare,
    /// 2 Epic and 1 Legendary, and no Mythic at all. If those counts change, the shares asserted in
    /// <see cref="TheDistributionMatchesTheWeights"/> change with them — which is the point of
    /// deriving them from the weights here rather than pasting percentages in.
    /// </summary>
    public class CardCollectionPackTests
    {
        private static CardCollectionPack.Tuning T => CardCollectionPack.Tuning.Default;

        /// <summary>The launch catalogue's rarity census: 9 / 6 / 6 / 3 / 0.</summary>
        private static int[] Pool => new[] { 9, 6, 6, 3, 0 };

        private const RosterCardState.Rarity C = RosterCardState.Rarity.Common;
        private const RosterCardState.Rarity R = RosterCardState.Rarity.Rare;
        private const RosterCardState.Rarity E = RosterCardState.Rarity.Epic;
        private const RosterCardState.Rarity L = RosterCardState.Rarity.Legendary;
        private const RosterCardState.Rarity M = RosterCardState.Rarity.Mythic;

        /// <summary>Walks a run of packs, advancing the counters exactly as the service will.</summary>
        private static RosterCardState.Rarity[] Packs(int n, System.Func<int, double> roll,
                                                      int[] pool, CardCollectionPack.Tuning t)
        {
            var got = new RosterCardState.Rarity[n];
            int e = 0, l = 0;
            for (int i = 0; i < n; i++)
            {
                got[i] = CardCollectionPack.RollRarity(roll(i), pool, e, l, t);
                CardCollectionPack.Advance(got[i], ref e, ref l);
            }
            return got;
        }

        // ---- the weight table --------------------------------------------------------------------

        [Test]
        public void RollRarityAlwaysReturnsARarityTheCatalogueCarries()
        {
            for (int i = 0; i <= 1000; i++)
            {
                var rarity = CardCollectionPack.RollRarity(i / 1000d, Pool, 0, 0, T);
                Assert.That((int)rarity, Is.InRange(0, CardCollection.RarityCount - 1));
                Assert.That(CardCollectionPack.PoolAt(Pool, rarity), Is.GreaterThan(0),
                            "rolled a rarity no card in the catalogue carries");
            }
        }

        [Test]
        public void RollsOutsideZeroToOneAreClampedRatherThanThrowing()
        {
            Assert.DoesNotThrow(() => CardCollectionPack.RollRarity(-5d, Pool, 0, 0, T));
            Assert.DoesNotThrow(() => CardCollectionPack.RollRarity(1d, Pool, 0, 0, T));
            Assert.DoesNotThrow(() => CardCollectionPack.RollRarity(double.NaN, Pool, 0, 0, T));
            Assert.That(CardCollectionPack.RollRarity(-5d, Pool, 0, 0, T), Is.EqualTo(C));
            Assert.That(CardCollectionPack.RollRarity(double.NaN, Pool, 0, 0, T), Is.EqualTo(C));
        }

        [Test]
        public void TheDistributionMatchesTheWeights()
        {
            // Sweep the unit interval instead of sampling: with a fresh pair of counters every time,
            // the share of the interval landing on a rarity IS its probability.
            const int n = 200000;
            var count = new int[CardCollection.RarityCount];
            for (int i = 0; i < n; i++)
                count[(int)CardCollectionPack.RollRarity((i + 0.5d) / n, Pool, 0, 0, T)]++;

            // Mythic carries no card, so the four that do are normalised over their own total.
            double total = T.CommonWeight + T.RareWeight + T.EpicWeight + T.LegendaryWeight;

            Assert.That(count[(int)C] / (double)n, Is.EqualTo(T.CommonWeight / total).Within(0.001));
            Assert.That(count[(int)R] / (double)n, Is.EqualTo(T.RareWeight / total).Within(0.001));
            Assert.That(count[(int)E] / (double)n, Is.EqualTo(T.EpicWeight / total).Within(0.001));
            Assert.That(count[(int)L] / (double)n, Is.EqualTo(T.LegendaryWeight / total).Within(0.001));
            Assert.That(count[(int)M], Is.Zero, "Mythic is unreachable while no card carries it");
        }

        [Test]
        public void EveryRarityWithACardIsReachable()
        {
            var seen = new bool[CardCollection.RarityCount];
            for (int i = 0; i < 200000; i++)
                seen[(int)CardCollectionPack.RollRarity((i + 0.5d) / 200000d, Pool, 0, 0, T)] = true;

            Assert.That(seen[(int)C], Is.True);
            Assert.That(seen[(int)R], Is.True);
            Assert.That(seen[(int)E], Is.True);
            Assert.That(seen[(int)L], Is.True);
        }

        [Test]
        public void AuthoringAMythicCardMakesItReachable()
        {
            int[] withMythic = { 9, 6, 6, 3, 1 };
            bool seen = false;
            for (int i = 0; i < 200000 && !seen; i++)
                seen = CardCollectionPack.RollRarity((i + 0.5d) / 200000d, withMythic, 0, 0, T) == M;

            Assert.That(seen, Is.True, "a Mythic card was authored and still cannot be drawn");
        }

        [Test]
        public void AnUncarriedRarityHasNoWeightAndNoPublishedOdds()
        {
            Assert.That(CardCollectionPack.WeightOf(M, Pool, 0, T), Is.Zero);
            Assert.That(CardCollectionPack.ChanceOf(M, Pool, T), Is.Zero);
        }

        [Test]
        public void PublishedOddsSumToOneOverTheRaritiesThatExist()
        {
            double sum = 0d;
            for (int r = 0; r < CardCollection.RarityCount; r++)
                sum += CardCollectionPack.ChanceOf((RosterCardState.Rarity)r, Pool, T);

            Assert.That(sum, Is.EqualTo(1d).Within(1e-9));
        }

        [Test]
        public void PublishedOddsIgnorePityEvenWhenOneIsDue()
        {
            // The sheet prints the base table. A player one pack from a guarantee must not see a
            // different set of numbers from the player who just opened one.
            double dry = CardCollectionPack.ChanceOf(L, Pool, T);
            double fresh = CardCollectionPack.ChanceOf(L, Pool, T);
            Assert.That(dry, Is.EqualTo(fresh));
            Assert.That(dry, Is.EqualTo(T.LegendaryWeight
                / (T.CommonWeight + T.RareWeight + T.EpicWeight + T.LegendaryWeight)).Within(1e-9));
        }

        [Test]
        public void AnEmptyCatalogueRollsCommonRatherThanThrowing()
        {
            int[] empty = { 0, 0, 0, 0, 0 };
            Assert.DoesNotThrow(() => CardCollectionPack.RollRarity(0.5d, empty, 0, 0, T));
            Assert.That(CardCollectionPack.RollRarity(0.5d, empty, 0, 0, T), Is.EqualTo(C));
            Assert.That(CardCollectionPack.RollRarity(0.5d, null, 0, 0, T), Is.EqualTo(C));
        }

        // ---- pity ---------------------------------------------------------------------------------

        [Test]
        public void TheEpicGuaranteeLandsExactlyOnItsThreshold()
        {
            // Roll 0 always takes the lowest band on offer, so nothing but pity can lift it.
            var got = Packs(T.EpicPity, _ => 0d, Pool, T);

            for (int i = 0; i < T.EpicPity - 1; i++)
                Assert.That(got[i], Is.EqualTo(C), $"pack {i + 1} should still be Common");

            Assert.That(got[T.EpicPity - 1], Is.EqualTo(E),
                        $"the Epic guarantee should fire on pack {T.EpicPity}");
        }

        [Test]
        public void TheLegendaryGuaranteeLandsExactlyOnItsThreshold()
        {
            var got = Packs(T.LegendaryPity, _ => 0d, Pool, T);

            for (int i = 0; i < T.LegendaryPity - 1; i++)
                Assert.That(got[i], Is.Not.EqualTo(L), $"pack {i + 1} should not be Legendary yet");

            Assert.That(got[T.LegendaryPity - 1], Is.EqualTo(L),
                        $"the Legendary guarantee should fire on pack {T.LegendaryPity}");
        }

        [Test]
        public void NoDryRunEverOutlastsAGuarantee()
        {
            // The worst luck the table allows, for long enough that a drifting counter would show.
            var got = Packs(5000, _ => 0d, Pool, T);

            int sinceEpic = 0, sinceLegendary = 0;
            for (int i = 0; i < got.Length; i++)
            {
                sinceEpic = got[i] >= E ? 0 : sinceEpic + 1;
                sinceLegendary = got[i] >= L ? 0 : sinceLegendary + 1;

                Assert.That(sinceEpic, Is.LessThan(T.EpicPity),
                            $"{sinceEpic} packs without an Epic at pack {i + 1}");
                Assert.That(sinceLegendary, Is.LessThan(T.LegendaryPity),
                            $"{sinceLegendary} packs without a Legendary at pack {i + 1}");
            }
        }

        [Test]
        public void SoftPityStartsWhereItSaysAndClimbsOneStepAPack()
        {
            Assert.That(CardCollectionPack.SoftPityBonus(0, T), Is.Zero);

            // SoftPityStart is how many packs the dry run has to REACH before the ramp starts, so
            // the last unbumped pack is the one opened at sinceLegendary == SoftPityStart - 1 and
            // the first step lands on the next. Same off-by-one as CaptainCrate.SoftPityBonus, kept
            // deliberately: two ramps that start half a pack apart is a bug nobody would ever find.
            Assert.That(CardCollectionPack.SoftPityBonus(T.SoftPityStart - 1, T), Is.Zero,
                        "the ramp should not have started yet");
            Assert.That(CardCollectionPack.SoftPityBonus(T.SoftPityStart, T),
                        Is.EqualTo(T.SoftPityStep).Within(1e-9));
            Assert.That(CardCollectionPack.SoftPityBonus(T.SoftPityStart + 5, T),
                        Is.EqualTo(T.SoftPityStep * 6d).Within(1e-9));
        }

        [Test]
        public void SoftPityRaisesTheLegendaryWeightAndNothingElse()
        {
            int dry = T.SoftPityStart + 9;

            Assert.That(CardCollectionPack.WeightOf(L, Pool, dry, T),
                        Is.GreaterThan(CardCollectionPack.WeightOf(L, Pool, 0, T)));

            foreach (var rarity in new[] { C, R, E })
                Assert.That(CardCollectionPack.WeightOf(rarity, Pool, dry, T),
                            Is.EqualTo(CardCollectionPack.WeightOf(rarity, Pool, 0, T)),
                            $"{rarity} should not move with the Legendary ramp");
        }

        [Test]
        public void SoftPityCannotLiftARarityNobodyCarries()
        {
            int[] noLegendary = { 9, 6, 6, 0, 0 };
            Assert.That(CardCollectionPack.WeightOf(L, noLegendary, T.SoftPityStart + 20, T), Is.Zero);
        }

        // ---- the counters -------------------------------------------------------------------------

        [Test]
        public void ALegendaryClearsBothCounters()
        {
            int e = 7, l = 40;
            CardCollectionPack.Advance(L, ref e, ref l);
            Assert.That(e, Is.Zero);
            Assert.That(l, Is.Zero);
        }

        [Test]
        public void AnEpicClearsTheShortCounterAndLengthensTheLong()
        {
            int e = 7, l = 40;
            CardCollectionPack.Advance(E, ref e, ref l);
            Assert.That(e, Is.Zero);
            Assert.That(l, Is.EqualTo(41));
        }

        [Test]
        public void ACommonOrRareLengthensBoth()
        {
            int e = 7, l = 40;
            CardCollectionPack.Advance(C, ref e, ref l);
            Assert.That(e, Is.EqualTo(8));
            Assert.That(l, Is.EqualTo(41));

            e = 7; l = 40;
            CardCollectionPack.Advance(R, ref e, ref l);
            Assert.That(e, Is.EqualTo(8));
            Assert.That(l, Is.EqualTo(41));
        }

        [Test]
        public void AMythicClearsBothCountersToo()
        {
            int e = 7, l = 40;
            CardCollectionPack.Advance(M, ref e, ref l);
            Assert.That(e, Is.Zero);
            Assert.That(l, Is.Zero);
        }

        [Test]
        public void WhenBothGuaranteesFallDueTheLegendaryOneWins()
        {
            var floor = CardCollectionPack.Floor(T.EpicPity - 1, T.LegendaryPity - 1, T);
            Assert.That(floor, Is.EqualTo(L),
                        "paying the Epic guarantee here would owe the player a Legendary they were due");
        }

        [Test]
        public void TheFloorIsCommonUntilAGuaranteeIsDue()
        {
            Assert.That(CardCollectionPack.Floor(0, 0, T), Is.EqualTo(C));
            Assert.That(CardCollectionPack.Floor(T.EpicPity - 2, T.LegendaryPity - 2, T), Is.EqualTo(C));
            Assert.That(CardCollectionPack.Floor(T.EpicPity - 1, 0, T), Is.EqualTo(E));
        }

        [Test]
        public void ZeroedThresholdsTurnTheGuaranteesOff()
        {
            var off = T;
            off.EpicPity = 0;
            off.LegendaryPity = 0;

            Assert.That(CardCollectionPack.Floor(9999, 9999, off), Is.EqualTo(C));
        }

        [Test]
        public void AGuaranteeForARarityNobodyCarriesFallsBackDownTheLadder()
        {
            // The Legendary guarantee comes due against a catalogue whose rarest card is Epic.
            int[] noLegendary = { 9, 6, 6, 0, 0 };
            var got = CardCollectionPack.RollRarity(0.5d, noLegendary,
                                                    T.EpicPity - 1, T.LegendaryPity - 1, T);

            Assert.That(got, Is.EqualTo(E), "should hand over the rarest card that exists");
            Assert.That(CardCollectionPack.PoolAt(noLegendary, got), Is.GreaterThan(0));
        }

        // ---- picking the card ---------------------------------------------------------------------

        [Test]
        public void TheCardIndexStaysInsideItsRarity()
        {
            for (int i = 0; i <= 1000; i++)
            {
                int nth = CardCollectionPack.RollIndexInRarity(i / 1000d, 6);
                Assert.That(nth, Is.InRange(0, 5));
            }

            Assert.That(CardCollectionPack.RollIndexInRarity(-1d, 6), Is.Zero);
            Assert.That(CardCollectionPack.RollIndexInRarity(1d, 6), Is.EqualTo(5));
            Assert.That(CardCollectionPack.RollIndexInRarity(double.NaN, 6), Is.Zero);
        }

        [Test]
        public void EveryCardOfARarityIsReachable()
        {
            var seen = new bool[6];
            for (int i = 0; i < 6000; i++) seen[CardCollectionPack.RollIndexInRarity(i / 6000d, 6)] = true;
            Assert.That(seen, Is.All.True);
        }

        [Test]
        public void AnEmptyRarityHandsOverNoCard()
        {
            Assert.That(CardCollectionPack.RollIndexInRarity(0.5d, 0), Is.EqualTo(-1));
            Assert.That(CardCollectionPack.RollIndexInRarity(0.5d, -3), Is.EqualTo(-1));
        }

        // ---- determinism --------------------------------------------------------------------------

        [Test]
        public void TheSameRollAndCountersAlwaysGiveTheSameCard()
        {
            for (int i = 0; i < 500; i++)
            {
                double roll = i / 500d;
                var first = CardCollectionPack.RollRarity(roll, Pool, 3, 22, T);
                var again = CardCollectionPack.RollRarity(roll, Pool, 3, 22, T);
                Assert.That(again, Is.EqualTo(first));
            }
        }

        [Test]
        public void APackHandsOverExactlyOneCard()
        {
            Assert.That(CardCollectionPack.CardsPerPack, Is.EqualTo(1));
        }
    }
}
