using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The display rack behind the counter, and the bars standing on it.
    ///
    /// It shows what is ON THE COUNTER — the same number <see cref="SellCounter"/> is already laying
    /// out on the plank — and showing it twice is the point rather than an oversight. The bars on the
    /// counter top are small, flat, and seen edge-on from a camera looking down at fifty degrees; you
    /// can tell there are some, and you cannot tell whether there are three or eleven without stopping
    /// to count. The rack says the same thing standing up, in three rows, from across the room. It is
    /// the difference between a plank with things on it and a shop with stock behind the till.
    ///
    /// Fills from the bottom, and that is deliberate too. A rack that filled top-down would empty
    /// upward, and a nearly-empty shelf with its last bars stranded on the top row reads as a display
    /// rather than as a supply running out.
    ///
    /// Behind the counter FROM THE CUSTOMER'S SIDE, which is the player's side, and standing far
    /// enough back to leave him the whole unloading strip. It carries no collider: the yard's rule for
    /// scenery is that nothing decorative may ever be something to walk into, and this one stands in
    /// the middle of the floor rather than against a wall.
    /// </summary>
    public sealed class CounterShelf : MonoBehaviour
    {
        private const string ShelfResource = "Market/Models/SM_Market_Shelf";

        /// <summary>Bars across one board, and how many boards. Nine on show at a full counter.</summary>
        private const int PerRow = 3, Rows = 3;

        /// <summary>
        /// Where the boards sit in the model, measured from its foot. Copied from the model rather than
        /// read off it: reading means keeping the mesh CPU-side for the sake of three numbers that have
        /// not moved since it was built.
        /// </summary>
        private static readonly float[] BoardHeights = { 0.34f, 1.12f, 1.90f };

        /// <summary>Half a board's thickness plus half a bar's, which is what lifts one onto the other.</summary>
        private const float BarLift = 0.045f + 0.16f;

        /// <summary>Gap between two bars along a board. The bar is 1.05 wide and the rack 3.92 inside.</summary>
        private const float BarSpacing = 1.2f;

        private SellCounter _counter;
        private readonly Transform[] _bars = new Transform[PerRow * Rows];
        private int _showing = -1;
        private float _refresh;

        /// <summary>
        /// Stands the rack behind the counter and hangs its bars, all of them hidden to start with.
        ///
        /// Every bar is built up front and switched on and off from then on. A rack that instantiated
        /// as it filled would allocate on the exact frames the yard is busiest — a good counter turns
        /// over several bars a second — which is the shape of hitch that only shows up twenty minutes
        /// into a session.
        /// </summary>
        public static CounterShelf Build(Transform yardRoot, Vector3 at, SellCounter counter,
                                         Material ore, MarketTheme.Palette theme, MarketPrefabs prefabs)
        {
            var holder = new GameObject("Vitrin");
            holder.transform.SetParent(yardRoot, false);
            holder.transform.localPosition = at;
            // Turned to face south, toward the counter and the camera both. The model's own front is
            // +Z, and +Z in the yard points away from everything that ever looks at this.
            holder.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var shelf = holder.AddComponent<CounterShelf>();
            shelf._counter = counter;
            shelf.BuildRack(theme, ore, prefabs);
            return shelf;
        }

        private void BuildRack(MarketTheme.Palette theme, Material ore, MarketPrefabs prefabs)
        {
            var prefab = Resources.Load<GameObject>(ShelfResource);
            if (prefab != null)
            {
                GameObject body = Instantiate(prefab, transform, false);
                body.name = "Raf";
                Material timber = MarketSurfaces.Get(theme.Trim, MarketSurfaces.Finish.Wood);
                var parts = body.GetComponentsInChildren<MeshRenderer>(true);
                for (int i = 0; i < parts.Length; i++) parts[i].sharedMaterial = timber;
                var hazards = body.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < hazards.Length; i++) Destroy(hazards[i]);
            }

            for (int i = 0; i < _bars.Length; i++)
            {
                int row = i / PerRow, column = i % PerRow;
                Transform bar = MarketPrefabs.SpawnCargo(prefabs != null ? prefabs.Bar : null, transform,
                                                         "Kulce", new Vector3(1.05f, 0.32f, 0.62f), ore);
                bar.localPosition = new Vector3((column - (PerRow - 1) * 0.5f) * BarSpacing,
                                                BoardHeights[row] + BarLift, 0.06f);
                // A few degrees off square, alternating, so nine bars do not read as a printed pattern.
                bar.localRotation = Quaternion.Euler(0f, (i % 2 == 0 ? 4f : -5f), 0f);
                bar.gameObject.SetActive(false);
                _bars[i] = bar;
            }
        }

        private void Update()
        {
            // Four times a second. The counter really does turn over several bars a second at a good
            // rate, and a rack that redrew itself in step would be flickering rather than filling.
            _refresh -= Time.deltaTime;
            if (_refresh > 0f) return;
            _refresh = 0.25f;
            Show(Wanted());
        }

        private int Wanted()
        {
            if (_counter == null) return 0;
            int capacity = Mathf.Max(1, _counter.ShelfCapacity);
            float fraction = Mathf.Clamp01(_counter.Stocked / (float)capacity);
            // Rounded UP off zero: one bar on the counter has to put one bar on the rack, or a shop
            // with something to sell looks shut.
            int want = Mathf.CeilToInt(fraction * _bars.Length);
            return Mathf.Clamp(want, _counter.Stocked > 0 ? 1 : 0, _bars.Length);
        }

        private void Show(int count)
        {
            if (count == _showing) return;
            for (int i = 0; i < _bars.Length; i++)
                if (_bars[i] != null) _bars[i].gameObject.SetActive(i < count);
            _showing = count;
        }
    }
}
