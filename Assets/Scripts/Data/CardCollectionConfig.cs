using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Card collection tuning and art. Every number here is a default in
    /// <see cref="Game.Core.CardCollection.Tuning"/> or
    /// <see cref="Game.Core.CardCollectionPack.Tuning"/> made editable — one asset for both, because
    /// what a card is worth and how often it is found are one balance surface, and splitting them
    /// across two files would only let them drift apart. The same call
    /// <see cref="CaptainConfig"/> makes.
    /// Create via: Assets &gt; Create &gt; Ore Empire &gt; Card Collection Config.
    ///
    /// The game runs without this asset, falling back to those two Defaults — exactly as it does for
    /// the foreman roster, the crate, the bench and the sea. Cards will simply have no faces.
    ///
    /// THE CATALOGUE IS NOT HERE. Which cards exist, which set each belongs to, its rarity and what
    /// kind of thing it does all live in <see cref="Game.Core.CardCollectionCatalogue"/>, because
    /// those strings are SAVE KEYS and an Inspector field nobody meant to touch is an orphaned
    /// collection. What is here is what genuinely cannot be a literal: the art, and the numbers a
    /// designer is meant to move.
    /// </summary>
    [CreateAssetMenu(fileName = "CardCollectionConfig", menuName = "Ore Empire/Card Collection Config", order = 24)]
    public sealed class CardCollectionConfig : ScriptableObject
    {
        // ------------------------------------------------------------------ paket
        [Header("Paket ihtimalleri — göreli ağırlık")]
        [Tooltip("Bir paket bir kart verir. Ağırlıklar görelidir ve yalnızca KART TAŞIYAN " +
                 "nadirlikler üzerinden normalize edilir; kataloğunda karşılığı olmayan bir " +
                 "nadirlik çekilemez. Mitik bu yüzden ağırlığı olduğu hâlde lansmanda çıkmaz.")]
        [SerializeField] private double commonWeight = 0.520d;
        [SerializeField] private double rareWeight = 0.280d;
        [SerializeField] private double epicWeight = 0.140d;
        [SerializeField] private double legendaryWeight = 0.055d;
        [SerializeField] private double mythicWeight = 0.005d;

        [Header("Merhamet sayaçları")]
        [Tooltip("Destansı gelmeden açılabilecek en fazla paket. 0 garantiyi kapatır. " +
                 "15: Destansı zaten dört pakette bir geldiği için bu bir takvim değil, " +
                 "şanssızlığın dış sınırı.")]
        [SerializeField, Min(0)] private int epicPity = 15;

        [Tooltip("Efsanevi gelmeden açılabilecek en fazla paket. 50 paket, günde bir paketle " +
                 "yaklaşık yedi hafta — ilk set tamamlamanın ortancasından kasten uzun. " +
                 "Garanti, oyunun şekli değil zemini olmalı.")]
        [SerializeField, Min(0)] private int legendaryPity = 50;

        [Tooltip("Efsanevi ağırlığının tırmanmaya başladığı kuru paket sayısı ve her paket için " +
                 "eklenen ağırlık. 35'ten sonra paket başına +0.015: kuru serilerin çoğu sert " +
                 "garantiye varmadan gerçek bir zarla biter.")]
        [SerializeField, Min(0)] private int softPityStart = 35;
        [SerializeField] private double softPityStep = 0.015d;

        // ------------------------------------------------------------- kopya eğrisi
        [Header("Kopya eğrisi — nadirlik başına 4 basamak (1→2, 2→3, 3→4, 4→5)")]
        [Tooltip("Nadirlik sırasıyla düz dizi: Sıradan, Nadir, Destansı, Efsanevi, Mitik. " +
                 "NADİR OLAN DAHA AZ KOPYA İSTER — Captains.cs'in ölçerek seçtiği ters eğri: " +
                 "nadirlik kartı BULMANIN zorluğudur, üstüne bir de pahalı olması tavanı " +
                 "ulaşılmaz yapar. Boş bırakılırsa kod içindeki eğri kullanılır.")]
        [SerializeField]
        private int[] duplicateCurve =
        {
            2, 4, 8, 14,   // Sıradan   — 28
            2, 4, 7, 12,   // Nadir     — 25
            1, 3, 5,  9,   // Destansı  — 18
            1, 2, 4,  7,   // Efsanevi  — 14
            1, 2, 3,  5,   // Mitik     — 11
        };

        // ----------------------------------------------------------------- etkiler
        [Header("Seviye başına etki — etki başına 5 nadirlik")]
        [Tooltip("Etki sırasıyla düz dizi: Gelir, Zanaat XP, Zanaat puanı ihtimali, Deniz " +
                 "hurdası, Deniz haritası. Her satır Sıradan'dan Mitik'e. Bir kartın değeri " +
                 "kart başına yazılmaz — ne yaptığının ve ne kadar nadir olduğunun sonucudur.")]
        [SerializeField]
        private double[] effectPerLevel =
        {
            0.002d, 0.004d, 0.006d, 0.010d, 0.016d,   // Gelir
            0.010d, 0.020d, 0.030d, 0.045d, 0.065d,   // Zanaat XP
            0.002d, 0.004d, 0.006d, 0.010d, 0.015d,   // Zanaat puanı ihtimali
            0.006d, 0.010d, 0.016d, 0.024d, 0.036d,   // Deniz hurdası
            0.006d, 0.010d, 0.016d, 0.024d, 0.036d,   // Deniz haritası
        };

        [Header("Tavanlar — koleksiyonun bir etkiye ekleyebileceği en fazla değer")]
        [Tooltip("Sırası: Gelir, Zanaat XP, Zanaat puanı ihtimali, Deniz hurdası, Deniz haritası. " +
                 "HEPSİ lansman kataloğunun ulaşabileceğinin ÜSTÜNDEDİR ve öyle kalmalıdır: " +
                 "yayımlanmış bir değeri kırpan tavan, dengeyi kuran şeyin kendisi olur. " +
                 "Bunlar sonradan eklenen bir setin ne kadar birikebileceğini sınırlar.")]
        [SerializeField]
        private double[] effectCaps = { 0.30d, 0.75d, 0.20d, 0.50d, 0.40d };

        [Tooltip("Zanaat puanı düşürme ihtimalinin görebileceği en yüksek değer — taban ve " +
                 "koleksiyon birlikte. Ayrı bir sınır: yukarıdaki tavan koleksiyonun katkısını, " +
                 "bu ise zarın atıldığı sayıyı sınırlar. 1.0'a ulaşabilen bir düşme ihtimali " +
                 "artık bir ihtimal değildir.")]
        [SerializeField] private double craftPointChanceCeiling = 0.40d;

        [Header("Tavan yapmış kartın fazla kopyası — nadirliğe göre elmas")]
        [Tooltip("Kullanılamayan bir çekilişin karşılığı. Görünür ama işe yaramaz bırakmak, " +
                 "değeri sessizce çöpe atan tek seçenektir; tavan yapmış kartı havuzdan çıkarmak " +
                 "ise yayımlanan ihtimalleri yalan yapar.")]
        [SerializeField] private long[] overflowGems = { 5L, 10L, 20L, 40L, 75L };

        // -------------------------------------------------------------- günlük paket
        [Header("Günlük paket")]
        [Tooltip("Her UTC gün bir bedava paket. Alınınca AÇILMAZ, açılmamış paket olarak bankaya " +
                 "yazılır — açma töreni oyuncunun kendi anıdır. En fazla bir tane birikir.")]
        [SerializeField, Min(0)] private int dailyPacks = 1;

        // ------------------------------------------------------------------- görsel
        [Header("Görseller")]
        [Tooltip("Nadirlik rengi: Sıradan, Nadir, Destansı, Efsanevi, Mitik. Renk hiçbir zaman " +
                 "tek ipucu değildir — kartın üstünde yazan kelime kaptan.derece.* ile aynıdır.")]
        [SerializeField]
        private Color[] rarityTint =
        {
            new Color(0.48f, 0.54f, 0.62f, 1f),   // Sıradan
            new Color(0.26f, 0.60f, 0.92f, 1f),   // Nadir
            new Color(0.62f, 0.40f, 0.86f, 1f),   // Destansı
            new Color(0.96f, 0.66f, 0.18f, 1f),   // Efsanevi
            new Color(0.90f, 0.32f, 0.46f, 1f),   // Mitik
        };

        [Tooltip("Kart yüzleri. Kart kimliğiyle eşleşir — katalog sırası değişse bile doğru " +
                 "kalsın diye sırayla değil kimlikle bağlanır. Eksik bir satır yüzsüz bir kart " +
                 "demektir, çökme değil.")]
        [SerializeField] private CardArt[] cardArt = Array.Empty<CardArt>();

        [Tooltip("Set afişleri. Aynı kural: sırayla değil set kimliğiyle eşleşir.")]
        [SerializeField] private SetArt[] setArt = Array.Empty<SetArt>();

        [Tooltip("Kart çerçevesi, nadirlik başına: Sıradan, Nadir, Destansı, Efsanevi, Mitik. " +
                 "Kartın üstüne serilir; boş bırakılan bir nadirlik çerçevesiz çizilir.")]
        [SerializeField] private Sprite[] rarityFrame = new Sprite[5];

        [Tooltip("Paket kartının simgesi. Boş bırakılırsa paket kartı simgesiz çizilir.")]
        [SerializeField] private Sprite packIcon;
        [Tooltip("Daha Fazla sayfasındaki koleksiyon satırının simgesi.")]
        [SerializeField] private Sprite collectionIcon;

        /// <summary>One card's face, bound by id rather than by position so reordering the
        /// catalogue cannot silently put the wrong picture on a card.</summary>
        [Serializable]
        public struct CardArt
        {
            public string cardId;
            public Sprite face;
        }

        /// <summary>One set's banner, bound by id for the same reason.</summary>
        [Serializable]
        public struct SetArt
        {
            public string setId;
            public Sprite banner;
        }

        // ------------------------------------------------------------------ tuning
        public Game.Core.CardCollection.Tuning ToTuning() => new Game.Core.CardCollection.Tuning
        {
            DuplicateCurve          = duplicateCurve,
            EffectPerLevel          = effectPerLevel,
            OverflowGems            = overflowGems,
            EffectCaps              = effectCaps,
            CraftPointChanceCeiling = craftPointChanceCeiling,
        };

        public Game.Core.CardCollectionPack.Tuning ToPackTuning()
            => new Game.Core.CardCollectionPack.Tuning
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
            };

        public int DailyPacks => dailyPacks < 0 ? 0 : dailyPacks;

        // ------------------------------------------------------------------ görsel
        public Color[] RarityTint => rarityTint;
        public Sprite PackIcon => packIcon;
        public Sprite CollectionIcon => collectionIcon;

        /// <summary>The frame drawn over a card of this rarity, or null for one nobody has wired.</summary>
        public Sprite FrameOf(Game.Core.RosterCardState.Rarity rarity)
        {
            int r = (int)rarity;
            return rarityFrame != null && r >= 0 && r < rarityFrame.Length ? rarityFrame[r] : null;
        }

        // Built on first ask rather than in OnEnable: an asset edited in the Inspector during play
        // would otherwise keep serving the art it had at load, and rebuilding a 24-entry dictionary
        // is not a cost worth protecting against. Cleared by OnValidate so an edit lands at once.
        private Dictionary<string, Sprite> _faces;
        private Dictionary<string, Sprite> _banners;

        /// <summary>A card's face, or null for one nobody has wired yet.</summary>
        public Sprite FaceOf(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return null;
            if (_faces == null)
            {
                _faces = new Dictionary<string, Sprite>(
                    cardArt != null ? cardArt.Length : 0, StringComparer.Ordinal);
                if (cardArt != null)
                    for (int i = 0; i < cardArt.Length; i++)
                        if (!string.IsNullOrEmpty(cardArt[i].cardId))
                            _faces[cardArt[i].cardId] = cardArt[i].face;
            }
            return _faces.TryGetValue(cardId, out Sprite face) ? face : null;
        }

        /// <summary>A set's banner, or null for one nobody has wired yet.</summary>
        public Sprite BannerOf(string setId)
        {
            if (string.IsNullOrEmpty(setId)) return null;
            if (_banners == null)
            {
                _banners = new Dictionary<string, Sprite>(
                    setArt != null ? setArt.Length : 0, StringComparer.Ordinal);
                if (setArt != null)
                    for (int i = 0; i < setArt.Length; i++)
                        if (!string.IsNullOrEmpty(setArt[i].setId))
                            _banners[setArt[i].setId] = setArt[i].banner;
            }
            return _banners.TryGetValue(setId, out Sprite banner) ? banner : null;
        }

        private void OnValidate()
        {
            _faces = null;
            _banners = null;
        }
    }
}
