using System.Collections.Generic;
using Game.Core;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Per-scene coordinator: discovers stations, applies saved per-track levels + hired managers on load,
    /// and routes upgrade, manager, and prestige actions so cash-spend and save-write stay in sync
    /// (GDD §3 upgrade axes, §5, §6, §8).
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

        /// <summary>Same-labelled stations (e.g. a 3-truck fleet) collapsed into one upgrade entry.</summary>
        public sealed class Group
        {
            public string Label;
            public IUpgradable Rep;                                   // representative for cost/level display
            public readonly List<IUpgradable> Members = new List<IUpgradable>();
        }
        public List<Group> Groups { get; } = new List<Group>();

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

            foreach (var u in Upgradables)
            {
                Group g = Groups.Find(x => x.Label == u.Label);
                if (g == null) { g = new Group { Label = u.Label, Rep = u }; Groups.Add(g); }
                g.Members.Add(u);
            }
        }

        private void Start()
        {
            if (_data == null) return;
            foreach (var u in Upgradables)
                for (int t = 0; t < u.TrackCount; t++)
                {
                    StationLevel saved = _data.stationLevels.Find(s => s.id == TrackId(u, t));
                    if (saved != null) u.SetTrackLevel(t, saved.level);
                }
            ApplyManagers();
        }

        public void TapAllMines() { for (int i = 0; i < Mines.Count; i++) Mines[i].Tap(); }

        // ---- per-track upgrades (GDD §3) ----
        public bool TryUpgradeTrack(IUpgradable u, int track)
        {
            if (u == null || _wallet == null || _economy == null) return false;
            if (track < 0 || track >= u.TrackCount) return false;
            BigDouble cost = u.TrackCost(track, _economy);
            if (!_wallet.TrySpendCash(cost)) return false;
            u.SetTrackLevel(track, u.TrackLevel(track) + 1);
            SaveTrack(u, track);
            return true;
        }

        // ---- group upgrades (a fleet upgrades as one; mines/trains stay separate via distinct labels) ----
        public bool TryUpgradeGroup(Group g, int track)
        {
            if (g == null || g.Rep == null || _wallet == null || _economy == null) return false;
            if (track < 0 || track >= g.Rep.TrackCount) return false;
            BigDouble cost = g.Rep.TrackCost(track, _economy);
            if (!_wallet.TrySpendCash(cost)) return false;
            int newLevel = g.Rep.TrackLevel(track) + 1;
            foreach (var m in g.Members) { m.SetTrackLevel(track, newLevel); SaveTrack(m, track); }
            return true;
        }

        public bool GroupCanManage(Group g) => g != null && g.Rep is IProducer;
        public bool GroupHasManager(Group g) => g != null && HasManager(g.Rep);
        public BigDouble GroupManagerCost(Group g) => ManagerCost(g != null ? g.Rep : null);

        public bool TryHireManagerGroup(Group g)
        {
            if (g == null || g.Rep == null || _data == null || _wallet == null || !(g.Rep is IProducer)) return false;
            if (HasManager(g.Rep)) return false;
            if (!_wallet.TrySpendCash(ManagerCost(g.Rep))) return false;
            foreach (var m in g.Members)
            {
                string id = IdOf(m);
                if (!_data.hiredManagers.Contains(id)) _data.hiredManagers.Add(id);
                if (m is IProducer prod) prod.RateMultiplier = 1d + _economy.ManagerBonus;
            }
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
                for (int t = 0; t < u.TrackCount; t++) u.SetTrackLevel(t, 0);
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
        private static string TrackId(IUpgradable u, int track) => IdOf(u) + "#" + track;

        private void SaveTrack(IUpgradable u, int track)
        {
            if (_data == null) return;
            string id = TrackId(u, track);
            StationLevel e = _data.stationLevels.Find(s => s.id == id);
            if (e == null) { e = new StationLevel { id = id }; _data.stationLevels.Add(e); }
            e.level = u.TrackLevel(track);
        }
    }
}
