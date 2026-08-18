using System.Collections.Generic;
using Game.Gameplay;
using UnityEditor;
using UnityEngine;

namespace Kayseri.IslandTools
{
    /// <summary>
    /// Fills every island's <c>workerPrefabs</c> list with the low-poly people pack, so the crews have
    /// bodies to wear.
    ///
    /// A menu command rather than a hand-edit: the field lives on eight <see cref="CoalOperation"/>
    /// components in Main.unity, and scene files are not something to edit by hand. Doing it through
    /// SerializedObject means Unity writes the references, with the right fileIDs, the way the Inspector
    /// would have.
    ///
    /// Safe to run more than once — it overwrites the list rather than appending, so re-running after
    /// the pack changes simply re-syncs it.
    /// </summary>
    public static class IslandPeopleWiring
    {
        private const string PackFolder = "Assets/DavidJalbert/LowPolyPeople/Prefabs";
        private const string Field = "workerPrefabs";

        [MenuItem("Kayseri/Island/Wire Island People", false, 43)]
        public static void Wire()
        {
            List<GameObject> people = LoadPack();
            if (people.Count == 0)
            {
                EditorUtility.DisplayDialog("Wire Island People",
                    "No character prefabs found under\n" + PackFolder +
                    "\n\nThe low-poly people package looks like it is missing from the project.", "OK");
                return;
            }

            // Whatever is open, rather than force-opening Main.unity: an editor command that swaps the
            // loaded scene out from under you can throw away unsaved work.
            var ops = UnityEngine.Object.FindObjectsByType<CoalOperation>(FindObjectsInactive.Include);
            if (ops.Length == 0)
            {
                EditorUtility.DisplayDialog("Wire Island People",
                    "No CoalOperation components in the open scene.\n\nOpen Assets/Scenes/Main.unity and run this again.",
                    "OK");
                return;
            }

            int wired = 0;
            foreach (CoalOperation op in ops)
            {
                var so = new SerializedObject(op);
                SerializedProperty list = so.FindProperty(Field);
                if (list == null)
                {
                    Debug.LogWarning("[IslandPeople] " + op.name + " has no '" + Field + "' field — skipped.", op);
                    continue;
                }

                list.arraySize = people.Count;
                for (int i = 0; i < people.Count; i++)
                    list.GetArrayElementAtIndex(i).objectReferenceValue = people[i];

                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(op);
                wired++;
            }

            if (wired > 0)
                UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();

            Debug.Log("[IslandPeople] Wired " + people.Count + " character prefabs onto " + wired +
                      " island operation(s). Save the scene to keep it.");
        }

        /// <summary>
        /// The pack's prefabs, sorted by name so the build prefixes group together — "normal*",
        /// "stout*", "strong*". <see cref="StationCrew"/> picks by that prefix, and a stable order is
        /// what makes which body stands where reproducible between runs.
        /// </summary>
        private static List<GameObject> LoadPack()
        {
            var found = new List<GameObject>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PackFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null) found.Add(go);
            }
            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return found;
        }
    }
}
