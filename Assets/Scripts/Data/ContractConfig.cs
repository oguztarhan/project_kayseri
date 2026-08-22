using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Port contract tuning (GDD §9): a cargo ship docks, offers three jobs at three difficulties, and
    /// sails once the job is settled. A job is "process N units of ore inside T minutes".
    ///
    /// Targets are not authored as absolute numbers. <c>floorUnits</c> only covers the opening minutes;
    /// after that each offer is sized off what the empire actually smelts per minute, so the same three
    /// cards stay meaningful whether the islands process 200 units a minute or 200 trillion.
    ///
    /// The three tiers are the whole design: EASY asks for less than passive play already delivers over a
    /// long window and pays little, HARD asks for more than passive play can produce over a short one and
    /// pays a lot. NORMAL sits on the line.
    /// </summary>
    [CreateAssetMenu(fileName = "ContractConfig", menuName = "Ore Empire/Contract Config", order = 12)]
    public sealed class ContractConfig : ScriptableObject
    {
        [Header("Hedef")]
        [Tooltip("Açılış dakikalarının tabanı: ada henüz bir şey işlemediyse NORMAL kontrat bu kadar " +
                 "birim ister. Ölçüm dolduktan sonra hedefi işleme hızı belirler, bu değil.")]
        [SerializeField] private double floorUnits = 50d;
        [Tooltip("NORMAL kontratın süresi, dakika. Kolay ve zor bunun katları.")]
        [SerializeField] private float normalMinutes = 10f;

        [Header("Ödül")]
        [Tooltip("NORMAL kontratın ödül tabanı. Gerçek ödül dakikalık gelirden hesaplanır; bu sadece " +
                 "gelirin henüz okunmadığı ilk dakikalar için.")]
        [SerializeField] private double rewardCash = 500d;
        [Tooltip("NORMAL kontratın elması. Kolay/zor bunun katları.")]
        [SerializeField] private long rewardGems = 2;

        [Header("Ustabaşı kartları")]
        [Tooltip("Her tamamlanan kontratın verdiği kart sayısı. Kartlar satın alınamaz, sadece " +
                 "kazanılır — kadroyu ilerleten asıl şey budur.")]
        [SerializeField] private int cardsPerContract = 2;
        [Tooltip("Kaç kontratlık seride bir, kart ödülü 1 artar.")]
        [SerializeField] private int cardsStreakStep = 5;
        [Tooltip("Ödül = dakikalık gelir × kontrat dakikası × bu oran × zorluk çarpanı. 0,45 = NORMAL " +
                 "kontrat, penceresinin ürettiği paranın %45'ini üstüne koyar.")]
        [SerializeField] private double rewardFraction = 0.45d;

        [Header("KOLAY")]
        [Tooltip("İstenen hız, oyuncunun şu anki işleme hızının katı olarak. 0,6 = pasif oynayış zaten yetiyor.")]
        [SerializeField] private float easyRate = 0.6f;
        [SerializeField] private float easyMinutes = 15f;
        [Tooltip("Ödül çarpanı — NORMAL'in katı.")]
        [SerializeField] private float easyPay = 0.5f;
        [SerializeField] private long easyGems = 1;

        [Header("ZOR")]
        [Tooltip("1'in üstü: pasif oynayış yetmez, hızlandırıcı ya da yükseltme ister.")]
        [SerializeField] private float hardRate = 1.6f;
        [SerializeField] private float hardMinutes = 7f;
        [SerializeField] private float hardPay = 2.2f;
        [SerializeField] private long hardGems = 4;

        [Header("Gemi")]
        [Tooltip("Geminin ufuktan iskeleye yanaşması kaç saniye sürsün.")]
        [SerializeField] private float shipArriveSeconds = 14f;
        [Tooltip("İş bittikten sonra geminin ekrandan çıkması kaç saniye sürsün.")]
        [SerializeField] private float shipDepartSeconds = 16f;
        [Tooltip("Gemi gittikten sonra yenisi gelene kadar geçen süre.")]
        [SerializeField] private float shipCooldownSeconds = 60f;

        public double FloorUnits => floorUnits;
        public float NormalMinutes => normalMinutes;
        public double RewardCash => rewardCash;
        public long RewardGems => rewardGems;
        public int CardsPerContract => cardsPerContract;
        public int CardsStreakStep => cardsStreakStep;
        public double RewardFraction => rewardFraction;

        public float EasyRate => easyRate;
        public float EasyMinutes => easyMinutes;
        public float EasyPay => easyPay;
        public long EasyGems => easyGems;

        public float HardRate => hardRate;
        public float HardMinutes => hardMinutes;
        public float HardPay => hardPay;
        public long HardGems => hardGems;

        public float ShipArriveSeconds => shipArriveSeconds;
        public float ShipDepartSeconds => shipDepartSeconds;
        public float ShipCooldownSeconds => shipCooldownSeconds;
    }
}
