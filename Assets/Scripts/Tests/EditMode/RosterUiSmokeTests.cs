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
            // The Common mine master: five cards is exactly his first star, so he lands owned AND
            // upgrade-ready, which is what the two filters below are looking for.
            int owned = Foremen.IndexOf(Foremen.Mine, Foremen.Rarity.Common);
            foremen.GrantDuplicates(owned, Foremen.CardsToStar(owned, 1, Foremen.Tuning.Default));

            var ui = _root.AddComponent<ForemanRosterUI>();
            InvokeAwake(ui);
            ui.Show();
            Click(ui.transform, "Sirala", 4);
            Click(ui.transform, "Filtre", 1);
            Assert.That(ActiveCards(ui.transform, "Kart_"), Is.EqualTo(1), "owned filter");
            Click(ui.transform, "Filtre", 1);
            Assert.That(ActiveCards(ui.transform, "Kart_"), Is.EqualTo(Foremen.Count - 1), "locked filter");
            Click(ui.transform, "Filtre", 1);

            Assert.That(Find(ui.transform, "Kart_" + owned).gameObject.activeInHierarchy, Is.True);
            Assert.That(ActiveCards(ui.transform, "Kart_"), Is.EqualTo(1), "upgrade-ready filter");
            Click(ui.transform, "Kart_" + owned, 1);
            Assert.That(Find(ui.transform, "KadroDetayKarartma").gameObject.activeInHierarchy, Is.True);
            Assert.That(Find(ui.transform, "Sirala").GetComponentInChildren<Text>().resizeTextMaxSize,
                        Is.GreaterThanOrEqualTo(33), "maximum text scale must reach roster controls");
            Click(ui.transform, "Filtre", 1);
            Assert.That(ActiveCards(ui.transform, "Kart_"), Is.EqualTo(Foremen.Count), "all filter");
            AssertPortraitCanvas(ui.transform, "UstabasiKanvas");
            AssertActiveCardAnchors(ui.transform, "Kart_");
        }

        [Test]
        public void TheAssignButtonActuallyPostsTheMasterYouTapped()
        {
            // The detail sheet's second button. It has to be reachable (present, active, hooked up AND
            // interactable) and it has to move the posting — a button that only greys out is
            // indistinguishable from one that does nothing.
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var foremen = new ForemanService(data, wallet, Foremen.Tuning.Default);
            ServiceLocator.Register(wallet);
            ServiceLocator.Register(foremen);

            int common = Foremen.IndexOf(Foremen.Mine, Foremen.Rarity.Common);
            int legend = Foremen.IndexOf(Foremen.Mine, Foremen.Rarity.Legendary);
            foremen.GrantDuplicates(common, 1);       // takes the empty post
            foremen.GrantDuplicates(legend, 1);       // owned, benched
            Assert.That(foremen.ActiveAt(Foremen.Mine), Is.EqualTo(common), "the premise");

            var ui = _root.AddComponent<ForemanRosterUI>();
            InvokeAwake(ui);
            ui.Show();

            Click(ui.transform, "Kart_" + legend, 1);
            Transform sheet = Find(ui.transform, "KadroDetayKarartma");
            Assert.That(sheet.gameObject.activeInHierarchy, Is.True, "the sheet must open");

            Transform assign = Find(ui.transform, "Ikincil");
            Assert.That(assign.gameObject.activeInHierarchy, Is.True, "the assign button must be shown");
            var button = assign.GetComponent<Button>();
            Assert.That(button.interactable, Is.True, "a benched master you own must be postable");

            button.onClick.Invoke();
            Assert.That(foremen.ActiveAt(Foremen.Mine), Is.EqualTo(legend),
                        "tapping assign did not move the posting");
        }

        [Test]
        public void TheAssignButtonIsHiddenForTheMasterAlreadyPosted()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var foremen = new ForemanService(data, wallet, Foremen.Tuning.Default);
            ServiceLocator.Register(wallet);
            ServiceLocator.Register(foremen);

            int common = Foremen.IndexOf(Foremen.Mine, Foremen.Rarity.Common);
            foremen.GrantDuplicates(common, 1);

            var ui = _root.AddComponent<ForemanRosterUI>();
            InvokeAwake(ui);
            ui.Show();
            Click(ui.transform, "Kart_" + common, 1);

            Assert.That(Find(ui.transform, "Ikincil").gameObject.activeInHierarchy, Is.False,
                        "the man already at the post has nothing to assign");
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

        /// <summary>
        /// THE PORTRAIT FIT. The game allows portrait and nothing else, and both screens were first
        /// laid out side by side — a tall panel down the left, content in the remaining two thirds.
        /// On a 1080x1920 sheet that put the shelf in a 273px sliver and crushed the cards.
        ///
        /// Everything on these screens is anchored with zero offsets, so the anchor rect IS the
        /// layout: a band that runs past 1 is off the sheet, and two bands that intersect are drawn on
        /// top of each other. Checking the fractions catches both without needing a camera.
        /// </summary>
        [Test]
        public void TheMasterScreenBandsFitThePortraitSheetWithoutOverlapping()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            ServiceLocator.Register(wallet);
            ServiceLocator.Register(new ForemanService(data, wallet, Foremen.Tuning.Default));

            var ui = _root.AddComponent<ForemanRosterUI>();
            InvokeAwake(ui);
            ui.Show();

            var bands = new[] { "Serit", "Carpan", "Kese", "Kapat", "Sandik", "Sirala", "Filtre" };
            AssertOnSheet(ui.transform, bands);
            AssertNoOverlap(ui.transform, "Sandik", "Sirala");
            AssertNoOverlap(ui.transform, "Sandik", "Kart_0");
            AssertNoOverlap(ui.transform, "Sirala", "Kart_0");
            AssertNoOverlap(ui.transform, "Serit", "Sandik");
            AssertCardsTile(ui.transform, "Kart_", Foremen.Count);
        }

        [Test]
        public void TheCaptainScreenBandsFitThePortraitSheetWithoutOverlapping()
        {
            var data = new SaveData();
            ServiceLocator.Register(new CaptainService(data, Captains.Tuning.Default,
                                                       CaptainCrate.Tuning.Default));

            var ui = _root.AddComponent<CaptainRosterUI>();
            InvokeAwake(ui);
            ui.Show();

            AssertOnSheet(ui.transform, new[] { "Serit", "Harita", "Kapat", "Sandik", "Sirala", "Filtre" });
            AssertNoOverlap(ui.transform, "Sandik", "Sirala");
            AssertNoOverlap(ui.transform, "Sandik", "Kaptan_0");
            AssertNoOverlap(ui.transform, "Sirala", "Kaptan_0");
            AssertCardsTile(ui.transform, "Kaptan_", Captains.Count);
        }

        /// <summary>Every named band anchored inside the sheet, with a real width and height.</summary>
        private static void AssertOnSheet(Transform root, string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                var r = (RectTransform)Find(root, names[i]);
                Assert.That(r.anchorMin.x, Is.InRange(0f, 1f), names[i] + " left");
                Assert.That(r.anchorMin.y, Is.InRange(0f, 1f), names[i] + " bottom");
                Assert.That(r.anchorMax.x, Is.InRange(0f, 1f), names[i] + " right");
                Assert.That(r.anchorMax.y, Is.InRange(0f, 1f), names[i] + " top");
                Assert.That(r.anchorMax.x, Is.GreaterThan(r.anchorMin.x), names[i] + " width");
                Assert.That(r.anchorMax.y, Is.GreaterThan(r.anchorMin.y), names[i] + " height");
            }
        }

        private static void AssertNoOverlap(Transform root, string a, string b)
        {
            var ra = (RectTransform)Find(root, a);
            var rb = (RectTransform)Find(root, b);
            Assert.That(Intersects(ra, rb), Is.False, a + " overlaps " + b);
        }

        /// <summary>Every visible card inside the sheet, and no two of them on top of each other.</summary>
        private static void AssertCardsTile(Transform root, string prefix, int count)
        {
            var cards = new RectTransform[count];
            for (int i = 0; i < count; i++) cards[i] = (RectTransform)Find(root, prefix + i);

            for (int i = 0; i < count; i++)
            {
                Assert.That(cards[i].anchorMin.x, Is.InRange(0f, 1f), prefix + i);
                Assert.That(cards[i].anchorMax.y, Is.InRange(0f, 1f), prefix + i);
                for (int j = i + 1; j < count; j++)
                    Assert.That(Intersects(cards[i], cards[j]), Is.False,
                                prefix + i + " overlaps " + prefix + j);
            }
        }

        private static bool Intersects(RectTransform a, RectTransform b)
        {
            const float slack = 0.0005f;   // touching edges are not an overlap
            return a.anchorMin.x < b.anchorMax.x - slack && a.anchorMax.x > b.anchorMin.x + slack
                && a.anchorMin.y < b.anchorMax.y - slack && a.anchorMax.y > b.anchorMin.y + slack;
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
