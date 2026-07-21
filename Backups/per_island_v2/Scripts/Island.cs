using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// One island in the archipelago. Each island runs its <b>own</b> upgradeable operation with several
    /// independent tracks (Mine / Train / Railway / Trucks / Buildings, from its <see cref="IslandDefinition"/>).
    /// Upgrading one island's tracks never touches another's — they persist under distinct save keys. Maxing
    /// every track unlocks the next island (GDD §4/§8). The starter (Coal) begins unlocked and is the "home"
    /// island powered by the real 3D chain, so it earns no passive income; the others earn cash scaled by their
    /// track levels. The island's art lives under <see cref="artRoot"/> so entering one island can hide the rest.
    /// </summary>
    public sealed class Island : MonoBehaviour
    {
        [SerializeField] private IslandDefinition def;
        [SerializeField] private Transform artRoot;   // this island's fbx art + train; toggled to hide other islands

        public IslandDefinition Def => def;
        public bool Unlocked { get; private set; }
        public bool Visible { get; private set; } = true;

        public int TrackCount => def != null ? def.TrackCount : 0;
        public string TrackName(int t) => def != null ? def.TrackName(t) : "";
        public int TrackLevel(int t) => (_levels != null && t >= 0 && t < _levels.Length) ? _levels[t] : 0;
        public int MaxLevelPerTrack => def != null ? def.MaxLevelPerTrack : 0;

        // Maxing every track is the gate that unlocks the next island in the chain.
        public bool IsMaxed
        {
            get
            {
                if (!Unlocked || def == null || _levels == null || _levels.Length == 0) return false;
                for (int t = 0; t < _levels.Length; t++) if (_levels[t] < def.MaxLevelPerTrack) return false;
                return true;
            }
        }

        public int TotalLevel { get { int s = 0; if (_levels != null) for (int t = 0; t < _levels.Length; t++) s += _levels[t]; return s; } }

        public Vector3 LabelAnchor => transform.position + Vector3.up * (12f * transform.localScale.y);

        // Passive income scales with every track's level × weight; the home island earns 0 (its real chain pays).
        public double IncomePerSec
        {
            get
            {
                if (def == null || def.HomeIsland || _levels == null) return 0d;
                double m = 1d;
                for (int t = 0; t < _levels.Length; t++) m += def.TrackIncomeWeight(t) * _levels[t];
                return def.BaseIncomePerSec * m;
            }
        }

        public BigDouble TrackCost(int t)
        {
            if (def == null) return new BigDouble(0d);
            if (_economy == null) return new BigDouble(def.TrackBaseCost(t));
            return _economy.UpgradeCost(def.TrackBaseCost(t), TrackLevel(t));
        }

        private int[] _levels;
        private GameClock _clock;
        private WalletService _wallet;
        private EconomyService _economy;
        private PrestigeService _prestige;
        private BoostService _boost;

        private void Awake()
        {
            _levels = new int[def != null ? def.TrackCount : 0];
            if (def != null && def.Starter) Unlocked = true;
        }

        private void Start()
        {
            _clock = ServiceLocator.Get<GameClock>();
            _wallet = ServiceLocator.Get<WalletService>();
            _economy = ServiceLocator.Get<EconomyService>();
            _prestige = ServiceLocator.Get<PrestigeService>();
            _boost = ServiceLocator.Get<BoostService>();
            if (_clock != null) _clock.OnTick += OnTick;
        }

        private void OnDestroy() { if (_clock != null) _clock.OnTick -= OnTick; }

        public void Unlock() { Unlocked = true; }

        /// <summary>Show/hide this island's whole art group (used to hide the others when one is entered).</summary>
        public void SetVisible(bool v)
        {
            Visible = v;
            if (artRoot != null && artRoot.gameObject.activeSelf != v) artRoot.gameObject.SetActive(v);
        }

        /// <summary>Set a track's saved level (load path). Clamped.</summary>
        public void SetTrackLevel(int t, int level)
        {
            if (_levels == null || def == null || t < 0 || t >= _levels.Length) return;
            _levels[t] = Mathf.Clamp(level, 0, def.MaxLevelPerTrack);
        }

        /// <summary>Buy one level on a track. No-op if locked or that track is already maxed.</summary>
        public bool TryUpgradeTrackLocal(int t)
        {
            if (!Unlocked || def == null || _levels == null || t < 0 || t >= _levels.Length) return false;
            if (_levels[t] >= def.MaxLevelPerTrack) return false;
            _levels[t]++;
            return true;
        }

        private void OnTick()
        {
            if (!Unlocked || _wallet == null || def == null) return;
            double inc = IncomePerSec;
            if (inc <= 0d) return;
            double m = 1d;
            if (_prestige != null) m *= _prestige.IncomeMultiplier;
            if (_boost != null) m *= _boost.ActiveMultiplier;
            _wallet.AddCash(new BigDouble(inc * m * _clock.TickInterval));
        }
    }
}
