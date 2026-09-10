using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Pet tuning and art. Every number here is a default in <see cref="Game.Core.Pets.Tuning"/> or
    /// <see cref="Game.Core.PetChest.Tuning"/> made editable, plus the slot-unlock gates and the art —
    /// one asset for all of it, the same call <see cref="CardCollectionConfig"/> makes for its own
    /// feature and for the same reason: what a pet is worth, how often one is found and when a slot
    /// opens are one balance surface.
    /// Create via: Assets &gt; Create &gt; Ore Empire &gt; Pet Config.
    ///
    /// The game runs without this asset, falling back to every Default below — exactly as it does for
    /// the captain roster and the card collection. Pets will simply have no faces.
    /// </summary>
    [CreateAssetMenu(fileName = "PetConfig", menuName = "Ore Empire/Pet Config", order = 25)]
    public sealed class PetConfig : ScriptableObject
    {
        // ------------------------------------------------------------------ etki tablosu
        [Header("Yıldız başına etki — etki başına 5 nadirlik")]
        [Tooltip("Etki sırasıyla düz dizi: Kaçınma (papağan), Atış Hızı (maymun), Sersemletme " +
                 "(ahtapot), Çalma (gemi faresi), Savunma (yengeç), Gövde (kaplumbağa). Her satır " +
                 "Sıradan'dan Mitik'e. Boş bırakılırsa kod içindeki tablo kullanılır.")]
        [SerializeField]
        private double[] effectPerRarity =
        {
            0.0011d, 0.0023d, 0.0034d, 0.0056d, 0.0090d,   // Kaçınma
            0.0011d, 0.0023d, 0.0034d, 0.0056d, 0.0090d,   // Atış Hızı
            0.0009d, 0.0018d, 0.0026d, 0.0044d, 0.0070d,   // Sersemletme
            0.0009d, 0.0018d, 0.0026d, 0.0044d, 0.0070d,   // Çalma
            0.2000d, 0.4000d, 0.6000d, 1.0000d, 1.6000d,   // Savunma
            1.0000d, 2.0000d, 3.0000d, 5.0000d, 8.0000d,   // Gövde
        };

        // ------------------------------------------------------------------ sandık
        [Header("Sandık ihtimalleri — göreli ağırlık")]
        [Tooltip("Bir sandık bir evcil hayvan verir. Her nadirlik altı türün hepsini taşır, bu " +
                 "yüzden (kart paketinin tersine) burada 'kimse taşımıyor' diye sıfırlanan bir " +
                 "satır yoktur.")]
        [SerializeField] private double commonWeight = 0.600d;
        [SerializeField] private double rareWeight = 0.260d;
        [SerializeField] private double epicWeight = 0.105d;
        [SerializeField] private double legendaryWeight = 0.030d;
        [SerializeField] private double mythicWeight = 0.005d;

        [Header("Merhamet sayaçları")]
        [SerializeField, Min(0)] private int epicPity = 10;
        [SerializeField, Min(0)] private int legendaryPity = 70;
        [SerializeField, Min(0)] private int softPityStart = 45;
        [SerializeField] private double softPityStep = 0.010d;

        [Header("Fiyat — İnci")]
        [Tooltip("Tek sandık maliyeti ve toplu açmanın sayısı/maliyeti. Toplu açma her zaman tek " +
                 "tek açmaktan ucuzdur.")]
        [SerializeField, Min(0)] private long pearlCost = 100L;
        [SerializeField, Min(1)] private int bulkCount = 10;
        [SerializeField, Min(0)] private long bulkPearlCost = 900L;

        // ------------------------------------------------------------------ inci kazancı
        [Header("İnci kazancı (ExpeditionService.RegisterWin)")]
        [Tooltip("Kazanılan her deniz çatışması bu kadar inci öder. Sürülen su derinliğiyle " +
                 "çarpılır (0-indeksli tier + 1), haritaların ve hurdanın zaten ölçeklendiği kural.")]
        [SerializeField, Min(0)] private long pearlsPerWin = 2L;

        // ------------------------------------------------------------------ yuva kilitleri
        [Header("Yuva kilitleri — kazanılan deniz çatışması sayısı")]
        [Tooltip("Kaç kazanılmış deniz çatışması bir yuvayı açar. İlk hücre her zaman 0'dır (ilk " +
                 "yuva en baştan açık). Dizinin kısa kalması kalan yuvaları asla açmaz, çökme " +
                 "değil.")]
        [SerializeField] private int[] slotUnlockFightsWon = { 0, 10, 25 };

        // ------------------------------------------------------------------- görsel
        [Header("Görseller")]
        [Tooltip("Nadirlik rengi: Sıradan, Nadir, Destansı, Efsanevi, Mitik. Kaptan kadrosuyla aynı " +
                 "palet — bir nadirlik her koleksiyonda aynı renk olmalı.")]
        [SerializeField]
        private Color[] rarityTint =
        {
            new Color(0.48f, 0.54f, 0.62f, 1f),   // Sıradan
            new Color(0.26f, 0.60f, 0.92f, 1f),   // Nadir
            new Color(0.62f, 0.38f, 0.92f, 1f),   // Destansı
            new Color(0.96f, 0.66f, 0.18f, 1f),   // Efsanevi
            new Color(0.94f, 0.28f, 0.42f, 1f),   // Mitik
        };

        [Tooltip("Tür portreleri. Tür kimliğiyle eşleşir (Game.Core.Pets.Roster[i].Id) — katalog " +
                 "sırası değişse bile doğru kalsın diye sırayla değil kimlikle bağlanır.")]
        [SerializeField] private SpeciesArt[] speciesArt = Array.Empty<SpeciesArt>();

        [SerializeField] private Sprite chestIcon;
        [SerializeField] private Sprite pearlIcon;

        /// <summary>One species' portrait, bound by id so reordering the roster cannot silently put
        /// the wrong picture on a pet.</summary>
        [Serializable]
        public struct SpeciesArt
        {
            public string speciesId;
            public Sprite portrait;
        }

        // ------------------------------------------------------------------ tuning
        public Game.Core.Pets.Tuning ToTuning() => new Game.Core.Pets.Tuning
        {
            EffectPerRarity = effectPerRarity,
        };

        public Game.Core.PetChest.Tuning ToChestTuning() => new Game.Core.PetChest.Tuning
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
            PearlCost       = pearlCost,
            BulkCount       = bulkCount,
            BulkPearlCost   = bulkPearlCost,
        };

        public long PearlsPerWin => pearlsPerWin < 0L ? 0L : pearlsPerWin;

        /// <summary>Won fights needed to unlock a slot. 0 for slot 0 (always open) and for a slot the
        /// array is too short to name — never negative, never unreachable by a missing cell.</summary>
        public int SlotUnlockFightsWon(int slot)
        {
            if (slot <= 0) return 0;
            if (slotUnlockFightsWon == null || slot >= slotUnlockFightsWon.Length) return 0;
            int n = slotUnlockFightsWon[slot];
            return n < 0 ? 0 : n;
        }

        // ------------------------------------------------------------------ görsel
        public Color[] RarityTint => rarityTint;
        public Sprite ChestIcon => chestIcon;
        public Sprite PearlIcon => pearlIcon;

        // Built on first ask rather than in OnEnable, the same lazy-dictionary trick
        // CardCollectionConfig uses; cleared by OnValidate so an Inspector edit lands at once.
        private Dictionary<string, Sprite> _portraits;

        /// <summary>A species' portrait, or null for one nobody has wired yet.</summary>
        public Sprite PortraitOf(string speciesId)
        {
            if (string.IsNullOrEmpty(speciesId)) return null;
            if (_portraits == null)
            {
                _portraits = new Dictionary<string, Sprite>(
                    speciesArt != null ? speciesArt.Length : 0, StringComparer.Ordinal);
                if (speciesArt != null)
                    for (int i = 0; i < speciesArt.Length; i++)
                        if (!string.IsNullOrEmpty(speciesArt[i].speciesId))
                            _portraits[speciesArt[i].speciesId] = speciesArt[i].portrait;
            }
            return _portraits.TryGetValue(speciesId, out Sprite portrait) ? portrait : null;
        }

        private void OnValidate()
        {
            _portraits = null;
        }
    }
}
