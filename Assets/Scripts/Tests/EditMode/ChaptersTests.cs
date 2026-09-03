using NUnit.Framework;
using Game.Core;

namespace Game.Tests
{
    /// <summary>
    /// The chapter rules. These are design instruments as much as regression guards: the two tests
    /// under "the ladder" are what fail first if the island order is ever re-cut, and the deadlock
    /// test is what fails if somebody makes a beat depend on something a player can be locked out of.
    /// </summary>
    public class ChaptersTests
    {
        private static Chapters.Tuning T => Chapters.Tuning.Default;

        private static Chapters.Progress Fresh() => new Chapters.Progress { Owned = true };

        private static Chapters.Progress Built(int levels, int unlocks, bool yard = false)
            => new Chapters.Progress { Owned = true, AxisLevels = levels, Unlocks = unlocks, YardStaffed = yard };

        // ---- the ladder --------------------------------------------------------------------------

        [Test]
        public void OneChapterPerIsland()
        {
            Assert.That(Chapters.Islands.Length, Is.EqualTo(Chapters.Count));
        }

        [Test]
        public void IslandOrderMatchesTheLadder()
        {
            // Mirrors Game.Gameplay.WorldIslands.DefaultLadder(). Game.Core cannot reference
            // Game.Gameplay, so this is the only thing holding the two in step — if the ladder is
            // re-cut, fix Chapters.Islands here rather than deleting the test.
            Assert.That(Chapters.Islands, Is.EqualTo(new[]
            { "coal", "copper", "iron", "silver", "gold", "ruby", "emerald", "diamond" }));
        }

        [Test]
        public void IslandKeysAreUniqueAndRoundTrip()
        {
            for (int c = 0; c < Chapters.Count; c++)
                Assert.That(Chapters.Of(Chapters.Island(c)), Is.EqualTo(c), "chapter " + c);

            Assert.That(Chapters.Of("nowhere"), Is.EqualTo(-1));
            Assert.That(Chapters.Of(null), Is.EqualTo(-1));
            Assert.That(Chapters.Of(""), Is.EqualTo(-1));
            Assert.That(Chapters.Island(-1), Is.Empty);
            Assert.That(Chapters.Island(Chapters.Count), Is.Empty);
        }

        // ---- an unowned island earns nothing -----------------------------------------------------

        [Test]
        public void UnownedIsland_SatisfiesNothing()
        {
            // Levels and unlocks cannot exist on an island nobody owns, but the save is a file and a
            // file can say anything. Every beat gates on Owned so a corrupt row cannot pay out.
            var p = new Chapters.Progress { Owned = false, AxisLevels = 9999, Unlocks = 10, YardStaffed = true };
            for (int b = 0; b < Chapters.BeatCount; b++)
                Assert.That(Chapters.Satisfied(b, p, T), Is.False, "beat " + b);

            Assert.That(Chapters.BeatsSatisfied(p, T), Is.Zero);
            Assert.That(Chapters.Complete(p, T), Is.False);
        }

        [Test]
        public void UnknownBeatIndex_ReadsAsUnearned()
        {
            // A save from a later build can carry beats this one has never heard of.
            var p = Built(9999, 10, true);
            Assert.That(Chapters.Satisfied(Chapters.BeatCount, p, T), Is.False);
            Assert.That(Chapters.Satisfied(-1, p, T), Is.False);
            Assert.That(Chapters.BeatProgress(Chapters.BeatCount, p, T), Is.Zero);
        }

        // ---- the beats ---------------------------------------------------------------------------

        [Test]
        public void Landfall_FiresOnOwnershipAlone()
        {
            Assert.That(Chapters.Satisfied(Chapters.Landfall, Fresh(), T), Is.True);
            Assert.That(Chapters.BeatsSatisfied(Fresh(), T), Is.EqualTo(1),
                        "buying an island must light exactly one beat, not a chapter");
        }

        [Test]
        public void FirstSmoke_NeedsItsLevels()
        {
            Assert.That(Chapters.Satisfied(Chapters.FirstSmoke, Built(T.FirstSmokeLevels - 1, 0), T), Is.False);
            Assert.That(Chapters.Satisfied(Chapters.FirstSmoke, Built(T.FirstSmokeLevels, 0), T), Is.True);
        }

        [Test]
        public void TheWorks_NeedsItsBuildings()
        {
            Assert.That(Chapters.Satisfied(Chapters.TheWorks, Built(999, T.WorksUnlocks - 1), T), Is.False);
            Assert.That(Chapters.Satisfied(Chapters.TheWorks, Built(0, T.WorksUnlocks), T), Is.True);
        }

        [Test]
        public void TheYard_IsTheStaffingFlagAndNothingElse()
        {
            Assert.That(Chapters.Satisfied(Chapters.TheYard, Built(9999, 10, false), T), Is.False);
            Assert.That(Chapters.Satisfied(Chapters.TheYard, Built(0, 0, true), T), Is.True);
        }

