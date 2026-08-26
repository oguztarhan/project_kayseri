using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The low-poly mountain range that closes off the mining corner of an island.
    ///
    /// The corner used to be dressed with copies of the mine mesh scattered behind the row. At mine size
    /// they read as more mines nobody could use; shrunk, they read as boulders on a lawn. Neither reads as
    /// the thing the corner is supposed to be, which is a mountain range with tunnels driven into it.
    ///
    /// So the range is generated instead: a band of irregular flat-shaded peaks, rising toward the back so
    /// it climbs away from the site rather than walling it off, with pale caps on the tall ones for
    /// silhouette. Everything lands in ONE mesh with three submeshes, so a whole range costs one draw call
    /// on the SRP batcher — which is what makes it affordable to build on a mid-range phone.
    ///
    /// Nothing here is random: the scatter comes from <see cref="BoxMeshBuilder.Hash"/>, so a range rebuilt
    /// on the next launch is identical and no shape data has to be saved.
    /// </summary>
    public static class MountainRange
    {
        private const int Rock = 0, Shadow = 1, Cap = 2;

        /// <summary>
        /// Fills a band of ground with peaks and returns the object holding them.
        /// </summary>
        /// <param name="centre">Middle of the band's front edge, at ground height.</param>
        /// <param name="along">Unit vector the band runs along.</param>
        /// <param name="back">Unit vector pointing away from the site, into the corner.</param>
        /// <param name="halfSpan">Half the band's length along <paramref name="along"/>.</param>
        /// <param name="depth">How far the band extends along <paramref name="back"/>.</param>
        /// <param name="peakRadius">Base radius of a mid-sized peak.</param>
        /// <param name="height">Height of a mid-sized peak.</param>
        /// <param name="keepClear">Points no peak may cover — the mine heads and their tunnel mouths.</param>
        /// <param name="clearRadius">How much room to leave around each of those.</param>
        /// <param name="anchors">Mine heads to bury in rock, so their tunnels open out of a mountain.</param>
        /// <param name="anchorRadius">Base radius of those peaks — wide enough to swallow a mine head.</param>
        /// <param name="anchorHeight">And their height.</param>
        /// <param name="land">Centre of the island's land ellipse.</param>
        /// <param name="landHalfX">Its half-extent in world X, already scaled by whatever inset the caller wants.</param>
        /// <param name="landHalfZ">And in world Z.</param>
        public static GameObject Build(Transform parent, string name, Vector3 centre, Vector3 along, Vector3 back,
                                       float halfSpan, float depth, float peakRadius, float height,
                                       int peaks, int seed,
                                       Vector3[] anchors, float anchorRadius, float anchorHeight,
                                       Vector3[] keepClear, float clearRadius,
                                       Vector3 land, float landHalfX, float landHalfZ,
                                       Material rock, Material shadow, Material cap)
        {
            var mb = new BoxMeshBuilder();
            int made = 0;
            bool any = false;

            // One big peak set behind each mine head first. The point of the corner is that the train comes
            // out of the ROCK: with the range merely standing behind them, the mine buildings read as sheds
            // parked in front of some scenery, and the tunnel mouth as a doorway to nowhere. Buried, the
            // head becomes the entrance and the mountain is what the train emerges from.
            if (anchors != null)
                for (int i = 0; i < anchors.Length; i++)
                {
                    Vector3 foot = anchors[i] + back * (anchorRadius * 0.5f) - Vector3.up * (anchorHeight * 0.18f);
                    Vector3 apex = foot + Vector3.up * anchorHeight
                                 + along * ((BoxMeshBuilder.Hash(seed, 900 + i) - 0.5f) * anchorRadius * 0.35f);
                    mb.AddPeak(foot, apex, anchorRadius, 6, 0.2f, seed + 500 + i, Rock);
                    mb.AddPeak(Vector3.Lerp(foot, apex, 0.6f), apex, anchorRadius * 0.44f, 6, 0.18f, seed + 500 + i, Cap);
                    any = true;
                }

            // Several times the wanted count of candidates, because a peak landing on a mine head is
            // dropped rather than nudged — a nudged one only lands on the next mine along.
            for (int i = 0; i < peaks * 5 && made < peaks; i++)
            {
                float u = BoxMeshBuilder.Hash(seed, i * 4);
                float v = BoxMeshBuilder.Hash(seed, i * 4 + 1);
                float s = BoxMeshBuilder.Hash(seed, i * 4 + 2);
                float lean = BoxMeshBuilder.Hash(seed, i * 4 + 3);

                // Peaks start a little way back rather than on the front edge: the range belongs BEHIND
                // the mine heads, framing them, not in among them.
                Vector3 foot = centre + along * ((u - 0.5f) * 2f * halfSpan) + back * ((0.12f + v * 0.88f) * depth);
                float r = peakRadius * (0.62f + s * 0.8f);
                // Only a third of the peak's own radius goes into the test. Charging the whole of it turned
                // eight modest keep-clear discs into eight 30 m ones, which between them blanketed the
                // corner and left the range with a single peak in it. Mountains are allowed to touch.
                if (Blocked(foot, keepClear, clearRadius + r * 0.33f)) continue;
                // A peak may stand right up at the waterline — a mountain meeting the sea reads as coast —
                // so only a third of its radius is held back from the shore.
                if (!OnLand(foot, land, landHalfX - r * 0.3f, landHalfZ - r * 0.3f)) continue;

                // Taller toward the back: a range that rises away from the player reads as depth, where a
                // flat-topped band of equal peaks reads as a wall.
                float h = height * (0.5f + v * 0.85f) * (0.78f + s * 0.44f);

                // The apex leans off centre. A perfectly axial cone reads as a spinning top; a leaning one
                // reads as rock, and it costs one vector add.
                Vector3 apex = foot + Vector3.up * h
                             + along * ((lean - 0.5f) * r * 0.55f)
                             + back * ((s - 0.5f) * r * 0.4f);

                // Sunk below the ground so the base ring never shows as a floating rim on a slope.
                Vector3 baseCentre = foot - Vector3.up * (h * 0.22f);
                int sides = 5 + (int)(s * 3f);                       // 5..7: enough facets to read as rock
                mb.AddPeak(baseCentre, apex, r, sides, 0.28f, seed + i, made % 3 == 1 ? Shadow : Rock);

                // A pale cap on the taller peaks. Sitting it a little below the apex and letting it share
                // the same tip keeps the two cones' silhouettes identical, so the cap reads as snow ON the
                // peak rather than as a second peak balanced on the first.
                if (h > height * 0.85f)
                {
                    Vector3 capBase = Vector3.Lerp(baseCentre, apex, 0.62f);
                    mb.AddPeak(capBase, apex, r * 0.42f, sides, 0.22f, seed + i, Cap);
                }
                made++;
                any = true;
            }

            if (!any) return null;

            var go = new GameObject(name);
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;

            var mesh = new Mesh { name = name };
            mb.Apply(mesh);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { rock, shadow, cap };
            // Unlike the flat track decoration, these are the tallest things on the island: their shadows
            // are most of what stops the corner reading as a sticker on green paper.
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go;
        }

        /// <summary>
        /// Ellipse rather than bounding box, matching how the rest of the site tests the shoreline: the
        /// island meshes are rounded, so their box corners are open water.
        /// </summary>
        private static bool OnLand(Vector3 p, Vector3 centre, float halfX, float halfZ)
        {
            if (halfX <= 0.01f || halfZ <= 0.01f) return true;   // no island mesh: don't constrain
            float nx = (p.x - centre.x) / halfX, nz = (p.z - centre.z) / halfZ;
            return nx * nx + nz * nz <= 1f;
        }

        private static bool Blocked(Vector3 p, Vector3[] keepClear, float radius)
        {
            if (keepClear == null) return false;
            float r2 = radius * radius;
            for (int i = 0; i < keepClear.Length; i++)
            {
                float dx = p.x - keepClear[i].x, dz = p.z - keepClear[i].z;
                if (dx * dx + dz * dz < r2) return true;
            }
            return false;
        }
    }
}
