using System.Reflection;
using Game.Core;
using Game.Systems;
using Game.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    public class MiningGearUiSmokeTests
    {
        private GameObject _host;

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
            ServiceLocator.Clear();
        }

        [Test]
        public void TargetedCraftUiBuildsFourSelectableSlotsAndASeparateButton()
        {
            var data = new SaveData { miningPoints = 20L, miningScrap = 20L };
            ServiceLocator.Register(new MiningGearService(data, null, new TimeService()));
            ServiceLocator.Register(new LocalizationService());
            _host = new GameObject("MiningGearUiSmoke");

            MiningGearUI ui = _host.AddComponent<MiningGearUI>();
            EnsureBuilt(ui);
            ui.Show();

            var target = (Button)typeof(MiningGearUI)
                .GetField("_targetBtn", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(ui);
            var cards = (RectTransform[])typeof(MiningGearUI)
                .GetField("_slotCard", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(ui);

            Assert.That(target, Is.Not.Null);
            Assert.That(target.interactable, Is.True);
            Assert.That(cards, Has.Length.EqualTo(MiningGear.SlotCount));
            for (int i = 0; i < cards.Length; i++)
                Assert.That(cards[i].GetComponent<Button>(), Is.Not.Null);
        }

        [Test]
        public void TargetedCraftUiDisablesTheActionWhenEitherBalanceIsInsufficient()
        {
            var data = new SaveData { miningPoints = 0L, miningScrap = 0L };
            ServiceLocator.Register(new MiningGearService(data, null, new TimeService()));
            ServiceLocator.Register(new LocalizationService());
            _host = new GameObject("MiningGearUiInsufficientSmoke");

            MiningGearUI ui = _host.AddComponent<MiningGearUI>();
            EnsureBuilt(ui);
            ui.Show();

            var target = (Button)typeof(MiningGearUI)
                .GetField("_targetBtn", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(ui);
            Assert.That(target.interactable, Is.False);
        }

        private static void EnsureBuilt(MiningGearUI ui)
        {
            var field = typeof(MiningGearUI).GetField("_targetBtn",
                                                       BindingFlags.Instance | BindingFlags.NonPublic);
            if (field.GetValue(ui) == null)
                typeof(MiningGearUI).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                                     .Invoke(ui, null);
        }
    }
}
