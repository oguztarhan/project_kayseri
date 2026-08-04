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
    /// $/min (persisted by its operation into <see cref="SaveData.islandRates"/>, clamped to that island's
    /// prestige-scaled cap), so buying
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
            public double capPerMin;         // fully-upgraded $/min — and the ceiling it earns against
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
            if (i != _active || _ops[i] == null || !_ops[i].enabled) return SavedRate(i);
            if (_ops[i].MeterTrustworthy) return _ops[i].CashPerMinute;
            // The meter is still warming up and reads zero. Handing that zero back would push it into
            // incomeRatePerSec below, so closing the game inside the first half minute would save an
            // empire that earns nothing and the next launch would grant no offline income at all.
            // The island's last measured rate is the honest stand-in until a real sale lands.
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

        /// <summary>
        /// Pays out every island you are NOT currently standing on, once a second.
        ///
        /// Only one island simulates at a time — running eight full operations would cost eight times the
        /// CPU for seven islands nobody is looking at. So each operation measures its own $/min while
        /// active and saves it (<see cref="SaveData.islandRates"/>); the moment you sail away, that becomes
        /// the island's payout rate. The result is an empire that keeps earning without simulating.
        ///
        /// This is why the whole "buy the next island" loop works: previous islands never go quiet.
        /// </summary>
        private void Update()
        {
            // Once per second, not per frame — this is bookkeeping, not animation.
            _bgAccum += Time.deltaTime;
            if (_bgAccum < 1f) return;
            _bgAccum -= 1f;
            // Services can be unavailable for the first frame or two after a scene load; retry rather
            // than caching in Start, which would silently leave this dead if it ran too early.
            if (_data == null) { _data = ServiceLocator.Get<SaveData>(); if (_data == null) return; }
            if (_wallet == null) { _wallet = ServiceLocator.Get<WalletService>(); if (_wallet == null) return; }

            double totalPerMin = 0d, background = 0d;
            for (int i = 0; i < islands.Length; i++)
            {
                if (!IsOwned(i)) continue;
                double rate = RatePerMin(i);       // live meter if active, last saved rate otherwise
                totalPerMin += rate;
                // The active island is excluded: its trucks are already paying the wallet directly as
                // they sell. Counting it here too would pay for the same ore twice.
                if (i != _active) background += rate;
            }
            if (background > 0d) _wallet.AddCash(new BigDouble(background / 60d));   // one second's worth

            // Offline earnings (granted in GameBootstrap on next launch) use the WHOLE empire's rate,
            // active island included — while the app is closed, nothing is simulating either.
            _data.incomeRatePerSec = totalPerMin / 60d;
        }

        // ---- persistence helpers (same islandLevels store the operations use) ----
        private double SavedRate(int i)
        {
            if (_data == null || _data.islandRates == null) return 0d;
            string id = islands[i].key;
            for (int r = 0; r < _data.islandRates.Count; r++)
                if (_data.islandRates[r].id == id) return _data.islandRates[r].perMin;
            return 0d;
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

        /// <summary>
        /// Every island moves by this step: what it earns, what its upgrades cost, and what it
        /// costs to unlock. Value and cost moving together is what holds the tempo flat — they
        /// used to be ×3.2 and ×4, so each island took 25% longer than the last and by the
        /// twentieth that had compounded to 73×. Nothing on a weekly content cadence survives that.
        /// </summary>
        private const double TierStep = 3.2d;

        /// <summary>
        /// What a maxed, fully-built coal island earns. MEASURED with the editor probe
        /// (Kayseri/Economy), not assumed: the ladder used to be priced off 29,000 $/min, a rate
        /// no island reaches, so every unlock silently cost about 1.8× the play it intended.
        /// </summary>
        private const double CoalMaxPerMin = Game.Core.EconomyCurve.MaxedCoalPerMin;

        /// <summary>
        /// Unlock prices through the onboarding ramp, solved against the pacing targets by
        /// Kayseri/Economy/Solve Ladder. The ramp is deliberately steep at the start — Copper
        /// on day one, not after thirty hours — and settles to a flat week per island, at which
        /// point it is simply ×<see cref="TierStep"/> and needs no more hand-picked numbers.
        /// </summary>
        private static readonly double[] RampUnlock =
        { 0d, 1.89e6d, 81.41e6d, 614.78e6d, 3.1e9d, 15.73e9d, 65.91e9d, 263.25e9d };

        private static double UnlockCostFor(int n)
            => n <= 0 ? 0d
             : n < RampUnlock.Length ? RampUnlock[n]
             : RampUnlock[RampUnlock.Length - 1] * System.Math.Pow(TierStep, n - RampUnlock.Length + 1);

        /// <summary>
        /// What the island earns fully upgraded, and the ceiling it earns against — the same
        /// number on purpose. The map's progress bar reads rate against this, so it reaches
        /// 100% exactly when an island is finished. Keep in step with each island's
        /// <c>incomeCapPerMin</c> in the scene.
        /// </summary>
        private static double CapPerMinFor(int n) => CoalMaxPerMin * System.Math.Pow(TierStep, n);

        /// <summary>
        /// The ladder. Only the name, the scene roots and the ore colour are authored; every
        /// number is derived, so next week's island is one row rather than a balance pass.
        /// </summary>
        private static Entry[] DefaultLadder()
        {
            var authored = new[]
            {
                E("coal",    "KÖMÜR ADASI",  "Island_Coal",    "",              new Color(0.10f, 0.10f, 0.12f)),
                E("copper",  "BAKIR ADASI",  "Island_Copper",  "Tiles_Copper",  new Color(0.72f, 0.45f, 0.20f)),
                E("iron",    "DEMİR ADASI",  "Island_Iron",    "Tiles_Iron",    new Color(0.62f, 0.63f, 0.68f)),
                E("silver",  "GÜMÜŞ ADASI",  "Island_Silver",  "Tiles_Silver",  new Color(0.85f, 0.87f, 0.92f)),
                E("gold",    "ALTIN ADASI",  "Island_Gold",    "Tiles_Gold",    new Color(0.95f, 0.78f, 0.22f)),
                E("ruby",    "YAKUT ADASI",  "Island_Ruby",    "Tiles_Ruby",    new Color(0.85f, 0.15f, 0.25f)),
                E("emerald", "ZÜMRÜT ADASI", "Island_Emerald", "Tiles_Emerald", new Color(0.15f, 0.75f, 0.35f)),
                E("diamond", "ELMAS ADASI",  "Island_Diamond", "Tiles_Diamond", new Color(0.75f, 0.95f, 1f)),
            };
            for (int n = 0; n < authored.Length; n++)
            {
                authored[n].unlockCost = UnlockCostFor(n);
                authored[n].capPerMin = CapPerMinFor(n);
            }
            return authored;
        }

        private static Entry E(string key, string name, string root, string tiles, Color c) =>
            new Entry { key = key, displayName = name, rootName = root, tilesRootName = tiles, oreColor = c };
    }
}
