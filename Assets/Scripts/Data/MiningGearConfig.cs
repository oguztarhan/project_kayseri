using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Mining gear tuning — every value here is a default in <see cref="Game.Core.MiningGear.Tuning"/>
    /// made Inspector-editable. Create via: Assets &gt; Create &gt; Ore Empire &gt; Mining Gear Config.
    ///
    /// The game runs without this asset, falling back to that Default, exactly as it does for the
    /// crate, the sea and the workshop bench.
    /// </summary>
    [CreateAssetMenu(fileName = "MiningGearConfig", menuName = "Ore Empire/Mining Gear Config", order = 28)]
    public sealed class MiningGearConfig : ScriptableObject
    {
        [Header("Üretim")]
        [Tooltip("Bir üretimin madencilik puanı bedeli.")]
        [SerializeField] private long craftCost = 5L;

        [Header("Üretim — dereceye göre ağırlık")]
        [Tooltip("Toplamlarının 1 olması gerekmez; kimsenin taşımadığı bir derece kendiliğinden düşer.")]
        [SerializeField] private double commonWeight = 0.600d;
        [SerializeField] private double rareWeight = 0.260d;
        [SerializeField] private double epicWeight = 0.105d;
        [SerializeField] private double legendaryWeight = 0.030d;
        [SerializeField] private double mythicWeight = 0.005d;

        [Header("Ada gelir bonusu — kuşanılan eşya başına, dereceye göre")]
        [Tooltip("Tam mitik takım (4 yuva) +%80 gelir eder; tam sıradan takım +%8.")]
        [SerializeField] private double commonBonus = 0.020d;
        [SerializeField] private double rareBonus = 0.040d;
        [SerializeField] private double epicBonus = 0.070d;
        [SerializeField] private double legendaryBonus = 0.120d;
        [SerializeField] private double mythicBonus = 0.200d;

        [Header("Madencilik puanı — zaman içinde kendiliğinden birikir")]
        [Tooltip("Kaptan görevde olduğu sürece, gerçek zamanla kendiliğinden birikir — aktivite " +
                 "gerektirmez. Havuz bir üst sınırda durur.")]
        [SerializeField, Min(0)] private int pointsPerTick = 1;
        [SerializeField] private double tickSeconds = 600d;
        [SerializeField] private long pointCap = 50L;

        public Game.Core.MiningGear.Tuning ToTuning() => new Game.Core.MiningGear.Tuning
        {
            CraftCost       = craftCost,
            CommonWeight    = commonWeight,
            RareWeight      = rareWeight,
            EpicWeight      = epicWeight,
            LegendaryWeight = legendaryWeight,
            MythicWeight    = mythicWeight,
            CommonBonus     = commonBonus,
            RareBonus       = rareBonus,
            EpicBonus       = epicBonus,
            LegendaryBonus  = legendaryBonus,
            MythicBonus     = mythicBonus,
            PointsPerTick   = pointsPerTick,
            TickSeconds     = tickSeconds,
            PointCap        = pointCap,
        };
    }
}
