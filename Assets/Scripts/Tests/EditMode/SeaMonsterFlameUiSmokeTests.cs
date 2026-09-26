using System.Reflection;
using Game.Core;
using Game.Gameplay;
using Game.Systems;
using Game.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    /// <summary>
    /// The sea monster breathes fire instead of firing a ball. Drives the real SeaFightUI's breath
    /// frame by frame: it must land with the damage, pour a tail, settle to nothing, survive a salvo's
    /// second breath and leave the ship's paint as it found it.
    /// </summary>
    public sealed class SeaMonsterFlameUiSmokeTests
    {
        private const float StageHeight = 400f;
        private const float Flight = 0.45f;
        private const float Frame = 1f / 60f;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private GameObject _root;
        private SeaFightUI _ui;

        [SetUp]
        public void SetUp()
        {
            ServiceLocator.Clear();
            _root = new GameObject("SeaMonsterFlameSmokeRoot");
            ServiceLocator.Register(new LocalizationService());

            var fightsGo = new GameObject("Fights");
            fightsGo.transform.SetParent(_root.transform, false);
            var fights = fightsGo.AddComponent<EncounterController>();
            var uiGo = new GameObject("SeaFightUI");
            uiGo.transform.SetParent(_root.transform, false);
            _ui = uiGo.AddComponent<SeaFightUI>();
            _ui.Build(fights);
        }

        [TearDown]
        public void TearDown()
        {
            ServiceLocator.Clear();
            if (_root != null) Object.DestroyImmediate(_root);
            var eventSystem = Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (eventSystem != null) Object.DestroyImmediate(eventSystem.gameObject);
        }

        private void Breathe(Vector2 target)
            => typeof(SeaFightUI).GetMethod("Breathe", Private).Invoke(_ui, new object[] { target, Flight });

        private void Scorch(bool crit)
            => typeof(SeaFightUI).GetMethod("Scorch", Private).Invoke(_ui, new object[] { StageHeight, crit });

        private void Step(float seconds)
        {
            MethodInfo drive = typeof(SeaFightUI).GetMethod("DriveFlame", Private);
            for (float t = 0f; t < seconds - 0.0001f; t += Frame)
                drive.Invoke(_ui, new object[] { Frame, StageHeight });
        }

        private T Field<T>(string name) => (T)typeof(SeaFightUI).GetField(name, Private).GetValue(_ui);

        private int Active(string pool)
        {
            int n = 0;
            foreach (RectTransform rt in Field<RectTransform[]>(pool))
                if (rt.gameObject.activeSelf) n++;
            return n;
        }

        [Test]
        public void OnlyTheSeaMonsterBreathesFire()
        {
            Assert.That(SeaFightUI.BreathesFire(SeaCombat.Beast), Is.True);
            Assert.That(SeaFightUI.BreathesFire(SeaCombat.Raider), Is.False);
            Assert.That(SeaFightUI.BreathesFire(SeaCombat.Derelict), Is.False);
            Assert.That(SeaFightUI.BreathesFire(SeaCombat.Fireship), Is.False);
            Assert.That(SeaFightUI.BreathesFire(SeaCombat.Ghost), Is.False);
        }

        [Test]
        public void ABreathPoursFlameAndFiresNoBall()
        {
            Breathe(new Vector2(100f, 120f));
            Step(0.2f);

            Assert.That(Active("_puff"), Is.GreaterThan(4));
            Assert.That(Field<RectTransform>("_flameGlow").gameObject.activeSelf, Is.True);
            Assert.That(Active("_ball"), Is.Zero);
        }

        [Test]
        public void TheFirstPuffLandsOnTheShipWhenTheDamageDoes()
        {
            var target = new Vector2(100f, 120f);
            Breathe(target);
            Step(Flight);

            RectTransform[] puffs = Field<RectTransform[]>("_puff");
            float[] ages = Field<float[]>("_puffT");
            int oldest = -1;
            for (int i = 0; i < puffs.Length; i++)
                if (ages[i] >= 0f && (oldest < 0 || ages[i] > ages[oldest])) oldest = i;

            Assert.That(oldest, Is.GreaterThanOrEqualTo(0));
            float miss = Vector2.Distance(puffs[oldest].anchoredPosition, target);
            Assert.That(miss, Is.LessThan(StageHeight * 0.08f), "only its spread may keep it off the hull");
        }

        [Test]
        public void TheBreathSettlesToNothing()
        {
            Breathe(new Vector2(100f, 120f));
            Scorch(false);
            Step(2f);

            Assert.That(Active("_puff"), Is.Zero);
            Assert.That(Active("_ember"), Is.Zero);
            Assert.That(Field<RectTransform>("_flameGlow").gameObject.activeSelf, Is.False);
            Assert.That(Field<Image>("_shipImage").color, Is.EqualTo(Color.white));
        }

        [Test]
        public void ASalvosSecondBreathStillPours()
        {
            Breathe(new Vector2(100f, 120f));
            Step(0.5f);
            int first = Active("_puff");
            Breathe(new Vector2(100f, 120f));
            Step(0.3f);

            Assert.That(Active("_puff"), Is.GreaterThan(first / 2));
            Assert.That(Field<RectTransform>("_flameGlow").gameObject.activeSelf, Is.True);
        }

        [Test]
        public void AHitScorchesTheShipAndThrowsEmbers_ACritThrowsMore()
        {
            Breathe(new Vector2(100f, 120f));
            Step(Flight);
            Scorch(false);
            int hit = Active("_ember");
            Assert.That(hit, Is.GreaterThan(0));
            Assert.That(Field<Image>("_shipImage").color, Is.EqualTo(Color.white), "tint lands next frame");
            Step(Frame);
            Assert.That(Field<Image>("_shipImage").color, Is.Not.EqualTo(Color.white));

            Step(2f);
            Scorch(true);
            Assert.That(Active("_ember"), Is.GreaterThan(hit));
        }
    }
}
