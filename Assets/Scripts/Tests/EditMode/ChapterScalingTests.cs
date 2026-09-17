using NUnit.Framework;
using Game.Core;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>
    /// Eight chapters are played on ONE island, each starting its progression over, so each one has to
    /// ask for more than the last. These pin how much more — and, just as importantly, that the first
    /// chapter is left exactly as it shipped.
    /// </summary>
    public class ChapterScalingTests
    {
        private static Chapters.Tuning T => Chapters.Tuning.Default;

        /// <summary>
        /// The cap a single axis can be bought to. It lives on CoalOperation as a serialized field
        /// (axisLevelCap), which a Core test cannot see, so it is restated here — if it moves in the
        /// Inspector, <see cref="TheLastChapterAsksForFewerLevelsThanTheIslandCanHold"/> is the test
        /// that should be re-read.
        /// </summary>
        private const int AxisLevelCap = 50;

        /// <summary>Ghost buildings on an island: <see cref="IslandEconomy.UnlockDeepShaft"/> is the last.</summary>
        private const int BuildingCount = IslandEconomy.UnlockDeepShaft + 1;

        // ------------------------------------------------------------------ chapter 1 is untouched
        [Test]
        public void TheFirstChapterIsBitIdenticalToTheAuthoredTuning()
        {
            Chapters.Tuning first = Chapters.TuningFor(0, T);

            Assert.That(first.FirstSmokeLevels, Is.EqualTo(T.FirstSmokeLevels));
            Assert.That(first.FullSteamLevels, Is.EqualTo(T.FullSteamLevels));
            Assert.That(first.WorksUnlocks, Is.EqualTo(T.WorksUnlocks));
            Assert.That(first.FullSteamUnlocks, Is.EqualTo(T.FullSteamUnlocks));
            Assert.That(first.GemsBase, Is.EqualTo(T.GemsBase));
            Assert.That(first.GemsStep, Is.EqualTo(T.GemsStep));
            Assert.That(first.CardsBase, Is.EqualTo(T.CardsBase));
            Assert.That(first.CardsStep, Is.EqualTo(T.CardsStep));
        }

        /// <summary>The numbers the game shipped with, spelled out so a growth edit cannot move them.</summary>
        [Test]
        public void TheFirstChapterStillAsksForTenLevelsThreeBuildingsAndTwoHundred()
        {
            Chapters.Tuning first = Chapters.TuningFor(0, T);

            Assert.That(first.FirstSmokeLevels, Is.EqualTo(10));
            Assert.That(first.WorksUnlocks, Is.EqualTo(3));
            Assert.That(first.FullSteamLevels, Is.EqualTo(200));
            Assert.That(first.FullSteamUnlocks, Is.EqualTo(8));
        }

        // ------------------------------------------------------------------ the ladder
        [Test]
        public void TheLevelTargetsFollowTheApprovedLadder()
        {
            var firstSmoke = new[] { 10, 12, 14, 16, 19, 23, 27, 32 };
            var fullSteam = new[] { 200, 236, 278, 329, 388, 458, 540, 637 };

            for (int chapter = 0; chapter < Chapters.Count; chapter++)
            {
                Chapters.Tuning scaled = Chapters.TuningFor(chapter, T);
                Assert.That(scaled.FirstSmokeLevels, Is.EqualTo(firstSmoke[chapter]), "FIRST SMOKE, chapter " + chapter);
                Assert.That(scaled.FullSteamLevels, Is.EqualTo(fullSteam[chapter]), "FULL STEAM, chapter " + chapter);
            }
        }

        [Test]
        public void EveryChapterAsksForStrictlyMoreLevelsThanTheOneBeforeIt()
        {
            for (int chapter = 1; chapter < Chapters.Count; chapter++)
            {
                Chapters.Tuning previous = Chapters.TuningFor(chapter - 1, T);
                Chapters.Tuning current = Chapters.TuningFor(chapter, T);
                Assert.That(current.FirstSmokeLevels, Is.GreaterThan(previous.FirstSmokeLevels), "chapter " + chapter);
                Assert.That(current.FullSteamLevels, Is.GreaterThan(previous.FullSteamLevels), "chapter " + chapter);
            }
        }

        /// <summary>
        /// The constraint the growth factor is solved against: the island holds a finite number of
        /// levels, and a chapter that asks for more than that can never be finished.
        /// </summary>
        [Test]
        public void TheLastChapterAsksForFewerLevelsThanTheIslandCanHold()
        {
            int ceiling = 0;
            for (int station = 0; station < IslandEconomy.Axes.Length; station++)
                for (int axis = 0; axis < IslandEconomy.Axes[station].Length; axis++)
                {
                    int cap = IslandEconomy.MaxLevel[station][axis];
                    ceiling += cap > 0 && cap < AxisLevelCap ? cap : AxisLevelCap;
                }

            Assert.That(ceiling, Is.EqualTo(807), "the island's level ceiling moved; re-solve the growth factor");

            int last = Chapters.TuningFor(Chapters.Count - 1, T).FullSteamLevels;
            Assert.That(last, Is.LessThan(ceiling),
                        "chapter " + Chapters.Count + " asks for " + last + " levels of " + ceiling + " that exist");
        }

        /// <summary>
        /// Buildings cannot carry the curve — there are only ten and FULL STEAM already wants eight —
        /// so they must stay where they are on every chapter.
        /// </summary>
        [Test]
        public void TheBuildingTargetsNeverGrowAndStayInsideTheTen()
        {
            for (int chapter = 0; chapter < Chapters.Count; chapter++)
            {
                Chapters.Tuning scaled = Chapters.TuningFor(chapter, T);
                Assert.That(scaled.WorksUnlocks, Is.EqualTo(T.WorksUnlocks), "THE WORKS, chapter " + chapter);
                Assert.That(scaled.FullSteamUnlocks, Is.EqualTo(T.FullSteamUnlocks), "FULL STEAM, chapter " + chapter);
                Assert.That(scaled.FullSteamUnlocks, Is.LessThanOrEqualTo(BuildingCount));
            }
        }

        // ------------------------------------------------------------------ economy
        [Test]
        public void TheEconomyIsFlatOnTheFirstChapterAndCompoundsAfterIt()
        {
            Assert.That(Chapters.EconomyScale(0, T), Is.EqualTo(1d).Within(1e-9));
            Assert.That(Chapters.EconomyScale(1, T), Is.EqualTo(3.2d).Within(1e-9));
            Assert.That(Chapters.EconomyScale(2, T), Is.EqualTo(10.24d).Within(1e-9));

            for (int chapter = 1; chapter < Chapters.Count; chapter++)
                Assert.That(Chapters.EconomyScale(chapter, T),
                            Is.EqualTo(Chapters.EconomyScale(chapter - 1, T) * 3.2d).Within(1e-6),
                            "chapter " + chapter);
        }

        /// <summary>
        /// Cost and value ride the SAME scale. If they ever drift apart, a chapter either pays for
        /// itself instantly or cannot be afforded at all — the two of them moving together is the
        /// only reason a later chapter plays like the first one.
        /// </summary>
        [Test]
        public void CostAndValueShareOneScale()
        {
            for (int chapter = 0; chapter < Chapters.Count; chapter++)
            {
                double scale = Chapters.EconomyScale(chapter, T);
                Assert.That(scale, Is.GreaterThanOrEqualTo(1d));
                Assert.That(scale, Is.EqualTo(System.Math.Pow(T.EconomyStep, chapter)).Within(1e-6));
            }
        }

        // ------------------------------------------------------------------ rewards are not scaled
        [Test]
        public void ScalingTheTargetsDoesNotChangeWhatABeatPays()
        {
            for (int chapter = 0; chapter < Chapters.Count; chapter++)
            {
                Chapters.Tuning scaled = Chapters.TuningFor(chapter, T);
                for (int beat = 0; beat < Chapters.BeatCount; beat++)
                {
                    Assert.That(Chapters.BeatGems(chapter, beat, scaled),
                                Is.EqualTo(Chapters.BeatGems(chapter, beat, T)), "gems " + chapter + "/" + beat);
                    Assert.That(Chapters.BeatCards(chapter, beat, scaled),
                                Is.EqualTo(Chapters.BeatCards(chapter, beat, T)), "cards " + chapter + "/" + beat);
                }
            }
        }

        // ------------------------------------------------------------------ defensive
        /// <summary>
        /// A Tuning built by hand — an old asset, a test, a tool — arrives with these fields at zero.
        /// That must read as "no scaling", never as "every target is zero", which would hand the
        /// player a finished chapter the moment they landed.
        /// </summary>
        [Test]
        public void ATuningWithNoGrowthAuthoredIsFlatRatherThanEmpty()
        {
            Chapters.Tuning bare = T;
            bare.LevelGrowth = 0d;
            bare.EconomyStep = 0d;

            for (int chapter = 0; chapter < Chapters.Count; chapter++)
            {
                Chapters.Tuning scaled = Chapters.TuningFor(chapter, bare);
                Assert.That(scaled.FirstSmokeLevels, Is.EqualTo(T.FirstSmokeLevels), "chapter " + chapter);
                Assert.That(scaled.FullSteamLevels, Is.EqualTo(T.FullSteamLevels), "chapter " + chapter);
                Assert.That(Chapters.EconomyScale(chapter, bare), Is.EqualTo(1d).Within(1e-9));
            }
        }

        [Test]
        public void AGrowthBelowOneNeverMakesALaterChapterEasier()
        {
            Chapters.Tuning shrinking = T;
            shrinking.LevelGrowth = 0.5d;

            for (int chapter = 0; chapter < Chapters.Count; chapter++)
                Assert.That(Chapters.TuningFor(chapter, shrinking).FullSteamLevels,
                            Is.GreaterThanOrEqualTo(T.FullSteamLevels), "chapter " + chapter);
        }

        [Test]
        public void ANegativeChapterIsReadAsTheFirstOne()
        {
            Assert.That(Chapters.TuningFor(-1, T).FullSteamLevels, Is.EqualTo(T.FullSteamLevels));
            Assert.That(Chapters.EconomyScale(-1, T), Is.EqualTo(1d).Within(1e-9));
        }

        // ------------------------------------------------------------------ through the service
        /// <summary>
        /// The end of the wiring: the service must judge each chapter against ITS OWN targets. A save
        /// holding exactly chapter 1's FIRST SMOKE levels finishes that beat in chapter 1 and does not
        /// come close in chapter 2, because chapter 2 asks for more.
        /// </summary>
        [Test]
        public void TheServiceJudgesEachChapterAgainstItsOwnTargets()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var service = new ChapterService(data, wallet, null, T);

            int firstChapterTarget = T.FirstSmokeLevels;
            data.islandLevels.Add(new StationLevel { id = "coal#0#0", level = firstChapterTarget });

            string secondNamespace = Chapters.Namespace(1);
            data.unlockedIslands.Add(secondNamespace);
            data.islandLevels.Add(new StationLevel { id = secondNamespace + "#0#0", level = firstChapterTarget });

            Assert.That(service.Satisfied(0, Chapters.FirstSmoke), Is.True,
                        "chapter 1 wants " + firstChapterTarget + " levels and has them");
            Assert.That(service.Satisfied(1, Chapters.FirstSmoke), Is.False,
                        "chapter 2 wants " + service.TuningFor(1).FirstSmokeLevels + " levels, not " + firstChapterTarget);

            data.islandLevels.Add(new StationLevel
            {
                id = secondNamespace + "#0#1",
                level = service.TuningFor(1).FirstSmokeLevels - firstChapterTarget,
            });
            Assert.That(service.Satisfied(1, Chapters.FirstSmoke), Is.True, "chapter 2 topped up to its own target");
        }

        [Test]
        public void TheServiceHandsOutTheAuthoredTuningForTheFirstChapter()
        {
            var data = new SaveData();
            var service = new ChapterService(data, new WalletService(data.wallet), null, T);

            Assert.That(service.TuningFor(0).FirstSmokeLevels, Is.EqualTo(T.FirstSmokeLevels));
            Assert.That(service.TuningFor(0).FullSteamLevels, Is.EqualTo(T.FullSteamLevels));
            Assert.That(service.Tuning.FullSteamLevels, Is.EqualTo(T.FullSteamLevels));
        }
    }
}
