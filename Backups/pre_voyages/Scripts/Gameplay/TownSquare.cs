using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The civic square at the middle of the ring road: four kerbed parcels around the crossing, one
    /// of them carrying a monument to whatever the island digs up.
    ///
    /// WHY. Every island is built around a ring road with two arterials crossing at its centre, and
    /// that crossing lands almost exactly on the focal point of the frame. The composition's subject
    /// was therefore a large piece of empty grey asphalt, which is most of why the islands photograph
    /// as busy at the edges and dead in the middle.
    ///
    /// WHY FOUR PARCELS AND NOT ONE PLINTH. The obvious fix — a monument in the centre — would stand
    /// in the middle of a live junction. ROAD_X and ROAD_Y are both ROAD_W = 14 wide and cross at the
    /// origin, so the centre is carriageway, not island. What is actually free is the four quadrant
    /// wedges between the arms: outside the 7-unit half-width of each road, and inside the town yards
    /// that start at 17. That is roughly an eight-unit parcel per quadrant, which is what this builds.
    ///
    /// Four corner parcels around a crossing is a real civic form rather than a compromise, and it
    /// reads as deliberate from the air in a way a single off-centre object would not.
    ///
    /// One mesh, four submeshes, built once and never ticked: four draw calls on the one island that
    /// is live at a time.
    /// </summary>
    public static class TownSquare
    {
        private const int Pad = 0, Kerb = 1, Stone = 2, Crystal = 3;

        /// <summary>
        /// Lays the square out around the island's origin.
        /// </summary>
        /// <param name="offset">Distance from the centre to each parcel's own centre, along both axes.</param>
        /// <param name="half">Half-extent of one parcel.</param>
        /// <param name="groundY">Height the square sits on.</param>
        /// <param name="monumentQuadrant">Which parcel carries the column: 0..3, or below zero for none.</param>
        public static GameObject Build(Transform parent, Vector3 centre, float offset, float half,
                                       float groundY, int monumentQuadrant, float monumentHeight,
                                       Material pad, Material kerb, Material stone, Material crystal)
        {
            if (half <= 0.1f) return null;

            var mb = new BoxMeshBuilder();
            Vector3 r = Vector3.right, f = Vector3.forward, up = Vector3.up;

            // Quadrant order is fixed so monumentQuadrant means the same thing every time it is tuned.
            for (int q = 0; q < 4; q++)
            {
                float sx = (q == 0 || q == 3) ? 1f : -1f;
                float sz = (q == 0 || q == 1) ? 1f : -1f;
                Vector3 c = new Vector3(centre.x + sx * offset, groundY, centre.z + sz * offset);

                // A kerb slightly proud of the pad, so the parcel has an edge from the air rather than
                // being a flat colour patch that reads as a texture seam.
                mb.AddBox(c + up * 0.10f, r, up, f, new Vector3(half, 0.10f, half), Kerb);
                mb.AddBox(c + up * 0.24f, r, up, f, new Vector3(half - 0.6f, 0.14f, half - 0.6f), Pad);

                if (q != monumentQuadrant)
                {
                    // A low planter, so the three non-monument parcels are not bare slabs.
                    mb.AddBox(c + up * 0.55f, r, up, f, new Vector3(half * 0.46f, 0.30f, half * 0.46f), Kerb);
                    continue;
                }

                // The monument: a stepped base, a column, and the island's own ore on top. The crystal
                // is a separate submesh so it can carry an emissive material — with _EmissionAlways it
                // glows faintly by day and lights up with the rest of the island after dark, which is
                // what makes the centre of the frame the brightest thing on it at night.
                float baseH = monumentHeight * 0.10f;
                float colH = monumentHeight * 0.62f;
                float capY = groundY + 0.38f + baseH * 2f + colH;

                mb.AddBox(c + up * (0.38f + baseH), r, up, f,
                          new Vector3(half * 0.52f, baseH, half * 0.52f), Stone);
                mb.AddBox(c + up * (0.38f + baseH * 3f), r, up, f,
                          new Vector3(half * 0.38f, baseH, half * 0.38f), Stone);
                mb.AddBox(c + up * (0.38f + baseH * 4f + colH * 0.5f), r, up, f,
                          new Vector3(half * 0.17f, colH * 0.5f, half * 0.17f), Stone);

                Vector3 apexBase = new Vector3(c.x, capY, c.z);
                mb.AddPeak(apexBase, apexBase + up * (monumentHeight * 0.28f),
                           half * 0.26f, 6, 0.12f, seed: 7, submesh: Crystal);
            }

            var go = new GameObject("Dressing_TownSquare");
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;

            var mesh = new Mesh { name = "TownSquare" };
            mb.Apply(mesh);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { pad, kerb, stone, crystal };
            return go;
        }
    }
}
