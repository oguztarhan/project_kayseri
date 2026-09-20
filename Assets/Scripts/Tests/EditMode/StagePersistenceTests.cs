using Game.Core;
using Game.Systems;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Stages deliberately have no saved row of their own. These tests protect that decision by
    /// rebuilding their labels from the encrypted, existing chapter data exactly as a relaunch does.
    /// </summary>
    public sealed class StagePersistenceTests
    {
        [Test]
        public void APreStageSaveStillStartsAtTheFirstStageAfterReload()
        {
            var legacy = new SaveData { chapters = null };

            SaveData reloaded = RoundTrip(legacy);
            var stages = Stages(reloaded, out _);

            Assert.That(stages.CurrentLabel(), Is.EqualTo("1-1"));
        }

        [Test]
        public void AnInProgressStageIsReconstructedAfterEncryptedSaveRoundTrip()
        {
            var data = new SaveData();
            data.islandLevels.Add(new StationLevel
            {
                id = "coal#0#0",
                level = Chapters.Tuning.Default.FirstSmokeLevels,
            });
            for (int unlock = 0; unlock < Chapters.Tuning.Default.WorksUnlocks; unlock++)
                data.islandLevels.Add(new StationLevel { id = "coalu#" + unlock, level = 1 });

            SaveData reloaded = RoundTrip(data);
            var stages = Stages(reloaded, out _);

            Assert.That(stages.CurrentLabel(), Is.EqualTo("1-3"));
        }

        [Test]
        public void ConfirmedChapterAdvanceKeepsItsRewardsAndRestoresTheNextChaptersFirstStage()
        {
            var data = new SaveData();
            var stages = Stages(data, out ChapterService chapters);
            Finish(data, 0);
            var progression = new ChapterProgressionService(data, chapters, null);

            Assert.That(progression.TryAdvance(), Is.True);
            long gemsAfterAdvance = data.wallet.gems;

            SaveData reloaded = RoundTrip(data);
            var restoredStages = Stages(reloaded, out ChapterService restoredChapters);

            Assert.That(restoredStages.CurrentLabel(), Is.EqualTo("2-1"));
            Assert.That(reloaded.wallet.gems, Is.EqualTo(gemsAfterAdvance));
            for (int beat = 0; beat < Chapters.BeatCount; beat++)
                Assert.That(restoredChapters.Claimed(0, beat), Is.True, "chapter 1 beat " + beat);

            // The source service stays in scope to state the intended handoff explicitly: the old
            // stage remains complete, while the active stage is rebuilt from chapter two's empty key.
            Assert.That(stages.CurrentLabel(), Is.EqualTo("2-1"));
        }

        [Test]
        public void EveryOneOfTheThirtyTwoStagesSurvivesItsOwnChapterBoundary()
        {
            var data = new SaveData();
            var stages = Stages(data, out ChapterService chapters);
            var progression = new ChapterProgressionService(data, chapters, null);

            for (int chapter = 0; chapter < Chapters.Count; chapter++)
            {
                Chapters.Tuning tuning = Chapters.TuningFor(chapter, Chapters.Tuning.Default);
                string key = Chapters.Namespace(chapter);
                Assert.That(stages.CurrentLabel(), Is.EqualTo((chapter + 1) + "-1"));

                data.islandLevels.Add(new StationLevel { id = key + "#0#0", level = tuning.FirstSmokeLevels });
                Assert.That(stages.CurrentLabel(), Is.EqualTo((chapter + 1) + "-2"));

                for (int unlock = 0; unlock < tuning.WorksUnlocks; unlock++)
                    data.islandLevels.Add(new StationLevel { id = key + "u#" + unlock, level = 1 });
                Assert.That(stages.CurrentLabel(), Is.EqualTo((chapter + 1) + "-3"));

                data.idleMarketYards.Add(new IdleMarketYard
                {
                    schemaVersion = IdleMarketMigration.SchemaVersion,
                    id = key,
                    hireCarry = MarketFlow.MaxHireLevel,
                    hireServe = MarketFlow.MaxHireLevel,
                    dispatchLevel = MarketFlow.MaxHireLevel,
                });
                Assert.That(stages.CurrentLabel(), Is.EqualTo((chapter + 1) + "-4"));

                data.islandLevels[data.islandLevels.Count - tuning.WorksUnlocks - 1].level = tuning.FullSteamLevels;
                for (int unlock = tuning.WorksUnlocks; unlock < tuning.FullSteamUnlocks; unlock++)
                    data.islandLevels.Add(new StationLevel { id = key + "u#" + unlock, level = 1 });
                Assert.That(chapters.Complete(chapter), Is.True);
                Assert.That(stages.CurrentLabel(), Is.EqualTo((chapter + 1) + "-4"));

                if (chapter < Chapters.Count - 1)
                {
                    Assert.That(progression.TryAdvance(), Is.True);
                    Assert.That(stages.CurrentLabel(), Is.EqualTo((chapter + 2) + "-1"));
                }
                else Assert.That(progression.TryAdvance(), Is.False);
            }
        }

        private static StageService Stages(SaveData data, out ChapterService chapters)
        {
            chapters = new ChapterService(data, new WalletService(data.wallet), null, Chapters.Tuning.Default);
            return new StageService(chapters);
        }

        private static SaveData RoundTrip(SaveData data)
        {
            var save = new SaveService("stage-progression-memory-only.dat");
            SaveData reloaded = save.Decrypt(save.Encrypt(data), out bool tampered);
            Assert.That(tampered, Is.False);
            Assert.That(reloaded, Is.Not.Null);
            return reloaded;
        }

        private static void Finish(SaveData data, int chapter)
        {
            string key = Chapters.Namespace(chapter);
            Chapters.Tuning tuning = Chapters.TuningFor(chapter, Chapters.Tuning.Default);
            data.islandLevels.Add(new StationLevel { id = key + "#0#0", level = tuning.FullSteamLevels });
            for (int unlock = 0; unlock < tuning.FullSteamUnlocks; unlock++)
                data.islandLevels.Add(new StationLevel { id = key + "u#" + unlock, level = 1 });
            data.idleMarketYards.Add(new IdleMarketYard
            {
                schemaVersion = IdleMarketMigration.SchemaVersion,
                id = key,
                hireCarry = MarketFlow.MaxHireLevel,
                hireServe = MarketFlow.MaxHireLevel,
                dispatchLevel = MarketFlow.MaxHireLevel,
            });
        }
    }
}
