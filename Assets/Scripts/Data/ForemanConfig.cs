using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Usta (master) roster tuning. Every value here is a default in
    /// <see cref="Game.Core.Foremen.Tuning"/> or <see cref="Game.Core.MasterChest.Tuning"/> made
    /// editable, so the maths stays in one testable place and the balance stays in the Inspector.
    /// Create the asset via: Assets &gt; Create &gt; Ore Empire &gt; Foreman Config.
    /// </summary>
    [CreateAssetMenu(fileName = "ForemanConfig", menuName = "Ore Empire/Foreman Config", order = 18)]
    public sealed class ForemanConfig : ScriptableObject
    {
        [Header("Yıldız başına güç (oran, 1.0 üzerine eklenir)")]
        [Tooltip("Ustanın KENDİ istasyonunun hızına eklenir. Her kademe iki yıldız: 1-2 Sıradan, " +
                 "3-4 Nadir, 5-6 Destansı, 7-8 Efsanevi, 9-10 Mitik.\n\n" +
                 "Efsanevi'nin son yıldızı tam +%300'dür — kartın vaat ettiği sayı odur. Eğri düz " +
                 "değil hızlanan bir eğridir: bir kademenin ikinci yıldızı birincisinden, bir sonraki " +
                 "kademenin ilk yıldızı ondan da değerli olsun ki terfi HİSSEDİLSİN.\n\n" +
                 "Not: gelir tavanına dayanmış bir adada hız artışı tek başına para etmez — tavanı " +
                 "kaldıran şey aşağıdaki gelir payıdır. İkisi bilerek birlikte çalışır.")]
        [SerializeField] private double commonBoost1 = 0.10d;
        [SerializeField] private double commonBoost2 = 0.20d;
        [SerializeField] private double rareBoost1 = 0.45d;
        [SerializeField] private double rareBoost2 = 0.70d;
        [SerializeField] private double epicBoost1 = 1.10d;
        [SerializeField] private double epicBoost2 = 1.60d;
        [SerializeField] private double legendaryBoost1 = 2.30d;
        [SerializeField] private double legendaryBoost2 = 3.00d;
        [SerializeField] private double mythicBoost1 = 4.00d;
        [SerializeField] private double mythicBoost2 = 5.00d;

        [Header("İmparatorluk gelir payı")]
        [Tooltip("Ustanın gücünün ne kadarı imparatorluk gelir TAVANINI da yükseltir. Tamamı sekiz " +
                 "usta boyunca devasa olurdu; onda biri ödeyen yarısıdır.\n\n" +
                 "Sekiz Efsanevi usta 1 + 8(3.0 x 0.10) = 3.4x eder — eski tam kadronun tam olarak " +
                 "indiği yer, yani merdivenin çözüldüğü yer. Mitik kuyruğu 5.0x'e uzatır, tabanı " +
                 "kaydırmaz. Emekli edilen prestij kömürde 70x veriyordu ve ekonomi ölçümü merdiveni " +
                 "kıran şeyin o olduğunu gösterdi; bu bilerek bir mertebe altında kalır.")]
        [SerializeField] private double incomeShare = 0.10d;

        [Header("Yıldız atlatma")]
        [Tooltip("L yıldızdan L+1'e gereken kart: Taban + Adım * (L - 1). 2,4,6,… = bir ustayı " +
                 "sonuna kadar götürmek için 90 kart.\n\n" +
                 "DEĞİŞTİRMEYİN: mevcut oyuncuların biriktirdiği kartlar bu eğriye göre sayıldı ve " +
                 "her kadro ekranındaki 'kaç/kaç' çubuğu bunu okuyor. Oynatmak, herkesin ne kadar " +
                 "yol aldığını sessizce yeniden yazar.")]
        [SerializeField] private int duplicateBase = 2;
        [SerializeField] private int duplicateStep = 2;

        [Header("Sandık")]
        [Tooltip("Bir sandıktan çıkan kart sayısı. Usta başına 90 kartlık yola karşı 3 kart, sekize " +
                 "bölünmüş bir yuvarlama hatası değil, tek bir ustada görünür ilerlemedir.")]
        [SerializeField] private int cardsPerChest = 3;
        [Tooltip("Bir sandığın elmas bedeli. İşe alma kaldırıldı (150-900 elmas); elmasın gideceği " +
                 "yer artık burası.")]
        [SerializeField] private long chestGemCost = 60;
        [Tooltip("Toplu açılışın adedi ve bedeli — sandık başına bilerek daha ucuz.")]
        [SerializeField] private int chestBulkCount = 10;
        [SerializeField] private long chestBulkGemCost = 540;
        [Tooltip("Her sandıkta kaç kart RASTGELE değil, en geride kalan ustaya nişanlanır. Üçte bir: " +
                 "hiçbir usta bir sandıktan fazla aç kalmaz, ama zar da anlamını yitirmez.")]
        [SerializeField] private int chestDirectedPerChest = 1;

        [Header("Bedava sandık")]
        [Tooltip("İki bedava sandık arası saniye. 28800 = 8 saat: sabah-akşam giren için günde iki, " +
                 "diğer herkes için bir kez, kimse için alarm kurma sebebi değil.")]
        [SerializeField] private long freeIntervalSeconds = 28800;
        [Tooltip("Bedava sandığın kart sayısı. Satın alınandan az — bu bir damla, satın almayı " +
                 "bırakma sebebi değil. Biriktirmez: bir hafta uzakta kalan tek sandıkla döner.")]
        [SerializeField] private int freeCards = 2;

        [Header("Kademe renkleri")]
        [Tooltip("Sıradan, Nadir, Destansı, Efsanevi, Mitik. TEK yerde durur: hem kadro ekranındaki " +
                 "kart çerçevesi hem adadaki kaide bunu okur, yoksa aynı Efsanevi iki ekranda iki " +
                 "farklı renk olur. Kaptan derecelerinin paletiyle aynı — oyunun her yerinde " +
                 "Efsanevi altındır.")]
        [SerializeField] private Color[] tierTint =
        {
            new Color(0.48f, 0.54f, 0.62f, 1f),   // Sıradan
            new Color(0.26f, 0.60f, 0.92f, 1f),   // Nadir
            new Color(0.62f, 0.38f, 0.92f, 1f),   // Destansı
            new Color(0.96f, 0.66f, 0.18f, 1f),   // Efsanevi
            new Color(0.94f, 0.28f, 0.42f, 1f),   // Mitik
        };

        public Color[] TierTint => tierTint;

        public Game.Core.Foremen.Tuning ToTuning() => new Game.Core.Foremen.Tuning
        {
            CommonBoost1    = commonBoost1,    CommonBoost2    = commonBoost2,
            RareBoost1      = rareBoost1,      RareBoost2      = rareBoost2,
            EpicBoost1      = epicBoost1,      EpicBoost2      = epicBoost2,
            LegendaryBoost1 = legendaryBoost1, LegendaryBoost2 = legendaryBoost2,
            MythicBoost1    = mythicBoost1,    MythicBoost2    = mythicBoost2,
            IncomeShare     = incomeShare,
            DuplicateBase   = duplicateBase,
            DuplicateStep   = duplicateStep,
        };

        public Game.Core.MasterChest.Tuning ToChestTuning() => new Game.Core.MasterChest.Tuning
        {
            CardsPerChest       = cardsPerChest,
            GemCost             = chestGemCost,
            BulkCount           = chestBulkCount,
            BulkGemCost         = chestBulkGemCost,
            DirectedPerChest    = chestDirectedPerChest,
            FreeIntervalSeconds = freeIntervalSeconds,
            FreeCards           = freeCards,
        };
    }
}
