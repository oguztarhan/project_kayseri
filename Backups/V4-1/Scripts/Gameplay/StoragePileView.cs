using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The storage yard's raw-ore heap: a pyramid of many pooled ore chunks that grows and shrinks with
    /// the stored amount, so a bottleneck is *visible* — ore literally stacks up (GDD §1 Pillar 1, §14.5
    /// pooling). Chunks are pre-instantiated once and toggled; nothing is spawned/destroyed at runtime.
    /// </summary>
    public sealed class StoragePileView : MonoBehaviour
    {
        [SerializeField] private StorageYard storage;
        [SerializeField] private GameObject chunkPrefab;
        [SerializeField] private Transform pileArea;
        [SerializeField] private int maxChunks = 120;
        [SerializeField] private double perChunk = 5d;
        [SerializeField] private int baseSide = 8;
        [SerializeField] private float spacing = 0.45f;
        [SerializeField] private float chunkScale = 1.4f;

        private Transform[] _chunks;
        private float _timer;
        private int _active = -1;

        private void Start()
        {
            if (chunkPrefab == null) return;
            Vector3 origin = pileArea != null ? pileArea.position : transform.position;
            _chunks = new Transform[maxChunks];
            for (int i = 0; i < maxChunks; i++)
            {
                Transform c = Instantiate(chunkPrefab, PilePos(origin, i), Quaternion.Euler(0f, (i * 53) % 360, 0f), transform).transform;
                c.localScale = Vector3.one * chunkScale;
                c.gameObject.SetActive(false);
                _chunks[i] = c;
            }
        }

        // Square-pyramid mound: fill layer 0 (baseSide²), then each higher layer one unit narrower, centered.
        private Vector3 PilePos(Vector3 origin, int i)
        {
            int layer = 0, idx = i, side = baseSide;
            while (side > 1 && idx >= side * side) { idx -= side * side; layer++; side = baseSide - layer; }
            int gx = idx % side, gz = idx / side;
            float off = (side - 1) * 0.5f;
            return origin + new Vector3((gx - off) * spacing, layer * spacing * 0.55f, (gz - off) * spacing);
        }

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f || _chunks == null || storage == null) return;
            _timer = 0.25f;

            double total = storage.Ore.Total.ToDouble();
            int want = Mathf.Clamp((int)(total / perChunk), 0, maxChunks);
            if (want == _active) return;
            _active = want;
            for (int i = 0; i < maxChunks; i++)
            {
                bool on = i < want;
                if (_chunks[i].gameObject.activeSelf != on) _chunks[i].gameObject.SetActive(on);
            }
        }
    }
}
