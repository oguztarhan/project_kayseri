using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// An unlockable ore mountain (GDD §4). Rendered <b>ghosted</b> (a translucent placeholder) until bought
    /// via <see cref="MountainManager"/> — so the player always sees what's next to unlock — then turns solid
    /// and starts extracting its <see cref="MountainDefinition.Ore"/> into the shared storage yard each tick,
    /// bringing that ore tier (and the recipes gated behind it) into the chain.
    /// </summary>
    public sealed class MountainMine : MonoBehaviour
    {
        [SerializeField] private MountainDefinition def;
        [SerializeField] private StorageYard storage;
        [SerializeField] private GameObject lockedMarker;   // optional prop shown only while locked
        [SerializeField] private Material ghostMaterial;    // translucent placeholder look while locked

        public MountainDefinition Def => def;
        public bool Unlocked { get; private set; }
        public Vector3 LabelAnchor => transform.position + Vector3.up * 6f;

        private GameClock _clock;
        private Renderer[] _renderers;
        private Material[][] _originals;

        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>();
            _originals = new Material[_renderers.Length][];
            for (int i = 0; i < _renderers.Length; i++) _originals[i] = _renderers[i].sharedMaterials;
        }

        private void Start()
        {
            _clock = ServiceLocator.Get<GameClock>();
            if (_clock != null) _clock.OnTick += OnTick;
            RefreshVisual();
        }

        private void OnDestroy() { if (_clock != null) _clock.OnTick -= OnTick; }

        public void Unlock() { Unlocked = true; RefreshVisual(); }

        private void RefreshVisual()
        {
            if (lockedMarker != null) lockedMarker.SetActive(!Unlocked);
            if (_renderers == null || ghostMaterial == null) return;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_originals[i] == null) continue;
                if (!Unlocked)
                {
                    var arr = new Material[_originals[i].Length];
                    for (int j = 0; j < arr.Length; j++) arr[j] = ghostMaterial;
                    _renderers[i].sharedMaterials = arr;
                }
                else
                {
                    _renderers[i].sharedMaterials = _originals[i];
                }
            }
        }

        private void OnTick()
        {
            if (!Unlocked || storage == null || def == null || def.Ore == null) return;
            storage.Ore.Add(def.Ore, new BigDouble(def.BaseRate * _clock.TickInterval));
        }
    }
}
