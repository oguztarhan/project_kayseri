using Game.Core;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Per-scene coordinator for the Main gameplay scene. Finds the stations, applies saved upgrade
    /// levels on load, and routes upgrade purchases through one place so cash-spend, level-up, and
    /// save-write stay in sync (GDD §5). The HUD drives it.
    /// </summary>
    public sealed class GameWorld : MonoBehaviour
    {
        public MineStation Mine { get; private set; }
        public StorageYard Storage { get; private set; }
        public Market Market { get; private set; }
        public Train Train { get; private set; }

        private WalletService _wallet;
        private EconomyService _economy;
        private SaveData _data;

        private void Awake()
        {
            Mine = FindFirstObjectByType<MineStation>();
            Storage = FindFirstObjectByType<StorageYard>();
            Market = FindFirstObjectByType<Market>();
            Train = FindFirstObjectByType<Train>();

            _wallet = ServiceLocator.Get<WalletService>();
            _economy = ServiceLocator.Get<EconomyService>();
            _data = ServiceLocator.Get<SaveData>();
        }

        private void Start()
        {
            if (_data == null) return;
            Mine?.ApplyLevel(_data.stations.mine);
            Storage?.ApplyLevel(_data.stations.storage);
            Market?.ApplyLevel(_data.stations.market);
            Train?.ApplyLevel(_data.stations.train);
        }

        /// <summary>Buy the next level of an upgradable if affordable; keeps the save in sync.</summary>
        public bool TryUpgrade(IUpgradable u)
        {
            if (u == null || _wallet == null || _economy == null) return false;

            BigDouble cost = u.UpgradeCost(_economy);
            if (!_wallet.TrySpendCash(cost)) return false;

            u.ApplyLevel(u.Level + 1);
            WriteLevels();
            return true;
        }

        private void WriteLevels()
        {
            if (_data == null) return;
            if (Mine != null) _data.stations.mine = Mine.Level;
            if (Storage != null) _data.stations.storage = Storage.Level;
            if (Market != null) _data.stations.market = Market.Level;
            if (Train != null) _data.stations.train = Train.Level;
        }
    }
}
