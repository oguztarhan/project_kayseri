using Game.Core;
using Game.Systems;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class EquipmentExpeditionLoopTests
    {
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void PreviewMatchesActualReplacementWithoutMutatingSaveOrLoadout(int slot)
        {
            var data = new SaveData();
            var sea = new ExpeditionService(null, data);
            var old = new SeaCombat.Item { Slot = slot, Grade = 2, Hull = 250, Shot = 40, Def = 8, Spd = 7 };
            sea.Equip(old);
            var next = new SeaCombat.Item { Slot = slot, Grade = 0, Hull = 80, Shot = 12, Def = 2, Spd = 3 };
            string saved = JsonUtility.ToJson(data);
            double before = sea.ShipPower();
            SeaCombat.Item[] borrowed = sea.Loadout();
            double preview = sea.ShipPowerWith(next);
            Assert.That(preview, Is.LessThan(before), "a downgrade must not be advertised as a gain");
            Assert.That(JsonUtility.ToJson(data), Is.EqualTo(saved));
            Assert.That(borrowed[slot].Hull, Is.EqualTo(old.Hull));
            Assert.That(sea.ShipPower(), Is.EqualTo(before));
            sea.Equip(next);
            Assert.That(sea.ShipPower(), Is.EqualTo(preview).Within(0.000001d));
        }

        [Test]
        public void ProducedGearPowersShipAndSeaPointsFundTheNextCraft()
        {
            var data = new SaveData();
            var tuning = Crafting.Tuning.Default;
            tuning.PointDropChance = 1d;
            tuning.PointsPerWin = (int)tuning.CraftCost;
            var craft = new CraftingService(data, null, null, tuning, random: new System.Random(7));
            var sea = new ExpeditionService(null, data) { Crafting = craft };
            craft.Expeditions = sea;
            craft.AddPoints(tuning.CraftCost);
            double empty = sea.ShipPower();
            Assert.That(craft.TryCraft(out SeaCombat.Item item), Is.True);
            double preview = sea.ShipPowerWith(item);
            Assert.That(craft.EquipPending(), Is.GreaterThanOrEqualTo(0L));
            Assert.That(sea.ShipPower(), Is.EqualTo(preview).And.GreaterThan(empty));
            Assert.That(craft.EquipPending(), Is.EqualTo(-1L), "the same output cannot be equipped twice");
            sea.SetSail("coal");
            sea.RegisterKill(0, 12);
            sea.RegisterWin(0, 0);
            sea.RecordTripCash(250d);
            Assert.That(sea.TripCraftPoints, Is.EqualTo(tuning.CraftCost));
            Assert.That(sea.TripSalvage, Is.EqualTo(12L));
            Assert.That(sea.TripWins, Is.EqualTo(1));
            Assert.That(sea.TripCash, Is.EqualTo(250d));
            sea.Ashore();
            Assert.That(craft.TryCraft(out _), Is.True, "the reward must buy a real second item");
            Assert.That(craft.Points, Is.Zero);
            Assert.That(sea.TripCraftPoints, Is.EqualTo(tuning.CraftCost), "receipt tracks earned, not remaining");
            double persistedPower = new ExpeditionService(null, data).ShipPower();
            Assert.That(persistedPower, Is.EqualTo(preview));
        }

        [Test]
        public void ReceiptSurvivesReturnDoesNotRepayAndResetsOnlyOnNewTrip()
        {
            var data = new SaveData();
            var sea = new ExpeditionService(null, data);
            sea.SetSail("coal");
            sea.RegisterKill(0, 10);
            sea.RegisterKill(0, 7);
            sea.RecordTripCash(100);
            sea.SetSail("iron");
            Assert.That(sea.TripSalvage, Is.EqualTo(17));
            sea.Ashore();
            sea.Ashore();
            sea.RecordTripCash(500);
            Assert.That(sea.RegisterKill(0, 100), Is.False);
            Assert.That(sea.HasTripRewards, Is.True);
            Assert.That(sea.TripSalvage, Is.EqualTo(17));
            Assert.That(data.salvage, Is.EqualTo(17));
            Assert.That(sea.TripCash, Is.EqualTo(100));
            sea.SetSail("coal");
            Assert.That(sea.HasTripRewards, Is.False);
            Assert.That(data.salvage, Is.EqualTo(17), "clearing a receipt cannot claw back rewards");
        }

        [Test]
        public void NoCraftPointDropIsReportedHonestly()
        {
            var data = new SaveData();
            var tuning = Crafting.Tuning.Default;
            tuning.PointDropChance = 0;
            var craft = new CraftingService(data, null, null, tuning);
            var sea = new ExpeditionService(null, data) { Crafting = craft };
            sea.SetSail("coal");
            sea.RegisterKill(0, 5);
            sea.RegisterWin(0, 0);
            Assert.That(sea.TripCraftPoints, Is.Zero);
            Assert.That(craft.Points, Is.Zero);
            Assert.That(sea.TripWins, Is.EqualTo(1));
        }
    }
}
