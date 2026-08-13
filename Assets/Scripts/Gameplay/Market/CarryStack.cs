using System.Collections.Generic;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The stack of bars on the player's back — the thing the whole yard loop is actually about.
    ///
    /// Whole bars, not a fraction. The pads and the ledger deal in doubles because an island's output
    /// is a rate, but a stack you can see has to be countable: the player has to be able to glance at
    /// their back and know whether another trip is worth it. Everything here is therefore integer, and
    /// the conversion happens at the two ends where it meets the ledger.
    ///
    /// The blocks are pooled and never destroyed. A busy yard adds and removes one several times a
    /// second, and that is exactly the shape of allocation that shows up as a stutter twenty minutes in.
    /// </summary>
    public sealed class CarryStack : MonoBehaviour
    {
        [Tooltip("Sıfırıncı seviyede sırtta taşınabilen külçe sayısı.")]
        [SerializeField, Min(1)] private int baseCapacity = 6;

        [Tooltip("Her yükseltme seviyesinin eklediği külçe.")]
        [SerializeField, Min(1)] private int capacityPerLevel = 2;

        [Tooltip("Bir külçe bloğunun boyu. Yığın bu kadar yükselir.")]
        [SerializeField] private Vector3 blockSize = new Vector3(1.05f, 0.32f, 0.62f);

        [Tooltip("En alttaki bloğun ayaktan yüksekliği.")]
        [SerializeField] private float firstBlockHeight = 1.35f;

        [Tooltip("Bloklar yerine oturma hızı. Yığın anında zıplamasın diye.")]
        [SerializeField, Min(1f)] private float settleSpeed = 14f;

        private readonly List<Transform> _blocks = new List<Transform>();
        private readonly Stack<Transform> _spare = new Stack<Transform>();
        private Transform _mount;
        private Material _material;
        private MarketPrefabs _prefabs;
        private int _capacity;

        public int Count => _blocks.Count;
        public int Capacity => _capacity;
        public bool IsFull => _blocks.Count >= _capacity;
        public bool IsEmpty => _blocks.Count == 0;

        /// <summary>Wires the stack to a mount point and the ore's colour, and sets how much it holds.</summary>
        public void Configure(Transform mount, Material material, int upgradeLevel)
        {
            _mount = mount != null ? mount : transform;
            _material = material;
            SetUpgradeLevel(upgradeLevel);
        }

        /// <summary>
        /// Re-reads how much the player can shoulder. Called the moment a carry pad is paid, so the
        /// upgrade lands while they are still standing on it rather than on the next visit.
        /// </summary>
        public void SetUpgradeLevel(int upgradeLevel)
        {
            _capacity = baseCapacity + capacityPerLevel * Mathf.Max(0, upgradeLevel);
        }

        /// <summary>
        /// Re-skins the load to the yard the player is standing in. Blocks already on the back are
        /// repainted too — walk from the coal yard into the copper one carrying half a stack and the
        /// alternative is a shoulder-load of the wrong ore.
        /// </summary>
        public void SetMaterial(Material material)
        {
            if (material == null || material == _material) return;
            _material = material;
            for (int i = 0; i < _blocks.Count; i++) Skin(_blocks[i]);
            foreach (Transform spare in _spare) Skin(spare);
        }

        private void Skin(Transform block)
        {
            var renderer = block != null ? block.GetComponent<MeshRenderer>() : null;
            if (renderer != null) renderer.sharedMaterial = _material;
        }

        /// <summary>Puts one bar on the stack. False when there is no room, which is the caller's cue to stop.</summary>
        public bool TryAdd()
        {
            if (IsFull) return false;
            Transform block = _spare.Count > 0 ? _spare.Pop() : NewBlock();
            block.gameObject.SetActive(true);
            // Dropped in from above its slot so it visibly lands rather than appearing.
            block.localPosition = SlotOf(_blocks.Count) + Vector3.up * 0.6f;
            _blocks.Add(block);
            return true;
        }

        /// <summary>Takes one bar off the top. False when there is nothing left to unload.</summary>
        public bool TryRemove()
        {
            int last = _blocks.Count - 1;
            if (last < 0) return false;
            Transform block = _blocks[last];
            _blocks.RemoveAt(last);
            block.gameObject.SetActive(false);
            _spare.Push(block);
            return true;
        }

        private void LateUpdate()
        {
            // Chasing the slot rather than being parked in it: the stack then sways a step behind the
            // player, which is the whole reason a loaded walk reads as heavy.
            float t = 1f - Mathf.Exp(-settleSpeed * Time.deltaTime);
            for (int i = 0; i < _blocks.Count; i++)
                _blocks[i].localPosition = Vector3.Lerp(_blocks[i].localPosition, SlotOf(i), t);
        }

        private Vector3 SlotOf(int index) =>
            new Vector3(0f, firstBlockHeight + blockSize.y * index * 1.06f, -0.28f);

        /// <summary>The art a carried bar is made of. Wired on the market boot object.</summary>
        public void SetPrefabs(MarketPrefabs prefabs) => _prefabs = prefabs;

        private Transform NewBlock()
        {
            // Colliders are stripped by the spawner — cargo must not collide with the yard it is
            // crossing, or a full stack would shove the player through a wall.
            return MarketPrefabs.Spawn(_prefabs != null ? _prefabs.Bar : null, _mount, "Kulce",
                                       PrimitiveType.Cube, blockSize, _material);
        }
    }
}
