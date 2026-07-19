using System.Collections.Generic;
using Game.Core;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Per-scene coordinator: discovers stations, applies saved levels + hired managers on load, and
    /// routes upgrade, manager, and prestige actions so cash-spend and save-write stay in sync
    /// (GDD §5, §6, §8).
    /// </summary>
    public sealed class GameWorld : MonoBehaviour
    {
        private WalletService _wallet;
        private EconomyService _economy;
        private PrestigeService _prestige;
        private SaveData _data;

        public List<IUpgradable> Upgradables { get; } = new List<IUpgradable>();
        public List<MineStation> Mines { get; } = new List<MineStation>();
        public PrestigeService Prestige => _prestige;

        private void Awake()
        {
            _wallet = ServiceLocator.Get<WalletService>();
            _economy = ServiceLocator.Get<EconomyService>();
            _prestige = ServiceLocator.Get<PrestigeService>();
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
            ApplyManagers();
        }

        public void TapAllMines() { for (int i = 0; i < Mines.Count; i++) Mines[i].Tap(); }

        public bool TryUpgrade(IUpgradable u)
        {
            if (u == null || _wallet == null || _economy == null) return false;
            BigDouble cost = u.UpgradeCost(_economy);
            if (!_wallet.TrySpendCash(cost)) return false;
            u.ApplyLevel(u.Level + 1);
            SaveLevel(u);
            return true;
        }

        // ---- managers (GDD §6) ----
        public bool CanManage(IUpgradable u) => u is IProducer;
        public bool HasManager(IUpgradable u) => _data != null && _data.hiredManagers.Contains(IdOf(u));
        public BigDouble ManagerCost(IUpgradable u) => new BigDouble(_economy != null ? _economy.ManagerCostBase : 500d);

        public bool TryHireManager(IUpgradable u)
        {
            if (u == null || _data == null || _wallet == null || !(u is IProducer prod)) return false;
            string id = IdOf(u);
            if (_data.hiredManagers.Contains(id)) return false;
            if (!_wallet.TrySpendCash(ManagerCost(u))) return false;
            _data.hiredManagers.Add(id);
            prod.RateMultiplier = 1d + _economy.ManagerBonus;
            return true;
        }

        // ---- prestige (GDD §8) ----
        public bool DoPrestige()
        {
            if (_prestige == null || !_prestige.CanPrestige()) return false;
            _prestige.DoPrestige();
            foreach (var u in Upgradables)
            {
                u.ApplyLevel(0);
                if (u is IProducer p) p.RateMultiplier = 1d;
            }
            return true;
        }

        private void ApplyManagers()
        {
            if (_data == null || _economy == null) return;
            foreach (var u in Upgradables)
                if (u is IProducer prod && _data.hiredManagers.Contains(IdOf(u)))
                    prod.RateMultiplier = 1d + _economy.ManagerBonus;
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
