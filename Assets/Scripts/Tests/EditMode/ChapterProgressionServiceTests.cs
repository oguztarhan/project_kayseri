using NUnit.Framework;
using Game.Core;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>
    /// Moving from one chapter to the next on the same island: what opens, what starts over, and —
    /// mostly — what must survive it.
    /// </summary>
    public class ChapterProgressionServiceTests
    {
        private static Chapters.Tuning T => Chapters.Tuning.Default;

        private SaveData _data;
        private WalletService _wallet;
        private ChapterService _chapters;
        private ChapterProgressionService _progression;
        private int _saves;

        [SetUp]
        public void SetUp()
        {
            _saves = 0;
            _data = new SaveData();
            _wallet = new WalletService(_data.wallet);
            _chapters = new ChapterService(_data, _wallet, null, T);
            _progression = new ChapterProgressionService(_data, _chapters, () => _saves++);
        }

        // ------------------------------------------------------------------ helpers
        /// <summary>Builds a chapter out to all five beats, in its own namespace.</summary>
        private void Finish(int chapter)
        {
            string ns = Chapters.Namespace(chapter);
            Chapters.Tuning t = Chapters.TuningFor(chapter, T);

            if (chapter > 0 && !_data.unlockedIslands.Contains(ns)) _data.unlockedIslands.Add(ns);

            _data.islandLevels.Add(new StationLevel { id = ns + "#0#0", level = t.FullSteamLevels });
            for (int u = 0; u < t.FullSteamUnlocks; u++)
                _data.islandLevels.Add(new StationLevel { id = ns + "u#" + u, level = 1 });

            _data.idleMarketYards.Add(new IdleMarketYard
            {
                schemaVersion = IdleMarketMigration.SchemaVersion,
                id = ns,
                hireCarry = MarketFlow.MaxHireLevel,
                hireServe = MarketFlow.MaxHireLevel,
                dispatchLevel = MarketFlow.MaxHireLevel,
            });
        }

        private int LevelRowsFor(int chapter)
        {
            string prefix = Chapters.Namespace(chapter);
            int n = 0;
            for (int i = 0; i < _data.islandLevels.Count; i++)
                if (_data.islandLevels[i].id.StartsWith(prefix, System.StringComparison.Ordinal)) n++;
            return n;
        }

        // ------------------------------------------------------------------ the gate
        [Test]
        public void AFreshSaveIsOnTheFirstChapterAndCannotAdvance()
        {
            Assert.That(_progression.Current, Is.Zero);
            Assert.That(_progression.CurrentNamespace, Is.EqualTo("coal"));
            Assert.That(_progression.CanAdvance, Is.False);
            Assert.That(_progression.TryAdvance(), Is.False);
            Assert.That(_data.unlockedIslands, Is.Empty);
        }

        [Test]
        public void AHalfFinishedChapterCannotAdvance()
        {
            _data.islandLevels.Add(new StationLevel { id = "coal#0#0", level = T.FullSteamLevels });

            Assert.That(_chapters.Satisfied(0, Chapters.FirstSmoke), Is.True, "levels are in");
            Assert.That(_progression.CanAdvance, Is.False, "the yard is not staffed and the buildings are not up");
            Assert.That(_progression.TryAdvance(), Is.False);
        }

        [Test]
        public void AFinishedChapterOpensTheNextOne()
        {
            Finish(0);
            Assert.That(_progression.CanAdvance, Is.True);

            Assert.That(_progression.TryAdvance(), Is.True);

            Assert.That(_progression.Current, Is.EqualTo(1));
            Assert.That(_progression.CurrentNamespace, Is.EqualTo(Chapters.Namespace(1)));
            Assert.That(_data.unlockedIslands, Does.Contain(Chapters.Namespace(1)));
            Assert.That(_saves, Is.EqualTo(1), "the save is written as part of the advance");
        }

        [Test]
        public void TheLastChapterHasNowhereToGo()
        {
            for (int chapter = 0; chapter < Chapters.Count; chapter++) Finish(chapter);

            Assert.That(_progression.Current, Is.EqualTo(Chapters.Count - 1));
            Assert.That(_progression.IsFinalChapter, Is.True);
            Assert.That(_progression.CanAdvance, Is.False);
            Assert.That(_progression.TryAdvance(), Is.False);
        }

        // ------------------------------------------------------------------ the reset
        /// <summary>
        /// The heart of it: the new chapter is empty, and the old one is untouched on disk. Nothing is
        /// deleted — the island simply reads a prefix that has no rows under it yet.
        /// </summary>
        [Test]
        public void TheNewChapterStartsEmptyWhileTheOldOneKeepsEveryRow()
        {
            Finish(0);
            int rowsBefore = LevelRowsFor(0);

            _progression.TryAdvance();

            Chapters.Progress opened = _chapters.Progress(1);
            Assert.That(opened.Owned, Is.True);
            Assert.That(opened.AxisLevels, Is.Zero, "a new chapter starts with nothing bought");
            Assert.That(opened.Unlocks, Is.Zero, "and nothing built");
            Assert.That(opened.YardStaffed, Is.False, "and a yard to staff again");

            Chapters.Progress previous = _chapters.Progress(0);
            Assert.That(previous.AxisLevels, Is.EqualTo(T.FullSteamLevels), "chapter 1's levels are still there");
            Assert.That(previous.Unlocks, Is.EqualTo(T.FullSteamUnlocks));
            Assert.That(previous.YardStaffed, Is.True);
            Assert.That(LevelRowsFor(0), Is.EqualTo(rowsBefore), "no row was removed");
        }

        /// <summary>
        /// A finished chapter goes on earning while the next one is being built — the decision that
        /// stops income falling to nothing at the exact moment the targets jump.
        /// </summary>
        [Test]
        public void TheFinishedChaptersYardAndRateSurviveTheAdvance()
        {
            Finish(0);
            _data.islandRates.Add(new IslandRate { id = "coal", perMin = 1234d });

            _progression.TryAdvance();

            IdleMarketYard yard = _data.idleMarketYards.Find(y => y.id == "coal");
            Assert.That(yard, Is.Not.Null, "the finished chapter keeps its yard");
            Assert.That(yard.hireCarry, Is.EqualTo(MarketFlow.MaxHireLevel), "with its crew still hired");

            IslandRate rate = _data.islandRates.Find(r => r.id == "coal");
            Assert.That(rate, Is.Not.Null, "and the rate it earns while you are away");
            Assert.That(rate.perMin, Is.EqualTo(1234d));
        }

        /// <summary>
        /// The 48-hour starter window belongs to chapter 1 and is not re-opened per chapter. It is
        /// keyed by the world ladder's island, which never changes, so an advance must leave the
        /// windows list exactly as it found it.
        /// </summary>
        [Test]
        public void AdvancingDoesNotOpenASecondStarterOfferWindow()
        {
            Finish(0);
            int before = _data.starterOfferWindows.Count;

            _progression.TryAdvance();

            Assert.That(_data.starterOfferWindows.Count, Is.EqualTo(before));
            Assert.That(_data.pendingStarterIsland, Is.Empty);
        }

        // ------------------------------------------------------------------ rewards
        [Test]
        public void AdvancingSweepsUpTheBeatsThePlayerNeverCollected()
        {
            Finish(0);
            long before = _wallet.Gems;
            Assert.That(_chapters.PendingCount(), Is.EqualTo(Chapters.BeatCount), "all five are owed");

            _progression.TryAdvance();

            long expected = 0L;
            for (int beat = 0; beat < Chapters.BeatCount; beat++) expected += Chapters.BeatGems(0, beat, T);
            Assert.That(_wallet.Gems - before, Is.EqualTo(expected), "every uncollected beat paid out");
            for (int beat = 0; beat < Chapters.BeatCount; beat++)
                Assert.That(_chapters.Claimed(0, beat), Is.True, "beat " + beat);
        }

        [Test]
        public void ABeatAlreadyCollectedIsNotPaidAgainBySweeping()
        {
            Finish(0);
            _chapters.Claim(0, Chapters.Landfall);
            long afterFirstClaim = _wallet.Gems;

            _progression.TryAdvance();

            long expected = 0L;
            for (int beat = 1; beat < Chapters.BeatCount; beat++) expected += Chapters.BeatGems(0, beat, T);
            Assert.That(_wallet.Gems - afterFirstClaim, Is.EqualTo(expected),
                        "LANDFALL was already paid and must not pay twice");
        }

        [Test]
        public void NothingOnAFinishedChapterCanBeClaimedAgainAfterTheAdvance()
        {
            Finish(0);
            _progression.TryAdvance();
            long after = _wallet.Gems;

            for (int beat = 0; beat < Chapters.BeatCount; beat++)
                Assert.That(_chapters.CanClaim(0, beat), Is.False, "beat " + beat);
            Assert.That(_chapters.ClaimChapter(0), Is.Zero);
            Assert.That(_wallet.Gems, Is.EqualTo(after));
        }

        // ------------------------------------------------------------------ idempotence
        /// <summary>
        /// The double tap. Once the next chapter is open it is the current one, and a chapter that has
        /// just begun is not finished — so the second call has nothing to do and says so.
        /// </summary>
        [Test]
        public void AdvancingTwiceInARowOpensOneChapterAndPaysOnce()
        {
            Finish(0);
            long before = _wallet.Gems;

            Assert.That(_progression.TryAdvance(), Is.True);
            long afterFirst = _wallet.Gems;

            Assert.That(_progression.TryAdvance(), Is.False, "the new chapter is not finished");
            Assert.That(_progression.Current, Is.EqualTo(1));
            Assert.That(_data.unlockedIslands.Count, Is.EqualTo(1));
            Assert.That(_wallet.Gems, Is.EqualTo(afterFirst));
            Assert.That(_wallet.Gems, Is.GreaterThan(before));
            Assert.That(_saves, Is.EqualTo(1), "a refused advance writes nothing");
        }

        [Test]
        public void AnAdvanceThatIsRefusedChangesNothingAtAll()
        {
            string json = UnityEngine.JsonUtility.ToJson(_data);

            Assert.That(_progression.TryAdvance(), Is.False);

            Assert.That(UnityEngine.JsonUtility.ToJson(_data), Is.EqualTo(json));
            Assert.That(_saves, Is.Zero);
        }

        // ------------------------------------------------------------------ the event
        [Test]
        public void TheAdvancedEventFiresOnceWithTheChapterThatOpened()
        {
            Finish(0);
            int fired = 0;
            int reported = -1;
            _progression.Advanced += chapter => { fired++; reported = chapter; };

            _progression.TryAdvance();

            Assert.That(fired, Is.EqualTo(1));
            Assert.That(reported, Is.EqualTo(1));
        }

        /// <summary>
        /// Listeners reload the world, so the save must already be on disk when they hear about it —
        /// otherwise a reload reads a save that does not know the chapter moved.
        /// </summary>
        [Test]
        public void TheSaveIsWrittenBeforeListenersAreTold()
        {
            Finish(0);
            int savesWhenTold = -1;
            _progression.Advanced += _ => savesWhenTold = _saves;

            _progression.TryAdvance();

            Assert.That(savesWhenTold, Is.EqualTo(1));
        }

        // ------------------------------------------------------------------ walking the ladder
        /// <summary>
        /// All eight, end to end: each one opens, starts empty, is finished on ITS OWN targets, and
        /// hands over to the next.
        /// </summary>
        [Test]
        public void EveryChapterInTurnOpensEmptyAndIsFinishedOnItsOwnTargets()
        {
            for (int chapter = 0; chapter < Chapters.Count; chapter++)
            {
                Assert.That(_progression.Current, Is.EqualTo(chapter), "standing on chapter " + chapter);
                Assert.That(_chapters.Progress(chapter).AxisLevels, Is.Zero, "chapter " + chapter + " starts empty");

                Finish(chapter);

                Assert.That(_chapters.Progress(chapter).AxisLevels,
                            Is.EqualTo(Chapters.TuningFor(chapter, T).FullSteamLevels),
                            "chapter " + chapter + " is finished on its own target");
                Assert.That(_progression.CanAdvance, Is.EqualTo(chapter < Chapters.Count - 1),
                            "chapter " + chapter);
                _progression.TryAdvance();
            }

            Assert.That(_progression.Current, Is.EqualTo(Chapters.Count - 1));
            Assert.That(_data.unlockedIslands.Count, Is.EqualTo(Chapters.Count - 1));
        }
    }
}
