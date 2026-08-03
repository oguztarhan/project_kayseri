using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kayseri.IslandTools
{
    /// <summary>
    /// Builds the Kayseri island from the FBX exported out of Blender.
    ///
    /// The FBX files carry world transforms baked in, so every district lands at
    /// the Unity origin and reassembles the map exactly as authored - nothing is
    /// positioned by hand here.
    ///
    /// Run "Kayseri/Island/Build All" once after importing new FBX.
    /// </summary>
    public static class IslandBuilder
    {
        private const string ArtRoot = "Assets/Art/KayseriIsland";
        private const string ModelsRoot = ArtRoot + "/Models";
        private const string MaterialsRoot = ArtRoot + "/Materials";
        private const string PalettePath = ArtRoot + "/palette.json";
        private const string PrefabRoot = "Assets/Prefabs/Island";
        private const string ScenePath = "Assets/Scenes/KayseriIsland.unity";

        private const string OpaqueShader = "Kayseri/IslandVertexLit";
        private const string TransparentShader = "Kayseri/IslandVertexLitTransparent";

        // Export order from Blender. Terrain first so it sorts to the bottom.
        private static readonly string[] Groups =
        {
            "Terrain", "Roads", "Rail", "Mine", "Depot", "Refinery",
            "Market", "Port", "Sites", "Props", "Foliage",
            // Town centre inside the ring road - one yard per quadrant, each
            // advancing on its own station (Civic follows the island as a whole).
            "Power", "Haul", "Fleet", "Civic",
            // Driven by the gameplay layer rather than scenery: the train rake and the
            // road fleet, which CoalOperation lifts onto the island root at startup.
            "Vehicles"
        };

        private const int PhaseCount = 3;

        // Camera/light values transposed from the Blender scene (Z-up -> Y-up).
        private static readonly Vector3 CameraEuler = new Vector3(48f, -45f, 0f);
        private static readonly Vector3 CameraPos = new Vector3(331f, 520f, -331f);
        private const float CameraOrthoSize = 126.7f;
        private static readonly Vector3 SunEuler = new Vector3(44f, 128f, 0f);

        [MenuItem("Kayseri/Island/Build All", false, 0)]
        public static void BuildAll()
        {
            // Materials MUST exist before the importers run - the import step
            // remaps FBX materials onto them, and an empty lookup silently
            // remaps nothing (leaving every mesh on the default grey material).
            CreateMaterials();
            ConfigureModelImports();
            BuildPhasePrefabs();
            BuildScene();
            Debug.Log("[Island] Build All finished.");
        }

        /// <summary>
        /// Answers the only question that matters when the map looks flat: did
        /// Unity actually import the baked vertex colours? If it did not, the
        /// materials are switched to their flat fallback so the map still reads
        /// correctly instead of turning white.
        /// </summary>
        [MenuItem("Kayseri/Island/Diagnose Vertex Colours", false, 40)]
        public static void DiagnoseVertexColours()
        {
            int meshes = 0, withColours = 0, totalVerts = 0;
            var sample = new List<string>();

            foreach (var guid in AssetDatabase.FindAssets("t:Mesh", new[] { ModelsRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (!(obj is Mesh mesh)) continue;
                    meshes++;
                    totalVerts += mesh.vertexCount;
                    var cols = mesh.colors32;
                    if (cols != null && cols.Length > 0)
                    {
                        withColours++;
                        if (sample.Count < 4)
                            sample.Add($"{mesh.name}: {cols.Length} colours, first=" +
                                       $"({cols[0].r},{cols[0].g},{cols[0].b})");
                    }
                }
                if (meshes > 400) break;   // a sample is enough
            }

            Debug.Log($"[Island] Vertex colour check: {withColours}/{meshes} meshes " +
                      $"carry colours ({totalVerts} verts sampled).");
            foreach (var s in sample) Debug.Log($"[Island]   {s}");

            bool missing = meshes > 0 && withColours == 0;
            if (missing)
            {
                Debug.LogWarning("[Island] No vertex colours imported - switching " +
                                 "materials to flat fallback (_VertexColorAmount = 0).");
                SetVertexColorAmount(0f);
            }
            else if (withColours > 0)
            {
                Debug.Log("[Island] Vertex colours present. If the map still looks " +
                          "flat, the shader is not reading them - check that the " +
                          "material's shader is Kayseri/IslandVertexLit.");
            }
        }

        [MenuItem("Kayseri/Island/Toggle Flat Colours", false, 41)]
        public static void ToggleFlatColours()
        {
            var probe = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsRoot}/grass.mat");
            float current = probe != null ? probe.GetFloat("_VertexColorAmount") : 1f;
            SetVertexColorAmount(current > 0.5f ? 0f : 1f);
        }

        private static void SetVertexColorAmount(float amount)
        {
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialsRoot }))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (mat == null || !mat.HasProperty("_VertexColorAmount")) continue;
                mat.SetFloat("_VertexColorAmount", amount);
                EditorUtility.SetDirty(mat);
                n++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Island] _VertexColorAmount = {amount} on {n} materials.");
        }

        // ------------------------------------------------------------ imports
        [MenuItem("Kayseri/Island/1. Configure Model Imports", false, 20)]
        public static void ConfigureModelImports()
        {
            var paths = FindAllFbx();
            if (paths.Count == 0)
            {
                Debug.LogError($"[Island] No FBX found under {ModelsRoot}.");
                return;
            }

            var materials = LoadMaterialLookup();
            int touched = 0;

            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var path in paths)
                {
                    var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                    if (importer == null) continue;

                    importer.globalScale = 1f;
                    importer.useFileScale = true;
                    importer.importBlendShapes = false;
                    importer.importVisibility = false;
                    importer.importCameras = false;
                    importer.importLights = false;
                    importer.importAnimation = false;
                    importer.animationType = ModelImporterAnimationType.None;
                    importer.importNormals = ModelImporterNormals.Import;
                    importer.importTangents = ModelImporterTangents.None;
                    importer.weldVertices = false;   // preserve hard edges + vertex colours
                    importer.optimizeMeshPolygons = true;
                    importer.optimizeMeshVertices = true;
                    importer.meshCompression = ModelImporterMeshCompression.Off;
                    importer.isReadable = false;
                    importer.addCollider = false;
                    importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                    importer.materialLocation = ModelImporterMaterialLocation.InPrefab;

                    // Point each embedded material at our URP material of the same name.
                    // Clear first: an earlier External-mode run left remaps aimed at the
                    // extracted materials under Models/*/Materials/.
                    var stale = new List<AssetImporter.SourceAssetIdentifier>(
                        importer.GetExternalObjectMap().Keys);
                    foreach (var id in stale) importer.RemoveRemap(id);

                    foreach (var kv in materials)
                    {
                        var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key);
                        importer.AddRemap(id, kv.Value);
                    }

                    EditorUtility.SetDirty(importer);
                    importer.SaveAndReimport();
                    touched++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[Island] Configured {touched} FBX importers.");
        }

        // ---------------------------------------------------------- materials
        [MenuItem("Kayseri/Island/2. Create Materials", false, 21)]
        public static void CreateMaterials()
        {
            if (!File.Exists(PalettePath))
            {
                Debug.LogError($"[Island] palette.json missing at {PalettePath}.");
                return;
            }

            var opaque = Shader.Find(OpaqueShader);
            var transparent = Shader.Find(TransparentShader);
            if (opaque == null || transparent == null)
            {
                Debug.LogError("[Island] Island shaders not found - let Unity compile them first.");
                return;
            }

            EnsureFolder(MaterialsRoot);
            var palette = JsonUtility.FromJson<Palette>(File.ReadAllText(PalettePath));
            if (palette?.materials == null)
            {
                Debug.LogError("[Island] palette.json could not be parsed.");
                return;
            }

            int created = 0, updated = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var entry in palette.materials)
                {
                    if (string.IsNullOrEmpty(entry.name)) continue;

                    bool isTransparent = entry.alpha < 0.999f;
                    string path = $"{MaterialsRoot}/{entry.name}.mat";
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (mat == null)
                    {
                        mat = new Material(isTransparent ? transparent : opaque);
                        AssetDatabase.CreateAsset(mat, path);
                        created++;
                    }
                    else
                    {
                        mat.shader = isTransparent ? transparent : opaque;
                        updated++;
                    }

                    var c = ToColor(entry.color, 1f);
                    c.a = Mathf.Clamp01(entry.alpha);
                    mat.SetColor("_BaseColor", c);
                    mat.SetFloat("_Metallic", Mathf.Clamp01(entry.metallic));
                    mat.SetFloat("_Smoothness", Mathf.Clamp01(entry.smoothness));
                    mat.SetFloat("_VertexColorAmount", 1f);

                    if (entry.emission > 0f)
                    {
                        var e = ToColor(entry.emissionColor, entry.emission);
                        mat.SetColor("_EmissionColor", e);
                        mat.EnableKeyword("_EMISSION");
                        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    }
                    else
                    {
                        mat.SetColor("_EmissionColor", Color.black);
                        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                    }

                    mat.renderQueue = isTransparent ? 3000 : 2000;
                    EditorUtility.SetDirty(mat);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[Island] Materials: {created} created, {updated} updated.");
        }

        // ------------------------------------------------------------ prefabs
        [MenuItem("Kayseri/Island/3. Build Phase Prefabs", false, 22)]
        public static void BuildPhasePrefabs()
        {
            EnsureFolder(PrefabRoot);
            int built = 0;

            for (int phase = 1; phase <= PhaseCount; phase++)
            {
                var root = new GameObject($"Island_Phase{phase}");
                root.isStatic = true;
                int added = 0;

                foreach (var group in Groups)
                {
                    string fbx = $"{ModelsRoot}/Phase{phase}/{group}_P{phase}.fbx";
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                    if (asset == null) continue;   // group may not exist at this phase

                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                    instance.name = group;
                    instance.transform.SetParent(root.transform, false);
                    instance.transform.localPosition = Vector3.zero;
                    instance.transform.localRotation = Quaternion.identity;
                    instance.transform.localScale = Vector3.one;
                    // The vehicles are driven every frame by the gameplay layer. Marking them
                    // static gets them folded into a combined world-space mesh, after which the
                    // drawn geometry stops following the transform - the train's position moves
                    // while its body stays behind, rendering as an untextured slab.
                    SetStaticRecursive(instance, group != "Vehicles");
                    added++;
                }

                if (added == 0)
                {
                    Object.DestroyImmediate(root);
                    Debug.LogWarning($"[Island] Phase {phase}: no FBX found, skipped.");
                    continue;
                }

                string prefabPath = $"{PrefabRoot}/Island_Phase{phase}.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Object.DestroyImmediate(root);
                built++;
                Debug.Log($"[Island] Phase {phase} prefab: {added} district groups.");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Island] Built {built} phase prefabs in {PrefabRoot}.");
        }

        // -------------------------------------------------------------- scene
        [MenuItem("Kayseri/Island/4. Build Scene", false, 23)]
        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                                                    NewSceneMode.Single);

            var camGo = new GameObject("IsoCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = CameraOrthoSize;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 3000f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.42f, 0.60f, 0.72f);
            camGo.transform.SetPositionAndRotation(CameraPos, Quaternion.Euler(CameraEuler));
            camGo.tag = "MainCamera";

            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.945f, 0.845f);
            sun.intensity = 1.6f;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(SunEuler);

            var islandRoot = new GameObject("Island");
            var controllerType = System.Type.GetType(
                "Kayseri.Island.IslandPhaseController, Assembly-CSharp");
            var phaseRoots = new List<GameObject>();

            for (int phase = 1; phase <= PhaseCount; phase++)
            {
                string prefabPath = $"{PrefabRoot}/Island_Phase{phase}.prefab";
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (asset == null) continue;

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                instance.transform.SetParent(islandRoot.transform, false);
                instance.SetActive(phase == 1);   // start on the phase 1 island
                phaseRoots.Add(instance);
            }

            if (controllerType != null)
            {
                var controller = islandRoot.AddComponent(controllerType);
                var so = new SerializedObject(controller);
                var prop = so.FindProperty("_phaseRoots");
                if (prop != null)
                {
                    prop.arraySize = phaseRoots.Count;
                    for (int i = 0; i < phaseRoots.Count; i++)
                        prop.GetArrayElementAtIndex(i).objectReferenceValue = phaseRoots[i];
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            EnsureFolder("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[Island] Scene written to {ScenePath} " +
                      $"({phaseRoots.Count} phase roots).");
        }

        // ------------------------------------------------------------ helpers
        private static Dictionary<string, Material> LoadMaterialLookup()
        {
            var map = new Dictionary<string, Material>();
            if (!AssetDatabase.IsValidFolder(MaterialsRoot)) return map;

            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialsRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null) map[mat.name] = mat;
            }
            return map;
        }

        private static List<string> FindAllFbx()
        {
            var list = new List<string>();
            if (!AssetDatabase.IsValidFolder(ModelsRoot)) return list;

            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ModelsRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                    list.Add(path);
            }
            return list;
        }

        private static void SetStaticRecursive(GameObject go, bool value)
        {
            go.isStatic = value;
            foreach (Transform child in go.transform)
                SetStaticRecursive(child.gameObject, value);
        }

        /// <summary>
        /// palette.json stores sRGB. Material colour properties set from script
        /// are passed to the shader raw, so in a Linear-space project they must
        /// be converted or every surface reads far too bright.
        /// </summary>
        private static Color ToColor(float[] rgb, float scale)
        {
            if (rgb == null || rgb.Length < 3) return Color.grey;
            var c = new Color(rgb[0], rgb[1], rgb[2], 1f);
            if (PlayerSettings.colorSpace == ColorSpace.Linear)
                c = c.linear;
            return new Color(c.r * scale, c.g * scale, c.b * scale, 1f);
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        [System.Serializable]
        private class Palette
        {
            public PaletteEntry[] materials;
        }

        [System.Serializable]
        private class PaletteEntry
        {
            public string name;
            public float[] color;
            public float metallic;
            public float smoothness;
            public float alpha;
            public float emission;
            public float[] emissionColor;
        }
    }
}
