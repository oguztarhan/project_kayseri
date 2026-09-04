using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Core;
using Game.Systems;
using Game.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class LiveEventsIndustryPassUiSmokeTests
    {
        [Test]
        public void ActivePassAppearsOnHubAndItsCardOpensTheTrack()
        {
            ServiceLocator.Clear();
            var host = new GameObject("LiveEventsIndustryPassUiSmoke");
            try
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var data = new SaveData();
                var wallet = new WalletService(data.wallet);
                var goals = new GoalService(data, wallet);
                var events = new LiveEventService(data, new List<LiveEvents.Definition>
                {
                    new LiveEvents.Definition
                    {
                        Id = "industry_pass_2026_09",
                        Kind = SeasonalIndustryPass.Kind,
                        StartUnix = now - 60L,
                        EndUnix = now + 3600L,
                        ConfigVersion = 1,
                        Slots = SeasonalIndustryPass.Slots,
                    },
                });
                var pass = new SeasonalIndustryPassService(events, goals, wallet,
                    SeasonalIndustryPass.Tuning.Default, data: data);
                ServiceLocator.Register(events);
                ServiceLocator.Register(pass);

                LiveEventsUI hub = host.AddComponent<LiveEventsUI>();
                MethodInfo awake = typeof(LiveEventsUI).GetMethod("Awake",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(awake, Is.Not.Null);
                awake.Invoke(hub, null);
                hub.Show();

                SeasonalIndustryPassUI passScreen =
                    UnityEngine.Object.FindAnyObjectByType<SeasonalIndustryPassUI>(
                        FindObjectsInactive.Include);
                Assert.That(passScreen, Is.Not.Null, "The hub did not create or find the pass screen.");
                MethodInfo passAwake = typeof(SeasonalIndustryPassUI).GetMethod("Awake",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(passAwake, Is.Not.Null);
                passAwake.Invoke(passScreen, null);

                Canvas[] canvases = host.GetComponentsInChildren<Canvas>(true);
                Transform hubCanvas = null;
                for (int i = 0; i < canvases.Length; i++)
                    if (canvases[i].name == "EtkinlikKanvas")
                    {
                        hubCanvas = canvases[i].transform.Find("Karartma");
                        break;
                    }
                Assert.That(hubCanvas, Is.Not.Null, "Hub canvas was not built.");
                Transform card = hubCanvas.Find("Kart0");
                Assert.That(card, Is.Not.Null, "The active pass was not seated in the first hub card.");
                Assert.That(card.gameObject.activeSelf, Is.True);
                card.GetComponent<Button>().onClick.Invoke();

                Canvas passCanvas = passScreen.GetComponentInChildren<Canvas>(true);
                Assert.That(passCanvas, Is.Not.Null, "The pass screen did not build its canvas.");
                Assert.That(passCanvas.transform.Find("Karartma").gameObject.activeSelf, Is.True,
                    "Tapping the pass card did not show its track.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                ServiceLocator.Clear();
            }
        }
    }
}
