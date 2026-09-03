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

        [Header("Depo — atölyenin rafı")]
        [Tooltip("Deponun kaç parça tuttuğu. Beşe dört ızgara yirmi eder; büyütmek ızgarayı da " +
                 "büyütür, küçültmek dolu bir depoyu boşaltmaz — fazlası dururken sadece yeni " +
                 "parça girmez.")]
        [SerializeField, Min(0)] private int stashCapacity = Game.Core.GearStash.DefaultCapacity;

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
        [Tooltip("SAVUNMA ve SÜRAT'ın taban değerleri. İkisi de esasen teçhizattan gelir: savunma " +
                 "her gülleyi tıraşlar, hızlı olan taraf ilk atışı yapar.")]
        [SerializeField] private double playerDefBase = 0d;
        [SerializeField] private double playerSpdBase = 10d;
        [Tooltip("Düşmanın ATIŞ başına hasarı = tehdit tablosu x bu.")]
        [SerializeField] private double enemyShotScale = 3.4d;
        [Tooltip("Hiçbir savunma yığını gülleyi tümden yutamaz: isabet eden atış en az TOP'un bu " +
                 "oranı kadar vurur.")]
        [SerializeField] private double minShotFrac = 0.25d;

        [Header("İkincil istatistikler")]
        [Tooltip("Kritik vuruşun çarpanı; yangının kurbanın turlarıyla süresi ve tur başına " +
                 "azami gövde oranı ısırığı.")]
        [SerializeField] private double critMult = 2.0d;
        [SerializeField] private int burnTurns = 3;
        [SerializeField] private double burnFrac = 0.06d;
        [Tooltip("Zehrin süresi ve tur başına ısırığı = ZEHİRLEYENİN topu x bu oran, bulaştığı " +
                 "anda mühürlenir. Yangın kurbanla ölçeklenir, zehir saldırganla — fark bu.")]
        [SerializeField] private int poisonTurns = 4;
        [SerializeField] private double poisonFrac = 0.35d;
        [Tooltip("Kaptan rolünün ikincil istatistiği = kadro değeri x bu. Topçu KRİTİK, Levazımcı " +
                 "MANEVRA, Lostromo ONARIM, Yazman YAĞMA.")]
        [SerializeField] private double roleSecFactor = 0.12d;

        [Header("Düşman imzaları — detay kartının uyardığı şey")]
        [SerializeField] private double raiderCrit = 0.22d;
        [SerializeField] private double beastStun = 0.25d;
        [SerializeField] private double fireshipBurn = 0.40d;
        [SerializeField] private double ghostDodge = 0.30d;
        [SerializeField] private double ghostMend = 0.03d;

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
        [SerializeField] private double powerDefWeight = 2.2d;
        [SerializeField] private double powerSpdWeight = 0.8d;
        [SerializeField] private double powerSecWeight = 0.9d;
        [Tooltip("Düşman gücü / bizimki bu oranın üstündeyse TEHLİKELİ, altındakinde KOLAY yazar.")]
        [SerializeField] private double dangerRatio = 1.15d;
        [SerializeField] private double easyRatio = 0.70d;

        public Game.Core.SeaCombat.Tuning ToTuning() => new Game.Core.SeaCombat.Tuning
        {
            EnergyMax             = energyMax,
            StashCapacity         = stashCapacity,
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
            PlayerDefBase         = playerDefBase,
            PlayerSpdBase         = playerSpdBase,
            EnemyShotScale        = enemyShotScale,
            MinShotFrac           = minShotFrac,
            CritMult              = critMult,
            BurnTurns             = burnTurns,
            BurnFrac              = burnFrac,
            PoisonTurns           = poisonTurns,
            PoisonFrac            = poisonFrac,
            RoleSecFactor         = roleSecFactor,
            RaiderCrit            = raiderCrit,
            BeastStun             = beastStun,
            FireshipBurn          = fireshipBurn,
            GhostDodge            = ghostDodge,
            GhostMend             = ghostMend,
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
            PowerDefWeight        = powerDefWeight,
            PowerSpdWeight        = powerSpdWeight,
            PowerSecWeight        = powerSecWeight,
            DangerRatio           = dangerRatio,
            EasyRatio             = easyRatio,
        };
    }
}
