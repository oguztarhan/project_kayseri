using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Sea-adventure tuning. Every value is a default in <see cref="Game.Core.SeaCombat.Tuning"/>
    /// made editable. Create via: Assets &gt; Create &gt; Ore Empire &gt; Sea Combat Config. The game
    /// runs without the asset, falling back to the code defaults like every other config here.
    /// </summary>
    [CreateAssetMenu(fileName = "SeaCombatConfig", menuName = "Ore Empire/Sea Combat Config", order = 22)]
    public sealed class SeaCombatConfig : ScriptableObject
    {
        [Header("Enerji — arayışın bedeli")]
        [Tooltip("Havuz ve duvar saati dolumu. Bir arama bir enerji; kapalıyken de dolar. " +
                 "Çatışma gelirini sınırlayan şey budur — asla bir orana dönüşemez.")]
        [SerializeField] private int energyMax = 30;
        [SerializeField] private double energyRegenSeconds = 300d;

        [Header("Tempo (saniye)")]
        [Tooltip("Dürbün taraması ve bulunanın bordaya süzülmesi. Kısa: düğmeye basıldı, cevap şimdi.")]
        [SerializeField] private double searchSeconds = 0.9d;
        [SerializeField] private double approachSeconds = 1.2d;
        [Tooltip("Sıralı atışın nabzı: nişan duraklaması ve güllenin uçuşu. Hasar ÇARPMA anında " +
                 "işlenir — resimle sayı aynı anda konuşur.")]
        [SerializeField] private double turnAimSeconds = 0.55d;
        [SerializeField] private double turnFlightSeconds = 0.45d;

        [Header("Gemi — türetilmiş, saklanan bir blok değil")]
        [Tooltip("Atış başına taban hasar. Mürettebat rayı oran ekler, kaptan kadro değeriyle " +
                 "çarpar (Topçu iki katıyla), takılar düz hasar ekler.")]
        [SerializeField] private double playerShotBase = 18d;
        [SerializeField] private double shotPerCrewLevel = 0.06d;
        [SerializeField] private double gunnerFightBonus = 2d;
        [Tooltip("Cesaret. Biterse püskürtülürüz — enerji gitti, sefer hiçbir şey hissetmez.")]
        [SerializeField] private double baseNerve = 100d;
        [SerializeField] private double nervePerCrewLevel = 8d;
        [Tooltip("Düşmanın ATIŞ başına hasarı = tehdit tablosu x bu.")]
        [SerializeField] private double enemyShotScale = 3.4d;

        [Header("İkincil istatistikler")]
        [Tooltip("Kritik vuruşun çarpanı; yangının kurbanın turlarıyla süresi ve tur başına " +
                 "azami gövde oranı ısırığı.")]
        [SerializeField] private double critMult = 2.0d;
        [SerializeField] private int burnTurns = 3;
        [SerializeField] private double burnFrac = 0.06d;
        [Tooltip("Kaptan rolünün ikincil istatistiği = kadro değeri x bu. Topçu KRİTİK, Levazımcı " +
                 "MANEVRA, Lostromo ONARIM, Yazman YAĞMA.")]
        [SerializeField] private double roleSecFactor = 0.12d;

        [Header("Düşman imzaları — detay kartının uyardığı şey")]
        [SerializeField] private double raiderCrit = 0.22d;
        [SerializeField] private double beastStun = 0.25d;
        [SerializeField] private double fireshipBurn = 0.40d;
        [SerializeField] private double ghostDodge = 0.30d;
        [SerializeField] private double ghostMend = 0.03d;

        [Header("Yetenekler — bekleme TUR sayar")]
        [Tooltip("BORDA: SIRADAKİ atışımız bu çarpanla.")]
        [SerializeField] private double broadsideMult = 2.2d;
        [SerializeField] private int broadsideCdTurns = 3;
        [Tooltip("SİPER: sıradaki İSABET EDEN atış bu çarpanla; ıskalanan atış harcamaz.")]
        [SerializeField] private double braceFactor = 0.35d;
        [SerializeField] private int braceCdTurns = 3;
        [Tooltip("KANCA: düşmanın sıradaki turu hiç olmaz.")]
        [SerializeField] private int hookCdTurns = 4;

        [Header("Ganimet — damla (esas ödül TEÇHİZATTIR)")]
        [Tooltip("Batırma başına, dolu ambarın payı olarak harita ve hurda. Küçük: kaptan " +
                 "döngüsünü besler, ikinci musluk olmaz.")]
        [SerializeField] private double encounterChartShare = 0.12d;
        [SerializeField] private double encounterSalvageShare = 0.12d;

        [Header("Teçhizat düşüşü — derece ağırlıkları")]
        [SerializeField] private double dropCommon = 0.52d;
        [SerializeField] private double dropRare = 0.27d;
        [SerializeField] private double dropEpic = 0.13d;
        [SerializeField] private double dropLegendary = 0.06d;
        [SerializeField] private double dropMythic = 0.02d;
        [Tooltip("Rota kademesi ve DÜRBÜN derecesi, Sıradan üstü her ağırlığı bu oranla kaldırır. " +
                 "Öğütme döngüsü tek cümlede: dövüşebildiğin yerde dövüş, daha iyi dürbün bul, " +
                 "daha uzağa dövüş.")]
        [SerializeField] private double dropTierBonus = 0.35d;
        [SerializeField] private double dropLuckBonus = 0.04d;

        [Header("GÜÇ göstergesi — bir okuma, asla bir kural")]
        [SerializeField] private double powerHullWeight = 0.55d;
        [SerializeField] private double powerShotWeight = 3.2d;
        [SerializeField] private double powerSecWeight = 0.9d;
        [Tooltip("Düşman gücü / bizimki bu oranın üstündeyse TEHLİKELİ, altındakinde KOLAY yazar.")]
        [SerializeField] private double dangerRatio = 1.15d;
        [SerializeField] private double easyRatio = 0.70d;

        public Game.Core.SeaCombat.Tuning ToTuning() => new Game.Core.SeaCombat.Tuning
        {
            EnergyMax             = energyMax,
            EnergyRegenSeconds    = energyRegenSeconds,
            SearchSeconds         = searchSeconds,
            ApproachSeconds       = approachSeconds,
            TurnAimSeconds        = turnAimSeconds,
            TurnFlightSeconds     = turnFlightSeconds,
            PlayerShotBase        = playerShotBase,
            ShotPerCrewLevel      = shotPerCrewLevel,
            GunnerFightBonus      = gunnerFightBonus,
            BaseNerve             = baseNerve,
            NervePerCrewLevel     = nervePerCrewLevel,
            EnemyShotScale        = enemyShotScale,
            CritMult              = critMult,
            BurnTurns             = burnTurns,
            BurnFrac              = burnFrac,
            RoleSecFactor         = roleSecFactor,
            RaiderCrit            = raiderCrit,
            BeastStun             = beastStun,
            FireshipBurn          = fireshipBurn,
            GhostDodge            = ghostDodge,
            GhostMend             = ghostMend,
            BroadsideMult         = broadsideMult,
            BroadsideCdTurns      = broadsideCdTurns,
            BraceFactor           = braceFactor,
            BraceCdTurns          = braceCdTurns,
            HookCdTurns           = hookCdTurns,
            EncounterChartShare   = encounterChartShare,
            EncounterSalvageShare = encounterSalvageShare,
            DropCommon            = dropCommon,
            DropRare              = dropRare,
            DropEpic              = dropEpic,
            DropLegendary         = dropLegendary,
            DropMythic            = dropMythic,
            DropTierBonus         = dropTierBonus,
            DropLuckBonus         = dropLuckBonus,
            PowerHullWeight       = powerHullWeight,
            PowerShotWeight       = powerShotWeight,
            PowerSecWeight        = powerSecWeight,
            DangerRatio           = dangerRatio,
            EasyRatio             = easyRatio,
        };
    }
}
