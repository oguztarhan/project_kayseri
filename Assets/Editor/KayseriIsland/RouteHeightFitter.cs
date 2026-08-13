using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Kayseri.IslandTools
{
    /// <summary>
    /// Puts the exported driving lines back on the tarmac they are supposed to run on.
    ///
    /// The generator writes a route file beside each island's FBX, and the trucks drive the heights in
    /// it. Those heights come from grade.road_z sampled at export time, so they only agree with the
    /// drawn carriageway while the two are exported from the same build. Coal and copper drifted: their
    /// route files are four days older than their art, and the road grade changed in between, so the
    /// line the trucks follow runs up to 2.9 m UNDER the tarmac — the truck drives inside the road.
    ///
    /// Worse, <see cref="RailCorridorFlattener"/> reads the same file and cuts the ground to 0.25 under
    /// it, so a stale line does not merely sink the trucks: it digs a trench along the whole road for
    /// them to sink into.
    ///
    /// This measures the road that was actually BUILT — the carriageway meshes in the phase prefab —
    /// and writes each drivable centreline back at that surface. Where the drift is zero the file is
    /// left alone, so an island exported in step with its art is untouched. Run it after any re-export
    /// of the island art, then rebuild the phase prefabs so the corridor cut is taken from the corrected
    /// line.
    ///
    /// Only paths with a width are touched. The rail, the footpath and the ship lane have their own
    /// datum and their own geometry, and none of them is a road.
    /// </summary>
    public static class RouteHeightFitter
    {
        private static readonly string[] Islands = { "Coal", "Copper", "Iron", "Gold" };
        private const int Phases = 3;

        private const string RouteFolder = "Assets/Art/KayseriIsland/Routes";
        private const string PrefabFolder = "Assets/Prefabs/Island";

        /// <summary>Below this the file is left alone — the exporter rounds to 4 places.</summary>
        private const float Ignore = 0.005f;

        [Serializable] private sealed class Pt { public float x, y, z; }
        [Serializable] private sealed class Anchor { public string name; public Pt pos; }
        [Serializable] private sealed class Path { public string name; public bool closed; public float width; public List<Pt> points; }

        [Serializable]
        private sealed class Routes
        {
            public int phase;
            public float roadHeight;
            public float railHeight;
            public float roadWidth;
            public float districtRadius;
            public List<string> activeSites;
            public List<Anchor> anchors;
            public List<Path> paths;
        }

        [MenuItem("Kayseri/Island/Fit Route Heights To Tarmac", false, 42)]
        public static void FitAll()
        {
            int files = 0;
            for (int i = 0; i < Islands.Length; i++)
                for (int phase = 1; phase <= Phases; phase++)
                    if (Fit(Islands[i], phase)) files++;

            AssetDatabase.Refresh();
            Debug.Log($"[Island] Route heights fitted to the tarmac in {files} route files. Rebuild the "
                      + "phase prefabs of any island listed above, so the terrain cut follows the corrected line.");
        }

        /// <summary>Fits one island's phase. True when the file was rewritten.</summary>
        private static bool Fit(string island, int phase)
        {
            string routePath = $"{RouteFolder}/{island.ToLowerInvariant()}_routes_P{phase}.json";
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(routePath);
            if (asset == null) return false;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{island}/Island_Phase{phase}.prefab");
            if (prefab == null)
            {
                Debug.LogWarning($"[Island] {island} phase {phase}: no phase prefab — route heights left as exported.");
                return false;
            }

            Tarmac tarmac = Tarmac.From(prefab);
            if (tarmac == null)
            {
                Debug.LogWarning($"[Island] {island} phase {phase}: no carriageway meshes — route heights left as exported.");
                return false;
            }

            var routes = JsonUtility.FromJson<Routes>(asset.text);
            if (routes == null || routes.paths == null) return false;

            int moved = 0, carried = 0;
            float worst = 0f;
            string worstPath = "";
            for (int p = 0; p < routes.paths.Count; p++)
            {
                Path path = routes.paths[p];
                if (path == null || path.points == null || path.width <= 0.001f) continue;

                int n = path.points.Count;
                var drop = new float[n];
                var fitted = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    Pt q = path.points[i];
                    float surface = tarmac.HeightAt(q.x, q.z);
                    if (surface <= Tarmac.Nothing) continue;
                    drop[i] = surface - q.y;
                    fitted[i] = true;
                }

                // A centreline runs on past the end of its tarmac — every arterial is trimmed at the
                // works gates, and the last few metres into a yard are the yard's own slab. Those
                // points take the correction of the nearest point that IS on tarmac, so the run stays
                // continuous instead of stepping down where the road stops.
                int last = -1;
                for (int i = 0; i < n; i++)
                {
                    if (fitted[i]) { last = i; continue; }
                    int next = -1;
                    for (int k = i + 1; k < n && next < 0; k++) if (fitted[k]) next = k;
                    if (last < 0 && next < 0) break;                       // path has no tarmac at all
                    int from = last < 0 ? next : next < 0 ? last : (i - last <= next - i ? last : next);
                    drop[i] = drop[from];
                    carried++;
                }

                for (int i = 0; i < n; i++)
                {
                    if (Mathf.Abs(drop[i]) < Ignore) continue;
                    path.points[i].y = (float)Math.Round(path.points[i].y + drop[i], 4);
                    moved++;
                    if (Mathf.Abs(drop[i]) > Mathf.Abs(worst)) { worst = drop[i]; worstPath = path.name; }
                }
            }

            if (moved == 0)
            {
                Debug.Log($"[Island] {island} phase {phase}: routes already sit on the tarmac.");
                return false;
            }

            System.IO.File.WriteAllText(routePath, Write(routes));
            Debug.Log($"[Island] {island} phase {phase}: lifted {moved} route points onto the tarmac "
                      + $"({carried} carried past the end of a road), worst {worst:F2} m on {worstPath}.");
            return true;
        }

        /// <summary>
        /// The file back in the exporter's own layout — json.dump(indent=1), four decimal places.
        ///
        /// JsonUtility would do this in one line, but it prints a float as its full double expansion:
        /// every 0.1 in the file comes back as 0.10000000149011612 and the whole thing reads as
        /// rewritten when only the heights moved. The point of this tool is a diff you can check.
        /// </summary>
        private static string Write(Routes r)
        {
            var sb = new System.Text.StringBuilder(1 << 18);
            sb.Append("{\n");
            sb.Append(" \"phase\": ").Append(r.phase).Append(",\n");
            sb.Append(" \"roadHeight\": ").Append(Num(r.roadHeight)).Append(",\n");
            sb.Append(" \"railHeight\": ").Append(Num(r.railHeight)).Append(",\n");
            sb.Append(" \"roadWidth\": ").Append(Num(r.roadWidth)).Append(",\n");
            sb.Append(" \"districtRadius\": ").Append(Num(r.districtRadius)).Append(",\n");

            sb.Append(" \"activeSites\": [");
            if (r.activeSites != null && r.activeSites.Count > 0)
            {
                sb.Append('\n');
                for (int i = 0; i < r.activeSites.Count; i++)
                    sb.Append("  \"").Append(r.activeSites[i]).Append(i + 1 < r.activeSites.Count ? "\",\n" : "\"\n");
                sb.Append(" ");
            }
            sb.Append("],\n");

            sb.Append(" \"anchors\": [\n");
            for (int i = 0; i < r.anchors.Count; i++)
            {
                Anchor a = r.anchors[i];
                sb.Append("  {\n   \"name\": \"").Append(a.name).Append("\",\n   \"pos\": ");
                Point(sb, a.pos, "   ");
                sb.Append("\n  }").Append(i + 1 < r.anchors.Count ? ",\n" : "\n");
            }
            sb.Append(" ],\n");

            sb.Append(" \"paths\": [\n");
            for (int i = 0; i < r.paths.Count; i++)
            {
                Path p = r.paths[i];
                sb.Append("  {\n   \"name\": \"").Append(p.name).Append("\",\n");
                sb.Append("   \"closed\": ").Append(p.closed ? "true" : "false").Append(",\n");
                sb.Append("   \"width\": ").Append(Num(p.width)).Append(",\n");
                sb.Append("   \"points\": [\n");
                for (int k = 0; k < p.points.Count; k++)
                {
                    sb.Append("    ");
                    Point(sb, p.points[k], "    ");
                    sb.Append(k + 1 < p.points.Count ? ",\n" : "\n");
                }
                sb.Append("   ]\n  }").Append(i + 1 < r.paths.Count ? ",\n" : "\n");
            }
            sb.Append(" ]\n}");
            return sb.ToString();
        }

        private static void Point(System.Text.StringBuilder sb, Pt p, string indent)
        {
            sb.Append("{\n").Append(indent).Append(" \"x\": ").Append(Num(p.x)).Append(",\n");
            sb.Append(indent).Append(" \"y\": ").Append(Num(p.y)).Append(",\n");
            sb.Append(indent).Append(" \"z\": ").Append(Num(p.z)).Append("\n").Append(indent).Append("}");
        }

        /// <summary>A number the way Python writes it: four places at most, and never bare of a point.</summary>
        private static string Num(float v)
        {
            string s = Math.Round((double)v, 4).ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture);
            return s == "-0.0" ? "0.0" : s;
        }

        /// <summary>
        /// The carriageways of one phase as a triangle soup, in the map's own space.
        ///
        /// Everything else in the Roads group is decoration laid at its own height — the verge under
        /// the tarmac, the kerbs and parapets above it, the paint, the pavements. Measuring against
        /// those would set the trucks down on a kerb.
        /// </summary>
        private sealed class Tarmac
        {
            public const float Nothing = -99999f;

            private Vector3[] _verts;
            private int[] _tris;

            public static Tarmac From(GameObject phasePrefab)
            {
                Transform roads = phasePrefab.transform.Find("Roads");
                if (roads == null) return null;

                var verts = new List<Vector3>();
                var tris = new List<int>();
                foreach (Transform road in roads)
                {
                    if (!IsCarriageway(road.name)) continue;
                    var filter = road.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) continue;

                    Mesh mesh = filter.sharedMesh;
                    Vector3[] mv = mesh.vertices;
                    int[] mt = mesh.triangles;
                    Matrix4x4 m = road.localToWorldMatrix;
                    int off = verts.Count;
                    for (int i = 0; i < mv.Length; i++) verts.Add(m.MultiplyPoint3x4(mv[i]));
                    for (int i = 0; i < mt.Length; i++) tris.Add(off + mt[i]);
                }
                if (tris.Count == 0) return null;

                return new Tarmac { _verts = verts.ToArray(), _tris = tris.ToArray() };
            }

            private static bool IsCarriageway(string name)
            {
                if (!name.StartsWith("Road.", StringComparison.Ordinal) &&
                    !name.StartsWith("Spur.", StringComparison.Ordinal) &&
                    !name.StartsWith("Street.", StringComparison.Ordinal)) return false;
                return name.IndexOf(".shoulder", StringComparison.Ordinal) < 0
                    && name.IndexOf(".edge", StringComparison.Ordinal) < 0
                    && name.IndexOf(".Bridge", StringComparison.Ordinal) < 0;
            }

            /// <summary>Road surface at a point, or <see cref="Nothing"/> where no carriageway covers it.</summary>
            public float HeightAt(float x, float z)
            {
                float top = Nothing;
                for (int i = 0; i < _tris.Length; i += 3)
                {
                    Vector3 a = _verts[_tris[i]], b = _verts[_tris[i + 1]], c = _verts[_tris[i + 2]];
                    if (x < Mathf.Min(a.x, Mathf.Min(b.x, c.x)) || x > Mathf.Max(a.x, Mathf.Max(b.x, c.x))) continue;
                    if (z < Mathf.Min(a.z, Mathf.Min(b.z, c.z)) || z > Mathf.Max(a.z, Mathf.Max(b.z, c.z))) continue;

                    float det = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    if (Mathf.Abs(det) < 1e-9f) continue;
                    float u = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / det;
                    float v = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / det;
                    float w = 1f - u - v;
                    if (u < -0.001f || v < -0.001f || w < -0.001f) continue;

                    // The highest surface wins: at a junction two carriageways overlap at the same
                    // level, and where a road passes over its own bridge deck the deck is not the
                    // thing to drive on.
                    float y = u * a.y + v * b.y + w * c.y;
                    if (y > top) top = y;
                }
                return top;
            }
        }
    }
}
