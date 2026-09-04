using Game.Core;
using Game.Data;
using Game.Systems;
using Game.UI;
using NUnit.Framework;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class RosterUiSmokeTests
    {
        private GameObject _root;
        private AccessibilityConfig _accessibility;

        [SetUp]
        public void SetUp()
        {
            ServiceLocator.Clear();
            _root = new GameObject("RosterUiSmokeRoot");
            ServiceLocator.Register(new LocalizationService());
            _accessibility = ScriptableObject.CreateInstance<AccessibilityConfig>();
            typeof(AccessibilityConfig).GetField("textScale", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_accessibility, 1.5f);
            ServiceLocator.Register(_accessibility);
        }

        [TearDown]
        public void TearDown()
        {
            ServiceLocator.Clear();
            if (_root != null) Object.DestroyImmediate(_root);
            if (_accessibility != null) Object.DestroyImmediate(_accessibility);
            var eventSystem = Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (eventSystem != null) Object.DestroyImmediate(eventSystem.gameObject);
        }

        [Test]
        public void MasterRosterBuildsFiltersAndOpensSharedDetails()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var foremen = new ForemanService(data, wallet, Foremen.Tuning.Default);
            ServiceLocator.Register(wallet);
            ServiceLocator.Register(foremen);
            foremen.GrantDuplicates(IslandEconomy.Storage, 2);

            var ui = _root.AddComponent<ForemanRosterUI>();
            InvokeAwake(ui);
            ui.Show();
            Click(ui.transform, "Sirala", 4);
            Click(ui.transform, "Filtre", 1);
            Assert.That(ActiveCards(ui.transform, "Kart_"), Is.EqualTo(1), "owned filter");
            Click(ui.transform, "Filtre", 1);
            Assert.That(ActiveCards(ui.transform, "Kart_"), Is.EqualTo(Foremen.Count - 1), "locked filter");
            Click(ui.transform, "Filtre", 1);

            Assert.That(Find(ui.transform, "Kart_" + IslandEconomy.Storage).gameObject.activeInHierarchy, Is.True);
            Assert.That(ActiveCards(ui.transform, "Kart_"), Is.EqualTo(1), "upgrade-ready filter");
            Click(ui.transform, "Kart_" + IslandEconomy.Storage, 1);
            Assert.That(Find(ui.transform, "KadroDetayKarartma").gameObject.activeInHierarchy, Is.True);
            Assert.That(Find(ui.transform, "Sirala").GetComponentInChildren<Text>().resizeTextMaxSize,
                        Is.GreaterThanOrEqualTo(33), "maximum text scale must reach roster controls");
            Click(ui.transform, "Filtre", 1);
            Assert.That(ActiveCards(ui.transform, "Kart_"), Is.EqualTo(Foremen.Count), "all filter");
            AssertPortraitCanvas(ui.transform, "UstabasiKanvas");
            AssertActiveCardAnchors(ui.transform, "Kart_");
        }

        [Test]
        public void CaptainRosterBuildsSortsFiltersAndOpensSharedDetails()
        {
            var data = new SaveData();
            var captains = new CaptainService(data, Captains.Tuning.Default, CaptainCrate.Tuning.Default);
            ServiceLocator.Register(captains);
            data.captainLevels[0] = 1;
            data.captainDuplicates[0] = captains.DuplicatesNeeded(0);

            var ui = _root.AddComponent<CaptainRosterUI>();
            InvokeAwake(ui);
            ui.Show();
            Click(ui.transform, "Sirala", 4);
            Click(ui.transform, "Filtre", 1);
            Assert.That(ActiveCards(ui.transform, "Kaptan_"), Is.EqualTo(1), "owned filter");
            Click(ui.transform, "Filtre", 1);
            Assert.That(ActiveCards(ui.transform, "Kaptan_"), Is.EqualTo(Captains.Count - 1), "locked filter");
            Transform locked = Find(ui.transform, "Kaptan_1");
            string lockedName = Find(locked, "Ad").GetComponentInChildren<Text>().text;
            Assert.That(lockedName, Is.EqualTo(Loc.T("kaptan.ad." + Captains.IdOf(1))),
                        "a locked captain remains a named collection goal");
            Click(ui.transform, "Filtre", 1);

            Assert.That(Find(ui.transform, "Kaptan_0").gameObject.activeInHierarchy, Is.True);
            Assert.That(ActiveCards(ui.transform, "Kaptan_"), Is.EqualTo(1), "upgrade-ready filter");
            Click(ui.transform, "Kaptan_0", 1);
            Assert.That(Find(ui.transform, "KadroDetayKarartma").gameObject.activeInHierarchy, Is.True);
            Click(ui.transform, "Filtre", 1);
            Assert.That(ActiveCards(ui.transform, "Kaptan_"), Is.EqualTo(Captains.Count), "all filter");
            AssertPortraitCanvas(ui.transform, "KaptanKanvas");
            AssertActiveCardAnchors(ui.transform, "Kaptan_");
        }

        private static void Click(Transform root, string name, int times)
        {
            Button button = Find(root, name).GetComponent<Button>();
            Assert.That(button, Is.Not.Null, name + " must be clickable");
            for (int i = 0; i < times; i++) button.onClick.Invoke();
        }

        private static Transform Find(Transform root, string name)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) if (all[i].name == name) return all[i];
            Assert.Fail("Missing UI node: " + name);
            return null;
        }

        private static int ActiveCards(Transform root, string prefix)
        {
            int count = 0;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i].name.StartsWith(prefix) && all[i].parent != null
                    && all[i].parent.name != "KadroDetayKarartma" && all[i].gameObject.activeInHierarchy)
                    count++;
            return count;
        }

        private static void AssertPortraitCanvas(Transform root, string name)
        {
            CanvasScaler scaler = Find(root, name).GetComponent<CanvasScaler>();
            Assert.That(scaler, Is.Not.Null);
            Assert.That(scaler.referenceResolution, Is.EqualTo(new Vector2(1080f, 1920f)));
            Assert.That(scaler.matchWidthOrHeight, Is.EqualTo(0.5f));
        }

        private static void AssertActiveCardAnchors(Transform root, string prefix)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (!all[i].name.StartsWith(prefix) || !all[i].gameObject.activeInHierarchy) continue;
                var rect = all[i] as RectTransform;
                if (rect == null) continue;
                Assert.That(rect.anchorMin.x, Is.InRange(0f, 1f), all[i].name);
                Assert.That(rect.anchorMin.y, Is.InRange(0f, 1f), all[i].name);
                Assert.That(rect.anchorMax.x, Is.InRange(0f, 1f), all[i].name);
                Assert.That(rect.anchorMax.y, Is.InRange(0f, 1f), all[i].name);
                Assert.That(rect.anchorMax.x, Is.GreaterThan(rect.anchorMin.x), all[i].name);
                Assert.That(rect.anchorMax.y, Is.GreaterThan(rect.anchorMin.y), all[i].name);
            }
        }

        private static void InvokeAwake(MonoBehaviour component)
        {
            MethodInfo awake = component.GetType().GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(awake, Is.Not.Null);
            awake.Invoke(component, null);
        }
    }
}
