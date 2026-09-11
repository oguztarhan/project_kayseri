using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Game.Core;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>
    /// Phase 6: every currency against real saves — old ones, clean ones, damaged ones, and an app
    /// killed at the worst moment. Each test builds the balance owners the way
    /// <see cref="GameBootstrap"/> does (load, the version check, the prestige retirement, then the
    /// services in boot order) and reads the ten balances through <see cref="CurrencyRegistry"/>,
    /// the surface the wallet screen shows.
    ///
    /// A "force-close" here is the in-memory session abandoned with no exit save, and a new one booted
    /// from whatever is on disk.
    /// </summary>
    public class CurrencyPersistenceTests
    {
        private string _path;

        [SetUp]
        public void SetUp()
        {
            _path = Path.Combine(Application.temporaryCachePath,
                                 "currency-persistence-" + Guid.NewGuid().ToString("N") + ".dat");
        }

        [TearDown]
        public void TearDown()
        {
            Delete(_path);
            Delete(_path + SaveService.TempSuffix);
            Delete(_path + SaveService.BackupSuffix);
            Delete(_path + SaveService.UnreadableSuffix);
        }

        private static void Delete(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        // ---- the session ---------------------------------------------------------------------------

        private sealed class Session
        {
            public SaveData Data;
            public SaveService Save;
            public WalletService Wallet;
            public MiningGearService Mining;
            public CaptainService Captains;
            public CraftingService Crafting;
            public ExpeditionService Sea;
            public PetService Pets;
            public CurrencyRegistry Registry;
        }

        /// <summary>The balance owners, built from one save in <see cref="GameBootstrap"/>'s order.</summary>
        private static Session Boot(SaveData data, SaveService save)
        {
            if (SaveMigration.NeedsReset(data)) data = SaveMigration.Reset(data);
            SaveMigration.RetirePrestige(data, 0.10d);

            var time = new TimeService();
            var s = new Session { Data = data, Save = save };
            s.Wallet = new WalletService(data.wallet);
            s.Mining = new MiningGearService(data, save, time, MiningGear.Tuning.Default);
            s.Captains = new CaptainService(data, Captains.Tuning.Default, CaptainCrate.Tuning.Default,
                                            save: save);
            s.Crafting = new CraftingService(data, save, time, Crafting.Tuning.Default,
                                             SeaCombat.Tuning.Default);
            s.Sea = new ExpeditionService(time, data, s.Captains, SeaCombat.Tuning.Default, save);
            s.Sea.Crafting = s.Crafting;
            s.Crafting.Expeditions = s.Sea;
            s.Pets = new PetService(data, Pets.Tuning.Default, PetChest.Tuning.Default, null, null, null,
                                    save, Pets.RewardTuning.Default, time.NowUnix);
            s.Sea.Pets = s.Pets;
            s.Registry = new CurrencyRegistry(time, s.Wallet, s.Sea, s.Crafting, s.Mining, s.Captains, s.Pets);
            return s;
        }

        private Session BootNew(SaveData data) => Boot(data, new SaveService(_path));

        /// <summary>What a launch would find if the app were killed right now: a new session booted
        /// from the disk alone. The running session is left untouched, so a test can probe after each
        /// step of one play-through.</summary>
        private static Session Relaunch(Session killed)
        {
            var save = new SaveService(killed.Save.SavePath);
            Assert.That(save.TryLoad(out SaveData loaded), Is.True, "the relaunch found a readable save");
            return Boot(loaded, save);
        }

        private static Dictionary<CurrencyId, CurrencySnapshot> Snap(Session s)
        {
            var list = new List<CurrencySnapshot>();
            s.Registry.GetSnapshots(list);
            var byId = new Dictionary<CurrencyId, CurrencySnapshot>();
            foreach (CurrencySnapshot snapshot in list) byId[snapshot.Id] = snapshot;
            return byId;
        }

        private static void AssertSameBalances(Dictionary<CurrencyId, CurrencySnapshot> expected,
                                               Dictionary<CurrencyId, CurrencySnapshot> actual,
                                               string context)
        {
            Assert.That(actual.Count, Is.EqualTo(10), context + ": every currency is listed");
            foreach (KeyValuePair<CurrencyId, CurrencySnapshot> pair in expected)
            {
                CurrencySnapshot want = pair.Value;
                CurrencySnapshot got = actual[pair.Key];
                string where = context + " — " + pair.Key;
                Assert.That(got.WholeAmount, Is.EqualTo(want.WholeAmount), where);
                Assert.That(got.Maximum, Is.EqualTo(want.Maximum), where + " maximum");
                Assert.That(got.Current.Exponent, Is.EqualTo(want.Current.Exponent), where + " exponent");
                Assert.That(got.Current.Mantissa, Is.EqualTo(want.Current.Mantissa).Within(1e-12),
                            where + " mantissa");
            }
        }

        private static long Whole(Session s, CurrencyId id) => s.Registry.GetSnapshot(id).WholeAmount;

        /// <summary>A live player's save with every one of the ten balances holding something, and the
        /// two regenerating pools stamped now so nothing accrues while a test runs.</summary>
        private static SaveData Populated()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var d = new SaveData
            {
                salvage = 1234L,
                charts = 567L,
                seaEnergy = SeaCombat.Tuning.Default.EnergyMax,
                seaEnergyStampUnix = now,
                craftPoints = 89L,
                miningPoints = 21L,
                miningPointsStampUnix = now,
                miningScrap = 345L,
                pearls = 678L,
            };
            d.wallet.cash = new BigDouble(7.25d, 11);
            d.wallet.gems = 432L;
            d.pets.petEssence = 9L;
            return d;
        }

        // ---- old saves -----------------------------------------------------------------------------

        /// <summary>
        /// A save written before any of the eight non-wallet currencies existed: a wallet, a station,
        /// and three fields later builds removed. The two balances it has are kept to the unit; the
        /// eight it never heard of arrive at the safe start — empty, except the energy pool, which a
        /// returning player finds FULL (SaveData.seaEnergy's -1 default).
        /// </summary>
        [Test]
        public void ASaveFromBeforeTheCurrenciesExistedKeepsItsWalletAndStartsTheRestSafely()
        {
            const string legacy =
                "{\"version\":7,\"prestigeRetired\":true,\"legacyIncomeMultiplier\":1.0," +
                "\"savedUnixSeconds\":1757000000," +
                "\"wallet\":{\"cash\":{\"Mantissa\":4.5,\"Exponent\":9},\"gems\":120," +
                "\"investors\":0.0,\"lifetimeCash\":{\"Mantissa\":0.0,\"Exponent\":0}}," +
                "\"stationLevels\":[{\"id\":\"mine\",\"level\":12}]," +
                "\"voyagesCompleted\":14,\"hullReadyUnix\":0,\"foremanLevels\":[1,2,3]}";

            SaveData data = JsonUtility.FromJson<SaveData>(legacy);
            Assert.That(SaveMigration.NeedsReset(data), Is.False, "no wipe for a save of this version");
            Session s = BootNew(data);

            Assert.That(s.Wallet.Cash.Mantissa, Is.EqualTo(4.5d).Within(1e-12));
            Assert.That(s.Wallet.Cash.Exponent, Is.EqualTo(9L));
            Assert.That(Whole(s, CurrencyId.Gems), Is.EqualTo(120L));
            Assert.That(Whole(s, CurrencyId.Salvage), Is.Zero);
            Assert.That(Whole(s, CurrencyId.Charts), Is.Zero);
            Assert.That(Whole(s, CurrencyId.CombatEnergy), Is.EqualTo(SeaCombat.Tuning.Default.EnergyMax),
                        "a pre-feature save starts with a full pool, not a wait");
            Assert.That(Whole(s, CurrencyId.CraftPoints), Is.Zero);
            Assert.That(Whole(s, CurrencyId.MiningPoints), Is.Zero);
            Assert.That(Whole(s, CurrencyId.MiningScrap), Is.Zero);
            Assert.That(Whole(s, CurrencyId.Pearls), Is.Zero);
            Assert.That(Whole(s, CurrencyId.PetEssence), Is.Zero);
            Assert.That(data.stationLevels[0].level, Is.EqualTo(12), "and the rest of the empire rides along");
            Assert.That(data.captainLevels.Length, Is.EqualTo(Captains.Count));
            Assert.That(data.pets.counts.Length, Is.EqualTo(Pets.CountsLength));
        }

        /// <summary>
        /// A save from the build before mining scrap and pet essence: every balance it carries is kept
        /// exactly, the two it lacks arrive at zero, and a short mining grade array keeps its cells.
        /// </summary>
        [Test]
        public void ASaveFromBeforeScrapAndEssenceKeepsEveryBalanceItHas()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string legacy =
                "{\"version\":7,\"wallet\":{\"cash\":{\"Mantissa\":2.0,\"Exponent\":6},\"gems\":55}," +
                "\"salvage\":340,\"charts\":75,\"seaEnergy\":12,\"seaEnergyStampUnix\":" + now + "," +
                "\"craftPoints\":9,\"miningPoints\":33,\"miningPointsStampUnix\":" + now + "," +
                "\"miningGearGrade\":[2,0,1],\"pearls\":410," +
                "\"pets\":{\"counts\":[],\"equippedSpecies\":[-1],\"chestsOpened\":3}}";

            SaveData data = JsonUtility.FromJson<SaveData>(legacy);
            Session s = BootNew(data);

            Assert.That(s.Wallet.Cash.ToDouble(), Is.EqualTo(2000000d).Within(1e-6));
            Assert.That(Whole(s, CurrencyId.Gems), Is.EqualTo(55L));
            Assert.That(Whole(s, CurrencyId.Salvage), Is.EqualTo(340L));
            Assert.That(Whole(s, CurrencyId.Charts), Is.EqualTo(75L));
            Assert.That(Whole(s, CurrencyId.CombatEnergy), Is.EqualTo(12L));
            Assert.That(Whole(s, CurrencyId.CraftPoints), Is.EqualTo(9L));
            Assert.That(Whole(s, CurrencyId.MiningPoints), Is.EqualTo(33L));
            Assert.That(Whole(s, CurrencyId.MiningScrap), Is.Zero);
            Assert.That(Whole(s, CurrencyId.Pearls), Is.EqualTo(410L));
            Assert.That(Whole(s, CurrencyId.PetEssence), Is.Zero);
            Assert.That(s.Mining.WornGrade(0), Is.EqualTo(1), "grade+1 cells keep their meaning");
            Assert.That(s.Mining.WornGrade(2), Is.EqualTo(0));
            Assert.That(s.Mining.WornGrade(3), Is.EqualTo(MiningGear.NoGrade), "the missing slot is empty");
            Assert.That(s.Pets.ChestsOpened, Is.EqualTo(3));
        }

        /// <summary>The load path adds no destructive step: a save of this version is not wiped, and
        /// retiring prestige moves none of the ten balances.</summary>
        [Test]
        public void TheLoadPathMovesNoBalanceOnASaveOfThisVersion()
        {
            SaveData data = Populated();
            data.prestigeRetired = false;
            data.wallet.investors = 40d;
            Assert.That(SaveMigration.NeedsReset(data), Is.False);

            Session reference = BootNew(Populated());
            Session s = BootNew(data);
            Assert.That(data.prestigeRetired, Is.True, "the retirement ran");
            AssertSameBalances(Snap(reference), Snap(s), "after the prestige retirement");
        }

        // ---- clean saves ---------------------------------------------------------------------------

        /// <summary>
        /// What a brand-new player's wallet reads: nothing banked anywhere, a full energy pool with no
        /// countdown, and an empty mining pool already counting toward its first point.
        /// </summary>
        [Test]
        public void ACleanSaveStartsEveryCurrencyAtASafeUnderstandableValue()
        {
            Session s = BootNew(new SaveData());
            Dictionary<CurrencyId, CurrencySnapshot> snap = Snap(s);

            Assert.That(snap[CurrencyId.Cash].Current.IsZero, Is.True);
            foreach (CurrencyId id in new[] { CurrencyId.Gems, CurrencyId.Salvage, CurrencyId.Charts,
                                              CurrencyId.CraftPoints, CurrencyId.MiningPoints,
                                              CurrencyId.MiningScrap, CurrencyId.Pearls,
                                              CurrencyId.PetEssence })
                Assert.That(snap[id].WholeAmount, Is.Zero, id.ToString());

            CurrencySnapshot energy = snap[CurrencyId.CombatEnergy];
            Assert.That(energy.WholeAmount, Is.EqualTo(energy.Maximum));
            Assert.That(energy.Maximum, Is.GreaterThan(0L));
            Assert.That(energy.SecondsToNextRegeneration, Is.Zero, "a full pool shows no countdown");

            CurrencySnapshot mining = snap[CurrencyId.MiningPoints];
            Assert.That(mining.Maximum, Is.EqualTo(MiningGear.Tuning.Default.PointCap));
            Assert.That(mining.SecondsToNextRegeneration, Is.GreaterThan(0d));
            Assert.That(mining.SecondsToNextRegeneration,
                        Is.LessThanOrEqualTo(MiningGear.Tuning.Default.TickSeconds),
                        "the first point is at most one tick away");
            Assert.That(s.Data.miningPointsStampUnix, Is.GreaterThan(0L), "the clock started on the first boot");
        }

        [Test]
        public void ACleanSaveReadsTheSameAfterASaveAndARelaunch()
        {
            Session s = BootNew(new SaveData());
            s.Save.Save(s.Data);
            AssertSameBalances(Snap(s), Snap(Relaunch(s)), "clean save relaunched");
        }

        // ---- damaged saves -------------------------------------------------------------------------

        [Test]
        public void EveryNegativeBalanceIsReadAsEmpty()
        {
            var data = new SaveData
            {
                salvage = -1L, charts = -2L, craftPoints = -3L, miningPoints = -4L,
                miningScrap = -5L, pearls = -6L, seaEnergy = -7,
            };
            data.wallet.cash = new BigDouble(-8d);
            data.wallet.gems = -9L;
            data.pets.petEssence = -10L;

            Session s = BootNew(data);
            Dictionary<CurrencyId, CurrencySnapshot> snap = Snap(s);

            Assert.That(snap[CurrencyId.Cash].Current.IsZero, Is.True);
            foreach (CurrencyId id in new[] { CurrencyId.Gems, CurrencyId.Salvage, CurrencyId.Charts,
                                              CurrencyId.CraftPoints, CurrencyId.MiningPoints,
                                              CurrencyId.MiningScrap, CurrencyId.Pearls,
                                              CurrencyId.PetEssence })
                Assert.That(snap[id].WholeAmount, Is.Zero, id.ToString());
            Assert.That(snap[CurrencyId.CombatEnergy].WholeAmount, Is.EqualTo(snap[CurrencyId.CombatEnergy].Maximum),
                        "an unreadable pool is a full one, never a debt");
        }

        [Test]
        public void NegativeBalancesStayRepairedAfterTheRepairIsWritten()
        {
            SaveData data = JsonUtility.FromJson<SaveData>(
                "{\"version\":7,\"salvage\":-40,\"charts\":-1,\"craftPoints\":-2,\"miningScrap\":-3," +
                "\"pearls\":-4,\"wallet\":{\"cash\":{\"Mantissa\":-5.0,\"Exponent\":3},\"gems\":-6}," +
                "\"pets\":{\"petEssence\":-7}}");
            Session s = BootNew(data);
            s.Save.Save(s.Data);

            Session relaunched = Relaunch(s);
            AssertSameBalances(Snap(s), Snap(relaunched), "repaired and relaunched");
            Assert.That(relaunched.Data.salvage, Is.Zero, "the repair itself is what reached the disk");
            Assert.That(relaunched.Data.wallet.gems, Is.Zero);
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void CashThatIsNotANumberIsReadAsEmptyAndStaysComparable(double mantissa)
        {
            var data = new SaveData();
            data.wallet.cash = new BigDouble { Mantissa = mantissa, Exponent = 4L };

            Session s = BootNew(data);
            Assert.That(s.Wallet.Cash.IsZero, Is.True);
            Assert.DoesNotThrow(() => s.Wallet.CanAfford(new BigDouble(1d)));
            s.Wallet.AddCash(new BigDouble(25d));
            Assert.That(s.Wallet.Cash.ToDouble(), Is.EqualTo(25d).Within(1e-9));
        }

        [Test]
        public void CashStoredOutOfNormalFormKeepsItsWorth()
        {
            var data = new SaveData();
            data.wallet.cash = new BigDouble { Mantissa = 125d, Exponent = 2L };   // 12,500 written oddly

            Session s = BootNew(data);
            Assert.That(s.Wallet.Cash.Mantissa, Is.EqualTo(1.25d).Within(1e-12));
            Assert.That(s.Wallet.Cash.Exponent, Is.EqualTo(4L));
            Assert.That(s.Wallet.CanAfford(new BigDouble(12500d)), Is.True);
            Assert.That(s.Wallet.CanAfford(new BigDouble(12501d)), Is.False);
        }

        [Test]
        public void AnEnergyPoolStoredAboveItsSizeReadsAndIsStoredFull()
        {
            var data = new SaveData
            {
                seaEnergy = SeaCombat.Tuning.Default.EnergyMax + 70,
                seaEnergyStampUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            };
            Session s = BootNew(data);

            Assert.That(s.Sea.Energy, Is.EqualTo(s.Sea.EnergyMax));
            Assert.That(data.seaEnergy, Is.EqualTo(s.Sea.EnergyMax), "the stored value is inside the pool");
            Assert.That(s.Sea.SecondsToNextEnergy, Is.Zero);
        }

        /// <summary>
        /// The cap stops the pool filling; it never takes points back. A pool above it — a cap lowered
        /// in tuning after the pool filled — keeps every point through a long absence and stays
        /// spendable, where the accrual used to trim it to the cap on the first tick.
        /// </summary>
        [Test]
        public void AMiningPoolAboveItsCapKeepsEveryPointThroughALongAbsence()
        {
            MiningGear.Tuning t = MiningGear.Tuning.Default;
            long over = t.PointCap + 30L;
            var data = new SaveData
            {
                miningPoints = over,
                miningPointsStampUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (long)(t.TickSeconds * 40d),
            };

            Session s = BootNew(data);
            s.Mining.Poll();
            Assert.That(s.Mining.Points, Is.EqualTo(over), "nothing trimmed, nothing added");
            Assert.That(s.Mining.SecondsToNextPoint, Is.Zero, "no countdown while over the cap");
            Assert.That(s.Registry.GetSnapshot(CurrencyId.MiningPoints).Maximum, Is.EqualTo(t.PointCap));
            Assert.That(s.Mining.CanCraft, Is.True);
        }

        [Test]
        public void AnUnderCapMiningPoolStillStopsFillingAtTheCap()
        {
            MiningGear.Tuning t = MiningGear.Tuning.Default;
            var data = new SaveData
            {
                miningPoints = t.PointCap - 2L,
                miningPointsStampUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (long)(t.TickSeconds * 40d),
            };
            Session s = BootNew(data);
            Assert.That(s.Mining.Points, Is.EqualTo(t.PointCap));
        }

        [Test]
        public void MissingAndShortArraysArePaddedWithoutMovingABalance()
        {
            SaveData data = Populated();
            data.captainLevels = null;
            data.captainDuplicates = new int[1];
            data.miningGearGrade = null;
            data.seaGearGrade = new int[1];
            data.pets.counts = null;
            data.pets.equippedSpecies = null;

            Session reference = BootNew(Populated());
            Session s = BootNew(data);

            AssertSameBalances(Snap(reference), Snap(s), "after padding");
            Assert.That(data.captainLevels.Length, Is.EqualTo(Captains.Count));
            Assert.That(data.captainDuplicates.Length, Is.EqualTo(Captains.Count));
            Assert.That(data.miningGearGrade.Length, Is.EqualTo(MiningGear.SlotCount));
            Assert.That(data.seaGearGrade.Length, Is.EqualTo(SeaCombat.SlotCount));
            Assert.That(data.pets.counts.Length, Is.EqualTo(Pets.CountsLength));
            Assert.That(data.pets.equippedSpecies.Length, Is.EqualTo(Pets.SlotCount));
        }

        [Test]
        public void ATornSaveFileBootsFromTheBackupWithEveryBalance()
        {
            Session s = BootNew(Populated());
            s.Save.Save(s.Data);
            Dictionary<CurrencyId, CurrencySnapshot> written = Snap(s);
            s.Save.Save(s.Data);   // the same balances again, so the backup holds them too

            byte[] whole = File.ReadAllBytes(_path);
            var torn = new byte[whole.Length / 3];
            Buffer.BlockCopy(whole, 0, torn, 0, torn.Length);
            File.WriteAllBytes(_path, torn);

            AssertSameBalances(written, Snap(Relaunch(s)), "booted from the backup");
        }

        // ---- save and reload -----------------------------------------------------------------------

        [Test]
        public void EveryBalanceSurvivesASaveAndARelaunchTwice()
        {
            Session s = BootNew(Populated());
            s.Save.Save(s.Data);
            Session first = Relaunch(s);
            AssertSameBalances(Snap(s), Snap(first), "first relaunch");

            first.Save.Save(first.Data);
            AssertSameBalances(Snap(s), Snap(Relaunch(first)), "second relaunch — nothing drifts");
        }

        // ---- force-close, one currency group at a time ----------------------------------------------

        /// <summary>
        /// MAIN ECONOMY AND PREMIUM. The wallet has no write of its own — cash moves every frame — so
        /// cash and gems ride whatever write comes next, and a kill with no write in between returns
        /// every balance to the last write exactly: nothing lands half.
        /// </summary>
        [Test]
        public void CashAndGemsRideTheNextWriteAndAKillBeforeItReturnsToTheLastWrite()
        {
            SaveData data = Populated();
            data.miningPoints = MiningGear.Tuning.Default.CraftCost;
            Session s = BootNew(data);
            s.Save.Save(s.Data);
            Dictionary<CurrencyId, CurrencySnapshot> lastWrite = Snap(s);

            s.Wallet.AddCash(new BigDouble(5000d));
            Assert.That(s.Wallet.TrySpendGems(3L), Is.True);
            AssertSameBalances(lastWrite, Snap(Relaunch(s)), "killed before any write");

            Assert.That(s.Mining.TryCraftWithRolls(0.1d, 0.1d).Crafted, Is.True);
            Session after = Relaunch(s);
            AssertSameBalances(Snap(s), Snap(after), "the craft's write carried the wallet");
            Assert.That(after.Wallet.Gems, Is.EqualTo(Populated().wallet.gems - 3L));
        }

        /// <summary>SEA. A won fight — the search's energy, the charts, the salvage, the craft-point
        /// roll, the pearls and the fight count — reaches the disk in the win's own write.</summary>
        [Test]
        public void AWonFightReachesTheDiskWholeBeforeTheBannerShowsIt()
        {
            SaveData data = Populated();
            Session s = BootNew(data);
            Assert.That(s.Sea.SetSail("coal"), Is.True);
            Assert.That(s.Sea.TrySpendEnergy(), Is.True);
            Assert.That(s.Sea.RegisterKill(4, 6), Is.True);
            s.Sea.RegisterWin(0, 0);

            Session relaunched = Relaunch(s);
            AssertSameBalances(Snap(s), Snap(relaunched), "after the win");
            Assert.That(Whole(relaunched, CurrencyId.Charts), Is.EqualTo(567L + 4L));
            Assert.That(Whole(relaunched, CurrencyId.Salvage), Is.EqualTo(1234L + 6L));
            Assert.That(Whole(relaunched, CurrencyId.CombatEnergy), Is.EqualTo(s.Sea.EnergyMax - 1L));
            Assert.That(relaunched.Data.seaFightsWon, Is.EqualTo(1));
        }

        /// <summary>SEA. A search the player walks away from rides the next write like any trickle; a
        /// kill before one refunds it. Never charged twice, never half.</summary>
        [Test]
        public void ASearchWithNoWriteBehindItIsRefundedByAKillNeverChargedTwice()
        {
            Session s = BootNew(Populated());
            s.Save.Save(s.Data);
            Assert.That(s.Sea.SetSail("coal"), Is.True);
            Assert.That(s.Sea.TrySpendEnergy(), Is.True);

            Assert.That(Whole(Relaunch(s), CurrencyId.CombatEnergy), Is.EqualTo(s.Sea.EnergyMax));
        }

        /// <summary>SEA (charts). The crate's price and its captains land in one write before the
        /// reveal, so a kill can neither refund the charts nor roll the crate again.</summary>
        [Test]
        public void ACrateOpenReachesTheDiskBeforeItIsRevealed()
        {
            var data = new SaveData();
            Session probe = BootNew(new SaveData());
            long cost = probe.Captains.CrateCost(1);
            data.charts = cost * 2L;

            Session s = BootNew(data);
            int[] got = s.Captains.TryOpen(1);
            Assert.That(got, Is.Not.Null);

            Session relaunched = Relaunch(s);
            Assert.That(relaunched.Captains.Charts, Is.EqualTo(cost), "the price is on disk");
            Assert.That(relaunched.Captains.Owned(got[0]), Is.True, "and the captain in the same write");
            Assert.That(relaunched.Data.cratesOpened, Is.EqualTo(1));
            Assert.That(relaunched.Data.crateSinceEpic, Is.EqualTo(s.Data.crateSinceEpic));
            Assert.That(relaunched.Data.crateSinceLegendary, Is.EqualTo(s.Data.crateSinceLegendary));
        }

        private static ForemanService ForemenOf(Session s)
            => new ForemanService(s.Data, s.Wallet, Game.Core.Foremen.Tuning.Default,
                                  MasterChest.Tuning.Default, save: s.Save);

        /// <summary>
        /// PREMIUM (the foreman chest). The gems and the cards they bought land in one write before the
        /// reveal, so a kill can neither refund the gems nor roll the chest again: the relaunch finds
        /// exactly the cards the reveal showed.
        /// </summary>
        [Test]
        public void AForemanChestReachesTheDiskBeforeItIsRevealed()
        {
            Session s = BootNew(Populated());
            s.Save.Save(s.Data);
            ForemanService foremen = ForemenOf(s);
            long cost = foremen.ChestCost(1);
            Assert.That(s.Wallet.Gems, Is.GreaterThanOrEqualTo(cost), "the fixture can pay for one chest");

            int[] got = foremen.TryOpenChest(1);
            Assert.That(got, Is.Not.Null.And.Not.Empty);

            Session relaunched = Relaunch(s);
            ForemanService reloaded = ForemenOf(relaunched);
            Assert.That(Whole(relaunched, CurrencyId.Gems), Is.EqualTo(432L - cost), "the price is on disk");
            Assert.That(reloaded.ChestsOpened, Is.EqualTo(1));
            for (int m = 0; m < Game.Core.Foremen.Count; m++)
            {
                int dealt = 0;
                for (int i = 0; i < got.Length; i++) if (got[i] == m) dealt++;
                Assert.That(reloaded.DuplicatesOf(m), Is.EqualTo(dealt), "master " + m + ": the cards revealed");
                Assert.That(reloaded.LevelOf(m), Is.EqualTo(foremen.LevelOf(m)), "master " + m + ": stars");
            }
            AssertSameBalances(Snap(s), Snap(relaunched), "after a foreman chest");
        }

        /// <summary>The free foreman chest the same way: its claim and its cards are one write, so a
        /// kill during the reveal does not hand it out a second time.</summary>
        [Test]
        public void AFreeForemanChestReachesTheDiskBeforeItIsRevealed()
        {
            Session s = BootNew(Populated());
            s.Save.Save(s.Data);
            ForemanService foremen = ForemenOf(s);
            Assert.That(foremen.FreeChestReady, Is.True, "a fresh roster's free chest is due");

            int[] got = foremen.TryClaimFreeChest();
            Assert.That(got, Is.Not.Null.And.Not.Empty);

            ForemanService reloaded = ForemenOf(Relaunch(s));
            Assert.That(reloaded.FreeChestReady, Is.False, "the claim is on disk");
            for (int m = 0; m < Game.Core.Foremen.Count; m++)
                Assert.That(reloaded.DuplicatesOf(m), Is.EqualTo(foremen.DuplicatesOf(m)), "master " + m);
        }

        /// <summary>CRAFTING. The points and the item they bought are one write.</summary>
        [Test]
        public void ACraftReachesTheDiskWithTheItemItBought()
        {
            SaveData data = Populated();
            long cost = Crafting.Tuning.Default.CraftCost;
            data.craftPoints = cost * 2L;

            Session s = BootNew(data);
            Assert.That(s.Crafting.TryCraft(out SeaCombat.Item item), Is.True);

            Session relaunched = Relaunch(s);
            Assert.That(Whole(relaunched, CurrencyId.CraftPoints), Is.EqualTo(cost));
            Assert.That(relaunched.Crafting.HasPending, Is.True);
            Assert.That(relaunched.Crafting.PendingItem().Grade, Is.EqualTo(item.Grade));
            Assert.That(relaunched.Crafting.PendingItem().Slot, Is.EqualTo(item.Slot));
        }

        /// <summary>MINING GEAR. Normal and targeted crafts each put the points, the scrap fee, the
        /// refund and the kept item on disk together.</summary>
        [Test]
        public void MiningCraftsReachTheDiskWithEveryBalanceTheyMoved()
        {
            SaveData data = Populated();
            data.miningPoints = MiningGear.Tuning.Default.CraftCost * 2L;
            data.miningScrap = 5000L;
            Session s = BootNew(data);

            Assert.That(s.Mining.TryCraftWithRolls(0.3d, 0.99d).Crafted, Is.True);
            Session afterNormal = Relaunch(s);
            AssertSameBalances(Snap(s), Snap(afterNormal), "after a normal craft");

            int slot = MiningGear.SlotCount - 1;
            Assert.That(s.Mining.TryTargetedCraftWithRoll(slot, 0.5d).Crafted, Is.True);
            Session afterTargeted = Relaunch(s);
            AssertSameBalances(Snap(s), Snap(afterTargeted), "after a targeted craft");
            for (int i = 0; i < MiningGear.SlotCount; i++)
                Assert.That(afterTargeted.Mining.WornGrade(i), Is.EqualTo(s.Mining.WornGrade(i)), "slot " + i);
        }

        /// <summary>PETS. A chest's pearls and its pets, and an essence grant, are each one write.</summary>
        [Test]
        public void PetChestsAndEssenceReachTheDiskBeforeTheyAreShown()
        {
            SaveData data = Populated();
            data.pearls = PetChest.Tuning.Default.PearlCost;
            Session s = BootNew(data);

            Assert.That(s.Pets.TryOpenChests(1), Is.Not.Null);
            Session afterChest = Relaunch(s);
            AssertSameBalances(Snap(s), Snap(afterChest), "after a chest");
            Assert.That(afterChest.Pets.ChestsOpened, Is.EqualTo(1));

            Assert.That(s.Pets.GrantPetEssence(2L), Is.True);
            AssertSameBalances(Snap(s), Snap(Relaunch(s)), "after an essence grant");
        }
    }
}