        [Test]
        public void FullSteam_NeedsBothHalves()
        {
            Assert.That(Chapters.Satisfied(Chapters.FullSteam,
                        Built(T.FullSteamLevels, T.FullSteamUnlocks - 1), T), Is.False,
                        "levels alone must not close the chapter");
            Assert.That(Chapters.Satisfied(Chapters.FullSteam,
                        Built(T.FullSteamLevels - 1, T.FullSteamUnlocks), T), Is.False,
                        "buildings alone must not close the chapter");
            Assert.That(Chapters.Satisfied(Chapters.FullSteam,
                        Built(T.FullSteamLevels, T.FullSteamUnlocks), T), Is.True);
        }

        [Test]
        public void EveryBeatIsReachable_NoChapterCanDeadlock()
        {
            // The load-bearing one. Docs/VOYAGES.md §16 dropped charts-gate-islands because a player
            // can be stalled behind a system they have not engaged with; the same trap here would be a
            // beat nobody can satisfy. A fully built island must close every chapter.
            var maxed = Built(T.FullSteamLevels, 10, true);
            for (int c = 0; c < Chapters.Count; c++)
                Assert.That(Chapters.Complete(maxed, T), Is.True, "chapter " + c + " cannot be completed");
            Assert.That(Chapters.BeatsSatisfied(maxed, T), Is.EqualTo(Chapters.BeatCount));
        }

        // ---- progress bars -----------------------------------------------------------------------

        [Test]
        public void BeatProgress_IsAlwaysInRange()
        {
            for (int levels = 0; levels <= T.FullSteamLevels + 50; levels += 7)
                for (int unlocks = 0; unlocks <= 10; unlocks++)
                {
                    var p = Built(levels, unlocks, unlocks > 5);
                    for (int b = 0; b < Chapters.BeatCount; b++)
                        Assert.That(Chapters.BeatProgress(b, p, T), Is.InRange(0f, 1f),
                                    "beat " + b + " at " + levels + "/" + unlocks);
                }
        }

        [Test]
        public void BeatProgress_NeverReadsFullBeforeItPays()
        {
            // A bar that sits at 100% and refuses to pay is the one thing a progress bar must not do.
            // FullSteam asks two things at once, so it reports the worse of them.
            for (int levels = 0; levels <= T.FullSteamLevels + 20; levels += 3)
                for (int unlocks = 0; unlocks <= 10; unlocks++)
                {
                    var p = Built(levels, unlocks, unlocks > 5);
                    for (int b = 0; b < Chapters.BeatCount; b++)
                    {
                        bool full = Chapters.BeatProgress(b, p, T) >= 1f;
                        Assert.That(full, Is.EqualTo(Chapters.Satisfied(b, p, T)),
                                    "beat " + b + " at " + levels + "/" + unlocks);
                    }
                }
        }

        [Test]
        public void UnownedIsland_ShowsNoProgress()
        {
            var p = new Chapters.Progress { Owned = false, AxisLevels = 9999, Unlocks = 10, YardStaffed = true };
            for (int b = 0; b < Chapters.BeatCount; b++)
                Assert.That(Chapters.BeatProgress(b, p, T), Is.Zero, "beat " + b);
        }

        // ---- rewards -----------------------------------------------------------------------------

        [Test]
        public void RewardsRiseWithTheChapter()
        {
            for (int c = 1; c < Chapters.Count; c++)
                Assert.That(Chapters.BeatGems(c, Chapters.FirstSmoke, T),
                            Is.GreaterThan(Chapters.BeatGems(c - 1, Chapters.FirstSmoke, T)),
                            "chapter " + c);
        }

        [Test]
        public void FullSteamIsWorthMoreThanTheBeatsBeforeIt()
        {
            // It is much the longest of the five; paying it the same as landing on the beach would
            // make the last stretch of an island the least rewarded part of it.
            for (int c = 0; c < Chapters.Count; c++)
                Assert.That(Chapters.BeatGems(c, Chapters.FullSteam, T),
                            Is.GreaterThan(Chapters.BeatGems(c, Chapters.TheYard, T)), "chapter " + c);
        }

        [Test]
        public void Landfall_PaysNoCards()
        {
            for (int c = 0; c < Chapters.Count; c++)
                Assert.That(Chapters.BeatCards(c, Chapters.Landfall, T), Is.Zero, "chapter " + c);
        }

