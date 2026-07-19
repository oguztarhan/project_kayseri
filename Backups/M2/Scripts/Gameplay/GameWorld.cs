using System.Collections.Generic;
using Game.Core;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Per-scene coordinator: discovers every upgradable station, applies saved levels on load, and
    /// routes upgrade purchases so cash-spend, level-up, and save-write stay in sync (GDD §5). Levels
    /// persist keyed by station id (GameObject name), so it scales to any number of stations.
    /// </summary>
    public sealed class GameWorld : MonoBehaviour
    {
        private WalletService _wallet;
        private EconomyService _economy;
        private SaveData _data;

        public List<IUpgradable> Upgradables { get; } = new List<IUpgradable>();
        public List<MineStation> Mines { get; } = new List<MineStation>();

        private void Awake()
        {
            _wallet = ServiceLocator.Get<WalletService>();
            _economy = ServiceLocator.Get<EconomyService>();
            _data = ServiceLocator.Get<SaveData>();

            var comps = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var c in comps)
            {
                if (c is IUpgradable u) Upgradables.Add(u);
                if (c is MineStation m) Mines.Add(m);
            }
        }

        private void Start()
        {
            if (_data == null) return;
            foreach (var u in Upgradables)
            {
                StationLevel saved = _data.stationLevels.Find(s => s.id == IdOf(u));
                if (saved != null) u.ApplyLevel(saved.level);
            }
        }

        public void TapAllMines()
        {
            for (int i = 0; i < Mines.Count; i++) Mines[i].Tap();
        }

        public bool TryUpgrade(IUpgradable u)
        {
            if (u == null || _wallet == null || _economy == null) return false;
            BigDouble cost = u.UpgradeCost(_economy);
            if (!_wallet.TrySpendCash(cost)) return false;
            u.ApplyLevel(u.Level + 1);
            SaveLevel(u);
            return true;
        }

        private static string IdOf(IUpgradable u) => ((MonoBehaviour)u).gameObject.name;

        private void SaveLevel(IUpgradable u)
        {
            if (_data == null) return;
            string id = IdOf(u);
            StationLevel e = _data.stationLevels.Find(s => s.id == id);
            if (e == null) { e = new StationLevel { id = id }; _data.stationLevels.Add(e); }
            e.level = u.Level;
        }
    }
}
