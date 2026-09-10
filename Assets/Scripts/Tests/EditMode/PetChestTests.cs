using NUnit.Framework;
using Game.Core;

namespace Game.Tests
{
    /// <summary>
    /// The pet chest. Because <see cref="PetChest"/> takes its roll as an argument, the whole
    /// distribution can be walked exactly rather than sampled and hoped over.
    /// </summary>
    public class PetChestTests
    {
        private static PetChest.Tuning T => PetChest.Tuning.Default;

        /// <summary>Walks a whole batch of opens, advancing the counters exactly as the service does.</summary>
        private static RosterCardState.Rarity[] Opens(int n, System.Func<int, double> roll, PetChest.Tuning t)
        {
            var got = new RosterCardState.Rarity[n];
            int e = 0, l = 0;
            for (int i = 0; i < n; i++)
            {
                got[i] = PetChest.RollRarity(roll(i), e, l, t);
                PetChest.Advance(got[i], ref e, ref l);
            }
            return got;
        }

        // ---- the weight table --------------------------------------------------------------------

        [Test]
        public void RollRarityIsAlwaysARealRarity()
        {
            for (int i = 0; i <= 1000; i++)
            {
                var r = PetChest.RollRarity(i / 1000d, 0, 0, T);
                Assert.That((int)r, Is.InRange(0, Pets.RarityCount - 1));
            }
        }

        [Test]
        public void RollsOutsideZeroToOneAreClampedRatherThanThrowing()
        {
            Assert.DoesNotThrow(() => PetChest.RollRarity(-5d, 0, 0, T));
            Assert.DoesNotThrow(() => PetChest.RollRarity(1d, 0, 0, T));
            Assert.DoesNotThrow(() => PetChest.RollRarity(double.NaN, 0, 0, T));
            Assert.That(PetChest.RollRarity(-5d, 0, 0, T), Is.EqualTo(RosterCardState.Rarity.Common));
        }

        [Test]
        public void TheDistributionMatchesTheWeights()
        {
            const int n = 200000;
            var count = new int[Pets.RarityCount];
            for (int i = 0; i < n; i++) count[(int)PetChest.RollRarity((i + 0.5d) / n, 0, 0, T)]++;

            double total = T.CommonWeight + T.RareWeight + T.EpicWeight + T.LegendaryWeight + T.MythicWeight;
            AssertShare(count[(int)RosterCardState.Rarity.Common], n, T.CommonWeight / total);
            AssertShare(count[(int)RosterCardState.Rarity.Rare], n, T.RareWeight / total);
            AssertShare(count[(int)RosterCardState.Rarity.Epic], n, T.EpicWeight / total);
            AssertShare(count[(int)RosterCardState.Rarity.Legendary], n, T.LegendaryWeight / total);
            AssertShare(count[(int)RosterCardState.Rarity.Mythic], n, T.MythicWeight / total);
        }

        private static void AssertShare(int got, int n, double expected)
            => Assert.That(got / (double)n, Is.EqualTo(expected).Within(0.002d));

        [Test]
        public void EveryRarityCarriesRealWeightUnlikeTheTwoReferenceChests()
        {
            // The one structural difference from CaptainCrate/CardCollectionPack: there is no
            // "nobody carries this rarity" case here, because every rarity carries all six species.
            for (int r = 0; r < Pets.RarityCount; r++)
                Assert.That(PetChest.WeightOf((RosterCardState.Rarity)r, 0, T), Is.GreaterThan(0d),
                            "rarity " + r);
        }

        // ---- pity --------------------------------------------------------------------------------

        [Test]
        public void TheShortPityAlwaysLandsWithinItsWindow()
        {
            var got = Opens(5000, _ => 0d, T);
            int dry = 0;
            for (int i = 0; i < got.Length; i++)
            {
                if (got[i] >= RosterCardState.Rarity.Epic) { dry = 0; continue; }
                dry++;
                Assert.That(dry, Is.LessThan(T.EpicPity),
                            "went " + dry + " opens without an Epic — the short pity is " + T.EpicPity);
            }
        }

        [Test]
        public void ABulkOpenAlwaysContainsAnEpic()
        {
            var got = Opens(T.BulkCount, _ => 0d, T);
            bool any = false;
            for (int i = 0; i < got.Length; i++) if (got[i] >= RosterCardState.Rarity.Epic) any = true;
            Assert.That(any, Is.True);
        }

        [Test]
        public void TheLongPityAlwaysLandsWithinItsWindow()
        {
            var got = Opens(20000, _ => 0d, T);
            int dry = 0;
            for (int i = 0; i < got.Length; i++)
            {
                if (got[i] >= RosterCardState.Rarity.Legendary) { dry = 0; continue; }
                dry++;
                Assert.That(dry, Is.LessThan(T.LegendaryPity),
                            "went " + dry + " opens without a Legendary — the long pity is " + T.LegendaryPity);
            }
        }

