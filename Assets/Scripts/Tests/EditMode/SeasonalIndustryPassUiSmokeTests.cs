using System.Reflection;
using Game.Core;
using Game.UI;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class SeasonalIndustryPassUiSmokeTests
    {
        [Test]
        public void ScreenBuildsScrollableTwoLaneTrackWithoutSceneReferences()
        {
            ServiceLocator.Clear();
            var host = new GameObject("SeasonalIndustryPassUiSmoke");
            try
            {
                SeasonalIndustryPassUI screen = host.AddComponent<SeasonalIndustryPassUI>();
                MethodInfo awake = typeof(SeasonalIndustryPassUI).GetMethod("Awake",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(awake, Is.Not.Null);
                awake.Invoke(screen, null);

                Canvas[] canvases = host.GetComponentsInChildren<Canvas>(true);
                Assert.That(canvases, Has.Length.EqualTo(1));
                Transform sheet = canvases[0].transform.Find("Karartma/Zemin");
                Assert.That(sheet, Is.Not.Null);
                Assert.That(sheet.Find("PremiumAl"), Is.Not.Null);
                Assert.That(sheet.Find("GeriYukle"), Is.Not.Null);
                Transform content = sheet.Find("KademeListesi/Viewport/Content");
                Assert.That(content, Is.Not.Null);
                Assert.That(content.Find("Kademe0/UcretsizAl"), Is.Not.Null);
                Assert.That(content.Find("Kademe0/PremiumAl"), Is.Not.Null);
                Assert.That(content.Find("Kademe14/PremiumAl"), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(host);
                ServiceLocator.Clear();
            }
        }
    }
}
