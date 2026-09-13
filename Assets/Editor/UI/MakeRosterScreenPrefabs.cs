// Editor-only baker for the roster SCREEN prefabs: the chrome around the cards. Run from
// Tools/Kayseri/UI/Build Masters Screen Prefab. It builds the screen with the scene's own Inspector
// values, through the screen's own builder, so the prefab starts out identical to the code-built
// screen. After that the prefab is the user's: re-baking overwrites every hand edit, which is why the
// menu asks first.
using System.Reflection;
using Game.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.EditorTools
{
    public static class MakeRosterScreenPrefabs
    {
        private const string Folder = "Assets/Prefabs/UI/Ekranlar";
        public const string MasterScreenPath = Folder + "/UI_UstalarEkrani.prefab";

        [MenuItem("Tools/Kayseri/UI/Build Masters Screen Prefab")]
        private static void BuildMastersMenu()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(MasterScreenPath) != null
                && !EditorUtility.DisplayDialog("Ustalar ekranı prefabı",
                                                MasterScreenPath + " zaten var. Koddan yeniden kurulursa " +
                                                "prefabda elle yaptığın bütün değişiklikler silinir.",
                                                "Üstüne yaz", "Vazgeç"))
                return;
            BakeMasters();
        }

        /// <summary>Bakes the masters screen chrome and OVERWRITES the prefab. Returns the path, or null
        /// when there was nothing to bake from.</summary>
        public static string BakeMasters()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Bake the screen prefab outside Play mode: a live screen carries runtime state.");
                return null;
            }

            // The Inspector values (sprites, colours, scale) live on the scene's component, not in code.
            var source = Object.FindAnyObjectByType<ForemanRosterUI>(FindObjectsInactive.Include);
            if (source == null)
            {
                Debug.LogError("No ForemanRosterUI in the open scenes. Open Main (UI_Sistemler) and bake again.");
                return null;
            }
            PrimeSkin();

            // A preview scene, so building and throwing away the host never dirties Main.
            Scene stage = EditorSceneManager.NewPreviewScene();
            try
            {
                var host = new GameObject("UstalarPisirme");
                SceneManager.MoveGameObjectToScene(host, stage);
                var screen = host.AddComponent<ForemanRosterUI>();
                EditorUtility.CopySerialized(source, screen);

                typeof(ForemanRosterUI).GetMethod("BuildChrome", BindingFlags.Instance | BindingFlags.NonPublic)
                                       .Invoke(screen, null);
                var scrim = (RectTransform)typeof(ForemanRosterUI)
                    .GetField("_root", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(screen);
                RectTransform safe = UiBuild.InsetContent(scrim);
                GameObject canvas = scrim.parent.gameObject;
                Tidy(canvas, safe);

                EnsureFolder();
                PrefabUtility.SaveAsPrefabAsset(canvas, MasterScreenPath);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(stage);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("Masters screen prefab written: " + MasterScreenPath);
            return MasterScreenPath;
        }

        /// <summary>
        /// UiSkin fills its statics in Awake, which never runs in edit mode. Without this the builders
        /// see no skin, tint every pill with its fallback colour, and the prefab bakes in a look the
        /// game never shows.
        /// </summary>
        private static void PrimeSkin()
        {
            var skin = Object.FindAnyObjectByType<UiSkin>(FindObjectsInactive.Include);
            if (skin == null) return;
            typeof(UiSkin).GetMethod("Cache", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(skin, null);
        }

        private static void Tidy(GameObject canvas, RectTransform safe)
        {
            // SafeArea runs in edit mode too and has just pinned Guvenli to this editor's game view. The
            // device inset is applied again on Awake; the asset carries the plain stretch.
            UiBuild.Anchor(safe, Vector2.zero, Vector2.one);

            // UiSkin.Flat is generated at runtime and cannot be saved into an asset: it would come back
            // as a missing reference. A spriteless Image draws the same flat quad.
            Image[] images = canvas.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
                if (images[i].sprite != null && !EditorUtility.IsPersistent(images[i].sprite))
                    images[i].sprite = null;
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Prefabs/UI", "Ekranlar");
        }
    }
}
