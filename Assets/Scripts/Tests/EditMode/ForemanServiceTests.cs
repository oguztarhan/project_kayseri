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

        // Three masters to name in the tests below, at three different stations so a posting at one
        // never disturbs another.
        private static readonly int MineCommon = Foremen.IndexOf(Foremen.Mine, Foremen.Rarity.Common);
        private static readonly int MineRare = Foremen.IndexOf(Foremen.Mine, Foremen.Rarity.Rare);
        private static readonly int MineLegend = Foremen.IndexOf(Foremen.Mine, Foremen.Rarity.Legendary);
        private static readonly int DepotRare = Foremen.IndexOf(Foremen.Deposit, Foremen.Rarity.Rare);
        private static readonly int MarketCommon = Foremen.IndexOf(Foremen.Market, Foremen.Rarity.Common);

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
            for (int i = 0; i < data.masterCards.Length; i++) n += data.masterCards[i];
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

            Assert.That(foremen.IsHired(MarketCommon), Is.False);
            foremen.GrantDuplicates(MarketCommon, 1);

            Assert.That(foremen.LevelOf(MarketCommon), Is.EqualTo(1), "a master, at one star");
            Assert.That(foremen.DuplicatesOf(MarketCommon), Is.EqualTo(1),
                        "and the card is still banked — the unlock costs nothing");
            Assert.That(foremen.StationMultiplier(Foremen.Market), Is.GreaterThan(1d),
                        "and he took the empty post, so his station is faster from the first card");
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
            foremen.GrantDuplicates(MineCommon, 5);
            Assert.That(foremen.IsHired(MineCommon), Is.True, "the premise: he arrived");
            Assert.That(levelled, Is.Zero);

            Assert.That(foremen.TryLevelUp(MineCommon), Is.True);
            Assert.That(levelled, Is.EqualTo(1), "spending cards on a star is the thing that counts");
        }

        [Test]
        public void AStarCostsCardsAndNoGems()
        {
            SaveData data; WalletService wallet;
            ForemanService foremen = Build(out data, out wallet);
            // Exactly the first star's price, off the curve rather than a literal: a Common, a Rare
            // and a Legendary all charge differently and a hard-coded 2 only ever tested one of them.
            foremen.GrantDuplicates(MineCommon, Foremen.CardsToStar(MineCommon, 1, Foremen.Tuning.Default));
            long gems = wallet.Gems;

            Assert.That(foremen.CanLevel(MineCommon), Is.True);
            Assert.That(foremen.TryLevelUp(MineCommon), Is.True);
            Assert.That(foremen.LevelOf(MineCommon), Is.EqualTo(2));
            Assert.That(wallet.Gems, Is.EqualTo(gems), "the gems were spent at the chest, not here");
            Assert.That(foremen.DuplicatesOf(MineCommon), Is.Zero);
        }

        [Test]
        public void SharedCardStateDistinguishesLockedAndUpgradeReadyMasters()
        {
            SaveData data; WalletService wallet;
            ForemanService foremen = Build(out data, out wallet);

            RosterCardState locked = foremen.CardState(MineLegend);
            Assert.That(locked.CardStatus, Is.EqualTo(RosterCardState.Status.Locked));
            Assert.That(locked.NeedsAttention, Is.False);
            Assert.That(locked.Busy, Is.False, "an unowned card is posted nowhere");

            foremen.GrantDuplicates(MineLegend, Foremen.CardsToStar(MineLegend, 1, Foremen.Tuning.Default));
            RosterCardState ready = foremen.CardState(MineLegend);
            Assert.That(ready.CardStatus, Is.EqualTo(RosterCardState.Status.Owned));
            Assert.That(ready.Progress, Is.EqualTo(1f));
            Assert.That(ready.CanUpgrade, Is.True);
            Assert.That(ready.Tier, Is.EqualTo(RosterCardState.Rarity.Legendary), "rarity is drawn");
            Assert.That(ready.Busy, Is.True, "the only card at his station takes the empty post");
            Assert.That(foremen.PendingCount(), Is.EqualTo(1));
        }

        // ---- the aimed card ----------------------------------------------------------------------

        [Test]
        public void EveryChestAimsACardAtTheLaggard()
        {
            SaveData data; WalletService wallet;
            ForemanService foremen = Build(out data, out wallet, gems: 100000L);

            // Two owned masters, one clearly further along.
            data.masterStars[MineLegend] = Foremen.MaxStars;
            data.masterStars[DepotRare] = 1;
            int before = data.masterCards[DepotRare];

            foremen.TryOpenChest(1);
            Assert.That(data.masterCards[DepotRare], Is.GreaterThan(before),
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

            int master = Foremen.IndexOf(Foremen.Refinery, Foremen.Rarity.Common);
            foremen.GrantDuplicates(master, Foremen.CardsToMax(master, Foremen.Tuning.Default));
            for (int i = 0; i < Foremen.MaxStars; i++) foremen.TryLevelUp(master);

            Assert.That(foremen.StationSpeeds, Is.SameAs(handedOver));
            Assert.That(handedOver[Foremen.EconomyStation[Foremen.Refinery]], Is.GreaterThan(1f),
                        "the array the island is holding must have moved");
        }

        [Test]
        public void AnOldSaveWithNoChestFieldsIsNormalisedRatherThanTrusted()
        {
            var data = new SaveData();
            data.masterFreeChestClaimUnix = -500L;      // a tampered or garbled save
            data.masterChestsOpened = -3;
            data.masterStars = new int[2];              // written before the roster was fifteen long

            var wallet = new WalletService(data.wallet);
            var foremen = new ForemanService(data, wallet, Foremen.Tuning.Default, C);

            Assert.That(data.masterFreeChestClaimUnix, Is.Zero);
            Assert.That(data.masterChestsOpened, Is.Zero);
            Assert.That(data.masterStars.Length, Is.EqualTo(Foremen.Count));
            Assert.That(data.masterActive.Length, Is.EqualTo(Foremen.StationCount));
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
            data.masterCards[MineCommon] = 12;
            data.masterCards[MarketCommon] = 1;
            var wallet = new WalletService(data.wallet);
            var foremen = new ForemanService(data, wallet, Foremen.Tuning.Default, C);

            Assert.That(foremen.LevelOf(MineCommon), Is.EqualTo(1));
            Assert.That(foremen.LevelOf(MarketCommon), Is.EqualTo(1));
            Assert.That(foremen.DuplicatesOf(MineCommon), Is.EqualTo(12), "the cards stay banked");
            Assert.That(foremen.CanLevel(MineCommon), Is.True, "and are spendable straight away");
            Assert.That(foremen.IncomeMultiplier, Is.GreaterThan(1d), "counted from the first frame");

            // A card with neither cards nor stars is left alone.
            Assert.That(foremen.LevelOf(MineLegend), Is.EqualTo(Foremen.NotHired));
        }

        // ---- posting -----------------------------------------------------------------------------

        [Test]
        public void TheFirstCardAtAStationPostsHimWithoutBeingAsked()
        {
            // A roster that works only after the player opens a screen is a feature hidden behind a
            // menu. The first card to arrive takes the empty post.
            SaveData data; WalletService wallet;
            ForemanService foremen = Build(out data, out wallet);

            foremen.GrantDuplicates(MineCommon, 1);
            Assert.That(foremen.ActiveAt(Foremen.Mine), Is.EqualTo(MineCommon));
            Assert.That(foremen.StationStaffed(Foremen.Mine), Is.True);
            Assert.That(foremen.StationMultiplier(Foremen.Mine), Is.GreaterThan(1d));
        }

        [Test]
        public void ABetterCardDoesNotStealAPostYouChose()
        {
            // Silently overriding a choice is worse than a bonus arriving one tap late, and the card
            // that arrived is announced anyway.
            SaveData data; WalletService wallet;
            ForemanService foremen = Build(out data, out wallet);

            foremen.GrantDuplicates(MineCommon, 1);
            foremen.GrantDuplicates(MineLegend, 1);
            Assert.That(foremen.ActiveAt(Foremen.Mine), Is.EqualTo(MineCommon));

            Assert.That(foremen.TrySetActive(MineLegend), Is.True);
            Assert.That(foremen.ActiveAt(Foremen.Mine), Is.EqualTo(MineLegend));
        }

        [Test]
        public void PostingIsRefusedForACardNobodyOwns()
        {
            SaveData data; WalletService wallet;
            ForemanService foremen = Build(out data, out wallet);

            Assert.That(foremen.TrySetActive(MineLegend), Is.False);
            Assert.That(foremen.ActiveAt(Foremen.Mine), Is.EqualTo(-1));
            Assert.That(foremen.IncomeMultiplier, Is.EqualTo(1d).Within(1e-9));
        }

        [Test]
        public void OnlyThePostedMasterSpeedsHisStation()
        {
            SaveData data; WalletService wallet;
            ForemanService foremen = Build(out data, out wallet);

            data.masterStars[MineCommon] = Foremen.MaxStars;
            data.masterStars[MineLegend] = Foremen.MaxStars;
            foremen.GrantDuplicates(MineCommon, 0);      // no-op; the posting below is the subject
            foremen.TrySetActive(MineCommon);

            double withCommon = foremen.StationMultiplier(Foremen.Mine);
            foremen.TrySetActive(MineLegend);
            Assert.That(foremen.StationMultiplier(Foremen.Mine), Is.GreaterThan(withCommon));
        }

        [Test]
        public void TheStationSpeedArrayIsIndexedByTheSavedEconomySlot()
        {
            // IslandEconomy.ForemanSpeed is called with the legacy eight-wide numbering and the roster
            // only covers five of those. Handing it a fifteen-wide array indexed by master would speed
            // the wrong stations and read off the end of the short ones.
            SaveData data; WalletService wallet;
            ForemanService foremen = Build(out data, out wallet);

            data.masterStars[MineLegend] = Foremen.MaxStars;
            foremen.TrySetActive(MineLegend);

            float[] speeds = foremen.StationSpeeds;
            Assert.That(speeds.Length, Is.EqualTo(IslandEconomy.Stations.Length));
            Assert.That(speeds[Foremen.EconomyStation[Foremen.Mine]], Is.GreaterThan(1f));
            Assert.That(speeds[IslandEconomy.Train], Is.EqualTo(1f).Within(1e-6),
                        "a station with no master is never sped up");
        }
    }
}
