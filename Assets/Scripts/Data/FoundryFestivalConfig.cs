using System;
using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Foundry Festival (Döküm Şenliği) balance. Every value here is a default in
    /// <see cref="Game.Core.FoundryFestival.Tuning"/> made editable, so the maths stays in one
    /// testable place and the numbers stay in the Inspector.
    /// Create via: Assets &gt; Create &gt; Ore Empire &gt; Foundry Festival Config.
    ///
    /// EMPTY MEANS SHIPPED. A list left empty is not an empty festival — it is the table in
    /// <c>FoundryFestival.Tuning.Default</c>, which is where the numbers live until somebody has a
    /// reason to move them. That keeps a single source of truth: a freshly created asset behaves
    /// exactly like no asset at all, and only what an author actually edits diverges. Right-click the
    /// asset and pick "Varsayılanları doldur" to write the shipped rows in and start editing from
    /// them.
    ///
    /// A table that fails <see cref="Game.Core.FoundryFestival.IsWellFormed"/> is DROPPED with a
    /// warning rather than clamped. The mistakes it catches — a chest priced above every point the
    /// week pays, a target of zero, a metric index off the end — all read as "the event is broken"
    /// to a player and as nothing at all in the console.
    /// </summary>
    [CreateAssetMenu(fileName = "FoundryFestivalConfig", menuName = "Ore Empire/Foundry Festival Config", order = 25)]
    public sealed class FoundryFestivalConfig : ScriptableObject
    {
        /// <summary>The goal metrics, by their <see cref="Game.Core.Goals"/> index. Named here so the
        /// Inspector offers a list instead of a number; the values are pinned to those constants and
        /// must not be renumbered.</summary>
        private enum Metric
        {
            Kulce = 0,        // Goals.BarsSold
            Yukseltme = 1,    // Goals.Upgrades
            Kontrat = 2,      // Goals.Contracts
            Onarim = 3,       // Goals.Repairs
            Ada = 4,          // Goals.Islands
            Ustabasi = 5,     // Goals.ForemanLevels
        }

        [Serializable]
        private sealed class TaskRow
        {
            [Tooltip("Hangi sayaç. SAYIM ölçüleri kullanın (yükseltme, kontrat, onarım, usta) — külçe " +
                     "ve ada değil. Üretim her cevher kademesinde x3.2 katlanır, bu yüzden sabit bir " +
                     "külçe hedefi kömürde imkânsız, elmasta bedavadır. Günlük görevler de tam olarak " +
                     "bu sebeple sayım ölçüleriyle sınırlıdır.")]
            public Metric metric = Metric.Yukseltme;

            [Tooltip("Görevin hedefi. Görevin GÜNÜ açıldığı andan itibaren sayılır — daha önce " +
                     "yapılan iş bu göreve yazılmaz.")]
            public long target = 5;

            [Tooltip("Bitirince sandık puanına eklenen değer. Sandıklar TAMAMLANAN görevleri sayar, " +
                     "alınanları değil: ekranı hiç açmayan oyuncu da hak ettiğini kazanır.")]
            public int points = 10;

            public long gems = 20;

            [Tooltip("Usta kartı. En geride kalan ustaya değil, rastgele bir yuvaya gider — " +
                     "GoalService'in günlük görev ödülüyle aynı yol.")]
            public int cards;
        }

        [Serializable]
        private sealed class MilestoneRow
        {
            [Tooltip("Sandığı açan TOPLAM puan. Artan sırada olmalı ve sonuncusu haftanın " +
                     "verebileceği toplam puanın altında kalmalı — yoksa yedi gün kovalanıp hiç " +
                     "alınamayan bir ödül olur.")]
            public int points = 40;

            public long gems = 60;
            public int cards;

            [Tooltip("Kaptan pusulası. Bir kaptan sandığı 100 pusuladır; pusula denizden gelir ve " +
                     "yalnızca denize gider, nakit ekonomisine dokunmaz.")]
            public long charts;

            [Tooltip("Geçici gelir çarpanı. 0 = boost yok. Çalışan boostun ÜSTÜNE eklenir " +
                     "(BoostService.AddBoost), yerine geçmez.")]
            public double boostMult;

            [Tooltip("Boostun saniyesi. 1800 = 30 dakika.")]
            public double boostSeconds;
        }

        [Header("Görevler — 7 gün x 3, gün sırasıyla (0-2: 1. gün, 3-5: 2. gün, …)")]
        [SerializeField] private TaskRow[] tasks = new TaskRow[0];

        [Header("Kilometre taşı sandıkları")]
        [SerializeField] private MilestoneRow[] milestones = new MilestoneRow[0];

        /// <summary>
        /// The table the service runs on. An empty list falls back to the shipped one for that half
        /// alone, so editing only the chests keeps the shipped tasks.
        /// </summary>
        public Game.Core.FoundryFestival.Tuning ToTuning()
        {
            Game.Core.FoundryFestival.Tuning shipped = Game.Core.FoundryFestival.Tuning.Default;

            var t = new Game.Core.FoundryFestival.Tuning
            {
                Tasks = tasks != null && tasks.Length > 0 ? BuildTasks() : shipped.Tasks,
                Milestones = milestones != null && milestones.Length > 0 ? BuildMilestones() : shipped.Milestones,
            };

            if (Game.Core.FoundryFestival.IsWellFormed(t)) return t;

            Debug.LogWarning("[Şenlik] Ayar tablosu geçersiz, varsayılanlara dönüldü. Beklenen: " +
                             Game.Core.FoundryFestival.TaskCount + " görev, " +
                             Game.Core.FoundryFestival.MilestoneCount + " sandık; hedefler sıfırdan " +
                             "büyük, sandık puanları artan ve sonuncusu toplam puanın altında.");
            return shipped;
        }

        private Game.Core.FoundryFestival.Task[] BuildTasks()
        {
            var built = new Game.Core.FoundryFestival.Task[tasks.Length];
            for (int i = 0; i < tasks.Length; i++)
            {
                TaskRow r = tasks[i];
                if (r == null) continue;
                built[i] = new Game.Core.FoundryFestival.Task
                {
                    Metric = (int)r.metric,
                    Target = r.target,
                    Points = r.points,
                    Gems = r.gems,
                    Cards = r.cards,
                };
            }
            return built;
        }

        private Game.Core.FoundryFestival.Milestone[] BuildMilestones()
        {
            var built = new Game.Core.FoundryFestival.Milestone[milestones.Length];
            for (int i = 0; i < milestones.Length; i++)
            {
                MilestoneRow r = milestones[i];
                if (r == null) continue;
                built[i] = new Game.Core.FoundryFestival.Milestone
                {
                    Points = r.points,
                    Gems = r.gems,
                    Cards = r.cards,
                    Charts = r.charts,
                    BoostMult = r.boostMult,
                    BoostSeconds = r.boostSeconds,
                };
            }
            return built;
        }

#if UNITY_EDITOR
        /// <summary>Writes the shipped table into the asset so it can be edited row by row. Only ever
        /// run by hand from the asset's context menu.</summary>
        [ContextMenu("Varsayılanları doldur")]
        private void FillWithDefaults()
        {
            Game.Core.FoundryFestival.Tuning shipped = Game.Core.FoundryFestival.Tuning.Default;

            tasks = new TaskRow[shipped.Tasks.Length];
            for (int i = 0; i < shipped.Tasks.Length; i++)
                tasks[i] = new TaskRow
                {
                    metric = (Metric)shipped.Tasks[i].Metric,
                    target = shipped.Tasks[i].Target,
                    points = shipped.Tasks[i].Points,
                    gems = shipped.Tasks[i].Gems,
                    cards = shipped.Tasks[i].Cards,
                };

            milestones = new MilestoneRow[shipped.Milestones.Length];
            for (int i = 0; i < shipped.Milestones.Length; i++)
                milestones[i] = new MilestoneRow
                {
                    points = shipped.Milestones[i].Points,
                    gems = shipped.Milestones[i].Gems,
                    cards = shipped.Milestones[i].Cards,
                    charts = shipped.Milestones[i].Charts,
                    boostMult = shipped.Milestones[i].BoostMult,
                    boostSeconds = shipped.Milestones[i].BoostSeconds,
                };

            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
