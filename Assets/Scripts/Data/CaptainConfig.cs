using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Captain and crate tuning. Every value here is a default in
    /// <see cref="Game.Core.Captains.Tuning"/> or <see cref="Game.Core.CaptainCrate.Tuning"/> made
    /// editable — one asset for both, because the roster's worth and the odds of finding it are one
    /// balance surface and splitting them across two files would only let them drift apart.
    /// Create via: Assets &gt; Create &gt; Ore Empire &gt; Captain Config.
    ///
    /// The game runs without this asset, falling back to those two Defaults, exactly as it does for
    /// the foreman roster and the dock.
    /// </summary>
    [CreateAssetMenu(fileName = "CaptainConfig", menuName = "Ore Empire/Captain Config", order = 21)]
    public sealed class CaptainConfig : ScriptableObject
    {
        [Header("Kaptanın değeri — seviye başına, dereceye göre")]
        [Tooltip("Levazımcı haritayı, topçu hurdayı, lostromo onarım süresini, yazman kartların " +
                 "yönlendirilen payını bu oranla değiştirir. Seviye 10'da sıradan bir kaptan +%40, " +
                 "efsanevi bir kaptan +%180 eder — aradaki fark, kovalamaya değmesinin sebebi.")]
        [SerializeField] private double commonPerLevel = 0.040d;
        [SerializeField] private double rarePerLevel = 0.060d;
        [SerializeField] private double epicPerLevel = 0.090d;
        [SerializeField] private double legendaryPerLevel = 0.130d;
        [SerializeField] private double mythicPerLevel = 0.180d;

        [Header("Lostromo — riskten düşülen puan, seviye başına")]
        [Tooltip("AYRI bir ölçek: risk mutlak yüzde puanıyla ölçülür, yukarıdaki her şey çarpandır. " +
                 "Formenin indirimiyle TOPLANIR. Seviye 10'da 2/3/4/5/6 puan; en iyi ihtimalle " +
                 "formenle birlikte 26 puan eder ve en uzak rotanın 30 puanından geriye 4 kalır. " +
                 "Kasten: riski sıfırlanabilen bir kumar artık karar değildir.")]
        [SerializeField] private double bosunRiskCommon = 0.0020d;
        [SerializeField] private double bosunRiskRare = 0.0030d;
        [SerializeField] private double bosunRiskEpic = 0.0040d;
        [SerializeField] private double bosunRiskLegendary = 0.0050d;
        [SerializeField] private double bosunRiskMythic = 0.0060d;

        [Tooltip("Onarım penceresinden geriye kalabilecek en az oran. Sıfıra indirilebilen bir " +
                 "onarım, bedeli olmayan bir başarısızlıktır — bedelin oturduğu yer rıhtımdır.")]
        [SerializeField] private double minRepairFraction = 0.25d;

        [Header("Seviye — yalnızca kopya, elmas yok")]
        [Tooltip("L'den L+1'e: Taban + Adım x (L-1). 2,4,6,… = bir kaptanı sonuna kadar çıkarmak " +
                 "90 kopya, formen kadrosuyla aynı. Elmas ALINMAZ: sandığın bedeli zaten haritaydı, " +
                 "iki kez ödetmek iki kadroyu aynı cüzdan için yarıştırırdı.")]
        [SerializeField] private int duplicateBase = 2;
        [SerializeField] private int duplicateStep = 2;

        [Tooltip("Yukarıdaki eğrinin dereceye göre çarpanı. NADİR OLAN DAHA AZ KOPYA İSTER — " +
                 "bariz cevabın tersi ve işe yarayan tek cevap. Tek düz eğriyle ölçüldüğünde " +
                 "sıradan bir kaptanı sonuna kadar çıkarmak 15 gün, tek mitik kaptanı 370 gün " +
                 "sürüyordu: çekilişlerin %0,66'sı onu taşıyor ve doksan tanesi on dört bin sandık " +
                 "ediyor. Ulaşılamayan bir tavan, altındaki bütün merdiveni anlamsız gösterir. " +
                 "Bu değerler toplamı 90 / 80 / 55 / 35 / 16 kopyaya indirir.")]
        [SerializeField] private double dupScaleCommon = 1.00d;
        [SerializeField] private double dupScaleRare = 0.89d;
        [SerializeField] private double dupScaleEpic = 0.61d;
        [SerializeField] private double dupScaleLegendary = 0.39d;
        [SerializeField] private double dupScaleMythic = 0.17d;

        [Header("Sandık — dereceler")]
        [Tooltip("Ağırlıklar. Toplamlarının 1 olması gerekmez; kadroda kimsenin taşımadığı bir " +
                 "derece kendiliğinden düşer.")]
        [SerializeField] private double commonWeight = 0.600d;
        [SerializeField] private double rareWeight = 0.260d;
        [SerializeField] private double epicWeight = 0.105d;
        [SerializeField] private double legendaryWeight = 0.030d;
        [SerializeField] private double mythicWeight = 0.005d;

        [Header("Sandık — teselli sayaçları")]
        [Tooltip("Kaç çekilişte bir Epic ve üstü garanti edilir. 10, onlu açılışın içinde her zaman " +
                 "bir Epic olması demek — onlu açılışın var oluş sebebi bu.")]
        [SerializeField] private int epicPity = 10;

        [Tooltip("Efsanevi için uzun sayaç, ve ondan önce ağırlığın tırmanmaya başladığı yer.")]
        [SerializeField] private int legendaryPity = 70;
        [SerializeField] private int softPityStart = 45;
        [SerializeField] private double softPityStep = 0.010d;

        [Header("Sandık — fiyat (harita)")]
        [SerializeField] private long chartCost = 100L;
        [Tooltip("Toplu açılışın adedi ve fiyatı. Adet başına ucuz olmalı, yoksa kimse basmaz.")]
        [SerializeField] private int bulkCount = 10;
        [SerializeField] private long bulkChartCost = 900L;

        public Game.Core.Captains.Tuning ToTuning() => new Game.Core.Captains.Tuning
        {
            CommonPerLevel     = commonPerLevel,
            RarePerLevel       = rarePerLevel,
            EpicPerLevel       = epicPerLevel,
            LegendaryPerLevel  = legendaryPerLevel,
            MythicPerLevel     = mythicPerLevel,
            BosunRiskCommon    = bosunRiskCommon,
            BosunRiskRare      = bosunRiskRare,
            BosunRiskEpic      = bosunRiskEpic,
            BosunRiskLegendary = bosunRiskLegendary,
            BosunRiskMythic    = bosunRiskMythic,
            MinRepairFraction  = minRepairFraction,
            DuplicateBase      = duplicateBase,
            DuplicateStep      = duplicateStep,
            DupScaleCommon     = dupScaleCommon,
            DupScaleRare       = dupScaleRare,
            DupScaleEpic       = dupScaleEpic,
            DupScaleLegendary  = dupScaleLegendary,
            DupScaleMythic     = dupScaleMythic,
        };

        public Game.Core.CaptainCrate.Tuning ToCrateTuning() => new Game.Core.CaptainCrate.Tuning
        {
            CommonWeight    = commonWeight,
            RareWeight      = rareWeight,
            EpicWeight      = epicWeight,
            LegendaryWeight = legendaryWeight,
            MythicWeight    = mythicWeight,
            EpicPity        = epicPity,
            LegendaryPity   = legendaryPity,
            SoftPityStart   = softPityStart,
            SoftPityStep    = softPityStep,
            ChartCost       = chartCost,
            BulkCount       = bulkCount,
            BulkChartCost   = bulkChartCost,
        };
    }
}
