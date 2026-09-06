using NUnit.Framework;
using Game.Core;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>
    /// Putting out from the island's port and coming ashore. The ship is the player's own now — no
    /// berth, no boarding rules — so the guarantee the old boarding tests held one assert at a time
    /// is held by distance instead: a trip never touches the dock's voyages at all, and the last
    /// test still proves it field by field.
    /// </summary>
    public class ExpeditionServiceTests
    {
        private const string Coal = "coal";
        private const double NoCeiling = 1e12d;

        private sealed class Terms : IIslandSaleTerms
        {
            public double BarPriceRaw { get; set; }
            public double IncomeCapPerMinuteRaw { get; set; }
            public double UpgradeTreeCostRaw { get; set; }
        }

        private static VoyageService Dock(out SaveData data, out MarketService market)
        {
            data = new SaveData();
            var wallet = new WalletService(data.wallet);
            market = new MarketService(data, wallet, null);
            market.Register(Coal, new Terms { BarPriceRaw = 10d, IncomeCapPerMinuteRaw = NoCeiling });
            market.SetActiveIsland(Coal);
            market.Product(Coal).deliveredPerMin = 600d;
            var foremen = new ForemanService(data, wallet, Foremen.Tuning.Default);
            return new VoyageService(data, market, foremen, wallet, new TimeService(),
                                     Voyages.Tuning.Default);
        }

        private static void Sail(VoyageService dock, MarketService market)
        {
            if (dock.At(0) == null) dock.TryStart(Coal, 0);
            market.Deliver(Coal, MarketService.ProductFor(Coal), dock.At(0).holdSize * 2d);
            dock.Tick((float)Voyages.SecondsToFill(0, Voyages.Tuning.Default) + 1f);
        }

        // ---- the session ---------------------------------------------------------------------

        [Test]
        public void AshoreUntilSheSails()
        {
            var sea = new ExpeditionService(null, new TimeService());
            Assert.That(sea.Active, Is.False);
            Assert.That(sea.Progress, Is.Zero);
            Assert.That(sea.SecondsLeft, Is.Zero);
            Assert.That(sea.SailedUnix, Is.Zero);
            Assert.That(sea.IslandKey, Is.Empty);
        }

        [Test]
        public void SettingSailOpensTheTripFromThatPort()
        {
            var sea = new ExpeditionService(null, new TimeService());
            Assert.That(sea.SetSail(Coal), Is.True);
            Assert.That(sea.Active, Is.True);
            Assert.That(sea.IslandKey, Is.EqualTo(Coal));
            Assert.That(sea.SailedUnix, Is.GreaterThan(0L));
            Assert.That(sea.Finds, Is.Zero);

            sea.Ashore();
            Assert.That(sea.Active, Is.False);
            Assert.That(sea.IslandKey, Is.Empty);
        }

        [Test]
        public void AskingAgainMidTripChangesNothing()
        {
            // A double tap, or a second entry point racing the first: the answer is yes, and the
            // trip already underway — its seed, its port, its finds — is the one that continues.
            var sea = new ExpeditionService(null, new TimeService());
            sea.SetSail(Coal);
            long stamp = sea.SailedUnix;
            sea.CountFind();

            Assert.That(sea.SetSail("iron"), Is.True);
            Assert.That(sea.IslandKey, Is.EqualTo(Coal));
            Assert.That(sea.SailedUnix, Is.EqualTo(stamp));
            Assert.That(sea.Finds, Is.EqualTo(1));
        }

        [Test]
        public void ComingAshoreTwiceIsHarmless()
        {
            var sea = new ExpeditionService(null, new TimeService());
            Assert.DoesNotThrow(() => { sea.Ashore(); sea.Ashore(); });
        }

        [Test]
        public void EveryTripDealsItsOwnDeck()
        {
            var sea = new ExpeditionService(null, new TimeService());
            sea.CountFind();
            Assert.That(sea.Finds, Is.Zero, "no finds ashore — there is no trip to count them into");

            sea.SetSail(Coal);
            sea.CountFind();
            sea.CountFind();
            Assert.That(sea.Finds, Is.EqualTo(2));

            sea.Ashore();
            sea.SetSail(Coal);
            Assert.That(sea.Finds, Is.Zero, "a new trip starts its seed index over");
        }

        [Test]
        public void ANullDockIsSurvivable()
        {
            // Combat is a port activity now: with no dock wired at all she still sails, and the
            // fights are simply priced for the first route.
            var sea = new ExpeditionService(null, null);
            Assert.That(sea.Tier, Is.Zero);
            Assert.That(sea.SetSail(Coal), Is.True);
            Assert.That(sea.Active, Is.True);
            Assert.That(sea.Progress, Is.GreaterThanOrEqualTo(0d).And.LessThan(1d));
            Assert.DoesNotThrow(() => sea.Ashore());
        }

        // ---- the patrol ----------------------------------------------------------------------

        [Test]
        public void ANewlySailedShipIsAtTheHomePortAndOutbound()
        {
            var sea = new ExpeditionService(null, new TimeService());
            sea.SetSail(Coal);

            Assert.That(sea.Progress, Is.LessThan(0.05d));
            Assert.That(sea.LanePosition, Is.LessThan(0.1d));
            Assert.That(sea.Outbound, Is.True);
            Assert.That(sea.SecondsLeft, Is.GreaterThan(0d));
        }

        [Test]
        public void ThePatrolStaysOnTheLane()
        {
            var sea = new ExpeditionService(null, new TimeService());
            sea.SetSail(Coal);
            for (int i = 0; i < 200; i++)
            {
                Assert.That(sea.Progress, Is.InRange(0d, 1d));
                Assert.That(sea.LanePosition, Is.InRange(0d, 1d));
            }
        }

        // ---- the dock, at arm's length -------------------------------------------------------

        [Test]
        public void TheFightsArePricedForTheFurthestOpenRoute()
        {
            SaveData data; MarketService market;
            VoyageService dock = Dock(out data, out market);
            var sea = new ExpeditionService(dock, new TimeService());

            Assert.That(sea.Tier, Is.Zero, "a fresh account fights in the first waters");

            data.voyagesCompleted = 999;   // every route long since opened
            Assert.That(dock.MaxTier(), Is.GreaterThan(0), "the premise: the ladder actually opened");
            Assert.That(sea.Tier, Is.EqualTo(dock.MaxTier()),
                        "combat climbs the same ladder the voyages climb");
        }

        [Test]
        public void SailingNeverTouchesTheDock()
        {
            // The point of moving the entry to the port, asserted rather than trusted: a whole
            // trip — out, read everything, ashore — leaves a running voyage exactly as it was.
            SaveData data; MarketService market;
            VoyageService dock = Dock(out data, out market);
            var sea = new ExpeditionService(dock, new TimeService());
            Sail(dock, market);

            VoyageState v = dock.At(0);
            long sailed = v.sailedUnix, returns = v.returnsUnix;
            double held = v.held, hold = v.holdSize;
            int tier = v.tier, foreman = v.foreman, captain = v.captain;
            bool settled = v.settled;

            sea.SetSail(Coal);
            for (int i = 0; i < 200; i++)
            {
                double _ = sea.Progress + sea.LanePosition + sea.SecondsLeft + sea.Tier;
                bool __ = sea.Outbound;
            }
            sea.Ashore();

            Assert.That(v.sailedUnix, Is.EqualTo(sailed));
            Assert.That(v.returnsUnix, Is.EqualTo(returns), "sailing must not shorten the crossing");
            Assert.That(v.held, Is.EqualTo(held));
            Assert.That(v.holdSize, Is.EqualTo(hold));
            Assert.That(v.tier, Is.EqualTo(tier));
            Assert.That(v.foreman, Is.EqualTo(foreman));
            Assert.That(v.captain, Is.EqualTo(captain));
            Assert.That(v.settled, Is.EqualTo(settled));
        }

        // ---- the route strip: the fleet opens the ladder, the player picks a rung ---------------

        private static SeaCombat.Tuning T => SeaCombat.Tuning.Default;

        [Test]
        public void AnUnpickedRouteKeepsFollowingTheFleet()
        {
            SaveData data; MarketService market;
            VoyageService dock = Dock(out data, out market);
            var sea = new ExpeditionService(dock, new TimeService(), data, null, T);

            Assert.That(data.seaTier, Is.EqualTo(-1), "the premise: nothing has been picked");
            Assert.That(sea.Tier, Is.Zero);

            data.voyagesCompleted = 999;
            Assert.That(sea.Tier, Is.EqualTo(dock.MaxTier()),
                        "an untouched strip climbs with the dock, exactly as it did before it existed");
        }

        [Test]
        public void APickedRouteStandsEvenWhenTheFleetOutgrowsIt()
        {
            SaveData data; MarketService market;
            VoyageService dock = Dock(out data, out market);
            data.voyagesCompleted = 999;
            var sea = new ExpeditionService(dock, new TimeService(), data, null, T);
            Assert.That(sea.MaxTier, Is.GreaterThan(0), "the premise: the ladder actually opened");

            Assert.That(sea.TrySetTier(0), Is.True);
            Assert.That(sea.Tier, Is.Zero,
                        "hunting shallower than the fleet can sail is the whole point of the choice");
            Assert.That(sea.MaxTier, Is.EqualTo(dock.MaxTier()), "and the ceiling did not move");
            Assert.That(data.seaTier, Is.Zero, "the pick is persisted, not session-shaped");
        }

        [Test]
        public void ALockedRouteCannotBeEnteredFromThePanel()
        {
            SaveData data; MarketService market;
            VoyageService dock = Dock(out data, out market);
            var sea = new ExpeditionService(dock, new TimeService(), data, null, T);

            for (int tier = 1; tier < Voyages.TierCount; tier++)
            {
                Assert.That(sea.TierUnlocked(tier), Is.False, "the premise: tier " + tier + " is shut");
                Assert.That(sea.TrySetTier(tier), Is.False, "tier " + tier);
                Assert.That(sea.VoyagesToUnlock(tier), Is.GreaterThan(0),
                            "a locked pill has to be able to say what still opens it");
            }
            Assert.That(sea.TrySetTier(-1), Is.False);
            Assert.That(sea.TrySetTier(Voyages.TierCount), Is.False);
            Assert.That(sea.Tier, Is.Zero, "and not one refusal moved the waters");
        }

        [Test]
        public void APickAboveTheLadderFallsBackToTheFurthestOpenRoute()
        {
            // A fleet reset out from under a pick that outlived the voyages which earned it. The
            // fights must never be priced for water nobody has opened.
            SaveData data; MarketService market;
            VoyageService dock = Dock(out data, out market);
            data.voyagesCompleted = 999;
            var sea = new ExpeditionService(dock, new TimeService(), data, null, T);
            Assert.That(sea.TrySetTier(Voyages.TierCount - 1), Is.True);

            data.voyagesCompleted = 0;
            Assert.That(sea.Tier, Is.Zero);
        }

        [Test]
        public void ASaveFromALongerLadderIsBroughtBackIntoRange()
        {
            SaveData data; MarketService market;
            VoyageService dock = Dock(out data, out market);
            data.seaTier = 99;
            var sea = new ExpeditionService(dock, new TimeService(), data, null, T);

            Assert.That(data.seaTier, Is.EqualTo(Voyages.TierCount - 1), "never an index past the table");
            Assert.That(sea.Tier, Is.Zero, "and still gated by what the fleet opened");
        }

        // ---- energy: the governor, and the two ways it moves ------------------------------------

        /// <summary>
        /// Rewinding the STAMP is how these move time. The pool is read as (value, stamp), so a
        /// stamp pushed back N seconds is indistinguishable from N seconds having passed — and it
        /// works against the real <see cref="TimeService"/>, which has no seam to fake.
        /// </summary>
        private static void Elapse(SaveData data, double seconds)
            => data.seaEnergyStampUnix -= (long)seconds;

        [Test]
        public void ASpendCannotBeUndoneByTheRefillItBanked()
        {
            // The pool is stored as (value, stamp) and READ as value plus whole periods since the
            // stamp. A spend that writes the value without settling the stamp hands those periods
            // straight back: the pool never goes down and one energy buys unlimited searches.
            var data = new SaveData();
            var sea = new ExpeditionService(null, new TimeService(), data, null, T);
            data.seaEnergy = 5;
            Elapse(data, T.EnergyRegenSeconds + 5d);   // one point owed, and five seconds into the next
            Assert.That(sea.Energy, Is.EqualTo(6), "the premise: a point is owed but not yet written");

            Assert.That(sea.TrySpendEnergy(), Is.True);
            Assert.That(sea.Energy, Is.EqualTo(5), "the search was actually paid for");
            Assert.That(sea.TrySpendEnergy(), Is.True);
            Assert.That(sea.Energy, Is.EqualTo(4), "and again — the pool goes DOWN");
        }

        [Test]
        public void ASpendKeepsThePointAlreadyInProgress()
        {
            var data = new SaveData();
            var sea = new ExpeditionService(null, new TimeService(), data, null, T);
            data.seaEnergy = 5;
            Elapse(data, T.EnergyRegenSeconds * 0.5d);
            double before = sea.SecondsToNextEnergy;

            Assert.That(sea.TrySpendEnergy(), Is.True);
            Assert.That(sea.SecondsToNextEnergy, Is.EqualTo(before).Within(2d),
                        "paying for a search must not also cost the half point already earned");
        }

        [Test]
        public void AClockWoundBackwardsIsNeverPaidTwice()
        {
            var data = new SaveData();
            var sea = new ExpeditionService(null, new TimeService(), data, null, T);
            data.seaEnergy = 5;
            Elapse(data, -3600d);   // a stamp an hour in the FUTURE: the device clock was wound back
            long stamp = data.seaEnergyStampUnix;

            Assert.That(sea.Energy, Is.EqualTo(5), "no points accrue on the way back");
            Assert.That(sea.TrySpendEnergy(), Is.True);
            Assert.That(sea.Energy, Is.EqualTo(4), "the spend still lands");
            Assert.That(data.seaEnergyStampUnix, Is.EqualTo(stamp),
                        "the stamp stands, so the hour forward cannot be charged as refill as well");
        }

        [Test]
        public void AGrantFillsThePoolAndNeverOverflowsIt()
        {
            var data = new SaveData();
            var sea = new ExpeditionService(null, new TimeService(), data, null, T);
            for (int i = 0; i < 4; i++) sea.TrySpendEnergy();
            Assert.That(sea.Energy, Is.EqualTo(T.EnergyMax - 4), "the premise: four searches paid for");

            Assert.That(sea.GrantEnergy(3), Is.EqualTo(3), "what landed is what was asked for");
            Assert.That(sea.Energy, Is.EqualTo(T.EnergyMax - 1));

            Assert.That(sea.GrantEnergy(999), Is.EqualTo(1), "a grant reports only what fitted");
            Assert.That(sea.Energy, Is.EqualTo(T.EnergyMax));
        }

        [Test]
        public void AGrantIntoAFullPoolLandsNothingSoNoChargeIsBurned()
        {
            var data = new SaveData();
            var sea = new ExpeditionService(null, new TimeService(), data, null, T);
            Assert.That(sea.Energy, Is.EqualTo(T.EnergyMax), "the premise: full");

            Assert.That(sea.GrantEnergy(10), Is.Zero,
                        "the caller has to be able to keep the player's daily charge");
            Assert.That(sea.GrantEnergy(0), Is.Zero);
            Assert.That(sea.GrantEnergy(-5), Is.Zero);
            Assert.That(sea.Energy, Is.EqualTo(T.EnergyMax));
        }

        [Test]
        public void AGrantIsSettledSoTheRefillUnderItIsNotCountedTwice()
        {
            var data = new SaveData();
            var sea = new ExpeditionService(null, new TimeService(), data, null, T);
            data.seaEnergy = 2;
            Elapse(data, T.EnergyRegenSeconds * 2d + 5d);   // two points owed
            Assert.That(sea.Energy, Is.EqualTo(4), "the premise");

            Assert.That(sea.GrantEnergy(3), Is.EqualTo(3));
            Assert.That(sea.Energy, Is.EqualTo(7), "banked two and granted three — not two more on top");
        }

        [Test]
        public void AnUnwiredServiceGrantsNothingAndPicksNoRoute()
        {
            var sea = new ExpeditionService(null, null);
            Assert.That(sea.GrantEnergy(10), Is.Zero);
            Assert.That(sea.TrySetTier(0), Is.False, "with no save there is nowhere to write a pick");
            Assert.That(sea.Tier, Is.Zero);
            Assert.That(sea.MaxTier, Is.Zero);
            Assert.That(sea.TierUnlocked(0), Is.True, "the first waters are always open");
            Assert.That(sea.TierUnlocked(1), Is.False);
        }
        // ---- the depo ----------------------------------------------------------------------------

        private static SeaCombat.Item Gear(int slot, int grade)
            => SeaCombat.ItemFor(slot, 0, grade, 0.5d, T);

        /// <summary>A shelf with this many Rare cannons on it, and the service that owns them.</summary>
        private static ExpeditionService Shelf(out SaveData data, int items, int capacity = 0)
        {
            data = new SaveData();
            SeaCombat.Tuning tuning = T;
            if (capacity > 0) tuning.StashCapacity = capacity;
            var sea = new ExpeditionService(null, new TimeService(), data, null, tuning);
            for (int i = 0; i < items; i++)
                Assert.That(sea.Stow(Gear(SeaCombat.SlotCannon, 1)), Is.True, "premise: stow " + i);
            return sea;
        }

        [Test]
        public void AFreshSaveHasAnEmptyShelfAndTheTunedCapacity()
        {
            var data = new SaveData();
            var sea = new ExpeditionService(null, new TimeService(), data, null, T);

            Assert.That(sea.StashCount, Is.Zero);
            Assert.That(sea.StashCapacity, Is.EqualTo(T.StashCapacity));
            Assert.That(sea.StashHasRoom, Is.True);
            Assert.That(sea.StashItemAt(0).Grade, Is.EqualTo(-1));
            Assert.That(sea.StashIdAt(0), Is.EqualTo(GearStash.NoId));
            Assert.That(sea.ScrapAllValue(out long xp), Is.Zero);
            Assert.That(xp, Is.Zero);
        }

        [Test]
        public void StowingKeepsTheWholeStatBlockAndGivesItAnId()
        {
            var data = new SaveData();
            var sea = new ExpeditionService(null, new TimeService(), data, null, T);
            SeaCombat.Item item = Gear(SeaCombat.SlotSpyglass, 3);

            Assert.That(sea.Stow(item), Is.True);
            Assert.That(sea.StashCount, Is.EqualTo(1));
            Assert.That(sea.StashIdAt(0), Is.GreaterThan(GearStash.NoId));

            SeaCombat.Item back = sea.StashItemAt(0);
            Assert.That(back.Slot, Is.EqualTo(item.Slot));
            Assert.That(back.Grade, Is.EqualTo(item.Grade));
            Assert.That(back.Sec, Is.EqualTo(item.Sec));
            Assert.That(back.Hull, Is.EqualTo(item.Hull));
            Assert.That(back.Shot, Is.EqualTo(item.Shot));
            Assert.That(back.Def, Is.EqualTo(item.Def));
            Assert.That(back.Spd, Is.EqualTo(item.Spd));
            Assert.That(back.SecAmt, Is.EqualTo(item.SecAmt));
        }

        [Test]
        public void AFullShelfRefusesEveryWayIn()
        {
            // "Full inventory" from three directions at once: a plain stow, a worn item coming
            // off, and the room flag the screens read before they offer the button.
            ExpeditionService sea = Shelf(out SaveData data, 3, capacity: 3);
            Assert.That(sea.StashHasRoom, Is.False);

            Assert.That(sea.Stow(Gear(SeaCombat.SlotCharm, 0)), Is.False);
            Assert.That(sea.StashCount, Is.EqualTo(3), "a refused stow must not grow the shelf");

            sea.Equip(Gear(SeaCombat.SlotPlating, 2));
            Assert.That(sea.StowWorn(SeaCombat.SlotPlating), Is.False);
            Assert.That(sea.GearGrade(SeaCombat.SlotPlating), Is.EqualTo(2),
                        "a refused unequip leaves the item worn");
        }

        [Test]
        public void ADepoTunedToNothingHoldsNothing()
        {
            var data = new SaveData();
            SeaCombat.Tuning tuning = T;
            tuning.StashCapacity = 0;
            var sea = new ExpeditionService(null, new TimeService(), data, null, tuning);

            Assert.That(sea.StashCapacity, Is.Zero);
            Assert.That(sea.StashHasRoom, Is.False);
            Assert.That(sea.Stow(Gear(SeaCombat.SlotCannon, 0)), Is.False);
        }

        [Test]
        public void AnItemForNoSlotOrNoGradeIsNeverShelved()
        {
            var data = new SaveData();
            var sea = new ExpeditionService(null, new TimeService(), data, null, T);

            Assert.That(sea.Stow(new SeaCombat.Item { Slot = 0, Grade = -1 }), Is.False, "no item");
            Assert.That(sea.Stow(new SeaCombat.Item { Slot = -1, Grade = 0 }), Is.False, "no slot");
            Assert.That(sea.Stow(new SeaCombat.Item { Slot = SeaCombat.SlotCount, Grade = 0 }), Is.False);
            Assert.That(sea.Stow(new SeaCombat.Item { Slot = 0, Grade = Captains.GradeCount }), Is.False,
                        "a grade off the end of the ladder has no hurda value to pay later");
            Assert.That(sea.StashCount, Is.Zero);
        }

        [Test]
        public void TakingAWornItemOffPaysNothingAndDestroysNothing()
        {
            var data = new SaveData();
            var sea = new ExpeditionService(null, new TimeService(), data, null, T);
            sea.Equip(Gear(SeaCombat.SlotCannon, 3));
            long salvage = data.salvage;

            Assert.That(sea.StowWorn(SeaCombat.SlotCannon), Is.True);
            Assert.That(data.salvage, Is.EqualTo(salvage), "parking an item is not refusing it");
            Assert.That(data.craftXp, Is.Zero, "and it teaches the bench nothing");
            Assert.That(sea.GearGrade(SeaCombat.SlotCannon), Is.EqualTo(-1), "the slot is empty");
            Assert.That(sea.GearScore(SeaCombat.SlotCannon), Is.Zero);
            Assert.That(sea.StashCount, Is.EqualTo(1));
            Assert.That(sea.StashItemAt(0).Grade, Is.EqualTo(3), "and the item is on the shelf");
        }

        [Test]
        public void StowingAnEmptySlotIsRefused()
        {
            var data = new SaveData();
            var sea = new ExpeditionService(null, new TimeService(), data, null, T);
            Assert.That(sea.StowWorn(SeaCombat.SlotCharm), Is.False);
            Assert.That(sea.StowWorn(-1), Is.False);
            Assert.That(sea.StowWorn(SeaCombat.SlotCount), Is.False);
            Assert.That(sea.StashCount, Is.Zero);
        }

        [Test]
        public void EquippingFromTheShelfIsASwapAndNotAPurchase()
        {
            var data = new SaveData();
            var sea = new ExpeditionService(null, new TimeService(), data, null, T);
            sea.Equip(Gear(SeaCombat.SlotCannon, 1));
            long salvage = data.salvage;
            Assert.That(sea.Stow(Gear(SeaCombat.SlotCannon, 3)), Is.True);
            long id = sea.StashIdAt(0);

            Assert.That(sea.EquipFromStash(id), Is.True);
            Assert.That(sea.GearGrade(SeaCombat.SlotCannon), Is.EqualTo(3), "the better one is worn");
            Assert.That(sea.StashCount, Is.EqualTo(1), "and the old one took its place");
            Assert.That(sea.StashItemAt(0).Grade, Is.EqualTo(1));
            Assert.That(data.salvage, Is.EqualTo(salvage), "a swap pays no hurda");
            Assert.That(data.craftXp, Is.Zero, "and teaches nothing — nothing was scrapped");
        }

        [Test]
        public void EquippingIntoAnEmptySlotClearsTheShelfCell()
        {
            var data = new SaveData();
            var sea = new ExpeditionService(null, new TimeService(), data, null, T);
            sea.Stow(Gear(SeaCombat.SlotPlating, 2));

            Assert.That(sea.EquipFromStash(sea.StashIdAt(0)), Is.True);
            Assert.That(sea.GearGrade(SeaCombat.SlotPlating), Is.EqualTo(2));
            Assert.That(sea.StashCount, Is.Zero, "nothing was displaced, so nothing came back");
        }

        [Test]
        public void TheDisplacedItemTakesANewIdSoADoubleTapCannotSwapItBack()
        {
            var data = new SaveData();
            var sea = new ExpeditionService(null, new TimeService(), data, null, T);
            sea.Equip(Gear(SeaCombat.SlotCannon, 1));
            sea.Stow(Gear(SeaCombat.SlotCannon, 3));
            long id = sea.StashIdAt(0);

            Assert.That(sea.EquipFromStash(id), Is.True);
            Assert.That(sea.StashIdAt(0), Is.Not.EqualTo(id), "the id that was worn is gone for good");

            Assert.That(sea.EquipFromStash(id), Is.False, "the second press finds nothing");
            Assert.That(sea.GearGrade(SeaCombat.SlotCannon), Is.EqualTo(3), "and changes nothing");
            Assert.That(sea.StashItemAt(0).Grade, Is.EqualTo(1));
        }

        [Test]
        public void AnUnknownIdIsRefusedByEveryDepoAction()
        {
            ExpeditionService sea = Shelf(out SaveData data, 2);
            long salvage = data.salvage;

            Assert.That(sea.EquipFromStash(GearStash.NoId), Is.False);
            Assert.That(sea.EquipFromStash(-7L), Is.False);
            Assert.That(sea.EquipFromStash(9999L), Is.False);
            Assert.That(sea.ScrapFromStash(9999L, out long xp), Is.Zero);
            Assert.That(xp, Is.Zero);
            Assert.That(sea.ScrapFromStash(GearStash.NoId, out _), Is.Zero);

            Assert.That(sea.StashCount, Is.EqualTo(2), "the shelf is untouched");
            Assert.That(data.salvage, Is.EqualTo(salvage), "and nothing was paid");
        }

        [Test]
        public void ScrappingFromTheShelfPaysOnceAndOnlyOnce()
        {
            // Idempotency by id: the second press of a card that has gone must pay 0, not pay for
            // whatever slid into the row behind it.
            var data = new SaveData();
            var sea = new ExpeditionService(null, new TimeService(), data, null, T);
            sea.Stow(Gear(SeaCombat.SlotCannon, 4));
            sea.Stow(Gear(SeaCombat.SlotCharm, 0));
            long id = sea.StashIdAt(0);

            long paid = sea.ScrapFromStash(id, out long xp);
            Assert.That(paid, Is.EqualTo(SeaCombat.ScrapFor(4)));
            Assert.That(xp, Is.EqualTo(Crafting.SalvageXpFor(4)));
            Assert.That(data.salvage, Is.EqualTo(paid));
            Assert.That(sea.StashCount, Is.EqualTo(1));

            Assert.That(sea.ScrapFromStash(id, out long again), Is.Zero, "the same card, again");
            Assert.That(again, Is.Zero);
            Assert.That(data.salvage, Is.EqualTo(paid), "no second payout");
            Assert.That(sea.StashCount, Is.EqualTo(1), "and the Common behind it survives");
            Assert.That(sea.StashItemAt(0).Grade, Is.Zero);
        }

        [Test]
        public void EmptyingTheShelfPaysExactlyWhatTheButtonPrinted()
        {
            var data = new SaveData();
            var sea = new ExpeditionService(null, new TimeService(), data, null, T);
            sea.Stow(Gear(SeaCombat.SlotCannon, 0));
            sea.Stow(Gear(SeaCombat.SlotCharm, 4));
            sea.Stow(Gear(SeaCombat.SlotPlating, 2));

            long quoted = sea.ScrapAllValue(out long quotedXp);
            long paid = sea.ScrapAllStash(out long xp);

            Assert.That(paid, Is.EqualTo(quoted), "the label and the press must agree");
            Assert.That(xp, Is.EqualTo(quotedXp));
            Assert.That(paid, Is.EqualTo(SeaCombat.ScrapFor(0) + SeaCombat.ScrapFor(4)
                                       + SeaCombat.ScrapFor(2)));
            Assert.That(data.salvage, Is.EqualTo(paid));
            Assert.That(sea.StashCount, Is.Zero);

            Assert.That(sea.ScrapAllStash(out long none), Is.Zero, "an empty shelf pays nothing");
            Assert.That(none, Is.Zero);
            Assert.That(data.salvage, Is.EqualTo(paid));
        }

        [Test]
        public void EmptyingTheShelfNeverTouchesWhatIsWorn()
        {
            var data = new SaveData();
            var sea = new ExpeditionService(null, new TimeService(), data, null, T);
            sea.Equip(Gear(SeaCombat.SlotCannon, 4));
            sea.Stow(Gear(SeaCombat.SlotCannon, 0));

            sea.ScrapAllStash(out _);
            Assert.That(sea.GearGrade(SeaCombat.SlotCannon), Is.EqualTo(4));
            Assert.That(sea.StashCount, Is.Zero);
        }

        [Test]
        public void ADepoMoveReachesTheDiskBeforeItIsAcknowledged()
        {
            // Not "a save happened" — the stronger thing: ONE write holds every part of the move.
            // A file with the item gone and the hurda unpaid, or paid twice, is what this rules out.
            var save = new SaveService("depo-test.dat");
            var data = new SaveData();
            var sea = new ExpeditionService(null, new TimeService(), data, null, T, save);

            Assert.That(sea.Stow(Gear(SeaCombat.SlotCannon, 3)), Is.True);
            Assert.That(save.TryLoad(out SaveData onDisk), Is.True);
            Assert.That(onDisk.gearStash.Count, Is.EqualTo(1), "the stow is already on the disk");
            Assert.That(onDisk.gearStash[0].grade, Is.EqualTo(4), "stored as grade + 1");

            long paid = sea.ScrapFromStash(sea.StashIdAt(0), out _);
            Assert.That(paid, Is.GreaterThan(0L));
            Assert.That(save.TryLoad(out SaveData after), Is.True);
            Assert.That(after.gearStash, Is.Empty, "the item is gone from the file");
            Assert.That(after.salvage, Is.EqualTo(paid), "and its hurda is in the same write");
        }

        [Test]
        public void ABrokenShelfRowIsDroppedOnLoadRatherThanDrawnAsAPhantomCommon()
        {
            var data = new SaveData();
            data.gearStash.Add(new GearStashItem { id = 1L, slot = 0, grade = 2 });         // good
            data.gearStash.Add(new GearStashItem { id = 2L, slot = 0, grade = 0 });         // no grade
            data.gearStash.Add(new GearStashItem { id = 3L, slot = -1, grade = 2 });        // no slot
            data.gearStash.Add(new GearStashItem { id = 4L, slot = 99, grade = 2 });        // no slot
            data.gearStash.Add(new GearStashItem { id = 5L, slot = 0,
                                                   grade = Captains.GradeCount + 1 });      // off the ladder
            data.gearStash.Add(null);                                                       // no row at all

            var sea = new ExpeditionService(null, new TimeService(), data, null, T);
            Assert.That(sea.StashCount, Is.EqualTo(1));
            Assert.That(sea.StashIdAt(0), Is.EqualTo(1L));
            Assert.That(sea.StashItemAt(0).Grade, Is.EqualTo(1));
        }

        [Test]
        public void ARestoredShelfIsGivenTheIdsItIsMissing()
        {
            // A save hand-edited, half-written, or restored from before ids existed. Two cards a
            // tap cannot tell apart is the one state the depo must never be left in.
            var data = new SaveData();
            data.gearStash.Add(new GearStashItem { id = 0L, slot = 0, grade = 1 });
            data.gearStash.Add(new GearStashItem { id = 7L, slot = 1, grade = 1 });
            data.gearStash.Add(new GearStashItem { id = 7L, slot = 2, grade = 1 });
            data.gearStash.Add(new GearStashItem { id = -4L, slot = 3, grade = 1 });

            var sea = new ExpeditionService(null, new TimeService(), data, null, T);
            Assert.That(sea.StashCount, Is.EqualTo(4));

            for (int i = 0; i < 4; i++)
            {
                long id = sea.StashIdAt(i);
                Assert.That(id, Is.GreaterThan(GearStash.NoId), "row " + i);
                for (int j = 0; j < i; j++)
                    Assert.That(sea.StashIdAt(j), Is.Not.EqualTo(id), "rows " + j + " and " + i);
            }
            Assert.That(data.gearStashLastId, Is.GreaterThanOrEqualTo(7L),
                        "the sequence has to be past every id already in the file");

            // And a fresh stow cannot collide with what was re-stamped.
            sea.Stow(Gear(SeaCombat.SlotCannon, 0));
            long fresh = sea.StashIdAt(4);
            for (int i = 0; i < 4; i++) Assert.That(sea.StashIdAt(i), Is.Not.EqualTo(fresh));
        }

        [Test]
        public void AShelfFullerThanTheTuningIsKeptRatherThanTrimmed()
        {
            // Capacity is a number in the Inspector. Lowering it must never delete earned items.
            var data = new SaveData();
            for (int i = 0; i < 5; i++)
                data.gearStash.Add(new GearStashItem { id = i + 1, slot = 0, grade = 2 });

            SeaCombat.Tuning tuning = T;
            tuning.StashCapacity = 2;
            var sea = new ExpeditionService(null, new TimeService(), data, null, tuning);

            Assert.That(sea.StashCount, Is.EqualTo(5), "nothing was thrown away");
            Assert.That(sea.StashHasRoom, Is.False, "but nothing new gets in either");
            Assert.That(sea.Stow(Gear(SeaCombat.SlotCannon, 0)), Is.False);
            Assert.That(sea.ScrapFromStash(sea.StashIdAt(0), out _), Is.GreaterThan(0L),
                        "and it can still be drained back under the line");
        }

        [Test]
        public void AnUnwiredServiceHasNoShelfAtAll()
        {
            var sea = new ExpeditionService(null, null);
            Assert.That(sea.StashCount, Is.Zero);
            Assert.That(sea.StashHasRoom, Is.False, "with no save there is nowhere to keep anything");
            Assert.That(sea.Stow(Gear(SeaCombat.SlotCannon, 0)), Is.False);
            Assert.That(sea.StowWorn(0), Is.False);
            Assert.That(sea.EquipFromStash(1L), Is.False);
            Assert.That(sea.ScrapFromStash(1L, out _), Is.Zero);
            Assert.That(sea.ScrapAllStash(out _), Is.Zero);
            Assert.That(sea.ScrapAllValue(out _), Is.Zero);
        }

    }
}
