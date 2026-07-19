using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Sells ore out of storage each tick and pays the player. Sell rate scales with level; per-unit
    /// value comes from the ore tier (GDD §3, §4). Cash is BigDouble.
    /// </summary>
    public sealed class Market : MonoBehaviour, IUpgradable
    {
        [SerializeField] private StationConfig config;
        [SerializeField] private StorageYard storage;
        [SerializeField] private OreTier oreTier;

        public int Level { get; private set; }

        private GameClock _clock;
        private WalletService _wallet;

        public event System.Action<BigDouble> Sold; // revenue per sale, for floating text

        private void Start()
        {
            _clock = ServiceLocator.Get<GameClock>();
            _wallet = ServiceLocator.Get<WalletService>();
            if (_clock != null) _clock.OnTick += OnTick;
        }

        private void OnDestroy()
        {
            if (_clock != null) _clock.OnTick -= OnTick;
        }

        private void OnTick()
        {
            if (storage == null || _wallet == null) return;

            BigDouble sold = storage.Buffer.Remove(new BigDouble(config.RateAtLevel(Level) * _clock.TickInterval));
            if (sold.Mantissa <= 0d) return;

            BigDouble revenue = sold * oreTier.BaseValue;
            _wallet.AddCash(revenue);
            Sold?.Invoke(revenue);
        }

        public string Label => config.DisplayName;

        public BigDouble UpgradeCost(EconomyService economy) => economy.UpgradeCost(config.BaseUpgradeCost, Level);

        public void ApplyLevel(int level) => Level = level;
    }
}
