using Game.Core;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Archipelago progression (GDD §2 meta + §4 ore ladder): eight ore islands — Coal → Diamond — each a
    /// full copy of the player-built operation, retinted per ore, with its own <see cref="CoalOperation"/>
    /// component on this same object. Exactly one island is ACTIVE: its root + tiles are enabled and its
    /// operation simulates visually. Every other OWNED island earns in the background at its last measured
    /// $/min (persisted by its operation as <c>rate#&lt;key&gt;</c>, clamped to that island's cap), so buying
    /// the next island never abandons the previous ones. The summed rate also feeds
    /// <see cref="SaveData.incomeRatePerSec"/> so offline earnings cover the whole empire.
    /// UI-free by design (assembly order): <c>IslandMapUI</c> drives Travel/TryBuy and re-frames the camera.
    /// </summary>
    public sealed class WorldIslands : MonoBehaviour
    {
        [System.Serializable]
        public sealed class Entry
        {
            public string key;            // save prefix + unlockedIslands id
            public string displayName;
            public string rootName;       // island root object in the scene
            public string tilesRootName;  // "" = tiles at scene root (the coal original)
            public double unlockCost;
            public double capPerMin;
            public Color oreColor = Color.white;
        }

        [SerializeField] private Entry[] islands;   // leave empty in the Inspector to use the default 8-ore ladder

        private CoalOperation[] _ops;
        private WalletService _wallet;
        private SaveData _data;
        private int _active;
        private float _bgAccum;

        public int Count => islands.Length;
        public int ActiveIndex => _active;
        public string IslandName(int i) => islands[i].displayName;
        public string IslandKey(int i) => islands[i].key;
        public string RootName(int i) => islands[i].rootName;
        public double UnlockCost(int i) => islands[i].unlockCost;
        public double CapPerMin(int i) => islands[i].capPerMin;
        public Color OreColor(int i) => islands[i].oreColor;
        public CoalOperation Operation(int i) => _ops[i];
        public bool IsOwned(int i) => i == 0 || (_data != null && _data.unlockedIslands.Contains(islands[i].key));
        public bool IsMaxed(int i) => _ops[i] != null && _ops[i].enabled && _ops[i].FullyMaxed;

        /// <summary>The island's earning rate: live meter when active, last persisted rate otherwise.</summary>
        public double RatePerMin(int i)
        {
            if (i == _active && _ops[i] != null && _ops[i].enabled) return _ops[i].CashPerMinute;
            return SavedRate(i);
        }

        private void Awake()
        {
            if (islands == null || islands.Length == 0) islands = DefaultLadder();
            _data = ServiceLocator.Get<SaveData>();

            // match each entry to its operation component (they all live on this controller object)
            _ops = new CoalOperation[islands.Length];
            var ops = GetComponents<CoalOperation>();
            for (int i = 0; i < islands.Length; i++)
                for (int o = 0; o < ops.Length; o++)
                    if (ops[o].IslandKey == islands[i].key) { _ops[i] = ops[o]; break; }

            _active = 0;
            StationLevel act = FindLevel("worldactive");
            if (act != null && act.level >= 0 && act.level < islands.Length && IsOwned(act.level)) _active = act.level;

            // exactly one island alive: Awake runs before every Start, so inactive operations never boot
            for (int i = 0; i < islands.Length; i++) SetIslandLive(i, i == _active);
        }

        /// <summary>Buy an island (world-map purchase). Does not travel — the map UI does that next.</summary>
        public bool TryBuy(int i)
        {
            if (i < 0 || i >= islands.Length || IsOwned(i)) return false;
            if (_wallet == null) _wallet = ServiceLocator.Get<WalletService>();
            if (_wallet == null || !_wallet.TrySpendCash(new BigDouble(islands[i].unlockCost))) return false;
            _data.unlockedIslands.Add(islands[i].key);
            return true;
        }

        /// <summary>Switch the live island. Returns the now-active operation (null if the switch was refused).</summary>
        public CoalOperation Travel(int i)
        {
            if (i < 0 || i >= islands.Length || i == _active || !IsOwned(i)) return null;
            SetIslandLive(_active, false);
            _active = i;
            SetIslandLive(i, true);
            SaveLevel("worldactive", i);
            return _ops[i];
        }

        private void SetIslandLive(int i, bool on)
        {
            var roots = gameObject.scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                string n = roots[r].name;
                if (n == islands[i].rootName) roots[r].SetActive(on);
                else if (!string.IsNullOrEmpty(islands[i].tilesRootName) && n == islands[i].tilesRootName) roots[r].SetActive(on);
            }
            if (_ops[i] != null) _ops[i].enabled = on;
        }

        private void Update()
        {
            _bgAccum += Time.deltaTime;
            if (_bgAccum < 1f) return;
            _bgAccum -= 1f;
            if (_data == null) { _data = ServiceLocator.Get<SaveData>(); if (_data == null) return; }
            if (_wallet == null) { _wallet = ServiceLocator.Get<WalletService>(); if (_wallet == null) return; }

            // owned, non-active islands keep producing at their last measured rate (already cap-clamped on save)
            double totalPerMin = 0d, background = 0d;
            for (int i = 0; i < islands.Length; i++)
            {
                if (!IsOwned(i)) continue;
                double rate = RatePerMin(i);
                totalPerMin += rate;
                if (i != _active) background += rate;
            }
            if (background > 0d) _wallet.AddCash(new BigDouble(background / 60d));
            _data.incomeRatePerSec = totalPerMin / 60d;   // offline earnings pay out the whole empire
        }

        // ---- persistence helpers (same islandLevels store the operations use) ----
        private int SavedRate(int i)
        {
            StationLevel e = FindLevel("rate#" + islands[i].key);
            return e != null ? e.level : 0;
        }

        private StationLevel FindLevel(string id)
        {
            if (_data == null || _data.islandLevels == null) return null;
            var list = _data.islandLevels;
            for (int i = 0; i < list.Count; i++) if (list[i].id == id) return list[i];
            return null;
        }

        private void SaveLevel(string id, int level)
        {
            if (_data == null || _data.islandLevels == null) return;
            StationLevel e = FindLevel(id);
            if (e == null) { e = new StationLevel { id = id }; _data.islandLevels.Add(e); }
            e.level = level;
        }

        /// <summary>Default balance ladder: caps ×3 per tier, unlock ≈ 6–45 cap-hours of everything owned so
        /// far — paces the full 8-island arc across roughly a month of casual play (GDD §5).</summary>
        private static Entry[] DefaultLadder() => new[]
        {
            E("coal",    "COAL ISLAND",    "Island_Coal",    "",              0d,      50000d,   new Color(0.10f, 0.10f, 0.12f)),
            E("copper",  "COPPER ISLAND",  "Island_Copper",  "Tiles_Copper",  20e6d,   150000d,  new Color(0.72f, 0.45f, 0.20f)),
            E("iron",    "IRON ISLAND",    "Island_Iron",    "Tiles_Iron",    100e6d,  450000d,  new Color(0.62f, 0.63f, 0.68f)),
            E("silver",  "SILVER ISLAND",  "Island_Silver",  "Tiles_Silver",  500e6d,  1.35e6d,  new Color(0.85f, 0.87f, 0.92f)),
            E("gold",    "GOLD ISLAND",    "Island_Gold",    "Tiles_Gold",    2.2e9d,  4.05e6d,  new Color(0.95f, 0.78f, 0.22f)),
            E("ruby",    "RUBY ISLAND",    "Island_Ruby",    "Tiles_Ruby",    9e9d,    12.15e6d, new Color(0.85f, 0.15f, 0.25f)),
            E("emerald", "EMERALD ISLAND", "Island_Emerald", "Tiles_Emerald", 36e9d,   36.45e6d, new Color(0.15f, 0.75f, 0.35f)),
            E("diamond", "DIAMOND ISLAND", "Island_Diamond", "Tiles_Diamond", 140e9d,  110e6d,   new Color(0.75f, 0.95f, 1f)),
        };

        private static Entry E(string key, string name, string root, string tiles, double cost, double cap, Color c) =>
            new Entry { key = key, displayName = name, rootName = root, tilesRootName = tiles, unlockCost = cost, capPerMin = cap, oreColor = c };
    }
}
