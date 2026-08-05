using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The marked-out plot that stands where a locked expansion will one day be built: a paved pad
    /// inside a dashed border.
    ///
    /// Locked buildings used to be shown as translucent copies of themselves. The intent was to let the
    /// player see the island's future, but a dozen see-through buildings scattered over open grass read as
    /// rendering errors rather than as plans, and they were the untidiest thing on the map.
    ///
    /// A surveyed plot says the same thing without the clutter: this ground is spoken for, something goes
    /// here, it is not built yet. It is the convention every tycoon map uses for buildable ground, so it
    /// needs no explaining.
    ///
    /// The whole plot is one mesh with two submeshes, so it costs one draw call however many are on screen.
    /// </summary>
    public static class BuildPlot
    {
        private const int Pad = 0, Mark = 1;

        /// <summary>
        /// Lays out a plot centred on <paramref name="centre"/> and squared to <paramref name="facing"/>.
        /// </summary>
        /// <param name="halfX">Half-width across <paramref name="facing"/>.</param>
        /// <param name="halfZ">Half-depth along it.</param>
        /// <param name="topY">Ground height the markings sit on.</param>
        public static GameObject Build(Transform parent, string name, Vector3 centre, Vector3 facing,
                                       float halfX, float halfZ, float topY, Material pad, Material mark)
        {
            Vector3 f = Flat(facing);
            f = f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
            Vector3 r = new Vector3(f.z, 0f, -f.x), up = Vector3.up;
            Vector3 c = new Vector3(centre.x, topY, centre.z);

            var mb = new BoxMeshBuilder();
            mb.AddBox(c, r, up, f, new Vector3(halfX, 0.05f, halfZ), Pad);

            // Dashed border. Each side is walked in steps of dash+gap and the run is scaled so a whole
            // number of dashes fits exactly, which is what keeps the four corners meeting cleanly.
            const float dash = 2.3f, gap = 1.7f, thick = 0.42f;
            Edge(mb, c + f * halfZ, r, f, halfX, dash, gap, thick, up);   // far
            Edge(mb, c - f * halfZ, r, f, halfX, dash, gap, thick, up);   // near
            Edge(mb, c + r * halfX, f, r, halfZ, dash, gap, thick, up);   // right
            Edge(mb, c - r * halfX, f, r, halfZ, dash, gap, thick, up);   // left

            var go = new GameObject(name);
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;

            var mesh = new Mesh { name = name };
            mb.Apply(mesh);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { pad, mark };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // flat ground marking
            return go;
        }

        /// <summary>One dashed side, centred on <paramref name="mid"/> and running along <paramref name="dir"/>.</summary>
        private static void Edge(BoxMeshBuilder mb, Vector3 mid, Vector3 dir, Vector3 across, float half,
                                 float dash, float gap, float thick, Vector3 up)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(half * 2f / (dash + gap)));
            float step = half * 2f / n;
            for (int i = 0; i < n; i++)
            {
                float t = -half + step * (i + 0.5f);
                Vector3 p = mid + dir * t + up * 0.01f;
                // Half-extent, so the dash covers 58% of its step and the gap is the rest. Passing the
                // whole 58% here made every dash 116% of its step, which is a solid line.
                mb.AddBox(p, dir, up, across, new Vector3(step * 0.29f, 0.05f, thick), Mark);
            }
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
