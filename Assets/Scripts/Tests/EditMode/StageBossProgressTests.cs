using Game.Core;
using Game.Systems;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class StageBossProgressTests
    {
        [Test]
        public void EveryStageHasExactlyTwoDistinctStableBosses()
        {
            Assert.That(StageBosses.Count, Is.EqualTo(64));
            var ids = new System.Collections.Generic.HashSet<string>();
            for (int chapter = 0; chapter < Chapters.Count; chapter++)
                for (int stage = 1; stage <= Stages.PerChapter; stage++)
                    for (int boss = 0; boss < StageBosses.BossesPerStage; boss++)
                    {
                        Assert.That(StageBosses.TryGet(chapter, stage, boss, out var definition), Is.True);
                        Assert.That(ids.Add(definition.Id), Is.True);
                        Assert.That(definition.StageId, Is.EqualTo(Stages.Id(chapter, stage)));
                    }
            Assert.That(ids.Count, Is.EqualTo(64));
        }

        [Test]
        public void NextStageWaitsForObjectiveAndBothBossWins()
        {
            var data = new SaveData();
            var chapters = new ChapterService(data, new WalletService(data.wallet), null, Chapters.Tuning.Default);
            var bosses = new StageBossProgressService(data, chapters, null);
            var stages = new StageService(chapters, bosses);

            Assert.That(stages.IsUnlocked(0, 2), Is.False);
            data.islandLevels.Add(new StationLevel { id = "coal#0#0", level = Chapters.Tuning.Default.FirstSmokeLevels });
            Assert.That(stages.IsUnlocked(0, 2), Is.False, "both mandatory victories are still missing");
            Assert.That(bosses.RecordVictory(0, 1, 0), Is.True);
            Assert.That(stages.IsUnlocked(0, 2), Is.False, "one victory cannot clear the stage");
            Assert.That(bosses.RecordVictory(0, 1, 1), Is.True);
            Assert.That(stages.IsUnlocked(0, 2), Is.True);
            Assert.That(bosses.UniqueBossesDefeated, Is.EqualTo(2));
            Assert.That(bosses.RecordVictory(0, 1, 0), Is.False, "a repeated clear cannot add progress");
            Assert.That(bosses.UniqueBossesDefeated, Is.EqualTo(2));
        }

        [Test]
        public void ExistingObjectiveProgressIsGrandfatheredWithoutLootLuckCredit()
        {
            var data = new SaveData();
            data.islandLevels.Add(new StationLevel { id = "coal#0#0", level = Chapters.Tuning.Default.FirstSmokeLevels });
            var chapters = new ChapterService(data, new WalletService(data.wallet), null, Chapters.Tuning.Default);
            var bosses = new StageBossProgressService(data, chapters, null);
            var stages = new StageService(chapters, bosses);

            Assert.That(stages.CurrentLabel(), Is.EqualTo("1-2"));
            Assert.That(bosses.IsStageCleared(0, 1), Is.True);
            Assert.That(bosses.UniqueBossesDefeated, Is.Zero);
            Assert.That(bosses.RecordVictory(0, 1, 0), Is.False);
        }

        [Test]
        public void BossAndCrewBonusesIncreaseRareOddsWithinConfiguredCap()
        {
            SeaCombat.Tuning tuning = SeaCombat.Tuning.Default;
            var baseline = new double[SeaCombat.GradeMult.Length];
            var progressed = new double[SeaCombat.GradeMult.Length];
            SeaCombat.GradeOdds(0, 0d, 0, 0, tuning, baseline);
            SeaCombat.GradeOdds(0, 0d, 64, 20, tuning, progressed);

            Assert.That(progressed[0], Is.LessThan(baseline[0]));
            for (int grade = 1; grade < progressed.Length; grade++)
                Assert.That(progressed[grade], Is.GreaterThan(baseline[grade]));
            Assert.That(SeaCombat.DropBump(3, 30d, 1000, 100, tuning), Is.EqualTo(4.19d).Within(0.0001d));
        }

        [Test]
        public void ChapterAdvanceWaitsForTheFinalStagesBosses()
        {
            var data = new SaveData();
            var chapters = new ChapterService(data, new WalletService(data.wallet), null, Chapters.Tuning.Default);
            var bosses = new StageBossProgressService(data, chapters, null);
            var stages = new StageService(chapters, bosses);
            FinishChapter(data, 0);
            var progression = new ChapterProgressionService(data, chapters, null, stages);

            Assert.That(chapters.Complete(0), Is.True);
            Assert.That(progression.CanAdvance, Is.False);
            for (int stage = 1; stage <= Stages.PerChapter; stage++)
            {
                Assert.That(bosses.RecordVictory(0, stage, 0), Is.True);
                if (stage < Stages.PerChapter) Assert.That(progression.CanAdvance, Is.False);
                Assert.That(bosses.RecordVictory(0, stage, 1), Is.True);
            }
            Assert.That(progression.CanAdvance, Is.True);
        }

        [Test]
        public void BossClearRowsSurviveSaveRoundTrip()
        {
            var data = new SaveData { stageBossesInitialised = true };
            data.stageBosses.Add(new StageBossState { stageId = "coal.stage.1", firstDefeated = true });
            var save = new SaveService("stage-boss-test-memory.dat");
            SaveData loaded = save.Decrypt(save.Encrypt(data), out bool tampered);

            Assert.That(tampered, Is.False);
            Assert.That(loaded.stageBosses, Has.Count.EqualTo(1));
            Assert.That(loaded.stageBosses[0].firstDefeated, Is.True);
            Assert.That(loaded.stageBosses[0].secondDefeated, Is.False);
        }

        private static void FinishChapter(SaveData data, int chapter)
        {
            Chapters.Tuning tuning = Chapters.TuningFor(chapter, Chapters.Tuning.Default);
            string key = Chapters.Namespace(chapter);
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
