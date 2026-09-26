using System;
using System.Collections.Generic;
using Game.Core;
using Game.Data;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>Customer tips: the pure rules, and the roll inside the shop simulation — gated, sized, saved, apart.</summary>
    public sealed class ShopTipTests
    {
        private const string BusinessId = "mining-shop.island-01-04";

        private static MiningShopState Fresh() => new MiningShopState { BusinessId = BusinessId };

        private static MiningShopBusinessSimulation.Tuning Ungated()
        {
            MiningShopBusinessSimulation.Tuning tuning = MiningShopBusinessSimulation.Tuning.Default;
            tuning.BuildRequiresLevel = BenchMastery.MinLevel;
            return tuning;
        }

        private static MiningShopBusinessSimulation Open(MiningShopState state, List<MiningShopBusinessSimulation.Sale> receipts,
            MiningShopBusinessSimulation.Tuning tuning)
            => new MiningShopBusinessSimulation(state, 1, tuning, sale => receipts?.Add(sale));

        private static void Run(MiningShopBusinessSimulation sim, int seconds)
        {
            for (int i = 0; i < seconds; i++) sim.Advance(1d);
        }

        /// <summary>A sale's price before its perfect multiplier: what a tip is a share of.</summary>
        private static double Normal(MiningShopBusinessSimulation.Sale sale)
            => sale.Perfect ? sale.Cash / BenchMastery.Tuning.Default.PerfectMultiplier : sale.Cash;

        // ------------------------------------------------------------------ pure rules
        [Test]
        public void TheApprovedNumbersAreTheDefaults()
        {
            ShopTips.Tuning t = ShopTips.Tuning.Default;
            t.Validate();
            Assert.That(t.BaseChance, Is.EqualTo(0.10d));
            Assert.That(t.ChancePerStar, Is.EqualTo(0.01d));
            Assert.That(t.PerfectBonus, Is.EqualTo(0.10d));
            Assert.That(t.MaxChance, Is.EqualTo(0.25d));
            Assert.That(t.MinSalesBetween, Is.EqualTo(2));
            Assert.That(t.UnlockReceipts, Is.EqualTo(10));
            CollectionAssert.AreEqual(new[] { 0.25d, 0.6d, 1.5d }, t.Shares);
            CollectionAssert.AreEqual(new[] { 75, 22, 3 }, t.Weights);
            Assert.That(MiningShopBusinessSimulation.Tuning.Default.Tips.BaseChance, Is.EqualTo(t.BaseChance));
        }

        [Test]
        public void TheInspectorDefaultsMatchTheCodeDefaults()
        {
            var config = ScriptableObject.CreateInstance<MiningShopConfig>();
            try
            {
                ShopTips.Tuning fromConfig = config.ToTipTuning();
                ShopTips.Tuning code = ShopTips.Tuning.Default;
                Assert.That(fromConfig.BaseChance, Is.EqualTo(code.BaseChance));
                Assert.That(fromConfig.ChancePerStar, Is.EqualTo(code.ChancePerStar));
                Assert.That(fromConfig.PerfectBonus, Is.EqualTo(code.PerfectBonus));
                Assert.That(fromConfig.MaxChance, Is.EqualTo(code.MaxChance));
                Assert.That(fromConfig.MinSalesBetween, Is.EqualTo(code.MinSalesBetween));
                Assert.That(fromConfig.UnlockReceipts, Is.EqualTo(code.UnlockReceipts));
                CollectionAssert.AreEqual(code.Shares, fromConfig.Shares);
                CollectionAssert.AreEqual(code.Weights, fromConfig.Weights);
                Assert.That(config.ToBusinessTuning().Tips.MaxChance, Is.EqualTo(code.MaxChance));
            }
            finally { UnityEngine.Object.DestroyImmediate(config); }
        }

        [Test]
        public void ChanceGrowsWithStarsAndPerfectSalesAndStopsAtTheCap()
        {
            ShopTips.Tuning t = ShopTips.Tuning.Default;
            Assert.That(ShopTips.Chance(0, false, t), Is.EqualTo(0.10d).Within(1e-12));
            Assert.That(ShopTips.Chance(2, false, t), Is.EqualTo(0.12d).Within(1e-12));
            Assert.That(ShopTips.Chance(5, false, t), Is.EqualTo(0.15d).Within(1e-12));
            Assert.That(ShopTips.Chance(0, true, t), Is.EqualTo(0.20d).Within(1e-12));
            Assert.That(ShopTips.Chance(5, true, t), Is.EqualTo(0.25d).Within(1e-12));
            t.BaseChance = 0.5d;
            Assert.That(ShopTips.Chance(5, true, t), Is.EqualTo(0.25d), "the cap holds whatever the base");
        }

        [Test]
        public void TiersFollowTheirWeightsAndPayAShareOfTheNormalPrice()
        {
            ShopTips.Tuning t = ShopTips.Tuning.Default;
            Assert.That(ShopTips.Tier(0d, t), Is.EqualTo(0));
            Assert.That(ShopTips.Tier(0.7499d, t), Is.EqualTo(0));
            Assert.That(ShopTips.Tier(0.75d, t), Is.EqualTo(1));
            Assert.That(ShopTips.Tier(0.9699d, t), Is.EqualTo(1));
            Assert.That(ShopTips.Tier(0.97d, t), Is.EqualTo(2));
            Assert.That(ShopTips.Tier(0.99999d, t), Is.EqualTo(2));
            Assert.That(ShopTips.Amount(400d, 0, t), Is.EqualTo(100d).Within(1e-9));
            Assert.That(ShopTips.Amount(400d, 1, t), Is.EqualTo(240d).Within(1e-9));
            Assert.That(ShopTips.Amount(400d, 2, t), Is.EqualTo(600d).Within(1e-9));
        }

        [Test]
        public void TuningThatCannotWorkIsRefused()
        {
            ShopTips.Tuning t = ShopTips.Tuning.Default;
            t.MaxChance = 1.5d;
            Assert.Throws<ArgumentException>(() => t.Validate(), "cap above one");
            t = ShopTips.Tuning.Default;
            t.Weights = new[] { 0, 0, 0 };
            Assert.Throws<ArgumentException>(() => t.Validate(), "no tier can come up");
            t = ShopTips.Tuning.Default;
            t.Shares = new[] { 0.25d, 0.6d };
            Assert.Throws<ArgumentException>(() => t.Validate(), "a tier with no share");
            t = ShopTips.Tuning.Default;
            t.Shares = new[] { 0.25d, 0d, 1.5d };
            Assert.Throws<ArgumentException>(() => t.Validate(), "a tier that pays nothing");
            t = ShopTips.Tuning.Default;
            t.MinSalesBetween = -1;
            Assert.Throws<ArgumentException>(() => t.Validate(), "negative gap");
            MiningShopBusinessSimulation.Tuning shop = Ungated();
            shop.Tips.BaseChance = double.NaN;
            Assert.Throws<ArgumentException>(() => new MiningShopBusinessSimulation(Fresh(), 1, shop, _ => { }));
        }

        // ------------------------------------------------------------------ in the shop
        [Test]
        public void TheFirstReceiptsNeverTipAndTheGapHoldsBetweenTips()
        {
            var receipts = new List<MiningShopBusinessSimulation.Sale>();
            var state = Fresh();
            MiningShopBusinessSimulation.Tuning tuning = Ungated();
            // Every sale that may tip does, so the gate and the gap are the only things between tips.
            tuning.Tips.BaseChance = 1d;
            tuning.Tips.MaxChance = 1d;
            MiningShopBusinessSimulation sim = Open(state, receipts, tuning);
            sim.BuyLevels(0, 99);
            Run(sim, 2000);

            Assert.That(receipts.Count, Is.GreaterThan(40));
            int sinceTip = int.MaxValue;
            for (int i = 0; i < receipts.Count; i++)
            {
                MiningShopBusinessSimulation.Sale sale = receipts[i];
                bool tipped = sale.TipTier != ShopTips.None;
                Assert.That(sale.Tip > 0d, Is.EqualTo(tipped), "receipt " + sale.Sequence);
                if (sale.Sequence <= tuning.Tips.UnlockReceipts)
                {
                    Assert.That(tipped, Is.False, "receipt " + sale.Sequence + " is inside the unlock");
                    continue;
                }
                if (tipped)
                {
                    Assert.That(sinceTip, Is.GreaterThanOrEqualTo(tuning.Tips.MinSalesBetween), "receipt " + sale.Sequence);
                    sinceTip = 0;
                }
                else sinceTip++;
            }
            // With a certain chance the pattern is exact: the first sale past the unlock tips, then every third.
            Assert.That(receipts[10].TipTier, Is.Not.EqualTo(ShopTips.None));
            Assert.That(receipts[11].TipTier, Is.EqualTo(ShopTips.None));
            Assert.That(receipts[12].TipTier, Is.EqualTo(ShopTips.None));
            Assert.That(receipts[13].TipTier, Is.Not.EqualTo(ShopTips.None));
        }

        [Test]
        public void ATipIsASeparateBonusSizedFromTheNormalPrice()
        {
            var receipts = new List<MiningShopBusinessSimulation.Sale>();
            var state = Fresh();
            MiningShopBusinessSimulation sim = Open(state, receipts, Ungated());
            sim.BuyLevels(0, 99);
            Run(sim, 20000);

            double cash = 0d, tips = 0d;
            long count = 0L;
            int perfectTips = 0;
            for (int i = 0; i < receipts.Count; i++)
            {
                MiningShopBusinessSimulation.Sale sale = receipts[i];
                cash += sale.Cash;
                if (sale.TipTier == ShopTips.None) continue;
                count++;
                tips += sale.Tip;
                if (sale.Perfect) perfectTips++;
                // A perfect sale's tip comes from the price before its multiplier: perfect raises the chance only.
                Assert.That(sale.Tip, Is.EqualTo(ShopTips.Amount(Normal(sale), sale.TipTier, ShopTips.Tuning.Default))
                    .Within(1e-9).Percent, "receipt " + sale.Sequence);
            }

            Assert.That(count, Is.GreaterThan(50));
            Assert.That(perfectTips, Is.GreaterThan(0), "some tipped sale was perfect");
            MiningShopBusinessSimulation.ProductSnapshot pickaxe = sim.View.ProductAt(0);
            Assert.That(pickaxe.Earned, Is.EqualTo(cash).Within(1e-9).Percent, "the bench earns its prices, not its tips");
            Assert.That(sim.View.TipCount, Is.EqualTo(count));
            Assert.That(sim.View.TipsEarned, Is.EqualTo(tips).Within(1e-9).Percent);
            Assert.That(pickaxe.Sold, Is.GreaterThan(0L));
        }

        [TestCase(1, 0, 0.07d, 0.10d, 0.020d, 0.045d)]
        [TestCase(100, 5, 0.10d, 0.14d, 0.030d, 0.060d)]
        public void TipsStayASmallExtraAtTheApprovedOdds(int level, int stars, double minRate, double maxRate,
            double minShare, double maxShare)
        {
            var receipts = new List<MiningShopBusinessSimulation.Sale>();
            MiningShopBusinessSimulation sim = Open(Fresh(), receipts, Ungated());
            sim.BuyLevels(0, level - 1);
            Assert.That(sim.View.ProductAt(0).Stars, Is.EqualTo(stars));
            Run(sim, 60000);

            int sales = 0, tipped = 0;
            int[] tiers = new int[ShopTips.TierCount];
            double normal = 0d, tips = 0d;
            for (int i = 0; i < receipts.Count; i++)
            {
                MiningShopBusinessSimulation.Sale sale = receipts[i];
                if (sale.Sequence <= ShopTips.Tuning.Default.UnlockReceipts) continue;
                sales++;
                normal += Normal(sale);
                if (sale.TipTier == ShopTips.None) continue;
                tipped++;
                tips += sale.Tip;
                tiers[sale.TipTier]++;
            }

            Assert.That(sales, Is.GreaterThan(4000));
            Assert.That((double)tipped / sales, Is.InRange(minRate, maxRate), "tips per sale");
            Assert.That(tips / normal, Is.InRange(minShare, maxShare), "tips as a share of normal prices");
            Assert.That((double)tiers[0] / tipped, Is.InRange(0.65d, 0.85d), "plain tips are most of them");
            Assert.That(tiers[1], Is.GreaterThan(0));
            Assert.That(tiers[2], Is.GreaterThan(0));
            Assert.That(tiers[2], Is.LessThan(tiers[1]));
        }

        [Test]
        public void ContractDeliveriesNeverTip()
        {
            var receipts = new List<MiningShopBusinessSimulation.Sale>();
            MiningShopBusinessSimulation.Tuning tuning = Ungated();
            tuning.Tips.BaseChance = 1d;
            tuning.Tips.MaxChance = 1d;
            tuning.Tips.UnlockReceipts = 0;
            tuning.Tips.MinSalesBetween = 0;
            MiningShopBusinessSimulation sim = Open(Fresh(), receipts, tuning);
            var terms = new ShopContract.Terms { Quantity = 30, Cash = 1000d, NormalCash = 900d };
            Assert.That(sim.StartContract(1L, 0, ShopContract.SmallSize, terms), Is.True);
            Run(sim, 3000);

            int deliveries = 0, sales = 0;
            for (int i = 0; i < receipts.Count; i++)
            {
                if (receipts[i].Contract)
                {
                    deliveries++;
                    Assert.That(receipts[i].Tip, Is.Zero);
                    Assert.That(receipts[i].TipTier, Is.EqualTo(ShopTips.None));
                }
                else
                {
                    sales++;
                    Assert.That(receipts[i].TipTier, Is.Not.EqualTo(ShopTips.None), "every cash sale tips here");
                }
            }
            Assert.That(deliveries, Is.GreaterThan(0));
            Assert.That(sales, Is.GreaterThan(0));
        }

        [Test]
        public void TipsMoveNoPerfectSale()
        {
            var withTips = new List<MiningShopBusinessSimulation.Sale>();
            var without = new List<MiningShopBusinessSimulation.Sale>();
            MiningShopBusinessSimulation a = Open(Fresh(), withTips, Ungated());
            MiningShopBusinessSimulation.Tuning off = Ungated();
            off.Tips.MaxChance = 0d;
            MiningShopBusinessSimulation b = Open(Fresh(), without, off);
            a.BuyLevels(0, 99);
            b.BuyLevels(0, 99);
            Run(a, 20000);
            Run(b, 20000);

            Assert.That(withTips.Count, Is.EqualTo(without.Count));
            int tipped = 0;
            for (int i = 0; i < withTips.Count; i++)
            {
                Assert.That(withTips[i].Perfect, Is.EqualTo(without[i].Perfect), "sale " + i);
                Assert.That(withTips[i].Cash, Is.EqualTo(without[i].Cash), "sale " + i);
                Assert.That(without[i].TipTier, Is.EqualTo(ShopTips.None));
                if (withTips[i].TipTier != ShopTips.None) tipped++;
            }
            Assert.That(tipped, Is.GreaterThan(0));
        }

        [Test]
        public void AReloadNeitherRerollsNorReplaysATip()
        {
            var straight = new List<MiningShopBusinessSimulation.Sale>();
            MiningShopBusinessSimulation a = Open(Fresh(), straight, Ungated());
            a.BuyLevels(0, 99);
            var reloaded = new List<MiningShopBusinessSimulation.Sale>();
            MiningShopState stateB = Fresh();
            MiningShopBusinessSimulation b = Open(stateB, reloaded, Ungated());
            b.BuyLevels(0, 99);

            Run(a, 20000);
            Run(b, 10000);
            var loaded = JsonUtility.FromJson<MiningShopState>(JsonUtility.ToJson(stateB));
            MiningShopBusinessSimulation resumed = Open(loaded, reloaded, Ungated());
            Run(resumed, 10000);

            Assert.That(reloaded.Count, Is.EqualTo(straight.Count));
            int tipped = 0;
            for (int i = 0; i < straight.Count; i++)
            {
                Assert.That(reloaded[i].TipTier, Is.EqualTo(straight[i].TipTier), "sale " + i);
                // JsonUtility does not round-trip a double exactly; the roll must match, the amount to the last digit.
                Assert.That(reloaded[i].Tip, Is.EqualTo(straight[i].Tip).Within(1e-9).Percent, "sale " + i);
                if (straight[i].TipTier != ShopTips.None) tipped++;
            }
            Assert.That(tipped, Is.GreaterThan(50));
            Assert.That(resumed.View.TipCount, Is.EqualTo(a.View.TipCount));
        }

        [Test]
        public void ASaveFromBeforeTipsOpensAndABrokenTipRecordIsRefused()
        {
            MiningShopState state = Fresh();
            MiningShopBusinessSimulation sim = Open(state, null, Ungated());
            Run(sim, 300);
            string json = JsonUtility.ToJson(state);
            // What a save written before tips holds: none of the four fields.
            foreach (string field in new[] { "TipSeed", "SalesSinceTip", "TipCount", "TipsEarned" })
                json = System.Text.RegularExpressions.Regex.Replace(json, ",\"" + field + "\":[^,}]*", string.Empty);
            StringAssert.DoesNotContain("TipCount", json);
            var old = JsonUtility.FromJson<MiningShopState>(json);
            MiningShopBusinessSimulation opened = Open(old, null, Ungated());
            Assert.That(opened.View.TipCount, Is.Zero);
            Assert.That(opened.View.TipsEarned, Is.Zero);

            MiningShopState broken = JsonUtility.FromJson<MiningShopState>(JsonUtility.ToJson(state));
            broken.Business.TipCount = -1L;
            Assert.Throws<ArgumentException>(() => Open(broken, null, Ungated()));
            broken.Business.TipCount = 0L;
            broken.Business.TipsEarned = double.NaN;
            Assert.Throws<ArgumentException>(() => Open(broken, null, Ungated()));
        }

        [Test]
        public void TheBenchQuotesItsTipChanceFromItsStars()
        {
            MiningShopBusinessSimulation sim = Open(Fresh(), null, Ungated());
            Assert.That(sim.TipChance(0), Is.EqualTo(0.10d).Within(1e-12));
            Assert.That(sim.TipChance(1), Is.Zero, "not built");
            sim.BuyLevels(0, 24);   // level 25: two stars
            Assert.That(sim.TipChance(0), Is.EqualTo(0.12d).Within(1e-12));
        }
    }
}
