using Game.Core;
using Game.Gameplay;
using Game.Systems;
using Game.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    /// <summary>Smoke coverage for the Phase 5 pet-equip strip on SeaFightUI: does the same job
    /// PetRosterUiSmokeTests does for the Phase 4 collection sheet — drive the slots so a visible,
    /// but disconnected, strip does not count as working.</summary>
    public sealed class SeaFightPetSlotsUiSmokeTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            ServiceLocator.Clear();
            _root = new GameObject("SeaFightPetSlotsSmokeRoot");
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

        private static PetService RegisterPets(SaveData data)
        {
            var pets = new PetService(data, Pets.Tuning.Default, PetChest.Tuning.Default,
                PetService.DefaultSlotUnlockFightsWon, new System.Random(7));
            ServiceLocator.Register(pets);
            return pets;
        }

        private SeaFightUI Open()
        {
            var fightsGo = new GameObject("Fights");
            fightsGo.transform.SetParent(_root.transform, false);
            var fights = fightsGo.AddComponent<EncounterController>();

            var uiGo = new GameObject("SeaFightUI");
            uiGo.transform.SetParent(_root.transform, false);
            var ui = uiGo.AddComponent<SeaFightUI>();
            ui.Build(fights);
            return ui;
        }

        private static SaveData NewSeaSave(int seaFightsWon = 25)
        {
            return new SaveData { pets = new PetSaveData(), seaFightsWon = seaFightsWon };
        }

        [Test]
        public void ThreeSlotsBuildWithoutAPetService()
        {
            SeaFightUI ui = null;
            Assert.DoesNotThrow(() => ui = Open());
            for (int slot = 0; slot < Pets.SlotCount; slot++)
                Assert.That(Find(ui.transform, "EvcilYuva" + slot), Is.Not.Null);
        }

        [Test]
        public void ALockedSlotShowsItsFightsRemainingAndRefusesToEquipOnTap()
        {
            var data = NewSeaSave(seaFightsWon: 0);   // only slot 0 open
            int idx = Pets.CellIndex(1, RosterCardState.Rarity.Common, 1);
            data.pets.counts[idx] = 1;
            PetService pets = RegisterPets(data);
            SeaFightUI ui = Open();

            string badgeText = ReadLabelText(Find(Find(ui.transform, "EvcilYuva1"), "Rozet"));
            Assert.That(badgeText, Does.Contain(pets.FightsUntilSlot(1).ToString()));
            Assert.That(badgeText, Does.Contain("\n"), "locked slots name the lock and the wins on two lines");

            Click(ui.transform, "EvcilYuva1");
            Assert.That(pets.EquippedAt(1), Is.EqualTo(-1));
        }

        [Test]
        public void AFilledSlotShowsThatPetsOwnBonusAndClearsItWhenEmptied()
        {
            var data = NewSeaSave();
            data.pets.counts[Pets.CellIndex(5, RosterCardState.Rarity.Mythic, 1)] = 1;   // kaplumbağa -> Hull
            PetService pets = RegisterPets(data);
            SeaFightUI ui = Open();
            Transform slot0 = Find(ui.transform, "EvcilYuva0");

            Assert.That(ReadLabelText(Find(slot0, "Bonus")), Is.Empty);
            Assert.That(ReadLabelText(Find(slot0, "Rozet")), Is.EqualTo("+"));

            Click(ui.transform, "EvcilYuva0");
            Assert.That(pets.EquippedAt(0), Is.EqualTo(5));
            double hull = Pets.Bonus(Pets.EffectKind.Hull, RosterCardState.Rarity.Mythic, 1, Pets.Tuning.Default);
            Assert.That(ReadLabelText(Find(slot0, "Bonus")), Is.EqualTo("+" + hull.ToString("0.#")));
            Assert.That(ReadLabelText(Find(slot0, "Rozet")), Is.Empty);

            Click(ui.transform, "EvcilYuva0");
            Assert.That(pets.EquippedAt(0), Is.EqualTo(-1));
            Assert.That(ReadLabelText(Find(slot0, "Bonus")), Is.Empty);
        }

        [Test]
        public void TappingAnUnlockedEmptySlotEquipsTheFirstOwnedSpeciesThenCyclesThenClears()
        {
            var data = NewSeaSave();
            int idxA = Pets.CellIndex(0, RosterCardState.Rarity.Common, 1);
            int idxB = Pets.CellIndex(2, RosterCardState.Rarity.Common, 1);
            data.pets.counts[idxA] = 1;
            data.pets.counts[idxB] = 1;
            PetService pets = RegisterPets(data);
            SeaFightUI ui = Open();

            Click(ui.transform, "EvcilYuva0");
            Assert.That(pets.EquippedAt(0), Is.EqualTo(0));

            Click(ui.transform, "EvcilYuva0");
            Assert.That(pets.EquippedAt(0), Is.EqualTo(2));

            Click(ui.transform, "EvcilYuva0");
            Assert.That(pets.EquippedAt(0), Is.EqualTo(-1));
        }

        [Test]
        public void CyclingSkipsASpeciesAlreadyWornInAnotherSlot()
        {
            var data = NewSeaSave();
            int idxA = Pets.CellIndex(0, RosterCardState.Rarity.Common, 1);
            int idxB = Pets.CellIndex(1, RosterCardState.Rarity.Common, 1);
            data.pets.counts[idxA] = 1;
            data.pets.counts[idxB] = 1;
            PetService pets = RegisterPets(data);
            pets.Equip(1, 1);   // species 1 already rides slot 1
            SeaFightUI ui = Open();

            Click(ui.transform, "EvcilYuva0");
            Assert.That(pets.EquippedAt(0), Is.EqualTo(0));

            Click(ui.transform, "EvcilYuva0");   // species 1 is worn elsewhere — must be skipped
            Assert.That(pets.EquippedAt(0), Is.EqualTo(-1));
        }

        [Test]
        public void EquippingAPetRepaintsTheStatGridAtOnceNotOnTheNextPowerTick()
        {
            var data = NewSeaSave();
            data.pets.counts[Pets.CellIndex(5, RosterCardState.Rarity.Mythic, 1)] = 1;   // kaplumbağa -> Hull
            PetService pets = RegisterPets(data);
            var sea = new ExpeditionService(new TimeService(), data, null, SeaCombat.Tuning.Default);
            sea.Pets = pets;
            sea.SetSail("coal");
            ServiceLocator.Register(sea);
            SeaFightUI ui = Open();
            Transform hullValue = Find(ui.transform, "Deger0");

            Click(ui.transform, "EvcilYuva0");

            string expected = Mathf.RoundToInt((float)sea.ShipStats().Hull).ToString();
            Assert.That(ReadLabelText(hullValue), Is.EqualTo(expected),
                        "the grid must show the fight's hull the moment the pet is worn");
        }

        private static void Click(Transform root, string slotName)
        {
            Button button = Find(root, slotName).GetComponent<Button>();
            Assert.That(button, Is.Not.Null, slotName + " must be clickable");
            button.onClick.Invoke();
        }

        private static Transform Find(Transform root, string name)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) if (all[i].name == name) return all[i];
            Assert.Fail("Missing UI node: " + name);
            return null;
        }

        /// <summary>Reads a TMP_Text's "text" property via reflection rather than a direct TMPro
        /// reference — this test assembly (no asmdef of its own) does not carry that dependency
        /// the way Game.UI's does.</summary>
        private static string ReadLabelText(Transform node)
        {
            var behaviours = node.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var prop = behaviours[i].GetType().GetProperty("text");
                if (prop != null && prop.PropertyType == typeof(string))
                    return (string)prop.GetValue(behaviours[i]);
            }
            Assert.Fail("No text-bearing label under " + node.name);
            return null;
        }
    }
}
