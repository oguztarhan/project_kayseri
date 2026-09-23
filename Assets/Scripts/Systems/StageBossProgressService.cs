using System;
using Game.Core;

namespace Game.Systems
{
    /// <summary>Persists the two mandatory first clears for each existing progression stage.</summary>
    public sealed class StageBossProgressService
    {
        private readonly SaveData _data;
        private readonly ChapterService _chapters;
        private readonly Action _save;
        public StageService Stages { get; set; }
        public event Action Changed;

        public StageBossProgressService(SaveData data, ChapterService chapters, Action save)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _chapters = chapters ?? throw new ArgumentNullException(nameof(chapters));
            _save = save;
            Normalise();
        }

        private void Normalise()
        {
            if (_data.stageBosses == null) _data.stageBosses = new System.Collections.Generic.List<StageBossState>();
            for (int i = _data.stageBosses.Count - 1; i >= 0; i--)
            {
                StageBossState row = _data.stageBosses[i];
                if (row == null || !Game.Core.Stages.TryCoordinate(row.stageId, out _, out _))
                {
                    _data.stageBosses.RemoveAt(i);
                    continue;
                }
                for (int j = i - 1; j >= 0; j--)
                    if (_data.stageBosses[j] == null || _data.stageBosses[j].stageId == row.stageId)
                        _data.stageBosses.RemoveAt(j);
            }

            if (!_data.stageBossesInitialised)
            {
                // A pre-feature save has already passed every objective it satisfied. Grandfather
                // those stages for progression, but do not count them as boss victories or loot luck.
                for (int chapter = 0; chapter < Chapters.Count; chapter++)
                    for (int stage = 1; stage <= Game.Core.Stages.PerChapter; stage++)
                        if (_chapters.Satisfied(chapter, stage) || _chapters.Claimed(chapter, stage))
                            Row(chapter, stage).legacyCleared = true;
                _data.stageBossesInitialised = true;
                _save?.Invoke();
            }
        }

        private StageBossState Row(int chapter, int stage)
        {
            string id = Game.Core.Stages.Id(chapter, stage);
            for (int i = 0; i < _data.stageBosses.Count; i++)
                if (_data.stageBosses[i] != null && _data.stageBosses[i].stageId == id)
                    return _data.stageBosses[i];
            var row = new StageBossState { stageId = id };
            _data.stageBosses.Add(row);
            return row;
        }

        public int UniqueBossesDefeated
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _data.stageBosses.Count; i++)
                {
                    StageBossState row = _data.stageBosses[i];
                    if (row == null || row.legacyCleared) continue;
                    if (row.firstDefeated) n++;
                    if (row.secondDefeated) n++;
                }
                return n;
            }
        }

        public bool IsDefeated(int chapter, int stage, int bossIndex)
        {
            if (!StageBosses.TryGet(chapter, stage, bossIndex, out _)) return false;
            StageBossState row = Find(chapter, stage);
            return row != null && (row.legacyCleared || (bossIndex == 0 ? row.firstDefeated : row.secondDefeated));
        }

        public bool IsStageCleared(int chapter, int stage)
            => IsDefeated(chapter, stage, 0) && IsDefeated(chapter, stage, 1);

        public int BossesRemaining(int chapter, int stage)
            => (IsDefeated(chapter, stage, 0) ? 0 : 1) + (IsDefeated(chapter, stage, 1) ? 0 : 1);

        public bool IsAvailable(int chapter, int stage, int bossIndex)
            => Stages != null && Stages.IsUnlocked(chapter, stage)
            && !IsDefeated(chapter, stage, bossIndex);

        /// <summary>Returns true only for a first, valid victory on an unlocked stage boss.</summary>
        public bool RecordVictory(int chapter, int stage, int bossIndex)
            => RecordVictory(chapter, stage, bossIndex, null);

        /// <summary>Records the first clear, applies its reward, then persists both together.</summary>
        public bool RecordVictory(int chapter, int stage, int bossIndex, Action beforeSave)
        {
            if (!IsAvailable(chapter, stage, bossIndex)) return false;
            StageBossState row = Row(chapter, stage);
            if (bossIndex == 0) row.firstDefeated = true;
            else row.secondDefeated = true;
            beforeSave?.Invoke();
            _save?.Invoke();
            Changed?.Invoke();
            return true;
        }

        private StageBossState Find(int chapter, int stage)
        {
            string id = Game.Core.Stages.Id(chapter, stage);
            for (int i = 0; i < _data.stageBosses.Count; i++)
                if (_data.stageBosses[i] != null && _data.stageBosses[i].stageId == id)
                    return _data.stageBosses[i];
            return null;
        }
    }
}
