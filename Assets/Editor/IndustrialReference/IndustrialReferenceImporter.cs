using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Kayseri.IndustrialReferenceTools
{
    // Imports only the approved Blender map into its own namespace of assets.
    public static class IndustrialReferenceImporter
    {
        private const string Art = "Assets/Art/IndustrialReference";
        private const string Prefabs = "Assets/Prefabs/Island/IndustrialReference";
        public const string ScenePath = "Assets/Scenes/KayseriIsland_IndustrialReference.unity";
        private const string Source = "Tools/blender/IndustrialReference";
        [Serializable] private class Manifest { public MaterialEntry[] materials; public MeshEntry[] meshes; public GroupEntry[] groups; public int objectCount; public int triangleCount; public CameraEntry camera; }
        [Serializable] private class MaterialEntry { public string name; public float[] color; public float metallic; public float roughness; public float[] emission; public float strength; }
        [Serializable] private class MeshEntry { public string name; public float[] vertices; public float[] normals; public float[] uv; public SubmeshEntry[] submeshes; }
        [Serializable] private class SubmeshEntry { public string material; public int[] triangles; }
        [Serializable] private class GroupEntry { public string name; public float[] position; public PartEntry[] parts; }
        [Serializable] private class PartEntry { public string name; public int mesh; public float[] position; }
        [Serializable] private class CameraEntry { public float[] position; public float[] forward; public float[] up; public float orthoSize; }

        [MenuItem("Kayseri/Industrial Reference/Build New Map")]
        public static void ScheduleBuild() { EditorApplication.delayCall += Build; }

        [MenuItem("Kayseri/Industrial Reference/Open Map")]
        public static void OpenMap()
        {
            if (!File.Exists(ScenePath)) throw new InvalidOperationException("Build the Industrial Reference map first.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save the currently edited scene first.");
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        private static void Status(string value) { File.WriteAllText(Source + "/import_status.txt", value); }
        private static Vector3 V(float[] a) { return new Vector3(a[0], a[1], a[2]); }
        private static Color C(float[] a) { return new Color(a[0], a[1], a[2], a.Length > 3 ? a[3] : 1f); }
        private static string Clean(string s) { return System.Text.RegularExpressions.Regex.Replace(s, "[^A-Za-z0-9_-]", "_"); }
        private static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            Folder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static void Build()
        {
            try
            {
                if (File.Exists(ScenePath) || AssetDatabase.IsValidFolder(Prefabs) || File.Exists(Art + "/Geometry/MeshLibrary.asset"))
                    throw new InvalidOperationException("Map output already exists. Refusing to overwrite it.");
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("An existing scene has unsaved changes.");
                Status("Reading approved Blender geometry.");
                var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Source + "/map_geometry.json"));
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new InvalidOperationException("URP Lit shader is missing.");
                Folder(Art + "/Materials"); Folder(Art + "/Geometry"); Folder(Prefabs);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "/Textures/Meadow_BaseColor.png");
                if (texture == null) throw new InvalidOperationException("Baked meadow texture is missing.");
                var materials = new Dictionary<string, Material>();
                foreach (var entry in manifest.materials)
                {
                    var material = new Material(shader) { name = entry.name };
                    material.SetColor("_BaseColor", C(entry.color).gamma);
                    material.SetFloat("_Metallic", entry.metallic);
                    material.SetFloat("_Smoothness", 1f - entry.roughness);
                    if (entry.name == "grass")
                    {
                        material.SetColor("_BaseColor", Color.white);
                        material.SetTexture("_BaseMap", texture);
                    }
                    if (entry.strength > 0)
                    {
                        material.EnableKeyword("_EMISSION");
                        material.SetColor("_EmissionColor", C(entry.emission).gamma * entry.strength);
                        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    }
                    AssetDatabase.CreateAsset(material, Art + "/Materials/" + Clean(entry.name) + ".mat");
                    materials.Add(entry.name, material);
                }
                Status("Creating " + manifest.meshes.Length + " native Unity meshes.");
                var meshes = new Mesh[manifest.meshes.Length];
                AssetDatabase.StartAssetEditing();
                try
                {
                    for (int k = 0; k < manifest.meshes.Length; k++)
                    {
                        var entry = manifest.meshes[k];
                        var mesh = new Mesh { name = k.ToString("D4") + "_" + entry.name, indexFormat = entry.vertices.Length / 3 > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                        var vertices = new Vector3[entry.vertices.Length / 3];
                        var normals = new Vector3[vertices.Length];
                        var uv = new Vector2[vertices.Length];
                        for (int i = 0; i < vertices.Length; i++)
                        {
                            vertices[i] = new Vector3(entry.vertices[i * 3], entry.vertices[i * 3 + 1], entry.vertices[i * 3 + 2]);
                            normals[i] = new Vector3(entry.normals[i * 3], entry.normals[i * 3 + 1], entry.normals[i * 3 + 2]);
                            uv[i] = new Vector2(entry.uv[i * 2], entry.uv[i * 2 + 1]);
                        }
                        mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uv;
                        mesh.subMeshCount = entry.submeshes.Length;
                        for (int i = 0; i < entry.submeshes.Length; i++) mesh.SetTriangles(entry.submeshes[i].triangles, i);
                        mesh.RecalculateBounds(); mesh.RecalculateTangents();
                        if (k == 0) AssetDatabase.CreateAsset(mesh, Art + "/Geometry/MeshLibrary.asset");
                        else AssetDatabase.AddObjectToAsset(mesh, Art + "/Geometry/MeshLibrary.asset");
                        meshes[k] = mesh;
                    }
                }
                finally { AssetDatabase.StopAssetEditing(); }
                AssetDatabase.SaveAssets();

                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var root = new GameObject("IndustrialReference_Map");
                var categories = new Dictionary<string, Transform>();
                int parts = 0;
                for (int k = 0; k < manifest.groups.Length; k++)
                {
                    var group = manifest.groups[k];
                    string category, name;
                    int slash = group.name.IndexOf('/');
                    if (slash >= 0) { category = group.name.Substring(0, slash); name = group.name.Substring(slash + 1); }
                    else
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(group.name, @"^(\d+_[A-Za-z]+)_(.*)$");
                        category = match.Success ? match.Groups[1].Value : "Props";
                        name = match.Success ? match.Groups[2].Value : group.name;
                    }
                    if (!categories.ContainsKey(category))
                    {
                        var cat = new GameObject(category); cat.transform.SetParent(root.transform, false);
                        categories.Add(category, cat.transform); Folder(Prefabs + "/" + category);
                    }
                    var go = new GameObject(name); go.transform.SetParent(categories[category], false);
                    foreach (var part in group.parts)
                    {
                        var child = new GameObject(part.name);
                        child.transform.SetParent(go.transform, false); child.transform.localPosition = V(part.position);
                        child.AddComponent<MeshFilter>().sharedMesh = meshes[part.mesh];
                        var renderer = child.AddComponent<MeshRenderer>();
                        var entries = manifest.meshes[part.mesh].submeshes; var slots = new Material[entries.Length];
                        for (int i = 0; i < entries.Length; i++) slots[i] = materials[entries[i].material];
                        renderer.sharedMaterials = slots;
                        bool isInterface = category.StartsWith("12_");
                        renderer.shadowCastingMode = isInterface ? ShadowCastingMode.Off : ShadowCastingMode.On;
                        renderer.receiveShadows = !isInterface;
                        if (!category.Contains("Vehicles")) GameObjectUtility.SetStaticEditorFlags(child, StaticEditorFlags.BatchingStatic);
                        parts++;
                    }
                    PrefabUtility.SaveAsPrefabAssetAndConnect(go, Prefabs + "/" + category + "/" + Clean(name) + ".prefab", InteractionMode.AutomatedAction);
                    go.transform.localPosition = V(group.position);
                    if (k % 25 == 0) Status("Creating editable prefabs: " + k + "/" + manifest.groups.Length);
                }
                if (parts != manifest.objectCount) throw new InvalidOperationException("Source object count mismatch.");
                PrefabUtility.SaveAsPrefabAssetAndConnect(root, Prefabs + "/IndustrialReference_Map.prefab", InteractionMode.AutomatedAction);

                var cameraObject = new GameObject("IndustrialReference_Camera");
                var camera = cameraObject.AddComponent<Camera>(); cameraObject.tag = "MainCamera";
                camera.orthographic = true; camera.orthographicSize = manifest.camera.orthoSize;
                camera.transform.position = V(manifest.camera.position);
                camera.transform.rotation = Quaternion.LookRotation(V(manifest.camera.forward), V(manifest.camera.up));
                camera.nearClipPlane = .1f; camera.farClipPlane = 300; camera.backgroundColor = new Color(.025f, .5f, .6f);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.allowHDR = true;
                cameraObject.AddComponent<AudioListener>();
                cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;

                RenderSettings.skybox = null; RenderSettings.fog = false;
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(.45f, .64f, .73f);
                RenderSettings.ambientEquatorColor = new Color(.34f, .41f, .39f);
                RenderSettings.ambientGroundColor = new Color(.23f, .24f, .17f);
                var lightRoot = new GameObject("IndustrialReference_Lighting");
                var sunObject = new GameObject("Warm afternoon sun"); sunObject.transform.SetParent(lightRoot.transform, false);
                var sun = sunObject.AddComponent<Light>(); sun.type = LightType.Directional;
                sun.intensity = 1.7f; sun.color = new Color(1f, .92f, .78f); sun.shadows = LightShadows.Soft;
                sun.shadowBias = .025f; sun.shadowNormalBias = .2f;
                sun.transform.rotation = Quaternion.Euler(52, -35, 0); RenderSettings.sun = sun;
                var fillObject = new GameObject("Soft blue fill"); fillObject.transform.SetParent(lightRoot.transform, false);
                var fill = fillObject.AddComponent<Light>(); fill.type = LightType.Directional;
                fill.intensity = .28f; fill.color = new Color(.58f, .78f, 1f); fill.shadows = LightShadows.None;
                fill.transform.rotation = Quaternion.Euler(38, 140, 0);

                var volumeObject = new GameObject("IndustrialReference_PostProcessing");
                var volume = volumeObject.AddComponent<Volume>(); volume.isGlobal = true; volume.priority = 20;
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, Art + "/IndustrialReference_Volume.asset");
                var bloom = profile.Add<Bloom>(true); bloom.intensity.Override(.28f); bloom.threshold.Override(1.1f); bloom.scatter.Override(.65f);
                var color = profile.Add<ColorAdjustments>(true); color.postExposure.Override(-.35f);
                AssetDatabase.AddObjectToAsset(bloom, profile); AssetDatabase.AddObjectToAsset(color, profile);
                volume.sharedProfile = profile;
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                EditorSceneManager.SaveScene(scene, ScenePath);
                var buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
                buildScenes.Add(new EditorBuildSettingsScene(ScenePath, true)); EditorBuildSettings.scenes = buildScenes.ToArray();
                if (SceneView.lastActiveSceneView != null)
                    SceneView.lastActiveSceneView.LookAt(new Vector3(0, 3.4f, .1f), camera.transform.rotation, 20f, true, true);
                Selection.activeGameObject = root;
                File.WriteAllText(Source + "/import_report.json", "{\"scene\":\"" + ScenePath + "\",\"prefabs\":" + manifest.groups.Length + ",\"parts\":" + parts + ",\"meshes\":" + meshes.Length + ",\"triangles\":" + manifest.triangleCount + "}");
                Status("COMPLETE");
                Debug.Log("[IndustrialReference] New map saved. " + manifest.groups.Length + " independent prefabs; " + parts + " editable mesh parts. Existing maps untouched.");
            }
            catch (Exception e) { Status("ERROR: " + e); Debug.LogException(e); }
        }
    

private static void RestorePreviewStartScene(PlayModeStateChange state)
{
    if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool("IndustrialReference_PreviewActive", false)) return;
    var previous = SessionState.GetString("IndustrialReference_PreviewReturn", "");
    EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previous) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
    SessionState.EraseBool("IndustrialReference_PreviewActive");
    SessionState.EraseString("IndustrialReference_PreviewReturn");
}


[InitializeOnLoadMethod]
private static void RegisterPreviewRestore()
{
    EditorApplication.playModeStateChanged -= RestorePreviewStartScene;
    EditorApplication.playModeStateChanged += RestorePreviewStartScene;
}


[MenuItem("Kayseri/Industrial Reference/Preview Map")]
public static void PreviewMap()
{
    if (EditorApplication.isPlayingOrWillChangePlaymode) return;
    OpenMap();
    var previous = EditorSceneManager.playModeStartScene;
    SessionState.SetString("IndustrialReference_PreviewReturn", previous != null ? AssetDatabase.GetAssetPath(previous) : "");
    SessionState.SetBool("IndustrialReference_PreviewActive", true);
    EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
    EditorApplication.isPlaying = true;
}
}
}
