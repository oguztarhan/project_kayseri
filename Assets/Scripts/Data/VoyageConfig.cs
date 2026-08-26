using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Voyage tuning. Every value here is a default in <see cref="Game.Core.Voyages.Tuning"/> made
    /// editable, so the maths stays in one testable place and the balance stays in the Inspector.
    /// Create the asset via: Assets &gt; Create &gt; Ore Empire &gt; Voyage Config.
    ///
    /// The game runs without this asset — GameBootstrap falls back to
    /// <see cref="Game.Core.Voyages.Tuning.Default"/>, the same way it does for the foreman roster.
    /// The asset is how the numbers get tuned, not how the feature turns on.
    /// </summary>
    [CreateAssetMenu(fileName = "VoyageConfig", menuName = "Ore Empire/Voyage Config", order = 19)]
    public sealed class VoyageConfig : ScriptableObject
    {
        [Header("Yükleme — adanın üretiminden ne kadarı gemiye gider")]
        [Tooltip("0.35 = adanın teslimatının %35'i tezgâh yerine ambara akar. " +
                 "Tüm rıhtımlar toplamı 0.5'i geçmemeli, yoksa sefer açmak oyunu kapatmak gibi hissettirir.")]
        [SerializeField] private double divertShare = 0.35d;

        [Tooltip("Ambar boyu, adanın kendi teslimat hızının kaç dakikası kadar. Gemi seviyesi 0 için.")]
        [SerializeField] private double holdMinutesBase = 3d;

        [Tooltip("Her Ambar seviyesinin eklediği dakika.")]
        [SerializeField] private double holdMinutesPerLevel = 0.6d;

        [Header("Sefer süresi")]
        [Tooltip("1. kademe seferin süresi (dakika). Diğer kademeler bunun katıdır.")]
        [SerializeField] private double baseVoyageMinutes = 35d;

        [Tooltip("Her Hız seviyesinin kısalttığı oran. 0.08 = seviye başına %8 daha hızlı.")]
        [SerializeField] private double speedPerLevel = 0.04d;

        [Header("Ödül")]
        [Tooltip("Dolu bir 1. kademe ambarının getirdiği kart sayısı. Diğer kademeler bunun katıdır.")]
        [SerializeField] private double cardRate = 1d;

        [Tooltip("Her Mürettebat seviyesinin eklediği oran. 0.10 = seviye başına %10 daha çok kart.")]
        [SerializeField] private double crewPerLevel = 0.05d;

        [Tooltip("Bir gemi en az bu doluluk oranıyla denize açılabilir. Altı kabul edilmez — " +
                 "neredeyse boş bir ambar için tam sefer beklemek tuzaktır.")]
        [SerializeField] private double minLaunchFraction = 0.25d;

        [Header("Risk")]
        [Tooltip("Gemide bir formen varsa, her seviyesi riskten bu kadar düşer. " +
                 "0.02 = seviye başına %2. Seviye 10'da 20 puan — uzak rotayı rahatlatır ama bedavaya çevirmez.")]
        [SerializeField] private double foremanRiskPerLevel = 0.02d;

        [Tooltip("Aksayan bir seferin yine de getirdiği ödül oranı. Sıfır olmamalı: " +
                 "harcanmış ambar + beklenmiş süre + hiçbir şey, oyuncunun oyunu bıraktığı sonuçtur.")]
        [SerializeField] private double failPayout = 0.40d;

        [Tooltip("Aksayan seferden sonra rıhtımın kullanılamayacağı süre — o rotanın kendi süresinin " +
                 "oranı olarak. 0.25 = seferin dörtte biri. Sabit dakika değil: sabit bir süre kısa " +
                 "rotayı ağır, uzun rotayı hiç cezalandırmıyordu.")]
        [SerializeField] private double repairFraction = 0.25d;

        [Header("Tersane")]
        [Tooltip("Aynı anda yükleme yapan TÜM rıhtımların adanın üretiminden alabileceği en yüksek pay. " +
                 "Rıhtım almak daha çok cevher göndermez — aynı cevheri iki gemiyle gönderir. " +
                 "Rıhtımın satın aldığı şey boşluğu doldurmaktır, akışı artırmak değil.")]
        [SerializeField] private double maxDivertShare = 0.50d;

        [Tooltip("Dolu bir 1. kademe ambarının getirdiği hurda. Diğer kademeler bunun katıdır. " +
                 "Hurda yalnızca gemiye harcanır — kapalı bir döngü, nakit ekonomisine dokunamaz.")]
        [SerializeField] private double salvageRate = 1.5d;

        [Tooltip("Dolu bir 1. kademe ambarının getirdiği harita. Kaptan sandığı bununla açılır. " +
                 "Hurda gibi kapalı bir döngü: denizden gelir, yalnızca kaptanlara gider.")]
        [SerializeField] private double chartRate = 4d;

        [Tooltip("Gemi yükseltmesinin ilk seviyesi bu kadar hurda; her seviye bu oranla büyür.")]
        [SerializeField] private double shipCostBase = 20d;
        [SerializeField] private double shipCostGrowth = 1.45d;

        [Header("Rıhtımlar")]
        [Tooltip("İkinci rıhtım hurdayla, üçüncü ve dördüncü elmasla alınır.")]
        [SerializeField] private long berthSalvageCost = 250L;
        [SerializeField] private long thirdBerthGems = 1200L;
        [SerializeField] private long fourthBerthGems = 3000L;

        [Tooltip("Onarımı elmasla atlamanın bedeli. Garantili başarı ASLA satılmaz — " +
                 "bu, oyunun tek gerçek kararını cüzdan kontrolüne çevirirdi.")]
        [SerializeField] private long repairSkipGems = 120L;

        public Game.Core.Voyages.Tuning ToTuning() => new Game.Core.Voyages.Tuning
        {
            DivertShare         = divertShare,
            HoldMinutesBase     = holdMinutesBase,
            HoldMinutesPerLevel = holdMinutesPerLevel,
            BaseVoyageMinutes   = baseVoyageMinutes,
            SpeedPerLevel       = speedPerLevel,
            CrewPerLevel        = crewPerLevel,
            CardRate            = cardRate,
            MinLaunchFraction   = minLaunchFraction,
            ForemanRiskPerLevel = foremanRiskPerLevel,
            FailPayout          = failPayout,
            RepairFraction      = repairFraction,
            MaxDivertShare      = maxDivertShare,
            SalvageRate         = salvageRate,
            ChartRate           = chartRate,
            ShipCostBase        = shipCostBase,
            ShipCostGrowth      = shipCostGrowth,
            BerthSalvageCost    = berthSalvageCost,
            ThirdBerthGems      = thirdBerthGems,
            FourthBerthGems     = fourthBerthGems,
            RepairSkipGems      = repairSkipGems,
        };
    }
}
