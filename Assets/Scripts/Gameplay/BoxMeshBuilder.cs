using System.Collections.Generic;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Accumulates flat-shaded boxes and discs into a single mesh, one submesh per material slot.
    /// The vertex lists survive between rebuilds, so a view that regenerates whenever its shape changes
    /// — the ore piles do, several times a second — stops allocating once it has reached its largest size.
    /// </summary>
    public sealed class BoxMeshBuilder
    {
        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Vector3> _norms = new List<Vector3>();
        private readonly List<List<int>> _tris = new List<List<int>>();

        public void Clear()
        {
            _verts.Clear();
            _norms.Clear();
            for (int i = 0; i < _tris.Count; i++) _tris[i].Clear();
        }

        /// <summary>
        /// A box centred on <paramref name="centre"/>, oriented by the three unit axes, extending
        /// <paramref name="half"/> along each of them.
        /// </summary>
        public void AddBox(Vector3 centre, Vector3 right, Vector3 up, Vector3 forward, Vector3 half, int submesh)
        {
            Vector3 r = right * half.x, u = up * half.y, f = forward * half.z;
            // Each face is given the two half-extent vectors whose cross product points outward, which is
            // what makes the winding and the normal agree without a per-face sign test.
            AddFace(centre + f, r, u, submesh);
            AddFace(centre - f, u, r, submesh);
            AddFace(centre + r, u, f, submesh);
            AddFace(centre - r, f, u, submesh);
            AddFace(centre + u, f, r, submesh);
            AddFace(centre - u, r, f, submesh);
        }

        /// <summary>
        /// Stamps a copy of a source mesh, given its already-extracted arrays. Callers cache those once
        /// (reading <c>Mesh.vertices</c> allocates a fresh array every time) and hand them in on each
        /// rebuild, which is what keeps a growing ore pile allocation-free.
        ///
        /// Every source submesh is flattened into <paramref name="submesh"/>: these are ore chunks and
        /// metal bars drawn in one flat per-island tint, so their original material splits carry no
        /// information worth keeping.
        /// </summary>
        public void AddMesh(Vector3[] srcVerts, Vector3[] srcNormals, int[] srcTris,
                            Vector3 centre, Quaternion rot, float scale, int submesh)
        {
            List<int> tris = Tris(submesh);
            int v0 = _verts.Count;
            for (int i = 0; i < srcVerts.Length; i++)
            {
                _verts.Add(centre + rot * (srcVerts[i] * scale));
                _norms.Add(srcNormals != null && i < srcNormals.Length ? rot * srcNormals[i] : Vector3.up);
            }
            for (int i = 0; i < srcTris.Length; i++) tris.Add(v0 + srcTris[i]);
        }

        /// <summary>
        /// A flat-shaded peak: an irregular cone from a base ring up to <paramref name="apex"/>, one
        /// triangle per side. Every triangle carries its own three vertices and one normal, which is what
        /// keeps the facets hard — shared vertices would smooth the silhouette into a dune.
        ///
        /// The rim radius wobbles by <paramref name="jitter"/> off a cheap deterministic hash of
        /// <paramref name="seed"/>, so a range of these does not read as a row of identical tents and
        /// still comes out the same on every launch without anything being stored.
        /// </summary>
        public void AddPeak(Vector3 baseCentre, Vector3 apex, float radius, int sides, float jitter,
                            int seed, int submesh)
        {
            if (sides < 3) sides = 3;
            List<int> tris = Tris(submesh);
            for (int i = 0; i < sides; i++)
            {
                Vector3 p0 = Rim(baseCentre, radius, sides, i, jitter, seed);
                Vector3 p1 = Rim(baseCentre, radius, sides, i + 1, jitter, seed);
                // Wound to match AddFace's convention: cross(edge1, edge2) points along the normal.
                Vector3 n = Vector3.Cross(apex - p0, p1 - apex);
                n = n.sqrMagnitude > 1e-8f ? n.normalized : Vector3.up;
                int v0 = _verts.Count;
                _verts.Add(p0); _verts.Add(apex); _verts.Add(p1);
                _norms.Add(n); _norms.Add(n); _norms.Add(n);
                tris.Add(v0); tris.Add(v0 + 1); tris.Add(v0 + 2);
            }
        }

        private static Vector3 Rim(Vector3 c, float radius, int sides, int i, float jitter, int seed)
        {
            int k = i % sides;
            float ang = k / (float)sides * Mathf.PI * 2f;
            float r = radius * (1f + (Hash(seed, k) - 0.5f) * 2f * jitter);
            return c + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
        }

        /// <summary>Deterministic 0..1 scatter. Enough disorder for scenery, and it allocates nothing.</summary>
        public static float Hash(int seed, int i)
        {
            float v = Mathf.Sin(seed * 12.9898f + i * 78.233f) * 43758.5453f;
            return v - Mathf.Floor(v);
        }

        /// <summary>An upward-facing disc in the XZ plane — a ground decal, so it has no underside.</summary>
        public void AddDisc(Vector3 centre, float radius, int segments, int submesh)
        {
            List<int> tris = Tris(submesh);
            int hub = _verts.Count;
            _verts.Add(centre); _norms.Add(Vector3.up);
            for (int i = 0; i < segments; i++)
            {
                float ang = i / (float)segments * Mathf.PI * 2f;
                _verts.Add(centre + new Vector3(Mathf.Cos(ang) * radius, 0f, Mathf.Sin(ang) * radius));
                _norms.Add(Vector3.up);
            }
            for (int i = 0; i < segments; i++)
            {
                tris.Add(hub);
                tris.Add(hub + 1 + (i + 1) % segments);
                tris.Add(hub + 1 + i);
            }
        }

        public void Apply(Mesh mesh)
        {
            mesh.Clear();
            mesh.SetVertices(_verts);
            mesh.SetNormals(_norms);
            mesh.subMeshCount = _tris.Count;
            for (int i = 0; i < _tris.Count; i++) mesh.SetTriangles(_tris[i], i, false);
            mesh.RecalculateBounds();
        }

        /// <summary>Materials needed to draw what has been accumulated so far.</summary>
        public int SubmeshCount => _tris.Count;

        private void AddFace(Vector3 faceCentre, Vector3 p, Vector3 q, int submesh)
        {
            List<int> tris = Tris(submesh);
            int v0 = _verts.Count;
            Vector3 n = Vector3.Cross(p, q).normalized;
            _verts.Add(faceCentre - p - q);
            _verts.Add(faceCentre + p - q);
            _verts.Add(faceCentre + p + q);
            _verts.Add(faceCentre - p + q);
            for (int i = 0; i < 4; i++) _norms.Add(n);
            tris.Add(v0); tris.Add(v0 + 1); tris.Add(v0 + 2);
            tris.Add(v0); tris.Add(v0 + 2); tris.Add(v0 + 3);
        }

        private List<int> Tris(int submesh)
        {
            while (_tris.Count <= submesh) _tris.Add(new List<int>());
            return _tris[submesh];
        }
    }
}
