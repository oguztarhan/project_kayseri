using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// An unlockable ore mountain (GDD §4). Locked until bought via <see cref="MountainManager"/>; once
    /// unlocked it extracts its <see cref="MountainDefinition.Ore"/> into the shared storage yard each sim
    /// tick, so the refinery gains that ore and the recipes gated behind it (e.g. Gold Bar) start running.
    /// </summary>
    public sealed class MountainMine : MonoBehaviour
    {
        [SerializeField] private MountainDefinition def;
        [SerializeField] private StorageYard storage;
        [SerializeField] private GameObject lockedMarker;   // optional prop shown only while locked

        public MountainDefinition Def => def;
        public bool Unlocked { get; private set; }

        private GameClock _clock;

        private void Start()
        {
            _clock = ServiceLocator.Get<GameClock>();
            if (_clock != null) _clock.OnTick += OnTick;
            RefreshMarker();
        }

        private void OnDestroy() { if (_clock != null) _clock.OnTick -= OnTick; }

        public void Unlock() { Unlocked = true; RefreshMarker(); }

        private void RefreshMarker() { if (lockedMarker != null) lockedMarker.SetActive(!Unlocked); }

        private void OnTick()
        {
            if (!Unlocked || storage == null || def == null || def.Ore == null) return;
            storage.Ore.Add(def.Ore, new BigDouble(def.BaseRate * _clock.TickInterval));
        }
    }
}
