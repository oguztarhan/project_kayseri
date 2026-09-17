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
    /// The game has one island and no way to buy or switch islands; the island map that did both was removed.
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
            public double capPerMin;         // fully-upgraded $/min — and the ceiling it earns against
            public Color oreColor = Color.white;
        }

        [SerializeField] private Entry[] islands;   // leave empty in the Inspector to use the default 8-ore ladder

        private CoalOperation[] _ops;
        private MarketService _market;
        private TimeService _time;
        private SaveService _save;
        private SaveData _data;
        private ChapterProgressionService _progression;
        private int _active;

        /// <summary>
        /// True when <paramref name="i"/> actually addresses an island.
        ///
        /// This exists because the ladder is not populated until <see cref="Awake"/> runs: the
        /// serialized array is allowed to be empty — the field's own comment invites it — and Awake
        /// fills it from DefaultLadder(). Unity interleaves Awake and OnEnable per object during a
        /// scene load rather than running every Awake first, so anything that asks this component a
        /// question from its own OnEnable can arrive before that has happened.
        ///
        /// OfferCountdown did exactly that and threw IndexOutOfRangeException on every load, which is
        /// why its badge never showed. OreColor and BrandColor already guarded; the rest did not, and
        /// the inconsistency was the whole bug.
        /// </summary>
        private bool Has(int i) => islands != null && i >= 0 && i < islands.Length;

        public int Count => islands != null ? islands.Length : 0;
        public int ActiveIndex => _active;
        /// <summary>
        /// The ladder's own label for a rung — always Turkish, and NOT the string to draw. The name the
        /// player reads comes from the text table: <c>Loc.Id("ada", IslandKey(i))</c>. Drawing this one
        /// put "KÖMÜR ADASI" on the offer window in every language.
        /// </summary>
        public string IslandName(int i) => Has(i) ? islands[i].displayName : string.Empty;
        public string IslandKey(int i) => Has(i) ? islands[i].key : string.Empty;
        public string RootName(int i) => Has(i) ? islands[i].rootName : string.Empty;
        public double CapPerMin(int i) => Has(i) ? islands[i].capPerMin : 0d;
        public Color OreColor(int i) => Has(i) ? islands[i].oreColor : Color.white;

        /// <summary>
        /// The island's colour FOR THE UI, which is not the same thing as the colour of its ore.
        ///
        /// oreColor has to stay honest, because it tints the actual ore chunks and heaps in the world
        /// (CoalOperation builds _oreMat from it). Honest ore is the problem: silver measures 0.076
        /// saturation, iron 0.088, coal 0.167, diamond 0.250 — four of the eight islands are, correctly,
        /// grey. Drive a map, a badge and a progress bar off that and half the game has no colour in it,
        /// which is exactly what happened.
        ///
        /// So the UI gets a brand instead. Every island in the genre does this — the mine's colour is a
        /// label, not a mineralogy claim. The four ores with real hue keep it and are only lifted; the
        /// four without are assigned one.
        ///
        /// HUES ARE SPACED AGAINST THE LADDER, not just against each other. Coal, Copper and Iron are
        /// islands 1, 2 and 3, so making iron a rust orange — the obvious choice — would open the game
        /// with three consecutive warm-orange islands. Iron reads naturally as steel blue, which both
        /// suits it and breaks the run.
        /// </summary>
        public Color BrandColor(int i)
        {
            if (!Has(i)) return Color.white;
            switch (islands[i].key)
            {
                // Hues, and the gaps between CONSECUTIVE islands, are solved rather than picked: the
                // smallest gap along the ladder is 20 degrees (coal to copper), and those two are also
                // separated by value — coal resolves darker than copper on the map, which is what a
                // 20-degree gap on its own would not carry.
                case "coal":    return new Color(0.76f, 0.24f, 0.13f);   //  10deg  ember, deliberately dark
                case "copper":  return new Color(0.94f, 0.58f, 0.20f);   //  31deg  its own colour, lifted
                case "iron":    return new Color(0.30f, 0.50f, 0.86f);   // 219deg  steel; see the note above
                case "silver":  return new Color(0.32f, 0.86f, 0.80f);   // 173deg  cool teal
                case "gold":    return new Color(0.98f, 0.78f, 0.20f);   //  45deg  its own colour, lifted
                case "ruby":    return new Color(0.92f, 0.20f, 0.34f);   // 348deg  its own colour, lifted
                case "emerald": return new Color(0.18f, 0.82f, 0.42f);   // 142deg  its own colour, lifted
                case "diamond": return new Color(0.64f, 0.52f, 0.98f);   // 256deg  ice violet
            }
            return Lift(islands[i].oreColor);
        }

        /// <summary>
        /// Fallback for an island the table above has never heard of — a new key, or a scene wired by
        /// hand. Pushes saturation and value up to a floor so it is at least usable in the UI, which is
        /// the best that can be done without being told what the island is supposed to look like.
        /// </summary>
        private static Color Lift(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            return Color.HSVToRGB(h, Mathf.Max(s, 0.55f), Mathf.Max(v, 0.70f));
        }
        // _ops is built in Awake alongside the ladder, so it needs its own guard rather than Has().
        public CoalOperation Operation(int i) => _ops != null && i >= 0 && i < _ops.Length ? _ops[i] : null;
        public bool IsOwned(int i) => i == 0 || (Has(i) && _data != null && _data.unlockedIslands.Contains(islands[i].key));
        public bool IsMaxed(int i)
        {
            var op = Operation(i);
            return op != null && op.enabled && op.FullyMaxed;
        }

        /// <summary>
        /// The island's earning rate. Every island — the one you are standing on included — is paid by
        /// its market yard now, so there is one meter to ask and it lives in <see cref="MarketService"/>.
        /// A zero from a yard that has not sold anything yet falls back to the last persisted rate:
        /// handing the zero on would let a launch-and-quit inside half a minute save an empire that
        /// earns nothing, and the next launch would grant no offline income at all.
        /// </summary>
        public double RatePerMin(int i)
        {
            if (_market == null) return SavedRate(i);
            double live = _market.RatePerMin(YardKey(i));
            return live > 0d ? live : SavedRate(i);
        }

        private void Awake()
        {
            if (islands == null || islands.Length == 0) islands = DefaultLadder();
            _data = ServiceLocator.Get<SaveData>();
            _market = ServiceLocator.Get<MarketService>();
            _time = ServiceLocator.Get<TimeService>();
            _save = ServiceLocator.Get<SaveService>();

            // match each entry to its operation component (they all live on this controller object)
            _ops = new CoalOperation[islands.Length];
            var ops = GetComponents<CoalOperation>();
            for (int i = 0; i < islands.Length; i++)
                for (int o = 0; o < ops.Length; o++)
                    if (ops[o].IslandKey == islands[i].key) { _ops[i] = ops[o]; break; }

            _active = 0;
            StationLevel act = FindLevel("worldactive");
            if (act != null && act.level >= 0 && act.level < islands.Length && IsOwned(act.level)) _active = act.level;

            // The old starter was account-wide. Convert it before starting today's island so a legacy
            // buyer is not offered old islands again and an unbought countdown keeps its original age.
            var owned = new System.Collections.Generic.List<string>();
            for (int i = 0; i < islands.Length; i++) if (IsOwned(i)) owned.Add(islands[i].key);
            bool starterChanged = StarterOfferState.MigrateLegacy(
                _data, islands[_active].key, owned);
            if (_time != null)
                starterChanged |= StarterOfferState.EnsureStarted(
                    _data, islands[_active].key, _time.NowUnix());
            if (starterChanged && _save != null) _save.Save(_data);

            // exactly one island alive: Awake runs before every Start, so inactive operations never boot
            for (int i = 0; i < islands.Length; i++) SetIslandLive(i, i == _active);
            if (_market != null) _market.SetActiveIsland(YardKey(_active));
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

        // Paying the background islands used to happen here, once a second, off each island's own meter.
        // It does not any more: every island — active or not — is paid by its market yard, and
        // MarketService settles all eight in one pass. Two payers reading the same rate would have paid
        // for the same ore twice, so this one had to go rather than be guarded.

        /// <summary>
        /// Which market yard a rung's income is filed under.
        ///
        /// THE RUNG AND THE YARD ARE NO LONGER THE SAME KEY. A rung is the physical island and keeps
        /// its id forever — the starter offer, the map label and the ore name all hang off that. The
        /// yard belongs to the CHAPTER being played, because a chapter resets its yard along with
        /// everything else it owns and files the new one under its own namespace. Reading the rung's
        /// key here would have paid the live island out of chapter one's yard for the rest of the game.
        ///
        /// Only the ACTIVE rung moves: a rung nobody is playing has no chapter of its own to ask
        /// about. With no progression service registered this is chapter one, whose namespace is the
        /// rung's own key anyway.
        /// </summary>
        private string YardKey(int i)
        {
            if (!Has(i)) return string.Empty;
            if (i != _active) return islands[i].key;
            if (_progression == null) _progression = ServiceLocator.Get<ChapterProgressionService>();
            return _progression != null ? _progression.CurrentNamespace : islands[i].key;
        }

        // ---- persistence helpers (same islandLevels store the operations use) ----
        private double SavedRate(int i)
        {
            if (_data == null || _data.islandRates == null) return 0d;
            string id = YardKey(i);
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

        /// <summary>
        /// Every island moves by this step: what it earns and what its upgrades cost. Value and cost moving together is what holds the tempo flat — they
        /// used to be ×3.2 and ×4, so each island took 25% longer than the last and by the
        /// twentieth that had compounded to 73×. Nothing on a weekly content cadence survives that.
        /// </summary>
        private const double TierStep = 3.2d;

        /// <summary>
        /// What a maxed, fully-built coal island earns. MEASURED with the editor probe
        /// (Kayseri/Economy), not assumed: the ladder used to be priced off 29,000 $/min, a rate
        /// no island reaches, so every unlock silently cost about 1.8× the play it intended.
        /// </summary>
        /// <summary>Scene root built by Tools/Kayseri/Island/Build Shipyard Island.</summary>
        public const string ShipyardRootName = "Island_Shipyard";

        private const double CoalMaxPerMin = Game.Core.EconomyCurve.MaxedCoalPerMin;

        /// <summary>
        /// What the island earns fully upgraded, and the ceiling it earns against — the same
        /// number on purpose. Keep in step with each island's
        /// <c>incomeCapPerMin</c> in the scene.
        /// </summary>
        private static double CapPerMinFor(int n) => CoalMaxPerMin * System.Math.Pow(TierStep, n);

        /// <summary>
        /// The ladder. Only the name, the scene roots and the ore colour are authored; every
        /// number is derived, so next week's island is one row rather than a balance pass.
        /// </summary>
        private static Entry[] DefaultLadder()
        {
            // ONE island. The archipelago was eight ore islands; the game is now the single
            // authored IndustrialReference map, so every per-island collection in SaveData
            // (unlockedIslands, islandRates, islandLevels, conditions, marketYards, chapter
            // rows) collapses with this table instead of being unpicked field by field.
            //
            // The save key stays "coal" deliberately. It is an id, not a name: keeping it means
            // the first-island-is-owned path, the chapter rows and every existing offer receipt
            // keep resolving. Only the display name and the scene root actually change.
            var authored = new[]
            {
                E("coal", "SANAYİ ADASI", ShipyardRootName, "", new Color(0.32f, 0.38f, 0.46f)),
            };
            for (int n = 0; n < authored.Length; n++)
                authored[n].capPerMin = CapPerMinFor(n);
            return authored;
        }

        /// <summary>
        /// The ore ladder's keys in order, with no instance and no scene to look in. The market hall
        /// builds one yard per island the player owns and has to lay them out in this order, and by the
        /// time that scene loads this component and its island are both gone.
        /// </summary>
        public static string[] LadderKeys()
        {
            Entry[] ladder = DefaultLadder();
            var keys = new string[ladder.Length];
            for (int i = 0; i < ladder.Length; i++) keys[i] = ladder[i].key;
            return keys;
        }

        /// <summary>
        /// An island's ore colour with no instance and no scene to look in. The market scene needs it —
        /// a yard is tinted by whose island it serves — and by the time that scene loads this component
        /// and the island it belonged to are both gone.
        /// </summary>
        public static Color OreColorFor(string key)
        {
            Entry[] ladder = DefaultLadder();
            for (int i = 0; i < ladder.Length; i++)
                if (ladder[i].key == key) return ladder[i].oreColor;
            return new Color(0.30f, 0.30f, 0.34f);
        }

        private static Entry E(string key, string name, string root, string tiles, Color c) =>
            new Entry { key = key, displayName = name, rootName = root, tilesRootName = tiles, oreColor = c };
    }
}
