using System;
using Game.Core;
using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Shop contract tuning: an offer every interval, three sizes, and what each pays when the last item is
    /// delivered. Nothing is paid at accept. See <see cref="ShopContract"/> for how the numbers are used.
    /// </summary>
    [CreateAssetMenu(fileName = "ShopContractConfig", menuName = "Ore Empire/Shop Contract Config", order = 13)]
    public sealed class ShopContractConfig : ScriptableObject
    {
        [Serializable]
        private sealed class SizeRow
        {
            [Tooltip("Kontrat müşterisi normal müşterilerle sırayla hizmet alırken beklenen süre, dakika.")]
            [Min(0.1f)] public double minutes = 10d;
            [Tooltip("Bu boyutun çıkma ağırlığı. Üç boyutun ağırlıkları oranlanır.")]
            [Min(0.01f)] public double weight = 40d;
            [Tooltip("Normal satışın üstüne verilen prim. 0,10 = %10.")]
            [Min(0f)] public double premium = 0.10d;
            [Tooltip("Kontrat tamamlanınca verilen elmas.")]
            [Min(0)] public long gems = 1;
            [Tooltip("Kontrat tamamlanınca verilen ustabaşı kartı; en geride kalan ustaya gider.")]
            [Min(0)] public int foremanCards = 1;

            public SizeRow() { }

            public SizeRow(double minutes, double weight, double premium, long gems, int foremanCards)
            {
                this.minutes = minutes;
                this.weight = weight;
                this.premium = premium;
                this.gems = gems;
                this.foremanCards = foremanCards;
            }

            public ShopContract.Size Build() => new ShopContract.Size
            {
                Minutes = minutes,
                Weight = weight,
                Premium = premium,
                Gems = gems,
                ForemanCards = foremanCards
            };
        }

        [Tooltip("Yeni kontrat teklifi kaç saniyede bir gelir. Kabul edilmeyen teklifin yerini yenisi alır.")]
        [SerializeField, Min(60f)] private double intervalSeconds = 1800d;
        [Tooltip("Kontrat müşterisinin tezgâhın ürettiğinden aldığı pay. 0,5 = normal müşterilerle 1:1 sıra.")]
        [SerializeField, Range(0.05f, 1f)] private double contractShare = 0.5d;
        [SerializeField, Min(1)] private int minQuantity = 5;
        [SerializeField, Min(1)] private int maxQuantity = 1000000;
        [SerializeField] private SizeRow small = new SizeRow(10d, 40d, 0.10d, 1L, 1);
        [SerializeField] private SizeRow medium = new SizeRow(20d, 40d, 0.15d, 2L, 1);
        [SerializeField] private SizeRow large = new SizeRow(40d, 20d, 0.20d, 3L, 2);

        [Header("Görseller")]
        [Tooltip("Ürün simgeleri, dükkân sırasıyla: kazma, kask, fener, çanta. Boş olan simgesiz gösterilir.")]
        [SerializeField] private Sprite[] productIcons = new Sprite[MiningShopCampaign.ProductCount];

        public Sprite ProductIcon(int productIndex)
            => productIcons != null && productIndex >= 0 && productIndex < productIcons.Length ? productIcons[productIndex] : null;

        public ShopContract.Tuning ToTuning() => new ShopContract.Tuning
        {
            IntervalSeconds = intervalSeconds,
            ContractShare = contractShare,
            MinQuantity = minQuantity,
            MaxQuantity = maxQuantity,
            Sizes = new[] { small.Build(), medium.Build(), large.Build() }
        };
    }
}
