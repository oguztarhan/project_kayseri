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
        private const int MinGrid = 2, MaxGrid = 5;

        private readonly Mesh _mesh;
        private readonly MeshRenderer _renderer;
        private readonly BoxMeshBuilder _builder = new BoxMeshBuilder();
        private readonly float _unitsPerChunk;
        private readonly float _cell;      // pad footprint divided by the widest grid, so it never overhangs
        private readonly float _baseY;

        private Vector3[] _slots = new Vector3[0];   // pyramid positions, ordered so the heap grows as a cone
        private int _grid = -1, _shown = -1;

        // Real ore/product geometry, extracted once. Null falls back to plain boxes, which is what the
        // piles looked like before: readable, but unmistakably placeholder cubes.
        private readonly Vector3[] _srcVerts, _srcNormals;
        private readonly int[] _srcTris;
        private readonly float _srcFit;   // scale that makes one source mesh fill one grid cell

        public PileStack(Transform pad, Material mat, float unitsPerChunk, string name, Mesh chunkMesh = null)
        {
            var pr = pad.GetComponentInChildren<Renderer>();
            Vector3 size = pr != null ? pr.bounds.size : new Vector3(8f, 1f, 8f);
            _baseY = pr != null ? pr.bounds.max.y : pad.position.y;
            // Sized so a level-0 yard (a 3-wide grid) still covers most of its pad. Dividing by MaxGrid
            // instead left the early game showing a handful of pebbles on a big empty slab.
            _cell = Mathf.Max(1.4f, Mathf.Min(size.x, size.z) * 0.92f / 4.3f);
            _unitsPerChunk = Mathf.Max(0.01f, unitsPerChunk);

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
            int grid = MinGrid;
            while (grid < MaxGrid && Pyramid(grid) < want) grid++;

            int shown = Mathf.Clamp((int)Math.Round(amount / _unitsPerChunk), 0, Pyramid(grid));
            if (grid == _grid && shown == _shown) return;
            if (grid != _grid) { _grid = grid; BuildSlots(grid); }
            _shown = shown;
            Rebuild();
        }

        private void Rebuild()
        {
            if (_shown <= 0) { _renderer.enabled = false; return; }
            float half = _cell * 0.42f;
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
                    _builder.AddBox(_slots[i], right, Vector3.up, fwd, new Vector3(half, half * 0.8f, half), 0);
                }
            }
            _builder.Apply(_mesh);
            _renderer.enabled = true;
        }

        /// <summary>Pyramid slot positions in fill order: lowest and most central first, so the heap cones up.</summary>
        private void BuildSlots(int grid)
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

        private static int Pyramid(int grid)
        {
            int n = 0;
            for (int layer = 0; layer < grid; layer++) { int w = grid - layer; n += w * w; }
            return n;
        }
    }
}
