using System.Collections.Generic;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// A box mesh built at its real size, with UVs measured in WORLD UNITS rather than across the
    /// face. This is what lets the yard wear a texture at all.
    ///
    /// The yard used to be scaled unit cubes: <c>CreatePrimitive(Cube)</c> with a 46x4.6x0.9 scale on
    /// the transform. That is fine for flat colour and useless for anything else — a unit cube's UVs
    /// run 0..1 across every face, so one texture would be stretched fifty times along a wall and
    /// squashed to nothing across its thickness. Every face here instead gets
    /// <c>side length x tiles-per-unit</c> of texture, so a bolt on the door post is the same size as
    /// a bolt on the wall behind it.
    ///
    /// V IS ALWAYS THE VERTICAL AXIS on the four upright faces, and it is not a detail: the wall map's
    /// ribs run down its v axis, and a face that took its v across the ground would lay that wall on
    /// its side. The two horizontal faces have no up to point at and take x and z.
    ///
    /// Meshes are CACHED by size and tiling. Eight yards are built from one layout, so the whole hall's
    /// several hundred boxes come out of about forty distinct meshes — and the seven yards nobody is
    /// standing in cost nothing to have built.
    /// </summary>
    public static class MarketBoxMesh
    {
        private readonly struct Key : System.IEquatable<Key>
        {
            private readonly int _x, _y, _z, _t;

            public Key(Vector3 size, float tiles)
            {
                // Millimetre quantisation. The layout is written in tidy numbers, but a few sizes are
                // computed off half-widths and land a float wobble apart; unquantised they would be
                // two meshes that draw identically.
                _x = Mathf.RoundToInt(size.x * 1000f);
                _y = Mathf.RoundToInt(size.y * 1000f);
                _z = Mathf.RoundToInt(size.z * 1000f);
                _t = Mathf.RoundToInt(tiles * 10000f);
            }

            public bool Equals(Key o) => _x == o._x && _y == o._y && _z == o._z && _t == o._t;
            public override bool Equals(object o) => o is Key k && Equals(k);
            public override int GetHashCode()
                => ((((_x * 397) ^ _y) * 397) ^ _z) * 397 ^ _t;
        }

        private static readonly Dictionary<Key, Mesh> Cache = new Dictionary<Key, Mesh>(64);

        // Scratch, reused across every build. These run at scene load rather than per frame, but the
        // hall builds a few hundred boxes in one go and there is no reason to hand the collector
        // several hundred short-lived arrays on the frame the player is watching a loading screen.
        private static readonly List<Vector3> Verts = new List<Vector3>(24);
        private static readonly List<Vector3> Norms = new List<Vector3>(24);
        private static readonly List<Vector2> Uvs = new List<Vector2>(24);
        private static readonly List<int> Tris = new List<int>(36);

        /// <summary>
        /// The mesh for a box of <paramref name="size"/> whose texture repeats
        /// <paramref name="tilesPerUnit"/> times per world unit. Zero tiling still gets UVs — a
        /// degenerate 0..0 is what an untextured surface wants, and it keeps one code path.
        /// </summary>
        public static Mesh Get(Vector3 size, float tilesPerUnit)
        {
            var key = new Key(size, tilesPerUnit);
            Mesh cached;
            if (Cache.TryGetValue(key, out cached) && cached != null) return cached;

            Vector3 h = size * 0.5f;
            Verts.Clear(); Norms.Clear(); Uvs.Clear(); Tris.Clear();

            // p is the face's horizontal axis, q its vertical one, and cross(p, q) is the outward
            // normal — the same winding rule BoxMeshBuilder uses, so the two agree about which way a
            // face is looking. On the four sides q is +Y every time; on the top and bottom it is
            // whichever of x/z leaves the cross product pointing out of the box.
            Face(new Vector3(0f, 0f, h.z), new Vector3(h.x, 0f, 0f), new Vector3(0f, h.y, 0f), tilesPerUnit);
            Face(new Vector3(0f, 0f, -h.z), new Vector3(-h.x, 0f, 0f), new Vector3(0f, h.y, 0f), tilesPerUnit);
            Face(new Vector3(h.x, 0f, 0f), new Vector3(0f, 0f, -h.z), new Vector3(0f, h.y, 0f), tilesPerUnit);
            Face(new Vector3(-h.x, 0f, 0f), new Vector3(0f, 0f, h.z), new Vector3(0f, h.y, 0f), tilesPerUnit);
            Face(new Vector3(0f, h.y, 0f), new Vector3(h.x, 0f, 0f), new Vector3(0f, 0f, -h.z), tilesPerUnit);
            Face(new Vector3(0f, -h.y, 0f), new Vector3(h.x, 0f, 0f), new Vector3(0f, 0f, h.z), tilesPerUnit);

            var mesh = new Mesh { name = "Kutu" };
            mesh.SetVertices(Verts);
            mesh.SetNormals(Norms);
            mesh.SetUVs(0, Uvs);
            mesh.SetTriangles(Tris, 0, true);
            // Tangents, or the normal maps do nothing. Cheap here and impossible later — the mesh is
            // shared by every box of this size in the hall, so this runs about forty times in total.
            mesh.RecalculateTangents();
            mesh.UploadMeshData(true);

            Cache[key] = mesh;
            return mesh;
        }

        /// <summary>
        /// A one-by-one quad facing +Z with UVs running the whole way across it, for the things in the
        /// yard that are a picture rather than a surface: the mood badges over the customers' heads.
        ///
        /// Separate from <see cref="Get"/> and not a thin box, because the two want opposite things. A
        /// box's UVs are measured in world units so a texture keeps its size however big the box is;
        /// a badge wants exactly one copy of its icon stretched over exactly one quad, whatever size
        /// that quad ends up.
        /// </summary>
        public static Mesh Quad()
        {
            if (_quad != null) return _quad;
            _quad = new Mesh { name = "Karo" };
            _quad.SetVertices(new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f)
            });
            _quad.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            _quad.SetUVs(0, new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f)
            });
            _quad.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0, true);
            _quad.UploadMeshData(true);
            return _quad;
        }

        private static Mesh _quad;

        private static void Face(Vector3 centre, Vector3 p, Vector3 q, float tiles)
        {
            int v0 = Verts.Count;
            Vector3 n = Vector3.Cross(p, q).normalized;
            Verts.Add(centre - p - q);
            Verts.Add(centre + p - q);
            Verts.Add(centre + p + q);
            Verts.Add(centre - p + q);
            for (int i = 0; i < 4; i++) Norms.Add(n);

            float u = p.magnitude * 2f * tiles;
            float v = q.magnitude * 2f * tiles;
            Uvs.Add(new Vector2(0f, 0f));
            Uvs.Add(new Vector2(u, 0f));
            Uvs.Add(new Vector2(u, v));
            Uvs.Add(new Vector2(0f, v));

            Tris.Add(v0); Tris.Add(v0 + 1); Tris.Add(v0 + 2);
            Tris.Add(v0); Tris.Add(v0 + 2); Tris.Add(v0 + 3);
        }
    }
}
