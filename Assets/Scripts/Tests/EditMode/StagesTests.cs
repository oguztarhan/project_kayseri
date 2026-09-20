using Game.Core;
using Game.Systems;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>Locks the approved 8x4 stage contract before presentation and content are attached.</summary>
    public sealed class StagesTests
    {
        [Test]
        public void CatalogueContainsFourStagesForEachOfEightChapters()
        {
            Assert.That(Stages.PerChapter, Is.EqualTo(4));
            Assert.That(Stages.Count, Is.EqualTo(32));

            int found = 0;
            for (int chapter = 0; chapter < Chapters.Count; chapter++)
                for (int stage = 1; stage <= Stages.PerChapter; stage++)
                {
                    Assert.That(Stages.IsValid(chapter, stage), Is.True);
                    Assert.That(Stages.CompletionBeat(stage), Is.EqualTo(stage));
                    Assert.That(Stages.Id(chapter, stage), Is.EqualTo(Chapters.Namespace(chapter) + ".stage." + stage));
                    Assert.That(Stages.Label(chapter, stage), Is.EqualTo((chapter + 1) + "-" + stage));
                    found++;
                }

            Assert.That(found, Is.EqualTo(Stages.Count));
        }

        [Test]
        public void StableIdsRoundTripAndRejectCoordinatesOutsideTheApprovedCatalogue()
        {
            Assert.That(Stages.TryCoordinate("ruby.stage.3", out int chapter, out int stage), Is.True);
            Assert.That(chapter, Is.EqualTo(5));
            Assert.That(stage, Is.EqualTo(3));

            Assert.That(Stages.IsValid(0, 0), Is.False);
            Assert.That(Stages.IsValid(0, 5), Is.False);
            Assert.That(Stages.IsValid(8, 1), Is.False);
            Assert.That(Stages.TryCoordinate("copper.stage.5", out _, out _), Is.False);
        }

        [Test]
        public void DefaultStageServiceUsesTheCatalogueWithoutASecondProgressLedger()
        {
            var data = new SaveData();
            var chapters = new ChapterService(data, new WalletService(data.wallet), null, Chapters.Tuning.Default);
            var stages = new StageService(chapters);

            Assert.That(stages.IsUnlocked(0, 1), Is.True);
            Assert.That(stages.IsUnlocked("coal.stage.1"), Is.True);
            Assert.That(stages.IsUnlocked(0, 2), Is.False);

            data.islandLevels.Add(new StationLevel
            {
                id = "coal#0#0",
                level = Chapters.Tuning.Default.FirstSmokeLevels,
            });

            Assert.That(stages.IsComplete(0, 1), Is.True);
            Assert.That(stages.IsUnlocked(0, 2), Is.True);
            Assert.That(stages.IsUnlocked(0, 3), Is.False);
        }

        [Test]
        public void CurrentStageAdvancesFromObjectivesButTheNextChapterStillNeedsExplicitAdvance()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var chapters = new ChapterService(data, wallet, null, Chapters.Tuning.Default);
            var stages = new StageService(chapters);

            Assert.That(stages.CurrentLabel(), Is.EqualTo("1-1"));

            data.islandLevels.Add(new StationLevel
            {
                id = "coal#0#0",
                level = Chapters.Tuning.Default.FirstSmokeLevels,
            });
            Assert.That(stages.CurrentLabel(), Is.EqualTo("1-2"),
                        "the first objective advances the player without claiming a reward");

            for (int unlock = 0; unlock < Chapters.Tuning.Default.WorksUnlocks; unlock++)
                data.islandLevels.Add(new StationLevel { id = "coalu#" + unlock, level = 1 });
            Assert.That(stages.CurrentLabel(), Is.EqualTo("1-3"));

            data.idleMarketYards.Add(new IdleMarketYard
            {
                schemaVersion = IdleMarketMigration.SchemaVersion,
                id = "coal",
                hireCarry = MarketFlow.MaxHireLevel,
                hireServe = MarketFlow.MaxHireLevel,
                dispatchLevel = MarketFlow.MaxHireLevel,
            });
            Assert.That(stages.CurrentLabel(), Is.EqualTo("1-4"));

            data.islandLevels[0].level = Chapters.Tuning.Default.FullSteamLevels;
            for (int unlock = Chapters.Tuning.Default.WorksUnlocks;
                 unlock < Chapters.Tuning.Default.FullSteamUnlocks; unlock++)
                data.islandLevels.Add(new StationLevel { id = "coalu#" + unlock, level = 1 });

            Assert.That(chapters.Complete(0), Is.True);
            Assert.That(stages.CurrentLabel(), Is.EqualTo("1-4"),
                        "a completed final stage waits for the player to choose NEXT CHAPTER");

            var progression = new ChapterProgressionService(data, chapters, null);
            Assert.That(progression.TryAdvance(), Is.True);
            Assert.That(stages.CurrentLabel(), Is.EqualTo("2-1"));
        }
    }
}
