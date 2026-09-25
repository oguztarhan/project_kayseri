using Game.Core;
using Game.Systems;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>Shop coins end to end: spawn, collect once, miss and come back, the caps, the reset, the clock, reload.</summary>
    public sealed class ShopCoinServiceTests
    {
        private const long Start = 11000L * ShopCoins.CycleSeconds;
        private const double Income = 500d;

        private SaveData _data;
        private WalletService _wallet;
        private long _now;

        [SetUp]
        public void SetUp()
        {
            _data = new SaveData { tutorialStep = TutorialProgress.StepDone };
            _now = Start + 10L;
        }

        private ShopCoinService Launch() => Launch(ShopCoins.Tuning.Default);

        private ShopCoinService Launch(ShopCoins.Tuning tuning)
        {
            _wallet = new WalletService(_data.wallet);
            return new ShopCoinService(_wallet, tuning, _data, () => _now,
                new BoostService(_data, new TimeService()));
        }

        private static void Spawn(ShopCoinService coins)
            => Assert.AreEqual(ShopCoinService.TickResult.Spawned, coins.Tick(1e6d), "no coin spawned");

        private static ShopCoinService.Receipt Collect(ShopCoinService coins)
        {
            Assert.IsTrue(coins.TryCollect(Income, out ShopCoinService.Receipt receipt), "the coin did not pay");
            return receipt;
        }

        private static void CollectMany(ShopCoinService coins, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Spawn(coins);
                Collect(coins);
            }
        }

        [Test]
        public void NothingSpawnsBeforeTheTutorialIsDone()
        {
            _data.tutorialStep = 0;
            ShopCoinService coins = Launch();
            Assert.AreEqual(ShopCoinService.TickResult.None, coins.Tick(1e6d));
            Assert.IsFalse(coins.HasLiveCoin);
        }

        [Test]
        public void TheFirstCoinComesAfterTheFirstWaitOfEligiblePlay()
        {
            ShopCoinService coins = Launch();
            Assert.AreEqual(0, coins.Collected);
            double wait = coins.SecondsToNextSpawn;
            Assert.That(wait, Is.InRange(45d, 90d));

            Assert.AreEqual(ShopCoinService.TickResult.None, coins.Tick(wait - 0.5d));
            Assert.AreEqual(ShopCoinService.TickResult.Spawned, coins.Tick(1d));
            Assert.IsTrue(coins.HasLiveCoin);
            Assert.AreEqual(20d, coins.LiveSecondsLeft);
            Assert.That(coins.SecondsToNextSpawn, Is.InRange(300d, 540d));
        }

        [Test]
        public void OnlyOneCoinShowsAtATime()
        {
            ShopCoinService coins = Launch();
            Spawn(coins);
            Assert.AreEqual(ShopCoinService.TickResult.None, coins.Tick(10d));
            Assert.IsTrue(coins.HasLiveCoin);
        }

        [Test]
        public void ACoinPaysOnceAndCountsTowardTheCycle()
        {
            ShopCoinService coins = Launch();
            Spawn(coins);
            ShopCoinService.Receipt receipt = Collect(coins);

            Assert.AreEqual(1, receipt.Collected);
            Assert.AreEqual(12, receipt.Cap);
            Assert.AreEqual(1, coins.Collected);
            Assert.IsFalse(coins.HasLiveCoin);
            Assert.IsFalse(coins.TryCollect(Income, out _), "a second tap paid again");
        }

        [Test]
        public void AMissedCoinGoesBackToTheCycleAndReturnsAfterTheGap()
        {
            ShopCoinService coins = Launch();
            Spawn(coins);
            double gap = coins.SecondsToNextSpawn;

            Assert.AreEqual(ShopCoinService.TickResult.None, coins.Tick(19d));
            Assert.AreEqual(ShopCoinService.TickResult.Expired, coins.Tick(1d));
            Assert.IsFalse(coins.HasLiveCoin);
            Assert.IsFalse(coins.TryCollect(Income, out _), "an expired coin paid");
            Assert.AreEqual(0, coins.Collected);
            Assert.IsTrue(coins.CanSpawnMore);

            Assert.AreEqual(ShopCoinService.TickResult.None, coins.Tick(gap - 0.5d));
            Assert.AreEqual(ShopCoinService.TickResult.Spawned, coins.Tick(1d));
        }

        [Test]
        public void MissingCoinsNeverChangesWhatTheNextOnePays()
        {
            ShopCoinService coins = Launch();
            for (int miss = 0; miss < 3; miss++)
            {
                Spawn(coins);
                Assert.AreEqual(ShopCoinService.TickResult.Expired, coins.Tick(20d));
            }
            Spawn(coins);
            ShopCoinService.Receipt receipt = Collect(coins);

            ShopCoins.Reward expected = ShopCoins.RewardFor(_data.playerId, ShopCoins.CycleAt(_now), 0, ShopCoins.Tuning.Default);
            Assert.AreEqual(expected.Kind, receipt.Kind);
        }

        [Test]
        public void TheFirstDayStopsAtEightAndTheCycleAtTwelve()
        {
            ShopCoinService coins = Launch();
            CollectMany(coins, 8);
            Assert.IsFalse(coins.CanSpawnMore);
            Assert.AreEqual(ShopCoinService.TickResult.None, coins.Tick(1e6d));

            _now = Start + ShopCoins.DaySeconds;
            Assert.IsTrue(coins.CanSpawnMore);
            CollectMany(coins, 4);
            Assert.AreEqual(12, coins.Collected);
            Assert.IsFalse(coins.CanSpawnMore);
            Assert.AreEqual(ShopCoinService.TickResult.None, coins.Tick(1e6d));
        }

        [Test]
        public void TheResetOpensAFreshCycleWithTheShortFirstWait()
        {
            ShopCoinService coins = Launch();
            CollectMany(coins, 8);

            _now = Start + ShopCoins.CycleSeconds;
            Assert.AreEqual(0, coins.Collected);
            Assert.IsTrue(coins.CanSpawnMore);
            Assert.That(coins.SecondsToNextSpawn, Is.InRange(45d, 90d));
            Assert.AreEqual(ShopCoins.CycleSeconds, coins.SecondsToReset);
        }

        [Test]
        public void ACoinShowingAtTheResetCountsTowardTheOldCycle()
        {
            ShopCoinService coins = Launch();
            CollectMany(coins, 3);
            Spawn(coins);

            _now = Start + ShopCoins.CycleSeconds;
            ShopCoinService.Receipt receipt = Collect(coins);
            Assert.AreEqual(0, receipt.Collected, "the old cycle's coin counted toward the new one");
            Assert.AreEqual(0, coins.Collected);
        }

        [Test]
        public void SettingTheClockBackFreezesTheCoinsUntilItCatchesUp()
        {
            ShopCoinService coins = Launch();
            CollectMany(coins, 2);
            Spawn(coins);

            _now = Start - 100L;
            Assert.IsTrue(coins.Frozen);
            Assert.IsFalse(coins.CanSpawnMore);
            Assert.AreEqual(ShopCoinService.TickResult.Expired, coins.Tick(1d), "the coin on screen stayed up");
            Assert.AreEqual(ShopCoinService.TickResult.None, coins.Tick(1e6d));
            Assert.IsFalse(coins.TryCollect(Income, out _));

            _now = Start + 50L;
            Assert.IsFalse(coins.Frozen);
            Assert.AreEqual(2, coins.Collected, "the rollback reset the cycle");
        }

        [Test]
        public void JumpingTheClockAheadOpensOneFreshCycleNotABacklog()
        {
            ShopCoinService coins = Launch();
            CollectMany(coins, 5);

            _now = Start + 5L * ShopCoins.CycleSeconds + 3L * ShopCoins.DaySeconds / 2L;
            Assert.AreEqual(0, coins.Collected);
            CollectMany(coins, 12);
            Assert.IsFalse(coins.CanSpawnMore);
        }

        [Test]
        public void ARelaunchDropsTheCoinOnScreenAndKeepsTheWait()
        {
            ShopCoinService coins = Launch();
            CollectMany(coins, 1);
            Spawn(coins);
            double gap = coins.SecondsToNextSpawn;

            ShopCoinService relaunched = Launch();
            Assert.IsFalse(relaunched.HasLiveCoin);
            Assert.AreEqual(1, relaunched.Collected);
            Assert.AreEqual(gap, relaunched.SecondsToNextSpawn, "the restart shortened the wait");
            Assert.AreEqual(ShopCoinService.TickResult.None, relaunched.Tick(gap - 0.5d));
            Assert.AreEqual(ShopCoinService.TickResult.Spawned, relaunched.Tick(1d));
        }

        [Test]
        public void EachRewardKindPaysWhatItSays()
        {
            ShopCoins.Tuning cashOnly = OnlyOne(ShopCoins.RewardKind.MediumCash);
            ShopCoinService coins = Launch(cashOnly);
            Spawn(coins);
            ShopCoinService.Receipt cash = Collect(coins);
            Assert.AreEqual(ShopCoins.RewardKind.MediumCash, cash.Kind);
            Assert.AreEqual(Income * 5d, cash.Cash);
            Assert.AreEqual(Income * 5d, _wallet.Cash.ToDouble(), 1e-6);

            SetUp();
            coins = Launch(OnlyOne(ShopCoins.RewardKind.Gems));
            Spawn(coins);
            ShopCoinService.Receipt gems = Collect(coins);
            Assert.AreEqual(5L, gems.Gems);
            Assert.AreEqual(5L, _wallet.Gems);

            SetUp();
            coins = Launch(OnlyOne(ShopCoins.RewardKind.Boost));
            Spawn(coins);
            ShopCoinService.Receipt boost = Collect(coins);
            Assert.AreEqual(ShopCoins.RewardKind.Boost, boost.Kind);
            Assert.AreEqual(2d, _data.boostMultiplier);
            Assert.Greater(_data.boostEndUnix, 0L);
        }

        [Test]
        public void BadSavedValuesAreRepairedOnLaunch()
        {
            _data.shopCoins = new ShopCoinSaveData
            {
                cycle = ShopCoins.CycleAt(_now),
                collected = -4,
                spawns = -1,
                secondsToNextSpawn = double.NaN
            };
            ShopCoinService coins = Launch();
            Assert.AreEqual(0, coins.Collected);
            Assert.AreEqual(0d, coins.SecondsToNextSpawn);

            _data.shopCoins.secondsToNextSpawn = 1e12d;
            Launch();
            Assert.AreEqual(540d, _data.shopCoins.secondsToNextSpawn);
        }

        [Test]
        public void ASaveWithoutTheBlockStartsAFreshCycle()
        {
            _data.shopCoins = null;
            ShopCoinService coins = Launch();
            Assert.AreEqual(0, coins.Collected);
            Assert.AreEqual(ShopCoins.CycleAt(_now), _data.shopCoins.cycle);
        }

        private static ShopCoins.Tuning OnlyOne(ShopCoins.RewardKind kind)
        {
            ShopCoins.Tuning t = ShopCoins.Tuning.Default;
            t.SmallCashWeight = kind == ShopCoins.RewardKind.SmallCash ? 1d : 0d;
            t.MediumCashWeight = kind == ShopCoins.RewardKind.MediumCash ? 1d : 0d;
            t.BoostWeight = kind == ShopCoins.RewardKind.Boost ? 1d : 0d;
            t.GemWeight = kind == ShopCoins.RewardKind.Gems ? 1d : 0d;
            t.JackpotWeight = kind == ShopCoins.RewardKind.Jackpot ? 1d : 0d;
            return t;
        }
    }
}
