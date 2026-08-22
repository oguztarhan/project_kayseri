using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Foreman roster tuning. Every value here is a default in <see cref="Game.Core.Foremen.Tuning"/>
    /// made editable, so the maths stays in one testable place and the balance stays in the Inspector.
    /// Create the asset via: Assets &gt; Create &gt; Ore Empire &gt; Foreman Config.
    /// </summary>
    [CreateAssetMenu(fileName = "ForemanConfig", menuName = "Ore Empire/Foreman Config", order = 18)]
    public sealed class ForemanConfig : ScriptableObject
    {
        [Header("Bir seviye ne kadar değerli (oran)")]
        [Tooltip("Hem o istasyonun hızına hem de imparatorluk gelir çarpanına eklenir. " +
                 "0.020 = seviye başına %2.")]
        [SerializeField] private double commonPerLevel = 0.020d;
        [SerializeField] private double rarePerLevel = 0.030d;
        [SerializeField] private double epicPerLevel = 0.045d;

        [Header("İşe alma bedeli (elmas)")]
        [SerializeField] private long commonHireGems = 150;
        [SerializeField] private long rareHireGems = 400;
        [SerializeField] private long epicHireGems = 900;

        [Header("Seviye atlatma")]
        [Tooltip("L'den L+1'e gereken kart: Taban + Adım * (L - 1).")]
        [SerializeField] private int duplicateBase = 2;
        [SerializeField] private int duplicateStep = 2;
        [Tooltip("Kartların yanında alınan elmas, aynı şekilde büyür.")]
        [SerializeField] private long levelGemBase = 60;
        [SerializeField] private long levelGemStep = 45;

        public Game.Core.Foremen.Tuning ToTuning() => new Game.Core.Foremen.Tuning
        {
            CommonPerLevel = commonPerLevel,
            RarePerLevel   = rarePerLevel,
            EpicPerLevel   = epicPerLevel,
            CommonHireGems = commonHireGems,
            RareHireGems   = rareHireGems,
            EpicHireGems   = epicHireGems,
            DuplicateBase  = duplicateBase,
            DuplicateStep  = duplicateStep,
            LevelGemBase   = levelGemBase,
            LevelGemStep   = levelGemStep,
        };
    }
}
