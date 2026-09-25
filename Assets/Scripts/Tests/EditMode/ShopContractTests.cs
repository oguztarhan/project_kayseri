using System;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class ShopContractTests
    {
        private static readonly ShopContract.Tuning T = ShopContract.Tuning.Default;
        private static readonly BenchMastery.Tuning Mastery = BenchMastery.Tuning.Default;
        private const string Business = "chapter-1.island-1";
        private static readonly bool[] AllBuilt = { true, true, true, true };

        [Test]
        public void DefaultTuningIsTheApprovedRewardTable()
        {
            T.Validate();
            Assert.That(T.IntervalSeconds, Is.EqualTo(1800d));
            Assert.That(T.ContractShare, Is.EqualTo(0.5d), "1:1 alternation with normal customers");

            double[] minutes = { 10d, 20d, 40d };
            double[] weights = { 40d, 40d, 20d };
            double[] premiums = { 0.10d, 0.15d, 0.20d };
            long[] gems = { 1L, 2L, 3L };
            int[] cards = { 1, 1, 2 };
            for (int i = 0; i < ShopContract.SizeCount; i++)
            {
                Assert.That(T.Sizes[i].Minutes, Is.EqualTo(minutes[i]), "minutes " + i);
                Assert.That(T.Sizes[i].Weight, Is.EqualTo(weights[i]), "weight " + i);
                Assert.That(T.Sizes[i].Premium, Is.EqualTo(premiums[i]).Within(1e-12), "premium " + i);
                Assert.That(T.Sizes[i].Gems, Is.EqualTo(gems[i]), "gems " + i);
                Assert.That(T.Sizes[i].ForemanCards, Is.EqualTo(cards[i]), "cards " + i);
            }
        }

        [Test]
        public void AverageContractPaysOnePointEightGemsAndOnePointTwoCards()
        {
            double total = 0d, gems = 0d, cards = 0d;
            for (int i = 0; i < ShopContract.SizeCount; i++)
            {
                total += T.Sizes[i].Weight;
                gems += T.Sizes[i].Weight * T.Sizes[i].Gems;
                cards += T.Sizes[i].Weight * T.Sizes[i].ForemanCards;
            }
            Assert.That(gems / total, Is.EqualTo(1.8d).Within(1e-12));
            Assert.That(cards / total, Is.EqualTo(1.2d).Within(1e-12));
        }

        [Test]
        public void InvalidTuningIsRefused()
        {
            ShopContract.Tuning t = ShopContract.Tuning.Default;
            t.IntervalSeconds = 0d;
            Assert.Throws<ArgumentException>(() => t.Validate());

            t = ShopContract.Tuning.Default;
            t.ContractShare = 1.5d;
            Assert.Throws<ArgumentException>(() => t.Validate());

            t = ShopContract.Tuning.Default;
            t.Sizes = new[] { t.Sizes[0], t.Sizes[1] };
            Assert.Throws<ArgumentException>(() => t.Validate());

            t = ShopContract.Tuning.Default;
            t.Sizes[2].Premium = -0.1d;
            Assert.Throws<ArgumentException>(() => t.Validate());

            t = ShopContract.Tuning.Default;
            t.MaxQuantity = t.MinQuantity - 1;
            Assert.Throws<ArgumentException>(() => t.Validate());
        }

        [Test]
        public void SlotsAreThirtyMinutesOfWallClock()
        {
            Assert.That(ShopContract.SlotAt(0L, T), Is.EqualTo(0L));
            Assert.That(ShopContract.SlotAt(1799L, T), Is.EqualTo(0L));
            Assert.That(ShopContract.SlotAt(1800L, T), Is.EqualTo(1L));
            Assert.That(ShopContract.SlotAt(1_758_800_000L, T), Is.EqualTo(1_758_800_000L / 1800L));
            Assert.That(ShopContract.SlotAt(-1L, T), Is.EqualTo(-1L), "a clock before 1970 never shares slot 0");

            Assert.That(ShopContract.SecondsToNextSlot(0L, T), Is.EqualTo(1800d));
            Assert.That(ShopContract.SecondsToNextSlot(1799L, T), Is.EqualTo(1d));
            Assert.That(ShopContract.SecondsToNextSlot(1800L, T), Is.EqualTo(1800d));
        }

        [Test]
        public void TheSameSlotAlwaysOffersTheSameJob()
        {
            for (long slot = 0; slot < 200; slot++)
            {
                Assert.That(ShopContract.TryPick(Business, slot, AllBuilt, T, out ShopContract.Pick a), Is.True);
                Assert.That(ShopContract.TryPick(Business, slot, AllBuilt, T, out ShopContract.Pick b), Is.True);
                Assert.That(b.ProductIndex, Is.EqualTo(a.ProductIndex));
                Assert.That(b.SizeIndex, Is.EqualTo(a.SizeIndex));
                Assert.That(a.Slot, Is.EqualTo(slot));
            }
        }

        [Test]
        public void SlotsAndBusinessesVaryTheJob()
        {
            var products = new int[4];
            var sizes = new int[ShopContract.SizeCount];
            int differ = 0;
            for (long slot = 0; slot < 400; slot++)
            {
                ShopContract.TryPick(Business, slot, AllBuilt, T, out ShopContract.Pick a);
                ShopContract.TryPick("chapter-1.island-2", slot, AllBuilt, T, out ShopContract.Pick b);
                products[a.ProductIndex]++;
                sizes[a.SizeIndex]++;
                if (a.ProductIndex != b.ProductIndex || a.SizeIndex != b.SizeIndex) differ++;
            }
            for (int i = 0; i < products.Length; i++) Assert.That(products[i], Is.GreaterThan(0), "product " + i);
            for (int i = 0; i < sizes.Length; i++) Assert.That(sizes[i], Is.GreaterThan(0), "size " + i);
            Assert.That(differ, Is.GreaterThan(200), "two islands must not share one offer sequence");
        }

        [Test]
        public void OnlyBuiltBenchesAreOrderedFrom()
        {
            bool[] built = { true, false, true, false };
            for (long slot = 0; slot < 500; slot++)
            {
                Assert.That(ShopContract.TryPick(Business, slot, built, T, out ShopContract.Pick pick), Is.True);
                Assert.That(pick.ProductIndex == 0 || pick.ProductIndex == 2, "slot " + slot + " picked " + pick.ProductIndex);
            }

            Assert.That(ShopContract.TryPick(Business, 0L, new bool[4], T, out _), Is.False, "no bench built");
            Assert.That(ShopContract.TryPick(Business, 0L, null, T, out _), Is.False);
        }

        [Test]
        public void SizesAreRolledFortyFortyTwenty()
        {
            var counts = new int[ShopContract.SizeCount];
            const int n = 20000;
            for (long slot = 0; slot < n; slot++)
            {
                ShopContract.TryPick(Business, slot, AllBuilt, T, out ShopContract.Pick pick);
                counts[pick.SizeIndex]++;
            }
            Assert.That(counts[ShopContract.SmallSize] / (double)n, Is.EqualTo(0.4d).Within(0.02d));
            Assert.That(counts[ShopContract.MediumSize] / (double)n, Is.EqualTo(0.4d).Within(0.02d));
            Assert.That(counts[ShopContract.LargeSize] / (double)n, Is.EqualTo(0.2d).Within(0.02d));
        }

        [Test]
        public void QuantityIsHalfTheBenchOutputOverTheSizesMinutes()
        {
            // A level-1 pickaxe makes one every 10 s; served 1:1, the contract gets one every 20 s.
            Assert.That(ShopContract.Quantity(10d, 10d, T), Is.EqualTo(30));
            Assert.That(ShopContract.Quantity(20d, 10d, T), Is.EqualTo(60));
            Assert.That(ShopContract.Quantity(40d, 10d, T), Is.EqualTo(120));
            // Rounded up to a whole item, then to two significant digits.
            Assert.That(ShopContract.Quantity(10d, 7d, T), Is.EqualTo(43));
            Assert.That(ShopContract.Quantity(40d, 0.5d, T), Is.EqualTo(2400));
            Assert.That(ShopContract.Quantity(40d, 0.47d, T), Is.EqualTo(2600));
        }

        [Test]
        public void QuantityIsClampedToItsRange()
        {
            Assert.That(ShopContract.Quantity(10d, 1000d, T), Is.EqualTo(T.MinQuantity));
            Assert.That(ShopContract.Quantity(40d, 1e-6d, T), Is.EqualTo(T.MaxQuantity));
        }

        [Test]
        public void TheUsersExampleTenPickaxesAtOneHundredPayElevenHundred()
        {
            Assert.That(ShopContract.Cash(10, 100d, 0.10d), Is.EqualTo(1100d).Within(1e-9));
        }

        [Test]
        public void TermsCarryTheSizesRewardsAndTheNormalComparison()
        {
            ShopContract.Terms terms = ShopContract.TermsFor(ShopContract.MediumSize, 10d, 100d, T);
            Assert.That(terms.Quantity, Is.EqualTo(60));
            Assert.That(terms.NormalCash, Is.EqualTo(6000d).Within(1e-9));
            Assert.That(terms.Cash, Is.EqualTo(6900d).Within(1e-9));
            Assert.That(terms.Gems, Is.EqualTo(2L));
            Assert.That(terms.ForemanCards, Is.EqualTo(1));
            Assert.That(terms.EstimatedSeconds, Is.EqualTo(1200d).Within(1e-9));

            ShopContract.Terms large = ShopContract.TermsFor(ShopContract.LargeSize, 10d, 100d, T);
            Assert.That(large.Gems, Is.EqualTo(3L));
            Assert.That(large.ForemanCards, Is.EqualTo(2));

            Assert.Throws<ArgumentOutOfRangeException>(() => ShopContract.TermsFor(3, 10d, 100d, T));
            Assert.Throws<ArgumentOutOfRangeException>(() => ShopContract.TermsFor(0, 0d, 100d, T));
            Assert.Throws<ArgumentOutOfRangeException>(() => ShopContract.TermsFor(0, 10d, double.NaN, T));
        }

        [Test]
        public void NormalPriceIncludesPerfectSalesAndStandingButNoBoost()
        {
            double unit = 100d;
            Assert.That(ShopContract.NormalUnitPrice(unit, 0, 1d, Mastery),
                Is.EqualTo(unit * BenchMastery.PerfectAverage(0, Mastery)).Within(1e-9));
            double five = ShopContract.NormalUnitPrice(unit, 5, 1.5d, Mastery);
            Assert.That(five, Is.EqualTo(unit * BenchMastery.PerfectAverage(5, Mastery) * 1.5d).Within(1e-9));
            Assert.That(five, Is.GreaterThan(unit * 1.5d), "a starred bench sells above its list price on average");
        }

        [Test]
        public void EveryContractOnEveryBenchAndLevelBeatsSellingNormally()
        {
            MiningShopBusinessSimulation.ProductTuning[] products = MiningShopBusinessSimulation.Tuning.Default.Products;
            for (int p = 0; p < products.Length; p++)
                for (int level = BenchMastery.MinLevel; level <= BenchMastery.MaxLevel; level++)
                {
                    int stars = BenchMastery.StarsAt(level);
                    double craft = BenchMastery.CycleSeconds(products[p].CraftSeconds, level, Mastery);
                    double list = BenchMastery.ItemValue(products[p].UnitPrice, products[p].CraftSeconds, level, Mastery);
                    // What the counter earns per item on average, the way offline earnings count it.
                    double counter = list * BenchMastery.PerfectAverage(stars, Mastery);
                    double normal = ShopContract.NormalUnitPrice(list, stars, 1d, Mastery);
                    for (int s = 0; s < ShopContract.SizeCount; s++)
                    {
                        ShopContract.Terms terms = ShopContract.TermsFor(s, craft, normal, T);
                        double perItem = terms.Cash / terms.Quantity;
                        Assert.That(perItem, Is.EqualTo(counter * (1d + T.Sizes[s].Premium)).Within(counter * 1e-9),
                            "product " + p + " level " + level + " size " + s);
                    }
                }
        }

        [Test]
        public void EstimatedTimeStaysNearTheSizesMinutesAtEveryLevel()
        {
            MiningShopBusinessSimulation.ProductTuning[] products = MiningShopBusinessSimulation.Tuning.Default.Products;
            for (int p = 0; p < products.Length; p++)
                for (int level = BenchMastery.MinLevel; level <= BenchMastery.MaxLevel; level++)
                {
                    double craft = BenchMastery.CycleSeconds(products[p].CraftSeconds, level, Mastery);
                    for (int s = 0; s < ShopContract.SizeCount; s++)
                    {
                        ShopContract.Terms terms = ShopContract.TermsFor(s, craft, 1d, T);
                        double target = T.Sizes[s].Minutes * 60d;
                        string where = "product " + p + " level " + level + " size " + s;
                        if (terms.Quantity == T.MinQuantity)
                            Assert.That(terms.EstimatedSeconds, Is.GreaterThanOrEqualTo(target * 0.9d), where);
                        else
                            // Two-digit rounding moves it at most 5%; rounding up to a whole item adds one item's time.
                            Assert.That(terms.EstimatedSeconds,
                                Is.EqualTo(target).Within(target * 0.06d + craft / T.ContractShare), where);
                    }
                }
        }
    }
}
