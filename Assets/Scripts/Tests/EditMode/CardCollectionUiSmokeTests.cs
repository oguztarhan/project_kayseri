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
    /// <summary>
    /// The collection screen (Docs/PLAN_14, slice 7), built the way the game builds it — a component
    /// whose Awake finds the service in the locator — and driven through its buttons rather than
    /// through the service, so a control that draws but is not hooked up fails here.
    /// </summary>
    public sealed class CardCollectionUiSmokeTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            ServiceLocator.Clear();
            _root = new GameObject("CardCollectionUiSmokeRoot");
            ServiceLocator.Register(new LocalizationService());
        }

        [TearDown]
        public void TearDown()
        {
            ServiceLocator.Clear();
            if (_root != null) Object.DestroyImmediate(_root);
            var eventSystem = Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (eventSystem != null) Object.DestroyImmediate(eventSystem.gameObject);
        }

        private static CardCollectionService Register(SaveData data)
        {
            var wallet = new WalletService(data.wallet);
            var cards = new CardCollectionService(data, null, new TimeService(), wallet, null, new System.Random(7));
            ServiceLocator.Register(wallet);
            ServiceLocator.Register(cards);
            return cards;
        }

        private CardCollectionUI Open()
        {
            var ui = _root.AddComponent<CardCollectionUI>();
            InvokeAwake(ui);
            ui.Show();
            return ui;
        }

        /// <summary>Owns every card of a set at level 1, written before the service is built so its
        /// effect snapshot starts from it.</summary>
        private static void OwnSet(SaveData data, int set)
        {
            int[] cards = CardCollectionCatalogue.CardsInSet(set);
            for (int i = 0; i < cards.Length; i++)
                data.cardCollection.FindOrAdd(CardCollectionCatalogue.IdOf(cards[i])).level = 1;
        }

        // ------------------------------------------------------------------ build

        [Test]
        public void TheScreenBuildsAndOpensWithNoServiceRegistered()
        {
            CardCollectionUI ui = null;
            Assert.DoesNotThrow(() => ui = Open());
            Assert.That(ui.Visible, Is.True);
            Assert.That(CountNamed(ui.transform, "Kart_"), Is.EqualTo(CardCollectionCatalogue.Count),
                        "one tile per catalogue card, built once");
            Assert.That(ui.PendingCount(), Is.Zero);
            ui.Hide();
            Assert.That(ui.Visible, Is.False);
        }

        [Test]
        public void TheBandsFitThePortraitSheetWithoutOverlapping()
        {
            Register(new SaveData());
            CardCollectionUI ui = Open();

            AssertPortraitCanvas(ui.transform, "KoleksiyonKanvas");
            AssertOnSheet(ui.transform, new[] { "Serit", "Paketler", "Kapat", "Paket", "SetSekme0",
                                                "SetBilgi", "Sirala", "Filtre" });
            AssertNoOverlap(ui.transform, "Paket", "SetSekme0");
            AssertNoOverlap(ui.transform, "SetSekme0", "SetBilgi");
            AssertNoOverlap(ui.transform, "SetBilgi", "Sirala");
            int[] first = CardCollectionCatalogue.CardsInSet(0);
            AssertNoOverlap(ui.transform, "Sirala", "Kart_" + first[0]);
            AssertTilesDoNotOverlap(ui.transform, first);
        }

        [Test]
        public void EachSetTabShowsExactlyItsOwnCards()
        {
            Register(new SaveData());
            CardCollectionUI ui = Open();

            for (int s = 0; s < CardCollectionCatalogue.SetCount; s++)
            {
                Click(ui.transform, "SetSekme" + s, 1);
                int[] cards = CardCollectionCatalogue.CardsInSet(s);
                Assert.That(ActiveTiles(ui.transform), Is.EqualTo(cards.Length), "set " + s);
                for (int i = 0; i < cards.Length; i++)
                    Assert.That(Find(ui.transform, "Kart_" + cards[i]).gameObject.activeInHierarchy, Is.True,
                                "set " + s + " card " + cards[i]);
                AssertTilesDoNotOverlap(ui.transform, cards);
            }
        }

        // --------------------------------------------------------------- packs

        [Test]
        public void TheOpenButtonSpendsExactlyOnePackAndShowsWhatCameOut()
        {
            var data = new SaveData();
            CardCollectionService cards = Register(data);
            cards.GrantPacks(2, CardCollection.PackSource.GoalMilestone);
            CardCollectionUI ui = Open();

            Assert.That(Find(ui.transform, "PaketAc").GetComponent<Button>().interactable, Is.True);
            Click(ui.transform, "PaketAc", 1);

            Assert.That(cards.UnopenedPackCount, Is.EqualTo(1));
            Assert.That(cards.OwnedCardCount, Is.EqualTo(1), "a first pack always unlocks a card");
            Assert.That(Find(ui.transform, "SonCekilis").GetComponentInChildren<Text>().text, Is.Not.Empty,
                        "the pack's outcome must be reported");

            // The screen jumps to the set of the card it just drew, so that tile is on screen.
            int drawn = -1;
            for (int c = 0; c < CardCollectionCatalogue.Count; c++) if (cards.IsOwned(c)) drawn = c;
            Assert.That(Find(ui.transform, "Kart_" + drawn).gameObject.activeInHierarchy, Is.True);

            Click(ui.transform, "PaketAc", 1);
            Assert.That(cards.UnopenedPackCount, Is.Zero);
            Assert.That(Find(ui.transform, "PaketAc").GetComponent<Button>().interactable, Is.False,
                        "no pack left, nothing to open");
        }

        [Test]
        public void TheDailyButtonBanksOnePackAndThenWaits()
        {
            CardCollectionService cards = Register(new SaveData());
            CardCollectionUI ui = Open();
            Assert.That(cards.DailyPackReady, Is.True, "the premise: a fresh save has today's pack");

            Button daily = Find(ui.transform, "GunlukPaket").GetComponent<Button>();
            Assert.That(daily.interactable, Is.True);
            // Found in Play mode: with no pill art wired the stand-in sprite is near-white, and a
            // live button left untinted drew white text on a white pill — an invisible button.
            Assert.That(daily.GetComponent<Image>().color, Is.Not.EqualTo(Color.white),
                        "an unwired screen must keep a live button's colour");
            daily.onClick.Invoke();

            Assert.That(cards.UnopenedPackCount, Is.EqualTo(1), "banked, not opened");
            Assert.That(cards.OwnedCardCount, Is.Zero);
            Assert.That(daily.interactable, Is.False, "one a day");
            daily.onClick.Invoke();
            Assert.That(cards.UnopenedPackCount, Is.EqualTo(1));
        }

        [Test]
        public void TheOddsBadgeOpensTheCardPackSheet()
        {
            Register(new SaveData());
            CardCollectionUI ui = Open();
            Click(ui.transform, "Oran", 1);
            Assert.That(Find(ui.transform, "OranKarartma").gameObject.activeInHierarchy, Is.True);
        }

        // --------------------------------------------------------------- cards

        [Test]
        public void TheTileButtonLevelsTheCardItSitsOn()
        {
            var data = new SaveData();
            int card = CardCollectionCatalogue.CardsInSet(0)[0];
            CardCollectionProgress row = data.cardCollection.FindOrAdd(CardCollectionCatalogue.IdOf(card));
            row.level = 1;
            row.duplicates = CardCollectionCatalogue.DuplicatesToLevel(card, 1, CardCollection.Tuning.Default);
            CardCollectionService cards = Register(data);
            CardCollectionUI ui = Open();

            Button level = Find(Find(ui.transform, "Kart_" + card), "Yukselt").GetComponent<Button>();
            Assert.That(level.interactable, Is.True, "enough copies for level 2");
            level.onClick.Invoke();
            Assert.That(cards.LevelOf(card), Is.EqualTo(2));
            Assert.That(level.interactable, Is.False, "the copies were spent");
        }

        [Test]
        public void LookingAtANewCardClearsItsBadge()
        {
            var data = new SaveData();
            CardCollectionService cards = Register(data);
            cards.GrantPacks(1, CardCollection.PackSource.GoalMilestone);
            CardCollectionUI ui = Open();
            Click(ui.transform, "PaketAc", 1);

            int drawn = -1;
            for (int c = 0; c < CardCollectionCatalogue.Count; c++) if (cards.IsOwned(c)) drawn = c;
            Transform tile = Find(ui.transform, "Kart_" + drawn);
            Assert.That(Find(tile, "Yeni").gameObject.activeSelf, Is.True, "a first copy is NEW");

            tile.GetComponent<Button>().onClick.Invoke();
            Assert.That(Find(ui.transform, "KadroDetayKarartma").gameObject.activeInHierarchy, Is.True);
            Assert.That(cards.IsNew(drawn), Is.False);
            Assert.That(Find(tile, "Yeni").gameObject.activeSelf, Is.False);
        }

        // ---------------------------------------------------------------- sets

        /// <summary>
        /// The plan's promise, on screen: the permanent bonus is live the moment the set is complete,
        /// BEFORE the reward is claimed, and the reward pays exactly once.
        /// </summary>
        [Test]
        public void ACompletedSetShowsItsBonusBeforeTheClaimAndPaysOnce()
        {
            var data = new SaveData();
            OwnSet(data, 0);
            CardCollectionService cards = Register(data);
            var wallet = ServiceLocator.Get<WalletService>();
            CardCollectionUI ui = Open();

            Assert.That(cards.Effects.Income, Is.GreaterThan(0d), "the premise: income cards are owned");
            Text bonus = Find(ui.transform, "SetBonus").GetComponentInChildren<Text>();
            Assert.That(bonus.text, Does.Contain(Loc.T("koleksiyon.set_aktif")),
                        "the bonus must read as live before anything is claimed");

            Button claim = Find(ui.transform, "SetOdulAl").GetComponent<Button>();
            Assert.That(claim.interactable, Is.True);
            long before = wallet.Gems;
            claim.onClick.Invoke();
            long paid = wallet.Gems - before;
            Assert.That(paid, Is.EqualTo(CardCollectionCatalogue.Sets[0].RewardAmount));
            Assert.That(claim.interactable, Is.False);

            claim.onClick.Invoke();
            Assert.That(wallet.Gems - before, Is.EqualTo(paid), "a second tap must pay nothing");
            Assert.That(bonus.text, Does.Contain(Loc.T("koleksiyon.set_aktif")), "and the bonus stays");
        }

        [Test]
        public void TheOpenerCountsEverythingWaiting()
        {
            var data = new SaveData();
            OwnSet(data, 0);                                           // one claimable set reward
            int card = CardCollectionCatalogue.CardsInSet(0)[0];
            data.cardCollection.FindOrAdd(CardCollectionCatalogue.IdOf(card)).duplicates =
                CardCollectionCatalogue.DuplicatesToLevel(card, 1, CardCollection.Tuning.Default);   // one upgrade
            CardCollectionService cards = Register(data);
            cards.GrantPacks(2, CardCollection.PackSource.GoalMilestone);                             // two packs
            CardCollectionUI ui = Open();

            Assert.That(cards.DailyPackReady, Is.True);
            Assert.That(ui.PendingCount(), Is.EqualTo(2 + 1 + 1 + 1));
        }

        // ------------------------------------------------- slice 8: words and art

        /// <summary>
        /// Every card and set the catalogue carries has its name and description row. Read off the
        /// table itself, not through the service, so the language the editor happens to be in cannot
        /// hide a gap — and <c>LocalizationTableTests</c> already holds every row to all 11 columns.
        /// A card authored without its rows would otherwise ship showing its id.
        /// </summary>
        [Test]
        public void EveryCardAndSetHasItsNameAndDescriptionRows()
        {
            var table = Resources.Load<TextAsset>("Diller/metinler");
            Assert.That(table, Is.Not.Null);
            var keys = new System.Collections.Generic.HashSet<string>();
            foreach (string line in table.text.Split('\n'))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                int tab = line.IndexOf('\t');
                if (tab > 0) keys.Add(line.Substring(0, tab));
            }

            for (int c = 0; c < CardCollectionCatalogue.Count; c++)
            {
                Assert.That(keys, Does.Contain(CardCollectionCatalogue.NameKey(c)));
                Assert.That(keys, Does.Contain(CardCollectionCatalogue.DescriptionKey(c)));
            }
            for (int s = 0; s < CardCollectionCatalogue.SetCount; s++)
            {
                Assert.That(keys, Does.Contain(CardCollectionCatalogue.SetNameKey(s)));
                Assert.That(keys, Does.Contain(CardCollectionCatalogue.SetDescriptionKey(s)));
            }
        }

        [Test]
        public void TheDetailsSheetAndSetPanelCarryTheirDescriptions()
        {
            var data = new SaveData();
            int card = CardCollectionCatalogue.CardsInSet(0)[0];
            data.cardCollection.FindOrAdd(CardCollectionCatalogue.IdOf(card)).level = 1;
            Register(data);
            CardCollectionUI ui = Open();

            Assert.That(Find(ui.transform, "SetAciklama").GetComponentInChildren<Text>().text,
                        Is.EqualTo(Loc.T(CardCollectionCatalogue.SetDescriptionKey(0))));

            Click(ui.transform, "Kart_" + card, 1);
            Transform sheet = Find(ui.transform, "KadroDetayKarartma");
            Assert.That(Find(sheet, "Baslik").GetComponentInChildren<Text>().text,
                        Is.EqualTo(Loc.T(CardCollectionCatalogue.NameKey(card))));
            Assert.That(Find(sheet, "Durum").GetComponentInChildren<Text>().text,
                        Is.EqualTo(Loc.T(CardCollectionCatalogue.DescriptionKey(card))));
        }

        /// <summary>
        /// The art hooks draw exactly what the config carries: a card's face and its rarity's frame, the
        /// pack icon, a set's banner on its tab — and stay switched off, not stretched blanks, for
        /// anything nobody has wired.
        /// </summary>
        [Test]
        public void TheArtHooksDrawWhatTheConfigCarriesAndNothingElse()
        {
            Sprite art = TestSprite();
            var config = ScriptableObject.CreateInstance<CardCollectionConfig>();
            try
            {
                int card = CardCollectionCatalogue.CardsInSet(0)[0];
                var frames = new Sprite[CardCollection.RarityCount];
                frames[(int)CardCollectionCatalogue.RarityOf(card)] = art;
                SetField(config, "rarityFrame", frames);
                SetField(config, "packIcon", art);
                SetField(config, "cardArt", new[] { new CardCollectionConfig.CardArt { cardId = CardCollectionCatalogue.IdOf(card), face = art } });
                SetField(config, "setArt", new[] { new CardCollectionConfig.SetArt { setId = CardCollectionCatalogue.Sets[0].Id, banner = art } });

                var data = new SaveData();
                data.cardCollection.FindOrAdd(CardCollectionCatalogue.IdOf(card)).level = 1;
                var wallet = new WalletService(data.wallet);
                ServiceLocator.Register(wallet);
                ServiceLocator.Register(new CardCollectionService(data, null, new TimeService(), wallet, config));
                CardCollectionUI ui = Open();

                Transform tile = Find(ui.transform, "Kart_" + card);
                Assert.That(tile.Find("Cerceve").GetComponent<Image>().enabled, Is.True);
                Assert.That(tile.Find("Cerceve").GetComponent<Image>().sprite, Is.SameAs(art));
                Assert.That(tile.Find("Yuz").GetComponent<Image>().sprite, Is.SameAs(art));
                Assert.That(Find(ui.transform, "PaketSimge").GetComponent<Image>().enabled, Is.True);
                Assert.That(Find(ui.transform, "SetSekme0").GetComponent<Image>().sprite, Is.SameAs(art));

                int other = CardCollectionCatalogue.CardsInSet(0)[CardCollectionCatalogue.CardsInSetCount(0) - 1];
                Assert.That(CardCollectionCatalogue.RarityOf(other), Is.Not.EqualTo(CardCollectionCatalogue.RarityOf(card)),
                            "the premise: a card of another rarity");
                Assert.That(Find(ui.transform, "Kart_" + other).Find("Cerceve").GetComponent<Image>().enabled, Is.False,
                            "a rarity with no frame wired draws none");
                Assert.That(Find(ui.transform, "SetSekme1").GetComponent<Image>().sprite, Is.Not.SameAs(art));
            }
            finally
            {
                Object.DestroyImmediate(config);
                Object.DestroyImmediate(art.texture);
                Object.DestroyImmediate(art);
            }
        }

        [Test]
        public void WithNoConfigTheArtHooksStayOff()
        {
            Register(new SaveData());
            CardCollectionUI ui = Open();
            Assert.That(Find(ui.transform, "PaketSimge").GetComponent<Image>().enabled, Is.False);
            int card = CardCollectionCatalogue.CardsInSet(0)[0];
            Assert.That(Find(ui.transform, "Kart_" + card).Find("Cerceve").GetComponent<Image>().enabled, Is.False);
        }

        private static Sprite TestSprite()
        {
            var texture = new Texture2D(8, 8);
            return Sprite.Create(texture, new Rect(0f, 0f, 8f, 8f), new Vector2(0.5f, 0.5f));
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, value);
        }

        // ------------------------------------------------------ goal pack lines

        [Test]
        public void TheGoalRewardRevealStatesThePacksAClaimBanked()
        {
            var canvas = new GameObject("Canvas", typeof(RectTransform));
            canvas.transform.SetParent(_root.transform, false);
            RewardRevealUI reveal = RewardRevealUI.Create((RectTransform)canvas.transform, null, null);

            reveal.Present(new GoalService.ClaimReceipt(1, 60L, 1, 3));
            Text value = Find(reveal.transform, "Deger").GetComponent<Text>();
            Assert.That(value.text, Does.Contain(string.Format(Loc.T("koleksiyon.paket_x"), 3)));

            reveal.Present(new GoalService.ClaimReceipt(1, 35L, 0, 0));
            Assert.That(value.text, Does.Not.Contain(string.Format(Loc.T("koleksiyon.paket_x"), 0)),
                        "a claim with no packs must not mention them");
        }

        [Test]
        public void TheWeeklyTrackLineStatesItsPacks()
        {
            MethodInfo line = typeof(GoalsUI).GetMethod("RewardLine", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(line, Is.Not.Null);
            var withPacks = (string)line.Invoke(null, new object[] { 150L, 3, 3 });
            var without = (string)line.Invoke(null, new object[] { 35L, 0, 0 });
            Assert.That(withPacks, Does.Contain(string.Format(Loc.T("koleksiyon.paket_x"), 3)));
            Assert.That(without, Does.Not.Contain("×"));
        }

        // ------------------------------------------------------------- helpers

        private static void AssertPortraitCanvas(Transform root, string name)
        {
            CanvasScaler scaler = Find(root, name).GetComponent<CanvasScaler>();
            Assert.That(scaler, Is.Not.Null);
            Assert.That(scaler.referenceResolution, Is.EqualTo(new Vector2(1080f, 1920f)));
        }

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

        private static void AssertTilesDoNotOverlap(Transform root, int[] cards)
        {
            for (int i = 0; i < cards.Length; i++)
            {
                var a = (RectTransform)Find(root, "Kart_" + cards[i]);
                Assert.That(a.anchorMin.y, Is.InRange(0f, 1f), "Kart_" + cards[i]);
                for (int j = i + 1; j < cards.Length; j++)
                    Assert.That(Intersects(a, (RectTransform)Find(root, "Kart_" + cards[j])), Is.False,
                                "Kart_" + cards[i] + " overlaps Kart_" + cards[j]);
            }
        }

        private static bool Intersects(RectTransform a, RectTransform b)
        {
            const float slack = 0.0005f;
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

        private static int CountNamed(Transform root, string prefix)
        {
            int n = 0;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) if (all[i].name.StartsWith(prefix)) n++;
            return n;
        }

        private static int ActiveTiles(Transform root)
        {
            int n = 0;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i].name.StartsWith("Kart_") && all[i].gameObject.activeInHierarchy) n++;
            return n;
        }

        private static void InvokeAwake(MonoBehaviour component)
        {
            MethodInfo awake = component.GetType().GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(awake, Is.Not.Null);
            awake.Invoke(component, null);
        }
    }
}
