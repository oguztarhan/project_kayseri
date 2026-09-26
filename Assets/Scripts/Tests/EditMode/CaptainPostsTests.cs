using NUnit.Framework;
using Game.Core;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>
    /// The four captain posts: the best owned captain of each role does that role's job, all four at
    /// once. Quartermaster and gunner lift a won fight's charts and salvage, the bosun may hand a lost
    /// fight's energy back, and the purser aims random foreman cards at the one furthest behind.
    /// </summary>
    public class CaptainPostsTests
    {
        private const string Coal = "coal";
        private static Captains.Tuning T => Captains.Tuning.Default;
        private static SeaCombat.Tuning Sea => SeaCombat.Tuning.Default;

        private static int Find(int role, Captains.Grade grade)
        {
            for (int i = 0; i < Captains.Count; i++)
                if (Captains.RoleOf(i) == role && Captains.RankOf(i) == grade) return i;
            return -1;
        }

        // ---- who holds a post ---------------------------------------------------------------------

        [Test]
        public void NobodyOfARoleLeavesThePostEmpty()
        {
            var levels = new int[Captains.Count];
            Assert.That(Captains.BestForRole(Captains.Quartermaster, levels, T), Is.EqualTo(-1));
            Assert.That(Captains.BestForRole(Captains.Quartermaster, null, T), Is.EqualTo(-1));
        }

        [Test]
        public void ThePostGoesToTheLargestAbilityNotTheRarestCard()
        {
            int common = Find(Captains.Quartermaster, Captains.Grade.Common);
            int rare = Find(Captains.Quartermaster, Captains.Grade.Rare);
            var levels = new int[Captains.Count];
            levels[common] = Captains.MaxLevel;   // 5 x 0.08 = +40%
            levels[rare] = 1;                     // 1 x 0.12 = +12%

            Assert.That(Captains.BestForRole(Captains.Quartermaster, levels, T), Is.EqualTo(common));

            levels[rare] = 4;                     // 4 x 0.12 = +48%
            Assert.That(Captains.BestForRole(Captains.Quartermaster, levels, T), Is.EqualTo(rare));
        }

        [Test]
        public void EachRoleFillsItsOwnPost()
        {
            var levels = new int[Captains.Count];
            for (int role = 0; role < Captains.RoleCount; role++)
                levels[Find(role, Captains.Grade.Common) >= 0 ? Find(role, Captains.Grade.Common)
                                                               : Find(role, Captains.Grade.Rare)] = 1;

            for (int role = 0; role < Captains.RoleCount; role++)
            {
                int officer = Captains.BestForRole(role, levels, T);
                Assert.That(officer, Is.GreaterThanOrEqualTo(0), "role " + role);
                Assert.That(Captains.RoleOf(officer), Is.EqualTo(role));
            }
        }

        // ---- the bosun's refund chance -------------------------------------------------------------

        [Test]
        public void OnlyABosunRefundsAndTheMythicIsHeldToTheCap()
        {
            int gunner = Find(Captains.Gunner, Captains.Grade.Common);
            Assert.That(Captains.LossRefundChance(gunner, Captains.MaxLevel, T), Is.Zero);

            int common = Find(Captains.Bosun, Captains.Grade.Common);
            Assert.That(Captains.LossRefundChance(common, Captains.MaxLevel, T),
                        Is.EqualTo(0.35d * 0.08d * 5d).Within(1e-9));

            int mythic = Find(Captains.Bosun, Captains.Grade.Mythic);
            Assert.That(Captains.LossRefundChance(mythic, Captains.MaxLevel, T), Is.EqualTo(0.5d).Within(1e-9));
            Assert.That(Captains.LossRefundChance(mythic, Captains.NotOwned, T), Is.Zero);
        }

        // ---- the sea -------------------------------------------------------------------------------

        private static ExpeditionService AtSea(SaveData data, out CaptainService captains)
        {
            captains = new CaptainService(data, T, CaptainCrate.Tuning.Default);
            var sea = new ExpeditionService(new TimeService(), data, captains, Sea);
            sea.SetSail(Coal);
            return sea;
        }

        [Test]
        public void AQuartermasterLiftsAWonFightsCharts()
        {
            var data = new SaveData();
            ExpeditionService sea = AtSea(data, out CaptainService captains);
            int qm = Find(Captains.Quartermaster, Captains.Grade.Common);
            data.captainLevels[qm] = Captains.MaxLevel;

            Assert.That(captains.PostChartMultiplier, Is.EqualTo(1.4d).Within(1e-9));
            sea.RegisterKill(100, 0);
            Assert.That(sea.LastKillCharts, Is.EqualTo(140L));
        }

        [Test]
        public void AGunnerLiftsAWonFightsSalvage()
        {
            var data = new SaveData();
            ExpeditionService sea = AtSea(data, out CaptainService captains);
            int gunner = Find(Captains.Gunner, Captains.Grade.Common);
            data.captainLevels[gunner] = Captains.MaxLevel;

            sea.RegisterKill(0, 200);
            Assert.That(sea.LastKillSalvage, Is.EqualTo(280L));
            Assert.That(sea.LastKillCharts, Is.Zero, "a gunner does not touch charts");
        }

        [Test]
        public void WithoutABosunALossNeverRefunds()
        {
            var data = new SaveData();
            ExpeditionService sea = AtSea(data, out _);
            data.seaEnergy = 0;
            data.seaEnergyStampUnix = new TimeService().NowUnix();

            for (int i = 0; i < 50; i++) Assert.That(sea.TryRefundLoss(), Is.False);
            Assert.That(sea.Energy, Is.Zero);
        }

        [Test]
        public void AMaxedMythicBosunRefundsAboutHalfOfLosses()
        {
            var data = new SaveData();
            ExpeditionService sea = AtSea(data, out _);
            data.captainLevels[Find(Captains.Bosun, Captains.Grade.Mythic)] = Captains.MaxLevel;
            long now = new TimeService().NowUnix();

            const int Trials = 2000;
            int refunded = 0;
            for (int i = 0; i < Trials; i++)
            {
                data.seaEnergy = 0;
                data.seaEnergyStampUnix = now;
                if (sea.TryRefundLoss())
                {
                    refunded++;
                    Assert.That(sea.Energy, Is.EqualTo(1), "one fight's energy, no more");
                }
            }
            // p = 0.5 over 2000 trials: the standard deviation is ~1.1%, so this band is ~7 of them.
            Assert.That(refunded / (double)Trials, Is.InRange(0.42d, 0.58d));
        }

        [Test]
        public void ARefundNeedsRoomInThePool()
        {
            var data = new SaveData();
            ExpeditionService sea = AtSea(data, out _);
            data.captainLevels[Find(Captains.Bosun, Captains.Grade.Mythic)] = Captains.MaxLevel;
            data.seaEnergy = sea.EnergyMax;
            data.seaEnergyStampUnix = new TimeService().NowUnix();

            for (int i = 0; i < 50; i++) Assert.That(sea.TryRefundLoss(), Is.False);
        }

        // ---- the purser ----------------------------------------------------------------------------

        [Test]
        public void AMaxedPurserAimsEveryRandomCardAtTheLaggard()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var foremen = new ForemanService(data, wallet, Foremen.Tuning.Default, MasterChest.Tuning.Default);
            int laggard = Foremen.IndexOf(Foremen.Mine, Foremen.Rarity.Common);
            for (int m = 0; m < Foremen.Count; m++) data.masterStars[m] = 3;
            data.masterStars[laggard] = 1;

            var captains = new CaptainService(data, T, CaptainCrate.Tuning.Default);
            data.captainLevels[Find(Captains.Purser, Captains.Grade.Legendary)] = Captains.MaxLevel;
            foremen.Captains = captains;
            Assert.That(captains.PostDirectedShare, Is.EqualTo(1d).Within(1e-9));

            for (int i = 0; i < 20; i++) Assert.That(foremen.GrantRandomDuplicates(1), Is.EqualTo(laggard));
        }

        [Test]
        public void WithoutAPurserRandomCardsStayRandom()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var foremen = new ForemanService(data, wallet, Foremen.Tuning.Default, MasterChest.Tuning.Default);
            int laggard = Foremen.IndexOf(Foremen.Mine, Foremen.Rarity.Common);
            for (int m = 0; m < Foremen.Count; m++) data.masterStars[m] = 3;
            data.masterStars[laggard] = 1;
            foremen.Captains = new CaptainService(data, T, CaptainCrate.Tuning.Default);

            bool elsewhere = false;
            for (int i = 0; i < 200 && !elsewhere; i++) elsewhere = foremen.GrantRandomDuplicates(1) != laggard;
            Assert.That(elsewhere, Is.True, "an empty post leaves the roll alone");
        }
    }
}
