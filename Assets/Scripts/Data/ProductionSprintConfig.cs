using System;
using UnityEngine;

namespace Game.Data
{
    /// <summary>Inspector authoring surface for Production Sprint scoring and personal rewards.</summary>
    [CreateAssetMenu(fileName = "ProductionSprintConfig", menuName = "Ore Empire/Production Sprint Config", order = 27)]
    public sealed class ProductionSprintConfig : ScriptableObject
    {
        private enum Metric
        {
            Kulce = 0,
            Yukseltme = 1,
            Kontrat = 2,
            Onarim = 3,
            Ada = 4,
            Ustabasi = 5,
        }

        [Serializable]
        private sealed class RuleRow
        {
            public Metric metric = Metric.Yukseltme;
            [Min(1)] public long actionLimit = 10;
            [Min(1)] public int pointsPerAction = 1;
        }

        [Serializable]
        private sealed class RewardRow
        {
            [Min(0)] public long gems;
            [Min(0)] public int cards;
            [Tooltip("Ödül alındığı andaki kalıcı gelir hızının kaç dakikası kadar nakit verilir.")]
            [Min(0f)] public double cashMinutes;

            public Game.Core.ProductionSprint.Reward Build() => new Game.Core.ProductionSprint.Reward
            {
                Gems = gems,
                Cards = cards,
                CashMinutes = cashMinutes,
            };

            public void Copy(in Game.Core.ProductionSprint.Reward reward)
            {
                gems = reward.Gems;
                cards = reward.Cards;
                cashMinutes = reward.CashMinutes;
            }
        }

        [Serializable]
        private sealed class MilestoneRow
        {
            [Min(1)] public long score = 10;
            public RewardRow reward = new RewardRow();
        }

        [SerializeField] private RuleRow[] rules = new RuleRow[0];
        [SerializeField] private MilestoneRow[] milestones = new MilestoneRow[0];

        public Game.Core.ProductionSprint.Tuning ToTuning()
        {
            if (rules == null || rules.Length == 0) return Game.Core.ProductionSprint.Tuning.Default;

            var tuning = new Game.Core.ProductionSprint.Tuning
            {
                Rules = new Game.Core.ProductionSprint.ScoringRule[rules.Length],
                Milestones = new Game.Core.ProductionSprint.Milestone[milestones != null ? milestones.Length : 0],
            };

            for (int i = 0; i < tuning.Rules.Length; i++)
            {
                RuleRow row = rules[i];
                if (row == null) continue;
                tuning.Rules[i] = new Game.Core.ProductionSprint.ScoringRule
                {
                    Metric = (int)row.metric,
                    ActionLimit = row.actionLimit,
                    PointsPerAction = row.pointsPerAction,
                };
            }
            for (int i = 0; i < tuning.Milestones.Length; i++)
            {
                MilestoneRow row = milestones[i];
                if (row == null) continue;
                tuning.Milestones[i] = new Game.Core.ProductionSprint.Milestone
                {
                    Score = row.score,
                    Reward = row.reward != null ? row.reward.Build() : default,
                };
            }

            if (Game.Core.ProductionSprint.IsWellFormed(tuning)) return tuning;
            Debug.LogWarning("[Üretim Sprinti] Ayar tablosu geçersiz; varsayılan tablo kullanılıyor.");
            return Game.Core.ProductionSprint.Tuning.Default;
        }

#if UNITY_EDITOR
        [ContextMenu("Varsayılanları doldur")]
        private void FillWithDefaults()
        {
            Game.Core.ProductionSprint.Tuning source = Game.Core.ProductionSprint.Tuning.Default;
            rules = new RuleRow[source.Rules.Length];
            for (int i = 0; i < rules.Length; i++)
                rules[i] = new RuleRow
                {
                    metric = (Metric)source.Rules[i].Metric,
                    actionLimit = source.Rules[i].ActionLimit,
                    pointsPerAction = source.Rules[i].PointsPerAction,
                };

            milestones = new MilestoneRow[source.Milestones.Length];
            for (int i = 0; i < milestones.Length; i++)
            {
                milestones[i] = new MilestoneRow { score = source.Milestones[i].Score };
                milestones[i].reward.Copy(source.Milestones[i].Reward);
            }
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
