using System;

namespace Game.Core
{
    /// <summary>Stable boss assignments for the existing eight-by-four stage ladder.</summary>
    public static class StageBosses
    {
        public const int BossesPerStage = 2;
        public const int Count = Stages.Count * BossesPerStage;

        // Archetypes are existing sea enemy signatures, paired per authored stage.
        private static readonly int[,] Kinds =
        {
            { SeaCombat.Raider, SeaCombat.Beast }, { SeaCombat.Fireship, SeaCombat.Ghost },
            { SeaCombat.Beast, SeaCombat.Raider }, { SeaCombat.Ghost, SeaCombat.Fireship },
            { SeaCombat.Raider, SeaCombat.Fireship }, { SeaCombat.Beast, SeaCombat.Ghost },
            { SeaCombat.Raider, SeaCombat.Beast }, { SeaCombat.Fireship, SeaCombat.Ghost },
            { SeaCombat.Beast, SeaCombat.Raider }, { SeaCombat.Ghost, SeaCombat.Fireship },
            { SeaCombat.Fireship, SeaCombat.Raider }, { SeaCombat.Beast, SeaCombat.Ghost },
            { SeaCombat.Ghost, SeaCombat.Beast }, { SeaCombat.Raider, SeaCombat.Beast },
            { SeaCombat.Fireship, SeaCombat.Ghost }, { SeaCombat.Raider, SeaCombat.Fireship },
            { SeaCombat.Fireship, SeaCombat.Raider }, { SeaCombat.Beast, SeaCombat.Ghost },
            { SeaCombat.Ghost, SeaCombat.Beast }, { SeaCombat.Raider, SeaCombat.Fireship },
            { SeaCombat.Beast, SeaCombat.Fireship }, { SeaCombat.Raider, SeaCombat.Ghost },
            { SeaCombat.Fireship, SeaCombat.Beast }, { SeaCombat.Ghost, SeaCombat.Raider },
            { SeaCombat.Ghost, SeaCombat.Raider }, { SeaCombat.Fireship, SeaCombat.Beast },
            { SeaCombat.Raider, SeaCombat.Ghost }, { SeaCombat.Beast, SeaCombat.Fireship },
            { SeaCombat.Raider, SeaCombat.Fireship }, { SeaCombat.Ghost, SeaCombat.Beast },
            { SeaCombat.Fireship, SeaCombat.Ghost }, { SeaCombat.Beast, SeaCombat.Raider },
        };

        public struct Definition
        {
            public int Chapter, Stage, Index, GlobalIndex, Kind, RewardTier;
            public string StageId, Id, Label;
        }

        public static bool TryGet(int chapter, int stage, int bossIndex, out Definition definition)
        {
            definition = default;
            if (!Stages.IsValid(chapter, stage) || bossIndex < 0 || bossIndex >= BossesPerStage) return false;
            int global = chapter * Stages.PerChapter + stage - 1;
            string stageId = Stages.Id(chapter, stage);
            definition = new Definition
            {
                Chapter = chapter,
                Stage = stage,
                Index = bossIndex,
                GlobalIndex = global,
                Kind = Kinds[global, bossIndex],
                RewardTier = Math.Min(Voyages.TierCount - 1, global / 8),
                StageId = stageId,
                Id = stageId + ".boss." + (bossIndex + 1),
                Label = Stages.Label(chapter, stage) + " · " + (bossIndex == 0 ? "I" : "II"),
            };
            return true;
        }

        public static SeaCombat.Stats Stats(in Definition boss, in SeaCombat.Tuning tuning)
        {
            SeaCombat.Stats stats = SeaCombat.ThreatStats(boss.RewardTier, boss.Kind, tuning);
            double withinBand = 1d + (boss.GlobalIndex % 8) * Math.Max(0d, tuning.BossWithinBandScale);
            double encounterScale = boss.Index == 0
                ? Math.Max(0.1d, tuning.BossFirstMultiplier)
                : Math.Max(0.1d, tuning.BossSecondMultiplier);
            double core = withinBand * encounterScale;
            stats.Hull *= core;
            stats.Shot *= core;
            stats.Def *= core;
            stats.Spd *= 1d + (boss.GlobalIndex % 8) * Math.Max(0d, tuning.BossSpeedPerStage);
            return stats;
        }
    }
}
