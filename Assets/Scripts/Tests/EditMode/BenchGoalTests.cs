using Game.Core;
using Game.Data;
using Game.Systems;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>The goal card's pick: the built bench whose next star is cheapest to reach.</summary>
    public sealed class BenchGoalTests
    {
        private const string BusinessId = "mining-shop.island-01-04";
        private MiningShopCampaignConfig _campaignConfig;
        private SaveData _data;
        private WalletService _wallet;
        private MiningShopBusinessService _shop;

        [SetUp]
        public void SetUp()
        {
            _campaignConfig = ScriptableObject.CreateInstance<MiningShopCampaignConfig>();
            _data = new SaveData { masterStars = new int[Foremen.Count] };
            _wallet = new WalletService(_data.wallet);
            var market = new MarketService(_data, _wallet, null, null, new ForemanService(_data, _wallet, Foremen.Tuning.Default));
            _shop = market.OpenMiningShopBusiness(_campaignConfig.CreateCampaign(), BusinessId,
                MiningShopBusinessSimulation.Tuning.Default);
            _wallet.AddCash(new BigDouble(1e30));
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_campaignConfig);

        private double ToNextStar(int bench)
            => _shop.CostOfLevels(bench, BenchMastery.LevelsToNextStar(_shop.View.ProductAt(bench).Level));

        [Test]
        public void TheFirstBenchIsTheGoalUntilAnotherIsBuilt()
        {
            Assert.That(_shop.NextStarBench(), Is.EqualTo(0));
            Assert.That(_shop.TryBuyLevels(0, 24), Is.EqualTo(24));
            Assert.That(_shop.NextStarBench(), Is.EqualTo(0), "an unbuilt bench is never the goal");
        }

        [Test]
        public void TheCheaperNextStarWins()
        {
            _shop.TryBuyLevels(0, 24);
            Assert.That(_shop.TryBuildTable(1), Is.True);
            int expected = ToNextStar(1) < ToNextStar(0) ? 1 : 0;
            Assert.That(_shop.NextStarBench(), Is.EqualTo(expected));

            // Take the helmet bench to its first star: now its next star is far off and the pickaxe's is nearer.
            _shop.TryBuyLevels(1, 9);
            expected = ToNextStar(1) < ToNextStar(0) ? 1 : 0;
            Assert.That(_shop.NextStarBench(), Is.EqualTo(expected));
            Assert.That(ToNextStar(expected), Is.LessThanOrEqualTo(ToNextStar(1 - expected)));
        }

        [Test]
        public void NoGoalOnceEveryBuiltBenchHasAllItsStars()
        {
            Assert.That(_shop.TryBuyLevels(0, BenchMastery.MaxLevel - 1), Is.EqualTo(BenchMastery.MaxLevel - 1));
            Assert.That(_shop.NextStarBench(), Is.EqualTo(-1), "the pickaxe bench is maxed and nothing else is built");
            Assert.That(_shop.TryBuildTable(1), Is.True);
            Assert.That(_shop.NextStarBench(), Is.EqualTo(1));
        }
    }
}
