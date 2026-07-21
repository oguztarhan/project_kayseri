using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// One island in the archipelago. Rendered <b>ghosted</b> until unlocked (so the player sees the whole
    /// progression). Once unlocked it earns cash every sim tick at its ore's rate — and keeps earning forever,
    /// even after the player moves on to the next island (GDD §4/§8). The starter (Coal) begins unlocked.
    /// </summary>
    public sealed class Island : MonoBehaviour
    {
        [SerializeField] private IslandDefinition def;
        [SerializeField] private GameObject[] ghostables;   // roots whose renderers are ghosted while locked
        [SerializeField] private Material ghostMaterial;

        public IslandDefinition Def => def;
        public bool Unlocked { get; private set; }
        public int Level { get; private set; }
        public int MaxLevel => def != null ? def.MaxLevel : 0;
        public bool IsMaxed => Unlocked && Level >= MaxLevel;
        public Vector3 LabelAnchor => transform.position + Vector3.up * 9f;

        // Income grows with each upgrade; upgrade price grows geometrically. Maxing the island is the
        // gate that unlocks the next one (GDD §4/§8 archipelago progression). The home island (Coal) is
        // powered by the real 3D chain, so it adds no passive income — its level only serves the gate.
        public double IncomePerSec => (def == null || def.HomeIsland) ? 0d : def.BaseIncomePerSec * (1d + 0.5d * Level);
        public double UpgradeCost => def == null ? 0d : def.UpgradeBaseCost * System.Math.Pow(1.65d, Level);

        private GameClock _clock;
        private WalletService _wallet;
        private PrestigeService _prestige;
        private BoostService _boost;
        private Renderer[] _rends;
        private Material[][] _orig;

        private void Awake()
        {
            CacheRenderers();
            if (def != null && def.Starter) Unlocked = true;
        }

        private void Start()
        {
            _clock = ServiceLocator.Get<GameClock>();
            _wallet = ServiceLocator.Get<WalletService>();
            _prestige = ServiceLocator.Get<PrestigeService>();
            _boost = ServiceLocator.Get<BoostService>();
            if (_clock != null) _clock.OnTick += OnTick;
            RefreshVisual();
        }

        private void OnDestroy() { if (_clock != null) _clock.OnTick -= OnTick; }

        public void Unlock() { Unlocked = true; RefreshVisual(); }

        /// <summary>Set the saved upgrade level (used when loading a save). Clamped to the island's max.</summary>
        public void SetLevel(int level) { Level = Mathf.Clamp(level, 0, MaxLevel); }

        /// <summary>Buy one upgrade level. No-op once maxed. Returns false if already maxed.</summary>
        public bool Upgrade()
        {
            if (!Unlocked || IsMaxed) return false;
            Level++;
            return true;
        }

        private void OnTick()
        {
            if (!Unlocked || _wallet == null || def == null) return;
            double m = 1d;
            if (_prestige != null) m *= _prestige.IncomeMultiplier;
            if (_boost != null) m *= _boost.ActiveMultiplier;
            _wallet.AddCash(new BigDouble(IncomePerSec * m * _clock.TickInterval));
        }

        private void CacheRenderers()
        {
            var list = new List<Renderer>();
            if (ghostables != null)
                for (int i = 0; i < ghostables.Length; i++)
                    if (ghostables[i] != null) list.AddRange(ghostables[i].GetComponentsInChildren<Renderer>());
            _rends = list.ToArray();
            _orig = new Material[_rends.Length][];
            for (int i = 0; i < _rends.Length; i++) _orig[i] = _rends[i].sharedMaterials;
        }

        private void RefreshVisual()
        {
            if (_rends == null || ghostMaterial == null) return;
            for (int i = 0; i < _rends.Length; i++)
            {
                if (_orig[i] == null) continue;
                if (!Unlocked)
                {
                    var arr = new Material[_orig[i].Length];
                    for (int j = 0; j < arr.Length; j++) arr[j] = ghostMaterial;
                    _rends[i].sharedMaterials = arr;
                }
                else _rends[i].sharedMaterials = _orig[i];
            }
        }
    }
}
