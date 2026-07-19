using Game.Core;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Base for every player-upgradable station. Holds an independent level per upgrade track and calls
    /// <see cref="OnUpgraded"/> whenever one changes so the station can recompute its behaviour. Subclasses
    /// declare their track names and per-track base cost, and map the levels to effects (GDD §3, §5).
    /// </summary>
    public abstract class UpgradableStation : MonoBehaviour, IUpgradable
    {
        [Tooltip("Optional friendly name shown in the upgrade UI (e.g. 'Coal Mine'). Falls back to the config name.")]
        [SerializeField] private string labelOverride;

        private int[] _levels;

        protected abstract string StationLabel { get; }
        protected abstract string[] TrackNames { get; }
        protected abstract double TrackBaseCost(int track);

        protected int[] Levels
        {
            get { if (_levels == null) _levels = new int[TrackNames.Length]; return _levels; }
        }

        public string Label => string.IsNullOrEmpty(labelOverride) ? StationLabel : labelOverride;
        public int TrackCount => TrackNames.Length;
        public string TrackName(int track) => TrackNames[track];
        public int TrackLevel(int track) => Levels[track];
        public BigDouble TrackCost(int track, EconomyService economy) => economy.UpgradeCost(TrackBaseCost(track), Levels[track]);
        public void SetTrackLevel(int track, int level) { Levels[track] = level; OnUpgraded(); }

        /// <summary>Recompute derived values from the track levels. Called on every upgrade (and on load).</summary>
        protected virtual void OnUpgraded() { }
    }
}
