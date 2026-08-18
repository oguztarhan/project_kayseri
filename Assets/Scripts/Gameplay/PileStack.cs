using System;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The heap of raw ore or finished bars sitting on a yard pad, drawn as a stack of chunks.
    ///
    /// The count of chunks tracks the <b>absolute</b> stored amount rather than the fill fraction, and the
    /// footprint widens with the yard's capacity. That ordering matters: driving the visual off the
    /// fraction meant buying a Capacity upgrade instantly shrank the pile, so the one purchase whose whole
    /// point is a bigger yard was the one that made the yard look emptier.
    ///
    /// Everything lives in one mesh that is rebuilt only when the chunk count or the grid actually
    /// changes, so a full yard costs a single draw call and no steady-state allocation.
    /// </summary>
    public sealed class PileStack
    {
        /// <summary>
        /// The two shapes a heap can take, and they are two different readings of the same number.
        ///
        /// <see cref="Layout.Pyramid"/> is the island's: a few big boulders coning up off a mine pad,
        /// which says "raw material, dumped here". <see cref="Layout.Pool"/> is the market's: a wide
        /// shallow basin of small pieces spread over the whole slab, which says "sorted stock, ready to
        /// carry". A market heap that coned up hid its own volume behind its front face — the player is
        /// looking at it from a fixed isometric angle and could only ever see one side of the cone.
        /// Spread flat, the same amount of stock is all of it on screen at once.
        /// </summary>
        public enum Layout { Pyramid, Pool }

        private const int MinGrid = 2, MaxGrid = 5;

        /// <summary>
        /// How many widths the pool steps through between a puddle and the full slab.
        ///
        /// Quantised rather than continuous because the footprint is what changes when it changes, and a
        /// pool that resized itself off every nudge in the delivery rate would rebuild its mesh several
        /// times a second for a difference of one ring of pieces.
        /// </summary>
        private const int PoolSteps = 6;

        /// <summary>
        /// Hard ceiling on the pieces in a pool, and it is a vertex budget rather than a taste one.
        /// Every piece is a box of 24 vertices, and the mesh uses the default 16-bit index buffer —
        /// 700 leaves comfortable room under the 65535 that buffer can address.
        /// </summary>
        private const int MaxPoolPieces = 700;

        /// <summary>How far in each successive pool layer is drawn, in cells. What makes the rim taper.</summary>
        private const float PoolInsetCells = 1.15f;

        /// <summary>
        /// Fastest the mesh is rebuilt for a small change, and the market's pool is what needs it.
        ///
        /// One piece is a tenth of a bar there, so standing on the pad takes seventy pieces a second off
        /// the heap — a rebuild of every box in the pool on every frame, for a difference of one box.
        /// Coalesced to twenty a second the pile still visibly drains as fast as the player picks up, and
        /// the work drops by two thirds. A change too big to be a pickup — a yard filling up while the
        /// player was away, or a capacity upgrade — goes through immediately, because that one is a
        /// change the player is watching for.
        /// </summary>
        private const float RebuildInterval = 0.05f;
        private const int BigJump = 24;

        private readonly Mesh _mesh;
        private readonly MeshRenderer _renderer;
        private readonly BoxMeshBuilder _builder = new BoxMeshBuilder();
        private readonly float _unitsPerChunk;
        private readonly float _cell;      // pad footprint divided by the widest grid, so it never overhangs
        private readonly float _baseY;

        private Vector3[] _slots = new Vector3[0];   // pyramid positions, ordered so the heap grows as a cone
        private int _grid = -1, _shown = -1;
        private float _rebuiltAt = float.NegativeInfinity;
        private readonly int _maxGrid;

        private readonly Layout _layout;
        private readonly int _poolLayers;
        private readonly float _poolAX, _poolAZ;     // half-extents of the pad the pool may cover

        // Real ore/product geometry, extracted once. Null falls back to plain boxes, which is what the
        // piles looked like before: readable, but unmistakably placeholder cubes.
        private readonly Vector3[] _srcVerts, _srcNormals;
        private readonly int[] _srcTris;
        private readonly float _srcFit;   // scale that makes one source mesh fill one grid cell

        /// <param name="maxGrid">
        /// Widest base the pyramid may reach. The island's yards keep the original 5 (a 55-chunk heap of
        /// big lumps). Ignored by <see cref="Layout.Pool"/>, which is bounded by the pad and by
        /// <see cref="MaxPoolPieces"/> instead.
        /// </param>
        /// <param name="layout">Cone or basin. See <see cref="Layout"/>.</param>
        /// <param name="poolLayers">
        /// How deep a full pool gets. Small on purpose — this is the number that decides whether the
        /// market's stock reads as a spread of goods or as another cone.
        /// </param>
        public PileStack(Transform pad, Material mat, float unitsPerChunk, string name, Mesh chunkMesh = null,
                         float cellScale = 1f, int maxGrid = MaxGrid,
                         Layout layout = Layout.Pyramid, int poolLayers = 3)
        {
            _layout = layout;
            _maxGrid = Math.Max(MinGrid, maxGrid);
            _poolLayers = Math.Max(1, poolLayers);
            var pr = pad.GetComponentInChildren<Renderer>();
            Vector3 size = pr != null ? pr.bounds.size : new Vector3(8f, 1f, 8f);
            _baseY = pr != null ? pr.bounds.max.y : pad.position.y;
            // Sized so a level-0 yard (a 3-wide grid) still covers most of its pad. Dividing by MaxGrid
            // instead left the early game showing a handful of pebbles on a big empty slab.
            //
            // The 1.4 floor is a PYRAMID floor: a cone of five boulders wants chunks you can pick out
            // individually. A pool wants the opposite and has to be allowed under it, or "many small
            // pieces" quietly becomes "the same big lumps, laid flat".
            float baseCell = Mathf.Min(size.x, size.z) * 0.92f / 4.3f;
            float scale = Mathf.Max(0.1f, cellScale);
            _cell = layout == Layout.Pool
                ? Mathf.Max(0.26f, baseCell * scale)
                : Mathf.Max(1.4f, baseCell) * scale;
            _unitsPerChunk = Mathf.Max(0.01f, unitsPerChunk);
            // Both axes, not the smaller one twice: the market's pad is 17 by 15 and a pool measured off
            // the short side would leave a bare metre of slab down each long edge.
            _poolAX = size.x * 0.46f;
            _poolAZ = size.z * 0.46f;

            var go = new GameObject(name);
            go.transform.SetParent(pad, true);
            go.transform.SetPositionAndRotation(new Vector3(pad.position.x, _baseY, pad.position.z), Quaternion.identity);
            go.transform.localScale = Vector3.one;

            _mesh = new Mesh { name = name };
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = mat;
            _renderer.enabled = false;

            if (chunkMesh != null)
            {
                _srcVerts = chunkMesh.vertices;
                _srcNormals = chunkMesh.normals;
                _srcTris = chunkMesh.triangles;       // flattened across submeshes by Mesh.triangles
                Vector3 ext = chunkMesh.bounds.size;
                float widest = Mathf.Max(0.001f, Mathf.Max(ext.x, Mathf.Max(ext.y, ext.z)));
                _srcFit = _cell * 0.95f / widest;     // one chunk ≈ one cell, whatever the asset's own scale
            }
        }

        public void Set(double amount, double capacity)
        {
            int want = (int)Math.Round(capacity / _unitsPerChunk);
            int grid;
            if (_layout == Layout.Pool)
            {
                // Pieces go as the AREA of the pool, so the width the capacity asks for goes as its root.
                float full = PoolPieceEstimate(1f);
                float t = full > 0f ? Mathf.Sqrt(Mathf.Clamp01(want / full)) : 1f;
                grid = Mathf.Clamp(Mathf.CeilToInt(t * PoolSteps), 1, PoolSteps);
            }
            else
            {
                grid = MinGrid;
                while (grid < _maxGrid && Pyramid(grid) < want) grid++;
            }

            // A new footprint always redraws, whatever the count says: the slots the last mesh was built
            // from no longer exist.
            bool regrid = grid != _grid;
            if (regrid) { _grid = grid; BuildSlots(grid); }

            int shown = Mathf.Clamp((int)Math.Round(amount / _unitsPerChunk), 0, _slots.Length);
            if (shown == _shown && !regrid) return;
            if (!regrid && Math.Abs(shown - _shown) < BigJump &&
                Time.unscaledTime - _rebuiltAt < RebuildInterval) return;

            _shown = shown;
            _rebuiltAt = Time.unscaledTime;
            Rebuild();
        }

        private void Rebuild()
        {
            if (_shown <= 0) { _renderer.enabled = false; return; }
            bool pool = _layout == Layout.Pool;
            // Smaller than its cell in a pool, so the gaps between pieces stay visible and the basin
            // reads as loose stock rather than as one poured slab.
            float half = _cell * (pool ? 0.36f : 0.42f);
            _builder.Clear();
            for (int i = 0; i < _shown && i < _slots.Length; i++)
            {
                // A per-chunk yaw derived from the slot index keeps the stack from reading as a pixel grid
                // without needing any stored state.
                float yawDeg = i * 47 % 90;
                float yaw = yawDeg * Mathf.Deg2Rad;
                if (_srcVerts != null)
                {
                    _builder.AddMesh(_srcVerts, _srcNormals, _srcTris, _slots[i],
                                     Quaternion.Euler(0f, yawDeg, 0f), _srcFit, 0);
                }
                else
                {
                    Vector3 right = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
                    Vector3 fwd = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                    // Pool pieces vary in height off the same deterministic hash the positions are
                    // jittered with. Uniform ones lay a suspiciously level lid over the basin.
                    float tall = pool ? half * (0.55f + BoxMeshBuilder.Hash(31, i) * 0.65f) : half * 0.8f;
                    _builder.AddBox(_slots[i], right, Vector3.up, fwd, new Vector3(half, tall, half), 0);
                }
            }
            _builder.Apply(_mesh);
            _renderer.enabled = true;
        }

        private void BuildSlots(int grid)
        {
            if (_layout == Layout.Pool) BuildPoolSlots(grid);
            else BuildPyramidSlots(grid);
        }

        /// <summary>Pyramid slot positions in fill order: lowest and most central first, so the heap cones up.</summary>
        private void BuildPyramidSlots(int grid)
        {
            int total = Pyramid(grid);
            if (_slots.Length != total) _slots = new Vector3[total];
            var score = new float[total];
            float layerH = _cell * 0.72f;
            int k = 0;
            for (int layer = 0; layer < grid; layer++)
            {
                int w = grid - layer;
                float off = (w - 1) * 0.5f;
                for (int i = 0; i < w; i++)
                    for (int j = 0; j < w; j++)
                    {
                        float x = (i - off) * _cell, z = (j - off) * _cell;
                        float y = (layer + 0.5f) * layerH;
                        _slots[k] = new Vector3(x, y, z);   // local: the pad object carries the placement
                        score[k] = y + Mathf.Sqrt(x * x + z * z) * 0.30f;
                        k++;
                    }
            }
            Array.Sort(score, _slots);
        }

        /// <summary>
        /// Pool slot positions: an ellipse inscribed in the pad, a few shallow layers deep, each one
        /// drawn in off the one below so the edge tapers instead of standing as a wall.
        ///
        /// Filled by layer and then outward from the middle, which is the order that makes it read as a
        /// pool rather than a heap — stock arriving SPREADS across the slab and only then gets deeper.
        /// Filling by distance alone would build a dome, which is the cone this exists to avoid.
        /// </summary>
        private void BuildPoolSlots(int step)
        {
            float widthAX = Mathf.Max(_cell * 1.2f, _poolAX * step / (float)PoolSteps);
            float widthAZ = Mathf.Max(_cell * 1.2f, _poolAZ * step / (float)PoolSteps);
            // Shallow: a layer is barely over half a piece tall, so three of them is a basin with a lip
            // rather than a tower three storeys high.
            float layerH = _cell * 0.46f;

            var slots = new System.Collections.Generic.List<Vector3>(256);
            var score = new System.Collections.Generic.List<float>(256);
            for (int layer = 0; layer < _poolLayers; layer++)
            {
                float rx = widthAX - layer * _cell * PoolInsetCells;
                float rz = widthAZ - layer * _cell * PoolInsetCells;
                if (rx < _cell * 0.6f || rz < _cell * 0.6f) break;

                int nx = Mathf.FloorToInt(rx / _cell);
                int nz = Mathf.FloorToInt(rz / _cell);
                float y = (layer + 0.5f) * layerH;
                for (int i = -nx; i <= nx; i++)
                    for (int j = -nz; j <= nz; j++)
                    {
                        float x = i * _cell, z = j * _cell;
                        float fx = x / rx, fz = z / rz;
                        float radial = fx * fx + fz * fz;
                        if (radial > 1f) continue;          // the ellipse is what makes it a pool, not a tray

                        int seed = layer * 977 + (i + 64) * 131 + (j + 64);
                        x += (BoxMeshBuilder.Hash(5, seed) - 0.5f) * _cell * 0.42f;
                        z += (BoxMeshBuilder.Hash(9, seed) - 0.5f) * _cell * 0.42f;
                        slots.Add(new Vector3(x, y, z));
                        // Layer first and distance second, with a pinch of the same hash so the growing
                        // edge is ragged rather than a perfect expanding ring.
                        score.Add(layer * 8f + radial + BoxMeshBuilder.Hash(13, seed) * 0.06f);
                    }
            }

            var order = score.ToArray();
            var points = slots.ToArray();
            Array.Sort(order, points);

            int total = Mathf.Min(points.Length, MaxPoolPieces);
            if (_slots.Length != total) _slots = new Vector3[total];
            Array.Copy(points, _slots, total);
        }

        /// <summary>
        /// Roughly how many pieces a pool of the given width fraction holds — ellipse area over cell area,
        /// summed down the layers. Only ever used to pick a width step, so an estimate is what is wanted:
        /// generating each candidate to count it exactly would allocate a list per capacity change.
        /// </summary>
        private float PoolPieceEstimate(float fraction)
        {
            float total = 0f;
            float ax = _poolAX * fraction, az = _poolAZ * fraction;
            for (int layer = 0; layer < _poolLayers; layer++)
            {
                float rx = ax - layer * _cell * PoolInsetCells;
                float rz = az - layer * _cell * PoolInsetCells;
                if (rx < _cell * 0.6f || rz < _cell * 0.6f) break;
                total += Mathf.PI * rx * rz / (_cell * _cell);
            }
            return Mathf.Min(total, MaxPoolPieces);
        }

        private static int Pyramid(int grid)
        {
            int n = 0;
            for (int layer = 0; layer < grid; layer++) { int w = grid - layer; n += w * w; }
            return n;
        }
    }
}