        [Test]
        public void SoftPityRampsOnlyAfterItsStart()
        {
            Assert.That(PetChest.SoftPityBonus(0, T), Is.Zero);
            Assert.That(PetChest.SoftPityBonus(T.SoftPityStart - 2, T), Is.Zero);

            double a = PetChest.SoftPityBonus(T.SoftPityStart, T);
            double b = PetChest.SoftPityBonus(T.SoftPityStart + 10, T);
            Assert.That(a, Is.GreaterThan(0d));
            Assert.That(b, Is.GreaterThan(a), "the ramp must actually climb");
        }

        [Test]
        public void TheFloorRisesOnlyWhenAPityIsDue()
        {
            Assert.That(PetChest.Floor(0, 0, T), Is.EqualTo(RosterCardState.Rarity.Common));
            Assert.That(PetChest.Floor(T.EpicPity - 2, 0, T), Is.EqualTo(RosterCardState.Rarity.Common));
            Assert.That(PetChest.Floor(T.EpicPity - 1, 0, T), Is.EqualTo(RosterCardState.Rarity.Epic));
            Assert.That(PetChest.Floor(0, T.LegendaryPity - 1, T), Is.EqualTo(RosterCardState.Rarity.Legendary));

            // The long pity outranks the short one when both are due.
            Assert.That(PetChest.Floor(T.EpicPity, T.LegendaryPity, T), Is.EqualTo(RosterCardState.Rarity.Legendary));
        }

        [Test]
        public void APityDuePullNeverComesBackBelowItsFloor()
        {
            for (int i = 0; i <= 100; i++)
            {
                Assert.That(PetChest.RollRarity(i / 100d, T.EpicPity - 1, 0, T),
                            Is.GreaterThanOrEqualTo(RosterCardState.Rarity.Epic));
                Assert.That(PetChest.RollRarity(i / 100d, 0, T.LegendaryPity - 1, T),
                            Is.GreaterThanOrEqualTo(RosterCardState.Rarity.Legendary));
            }
        }

        [Test]
        public void PityCanBeSwitchedOff()
        {
            var t = T;
            t.EpicPity = 0;
            t.LegendaryPity = 0;
            Assert.That(PetChest.Floor(9999, 9999, t), Is.EqualTo(RosterCardState.Rarity.Common));
            Assert.That(PetChest.RollRarity(0d, 9999, 9999, t), Is.EqualTo(RosterCardState.Rarity.Common));
        }

        // ---- the counters ------------------------------------------------------------------------

        [Test]
        public void ALegendaryClearsBothCounters()
        {
            int e = 7, l = 40;
            PetChest.Advance(RosterCardState.Rarity.Legendary, ref e, ref l);
            Assert.That(e, Is.Zero);
            Assert.That(l, Is.Zero);

            e = 7; l = 40;
            PetChest.Advance(RosterCardState.Rarity.Mythic, ref e, ref l);
            Assert.That(e, Is.Zero);
            Assert.That(l, Is.Zero);
        }

        [Test]
        public void AnEpicClearsTheShortCounterOnly()
        {
            int e = 7, l = 40;
            PetChest.Advance(RosterCardState.Rarity.Epic, ref e, ref l);
            Assert.That(e, Is.Zero);
            Assert.That(l, Is.EqualTo(41));
        }

        [Test]
        public void AnythingLesserLengthensBoth()
        {
            int e = 7, l = 40;
            PetChest.Advance(RosterCardState.Rarity.Common, ref e, ref l);
            Assert.That(e, Is.EqualTo(8));
            Assert.That(l, Is.EqualTo(41));

            PetChest.Advance(RosterCardState.Rarity.Rare, ref e, ref l);
            Assert.That(e, Is.EqualTo(9));
            Assert.That(l, Is.EqualTo(42));
        }

        // ---- which species comes out -----------------------------------------------------------

        [Test]
        public void RollSpeciesAlwaysHandsOverSomebodyReal()
        {
            for (int i = 0; i <= 1000; i++)
                Assert.That(Pets.Exists(PetChest.RollSpecies(i / 1000d)), Is.True);
        }

        [Test]
        public void EverySpeciesComesOutEvenly()
        {
            const int n = 60000;
            var count = new int[Pets.SpeciesCount];
            for (int i = 0; i < n; i++) count[PetChest.RollSpecies((i + 0.5d) / n)]++;

            for (int sp = 0; sp < Pets.SpeciesCount; sp++)
                Assert.That(count[sp] / (double)n, Is.EqualTo(1d / Pets.SpeciesCount).Within(0.01d),
                            Pets.IdOf(sp));
        }

        // ---- price -------------------------------------------------------------------------------

        [Test]
        public void BulkIsCheaperPerChestAndNothingElseIs()
        {
            Assert.That(PetChest.Cost(1, T), Is.EqualTo(T.PearlCost));
            Assert.That(PetChest.Cost(T.BulkCount, T), Is.EqualTo(T.BulkPearlCost));
            Assert.That(PetChest.Cost(T.BulkCount, T),
                        Is.LessThan(T.PearlCost * T.BulkCount), "a bulk open nobody saves on is a button nobody presses");
            Assert.That(PetChest.Cost(3, T), Is.EqualTo(T.PearlCost * 3));
            Assert.That(PetChest.Cost(0, T), Is.Zero);
            Assert.That(PetChest.Cost(-4, T), Is.Zero);
        }
    }
}
