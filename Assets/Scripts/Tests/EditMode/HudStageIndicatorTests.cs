using System.Reflection;
using Game.Core;
using Game.Systems;
using Game.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>Ensures the stage label uses the shipped HUD's styling seam rather than new prefab wiring.</summary>
    public sealed class HudStageIndicatorTests
    {
        private const string HudPrefab = "Assets/Prefabs/UI/UI_HUD.prefab";

        [Test]
        public void HudBuildsTheCurrentStagePillBelowSettingsFromTheAuthoredRatePill()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefab);
            Assert.That(prefab, Is.Not.Null, HudPrefab);

            GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            try
            {
                HudUI hud = instance.GetComponentInChildren<HudUI>(true);
                Assert.That(hud, Is.Not.Null);

                var data = new SaveData();
                var chapters = new ChapterService(data, new WalletService(data.wallet), null, Chapters.Tuning.Default);
                Set(hud, "_stages", new StageService(chapters));
                Call(hud, "BuildStageIndicator");
                Call(hud, "PlaceStageIndicator");
                Call(hud, "RefreshStageIndicator");

                var indicator = (RectTransform)Get(hud, "_stageIndicator");
                object label = Get(hud, "_stageIndicatorLabel");
                Assert.That(indicator, Is.Not.Null);
                Assert.That(indicator.name, Is.EqualTo("AsamaGostergesi"));
                Assert.That(label, Is.Not.Null);
                Assert.That(LabelText(label), Is.EqualTo("1-1"));

                var settings = (UnityEngine.UI.Button)Get(hud, "settingsButton");
                Assert.That(indicator.parent, Is.EqualTo(settings.transform.parent));
                Assert.That(indicator.anchoredPosition.y,
                            Is.LessThan(((RectTransform)settings.transform).anchoredPosition.y));
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
            }
        }

        private static object Get(object target, string name)
            => Field(name).GetValue(target);

        private static void Set(object target, string name, object value)
            => Field(name).SetValue(target, value);

        private static void Call(object target, string name)
            => typeof(HudUI).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);

        private static string LabelText(object label)
            => (string)label.GetType().GetProperty("text").GetValue(label);

        private static FieldInfo Field(string name)
            => typeof(HudUI).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
    }
}
