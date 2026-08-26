using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Chapter tuning. Every value here is a default in <see cref="Game.Core.Chapters.Tuning"/> made
    /// editable, so the rules stay in one testable place and the pacing stays in the Inspector.
    /// Create the asset via: Assets &gt; Create &gt; Ore Empire &gt; Chapter Config.
    ///
    /// The game runs without this asset — GameBootstrap falls back to
    /// <see cref="Game.Core.Chapters.Tuning.Default"/>, the same way it does for the foreman roster
    /// and the dock. The asset is how the numbers get tuned, not how the feature turns on.
    /// </summary>
    [CreateAssetMenu(fileName = "ChapterConfig", menuName = "Ore Empire/Chapter Config", order = 20)]
    public sealed class ChapterConfig : ScriptableObject
    {
        [Header("İLK DUMAN — zincir görünür şekilde işlemeye başladığında")]
        [Tooltip("Adada satın alınmış toplam eksen seviyesi. Adanın ilk birkaç dakikasına denk " +
                 "gelmeli: hedef, oyuncunun zaten yapacağı şeyi adlandırmak, ona iş çıkarmak değil.")]
        [SerializeField] private int firstSmokeLevels = 10;

        [Header("TESİSLER — adanın görüntüsünü değiştiren ilk binalar")]
        [Tooltip("Dikilmiş hayalet bina sayısı. Varsayılan 3, onun en ucuz üçü: ikinci ocak, " +
                 "ikinci dökümhane, ticaret karakolu.")]
        [SerializeField] private int worksUnlocks = 3;

        [Header("TAM YOL — bölümün kapanışı")]
        [Tooltip("İki koşul birden aranır. Buradaki eşikler her adada AYNIDIR: para ve külçe " +
                 "cevher kademesi başına 3.2 kat şişer, sayılar şişmez. Görev sisteminin " +
                 "günlükleri neden sayı saydığıyla aynı gerekçe.")]
        [SerializeField] private int fullSteamLevels = 200;
        [SerializeField] private int fullSteamUnlocks = 8;

        [Header("Ödül — elmas")]
        [Tooltip("Bir aşamanın ödemesi: Taban + Adım x bölüm sırası. Sonraki bölümler daha çok " +
                 "öder çünkü daha geç gelinir, daha zor oldukları için değil. " +
                 "NAKİT ASLA ÖDENMEZ: nakdin tek musluğu tezgâhtır (Docs/VOYAGES.md R1).")]
        [SerializeField] private long gemsBase = 40L;
        [SerializeField] private long gemsStep = 15L;

        [Header("Ödül — formen kartı")]
        [Tooltip("Aynı biçim, iki bölümde bir artar. KARAYA ÇIKIŞ aşaması kart ödemez: " +
                 "ada satın alındığı anda tetiklenir ve zaten para ödenmiş bir işlem için " +
                 "verilen kart ödül değil, para üstü gibi okunur.")]
        [SerializeField] private int cardsBase = 1;
        [SerializeField] private int cardsStep = 1;

        public Game.Core.Chapters.Tuning ToTuning() => new Game.Core.Chapters.Tuning
        {
            FirstSmokeLevels = firstSmokeLevels,
            WorksUnlocks     = worksUnlocks,
            FullSteamLevels  = fullSteamLevels,
            FullSteamUnlocks = fullSteamUnlocks,
            GemsBase         = gemsBase,
            GemsStep         = gemsStep,
            CardsBase        = cardsBase,
            CardsStep        = cardsStep,
        };
    }
}
