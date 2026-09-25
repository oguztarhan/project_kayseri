using Game.Core;
using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Shop coin tuning: how many coins a 48-hour cycle holds, the waits between them, and what a coin pays.
    /// See <see cref="ShopCoins"/> for how the numbers are used. A reward whose amount is 0 drops out of the roll
    /// and the rest share it, so setting the gems to 0 removes gem coins without touching anything else.
    /// </summary>
    [CreateAssetMenu(fileName = "ShopCoinConfig", menuName = "Ore Empire/Shop Coin Config", order = 14)]
    public sealed class ShopCoinConfig : ScriptableObject
    {
        [Header("Döngü (48 saat, UTC)")]
        [Tooltip("Bir 48 saatlik döngüde toplanabilecek sikke. Kaçırılan sikke döngüye geri döner, sayılmaz.")]
        [SerializeField, Min(1)] private int coinsPerCycle = 12;
        [Tooltip("Döngünün ilk 24 saatinde toplanabilecek en çok sikke; ikinci güne hep sikke kalsın diye.")]
        [SerializeField, Min(1)] private int firstDayCap = 8;
        [Tooltip("Döngünün ilk sikkesinden önce beklenen oyun süresi, saniye (en az / en çok).")]
        [SerializeField, Min(0f)] private double firstSpawnMinSeconds = 45d;
        [SerializeField, Min(0f)] private double firstSpawnMaxSeconds = 90d;
        [Tooltip("Bir sikke toplandıktan ya da kaybolduktan sonra yenisine kadar beklenen oyun süresi, saniye.")]
        [SerializeField, Min(0f)] private double gapMinSeconds = 300d;
        [SerializeField, Min(0f)] private double gapMaxSeconds = 540d;
        [Tooltip("Sikkenin ekranda kaldığı süre, saniye. Dükkân görünmezken durur.")]
        [SerializeField, Min(1f)] private double lifetimeSeconds = 20d;

        [Header("Ödüller (ağırlıklar oranlanır)")]
        [Tooltip("Nakit ödüller en az bu kadar öder.")]
        [SerializeField, Min(0f)] private double cashFloor = 100d;
        [SerializeField, Min(0f)] private double smallCashWeight = 60d;
        [Tooltip("Küçük nakit: kaç dakikalık gelir.")]
        [SerializeField, Min(0f)] private double smallCashMinutes = 2d;
        [SerializeField, Min(0f)] private double mediumCashWeight = 20d;
        [SerializeField, Min(0f)] private double mediumCashMinutes = 5d;
        [SerializeField, Min(0f)] private double boostWeight = 12d;
        [SerializeField, Min(1f)] private double boostMultiplier = 2d;
        [SerializeField, Min(0f)] private double boostSeconds = 90d;
        [Tooltip("Elmas sikkesinin ağırlığı. 0 = elmas yok.")]
        [SerializeField, Min(0f)] private double gemWeight = 5d;
        [Tooltip("Elmas sikkesinin verdiği elmas. 0 = elmas yok.")]
        [SerializeField, Min(0)] private long gems = 5L;
        [SerializeField, Min(0f)] private double jackpotWeight = 3d;
        [Tooltip("Büyük ikramiye: kaç dakikalık gelir.")]
        [SerializeField, Min(0f)] private double jackpotMinutes = 15d;

        public ShopCoins.Tuning ToTuning() => new ShopCoins.Tuning
        {
            CoinsPerCycle = coinsPerCycle,
            FirstDayCap = firstDayCap,
            FirstSpawnMinSeconds = firstSpawnMinSeconds,
            FirstSpawnMaxSeconds = firstSpawnMaxSeconds,
            GapMinSeconds = gapMinSeconds,
            GapMaxSeconds = gapMaxSeconds,
            LifetimeSeconds = lifetimeSeconds,
            CashFloor = cashFloor,
            SmallCashWeight = smallCashWeight,
            SmallCashMinutes = smallCashMinutes,
            MediumCashWeight = mediumCashWeight,
            MediumCashMinutes = mediumCashMinutes,
            BoostWeight = boostWeight,
            BoostMultiplier = boostMultiplier,
            BoostSeconds = boostSeconds,
            GemWeight = gemWeight,
            Gems = gems,
            JackpotWeight = jackpotWeight,
            JackpotMinutes = jackpotMinutes
        };
    }
}
