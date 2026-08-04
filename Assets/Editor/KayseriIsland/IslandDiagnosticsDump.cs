using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Kayseri.IslandTools
{
    /// <summary>
    /// Writes a one-shot report about the imported island assets on every domain
    /// reload. Temporary debugging aid - delete this file once the island
    /// materials are confirmed working.
    /// </summary>
    [InitializeOnLoad]
    public static class IslandDiagnosticsDump
    {
        private const string OutPath = "Tools/blender/island_diag.txt";

        static IslandDiagnosticsDump()
        {
            EditorApplication.delayCall += Dump;
        }

        private static void Dump()
        {
            var sb = new StringBuilder();
            try
            {
                sb.AppendLine($"time            : {System.DateTime.Now:HH:mm:ss}");
                sb.AppendLine($"colorSpace      : {PlayerSettings.colorSpace}");
                sb.AppendLine($"activeBuildTgt  : {EditorUserBuildSettings.activeBuildTarget}");
                sb.AppendLine();

                // --- 1. do the imported meshes carry vertex colours? -----------
                int meshes = 0, withCols = 0, checkedFiles = 0;
                var samples = new StringBuilder();
                foreach (var guid in AssetDatabase.FindAssets(
                             "t:Mesh", new[] { "Assets/Art/KayseriIsland/Models/Coal/Phase1" }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    checkedFiles++;
                    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
                    {
                        if (!(o is Mesh m)) continue;
                        meshes++;
                        var c = m.colors32;
                        if (c != null && c.Length > 0)
                        {
                            withCols++;
                            if (withCols <= 3)
                                samples.AppendLine(
                                    $"    {m.name} verts={m.vertexCount} cols={c.Length} " +
                                    $"first=({c[0].r},{c[0].g},{c[0].b}) " +
                                    $"mid=({c[c.Length / 2].r},{c[c.Length / 2].g},{c[c.Length / 2].b})");
                        }
                    }
                    if (checkedFiles >= 6) break;
                }
                sb.AppendLine($"MESH VERTEX COLOURS: {withCols}/{meshes} meshes have colours " +
                              $"(from {checkedFiles} fbx)");
                sb.Append(samples);
                sb.AppendLine();

                // --- 2. what materials do the renderers actually reference? ----
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/Island/Coal/Island_Phase1.prefab");
                if (prefab == null)
                {
                    sb.AppendLine("PREFAB: Island_Phase1.prefab NOT FOUND");
                }
                else
                {
                    var rends = prefab.GetComponentsInChildren<MeshRenderer>(true);
                    sb.AppendLine($"PREFAB RENDERERS: {rends.Length}");
                    int nullMat = 0, defaultMat = 0, islandMat = 0, otherMat = 0;
                    var seen = new StringBuilder();
                    int shown = 0;
                    foreach (var r in rends)
                    {
                        foreach (var m in r.sharedMaterials)
                        {
                            if (m == null) { nullMat++; continue; }
                            string sh = m.shader != null ? m.shader.name : "<null shader>";
                            if (sh.StartsWith("Kayseri/")) islandMat++;
                            else if (m.name.Contains("Default")) defaultMat++;
                            else otherMat++;
                            if (shown < 6)
                            {
                                seen.AppendLine($"    {r.name} -> mat '{m.name}' shader '{sh}'");
                                shown++;
                            }
                        }
                    }
                    sb.AppendLine($"    kayseriShader={islandMat} default={defaultMat} " +
                                  $"other={otherMat} null={nullMat}");
                    sb.Append(seen);
                }
                sb.AppendLine();

                // --- 3. material values -----------------------------------------
                var grass = AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/Art/KayseriIsland/Materials/grass.mat");
                if (grass == null) sb.AppendLine("MATERIAL grass.mat NOT FOUND");
                else
                {
                    sb.AppendLine($"grass.mat shader     : {grass.shader.name}");
                    sb.AppendLine($"grass.mat _BaseColor : {grass.GetColor("_BaseColor")}");
                    sb.AppendLine($"grass.mat _VertexColorAmount : " +
                                  $"{(grass.HasProperty("_VertexColorAmount") ? grass.GetFloat("_VertexColorAmount").ToString() : "MISSING")}");
                }

                // --- 4. is the shader actually usable? --------------------------
                var sh2 = Shader.Find("Kayseri/IslandVertexLit");
                sb.AppendLine($"shader found         : {(sh2 != null)}");
                if (sh2 != null)
                    sb.AppendLine($"shader isSupported   : {sh2.isSupported}");
            }
            catch (System.Exception e)
            {
                sb.AppendLine("EXCEPTION: " + e);
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
                File.WriteAllText(OutPath, sb.ToString());
            }
            catch { /* diagnostic only */ }
        }
    }
}
