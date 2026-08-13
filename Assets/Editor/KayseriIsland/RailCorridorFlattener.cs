using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Kayseri.IslandTools
{
    /// <summary>
    /// Takes the mounds off the terrain where the map modelled ground on top of its own railway.
    ///
    /// The train runs on the centreline the map exported, so anywhere the heightfield stands proud of
    /// the railhead it stands proud of the train too, and the rake drives through soil. On Gold two low
    /// mounds sit on the line — 3 m and 5 m over the rails — with no tunnel and no portal: they are
    /// simply terrain laid over track.
    ///
    /// Rather than lifting the train over them or hiding it inside them, the ground comes down. The
    /// heightfield is edited here and written out as its own mesh, because the FBX is generated and
    /// gets overwritten on every re-export.
    ///
    /// Deep ground is left alone. A rise past <see cref="HillsideRise"/> is a hill with a bore through
    /// it, and digging a trench through a mountain would be worse than the problem — CoalOperation
    /// hides the train inside those instead.
    /// </summary>
    public static class RailCorridorFlattener
    {
        /// <summary>
        /// A run to keep clear, and how wide. Inner is cut to full depth, outer blends back to
        /// untouched ground so the cut is not a crater. The rail wants a wider berth than a street
        /// because of its ballast shoulder, and sits further under because the ballast stands proud.
        /// </summary>
        private struct Corridor
        {
            public string Path;
            public float Inner, Outer, Below;
            public Corridor(string path, float inner, float outer, float below)
            { Path = path; Inner = inner; Outer = outer; Below = below; }
        }

        private static readonly Corridor[] Corridors =
        {
            new Corridor("rail",         9f, 20f, 1.2f),
            new Corridor("loop",         6f, 14f, 0.25f),
            new Corridor("roadX",        6f, 14f, 0.25f),
            new Corridor("roadY",        6f, 14f, 0.25f),
            new Corridor("portRoad",     6f, 14f, 0.25f),
            new Corridor("Street.TownN", 5f, 12f, 0.25f),
            new Corridor("Street.TownS", 5f, 12f, 0.25f),
        };

        /// <summary>Past this, the ground over the rails is a hillside and the line is bored through it.</summary>
        private const float HillsideRise = 8f;

        private const string OutputFolder = "Assets/Art/KayseriIsland/Generated";

        [Serializable] private sealed class Pt { public float x, y, z; }
        [Serializable] private sealed class Path { public string name; public List<Pt> points; }
        [Serializable] private sealed class Routes { public List<Path> paths; }

        /// <summary>
        /// Flattens the rail corridor on one built phase root. Returns how many vertices moved.
        /// Call it on the in-memory root BEFORE it is saved as a prefab.
        /// </summary>
        public static int Apply(GameObject phaseRoot, string island, int phase)
        {
            if (phaseRoot == null) return 0;

            var runs = new List<KeyValuePair<Corridor, List<Vector3>>>();
            for (int c = 0; c < Corridors.Length; c++)
            {
                List<Vector3> pts = Centreline(island, phase, Corridors[c].Path);
                if (pts != null && pts.Count >= 2)
                    runs.Add(new KeyValuePair<Corridor, List<Vector3>>(Corridors[c], pts));
            }
            if (runs.Count == 0) return 0;

            Transform terrain = phaseRoot.transform.Find("Terrain");
            if (terrain == null) return 0;
            Transform ground = terrain.Find("Ground");
            if (ground == null) return 0;
            var filter = ground.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return 0;

            Mesh src = filter.sharedMesh;
            Vector3[] verts = src.vertices;

            // Each run's box, so the great majority of a quarter-million vertices are rejected on one
            // compare instead of being measured against every segment of every route.
            var boxLo = new Vector2[runs.Count];
            var boxHi = new Vector2[runs.Count];
            for (int c = 0; c < runs.Count; c++)
            {
                var lo = new Vector2(float.MaxValue, float.MaxValue);
                var hi = new Vector2(float.MinValue, float.MinValue);
                List<Vector3> pts = runs[c].Value;
                for (int i = 0; i < pts.Count; i++)
                {
                    lo = Vector2.Min(lo, new Vector2(pts[i].x, pts[i].z));
                    hi = Vector2.Max(hi, new Vector2(pts[i].x, pts[i].z));
                }
                boxLo[c] = lo - Vector2.one * runs[c].Key.Outer;
                boxHi[c] = hi + Vector2.one * runs[c].Key.Outer;
            }

            Transform t = ground.transform;
            int moved = 0;
            float deepest = 0f;

            for (int v = 0; v < verts.Length; v++)
            {
                Vector3 world = t.TransformPoint(verts[v]);

                // The deepest cut any run asks for wins, so a junction is levelled to whichever route
                // needs it lowest rather than the two fighting over the same vertex.
                float drop = 0f;
                for (int c = 0; c < runs.Count; c++)
                {
                    if (world.x < boxLo[c].x || world.x > boxHi[c].x ||
                        world.z < boxLo[c].y || world.z > boxHi[c].y) continue;

                    Corridor spec = runs[c].Key;
                    List<Vector3> pts = runs[c].Value;
                    float best = float.MaxValue;
                    float lineY = 0f;
                    for (int s = 1; s < pts.Count; s++)
                    {
                        Vector3 a = pts[s - 1], b = pts[s];
                        Vector2 ab = new Vector2(b.x - a.x, b.z - a.z);
                        float len2 = ab.sqrMagnitude;
                        if (len2 < 1e-6f) continue;
                        float u = Mathf.Clamp01(Vector2.Dot(new Vector2(world.x - a.x, world.z - a.z), ab) / len2);
                        Vector2 q = new Vector2(a.x, a.z) + ab * u;
                        float d = Vector2.Distance(new Vector2(world.x, world.z), q);
                        if (d >= best) continue;
                        best = d;
                        lineY = Mathf.Lerp(a.y, b.y, u);
                    }
                    if (best > spec.Outer) continue;

                    float rise = world.y - (lineY - spec.Below);
                    // Only ever cut, never fill — and never cut a mountain the line is bored through.
                    if (rise <= 0f || rise > HillsideRise) continue;

                    // Full depth along the run, easing out to nothing at the corridor's edge, so the
                    // flattened strip meets untouched ground on a slope rather than a step.
                    float k = best <= spec.Inner ? 1f
                            : 1f - Mathf.SmoothStep(0f, 1f, (best - spec.Inner) / (spec.Outer - spec.Inner));
                    if (k <= 0f) continue;
                    if (rise * k > drop) drop = rise * k;
                }
                if (drop <= 0f) continue;

                world.y -= drop;
                verts[v] = t.InverseTransformPoint(world);
                moved++;
                if (drop > deepest) deepest = drop;
            }

            if (moved == 0) return 0;

            var baked = UnityEngine.Object.Instantiate(src);
            baked.name = src.name + "_railCut";
            baked.vertices = verts;
            baked.RecalculateNormals();
            baked.RecalculateBounds();
            // Build-time quantization. These 260k-vertex grounds ship ~17MB each with compression
            // off — the single biggest slice of the APK. Applied here so a Blender re-export
            // doesn't silently regenerate them uncompressed.
            MeshUtility.SetMeshCompression(baked, ModelImporterMeshCompression.Medium);

            EnsureFolder(OutputFolder);
            string path = OutputFolder + "/" + island + "_Ground_P" + phase + ".asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(baked, path);
            filter.sharedMesh = baked;

            Debug.Log($"[Island] {island} phase {phase}: cut {runs.Count} rail/road corridors into the "
                      + $"terrain — {moved} vertices, up to {deepest:F1}m.");
            return moved;
        }

        /// <summary>One exported centreline for an island and phase, in the map's own space.</summary>
        private static List<Vector3> Centreline(string island, int phase, string want)
        {
            string path = "Assets/Art/KayseriIsland/Routes/" + island.ToLowerInvariant()
                        + "_routes_P" + phase + ".json";
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset == null) return null;

            var routes = JsonUtility.FromJson<Routes>(asset.text);
            if (routes == null || routes.paths == null) return null;

            for (int i = 0; i < routes.paths.Count; i++)
            {
                if (routes.paths[i] == null || routes.paths[i].name != want) continue;
                var pts = routes.paths[i].points;
                if (pts == null) return null;
                var line = new List<Vector3>(pts.Count);
                for (int k = 0; k < pts.Count; k++) line.Add(new Vector3(pts[k].x, pts[k].y, pts[k].z));
                return line;
            }
            return null;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            int cut = folder.LastIndexOf('/');
            string parent = folder.Substring(0, cut);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder.Substring(cut + 1));
        }
    }
}
