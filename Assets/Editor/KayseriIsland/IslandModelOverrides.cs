using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kayseri.IslandTools
{
    /// <summary>
    /// Which model stands in for each generated one, per island and per phase.
    ///
    /// The twenty-four phase prefabs are BUILT, not authored — <see cref="IslandBuilder.BuildPhasePrefabs"/>
    /// throws them away and remakes them from the Blender FBX every time the map is re-exported. So a
    /// model swapped by hand inside one of those prefabs survives exactly until the next export. This
    /// asset is where a swap lives instead: the rebuild reads it and re-applies everything, so the work
    /// is done once.
    ///
    /// Entries are matched by DISTRICT + MODEL NAME, not per object. "Foliage/Pine" is one entry and it
    /// covers all 833 trees; "Depot/Silo" covers Silo0..Silo3. The names come from the generator, so
    /// nothing here is typed by hand — <see cref="IslandModelSwapper.Scan"/> reads them off the prefabs.
    ///
    /// Island and phase are filters, not keys. An entry left on Any applies everywhere, and a more
    /// specific entry beats it: island counts for more than phase, so a Gold/Any entry wins over an
    /// Any/Phase2 one. That is what makes the common case one row and the exception a second row.
    /// </summary>
    public sealed class IslandModelOverrides : ScriptableObject
    {
        public enum IslandFilter { Any, Coal, Copper, Iron, Gold }

        public enum PhaseFilter { Any, Phase1, Phase2, Phase3 }

        [Serializable]
        public sealed class Entry
        {
            [Tooltip("District collection the model belongs to — Depot, Mine, Foliage, Port…")]
            public string Group;

            [Tooltip("Model name with its trailing number removed: Silo, Pine, Crane, Plant…")]
            public string Model;

            public IslandFilter Island = IslandFilter.Any;
            public PhaseFilter Phase = PhaseFilter.Any;

            [Tooltip("Boş bırakılırsa üretilen model olduğu gibi kalır.")]
            public GameObject Replacement;

            [Tooltip("Model tamamen gizlenir, yerine bir şey konmaz. Yolda park etmiş süs araçları " +
                     "gibi oyuncunun filosuyla karışan parçaları kaldırmak için. Replacement boş " +
                     "olabilir; doluysa yok sayılır.")]
            public bool Hide;

            [Tooltip("Bir bina alanının (Pad / Yard / Apron / Plaza) üstüne düşen kopyaları atlar ve " +
                     "gizler. Kayaların inşaat alanını doldurmasını engellemek için.")]
            public bool SkipOverBuildings;

            [Tooltip("Yeni modeli eskisinin kapladığı hacme göre ölçekler. Kenney parçaları " +
                     "üretilen modellerle aynı boyda değil, bu yüzden varsayılan açık.")]
            public bool FitToOriginal = true;

            [Tooltip("Sığdırmadan sonra uygulanan çarpan. 1 = eskisiyle aynı boy.")]
            public float Scale = 1f;

            [Tooltip("Metre cinsinden kaydırma — modelin oturma noktası farklıysa.")]
            public Vector3 Offset;

            [Tooltip("Derece cinsinden ek dönüş — modelin burnu başka yöne bakıyorsa.")]
            public Vector3 Rotation;

            /// <summary>The row as the window lists it, e.g. "Depot / Silo  (Gold, Phase2)".</summary>
            public string Title
            {
                get
                {
                    string where = Island == IslandFilter.Any && Phase == PhaseFilter.Any
                        ? ""
                        : "  (" + (Island == IslandFilter.Any ? "all islands" : Island.ToString()) +
                          ", " + (Phase == PhaseFilter.Any ? "all phases" : Phase.ToString()) + ")";
                    return Group + " / " + Model + where;
                }
            }
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        public List<Entry> Entries => entries;

        /// <summary>
        /// The entry that governs one model on one island at one phase, or null when nothing matches.
        ///
        /// Scored rather than searched in priority order, because the list is flat and the caller runs
        /// this once per object: island exact is worth 2 and phase exact 1, so Gold/Any (2) outranks
        /// Any/Phase2 (1) and Gold/Phase2 (3) outranks both. Ties keep the FIRST entry, so a row added
        /// later never silently displaces one already working.
        /// </summary>
        public Entry Resolve(string group, string model, IslandFilter island, PhaseFilter phase)
        {
            Entry best = null;
            int bestScore = -1;
            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                // A Hide row does its work without a replacement — that IS the work.
                if (e == null || (e.Replacement == null && !e.Hide)) continue;
                if (!string.Equals(e.Group, group, StringComparison.Ordinal)) continue;
                if (!string.Equals(e.Model, model, StringComparison.Ordinal)) continue;
                if (e.Island != IslandFilter.Any && e.Island != island) continue;
                if (e.Phase != PhaseFilter.Any && e.Phase != phase) continue;

                int score = (e.Island == IslandFilter.Any ? 0 : 2) + (e.Phase == PhaseFilter.Any ? 0 : 1);
                if (score <= bestScore) continue;
                bestScore = score;
                best = e;
            }
            return best;
        }

        public static PhaseFilter ToPhase(int phase)
        {
            return phase == 1 ? PhaseFilter.Phase1
                 : phase == 2 ? PhaseFilter.Phase2
                 : phase == 3 ? PhaseFilter.Phase3
                 : PhaseFilter.Any;
        }

        public static IslandFilter ToIsland(string island)
        {
            for (int i = 1; i < 5; i++)
            {
                var f = (IslandFilter)i;
                if (string.Equals(f.ToString(), island, StringComparison.OrdinalIgnoreCase)) return f;
            }
            return IslandFilter.Any;
        }
    }
}
