using System.Collections.Generic;
using System.Reflection;
using Game.Core;
using Game.Systems;
using Game.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class CardCollectionArtworkTests
    {
        private GameObject _root;
        private CardCollectionUI _ui;
        private CardCollectionService _cards;

        [SetUp]
        public void SetUp()
        {
            ServiceLocator.Clear();
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            _cards = new CardCollectionService(data, null, new TimeService(), wallet, null, new System.Random(7));
            ServiceLocator.Register(wallet); ServiceLocator.Register(_cards);
            ServiceLocator.Register(new LocalizationService());
            _root = new GameObject("CollectionArtworkTest");
            _ui = _root.AddComponent<CardCollectionUI>();
            typeof(CardCollectionUI).GetField("_usePortraitArtwork", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_ui, true);
            typeof(CardCollectionUI).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_ui, null);
            _ui.Show(); Canvas.ForceUpdateCanvases();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            ServiceLocator.Clear();
        }

        [Test]
        public void AllElevenSourceAssetsAreUsedWithTheirProportionsPreserved()
        {
            var sources = new HashSet<string>();
            foreach (var image in _root.GetComponentsInChildren<Image>(true))
            {
                if (image.sprite == null) continue;
                string path = AssetDatabase.GetAssetPath(image.sprite.texture);
                if (!path.Contains("/kart koleksiyonu/")) continue;
                sources.Add(path);
                Assert.That(image.preserveAspect, Is.True, image.name);
                Assert.That(image.type, Is.EqualTo(Image.Type.Simple), image.name);
            }
            // Eleven of the kit's twelve: the pack odds window is drawn by the shared OddsSheetUI in the
            // card pack's coat now, the same sheet the masters and captain screens open.
            Assert.That(sources.Count, Is.EqualTo(11));
            var card = (RectTransform)Find("Kart_0");
            var sprite = card.GetComponent<Image>().sprite;
            Assert.That(card.rect.width / card.rect.height, Is.EqualTo(sprite.rect.width / sprite.rect.height).Within(0.001f));
        }

        [Test]
        public void SetTabsSwitchTheVisibleCardsAndReturnTheScrollToTheTop()
        {
            var scroll = Find("CardViewport").GetComponent<ScrollRect>();
            for (int set = 0; set < CardCollectionCatalogue.SetCount; set++)
            {
                scroll.verticalNormalizedPosition = 0;
                Click("SetSekme" + set);
                int count = 0;
                foreach (int card in CardCollectionCatalogue.CardsInSet(set))
                    if (Find("Kart_" + card).gameObject.activeSelf) count++;
                Assert.That(count, Is.EqualTo(CardCollectionCatalogue.CardsInSetCount(set)));
                if (set > 0) Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(1).Within(0.01f));
            }
            Click("QuickFilter1");
            Assert.That(Find("FiltreBos").GetComponentInChildren<Text>(true).gameObject.activeSelf, Is.True);
            Click("QuickFilter2");
            Assert.That(Find("FiltreBos").GetComponentInChildren<Text>(true).gameObject.activeSelf, Is.False);
        }

        [Test]
        public void ClosingAndReopeningTheScreenDismissesEveryPopup()
        {
            Click("OpenCollectionPacks");
            Assert.That(Find("CollectionPack").gameObject.activeSelf, Is.True);
            Click("Oran"); Assert.That(Find("OranKarartma").gameObject.activeSelf, Is.True);
            _ui.Hide(); _ui.Show();
            Click("SetInfoShortcut"); Assert.That(Find("CollectionSetInfo").gameObject.activeSelf, Is.True);
            _ui.Hide(); _ui.Show();
            Click("Kart_0"); Assert.That(Find("KadroDetayKarartma").gameObject.activeSelf, Is.True);
            _ui.Hide(); _ui.Show();
            foreach (var name in new[] { "CollectionPack", "CollectionSetInfo", "OranKarartma", "KadroDetayKarartma", "CollectionResult" })
                Assert.That(Find(name).gameObject.activeSelf, Is.False, name);
            Assert.That(_ui.Visible, Is.True);
        }

        [Test]
        public void OpeningAPackShowsTheRealReceiptAndDoneReturnsToThePackPanel()
        {
            _cards.GrantPacks(2, CardCollection.PackSource.GoalMilestone);
            Click("OpenCollectionPacks"); Click("PaketAc");
            Assert.That(_cards.UnopenedPackCount, Is.EqualTo(1));
            Assert.That(Find("CollectionResult").gameObject.activeSelf, Is.True);
            Assert.That(Find("Receipt").GetComponentInChildren<Text>().text, Is.Not.Empty);
            Assert.That(Find("RewardValue0").GetComponentInChildren<Text>().text, Is.EqualTo("+1"));
            Click("Done");
            Assert.That(Find("CollectionResult").gameObject.activeSelf, Is.False);
            Assert.That(Find("CollectionPack").gameObject.activeSelf, Is.True);
        }

        private Transform Find(string name)
        {
            foreach (var item in _root.GetComponentsInChildren<Transform>(true)) if (item.name == name) return item;
            Assert.Fail("Missing " + name); return null;
        }

        private void Click(string name) => Find(name).GetComponent<Button>().onClick.Invoke();
    }
}
