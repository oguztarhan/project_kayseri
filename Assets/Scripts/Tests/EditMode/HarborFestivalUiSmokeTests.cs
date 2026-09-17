using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Core;
using Game.Systems;
using Game.UI;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class HarborFestivalUiSmokeTests
    {
        [Test]
        public void PremiumColumnIsHiddenWithoutAStoreId()
        {
            Transform tier = BuildFirstTier(HarborFestival.Tuning.Default);

            Assert.That(tier.Find("PremiumAl").gameObject.activeSelf, Is.False);
            Assert.That(tier.Find("PremiumOdul").gameObject.activeSelf, Is.False);

            var free = (RectTransform)tier.Find("UcretsizAl");
            Assert.That(free.gameObject.activeSelf, Is.True);
            Assert.That(free.anchorMax.x - free.anchorMin.x, Is.EqualTo(0.240f).Within(1e-4f),
                        "The free capsule must keep its size so its art does not stretch.");
            Assert.That((free.anchorMin.x + free.anchorMax.x) * 0.5f, Is.EqualTo(0.6975f).Within(1e-4f),
                        "The free capsule should sit centred under both reward columns.");
        }

        [Test]
        public void PremiumColumnIsShownOnceAStoreIdIsSet()
        {
            HarborFestival.Tuning tuning = HarborFestival.Tuning.Default;
            tuning.PremiumSku = "harbor_premium_test";
            Transform tier = BuildFirstTier(tuning);

            Assert.That(tier.Find("PremiumAl").gameObject.activeSelf, Is.True);
            Assert.That(tier.Find("PremiumOdul").gameObject.activeSelf, Is.True);
        }

        private GameObject _host;

        [TearDown]
        public void TearDown()
        {
            if (_host != null) UnityEngine.Object.DestroyImmediate(_host);
            _host = null;
            ServiceLocator.Clear();
        }

        /// <summary>Builds the screen against a running festival and returns its first tier row.</summary>
        private Transform BuildFirstTier(HarborFestival.Tuning tuning)
        {
            ServiceLocator.Clear();
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var goals = new GoalService(data, wallet);
            var events = new LiveEventService(data, new List<LiveEvents.Definition>
            {
                new LiveEvents.Definition
                {
                    Id = "harbor-ui-smoke",
                    Kind = HarborFestival.Kind,
                    StartUnix = now - 60L,
                    EndUnix = now + 3600L,
                    ConfigVersion = 1,
                    Slots = HarborFestival.Slots,
                },
            });
            ServiceLocator.Register(new HarborFestivalService(events, goals, wallet, tuning, data: data));

            _host = new GameObject("HarborFestivalUiSmoke");
            HarborFestivalUI screen = _host.AddComponent<HarborFestivalUI>();
            MethodInfo awake = typeof(HarborFestivalUI).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(awake, Is.Not.Null);
            awake.Invoke(screen, null);
            screen.Show();

            foreach (RectTransform rect in _host.GetComponentsInChildren<RectTransform>(true))
                if (rect.name == "Kademe0") return rect;
            Assert.Fail("The tiers list did not build a first row.");
            return null;
        }
    }
}
