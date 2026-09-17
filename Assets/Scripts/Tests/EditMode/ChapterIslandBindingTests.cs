using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Game.Core;
using Game.Gameplay;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>
    /// What a chapter binds the island to, and — mostly — what it must leave alone.
    ///
    /// The island is one physical place for the whole game. A chapter changes where its progress is
    /// FILED and what its economy is multiplied by; it must not change the island's name, its goods or
    /// anything else the player would read as a different island.
    /// </summary>
    public class ChapterIslandBindingTests
    {
        private static Chapters.Tuning T => Chapters.Tuning.Default;

        private GameObject _host;
        private SaveData _data;

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
            ServiceLocator.Clear();
        }

        /// <summary>
        /// Builds the island with the services it reads in Awake, standing on <paramref name="chapter"/>.
        /// Awake is invoked directly: the component is added to a bare object rather than loaded from a
        /// scene, so Unity never calls it.
        /// </summary>
        private CoalOperation Island(int chapter)
        {
            ServiceLocator.Clear();
            _data = new SaveData();
            var wallet = new WalletService(_data.wallet);
            for (int c = 1; c <= chapter; c++) _data.unlockedIslands.Add(Chapters.Namespace(c));

            var chapters = new ChapterService(_data, wallet, null, T);
            var market = new MarketService(_data, wallet, null);
            ServiceLocator.Register(_data);
            ServiceLocator.Register(wallet);
            ServiceLocator.Register(chapters);
            ServiceLocator.Register(market);
            ServiceLocator.Register(new ChapterProgressionService(_data, chapters, null));

            _host = new GameObject("ChapterIslandBinding");
            var island = _host.AddComponent<CoalOperation>();
            typeof(CoalOperation).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                                 .Invoke(island, null);
            return island;
        }

        // ------------------------------------------------------------------ the namespace
        [Test]
        public void WithNoProgressionServiceTheIslandFilesUnderItsOwnKey()
        {
            ServiceLocator.Clear();
            _host = new GameObject("ChapterIslandBinding");
            var island = _host.AddComponent<CoalOperation>();

            Assert.That(island.ProgressionKey, Is.EqualTo(island.IslandKey),
                        "no chapters registered is chapter one, which is the island's own key");
        }

        [Test]
        public void TheIslandFilesItsProgressionUnderTheCurrentChaptersNamespace()
        {
            CoalOperation island = Island(3);

            Assert.That(island.ProgressionKey, Is.EqualTo(Chapters.Namespace(3)));
            Assert.That(island.IslandKey, Is.EqualTo("coal"), "the physical island never moves");
        }

        [Test]
        public void TheFirstChaptersNamespaceIsTheIslandsOwnKey()
        {
            CoalOperation island = Island(0);

            Assert.That(island.ProgressionKey, Is.EqualTo("coal"));
            Assert.That(island.ProgressionKey, Is.EqualTo(island.IslandKey),
                        "chapter one must be indistinguishable from the game before chapters existed");
        }

        // ------------------------------------------------------------------ what must not move
        /// <summary>
        /// The namespaces read as ore names — copper, iron, gold — and the island's own labels are
        /// built from its key. If those followed the chapter, chapter 5 would rename the power plant
        /// GOLD POWER PLANT on a map that never changed. Only the theme is allowed to move.
        /// </summary>
        [Test]
        public void TheIslandsNameAndBuildingsDoNotFollowTheChapter()
        {
            CoalOperation first = Island(0);
            string name = first.OreName;
            string powerPlant = first.PowerPlantName;
            string unlock = first.UnlockName(0);
            Object.DestroyImmediate(_host);

            CoalOperation later = Island(5);

            Assert.That(later.OreName, Is.EqualTo(name), "the ore word is the island's, not the chapter's");
            Assert.That(later.PowerPlantName, Is.EqualTo(powerPlant));
            Assert.That(later.UnlockName(0), Is.EqualTo(unlock));
        }

        // ------------------------------------------------------------------ the economy
        /// <summary>
        /// The ceiling is a MEASURED number for a maxed island. It has to ride the chapter's scale or
        /// every chapter after the first is clamped back to chapter one's income while its costs climb.
        /// </summary>
        [Test]
        public void TheIncomeCeilingRidesTheChaptersScale()
        {
            CoalOperation first = Island(0);
            double baseCeiling = ((IIslandSaleTerms)first).IncomeCapPerMinuteRaw;
            Object.DestroyImmediate(_host);

            for (int chapter = 1; chapter < Chapters.Count; chapter++)
            {
                CoalOperation island = Island(chapter);
                double expected = baseCeiling * Chapters.EconomyScale(chapter, T);

                Assert.That(((IIslandSaleTerms)island).IncomeCapPerMinuteRaw,
                            Is.EqualTo(expected).Within(expected * 1e-6),
                            "chapter " + chapter);
                Object.DestroyImmediate(_host);
            }
        }

        /// <summary>
        /// Costs climb with value, so the chapter keeps its shape. Read off the upgrade tree rather
        /// than a single price: that is the number the yard's own terms quote.
        /// </summary>
        [Test]
        public void TheUpgradeTreeCostsMoreOnEveryChapterByTheSameScale()
        {
            CoalOperation first = Island(0);
            double baseTree = ((IIslandSaleTerms)first).UpgradeTreeCostRaw;
            Object.DestroyImmediate(_host);

            Assert.That(baseTree, Is.GreaterThan(0d));

            CoalOperation second = Island(1);
            double expected = baseTree * Chapters.EconomyScale(1, T);

            Assert.That(((IIslandSaleTerms)second).UpgradeTreeCostRaw,
                        Is.EqualTo(expected).Within(expected * 1e-6));
        }

        // ------------------------------------------------------------------ the goods
        [Test]
        public void TheIslandSellsTheSameProductOnEveryChapter()
        {
            for (int chapter = 0; chapter < Chapters.Count; chapter++)
            {
                CoalOperation island = Island(chapter);
                var market = ServiceLocator.Get<MarketService>();

                Assert.That(market.Product(island.ProgressionKey).productId,
                            Is.EqualTo(MarketService.IslandProduct), "chapter " + chapter);
                Object.DestroyImmediate(_host);
            }
        }

        /// <summary>
        /// The island registers its yard under the chapter's namespace, so the chapter it is playing is
        /// the one that gets paid — not the one it finished.
        /// </summary>
        [Test]
        public void TheIslandRegistersItsYardUnderTheChapterItIsPlaying()
        {
            CoalOperation island = Island(2);
            var market = ServiceLocator.Get<MarketService>();

            market.Deliver(island.ProgressionKey, MarketService.IslandProduct, 4d);

            Assert.That(market.Stock(Chapters.Namespace(2)), Is.EqualTo(4d).Within(1e-9));
            Assert.That(market.Stock("coal"), Is.Zero.Within(1e-9),
                        "the finished chapter's yard is not fed by the one being played");
        }
    }
}
