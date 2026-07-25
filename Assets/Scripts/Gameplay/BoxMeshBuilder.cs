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
