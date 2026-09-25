using System;
using Game.Core;
using Game.Data;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class MiningShopCampaignTests
    {
        private MiningShopCampaignConfig _config;
        private MiningShopCampaign _campaign;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<MiningShopCampaignConfig>();
            _campaign = _config.CreateCampaign();
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(_config);

        [Test]
        public void ExampleContentHasSevenSixAndNineIslands()
        {
            Assert.That(_campaign.ChapterCount, Is.EqualTo(3));
            Assert.That(_campaign.IslandCount, Is.EqualTo(22));
            var counts = new int[3];
            for (int i = 0; i < _campaign.IslandCount; i++)
                counts[_campaign.IslandAt(i).ChapterNumber - 1]++;
            CollectionAssert.AreEqual(new[] { 7, 6, 9 }, counts);
        }

        [TestCase("mining-shop.island-01-07", "mining-shop.island-02-01", 2, 7)]
        [TestCase("mining-shop.island-02-06", "mining-shop.island-03-01", 3, 13)]
        public void ChapterBoundaryContinuesOverallDifficultyOrder(string from, string expected, int chapter, int index)
        {
            Assert.That(_campaign.TryGet(from, out var previous), Is.True);
            Assert.That(_campaign.TryGetNext(from, out var next), Is.True);
            Assert.That(next.Id, Is.EqualTo(expected));
            Assert.That(next.ChapterNumber, Is.EqualTo(chapter));
            Assert.That(next.IslandNumber, Is.EqualTo(1));
            Assert.That(next.CampaignIndex, Is.EqualTo(index));
            Assert.That(next.CampaignIndex, Is.GreaterThan(previous.CampaignIndex));
        }

        [Test]
        public void AllBusinessesStartWithOneTableEvenWhenFourProductsAreAvailable()
        {
            for (int i = 0; i < _campaign.IslandCount; i++)
            {
                var island = _campaign.IslandAt(i);
                Assert.That(island.StartingTableCount, Is.EqualTo(1), island.Id);
                Assert.That(island.AvailableProductCount, Is.EqualTo(MiningShopCampaign.ProductCount), island.Id);
            }
        }

        [Test]
        public void ProductUnlockOrderDoesNotReorderCaptainEquipment()
        {
            Assert.That(MiningShopCampaign.ProductIdAt(0), Is.EqualTo("mining-shop.pickaxe"));
            Assert.That(MiningShopCampaign.ProductIdAt(1), Is.EqualTo("mining-shop.helmet"));
            Assert.That(MiningShopCampaign.ProductIdAt(2), Is.EqualTo("mining-shop.lantern"));
            Assert.That(MiningShopCampaign.ProductIdAt(3), Is.EqualTo("mining-shop.bag"));
            Assert.That(MiningGear.SlotBag, Is.EqualTo(2));
            Assert.That(MiningGear.SlotLantern, Is.EqualTo(3));
        }

        [TestCase(-1)]
        [TestCase(4)]
        public void InvalidProductIndexDoesNotAliasMerchandise(int index)
            => Assert.Throws<ArgumentOutOfRangeException>(() => MiningShopCampaign.ProductIdAt(index));

        [TestCase(null)]
        [TestCase("")]
        [TestCase("missing")]
        public void UnknownBusinessDoesNotFallBackToTheFirstIsland(string id)
        {
            Assert.That(_campaign.TryGet(id, out _), Is.False);
            Assert.That(_campaign.TryGetNext(id, out _), Is.False);
        }

        [Test]
        public void LastAuthoredBusinessDoesNotWrapOrInventAChapter()
            => Assert.That(_campaign.TryGetNext("mining-shop.island-03-09", out _), Is.False);

        [Test]
        public void AdditionalChaptersAndNonNumericStableIdsRequireNoCodeChange()
        {
            var campaign = new MiningShopCampaign(new[] { "coast", "cliffs", "forest", "desert" },
                new[] { new[] { "dock" }, new[] { "ridge", "summit" }, new[] { "grove" }, new[] { "oasis", "dunes" } });
            Assert.That(campaign.IslandCount, Is.EqualTo(6));
            Assert.That(campaign.TryGetNext("grove", out var next), Is.True);
            Assert.That(next.Id, Is.EqualTo("oasis"));
            Assert.That(next.ChapterNumber, Is.EqualTo(4));
            Assert.That(next.CampaignIndex, Is.EqualTo(4));
            Assert.That(next.AvailableProductCount, Is.EqualTo(4));
        }

        [Test]
        public void CatalogueDoesNotRetainMutableSourceArrays()
        {
            var chapters = new[] { "coast" };
            var islands = new[] { new[] { "dock" } };
            var campaign = new MiningShopCampaign(chapters, islands);
            chapters[0] = "changed";
            islands[0][0] = "changed";
            Assert.That(campaign.TryGet("dock", out var island), Is.True);
            Assert.That(island.ChapterId, Is.EqualTo("coast"));
            Assert.That(campaign.TryGet("changed", out _), Is.False);
        }

        [Test]
        public void InvalidContentIsRejectedBeforeRegistration()
        {
            Assert.Throws<ArgumentNullException>(() => new MiningShopCampaign(null, new[] { new[] { "a" } }));
            Assert.Throws<ArgumentNullException>(() => new MiningShopCampaign(new[] { "c" }, null));
            Assert.Throws<ArgumentException>(() => new MiningShopCampaign(Array.Empty<string>(), Array.Empty<string[]>()));
            Assert.Throws<ArgumentException>(() => new MiningShopCampaign(new[] { "c" }, Array.Empty<string[]>()));
            Assert.Throws<ArgumentException>(() => new MiningShopCampaign(new[] { "c" }, new string[][] { null }));
            Assert.Throws<ArgumentException>(() => new MiningShopCampaign(new[] { "c" }, new[] { Array.Empty<string>() }));
            Assert.Throws<ArgumentException>(() => new MiningShopCampaign(new[] { "c", "c" }, new[] { new[] { "a" }, new[] { "b" } }));
            Assert.Throws<ArgumentException>(() => new MiningShopCampaign(new[] { "c", "d" }, new[] { new[] { "a" }, new[] { "a" } }));
            Assert.Throws<ArgumentException>(() => new MiningShopCampaign(new[] { " c" }, new[] { new[] { "a" } }));
            Assert.Throws<ArgumentException>(() => new MiningShopCampaign(new[] { "c" }, new[] { new[] { " " } }));
        }
    }
}
