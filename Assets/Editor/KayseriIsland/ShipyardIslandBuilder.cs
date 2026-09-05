using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kayseri.IndustrialReferenceTools
{
    /// <summary>
    /// Assembles the IndustrialReference map into Main.unity as the single playable island.
    ///
    /// His map is never modified: the prefab is instanced, not edited, and nothing is written back
    /// to Assets/Prefabs/Island/IndustrialReference or to his Blender source.
    ///
    /// The island root stays at scale 1 and the ART is the scaled child. IslandRoutes.Rebase runs
    /// every route coordinate through islandRoot.TransformPoint, so scaling the ROOT would scale
    /// the already-baked route file a second time; and the route file's scalar fields (roadWidth,
    /// railHeight, districtRadius) are not transformed at all, so they would then be in the wrong
    /// units. Art-as-child keeps one consistent space. Scale must match SCALE in build_routes.py.
    ///
    /// No IslandPhaseController is added. CoalOperation documents _phases as "null unless the
    /// island has phase art" and every consumer null-guards it, so a phase-less island is an
    /// already-supported configuration - this map is static art.
    /// </summary>
    public static class ShipyardIslandBuilder
    {
        public const string RootName = "Island_Shipyard";
        private const string MapPrefab =
            "Assets/Prefabs/Island/IndustrialReference/IndustrialReference_Map.prefab";
        private const string RoutesAsset =
            "Assets/Art/KayseriIsland/Routes/industrial_routes_P1.json";

        /// <summary>Must equal SCALE in Tools/blender/IndustrialReference/build_routes.py.</summary>
        private const float ArtScale = 85f;

        /// <summary>
        /// CoalOperation forces this pose on every vehicle it adopts - it is a constant of the
        /// Blender export, where Z-up becomes Y-up. His map came through IndustrialReferenceImporter,
        /// which already wrote Unity-space meshes, so the models are upright to begin with and the
        /// forced -90 would lay them on their side. Each vehicle is therefore a named wrapper with
        /// the model tipped +90 inside it, which cancels exactly.
        /// </summary>
        private static readonly Quaternion ModelCancel = Quaternion.Euler(90f, 0f, 0f);

        // Which of his vehicle groups stands in for which gameplay vehicle. The names on the left
        // are CoalOperation's vocabulary (Child(_islandRoot,"train"), StartsWith("truck_road"))
        // and cannot change.
        private static readonly (string name, string group)[] Fleet =
        {
            ("train",            "04_Vehicles/Ore_wagon_chassis"),
            ("wagon",            "04_Vehicles/Ore_wagon_chassis_001"),
            ("truck_road_ore1",  "04_Vehicles/Truck_chassis"),
            ("truck_road_cargo1","06_Vehicles/Truck_chassis_002"),
        };

        [MenuItem("Tools/Kayseri/Island/Build Shipyard Island")]
        public static void Build()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.name != "Main")
            {
                Debug.LogError("Shipyard: open Main.unity first (active scene is " + scene.name + ").");
                return;
            }

            var map = AssetDatabase.LoadAssetAtPath<GameObject>(MapPrefab);
            if (map == null) { Debug.LogError("Shipyard: missing " + MapPrefab); return; }

            var routes = AssetDatabase.LoadAssetAtPath<TextAsset>(RoutesAsset);
            if (routes == null)
            {
                Debug.LogError("Shipyard: missing " + RoutesAsset
                               + " — run Tools/blender/IndustrialReference/build_routes.py first.");
                return;
            }

            // Idempotent: a rebuild replaces the previous one rather than stacking a second island.
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == RootName) Object.DestroyImmediate(go);

            var root = new GameObject(RootName);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;

            var art = (GameObject)PrefabUtility.InstantiatePrefab(map, root.transform);
            art.name = "Art";
            art.transform.localPosition = Vector3.zero;
            art.transform.localRotation = Quaternion.identity;
            art.transform.localScale = Vector3.one * ArtScale;

            TameSea(art.transform);
            int made = BuildFleet(root.transform, art.transform);
            HideDecorativeFleet(art.transform);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("Shipyard: built " + RootName + " (art x" + ArtScale
                      + ", " + made + " vehicles). Wire CoalOperation next.");
            Selection.activeGameObject = root;
        }

        /// <summary>
        /// Builds the Vehicles group CoalOperation adopts in PrepareAuthoredIsland. Each entry is a
        /// wrapper named for the role, holding a COPY of one of his parked vehicles - his own
        /// vehicles are left where they were parked, as scenery.
        /// </summary>
        private static int BuildFleet(Transform root, Transform art)
        {
            var group = new GameObject("Vehicles");
            group.transform.SetParent(root, false);

            int made = 0;
            for (int i = 0; i < Fleet.Length; i++)
            {
                Transform source = FindDeep(art, LeafName(Fleet[i].group));
                if (source == null)
                {
                    Debug.LogWarning("Shipyard: no vehicle group '" + Fleet[i].group + "' in the map.");
                    continue;
                }

                var slot = new GameObject(Fleet[i].name);
                slot.transform.SetParent(group.transform, false);
                // World pose of his parked vehicle, so the fleet starts on the roads it was drawn on.
                slot.transform.position = source.position;
                slot.transform.rotation = Quaternion.identity;

                var model = Object.Instantiate(source.gameObject, slot.transform);
                model.name = "model";
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = ModelCancel;
                model.transform.localScale = source.lossyScale;
                made++;
            }
            return made;
        }

        /// <summary>
        /// The Blender reference contains parked copies of every wagon and truck. Gameplay owns the
        /// four wrappers above; leaving the source copies visible made each terrace look like a depot.
        /// The port ship is intentionally not matched here because it remains the Sail Combat landmark.
        /// </summary>
        private static void HideDecorativeFleet(Transform root)
        {
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

        /// <summary>
        /// Shrinks the map's ocean quad to something the size of a sea rather than a continent.
        ///
        /// His sea is 236 x 200 metres, which at x85 is 20 x 17 KILOMETRES around a 1.6 x 2.9km
        /// island. It costs nothing to draw - it is one quad - but it is included in every
        /// bounds query, so Scene View framing and anything that measures the island end up
        /// sized to the ocean and the island becomes a speck. The game camera framed open sky
        /// for exactly this reason.
        ///
        /// It is scaled, not deleted: with the eight ore islands gone this quad is the only
        /// water left in Main.unity.
        /// </summary>
        private static void TameSea(Transform art)
        {
            const float TargetSpan = 9000f;   // ~3x the island's long axis, so the horizon stays full

            foreach (var r in art.GetComponentsInChildren<Renderer>(true))
            {
                Vector3 size = r.bounds.size;
                if (size.x < TargetSpan && size.z < TargetSpan) continue;

                float span = Mathf.Max(size.x, size.z);
                if (span <= 0.01f) continue;
                Transform t = r.transform;
                t.localScale *= TargetSpan / span;
                Debug.Log("Shipyard: sea '" + r.name + "' " + span.ToString("F0")
                          + " -> " + TargetSpan.ToString("F0") + " units across.");
            }
        }

        private static string LeafName(string groupPath)
        {
            int slash = groupPath.LastIndexOf('/');
            return slash >= 0 ? groupPath.Substring(slash + 1) : groupPath;
        }

        private static Transform FindDeep(Transform under, string name)
        {
            if (under.name == name) return under;
            for (int i = 0; i < under.childCount; i++)
            {
                Transform hit = FindDeep(under.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }
    }
}
