using System;
using NUnit.Framework;
using Game.Core;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>
    /// Where the collection touches the game. Five effects, five consumers, and the thing every test
    /// here is really guarding is ONCE — a bonus applied twice on one path and not at all on another
    /// is the failure mode nobody notices until the economy is already wrong.
    ///
    /// So every test runs the same consumer twice, with the collection and without, and asserts the
    /// exact ratio between the two runs. Nothing here hard-codes a balance number: the expectation is
    /// read back off <see cref="CardCollectionService.Effects"/>, so retuning the cards in
    /// <c>Docs/PLAN_14</c> cannot make these tests lie about whether the wiring is right.
    ///
    /// The scope boundaries in that document are tested as hard as the wiring is. A find bonus that
    /// leaked into the sea's scrap buttons would let a collection print salvage out of a stash it did
    /// not help fill, and there is a test below for each of the paths it must not reach.
    /// </summary>
    public class CardCollectionIntegrationTests
    {
        private const string Coal = "coal";
        private const double Price = 10d;
        private const double NoCeiling = 1e12d;

        private sealed class Terms : IIslandSaleTerms
        {
            public double BarPriceRaw { get; set; }
            public double IncomeCapPerMinuteRaw { get; set; }
            public double UpgradeTreeCostRaw { get; set; }
        }

        private static SeaCombat.Tuning T => SeaCombat.Tuning.Default;

        private static SeaCombat.Item Gear(int slot, int grade)
            => SeaCombat.ItemFor(slot, 0, grade, 0.5d, T);

        // ---- a collection worth something ---------------------------------------------------------

        /// <summary>
        /// Every card owned at max level, which is the largest number each effect can reach and so the
        /// one where a missed or doubled application is most visible. Written straight into the save
        /// rather than opened out of packs: what these tests are about is the consumers, and a
        /// thousand scripted pack rolls would only be a slower way to reach the same state.
        /// </summary>
        private static CardCollectionService Maxed(SaveData data, WalletService wallet)
        {
            for (int card = 0; card < CardCollectionCatalogue.Count; card++)
            {
                CardCollectionProgress row =
                    data.cardCollection.FindOrAdd(CardCollectionCatalogue.IdOf(card));
                row.level = CardCollection.MaxLevel;
            }
            return new CardCollectionService(data, null, new TimeService(), wallet);
        }

        [Test]
        public void TheMaxedCollectionUsedByEveryTestBelowIsActuallyWorthSomething()
        {
            // If this fails the rest of the file is asserting x1 == x1 and proving nothing.
            var data = new SaveData();
            CardCollectionEffects gain = Maxed(data, new WalletService(data.wallet)).Effects;

            Assert.That(gain.IncomeMultiplier, Is.GreaterThan(1d));
            Assert.That(gain.CraftXpMultiplier, Is.GreaterThan(1d));
            Assert.That(gain.SeaSalvageMultiplier, Is.GreaterThan(1d));
            Assert.That(gain.SeaChartMultiplier, Is.GreaterThan(1d));
            Assert.That(gain.CraftPointDrop, Is.GreaterThan(0d));
        }

        // ---- the yard: income ---------------------------------------------------------------------

        /// <summary>Runs one live island for a minute and reports what the wallet holds after.</summary>
        private static double RunYard(bool withCards, double capPerMinute, out double multiplier)
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            CardCollectionService cards = withCards ? Maxed(data, wallet) : null;
            multiplier = cards != null ? cards.Effects.IncomeMultiplier : 1d;

            var market = new MarketService(data, wallet, null, null, null, null, null, cards);
            market.Register(Coal, new Terms { BarPriceRaw = Price, IncomeCapPerMinuteRaw = capPerMinute });
            market.SetActiveIsland(Coal);

            IdleMarketYard row = market.Row(Coal);
            row.hireCarry = row.hireServe = row.dispatchLevel = MarketFlow.MaxHireLevel;

            for (int second = 0; second < 60; second++)
            {
                market.Deliver(Coal, MarketService.ProductFor(Coal), 2d);
                market.Tick(1f);
            }
            return wallet.Cash.ToDouble();
        }

        [Test]
        public void TheCollectionMultipliesWhatAYardSells()
        {
            double plain = RunYard(false, NoCeiling, out _);
            double lifted = RunYard(true, NoCeiling, out double mult);

            Assert.That(plain, Is.GreaterThan(0d), "the premise: the yard sold something at all");
            Assert.That(lifted, Is.EqualTo(plain * mult).Within(1e-6),
                        "exactly once — half this figure means it was missed, the square means twice");
        }

        [Test]
        public void TheCollectionLiftsTheIncomeCeilingAsWellAsThePayout()
        {
            // The whole reason the bonus enters the cap: a player who has collected enough cards to
            // matter is already earning at the ceiling, so a bonus on throughput alone would be worth
            // precisely nothing to exactly the people who earned it. Same argument as Foremen.
            const double Cap = 50d;
            double plain = RunYard(false, Cap, out _);
            double lifted = RunYard(true, Cap, out double mult);

            Assert.That(plain, Is.LessThan(RunYard(false, NoCeiling, out _)),
                        "the premise: at this ceiling the yard is being held back");
            Assert.That(lifted, Is.EqualTo(plain * mult).Within(1e-6),
                        "a capped yard earns the bonus too, or the bonus is worthless when it matters");
        }

        [Test]
        public void ThePriceBoardShowsWhatTheCollectionMadeABarWorth()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            CardCollectionService cards = Maxed(data, wallet);

            var plain = new MarketService(data, wallet, null);
            plain.Register(Coal, new Terms { BarPriceRaw = Price, IncomeCapPerMinuteRaw = NoCeiling });
            plain.Tick(1f);

            var lifted = new MarketService(data, wallet, null, null, null, null, null, cards);
            lifted.Register(Coal, new Terms { BarPriceRaw = Price, IncomeCapPerMinuteRaw = NoCeiling });
            lifted.Tick(1f);

            Assert.That(lifted.BarPrice(Coal),
                        Is.EqualTo(plain.BarPrice(Coal) * cards.Effects.IncomeMultiplier).Within(1e-9),
                        "the wall price has to agree with the till, or the board is lying");
        }

        // ---- the bench: craft XP ------------------------------------------------------------------

        private const int Grade = 4;   // the top of the salvage XP table, where rounding is visible

        private static long ExpectedXp(double multiplier)
            => (long)Math.Round(Crafting.SalvageXpFor(Grade) * multiplier, MidpointRounding.AwayFromZero);

        [Test]
        public void TheSeasScrapButtonsTeachTheCollectionsBonus()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            CardCollectionService cards = Maxed(data, wallet);

            var bench = new CraftingService(data, null, new TimeService());
            long plain = bench.GrantScrapXp(Grade);
            bench.Cards = cards;
            long lifted = bench.GrantScrapXp(Grade);

            Assert.That(plain, Is.EqualTo(Crafting.SalvageXpFor(Grade)), "unwired is the shipped table");
            Assert.That(lifted, Is.EqualTo(ExpectedXp(cards.Effects.CraftXpMultiplier)));
            Assert.That(lifted, Is.GreaterThan(plain), "a maxed collection has to be worth a whole XP");
            Assert.That(data.craftXp, Is.EqualTo(plain + lifted), "and both landed on the bench");
        }

        [Test]
        public void ScrappingThePendingItemTeachesTheSameBonusAsTheSeaDoes()
        {
            // The two XP write sites. A bonus on one and not the other would be a bonus that depended
            // on which screen the player happened to be standing on when they scrapped.
            var data = new SaveData { craftPendingGrade = Grade + 1, craftPendingSlot = SeaCombat.SlotCannon };
            var wallet = new WalletService(data.wallet);
            var bench = new CraftingService(data, null, new TimeService()) { Cards = Maxed(data, wallet) };

            Assert.That(bench.HasPending, Is.True, "the premise: there is something on the bench");
            bench.SalvagePending(out long xp);

            Assert.That(xp, Is.EqualTo(ExpectedXp(bench.Cards.Effects.CraftXpMultiplier)));
            Assert.That(data.craftXp, Is.EqualTo(xp));
        }

        [Test]
        public void FeedingTheBenchYourOwnItemDoesNotPayTheSeasFindBonus()
        {
            // The hurda from the pending cell is an item the player already owned turned back into
            // scrap. SeaSalvageMultiplier is a bonus on what the sea PAYS OUT, and this is not that.
            var data = new SaveData { craftPendingGrade = Grade + 1, craftPendingSlot = SeaCombat.SlotCannon };
            var wallet = new WalletService(data.wallet);
            var bench = new CraftingService(data, null, new TimeService()) { Cards = Maxed(data, wallet) };

            Assert.That(bench.Cards.Effects.SeaSalvageMultiplier, Is.GreaterThan(1d), "the premise");
            long paid = bench.SalvagePending(out _);

            Assert.That(paid, Is.EqualTo(SeaCombat.ScrapFor(Grade)), "the table, untouched");
            Assert.That(data.salvage, Is.EqualTo(paid));
        }

        // ---- the bench: the point drop ------------------------------------------------------------

        [Test]
        public void TheCollectionWidensThePointDropWindowRatherThanRollingAgain()
        {
            // One roll, one published chance. The card bonus moves where the line is; it does not get
            // the player a second dice throw, which would make the printed odds untrue.
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            CardCollectionService cards = Maxed(data, wallet);

            double bare = Crafting.Tuning.Default.PointDropChance;
            double wide = CardCollection.CraftPointChance(bare, cards.Effects.CraftPointDrop, cards.Tuning);
            Assert.That(wide, Is.GreaterThan(bare), "the premise: the window actually got wider");

            double between = (bare + wide) * 0.5d;   // inside the new window, outside the old one

            var unwired = new CraftingService(new SaveData(), null, new TimeService());
            Assert.That(unwired.TryDropPoint(between), Is.False, "without cards this roll is a miss");

            var bench = new CraftingService(data, null, new TimeService()) { Cards = cards };
            Assert.That(bench.TryDropPoint(between), Is.True, "with them it lands");
            Assert.That(data.craftPoints, Is.EqualTo((long)Crafting.Tuning.Default.PointsPerWin),
                        "one win pays one win's points however wide the window is");
        }

        [Test]
        public void TheWidenedWindowStillRefusesARollOutsideIt()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var bench = new CraftingService(data, null, new TimeService()) { Cards = Maxed(data, wallet) };

            Assert.That(bench.TryDropPoint(0.999d), Is.False, "the collection is a bonus, not a guarantee");
            Assert.That(data.craftPoints, Is.Zero);
        }

        // ---- the sea: fight loot ------------------------------------------------------------------

        [Test]
        public void AWonFightsSalvageAndChartsCarryTheCollection()
        {
            const int Charts = 100, Salvage = 200;
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            CardCollectionService cards = Maxed(data, wallet);
            var captains = new CaptainService(data, Captains.Tuning.Default, CaptainCrate.Tuning.Default);

            var sea = new ExpeditionService(new TimeService(), data, captains, T) { Cards = cards };
            sea.SetSail(Coal);
            Assert.That(sea.RegisterKill(Charts, Salvage), Is.True);

            Assert.That(data.salvage,
                        Is.EqualTo(CardCollection.Scale(Salvage, cards.Effects.SeaSalvageMultiplier)));
            Assert.That(data.charts,
                        Is.EqualTo(CardCollection.Scale(Charts, cards.Effects.SeaChartMultiplier)));
            Assert.That(data.salvage, Is.GreaterThan(Salvage));
            Assert.That(data.charts, Is.GreaterThan(Charts));
        }

        [Test]
        public void AnUnwiredSeaPaysExactlyWhatItAlwaysPaid()
        {
            var data = new SaveData();
            var captains = new CaptainService(data, Captains.Tuning.Default, CaptainCrate.Tuning.Default);
            var sea = new ExpeditionService(new TimeService(), data, captains, T);
            sea.SetSail(Coal);
            sea.RegisterKill(100, 200);

            Assert.That(data.salvage, Is.EqualTo(200L));
            Assert.That(data.charts, Is.EqualTo(100L));
        }

        [Test]
        public void ScrappingFromTheShelfDoesNotPayTheFindBonus()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var sea = new ExpeditionService(new TimeService(), data, null, T) { Cards = Maxed(data, wallet) };
            sea.Stow(Gear(SeaCombat.SlotCannon, Grade));

            long paid = sea.ScrapFromStash(sea.StashIdAt(0), out _);

            Assert.That(paid, Is.EqualTo(SeaCombat.ScrapFor(Grade)), "the table, untouched");
            Assert.That(data.salvage, Is.EqualTo(paid), "a shelf clear-out is not a find");
        }

        [Test]
        public void EmptyingTheShelfDoesNotPayTheFindBonusEither()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var sea = new ExpeditionService(new TimeService(), data, null, T) { Cards = Maxed(data, wallet) };
            sea.Stow(Gear(SeaCombat.SlotCannon, Grade));
            sea.Stow(Gear(SeaCombat.SlotCharm, Grade));

            long paid = sea.ScrapAllStash(out _);

            Assert.That(paid, Is.EqualTo(SeaCombat.ScrapFor(Grade) * 2L));
            Assert.That(data.salvage, Is.EqualTo(paid));
        }

        [Test]
        public void StrippingAndRefusingDoNotPayTheFindBonus()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var sea = new ExpeditionService(new TimeService(), data, null, T) { Cards = Maxed(data, wallet) };

            sea.Scrap(Grade);                                   // refused a drop
            long afterRefuse = data.salvage;
            Assert.That(afterRefuse, Is.EqualTo(SeaCombat.ScrapFor(Grade)));

            sea.Equip(Gear(SeaCombat.SlotCannon, Grade));       // nothing displaced, so nothing scrapped
            sea.ScrapWorn(SeaCombat.SlotCannon);                // stripped it off again

            Assert.That(data.salvage - afterRefuse, Is.EqualTo(SeaCombat.ScrapFor(Grade)),
                        "a strip pays the table and not a penny of the find bonus");
        }

        // ---- the reported figure ------------------------------------------------------------------

        [Test]
        public void TheShelfReportsTheLessonThatActuallyLanded()
        {
            // ScrapFromStash's out-xp used to read the shipped table directly. Once a collection can
            // lift the figure, the popup and the bench have to agree — a toast saying 250 while 320
            // went in is a bug waiting for whoever finally wires that number to a label.
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            CardCollectionService cards = Maxed(data, wallet);
            var bench = new CraftingService(data, null, new TimeService()) { Cards = cards };

            var sea = new ExpeditionService(new TimeService(), data, null, T) { Crafting = bench };
            sea.Stow(Gear(SeaCombat.SlotCannon, Grade));
            sea.ScrapFromStash(sea.StashIdAt(0), out long reported);

            Assert.That(reported, Is.EqualTo(ExpectedXp(cards.Effects.CraftXpMultiplier)));
            Assert.That(data.craftXp, Is.EqualTo(reported), "reported and granted are the same number");
        }

        [Test]
        public void EmptyingTheShelfReportsEveryLessonItTaught()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            CardCollectionService cards = Maxed(data, wallet);
            var bench = new CraftingService(data, null, new TimeService()) { Cards = cards };

            var sea = new ExpeditionService(new TimeService(), data, null, T) { Crafting = bench };
            sea.Stow(Gear(SeaCombat.SlotCannon, Grade));
            sea.Stow(Gear(SeaCombat.SlotCharm, Grade));
            sea.ScrapAllStash(out long reported);

            Assert.That(reported, Is.EqualTo(ExpectedXp(cards.Effects.CraftXpMultiplier) * 2L));
            Assert.That(data.craftXp, Is.EqualTo(reported));
        }

        [Test]
        public void AnUnwiredBenchStillReportsTheShippedTable()
        {
            var data = new SaveData();
            var bench = new CraftingService(data, null, new TimeService());
            var sea = new ExpeditionService(new TimeService(), data, null, T) { Crafting = bench };
            sea.Stow(Gear(SeaCombat.SlotCannon, Grade));
            sea.ScrapFromStash(sea.StashIdAt(0), out long reported);

            Assert.That(reported, Is.EqualTo(Crafting.SalvageXpFor(Grade)));
        }

        // ---- the shared rounding rule -------------------------------------------------------------

        [Test]
        public void AScaledRewardRoundsAwayFromZeroLikeEveryOtherOneInTheProject()
        {
            Assert.That(CardCollection.Scale(10L, 1.05d), Is.EqualTo(11L), "10.5 goes up, not down");
            Assert.That(CardCollection.Scale(100L, 1.16d), Is.EqualTo(116L));
        }

        [Test]
        public void ABonusCanNeverSubtract()
        {
            // Every consumer hands Scale a multiplier it did not compute itself. A collection that is
            // empty, damaged, or somehow negative must leave the reward exactly as the table set it.
            Assert.That(CardCollection.Scale(50L, 1d), Is.EqualTo(50L));
            Assert.That(CardCollection.Scale(50L, 0.5d), Is.EqualTo(50L), "never shaves");
            Assert.That(CardCollection.Scale(50L, -3d), Is.EqualTo(50L));
            Assert.That(CardCollection.Scale(50L, double.NaN), Is.EqualTo(50L));
            Assert.That(CardCollection.Scale(0L, 2d), Is.Zero, "nothing doubled is still nothing");
            Assert.That(CardCollection.Scale(-5L, 2d), Is.EqualTo(-5L), "and a debt is not a reward");
        }

        [Test]
        public void AScaledRewardCannotOverflowIntoANegativeNumber()
        {
            Assert.That(CardCollection.Scale(long.MaxValue, 2d), Is.EqualTo(long.MaxValue));
            Assert.That(CardCollection.Scale(1L, double.PositiveInfinity), Is.EqualTo(long.MaxValue));
        }
    }
}
