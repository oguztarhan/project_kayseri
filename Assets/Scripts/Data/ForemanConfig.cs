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
        [Header("Beceri 1 — İstasyon hızı (son yıldızda, 1.0 üzerine eklenir)")]
        [Tooltip("Ustanın KENDİ istasyonunun hızına eklenir; her yıldız bunun beşte birini öder. " +
                 "Doğrusal, bilerek: 'beşte üç yıldız' kartın beşte üçü demek olsun, bakılacak bir " +
                 "eğri olmasın.\n\n" +
                 "Nadirlik SABİTTİR — kartın üstünde yazar, yıldızla kazanılmaz. Efsanevi bir usta " +
                 "bulunan bir şeydir, çıkılan bir kademe değil.\n\n" +
                 "Not: gelir tavanına dayanmış bir adada hız artışı tek başına para etmez — tavanı " +
                 "kaldıran şey aşağıdaki gelir payıdır. İkisi bilerek birlikte çalışır.")]
        [SerializeField] private double throughputCommon = 0.50d;
        [SerializeField] private double throughputRare = 1.50d;
        [SerializeField] private double throughputLegendary = 4.00d;

        [Header("Beceri 2 — İmparatorluk gelir payı")]
        [Tooltip("Ustanın hızının ne kadarı imparatorluk gelir TAVANINI da yükseltir. Tamamı beş " +
                 "istasyon boyunca devasa olurdu; onda biri ödeyen yarısıdır.\n\n" +
                 "Beş dolu Efsanevi 1 + 5(4.0 x 0.10) = 3.0x eder — yerini aldığı sekiz ustalık " +
                 "kadronun indiği yer, yani merdivenin çözüldüğü yer. Emekli edilen prestij kömürde " +
                 "70x veriyordu ve ekonomi ölçümü merdiveni kıran şeyin o olduğunu gösterdi; bu " +
                 "bilerek bir mertebe altında kalır.")]
        [SerializeField] private double incomeShare = 0.10d;

        [Header("Beceri 3 — Çevrimdışı kazanç (son yıldızda, puan olarak)")]
        [Tooltip("Çevrimdışı verimliliğe eklenen puan. Hızdan TÜRETİLMEZ: verimlilik birin kesri, " +
                 "hız ise sınırsız bir çarpan — birini diğerinden türetmek beş Efsanevi'yi %100'ün " +
                 "üstüne çıkarıyordu, yani oynamamak oynamaktan iyi oluyordu.\n\n" +
                 "Beş dolu Efsanevi 100 puan ekler; tavanı bu dosya değil OfflineConfig koyar.")]
        [SerializeField] private double offlineCommon = 0.05d;
        [SerializeField] private double offlineRare = 0.10d;
        [SerializeField] private double offlineLegendary = 0.20d;

        [Header("Yıldız atlatma")]
        [Tooltip("Y yıldızdan Y+1'e gereken kart: Taban + Adım * (Y - 1), sonra aşağıdaki nadirlik " +
                 "katsayısı. 5,10,15,20 = Sıradan bir ustayı sonuna kadar götürmek için 50 kart.\n\n" +
                 "DEĞİŞTİRMEYİN: mevcut oyuncuların biriktirdiği kartlar bu eğriye göre sayıldı ve " +
                 "her kadro ekranındaki 'kaç/kaç' çubuğu bunu okuyor. Oynatmak, herkesin ne kadar " +
                 "yol aldığını sessizce yeniden yazar.")]
        [SerializeField] private int cardBase = 5;
        [SerializeField] private int cardStep = 5;

        [Tooltip("Eğrinin nadirliğe göre katsayısı: 50 / 70 / 100 kart eder.\n\n" +
                 "NADİR OLAN DAHA PAHALIDIR — kaptanlarda tam tersi, ve ters olması doğru: bir " +
                 "kaptanın derecesi BULUNMASININ zorluğu, o yüzden nadiri ucuz olmalı yoksa hiç " +
                 "yükseltilemez. Ustanın nadirliği ne kadar İYİ olduğu, ve on beşinin de düşme " +
                 "sıklığı bitirilebilir — Efsanevi'nin iki katı ödemesi, kademesini bir kupon değil " +
                 "bir yol yapan tek şey.")]
        [SerializeField] private double cardScaleCommon = 1.00d;
        [SerializeField] private double cardScaleRare = 1.40d;
        [SerializeField] private double cardScaleLegendary = 2.00d;

        [Header("Sandık")]
        [Tooltip("Bir sandıktan çıkan kart sayısı. Usta başına 50-100 kartlık yola karşı 4 kart. " +
                 "Sekiz ustada 90 kartlık yola karşı 3'tü; on beş usta yarı yarıya daha uzun bir " +
                 "koleksiyon, sandık da onunla birlikte büyüdü.")]
        [SerializeField] private int cardsPerChest = 4;
        [Tooltip("Bir sandığın elmas bedeli. İşe alma kaldırıldı (150-900 elmas); elmasın gideceği " +
                 "yer artık burası.")]
        [SerializeField] private long chestGemCost = 60;
        [Tooltip("Toplu açılışın adedi ve bedeli — sandık başına bilerek daha ucuz.")]
        [SerializeField] private int chestBulkCount = 10;
        [SerializeField] private long chestBulkGemCost = 540;
        [Tooltip("Her sandıkta kaç kart RASTGELE değil, en geride kalan ustaya nişanlanır. Dörtte " +
                 "bir: hiçbir usta bir sandıktan fazla aç kalmaz, ama zar da anlamını yitirmez.")]
        [SerializeField] private int chestDirectedPerChest = 1;

        [Header("Sandık nadirlik ağırlıkları")]
        [Tooltip("Hangi nadirliğin ne sıklıkta çıkacağı. Göreli sayılar — birini yükseltmek için " +
                 "diğer ikisini toplamı tutsun diye düzeltmek gerekmez.\n\n" +
                 "70/25/5: Efsanevi yirmi kartta bir ve beş tane var, yani ilki ilk yüz kartın " +
                 "içinde bir yerde — bedava sandıkla iki hafta kadar — beşinin tamamı ise aylara " +
                 "yayılan kuyruk, duvar değil.")]
        [SerializeField] private double weightCommon = 70d;
        [SerializeField] private double weightRare = 25d;
        [SerializeField] private double weightLegendary = 5d;

        [Header("Bedava sandık")]
        [Tooltip("İki bedava sandık arası saniye. 28800 = 8 saat: sabah-akşam giren için günde iki, " +
                 "diğer herkes için bir kez, kimse için alarm kurma sebebi değil.")]
        [SerializeField] private long freeIntervalSeconds = 28800;
        [Tooltip("Bedava sandığın kart sayısı. Satın alınandan az — bu bir damla, satın almayı " +
                 "bırakma sebebi değil. Biriktirmez: bir hafta uzakta kalan tek sandıkla döner.")]
        [SerializeField] private int freeCards = 2;

        [Header("Nadirlik renkleri")]
        [Tooltip("Sıradan, Nadir, Efsanevi. TEK yerde durur: hem kadro ekranındaki kart çerçevesi " +
                 "hem adadaki kaide bunu okur, yoksa aynı Efsanevi iki ekranda iki farklı renk " +
                 "olur. Kaptan derecelerinin paletiyle aynı — oyunun her yerinde Efsanevi altındır.")]
        [SerializeField] private Color[] rarityTint =
        {
            new Color(0.48f, 0.54f, 0.62f, 1f),   // Sıradan
            new Color(0.26f, 0.60f, 0.92f, 1f),   // Nadir
            new Color(0.96f, 0.66f, 0.18f, 1f),   // Efsanevi
        };

        public Color[] RarityTint => rarityTint;

        public Game.Core.Foremen.Tuning ToTuning() => new Game.Core.Foremen.Tuning
        {
            ThroughputCommon    = throughputCommon,
            ThroughputRare      = throughputRare,
            ThroughputLegendary = throughputLegendary,
            OfflineCommon       = offlineCommon,
            OfflineRare         = offlineRare,
            OfflineLegendary    = offlineLegendary,
            IncomeShare         = incomeShare,
            CardBase            = cardBase,
            CardStep            = cardStep,
            CardScaleCommon     = cardScaleCommon,
            CardScaleRare       = cardScaleRare,
            CardScaleLegendary  = cardScaleLegendary,
        };

        public Game.Core.MasterChest.Tuning ToChestTuning() => new Game.Core.MasterChest.Tuning
        {
            CardsPerChest       = cardsPerChest,
            GemCost             = chestGemCost,
            BulkCount           = chestBulkCount,
            BulkGemCost         = chestBulkGemCost,
            DirectedPerChest    = chestDirectedPerChest,
            WeightCommon        = weightCommon,
            WeightRare          = weightRare,
            WeightLegendary     = weightLegendary,
            FreeIntervalSeconds = freeIntervalSeconds,
            FreeCards           = freeCards,
        };
    }
}
