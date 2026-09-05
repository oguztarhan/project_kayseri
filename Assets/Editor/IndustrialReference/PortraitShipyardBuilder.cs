using System;
using System.IO;
using Game.Systems;
using Game.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kayseri.IndustrialReferenceTools
{
    public static class PortraitShipyardBuilder
    {
        public const string ScenePath = "Assets/Scenes/Shipyard.unity";
        private const string PreviewSession = "Kayseri.Shipyard.PreviewActive";
        private const string PreviousStart = "Kayseri.Shipyard.PreviousStart";

        [InitializeOnLoadMethod]
        private static void WatchPreview()
        {
            EditorApplication.playModeStateChanged -= RestoreStartScene;
            EditorApplication.playModeStateChanged += RestoreStartScene;
            if (!EditorApplication.isPlayingOrWillChangePlaymode) RestoreStartScene(PlayModeStateChange.EnteredEditMode);
        }

        private static void RestoreStartScene(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(PreviewSession, false)) return;
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(PreviousStart, ""));
            SessionState.SetBool(PreviewSession, false);
        }

        [MenuItem("Kayseri/Portrait Shipyard/Create Preview Scene")]
        public static void Build()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            if (File.Exists(ScenePath)) throw new InvalidOperationException("Preview already exists; open it instead. No scene was overwritten.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save your current scene before building.");
            var manifestAsset = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Resources/Shipyard/Map.json");
            var manifest = JsonUtility.FromJson<ShipyardMapManifest>(manifestAsset.text);
            var scene = EditorSceneManager.OpenScene(IndustrialReferenceImporter.ScenePath, OpenSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Transform map = null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.transform.Find("05_Smelter/Smelting_Plant") != null) map = root.transform;
            if (map == null || map.position != Vector3.zero || map.localScale != Vector3.one)
                throw new InvalidOperationException("Expected approved map at original scale and origin.");
            var view = new GameObject("Shipyard_Runtime").AddComponent<ShipyardMapView>();
            view.mapRoot = map; view.manifestAsset = manifestAsset;
            view.anchorRoot = new GameObject("Gameplay_Anchors").transform;
            view.anchorRoot.SetParent(view.transform, false);
            foreach (var anchor in manifest.anchors)
            {
                var marker = new GameObject(anchor.id).transform;
                marker.SetParent(view.anchorRoot, false); marker.position = anchor.position;
            }
            var routeRoot = new GameObject("Gameplay_Routes").transform;
            routeRoot.SetParent(view.transform, false);
            foreach (var route in manifest.routes)
            {
                var routeObject = new GameObject(route.id).transform;
                routeObject.SetParent(routeRoot, false);
                for (int i = 0; i < route.points.Length; i++)
                {
                    var point = new GameObject("Point_" + i.ToString("D3")).transform;
                    point.SetParent(routeObject, false); point.position = route.points[i];
                }
            }
            // The reference's decorative interface is not gameplay UI.
            for (int i = 0; i < map.childCount; i++)
                if (map.GetChild(i).name.StartsWith("12_", StringComparison.Ordinal)) map.GetChild(i).gameObject.SetActive(false);
            var padMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            padMaterial.name = "Shipyard_LockedPad";
            padMaterial.color = new Color(.26f, .30f, .30f);
            const string materialPath = "Assets/Resources/Shipyard/LockedPad.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (existing != null) { UnityEngine.Object.DestroyImmediate(padMaterial); padMaterial = existing; }
            else AssetDatabase.CreateAsset(padMaterial, materialPath);
            view.buildings = new GameObject[manifest.zones.Length];
            view.lockedPads = new GameObject[manifest.zones.Length];
            for (int i = 0; i < manifest.zones.Length; i++)
            {
                var zone = manifest.zones[i];
                if (!zone.needsArt)
                {
                    var building = map.Find(zone.artGroup);
                    if (building == null) throw new InvalidOperationException("Missing art: " + zone.artGroup);
                    view.buildings[i] = building.gameObject;
                }
                var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pad.name = zone.id + "_LockedPad"; pad.transform.SetParent(view.transform, false);
                pad.transform.position = zone.centre + Vector3.up * .025f;
                pad.transform.localScale = new Vector3(zone.size.x, .045f, zone.size.y);
                pad.GetComponent<Renderer>().sharedMaterial = padMaterial;
                UnityEngine.Object.DestroyImmediate(pad.GetComponent<Collider>());
                view.lockedPads[i] = pad;
            }
            view.Apply(new ShipyardProgression());
            var camera = Camera.main;
            camera.transform.rotation = Quaternion.Euler(58f, 0f, 0f);
            var mapCentre = (manifest.boundsMin + manifest.boundsMax) * .5f;
            camera.transform.position = mapCentre - camera.transform.forward * 50f;
            camera.backgroundColor = new Color(.015f, .34f, .50f);
            var movement = camera.gameObject.AddComponent<PortraitShipyardCamera>();
            movement.origin = camera.transform.position;
            movement.halfWidth = 8.9f;
            movement.minTravel = float.PositiveInfinity; movement.maxTravel = float.NegativeInfinity;
            foreach (var anchor in manifest.anchors)
                if (anchor.id.StartsWith("Camera_Stop_", StringComparison.Ordinal))
                {
                    float d = Vector3.Dot(anchor.position - movement.origin, camera.transform.up);
                    movement.minTravel = Mathf.Min(movement.minTravel, d);
                    movement.maxTravel = Mathf.Max(movement.maxTravel, d);
                }
            view.portraitCamera = movement;
            camera.aspect = 9f / 19.5f;
            movement.Focus(mapCentre, true);
            RenderSettings.fog = false;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = view.gameObject;
            Debug.Log("Shipyard preview ready: 45 anchors, 17 route bindings, Cannon only. Existing Main and Bootstrap unchanged.");
        }

        [MenuItem("Kayseri/Portrait Shipyard/Open Preview Scene")]
        public static void Open()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Kayseri/Portrait Shipyard/Play Preview (No Player Save)")]
        public static void PlayPreview()
        {
            if (Application.isPlaying) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            // Temporarily bypass Bootstrap; restore its start-scene preference on exit.
            SessionState.SetString(PreviousStart, AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetBool(PreviewSession, true);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            EditorApplication.isPlaying = true;
        }

    }
}
