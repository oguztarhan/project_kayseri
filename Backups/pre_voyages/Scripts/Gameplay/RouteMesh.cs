using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Builds the visible road, rail and site-pad geometry for an operation, straight from the same
    /// endpoints the vehicles drive between.
    ///
    /// The islands ship with painted road and rail decoration, but it was authored against a layout the
    /// sim no longer uses, so trucks drove across bare ground next to track that led somewhere else —
    /// which is what made the maps read as an unfinished tangle. Generating the track from the route
    /// endpoints means it can never disagree with the motion, on any island, including the ones whose
    /// meshes carry a different number of painted segments.
    ///
    /// Each call produces one GameObject with one mesh, so a full chain costs a handful of draw calls.
    /// </summary>
    public static class RouteMesh
    {
        private const int Surface = 0, Detail = 1, Metal = 2;

        /// <summary>
        /// A two-lane slab from <paramref name="a"/> to <paramref name="b"/> with a dashed centre line.
        ///
        /// The two overrun values extend the slab past each end. That is wanted where a truck turns around
        /// in the open, and unwanted where the road meets a building — an overrun there pushes tarmac
        /// straight through the wall, which is exactly how the roads ended up colliding with the buildings.
        /// Callers pass 0 for any end that terminates against something solid.
        /// </summary>
        public static GameObject Road(Transform parent, string name, Vector3 a, Vector3 b, float width,
                                      float topY, float overrunA, float overrunB, Material surface, Material line)
        {
            Vector3 dir = b - a; dir.y = 0f;
            float len = dir.magnitude;
            if (len < 0.01f) return null;
            dir /= len;
            Vector3 right = new Vector3(dir.z, 0f, -dir.x);
            float half = width * 0.5f;

            // Grow the slab asymmetrically, so its centre shifts toward whichever end is allowed to overrun.
            Vector3 slabA = a - dir * overrunA, slabB = b + dir * overrunB;
            Vector3 centre = (slabA + slabB) * 0.5f; centre.y = topY - 0.06f;
            float slabLen = Vector3.Distance(slabA, slabB);

            var mb = new BoxMeshBuilder();
            mb.AddBox(centre, right, Vector3.up, dir, new Vector3(half, 0.06f, slabLen * 0.5f), Surface);

            // Dashes stop short of both ends so the line never runs off the overrun and into the buildings.
            const float dashLen = 2.6f, dashGap = 3.4f;
            float span = len - width;
            int dashes = Mathf.FloorToInt(span / (dashLen + dashGap));
            for (int i = 0; i < dashes; i++)
            {
                float t = (i + 0.5f) / dashes;
                Vector3 p = Vector3.Lerp(a, b, t); p.y = topY + 0.01f;
                mb.AddBox(p, right, Vector3.up, dir, new Vector3(0.22f, 0.02f, dashLen * 0.5f), Detail);
            }
            return Build(parent, name, mb, surface, line, null);
        }

        /// <summary>Ballast bed, sleepers and two rails, with the railheads landing exactly on the train's wheel line.</summary>
        public static GameObject Rail(Transform parent, string name, Vector3 a, Vector3 b, float groundY,
                                      float railTopY, Material bed, Material sleeper, Material steel)
        {
            Vector3 dir = b - a; dir.y = 0f;
            float len = dir.magnitude;
            if (len < 0.01f) return null;
            dir /= len;
            Vector3 right = new Vector3(dir.z, 0f, -dir.x);
            Vector3 mid = (a + b) * 0.5f;

            float railHalf = (railTopY - groundY) * 0.25f;
            float railCy = railTopY - railHalf;
            float sleeperCy = groundY + (railCy - railHalf - groundY) * 0.5f;
            float bedCy = groundY - 0.08f;

            var mb = new BoxMeshBuilder();
            mb.AddBox(new Vector3(mid.x, bedCy, mid.z), right, Vector3.up, dir,
                      new Vector3(2.1f, 0.14f, len * 0.5f + 1.2f), Surface);

            const float sleeperStep = 2.3f;
            int ties = Mathf.Max(2, Mathf.FloorToInt(len / sleeperStep));
            for (int i = 0; i <= ties; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, i / (float)ties); p.y = sleeperCy;
                mb.AddBox(p, right, Vector3.up, dir, new Vector3(1.7f, 0.09f, 0.34f), Detail);
            }

            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 p = mid + right * (0.95f * s); p.y = railCy;
                mb.AddBox(p, right, Vector3.up, dir, new Vector3(0.11f, railHalf, len * 0.5f), Metal);
            }
            return Build(parent, name, mb, bed, sleeper, steel);
        }

        /// <summary>A dark apron under a building, so its silhouette reads against the terrain.</summary>
        public static GameObject Pad(Transform parent, string name, Vector3 centre, float radius, float topY, Material mat)
        {
            var mb = new BoxMeshBuilder();
            mb.AddDisc(new Vector3(centre.x, topY, centre.z), radius, 20, Surface);
            return Build(parent, name, mb, mat, null, null);
        }

        private static GameObject Build(Transform parent, string name, BoxMeshBuilder mb,
                                        Material m0, Material m1, Material m2)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;

            var mesh = new Mesh { name = name };
            mb.Apply(mesh);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var mats = new Material[mb.SubmeshCount];
            for (int i = 0; i < mats.Length; i++) mats[i] = i == 0 ? m0 : i == 1 ? m1 : m2;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // flat ground decoration
            return go;
        }
    }
}
