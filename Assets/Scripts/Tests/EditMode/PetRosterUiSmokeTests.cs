using System.Reflection;
using Game.Core;
using Game.Systems;
using Game.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    /// <summary>Smoke coverage for the Phase 4 collection sheet.  These tests drive the buttons so
    /// a visible, but disconnected, pet UI does not count as working.</summary>
    public sealed class PetRosterUiSmokeTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            ServiceLocator.Clear();
            _root = new GameObject("PetRosterUiSmokeRoot");
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

        private static PetService Register(SaveData data)
        {
            var pets = new PetService(data, Pets.Tuning.Default, PetChest.Tuning.Default,
                PetService.DefaultSlotUnlockFightsWon, new System.Random(7));
            ServiceLocator.Register(pets);
            return pets;
        }

        private PetRosterUI Open()
        {
            var ui = _root.AddComponent<PetRosterUI>();
            InvokeAwake(ui);
            ui.Show();
            return ui;
        }

        [Test]
        public void TheScreenBuildsAllSixCardsWithoutAService()
        {
            PetRosterUI ui = null;
            Assert.DoesNotThrow(() => ui = Open());
            Assert.That(ui.Visible, Is.True);
            Assert.That(CountNamed(ui.transform, "Pet_"), Is.EqualTo(Pets.SpeciesCount));
            Assert.That(Find(ui.transform, "Sandik"), Is.Not.Null);
            Assert.That(Find(ui.transform, "Durum"), Is.Not.Null);
            Assert.That(Find(ui.transform, "Yuvalar"), Is.Not.Null);
            Assert.That(Find(ui.transform, "Fusyon"), Is.Not.Null);
            Assert.That(Find(ui.transform, PetRosterUI.OpenerButtonName), Is.Not.Null,
                        "a presentation with no HudUI still needs a live opener");
            Assert.That(ui.PendingCount(), Is.Zero);
        }

        [Test]
        public void EmptyRosterShowsPityAndLockedSlotInformation()
        {
            var data = NewPetSave();
            data.pets.bootstrapGranted = true;
            Register(data);
            PetRosterUI ui = Open();

            string pity = Find(ui.transform, "Merhamet").GetComponentInChildren<Text>(true).text;
            Assert.That(pity, Does.Contain(Loc.T("kaptan.derece.2")));
            Assert.That(pity, Does.Contain(Loc.T("kaptan.derece.3")));
            Assert.That(Find(ui.transform, "Bilgi").GetComponentInChildren<Text>(true).text,
                        Does.Contain(string.Format(Loc.T("dost.yuva_kilit"), 10)));
            Assert.That(Find(Find(ui.transform, "Pet_0"), "EnIyi").GetComponentInChildren<Text>(true).text,
                        Does.Contain(Loc.T("kaptan.bulunmadi")));
            Assert.That(Find(ui.transform, "AcBir").GetComponent<Button>().interactable, Is.False);
            Assert.That(Find(ui.transform, "AcToplu").GetComponent<Button>().interactable, Is.False);
        }

        [Test]
        public void FusionPreviewUsesTwoCopiesAndOneEssenceThenConsumesExactlyThoseInputs()
        {
            var data = NewPetSave();
            data.pets.bootstrapGranted = true;
            data.pets.petEssence = 1L;
            int source = Pets.CellIndex(0, RosterCardState.Rarity.Common, 1);
            data.pets.counts[source] = 2;
            PetService pets = Register(data);
            PetRosterUI ui = Open();

            Click(ui.transform, "Pet_0");
            Assert.That(ui.FusionAvailable, Is.True);
            Assert.That(ui.FusionPreviewText, Does.Contain("2×"));
            Assert.That(ui.FusionPreviewText, Does.Contain(Loc.T("dost.oz")));
            Assert.That(Find(ui.transform, "FusyonYap").GetComponent<Button>().interactable, Is.True);

            Click(ui.transform, "FusyonYap");
            Assert.That(Find(ui.transform, "FusyonOnay").gameObject.activeSelf, Is.True, "FÜZYON only asks");
            Assert.That(TextOf(ui.transform, "Girdiler"), Does.Contain("2×").And.Contain(Loc.T("dost.oz")));
            Assert.That(pets.CountAt(0, RosterCardState.Rarity.Common, 1), Is.EqualTo(2), "nothing moves before ONAYLA");
            Assert.That(pets.PetEssence, Is.EqualTo(1L));

            Click(ui.transform, "Onayla");
            Assert.That(Find(ui.transform, "FusyonOnay").gameObject.activeSelf, Is.False);
            Assert.That(pets.CountAt(0, RosterCardState.Rarity.Common, 1), Is.Zero);
            Assert.That(pets.CountAt(0, RosterCardState.Rarity.Common, 2), Is.EqualTo(1));
            Assert.That(pets.PetEssence, Is.Zero);
        }

        [Test]
        public void AChestButtonUsesTheServiceAndShowsTheLastPull()
        {
            var data = NewPetSave();
            data.pets.bootstrapGranted = true;
            PetService pets = Register(data);
            pets.GrantPearls(pets.ChestCost(1));
            PetRosterUI ui = Open();

            Click(ui.transform, "AcBir");
            Assert.That(pets.ChestsOpened, Is.EqualTo(1));
            Assert.That(Find(ui.transform, "SonCekilis").GetComponentInChildren<Text>(true).text,
                        Does.StartWith(string.Format(Loc.T("dost.son"), string.Empty)));
        }

        [Test]
        public void ASingleChestRevealsOneTileAndABulkRevealsTenWithThePostCommitFooter()
        {
            var data = NewPetSave();
            data.pets.bootstrapGranted = true;
            PetService pets = Register(data);
            int bulk = pets.ChestTuning.BulkCount;
            pets.GrantPearls(pets.ChestCost(1) + pets.ChestCost(bulk) + 7L);
            PetRosterUI ui = Open();

            Click(ui.transform, "AcBir");
            Assert.That(Find(ui.transform, "Acilis").gameObject.activeSelf, Is.True);
            Assert.That(ActiveTiles(ui.transform), Is.EqualTo(1));
            Assert.That(pets.ChestsOpened, Is.EqualTo(1), "paid and saved before the reveal plays");
            string footer = TextOf(ui.transform, "Ozet");
            Assert.That(footer, Does.Contain((pets.ChestCost(bulk) + 7L).ToString()), "remaining pearls, after the spend");
            Assert.That(footer, Does.Contain(Loc.T("kaptan.derece.2")).And.Contain(Loc.T("kaptan.derece.3")));

            Click(ui.transform, "Acilis");   // turn every tile
            Assert.That(TextOf(Find(ui.transform, "Kart_0"), "Ad"), Is.Not.Empty);
            Click(ui.transform, "Acilis");   // close
            Assert.That(Find(ui.transform, "Acilis").gameObject.activeSelf, Is.False);

            Click(ui.transform, "AcToplu");
            Assert.That(ActiveTiles(ui.transform), Is.EqualTo(bulk));
            Assert.That(pets.ChestsOpened, Is.EqualTo(1 + bulk));
            Assert.That(pets.Pearls, Is.EqualTo(7L));
        }

        [Test]
        public void AnEssencePullIsLabelledOnItsTile()
        {
            var data = NewPetSave();
            data.pets.bootstrapGranted = true;
            data.pets.counts[Pets.CellIndex(0, RosterCardState.Rarity.Mythic, Pets.MaxStars)] = 1;
            var pets = new PetService(data, Pets.Tuning.Default, PetChest.Tuning.Default,
                PetService.DefaultSlotUnlockFightsWon, new FixedRandom(0.999d, 0d));
            ServiceLocator.Register(pets);
            pets.GrantPearls(pets.ChestCost(1));
            PetRosterUI ui = Open();

            Click(ui.transform, "AcBir");
            Click(ui.transform, "Acilis");
            Assert.That(TextOf(Find(ui.transform, "Kart_0"), "Nadirlik"), Does.Contain(Loc.T("dost.oz")));
            Assert.That(pets.PetEssence, Is.EqualTo(1L));
        }

        [Test]
        public void ClosingAndReopeningMidRevealNeitherSpendsNorGrantsAgain()
        {
            var data = NewPetSave();
            PetService pets = Register(data);
            PetRosterUI ui = Open();               // first open pays the bootstrap
            long afterBootstrap = pets.Pearls;
            Assert.That(afterBootstrap, Is.EqualTo(Pets.RewardTuning.Default.BootstrapPearls));

            Click(ui.transform, "AcBir");
            long afterChest = pets.Pearls;
            ui.Hide();
            Assert.That(Find(ui.transform, "Acilis").gameObject.activeSelf, Is.False, "hiding dismisses the reveal");

            ui.Show();
            ui.Show();
            Assert.That(pets.Pearls, Is.EqualTo(afterChest));
            Assert.That(pets.ChestsOpened, Is.EqualTo(1));
            Assert.That(Find(ui.transform, "Acilis").gameObject.activeSelf, Is.False, "nothing replays the receipt");
        }

        [Test]
        public void CancellingTheFusionConfirmChangesNothing()
        {
            var data = NewPetSave();
            data.pets.bootstrapGranted = true;
            data.pets.counts[Pets.CellIndex(1, RosterCardState.Rarity.Rare, 5)] = 3;
            PetService pets = Register(data);
            PetRosterUI ui = Open();

            Click(ui.transform, "Pet_1");
            Click(ui.transform, "FusyonYap");
            Assert.That(TextOf(ui.transform, "Sonuc"), Does.Contain("★☆☆☆☆"), "Rare ★5 fuses into the next rarity at ★1");
            Click(ui.transform, "Vazgec");

            Assert.That(Find(ui.transform, "FusyonOnay").gameObject.activeSelf, Is.False);
            Assert.That(pets.CountAt(1, RosterCardState.Rarity.Rare, 5), Is.EqualTo(3));
            Assert.That(pets.CountAt(1, RosterCardState.Rarity.Epic, 1), Is.Zero);
        }

        [Test]
        public void WithoutEnoughMaterialsThePreviewNamesTheShortfall()
        {
            var data = NewPetSave();
            data.pets.bootstrapGranted = true;
            data.pets.counts[Pets.CellIndex(0, RosterCardState.Rarity.Common, 1)] = 1;
            Register(data);
            PetRosterUI ui = Open();

            Click(ui.transform, "Pet_0");
            Assert.That(ui.FusionAvailable, Is.False);
            Assert.That(ui.FusionPreviewText, Does.Contain("1/3"));
            Assert.That(ui.FusionPreviewText, Does.Contain(string.Format(Loc.T("dost.fusyon_eksik"), 2)));
            Assert.That(Find(ui.transform, "FusyonYap").GetComponent<Button>().interactable, Is.False);

            Click(ui.transform, "Pet_3");
            Assert.That(ui.FusionPreviewText, Does.Contain(Loc.T("kaptan.bulunmadi")));
        }

        [Test]
        public void ALanguageChangeRewritesTheLastPullAndTheConfirmCard()
        {
            var loc = ServiceLocator.Get<LocalizationService>();
            string original = loc.Code;
            try
            {
                loc.SetLanguage("en");
                var data = NewPetSave();
                data.pets.bootstrapGranted = true;
                PetService pets = Register(data);
                pets.GrantPearls(pets.ChestCost(1));
                PetRosterUI ui = Open();
                Click(ui.transform, "AcBir");
                Click(ui.transform, "Acilis");
                Click(ui.transform, "Acilis");
                Assert.That(TextOf(ui.transform, "SonCekilis"), Does.StartWith("Last: "));
                Assert.That(TextOf(ui.transform, "Onayla"), Is.EqualTo("CONFIRM"));

                loc.SetLanguage("de");
                Assert.That(TextOf(ui.transform, "SonCekilis"), Does.StartWith("Zuletzt: "));
                Assert.That(TextOf(ui.transform, "Onayla"), Is.EqualTo("BESTÄTIGEN"));
                Assert.That(TextOf(ui.transform, "Vazgec"), Is.EqualTo("ABBRECHEN"));
                Assert.That(TextOf(Find(ui.transform, "Pet_0"), "Ad"), Does.StartWith("Papagei"));
            }
            finally
            {
                loc.SetLanguage(original);
            }
        }

        [Test]
        public void TheRewardsButtonClaimsEachReadySourceOnce()
        {
            var data = NewPetSave();
            data.pets.bootstrapGranted = true;
            data.seaFightsWon = 10;
            data.pets.counts[Pets.CellIndex(0, RosterCardState.Rarity.Common, 1)] = 1;
            PetService pets = Register(data);
            PetRosterUI ui = Open();
            var r = Pets.RewardTuning.Default;
            Assert.That(pets.ClaimableRewardCount, Is.EqualTo(3), "daily, the 10-win milestone, the first-pet tier");
            Assert.That(ui.PendingCount(), Is.EqualTo(3));

            Click(ui.transform, "Oduller");
            long expected = r.DailyPearls + r.SeaFightMilestonePearls[0] + r.AchievementPearls[0];
            Assert.That(pets.Pearls, Is.EqualTo(expected));
            Assert.That(Find(ui.transform, "Oduller").GetComponent<Button>().interactable, Is.False);

            Click(ui.transform, "Oduller");
            Assert.That(pets.Pearls, Is.EqualTo(expected), "a second tap pays nothing");
        }

        [Test]
        public void AForceClosedSessionReloadsWithEverythingPaidAndNothingRepeated()
        {
            var save = new SaveService("pets-phase6-test.dat");
            var data = NewPetSave();
            var first = new PetService(data, Pets.Tuning.Default, PetChest.Tuning.Default,
                PetService.DefaultSlotUnlockFightsWon, new System.Random(3), null, save);
            ServiceLocator.Register(first);
            PetRosterUI firstUi = Open();          // bootstrap grant, saved
            Click(firstUi.transform, "AcBir");     // chest, saved — then the app "dies" mid-reveal

            Assert.That(save.TryLoad(out SaveData reloaded), Is.True);
            var second = new PetService(reloaded, Pets.Tuning.Default, PetChest.Tuning.Default,
                PetService.DefaultSlotUnlockFightsWon, new System.Random(4), null, save);
            ServiceLocator.Register(second);
            var go = new GameObject("Reloaded");
            go.transform.SetParent(_root.transform, false);
            var secondUi = go.AddComponent<PetRosterUI>();
            InvokeAwake(secondUi);
            secondUi.Show();

            Assert.That(second.Pearls, Is.EqualTo(first.Pearls), "no second bootstrap, no refund");
            Assert.That(second.ChestsOpened, Is.EqualTo(1));
            Assert.That(second.SinceEpic, Is.EqualTo(first.SinceEpic));
            Assert.That(second.SinceLegendary, Is.EqualTo(first.SinceLegendary));
            int copies = 0;
            foreach (int c in reloaded.pets.counts) copies += c;
            Assert.That(copies, Is.EqualTo(1), "the pull survived the reload exactly once");
            Assert.That(Find(secondUi.transform, "Acilis").gameObject.activeSelf, Is.False);
        }

        private sealed class FixedRandom : System.Random
        {
            private readonly double[] _values;
            private int _next;
            public FixedRandom(params double[] values) => _values = values;
            public override double NextDouble() => _values[_next < _values.Length ? _next++ : _values.Length - 1];
        }

        private static int ActiveTiles(Transform root)
        {
            int n = 0;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i].name.StartsWith("Kart_") && all[i].gameObject.activeSelf) n++;
            return n;
        }

        private static string TextOf(Transform root, string name)
            => Find(root, name).GetComponentInChildren<Text>(true).text;

        private static void Click(Transform root, string name)
        {
            Button button = Find(root, name).GetComponent<Button>();
            Assert.That(button, Is.Not.Null, name + " must be clickable");
            button.onClick.Invoke();
        }

        private static SaveData NewPetSave()
        {
            return new SaveData { pets = new PetSaveData() };
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
            int count = 0;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) if (all[i].name.StartsWith(prefix)) count++;
            return count;
        }

        private static void InvokeAwake(MonoBehaviour component)
        {
            MethodInfo awake = component.GetType().GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(awake, Is.Not.Null);
            awake.Invoke(component, null);
        }
    }
}
