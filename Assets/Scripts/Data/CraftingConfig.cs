using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Workshop tuning. Every value here is a default in <see cref="Game.Core.Crafting.Tuning"/>
    /// made editable. The odds and XP TABLES stay in <see cref="Game.Core.Crafting"/> as code —
    /// they are append-only ladders a save leans on, not sliders.
    /// Create via: Assets &gt; Create &gt; Ore Empire &gt; Crafting Config.
    ///
    /// The game runs without this asset, falling back to that Default, exactly as it does for the
    /// crate and the sea.
    /// </summary>
    [CreateAssetMenu(fileName = "CraftingConfig", menuName = "Ore Empire/Crafting Config", order = 23)]
    public sealed class CraftingConfig : ScriptableObject
    {
        [Header("Üretim")]
        [Tooltip("Bir üretimin zanaat puanı bedeli.")]
        [SerializeField] private long craftCost = 1L;

        [Header("Yenilenme durakları — 10/20/30. seviye, saat cinsinden")]
        [Tooltip("10. seviyeye ulaşınca başlayan bekleme. Bugün açılıp bugün Efsanevi üretilememesi " +
                 "bu üç sayının işi: 16. seviye — ilk Efsanevi ihtimali — hesabın açıldığı oturuma " +
                 "hiçbir puan bolluğunda sığamaz.")]
        [SerializeField] private double gate1Hours = 6d;
        [SerializeField] private double gate2Hours = 12d;
        [Tooltip("30. seviyenin durağı da vardır: onu geçmek son kademe (tier 3) bütçesini açar.")]
        [SerializeField] private double gate3Hours = 24d;

        [Header("Puan gelirleri")]
        [Tooltip("Kazanılan bir deniz çatışmasının puan düşürme ihtimali (0..1), ve düşürdüğünde kaç puan.")]
        [SerializeField] private double pointDropChance = 0.20d;
        [SerializeField, Min(0)] private int pointsPerWin = 1;

        [Header("Zanaat Kaptanı — seviye başına göreli nadirlik ağırlığı")]
        [Tooltip("Kaptanın her seviyesi, atölyenin zaten açtığı nadirliğe bu göreli ağırlığı ekler. " +
                 "Sıra: Sıradan, Nadir, Destansı, Efsanevi, Mitik. Kilitli nadirlikler hiçbir zaman " +
                 "ağırlık kazanmaz; sonuçlar her zaman yeniden normalize edilir.")]
        [SerializeField, Min(0)] private double captainCommonBonusPerLevel = 0.00d;
        [SerializeField, Min(0)] private double captainRareBonusPerLevel = 0.020d;
        [SerializeField, Min(0)] private double captainEpicBonusPerLevel = 0.040d;
        [SerializeField, Min(0)] private double captainLegendaryBonusPerLevel = 0.060d;
        [SerializeField, Min(0)] private double captainMythicBonusPerLevel = 0.080d;


        public Game.Core.Crafting.Tuning ToTuning() => new Game.Core.Crafting.Tuning
        {
            CraftCost       = craftCost,
            Gate1Hours      = gate1Hours,
            Gate2Hours      = gate2Hours,
            Gate3Hours      = gate3Hours,
            PointDropChance = pointDropChance,
            PointsPerWin    = pointsPerWin,
            CaptainCommonBonusPerLevel    = captainCommonBonusPerLevel,
            CaptainRareBonusPerLevel      = captainRareBonusPerLevel,
            CaptainEpicBonusPerLevel      = captainEpicBonusPerLevel,
            CaptainLegendaryBonusPerLevel = captainLegendaryBonusPerLevel,
            CaptainMythicBonusPerLevel    = captainMythicBonusPerLevel,
        };
    }
}
