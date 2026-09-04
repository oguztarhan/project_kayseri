using System.Reflection;
using Game.Core;
using Game.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class ProductionSprintUiSmokeTests
    {
        [Test]
        public void ScreenBuildsBothTabsAndClaimRowsWithoutSceneReferences()
        {
            ServiceLocator.Clear();
            var host = new GameObject("ProductionSprintUiSmoke");
            try
            {
                ProductionSprintUI screen = host.AddComponent<ProductionSprintUI>();
                MethodInfo awake = typeof(ProductionSprintUI).GetMethod("Awake",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(awake, Is.Not.Null);
                awake.Invoke(screen, null);

                Canvas[] canvases = host.GetComponentsInChildren<Canvas>(true);
                Assert.That(canvases, Has.Length.EqualTo(1));

                Transform scrim = canvases[0].transform.Find("Karartma");
                Assert.That(scrim, Is.Not.Null);
                Transform sheet = scrim.Find("Zemin");
                Assert.That(sheet, Is.Not.Null);
                Assert.That(sheet.Find("Sekme0"), Is.Not.Null);
                Assert.That(sheet.Find("Sekme1"), Is.Not.Null);
                Assert.That(sheet.Find("Satir0/OduluAl"), Is.Not.Null);
                Assert.That(sheet.Find("Satir4/OduluAl"), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(host);
                ServiceLocator.Clear();
            }
        }
    }
}