        [Test]
        public void RewardsAreNeverNegativeAndOffLadderPaysNothing()
        {
            for (int c = 0; c < Chapters.Count; c++)
                for (int b = 0; b < Chapters.BeatCount; b++)
                {
                    Assert.That(Chapters.BeatGems(c, b, T), Is.GreaterThanOrEqualTo(0L));
                    Assert.That(Chapters.BeatCards(c, b, T), Is.GreaterThanOrEqualTo(0));
                }

            Assert.That(Chapters.BeatGems(-1, 0, T), Is.Zero);
            Assert.That(Chapters.BeatGems(Chapters.Count, 0, T), Is.Zero);
            Assert.That(Chapters.BeatGems(0, Chapters.BeatCount, T), Is.Zero);
            Assert.That(Chapters.BeatCards(-1, 1, T), Is.Zero);
            Assert.That(Chapters.BeatCards(Chapters.Count, 1, T), Is.Zero);
            Assert.That(Chapters.BeatCards(0, Chapters.BeatCount, T), Is.Zero);
        }

        // ---- the objective banner's two questions ------------------------------------------------

        [Test]
        public void NextBeat_IsTheLowestUnsatisfiedOne()
        {
            Assert.That(Chapters.NextBeat(Fresh(), T), Is.EqualTo(Chapters.FirstSmoke));
            Assert.That(Chapters.NextBeat(Built(T.FirstSmokeLevels, 0), T), Is.EqualTo(Chapters.TheWorks));
            Assert.That(Chapters.NextBeat(Built(T.FirstSmokeLevels, T.WorksUnlocks), T),
                        Is.EqualTo(Chapters.TheYard));
        }

        /// <summary>
        /// A player can staff the yard before raising three buildings, so the beats do not fall in
        /// order. The banner must still send them back for the one they skipped rather than naming a
        /// target they have already met.
        /// </summary>
        [Test]
        public void NextBeat_DoesNotSkipAnEarlierBeatBecauseALaterOneLanded()
        {
            Chapters.Progress p = Built(T.FirstSmokeLevels, 0, yard: true);
            Assert.That(Chapters.Satisfied(Chapters.TheYard, p, T), Is.True, "the yard is staffed");
            Assert.That(Chapters.NextBeat(p, T), Is.EqualTo(Chapters.TheWorks));
        }

        [Test]
        public void NextBeat_IsMinusOneOnAFinishedChapter()
        {
            Chapters.Progress done = Built(T.FullSteamLevels, T.FullSteamUnlocks, yard: true);
            Assert.That(Chapters.Complete(done, T), Is.True);
            Assert.That(Chapters.NextBeat(done, T), Is.EqualTo(-1));
        }

        /// <summary>An unowned island has nothing to work on, and LANDFALL is the thing to do.</summary>
        [Test]
        public void NextBeat_OnAnUnownedIslandIsLandfall()
        {
            Assert.That(Chapters.NextBeat(new Chapters.Progress(), T), Is.EqualTo(Chapters.Landfall));
        }

        [Test]
        public void BeatCounts_AgreeWithBeatProgress()
        {
            Chapters.Progress[] cases =
            {
                new Chapters.Progress(),
                Fresh(),
                Built(4, 1),
                Built(T.FirstSmokeLevels, T.WorksUnlocks, yard: true),
                Built(150, 8),
                Built(T.FullSteamLevels, T.FullSteamUnlocks, yard: true),
            };

            for (int c = 0; c < cases.Length; c++)
                for (int b = 0; b < Chapters.BeatCount; b++)
                {
                    int have, need;
                    Chapters.BeatCounts(b, cases[c], T, out have, out need);
                    Assert.That(need, Is.GreaterThan(0), "beat " + b + " wants nothing");
                    Assert.That(Goals.Progress(have, need),
                                Is.EqualTo(Chapters.BeatProgress(b, cases[c], T)).Within(0.0001f),
                                "case " + c + " beat " + b);
                }
        }

        /// <summary>
        /// FULL STEAM asks two things at once, and the bar must report whichever half is further
        /// behind. Reporting the levels while the buildings were missing is how a bar ends up sitting
        /// at 100% refusing to pay.
        /// </summary>
        [Test]
        public void BeatCounts_ForFullSteamReportTheHalfThatIsFurtherBehind()
        {
            int have, need;

            // Levels finished, buildings not: the bar has to be talking about buildings.
            Chapters.BeatCounts(Chapters.FullSteam, Built(T.FullSteamLevels, 2), T, out have, out need);
            Assert.That(need, Is.EqualTo(T.FullSteamUnlocks));
            Assert.That(have, Is.EqualTo(2));

            // Buildings finished, levels not: and now about levels.
            Chapters.BeatCounts(Chapters.FullSteam, Built(20, T.FullSteamUnlocks), T, out have, out need);
            Assert.That(need, Is.EqualTo(T.FullSteamLevels));
            Assert.That(have, Is.EqualTo(20));
        }

        [Test]
        public void BeatCounts_ForAnUnknownBeatWantNothingRatherThanThrowing()
        {
            int have, need;
            Chapters.BeatCounts(Chapters.BeatCount, Fresh(), T, out have, out need);
            Assert.That(have, Is.Zero);
            Assert.That(need, Is.Zero);
        }
    }
}
