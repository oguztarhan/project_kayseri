using System.Linq;
using Game.Systems;
using Game.UI;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public class ShipyardFoundationTests
    {
        [Test] public void NewRunOnlyOpensCannon()
        {
            var p = new ShipyardProgression(); p.Normalize();
            Assert.That(p.unlockedStations, Is.EquivalentTo(new[] { "Station_Cannon" }));
            Assert.That(p.NextStation, Is.EqualTo("Station_Hull"));
        }

        [Test] public void CannotSkipStationsOrUnlockMissingArt()
        {
            var p = new ShipyardProgression();
            Assert.That(p.TryUnlockNext("Station_Rigging", true, true), Is.False);
            Assert.That(p.TryUnlockNext("Station_Hull", false, true), Is.False);
            Assert.That(p.TryUnlockNext("Station_Hull", true, false), Is.False);
            Assert.That(p.TryUnlockNext("Station_Hull", true, true), Is.True);
            Assert.That(p.TryUnlockNext("Station_Hull", true, true), Is.False);
        }

        [Test] public void FigureheadRemainsLockedAfterFirstFourStations()
        {
            var p = new ShipyardProgression();
            for (int i = 1; i < 4; i++) Assert.That(p.TryUnlockNext(ShipyardProgression.StationIds[i], true, true), Is.True);
            Assert.That(p.TryUnlockNext("Station_Figurehead", true, false), Is.False);
            Assert.That(p.NextStation, Is.EqualTo("Station_Figurehead"));
        }

        [Test] public void MigrationIsAdditiveAndIdempotent()
        {
            var save = JsonUtility.FromJson<SaveData>("{\"unlockedIslands\":[\"coal\",\"iron\"],\"tutorialStep\":100}");
            save.shipyard = save.shipyard ?? new ShipyardProgression();
            save.shipyard.unlockedStations.Add("future_station");
            save.shipyard.Normalize(); save.shipyard.Normalize();
            Assert.That(save.unlockedIslands, Is.EqualTo(new[] { "coal", "iron" }));
            Assert.That(save.tutorialStep, Is.EqualTo(100));
            Assert.That(save.shipyard.unlockedStations.Count(x => x == "Station_Cannon"), Is.EqualTo(1));
            Assert.That(save.shipyard.unlockedStations, Does.Contain("future_station"));
        }

        [Test] public void ShipyardProgressSurvivesEncryptedSaveRoundTrip()
        {
            var save = new SaveData(); save.shipyard.TryUnlockNext("Station_Hull", true, true);
            var service = new SaveService("shipyard-test-unused.dat");
            var restored = service.Decrypt(service.Encrypt(save), out bool tampered);
            Assert.That(tampered, Is.False);
            Assert.That(restored.shipyard.IsUnlocked("Station_Hull"), Is.True);
            Assert.That(restored.shipyard.NextStation, Is.EqualTo("Station_Rigging"));
        }

        [Test] public void ManifestHasCompleteUniqueContractAndIndependentBuildings()
        {
            var manifest = JsonUtility.FromJson<ShipyardMapManifest>(Resources.Load<TextAsset>("Shipyard/Map").text);
            Assert.That(manifest.anchors.Length, Is.EqualTo(45));
            Assert.That(manifest.anchors.Select(x => x.id).Distinct().Count(), Is.EqualTo(45));
            Assert.That(manifest.routes.Length, Is.EqualTo(17));
            Assert.That(manifest.zones.Length, Is.EqualTo(5));
            Assert.That(manifest.zones.Where(x => !x.needsArt).Select(x => x.artGroup).Distinct().Count(), Is.EqualTo(4));
            Assert.That(manifest.zones[4].needsArt, Is.True);
            foreach (var r in manifest.routes)
            {
                Assert.That(r.points.Length, Is.GreaterThanOrEqualTo(2));
                Assert.That(manifest.anchors.Any(a => a.id == r.from), Is.True, r.id);
                Assert.That(manifest.anchors.Any(a => a.id == r.to), Is.True, r.id);
            }
        }

        [Test] public void CameraPanCannotDriftHorizontallyOrExceedStops()
        {
            var go = new GameObject("test camera");
            try
            {
                var c = go.AddComponent<Camera>(); c.orthographicSize = 10;
                go.transform.rotation = Quaternion.LookRotation(new Vector3(0, -.72f, .69f));
                var p = go.AddComponent<PortraitShipyardCamera>();
                p.origin = new Vector3(0, 43, -38); p.minTravel = -12; p.maxTravel = 12;
                p.PanPixels(100000, 2340); Assert.That(p.travel, Is.EqualTo(-12));
                p.PanPixels(-100000, 2340); Assert.That(p.travel, Is.EqualTo(12));
                p.Focus(new Vector3(400, 5, 6), true);
                Assert.That(go.transform.position.x, Is.EqualTo(0).Within(.00001));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test] public void CompactHudRejectsLegacyExtraOpeners()
        {
            var go = new GameObject("compact hud");
            try
            {
                var hud = go.AddComponent<HudUI>();
                Assert.That(hud.AttachBottomButton(0, "unused", null, null), Is.Null);
                Assert.That(go.transform.childCount, Is.Zero);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
