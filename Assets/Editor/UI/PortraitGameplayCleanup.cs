using Game.Core;
using Game.Gameplay;
using Game.Systems;
using Game.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.EditorTools
{
    public static class PortraitGameplayCleanup
    {
        private const string MainScene = "Assets/Scenes/Main.unity";
        private const string PreviewScene = "Assets/Scenes/Shipyard.unity";
        private const string UpgradePrefab = "Assets/Prefabs/UI/UI_IstasyonEkrani.prefab";

        [MenuItem("Tools/Kayseri/UI/Apply Portrait Gameplay Cleanup")]
        public static void Apply()
        {
            if (Application.isPlaying) throw new System.InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new System.InvalidOperationException("Save the current scene first.");

            string returnTo = SceneManager.GetActiveScene().path;
            CleanUpgradePrefab();
            CleanMain();
            CleanPreview();
            EditorSceneManager.OpenScene(returnTo, OpenSceneMode.Single);
            AssetDatabase.SaveAssets();
            Debug.Log("Portrait gameplay cleanup applied: five labelled stations, one vehicle per route, Sail Combat restored, fog off.");
        }

        private static void CleanMain()
        {
            var scene = EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Single);
            var signs = Object.FindFirstObjectByType<BuildingSigns>(FindObjectsInactive.Include);
            if (signs != null)
            {
                signs.enabled = true;
                SetBool(signs, "showBuildingNames", true);
                SetFloat(signs, "_fadeInStartT", 0f);
                SetFloat(signs, "_fadeInEndT", .08f);
            }
            var day = Object.FindFirstObjectByType<DayNightCycle>(FindObjectsInactive.Include);
            SetBool(day, "_fog", false);
            var boot = Object.FindFirstObjectByType<OperationCameraBoot>(FindObjectsInactive.Include);
            SetFloat(boot, "pitch", 58f);
            SetFloat(boot, "widthMargin", .74f);
            SetFloat(boot, "hudSideFraction", .10f);
            SetBool(boot, "verticalShipyard", true);
            SetFloat(boot, "authoredYaw", 0f);
            var camera = Camera.main != null ? Camera.main.GetComponent<CameraController>() : null;
            SetBool(camera, "verticalDragOnly", true);
            var hud = Object.FindFirstObjectByType<HudUI>(FindObjectsInactive.Include);
            SetBool(hud, "compactShipyardHud", true);
            SetBool(hud, "sideRail", true);
            SetBool(hud, "railOnLeft", true);
            var island = GameObject.Find("Island_Shipyard");
            if (island != null) HideDecorativeFleet(island.transform.Find("Art"));
            RenderSettings.fog = false;
            EditorSceneManager.SaveScene(scene);
        }

        private static void CleanPreview()
        {
            var scene = EditorSceneManager.OpenScene(PreviewScene, OpenSceneMode.Single);
            Destroy("Shipyard_PreviewHUD");
            Destroy("Shipyard_EventSystem");
            var view = Object.FindFirstObjectByType<ShipyardMapView>(FindObjectsInactive.Include);
            var camera = Camera.main;
            if (view != null) HideDecorativeFleet(view.mapRoot);
            if (view != null && camera != null && view.portraitCamera != null)
            {
                var manifest = view.Manifest ?? JsonUtility.FromJson<ShipyardMapManifest>(view.manifestAsset.text);
                Vector3 mapCentre = (manifest.boundsMin + manifest.boundsMax) * .5f;
                camera.transform.rotation = Quaternion.Euler(58f, 0f, 0f);
                camera.transform.position = mapCentre - camera.transform.forward * 50f;
                camera.backgroundColor = new Color(.015f, .34f, .50f);
                view.portraitCamera.origin = camera.transform.position;
                view.portraitCamera.halfWidth = 8.9f;
                view.portraitCamera.minTravel = float.PositiveInfinity;
                view.portraitCamera.maxTravel = float.NegativeInfinity;
                foreach (var anchor in manifest.anchors)
                    if (anchor.id.StartsWith("Camera_Stop_", System.StringComparison.Ordinal))
                    {
                        float d = Vector3.Dot(anchor.position - view.portraitCamera.origin, camera.transform.up);
                        view.portraitCamera.minTravel = Mathf.Min(view.portraitCamera.minTravel, d);
                        view.portraitCamera.maxTravel = Mathf.Max(view.portraitCamera.maxTravel, d);
                    }
                view.portraitCamera.Focus(mapCentre, true);
            }
            RenderSettings.fog = false;
            EditorSceneManager.SaveScene(scene);
        }

        private static void CleanUpgradePrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(UpgradePrefab);
            try
            {
                var screen = root.GetComponentInChildren<StationScreenUI>(true);
                if (screen == null) return;
                var so = new SerializedObject(screen);
                SerializedProperty list = so.FindProperty("stations");
                var icons = new System.Collections.Generic.Dictionary<int, Sprite>();
                for (int i = 0; i < list.arraySize; i++)
                {
                    SerializedProperty entry = list.GetArrayElementAtIndex(i);
                    int station = entry.FindPropertyRelative("station").intValue;
                    icons[station] = entry.FindPropertyRelative("icon").objectReferenceValue as Sprite;
                }
                Sprite ship = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/UI/Sea/gemi.png");
                list.arraySize = IslandEconomy.PlayerStations.Length;
                for (int i = 0; i < IslandEconomy.PlayerStations.Length; i++)
                {
                    int station = IslandEconomy.PlayerStations[i];
                    SerializedProperty entry = list.GetArrayElementAtIndex(i);
                    entry.FindPropertyRelative("station").intValue = station;
                    entry.FindPropertyRelative("title").stringValue = "";
                    Sprite icon;
                    icons.TryGetValue(station, out icon);
                    entry.FindPropertyRelative("icon").objectReferenceValue =
                        station == IslandEconomy.Power && ship != null ? ship : icon;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, UpgradePrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void HideDecorativeFleet(Transform root)
        {
            if (root == null) return;
            foreach (Transform child in root)
            {
                if (child.name.StartsWith("Ore_wagon_chassis", System.StringComparison.Ordinal)
                    || child.name.StartsWith("Truck_chassis", System.StringComparison.Ordinal))
                {
                    child.gameObject.SetActive(false);
                    continue;
                }
                HideDecorativeFleet(child);
            }
        }

        private static void Destroy(string name)
        {
            var go = GameObject.Find(name);
            if (go != null) Object.DestroyImmediate(go);
        }

        private static void SetBool(Object target, string name, bool value)
        {
            if (target == null) return;
            var so = new SerializedObject(target);
            var property = so.FindProperty(name);
            if (property == null) return;
            property.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(Object target, string name, float value)
        {
            if (target == null) return;
            var so = new SerializedObject(target);
            var property = so.FindProperty(name);
            if (property == null) return;
            property.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
