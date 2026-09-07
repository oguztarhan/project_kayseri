// Editor-only. Renders one head-and-shoulders portrait per master and per captain out of the
// LowPolyPeople pack the island already uses, writes them as PNGs, imports them as sprites and
// wires them into the two roster screens.
//
// WHY RENDER RATHER THAN DRAW. There is no portrait art and there may not be for a while, and a
// roster of twenty blank squares teaches the player nothing. Rendering the pack has a property
// bought art would not: a master's card is a picture of THE MAN STANDING AT HIS STATION, because
// StationForemen picks his body out of this same pack by the same index. Recognising the card on the
// island is the whole point of putting him there.
//
// Re-runnable. Delete the folder and run it again, or run it after changing the framing below.
using System.IO;
using Game.Core;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    public static class RosterPortraitBaker
    {
        private const string PackFolder = "Assets/DavidJalbert/LowPolyPeople/Prefabs";
        private const string OutFolder = "Assets/Art/UI/Roster";

        // Everything a re-bake can move lives in these four constants.
        private const int Size = 256;

        /// <summary>
        /// How much of the body the frame holds, measured down from the top of the head.
        ///
        /// A HEAD SHOT. The pack's prefabs are authored in a T-pose and these are chibi proportions —
        /// the chin and the shoulders sit at nearly the same height — so there is no head-and-shoulders
        /// frame that does not have two arms running out of it sideways. 0.42 caught the full T-pose as
        /// a bar across the picture; 0.26 cropped into the chin. This holds the whole head with air
        /// above it and lets a sliver of shoulder show at the very bottom, which reads as a portrait.
        /// On a card this size a face is legible where a body is a smudge anyway.
        /// </summary>
        private const float BodyFraction = 0.36f;

        /// <summary>Where the frame sits, as a fraction of itself below the crown. Below 0.5 leaves
        /// headroom above the head; at 0.5 the crown is exactly on the top edge and clips.</summary>
        private const float HeadRoom = 0.42f;

        /// <summary>Degrees off dead-on. A face square to camera reads as a mugshot; a little turn
        /// gives the low-poly heads a readable silhouette.</summary>
        private const float Yaw = 22f;

        [MenuItem("Tools/Kayseri/UI/Bake Roster Portraits")]
        public static void Bake()
        {
            // Wiring the results means writing to the open scene, and a scene edited in play mode is
            // thrown away when play stops. Better to refuse than to look like it worked.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Stop play mode first — baking wires the portraits into the open "
                               + "scene, and a play-mode scene edit is discarded on exit.");
                return;
            }

            GameObject[] pack = LoadPack();
            if (pack.Length == 0)
            {
                Debug.LogError("No people prefabs under " + PackFolder + " — nothing to bake.");
                return;
            }

            Directory.CreateDirectory(OutFolder);

            var master = new string[Foremen.Count];
            for (int m = 0; m < Foremen.Count; m++)
                master[m] = Shoot(pack, BodyIndex(m, pack.Length), "usta_" + Foremen.IdOf(m));

            var captain = new string[Captains.Count];
            for (int c = 0; c < Captains.Count; c++)
            {
                // Offset so a captain and a master never share a face — the two rosters are collected
                // side by side and twenty distinct people is the point.
                int index = BodyIndex(c + Foremen.Count, pack.Length);
                captain[c] = Shoot(pack, index, "kaptan_" + Captains.IdOf(c));
            }

            AssetDatabase.Refresh();
            Wire(master, captain);
            Debug.Log("Roster portraits baked: " + (master.Length + captain.Length) + " into " + OutFolder);
        }

        /// <summary>
        /// The same index StationForemen uses, so a master's card and the man at his station are the
        /// same person. Duplicated as a constant rather than shared because Game.Gameplay's copy is
        /// private and an editor tool reaching into it would be worse than five characters of maths;
        /// the accompanying test asserts the two agree.
        /// </summary>
        public static int BodyIndex(int who, int packSize)
            => packSize <= 0 ? 0 : (Mathf.Max(0, who) * 5) % packSize;

        private static GameObject[] LoadPack()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PackFolder });
            System.Array.Sort(guids, (a, b) => string.CompareOrdinal(
                AssetDatabase.GUIDToAssetPath(a), AssetDatabase.GUIDToAssetPath(b)));

            var list = new System.Collections.Generic.List<GameObject>();
            for (int i = 0; i < guids.Length; i++)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (go != null && go.GetComponentInChildren<Renderer>(true) != null) list.Add(go);
            }
            return list.ToArray();
        }

        /// <summary>Renders one body and returns the asset path of the sprite.</summary>
        private static string Shoot(GameObject[] pack, int index, string fileName)
        {
            GameObject body = Object.Instantiate(pack[index % pack.Length]);
            // Off in the middle of nowhere so nothing in an open scene can wander into frame.
            body.transform.position = new Vector3(0f, 0f, 5000f);
            body.transform.rotation = Quaternion.Euler(0f, 180f + Yaw, 0f);

            var rig = new GameObject("PortraitRig");
            rig.transform.position = body.transform.position;

            var cam = new GameObject("PortraitCam").AddComponent<Camera>();
            cam.transform.SetParent(rig.transform, false);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);   // transparent: the card art is behind it
            cam.orthographic = true;
            cam.cullingMask = ~0;

            // A dedicated light, because an edit-mode render does not get the scene's.
            var key = new GameObject("PortraitKey").AddComponent<Light>();
            key.transform.SetParent(rig.transform, false);
            key.type = LightType.Directional;
            key.transform.rotation = Quaternion.Euler(28f, 205f, 0f);
            key.intensity = 1.35f;
            key.color = new Color(1f, 0.97f, 0.92f);

            Bounds b = WorldBounds(body);
            float frame = Mathf.Max(0.01f, b.size.y * BodyFraction);
            cam.orthographicSize = frame * 0.5f;
            // Aimed at the crown and dropped by less than half the frame, so there is sky above him.
            var focus = new Vector3(b.center.x, b.max.y - frame * HeadRoom, b.center.z);
            cam.transform.position = focus + new Vector3(0f, 0f, -6f);
            cam.transform.rotation = Quaternion.identity;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 40f;

            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var shot = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            shot.ReadPixels(new Rect(0f, 0f, Size, Size), 0, 0);
            shot.Apply();
            RenderTexture.active = previous;

            string path = OutFolder + "/" + fileName + ".png";
            File.WriteAllBytes(path, shot.EncodeToPNG());

            cam.targetTexture = null;
            Object.DestroyImmediate(shot);
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(rig);
            Object.DestroyImmediate(body);
            return path;
        }

        private static Bounds WorldBounds(GameObject go)
        {
            Renderer[] rends = go.GetComponentsInChildren<Renderer>(true);
            var b = new Bounds(go.transform.position, Vector3.zero);
            bool any = false;
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] is ParticleSystemRenderer) continue;
                if (!any) { b = rends[i].bounds; any = true; }
                else b.Encapsulate(rends[i].bounds);
            }
            return b;
        }

        /// <summary>Sprite import settings, then the arrays on the two screens.</summary>
        private static void Wire(string[] masterPaths, string[] captainPaths)
        {
            var master = new Sprite[masterPaths.Length];
            for (int i = 0; i < masterPaths.Length; i++) master[i] = AsSprite(masterPaths[i]);
            var captain = new Sprite[captainPaths.Length];
            for (int i = 0; i < captainPaths.Length; i++) captain[i] = AsSprite(captainPaths[i]);

            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            int wired = 0;
            foreach (var ui in Object.FindObjectsByType<Game.UI.ForemanRosterUI>(FindObjectsInactive.Include))
            {
                var f = typeof(Game.UI.ForemanRosterUI).GetField("portraits", flags);
                if (f == null) continue;
                Undo.RecordObject(ui, "Wire master portraits");
                f.SetValue(ui, master);
                EditorUtility.SetDirty(ui);
                wired++;
            }
            foreach (var ui in Object.FindObjectsByType<Game.UI.CaptainRosterUI>(FindObjectsInactive.Include))
            {
                var f = typeof(Game.UI.CaptainRosterUI).GetField("portraits", flags);
                if (f == null) continue;
                Undo.RecordObject(ui, "Wire captain portraits");
                f.SetValue(ui, captain);
                EditorUtility.SetDirty(ui);
                wired++;
            }

            if (wired > 0)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
            }
            Debug.Log("Portraits wired into " + wired + " roster screen(s).");
        }

        private static Sprite AsSprite(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
