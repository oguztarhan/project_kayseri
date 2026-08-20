using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Tests
{
    public sealed class RenderingSafetyTests
    {
        [TestCase("Assets/Prefabs/Island/Copper/Island_Phase1.prefab")]
        [TestCase("Assets/Prefabs/Island/Copper/Island_Phase2.prefab")]
        [TestCase("Assets/Prefabs/Island/Copper/Island_Phase3.prefab")]
        public void CopperPhaseRenderersAreNotBuildTimeStaticBatched(string path)
        {
            GameObject root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
            try
            {
                MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
                Assert.That(renderers.Length, Is.GreaterThan(0));
                foreach (MeshRenderer renderer in renderers)
                {
                    UnityEditor.StaticEditorFlags flags =
                        UnityEditor.GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
                    Assert.That((flags & UnityEditor.StaticEditorFlags.BatchingStatic) != 0, Is.False,
                                renderer.name + " would be combined before district visibility is resolved.");
                }
            }
            finally
            {
                UnityEditor.PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void CopperPhaseOneKeepsTheTransparentWaterSceneOverride()
        {
            const string path = "Assets/Scenes/Main.unity";
            Scene scene = SceneManager.GetSceneByPath(path);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

            try
            {
                GameObject copper = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                    if (root.name == "Island_Copper") { copper = root; break; }

                Assert.That(copper, Is.Not.Null, "Main scene must contain Island_Copper.");
                Transform phase = copper.transform.Find("Island_Phase1");
                Assert.That(phase, Is.Not.Null);
                MeshRenderer sea = null;
                foreach (MeshRenderer renderer in phase.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (renderer.name != "Sea") continue;
                    sea = renderer;
                    break;
                }

                Assert.That(sea, Is.Not.Null);
                Assert.That(sea.sharedMaterial, Is.Not.Null);
                Assert.That(sea.sharedMaterial.name, Is.EqualTo("M_StylizedColdWater"));
                Assert.That(sea.sharedMaterial.renderQueue, Is.GreaterThanOrEqualTo(3000));
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void OperationCameraFarPlaneIsSolvedFromTheViewportInsteadOfTheArchipelago()
        {
            Type type = Type.GetType("Game.UI.OperationCameraBoot, Game.UI");
            Assert.That(type, Is.Not.Null);
            MethodInfo method = type.GetMethod("RequiredFarClip", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            float far = (float)method.Invoke(null, new object[]
            {
                Quaternion.Euler(46f, 90f, 0f), 30f, 9f / 16f, 1000f, 400f,
            });

            Assert.That(far, Is.GreaterThan(1000f), "Maximum zoom must remain fully visible.");
            Assert.That(far, Is.LessThan(5000f), "The old 20,000-unit depth range must not return.");
        }
    }
}
