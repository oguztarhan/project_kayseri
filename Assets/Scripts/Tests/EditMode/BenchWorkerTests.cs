using Game.Core;
using Game.Data;
using Game.Systems;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>Shop bench workers: masters from the roster, one bench each, posted automatically and swapped by hand.</summary>
    public sealed class BenchWorkerTests
    {
        private const string BusinessId = "mining-shop.island-01-04";
        private const int Hasan = 0, Sukru = 1, Nazmi = 2, Riza = 12;
        private MiningShopCampaignConfig _campaignConfig;
        private MiningShopCampaign _campaign;
        private SaveData _data;
        private WalletService _wallet;
        private ForemanService _foremen;

        [SetUp]
        public void SetUp()
        {
            _campaignConfig = ScriptableObject.CreateInstance<MiningShopCampaignConfig>();
            _campaign = _campaignConfig.CreateCampaign();
            _data = new SaveData { masterStars = new int[Foremen.Count] };
            _wallet = new WalletService(_data.wallet);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_campaignConfig);

        /// <summary>Opens the shop with the roster as it stands in the save.</summary>
        private MiningShopBusinessService Open()
        {
            _foremen = new ForemanService(_data, _wallet, Foremen.Tuning.Default);
            var market = new MarketService(_data, _wallet, null, null, _foremen);
            return market.OpenMiningShopBusiness(_campaign, BusinessId, MiningShopBusinessSimulation.Tuning.Default);
        }

        private void BuildSecondBench(MiningShopBusinessService shop)
        {
            _wallet.AddCash(new BigDouble(1e9));
            Assert.That(shop.TryBuyLevels(0, 24), Is.EqualTo(24));
            Assert.That(shop.TryBuildTable(1), Is.True);
        }

        [Test]
        public void TheBestIdleMasterIsTheMostThroughputNotAlreadyTaken()
        {
            var stars = new int[Foremen.Count];
            Foremen.Tuning t = Foremen.Tuning.Default;
            Assert.That(Foremen.BestIdle(stars, null, t), Is.EqualTo(-1), "nobody owned");
            stars[Hasan] = 5;   // +50%
            stars[Sukru] = 1;   // +30%
            stars[Riza] = 5;    // +50%, a later index
            Assert.That(Foremen.BestIdle(stars, new[] { -1, -1 }, t), Is.EqualTo(Hasan), "ties go to the lower index");
            Assert.That(Foremen.BestIdle(stars, new[] { Hasan, -1 }, t), Is.EqualTo(Riza));
            Assert.That(Foremen.BestIdle(stars, new[] { Hasan, Riza }, t), Is.EqualTo(Sukru));
            Assert.That(Foremen.BestIdle(stars, new[] { Hasan, Riza, Sukru }, t), Is.EqualTo(-1));
            stars[Nazmi] = 1;   // +80% beats a maxed Common
            Assert.That(Foremen.BestIdle(stars, null, t), Is.EqualTo(Nazmi));
        }

        [Test]
        public void BuiltBenchesTakeTheBestIdleMastersOneBenchEach()
        {
            _data.masterStars[Hasan] = 5;
            _data.masterStars[Sukru] = 1;
            MiningShopBusinessService shop = Open();
            Assert.That(shop.WorkerAt(0), Is.EqualTo(Hasan), "+25% before +15%");
            Assert.That(shop.WorkerAt(1), Is.EqualTo(-1), "an unbuilt bench employs nobody");
            Assert.That(shop.WorkerMultiplier(0), Is.EqualTo(1.25d).Within(1e-12), "half his station's +50%");

            BuildSecondBench(shop);
            Assert.That(shop.WorkerAt(1), Is.EqualTo(Sukru), "the build posts whoever is left");
            Assert.That(shop.WorkerMultiplier(1), Is.EqualTo(1.15d).Within(1e-12));
            Assert.That(shop.BenchOf(Sukru), Is.EqualTo(1));
            Assert.That(shop.BenchOf(Nazmi), Is.EqualTo(-1));
            Assert.That(_data.miningShopBusinesses[0].Business.Lines[1].Worker, Is.EqualTo(Sukru), "saved on the bench");
            Assert.That(_foremen.ActiveAt(Foremen.Mine), Is.EqualTo(Sukru),
                "station posts are a separate board: the rarest mine card still runs the mine");
        }

        [Test]
        public void TheWorkerMultipliesEveryItemAndTheSteadyRate()
        {
            MiningShopBusinessService apprentice = Open();
            double price = apprentice.UnitPrice(0), rate = apprentice.SteadyStateRate();
            Assert.That(apprentice.WorkerAt(0), Is.EqualTo(-1));
            Assert.That(apprentice.WorkerMultiplier(0), Is.EqualTo(1d));

            TearDown();
            SetUp();
            _data.masterStars[Nazmi] = 5;   // a maxed Legendary: +400% at his station, +200% at a bench
            MiningShopBusinessService master = Open();
            Assert.That(master.UnitPrice(0), Is.EqualTo(price * 3d).Within(1e-9));
            Assert.That(master.SteadyStateRate(), Is.EqualTo(rate * 3d).Within(1e-9),
                "offline and income-minute rewards pay the worker too");
        }

        [Test]
        public void ASwapTradesBenchesAndRefusesWhatCannotWork()
        {
            _data.masterStars[Hasan] = 5;
            _data.masterStars[Sukru] = 1;
            MiningShopBusinessService shop = Open();
            BuildSecondBench(shop);
            int moves = 0;
            shop.WorkersChanged += () => moves++;

            Assert.That(shop.TrySetWorker(0, Sukru), Is.True);
            Assert.That(shop.WorkerAt(0), Is.EqualTo(Sukru));
            Assert.That(shop.WorkerAt(1), Is.EqualTo(Hasan), "the other bench takes the one it gave up");
            Assert.That(shop.WorkerMultiplier(0), Is.EqualTo(1.15d).Within(1e-12));
            Assert.That(shop.WorkerMultiplier(1), Is.EqualTo(1.25d).Within(1e-12));
            Assert.That(moves, Is.EqualTo(1));

            Assert.That(shop.TrySetWorker(0, Sukru), Is.False, "already there");
            Assert.That(shop.TrySetWorker(0, Nazmi), Is.False, "not owned");
            Assert.That(shop.TrySetWorker(2, Hasan), Is.False, "not built");
            Assert.That(shop.WorkerAt(1), Is.EqualTo(Hasan));
        }

        [Test]
        public void ASavedPostingThatNoLongerHoldsIsReplacedOnOpen()
        {
            _data.masterStars[Hasan] = 5;
            _data.masterStars[Sukru] = 1;
            MiningShopBusinessService shop = Open();
            BuildSecondBench(shop);
            var lines = _data.miningShopBusinesses[0].Business.Lines;
            lines[0].Worker = Nazmi;    // never owned
            lines[1].Worker = Sukru;
            lines[2].Worker = Hasan;    // an unbuilt bench

            MiningShopBusinessService reopened = Open();
            Assert.That(reopened.WorkerAt(1), Is.EqualTo(Sukru), "a good posting is kept");
            Assert.That(reopened.WorkerAt(0), Is.EqualTo(Hasan), "the bad one is refilled with the best idle");
            Assert.That(lines[2].Worker, Is.EqualTo(-1), "an unbuilt bench lets him go");

            lines[0].Worker = Sukru;    // two benches, one man
            reopened = Open();
            Assert.That(reopened.WorkerAt(0), Is.EqualTo(Sukru), "the first bench keeps him");
            Assert.That(reopened.WorkerAt(1), Is.EqualTo(Hasan));
        }

        [Test]
        public void ACardArrivingFillsAnEmptyBenchAndAStarReprices()
        {
            MiningShopBusinessService shop = Open();
            Assert.That(shop.WorkerAt(0), Is.EqualTo(-1), "no masters yet: the apprentice");
            double apprenticeRate = shop.SteadyStateRate();
            int moves = 0;
            shop.WorkersChanged += () => moves++;

            _foremen.GrantDuplicates(Riza, 1);
            Assert.That(shop.WorkerAt(0), Is.EqualTo(Riza));
            Assert.That(shop.WorkerMultiplier(0), Is.EqualTo(1.05d).Within(1e-12));
            Assert.That(shop.SteadyStateRate(), Is.EqualTo(apprenticeRate * 1.05d).Within(1e-9));

            _foremen.GrantDuplicates(Riza, Foremen.CardsToStar(Riza, 1, Foremen.Tuning.Default));
            Assert.That(_foremen.TryLevelUp(Riza), Is.True);
            Assert.That(shop.WorkerMultiplier(0), Is.EqualTo(1.1d).Within(1e-12));
            Assert.That(moves, Is.EqualTo(3), "two arrivals and a star");

            _foremen.GrantDuplicates(Nazmi, 1);
            Assert.That(shop.WorkerAt(0), Is.EqualTo(Riza), "auto-posting only fills; a better card waits for the player");
        }

        [Test]
        public void WithoutARosterEveryBenchIsTheApprenticeAndTheSaveIsLeftAlone()
        {
            var market = new MarketService(_data, _wallet, null);
            MiningShopBusinessService shop = market.OpenMiningShopBusiness(_campaign, BusinessId,
                MiningShopBusinessSimulation.Tuning.Default);
            _data.miningShopBusinesses[0].Business.Lines[0].Worker = Sukru;
            Assert.That(shop.WorkerAt(0), Is.EqualTo(-1));
            Assert.That(shop.WorkerMultiplier(0), Is.EqualTo(1d));
            Assert.That(shop.TrySetWorker(0, Sukru), Is.False);
            Assert.That(_data.miningShopBusinesses[0].Business.Lines[0].Worker, Is.EqualTo(Sukru));
        }
    }
}
