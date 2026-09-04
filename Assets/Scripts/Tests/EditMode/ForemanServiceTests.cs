using NUnit.Framework;
using Game.Core;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>
    /// The chest as a transaction: what it takes, what it hands over, and what it must never do
    /// twice. <see cref="MasterChestTests"/> covers the maths; this covers the till.
    /// </summary>
    public class ForemanServiceTests
    {
        private static MasterChest.Tuning C => MasterChest.Tuning.Default;

        private static ForemanService Build(out SaveData data, out WalletService wallet, long gems = 10000L)
        {
            data = new SaveData();
            data.wallet.gems = gems;
            wallet = new WalletService(data.wallet);
            return new ForemanService(data, wallet, Foremen.Tuning.Default, C);
        }

        private static int TotalCards(SaveData data)
        {
            int n = 0;
            for (int i = 0; i < data.foremanDuplicates.Length; i++) n += data.foremanDuplicates[i];
            return n;
        }

        // ---- paying for it -----------------------------------------------------------------------

        [Test]
        public void AChestThatCannotBePaidForChangesNothing()
        {
            SaveData data; WalletService wallet;
            ForemanService foremen = Build(out data, out wallet, gems: 10L);

            Assert.That(foremen.CanOpenChest(1), Is.False);
            Assert.That(foremen.TryOpenChest(1), Is.Null);
            Assert.That(wallet.Gems, Is.EqualTo(10L), "a refused open must not take the gems anyway");
            Assert.That(TotalCards(data), Is.Zero);
            Assert.That(foremen.HiredCount, Is.Zero);
        }

        [Test]
        public void OpeningTakesTheGemsExactlyOnce()
        {
            SaveData data; WalletService wallet;
            ForemanService foremen = Build(out data, out wallet);
            long before = wallet.Gems;

            int[] got = foremen.TryOpenChest(1);
            Assert.That(got, Is.Not.Null);
            Assert.That(wallet.Gems, Is.EqualTo(before - foremen.ChestCost(1)));
        }

        [Test]
        public void ABulkOpenHandsOverEveryCardItChargedFor()
        {
            SaveData data; WalletService wallet;
            ForemanService foremen = Build(out data, out wallet, gems: 100000L);

            int[] got = foremen.TryOpenChest(C.BulkCount);
            Assert.That(got.Length, Is.EqualTo(C.BulkCount * C.CardsPerChest));
            Assert.That(TotalCards(data), Is.EqualTo(got.Length),
                        "every card handed over is banked — the unlock is free");
        }

        [Test]
        public void EveryCardLandsOnARealMaster()
        {
            SaveData data; WalletService wallet;
            ForemanService foremen = Build(out data, out wallet, gems: 100000L);

            int[] got = foremen.TryOpenChest(C.BulkCount);
            for (int i = 0; i < got.Length; i++)
                Assert.That(got[i], Is.InRange(0, Foremen.Count - 1));
        }

        // ---- the first card stands a master up ---------------------------------------------------

        [Test]
        public void TheFirstCardUnlocksItsMasterAtOneStar()
        {
            SaveData data; WalletService wallet;
            ForemanService foremen = Build(out data, out wallet);

            Assert.That(foremen.IsHired(IslandEconomy.Market), Is.False);
            foremen.GrantDuplicates(IslandEconomy.Market, 1);

            Assert.That(foremen.LevelOf(IslandEconomy.Market), Is.EqualTo(1), "a master, at one star");
            Assert.That(foremen.DuplicatesOf(IslandEconomy.Market), Is.EqualTo(1),
                        "and the card is still banked — the unlock costs nothing");
            Assert.That(foremen.StationMultiplier(IslandEconomy.Market), Is.GreaterThan(1d));
        }

        [Test]
        public void AnUnlockIsNotAStarBought()
        {
            // GoalService counts Levelled to score a "gain stars" goal, and it pays cards itself. If
            // an unlock raised it, a goal would pay for its own completion.
            SaveData data; WalletService wallet;
            ForemanService foremen = Build(out data, out wallet);

            int levelled = 0;
            foremen.Levelled += _ => levelled++;
            foremen.GrantDuplicates(IslandEconomy.Mine, 5);
            Assert.That(foremen.IsHired(IslandEconomy.Mine), Is.True, "the premise: he arrived");
            Assert.That(levelled, Is.Zero);

            Assert.That(foremen.TryLevelUp(IslandEconomy.Mine), Is.True);
            Assert.That(levelled, Is.EqualTo(1), "spending cards on a star is the thing that counts");
        }

        [Test]
        public void AStarCostsCardsAndNoGems()
        {
            SaveData data; WalletService wallet;
            ForemanService foremen = Build(out data, out wallet);
            foremen.GrantDuplicates(IslandEconomy.Storage, 2);
            long gems = wallet.Gems;

            Assert.That(foremen.CanLevel(IslandEconomy.Storage), Is.True);
            Assert.That(foremen.TryLevelUp(IslandEconomy.Storage), Is.True);
            Assert.That(foremen.LevelOf(IslandEconomy.Storage), Is.EqualTo(2));
            Assert.That(wallet.Gems, Is.EqualTo(gems), "the gems were spent at the chest, not here");
            Assert.That(foremen.DuplicatesOf(IslandEconomy.Storage), Is.Zero);
        }

        [Test]
        public void SharedCardStateDistinguishesLockedAndUpgradeReadyMasters()
        {
            SaveData data; WalletService wallet;
            ForemanService foremen = Build(out data, out wallet);

            RosterCardState locked = foremen.CardState(IslandEconomy.Storage);
            Assert.That(locked.CardStatus, Is.EqualTo(RosterCardState.Status.Locked));
            Assert.That(locked.NeedsAttention, Is.False);

            foremen.GrantDuplicates(IslandEconomy.Storage, 2);
            RosterCardState ready = foremen.CardState(IslandEconomy.Storage);
            Assert.That(ready.CardStatus, Is.EqualTo(RosterCardState.Status.Owned));
            Assert.That(ready.Progress, Is.EqualTo(1f));
            Assert.That(ready.CanUpgrade, Is.True);
            Assert.That(foremen.PendingCount(), Is.EqualTo(1));
        }

        // ---- the aimed card ----------------------------------------------------------------------

        [Test]
        public void EveryChestAimsACardAtTheLaggard()
        {
            SaveData data; WalletService wallet;
            ForemanService foremen = Build(out data, out wallet, gems: 100000L);

            // Two owned masters, one clearly further along.
            data.foremanLevels[IslandEconomy.Train] = 6;
            data.foremanLevels[IslandEconomy.Storage] = 1;
            int before = data.foremanDuplicates[IslandEconomy.Storage];

            foremen.TryOpenChest(1);
            Assert.That(data.foremanDuplicates[IslandEconomy.Storage], Is.GreaterThan(before),
                        "at least one card in every chest goes to whoever is furthest behind");
        }

        [Test]
        public void AnEmptyRosterStillTakesItsAimedCard()
        {
            // Nobody is owned, so there is no laggard to aim at. The card must still be dealt rather
            // than dropped on the floor.
            SaveData data; WalletService wallet;
            ForemanService foremen = Build(out data, out wallet);

            int[] got = foremen.TryOpenChest(1);
            Assert.That(got.Length, Is.EqualTo(C.CardsPerChest));
            Assert.That(TotalCards(data), Is.EqualTo(C.CardsPerChest));
            Assert.That(foremen.HiredCount, Is.GreaterThan(0));
        }

        // ---- the free chest ----------------------------------------------------------------------

        [Test]
        public void TheFreeChestIsWaitingOnAFreshSave()
        {
            SaveData data; WalletService wallet;
            ForemanService foremen = Build(out data, out wallet, gems: 0L);

            Assert.That(foremen.FreeChestReady, Is.True);
            int[] got = foremen.TryClaimFreeChest();
            Assert.That(got, Is.Not.Null);
            Assert.That(got.Length, Is.EqualTo(C.FreeCards));
            Assert.That(wallet.Gems, Is.Zero, "free means free");
        }

        [Test]
        public void TheFreeChestCannotBeTakenTwice()
        {
            SaveData data; WalletService wallet;
            ForemanService foremen = Build(out data, out wallet);

            Assert.That(foremen.TryClaimFreeChest(), Is.Not.Null);
            Assert.That(foremen.FreeChestReady, Is.False);
            Assert.That(foremen.TryClaimFreeChest(), Is.Null);
            Assert.That(foremen.FreeChestSecondsLeft, Is.GreaterThan(0L));
            Assert.That(TotalCards(data), Is.EqualTo(C.FreeCards), "and no second helping was banked");
        }

        [Test]
        public void AWeekAwayIsStillOneChest()
        {
            SaveData data; WalletService wallet;
            ForemanService foremen = Build(out data, out wallet);
            foremen.TryClaimFreeChest();

            // Claiming stamps NOW, not when it came due, so an absence cannot accrue chests.
            data.masterFreeChestClaimUnix -= C.FreeIntervalSeconds * 21L;
            Assert.That(foremen.FreeChestReady, Is.True);
            Assert.That(foremen.TryClaimFreeChest(), Is.Not.Null);
            Assert.That(foremen.TryClaimFreeChest(), Is.Null);
            Assert.That(TotalCards(data), Is.EqualTo(C.FreeCards * 2));
        }

        // ---- what the rest of the game reads -----------------------------------------------------

        [Test]
        public void TheLiveSpeedArrayIsRewrittenInPlace()
        {
            // IslandEconomy holds this array for the life of the scene — handing back a new one on
            // every change would leave all eight islands reading a stale roster forever.
            SaveData data; WalletService wallet;
            ForemanService foremen = Build(out data, out wallet);
            float[] handedOver = foremen.StationSpeeds;

            foremen.GrantDuplicates(IslandEconomy.Smelter, 40);
            for (int i = 0; i < 4; i++) foremen.TryLevelUp(IslandEconomy.Smelter);

            Assert.That(foremen.StationSpeeds, Is.SameAs(handedOver));
            Assert.That(handedOver[IslandEconomy.Smelter], Is.GreaterThan(1f),
                        "the array the island is holding must have moved");
        }

        [Test]
        public void AnOldSaveWithNoChestFieldsIsNormalisedRatherThanTrusted()
        {
            var data = new SaveData();
            data.masterFreeChestClaimUnix = -500L;      // a tampered or garbled save
            data.masterChestsOpened = -3;
            data.foremanLevels = new int[2];            // written before the roster was eight long

            var wallet = new WalletService(data.wallet);
            var foremen = new ForemanService(data, wallet, Foremen.Tuning.Default, C);

            Assert.That(data.masterFreeChestClaimUnix, Is.Zero);
            Assert.That(data.masterChestsOpened, Is.Zero);
            Assert.That(data.foremanLevels.Length, Is.EqualTo(Foremen.Count));
            Assert.That(foremen.FreeChestReady, Is.True);
        }

        [Test]
        public void ASaveWithCardsButNoHireGetsItsMasterStoodUp()
        {
            // Goals, contracts, chapters and voyages all paid foreman cards from the first hour, while
            // the first hire cost gems. So a real pre-rework save can carry cards for a master nobody
            // ever hired — and hiring is gone. Left alone those cards are unspendable: the roster shows
            // the slot as empty and the aimed card skips unowned slots.
            var data = new SaveData();
            data.foremanDuplicates[IslandEconomy.Mine] = 12;
            data.foremanDuplicates[IslandEconomy.Market] = 1;
            var wallet = new WalletService(data.wallet);
            var foremen = new ForemanService(data, wallet, Foremen.Tuning.Default, C);

            Assert.That(foremen.LevelOf(IslandEconomy.Mine), Is.EqualTo(1));
            Assert.That(foremen.LevelOf(IslandEconomy.Market), Is.EqualTo(1));
            Assert.That(foremen.DuplicatesOf(IslandEconomy.Mine), Is.EqualTo(12), "the cards stay banked");
            Assert.That(foremen.CanLevel(IslandEconomy.Mine), Is.True, "and are spendable straight away");
            Assert.That(foremen.IncomeMultiplier, Is.GreaterThan(1d), "counted from the first frame");

            // A slot with neither cards nor stars is left alone.
            Assert.That(foremen.LevelOf(IslandEconomy.Train), Is.EqualTo(Foremen.NotHired));
        }
    }
}
