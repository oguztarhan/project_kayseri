using NUnit.Framework;
using Game.Core;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>
    /// The half of chapters that touches the save. The rules are covered in ChaptersTests; what is
    /// tested here is the reading — the two level-key shapes, the implicit coal, the padding that
    /// lets a beat be appended without a version bump, and the claim paying exactly once.
    /// </summary>
    public class ChapterServiceTests
    {
        private static Chapters.Tuning T => Chapters.Tuning.Default;

        private static ChapterService Make(SaveData data, out WalletService wallet)
        {
            wallet = new WalletService(data.wallet);
            return new ChapterService(data, wallet, null, T);
        }

        private static void Level(SaveData d, string id, int level)
            => d.islandLevels.Add(new StationLevel { id = id, level = level });

        /// <summary>Buys enough axis levels on an island to clear a threshold, in one row per axis.</summary>
        private static void Levels(SaveData d, string island, int total)
        {
            Level(d, island + "#0#0", total);
        }

        private static void Unlocks(SaveData d, string island, int count)
        {
            for (int u = 0; u < count; u++) Level(d, island + "u#" + u, 1);
        }

        private static void Yard(SaveData d, string island, int carry, int serve, int collect)
            => d.idleMarketYards.Add(new IdleMarketYard
            { schemaVersion = IdleMarketMigration.SchemaVersion, id = island,
              hireCarry = carry, hireServe = serve, dispatchLevel = collect });

        // ---- ownership ---------------------------------------------------------------------------

        [Test]
        public void CoalIsOwnedWithoutBeingInTheList()
        {
            // WorldIslands.IsOwned returns true for index 0 without consulting unlockedIslands,
            // because the game starts you on coal and nothing ever adds it.
            var d = new SaveData();
            ChapterService s = Make(d, out _);
            Assert.That(s.Owned(0), Is.True);
            Assert.That(d.unlockedIslands, Is.Empty);
        }

        [Test]
        public void OtherIslandsNeedBuying()
        {
            var d = new SaveData();
            ChapterService s = Make(d, out _);
            for (int c = 1; c < Chapters.Count; c++)
                Assert.That(s.Owned(c), Is.False, Chapters.Island(c));

            d.unlockedIslands.Add("iron");
            Assert.That(s.Owned(Chapters.Of("iron")), Is.True);
            Assert.That(s.Owned(Chapters.Of("copper")), Is.False);
        }

        [Test]
        public void Current_IsTheFurthestIslandOwned()
        {
            var d = new SaveData();
            ChapterService s = Make(d, out _);
            Assert.That(s.Current, Is.Zero);

            // Bought out of order on purpose: Current is the furthest owned, not the furthest
            // finished, because the screen should open where the player actually is.
            d.unlockedIslands.Add("gold");
            d.unlockedIslands.Add("copper");
            Assert.That(s.Current, Is.EqualTo(Chapters.Of("gold")));
        }

        // ---- reading the level list --------------------------------------------------------------

        [Test]
        public void AxisLevelsAndUnlocksAreCountedApart()
        {
            // "coal#4#1" is an axis and "coalu#8" is a ghost building — CoalOperation.cs:1101/:1129.
            var d = new SaveData();
            Level(d, "coal#0#0", 6);
            Level(d, "coal#4#1", 4);
            Unlocks(d, "coal", 3);

            ChapterService s = Make(d, out _);
            Chapters.Progress p = s.Progress(0);
            Assert.That(p.AxisLevels, Is.EqualTo(10));
            Assert.That(p.Unlocks, Is.EqualTo(3));
        }

        [Test]
        public void OneIslandsLevelsNeverCountForAnother()
        {
            var d = new SaveData();
            d.unlockedIslands.Add("copper");
            Levels(d, "coal", 50);
            Unlocks(d, "coal", 9);

            ChapterService s = Make(d, out _);
            Chapters.Progress copper = s.Progress(Chapters.Of("copper"));
            Assert.That(copper.AxisLevels, Is.Zero);
            Assert.That(copper.Unlocks, Is.Zero);
        }

        [Test]
        public void ZeroAndNegativeLevelRowsAreIgnored()
        {
            var d = new SaveData();
            Level(d, "coal#0#0", 0);
            Level(d, "coal#1#1", -3);
            Level(d, "coalu#0", 0);
            d.islandLevels.Add(null);
            d.islandLevels.Add(new StationLevel { id = null, level = 5 });

            ChapterService s = Make(d, out _);
            Chapters.Progress p = s.Progress(0);
            Assert.That(p.AxisLevels, Is.Zero);
            Assert.That(p.Unlocks, Is.Zero);
        }

        // ---- the yard ----------------------------------------------------------------------------

        [Test]
        public void TheYardBeatFollowsMarketFlowsOwnMaxedFlag()
        {
            var d = new SaveData();
            Yard(d, "coal", MarketFlow.MaxHireLevel, MarketFlow.MaxHireLevel, MarketFlow.MaxHireLevel - 1);
            ChapterService s = Make(d, out _);
            Assert.That(s.Progress(0).YardStaffed, Is.False, "one job short is not a staffed yard");

            d.idleMarketYards[0].dispatchLevel = MarketFlow.MaxHireLevel;
            Assert.That(s.Progress(0).YardStaffed, Is.True);
        }

        [Test]
        public void NoYardRowIsNotAStaffedYard()
        {
            var d = new SaveData();
            ChapterService s = Make(d, out _);
            Assert.That(s.Progress(0).YardStaffed, Is.False);
        }

        // ---- claiming ----------------------------------------------------------------------------

        [Test]
        public void ClaimPaysGemsOnceAndOnlyOnce()
        {
            var d = new SaveData();
            ChapterService s = Make(d, out WalletService wallet);

            long expected = Chapters.BeatGems(0, Chapters.Landfall, T);
            Assert.That(s.CanClaim(0, Chapters.Landfall), Is.True);
            Assert.That(s.Claim(0, Chapters.Landfall), Is.True);
            Assert.That(wallet.Gems, Is.EqualTo(expected));

            Assert.That(s.Claim(0, Chapters.Landfall), Is.False, "a beat must not pay twice");
            Assert.That(wallet.Gems, Is.EqualTo(expected));
        }

        [Test]
        public void AnUnearnedBeatCannotBeClaimed()
        {
            var d = new SaveData();
            ChapterService s = Make(d, out WalletService wallet);
            Assert.That(s.Claim(0, Chapters.FullSteam), Is.False);
            Assert.That(wallet.Gems, Is.Zero);
        }

        [Test]
        public void ClaimChapterTakesEverythingOwedInOneGo()
        {
            var d = new SaveData();
            Levels(d, "coal", T.FullSteamLevels);
            Unlocks(d, "coal", T.FullSteamUnlocks);
            Yard(d, "coal", MarketFlow.MaxHireLevel, MarketFlow.MaxHireLevel, MarketFlow.MaxHireLevel);

            ChapterService s = Make(d, out WalletService wallet);
            Assert.That(s.Complete(0), Is.True);
            Assert.That(s.PendingCount(), Is.EqualTo(Chapters.BeatCount));

            long expected = 0L;
            for (int b = 0; b < Chapters.BeatCount; b++) expected += Chapters.BeatGems(0, b, T);

            Assert.That(s.ClaimChapter(0), Is.EqualTo(Chapters.BeatCount));
            Assert.That(wallet.Gems, Is.EqualTo(expected));
            Assert.That(s.PendingCount(), Is.Zero);
            Assert.That(s.ClaimChapter(0), Is.Zero);
        }

        [Test]
        public void PendingCountIgnoresIslandsNobodyOwns()
        {
            var d = new SaveData();
            // A level row for an island that was never bought must not light a badge.
            Levels(d, "diamond", 9999);
            Unlocks(d, "diamond", 10);

            ChapterService s = Make(d, out _);
            Assert.That(s.PendingCount(), Is.EqualTo(1), "only coal's Landfall should be waiting");
        }

        // ---- the save contract -------------------------------------------------------------------

        [Test]
        public void ASaveFromBeforeChaptersExistedWorks()
        {
            // The whole point of adding fields instead of bumping the version: an old save arrives
            // with nothing, and because beats are observed rather than reported, an island the player
            // already built lights up everything they had earned the first time they look.
            var d = new SaveData();
            d.chapters = null;
            Levels(d, "coal", T.FullSteamLevels);
            Unlocks(d, "coal", T.FullSteamUnlocks);
            Yard(d, "coal", MarketFlow.MaxHireLevel, MarketFlow.MaxHireLevel, MarketFlow.MaxHireLevel);

            ChapterService s = Make(d, out _);
            Assert.That(d.chapters, Is.Not.Null);
            Assert.That(s.PendingCount(), Is.EqualTo(Chapters.BeatCount));
        }

        [Test]
        public void AShortBeatArrayIsPaddedAndKeepsItsClaims()
        {
            // This is what makes appending a sixth beat free. A row written when there were three
            // beats must come back with three claims intact and the rest clear.
            var d = new SaveData();
            d.chapters.Add(new ChapterState { id = "coal", claimed = new[] { true, true, true } });

            ChapterService s = Make(d, out _);
            Assert.That(d.chapters[0].claimed.Length, Is.EqualTo(Chapters.BeatCount));
            Assert.That(s.Claimed(0, 0), Is.True);
            Assert.That(s.Claimed(0, 1), Is.True);
            Assert.That(s.Claimed(0, 2), Is.True);
            for (int b = 3; b < Chapters.BeatCount; b++)
                Assert.That(s.Claimed(0, b), Is.False, "beat " + b);
        }

        [Test]
        public void RowsAreMadeOnDemandNotAllAtOnce()
        {
            var d = new SaveData();
            ChapterService s = Make(d, out _);
            Assert.That(d.chapters, Is.Empty, "an untouched save must stay small");

            s.Claim(0, Chapters.Landfall);
            Assert.That(d.chapters.Count, Is.EqualTo(1));
            Assert.That(d.chapters[0].id, Is.EqualTo("coal"));
        }

        [Test]
        public void IntroIsSeenOnce()
        {
            var d = new SaveData();
            ChapterService s = Make(d, out _);
            Assert.That(s.IntroSeen(0), Is.False);
            s.MarkIntroSeen(0);
            Assert.That(s.IntroSeen(0), Is.True);
            Assert.That(s.IntroSeen(1), Is.False);
        }

        [Test]
        public void OffLadderChaptersAreInert()
        {
            var d = new SaveData();
            ChapterService s = Make(d, out WalletService wallet);
            Assert.That(s.Owned(-1), Is.False);
            Assert.That(s.Owned(Chapters.Count), Is.False);
            Assert.That(s.Claim(-1, 0), Is.False);
            Assert.That(s.Claim(Chapters.Count, 0), Is.False);
            Assert.That(s.ClaimChapter(Chapters.Count), Is.Zero);
            Assert.That(wallet.Gems, Is.Zero);
            Assert.That(d.chapters, Is.Empty);
        }

        [Test]
        public void ANullSaveIsSurvivable()
        {
            var s = new ChapterService(null, null, null, T);
            Assert.That(s.Owned(0), Is.False);
            Assert.That(s.PendingCount(), Is.Zero);
            Assert.That(s.Claim(0, 0), Is.False);
            Assert.That(s.Current, Is.Zero);
            Assert.That(s.IntroSeen(0), Is.False);
        }
    }
}
