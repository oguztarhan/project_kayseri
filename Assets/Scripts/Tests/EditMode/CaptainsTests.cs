using NUnit.Framework;
using Game.Core;

namespace Game.Tests
{
    /// <summary>
    /// The captain roster. The tests that matter most here are the last two: one asserts that nothing
    /// a captain does touches the payout stack Docs/VOYAGES.md §21 solved, and the other asserts that
    /// the best possible pair of officers still cannot make the far reach free.
    /// </summary>
    public class CaptainsTests
    {
        private static Captains.Tuning T => Captains.Tuning.Default;

        /// <summary>The first captain of a role at a given grade, or -1.</summary>
        private static int Find(int role, Captains.Grade grade)
        {
            for (int i = 0; i < Captains.Count; i++)
                if (Captains.RoleOf(i) == role && Captains.RankOf(i) == grade) return i;
            return -1;
        }

        // ---- the roster --------------------------------------------------------------------------

        [Test]
        public void RosterLengthMatchesCount()
        {
            Assert.That(Captains.Roster.Length, Is.EqualTo(Captains.Count));
        }

        [Test]
        public void EveryIdIsPresentLowercaseAndUnique()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < Captains.Count; i++)
            {
                string id = Captains.IdOf(i);
                Assert.That(id, Is.Not.Empty, "captain " + i);
                Assert.That(id, Is.EqualTo(id.ToLowerInvariant()), "id is also a loc key: " + id);
                Assert.That(seen.Add(id), Is.True, "duplicate id " + id);
            }
        }

        [Test]
        public void EveryGradeHasSomebody()
        {
            // CaptainCrate zeroes the weight of an empty grade, so an empty one is not a crash — it is
            // a silently unreachable rank, which is worse. This is what catches that.
            for (int g = 0; g < Captains.GradeCount; g++)
                Assert.That(Captains.CountOfGrade((Captains.Grade)g), Is.GreaterThan(0),
                            "nobody carries grade " + (Captains.Grade)g);
        }

        [Test]
        public void EveryRoleIsDrawable()
        {
            // Four roles across five captains. A role nobody carries is a role the odds sheet, the
            // dock's officer picker and the loc table all describe and no player can ever field.
            for (int role = 0; role < Captains.RoleCount; role++)
            {
                bool found = false;
                for (int i = 0; i < Captains.Count && !found; i++) found = Captains.RoleOf(i) == role;
                Assert.That(found, Is.True, "nobody does role " + role);
            }
        }

        [Test]
        public void EachGradeIsExactlyOneCaptain()
        {
            // Five captains at five grades is what makes the crate's rarity readout and the card you
            // actually get the same fact. A grade carrying two would put a second roll inside the
            // first and quietly halve the odds the sheet prints for each of them.
            Assert.That(Captains.Count, Is.EqualTo(Captains.GradeCount));
            for (int g = 0; g < Captains.GradeCount; g++)
                Assert.That(Captains.CountOfGrade((Captains.Grade)g), Is.EqualTo(1),
                            "grade " + (Captains.Grade)g);
        }

        [Test]
        public void OfGradeWalksEverySomebodyExactlyOnce()
        {
            for (int g = 0; g < Captains.GradeCount; g++)
            {
                var grade = (Captains.Grade)g;
                int n = Captains.CountOfGrade(grade);
                var seen = new System.Collections.Generic.HashSet<int>();
                for (int nth = 0; nth < n; nth++)
                {
                    int c = Captains.OfGrade(grade, nth);
                    Assert.That(Captains.Exists(c), Is.True);
                    Assert.That(Captains.RankOf(c), Is.EqualTo(grade));
                    Assert.That(seen.Add(c), Is.True);
                }
                Assert.That(Captains.OfGrade(grade, n), Is.EqualTo(-1), "past the end must be -1");
                Assert.That(Captains.OfGrade(grade, -1), Is.EqualTo(-1));
            }
        }

        [Test]
        public void OffRosterIndicesAreInert()
        {
            foreach (int bad in new[] { -1, Captains.Count, Captains.Count + 50 })
            {
                Assert.That(Captains.Exists(bad), Is.False);
                Assert.That(Captains.IdOf(bad), Is.Empty);
                Assert.That(Captains.ChartMultiplier(bad, 10, T), Is.EqualTo(1d).Within(1e-9));
                Assert.That(Captains.SalvageMultiplier(bad, 10, T), Is.EqualTo(1d).Within(1e-9));
                Assert.That(Captains.RiskReduction(bad, 10, T), Is.Zero);
                Assert.That(Captains.RepairMultiplier(bad, 10, T), Is.EqualTo(1d).Within(1e-9));
                Assert.That(Captains.DirectedShare(bad, 10, T), Is.Zero);
            }
        }

        // ---- levels ------------------------------------------------------------------------------

        [Test]
        public void MaxingACommonCostsEightyDuplicates()
        {
            // Four star-ups at 8, 16, 24 and 32. It was 90 over nine levels; the ladder is shorter and
            // each rung costs more, so the road is the same length it was measured at — "months of
            // duplicates, not a weekend" still holds.
            int common = Captains.OfGrade(Captains.Grade.Common, 0);
            Assert.That(Captains.DuplicatesToMax(common, T), Is.EqualTo(80));
            Assert.That(Captains.DuplicatesToLevel(common, 1, T), Is.EqualTo(8));
        }

        [Test]
        public void ARarerCaptainNeedsFewerCopies()
        {
            // The opposite of the obvious answer, and the only one that works — see
            // Captains.Tuning.DupScaleCommon for the measurement that forced it. A flat curve put the
            // single Mythic at 370 days, which is not a long tail but an unreachable one.
            var grades = new[] { Captains.Grade.Common, Captains.Grade.Rare, Captains.Grade.Epic,
                                 Captains.Grade.Legendary, Captains.Grade.Mythic };
            int last = int.MaxValue;
            foreach (var g in grades)
            {
                int total = Captains.DuplicatesToMax(Captains.OfGrade(g, 0), T);
                Assert.That(total, Is.LessThan(last), "grade " + g + " does not cost fewer copies than the one below");
                Assert.That(total, Is.GreaterThan(0));
                last = total;
            }
        }

        [Test]
        public void DuplicateCostRisesAndStopsAtTheCeiling()
        {
            for (int c = 0; c < Captains.Count; c++)
            {
                for (int l = 1; l < Captains.MaxLevel - 1; l++)
                    Assert.That(Captains.DuplicatesToLevel(c, l + 1, T),
                                Is.GreaterThanOrEqualTo(Captains.DuplicatesToLevel(c, l, T)),
                                "captain " + c + " level " + l);

                Assert.That(Captains.DuplicatesToLevel(c, Captains.MaxLevel, T), Is.Zero);
                Assert.That(Captains.DuplicatesToLevel(c, Captains.NotOwned, T), Is.Zero,
                            "a captain you do not have has no next level to buy");
                Assert.That(Captains.DuplicatesToLevel(c, -5, T), Is.Zero);
            }
        }

        [Test]
        public void ALevelNeverCostsNothing()
        {
            // The Mythic scale is small enough that rounding could reach zero — a level that costs
            // nothing would let one pull carry a captain straight up the ladder.
            for (int c = 0; c < Captains.Count; c++)
                for (int l = 1; l < Captains.MaxLevel; l++)
                    Assert.That(Captains.DuplicatesToLevel(c, l, T), Is.GreaterThanOrEqualTo(1),
                                "captain " + c + " level " + l);
        }

        [Test]
        public void OwnedCountHandlesNullAndShortArrays()
        {
            Assert.That(Captains.OwnedCount(null), Is.Zero);
            Assert.That(Captains.OwnedCount(new int[0]), Is.Zero);
            Assert.That(Captains.OwnedCount(new[] { 1, 0, 3 }), Is.EqualTo(2));

            var full = Captains.NewLevels();
            for (int i = 0; i < full.Length; i++) full[i] = 1;
            Assert.That(Captains.OwnedCount(full), Is.EqualTo(Captains.Count));
        }

        [Test]
        public void IncomeCurveMatchesEveryConfiguredRarityAtMaximumLevel()
        {
            var expected = new[] { 5d, 17.5d, 61.28d, 214.52d, 751d };
            for (int grade = 0; grade < Captains.GradeCount; grade++)
            {
                int captain = Captains.OfGrade((Captains.Grade)grade, 0);
                Assert.That(Captains.IncomeMultiplier(captain, Captains.MaxLevel, T),
                            Is.EqualTo(expected[grade]).Within(1e-9), "grade " + (Captains.Grade)grade);
            }
        }

        [Test]
        public void IncomeCurveIsOneWhenUnownedAndIncreasesWithLevel()
        {
            for (int captain = 0; captain < Captains.Count; captain++)
            {
                Assert.That(Captains.IncomeMultiplier(captain, Captains.NotOwned, T), Is.EqualTo(1d));
                double previous = 1d;
                for (int level = 1; level <= Captains.MaxLevel; level++)
                {
                    double current = Captains.IncomeMultiplier(captain, level, T);
                    Assert.That(current, Is.GreaterThan(previous), "captain " + captain + " level " + level);
                    previous = current;
                }
            }
        }

        [Test]
        public void IncomePercentFormattingGroupsLargeBonusesWithoutOverflow()
        {
            Assert.That(Captains.IncomePercent(400d), Is.EqualTo("400"));
            Assert.That(Captains.IncomePercent(21352d), Is.EqualTo("21,352"));
            Assert.That(Captains.IncomePercent(75000d), Is.EqualTo("75,000"));
        }

        [Test]
        public void BestOwnedUsesHighestLevelAndRosterOrderForTies()
        {
            Assert.That(Captains.BestOwned(null), Is.EqualTo(-1));
            Assert.That(Captains.BestOwned(new[] { 0, 0, 0 }), Is.EqualTo(-1));
            Assert.That(Captains.BestOwned(new[] { 2, 5, 5, 1, 0 }), Is.EqualTo(1));
            Assert.That(Captains.BestOwned(new[] { 4, 0, 4, 0, 0 }), Is.EqualTo(0));
        }

        // ---- effects -----------------------------------------------------------------------------

        [Test]
        public void ACaptainYouDoNotOwnDoesNothing()
        {
            for (int i = 0; i < Captains.Count; i++)
            {
                Assert.That(Captains.ChartMultiplier(i, Captains.NotOwned, T), Is.EqualTo(1d).Within(1e-9));
                Assert.That(Captains.SalvageMultiplier(i, Captains.NotOwned, T), Is.EqualTo(1d).Within(1e-9));
                Assert.That(Captains.RiskReduction(i, Captains.NotOwned, T), Is.Zero);
                Assert.That(Captains.RepairMultiplier(i, Captains.NotOwned, T), Is.EqualTo(1d).Within(1e-9));
                Assert.That(Captains.DirectedShare(i, Captains.NotOwned, T), Is.Zero);
            }
        }

        [Test]
        public void EachRoleMovesItsOwnNumberAndNoOtherRoles()
        {
            for (int i = 0; i < Captains.Count; i++)
            {
                int role = Captains.RoleOf(i);
                bool charts  = Captains.ChartMultiplier(i, 5, T)   > 1d;
                bool salvage = Captains.SalvageMultiplier(i, 5, T) > 1d;
                bool risk    = Captains.RiskReduction(i, 5, T)     > 0d;
                bool repair  = Captains.RepairMultiplier(i, 5, T)  < 1d;
                bool aimed   = Captains.DirectedShare(i, 5, T)     > 0d;

                Assert.That(charts,  Is.EqualTo(role == Captains.Quartermaster), "charts, captain " + i);
                Assert.That(salvage, Is.EqualTo(role == Captains.Gunner),        "salvage, captain " + i);
                Assert.That(risk,    Is.EqualTo(role == Captains.Bosun),         "risk, captain " + i);
                Assert.That(repair,  Is.EqualTo(role == Captains.Bosun),         "repair, captain " + i);
                Assert.That(aimed,   Is.EqualTo(role == Captains.Purser),        "aim, captain " + i);
            }
        }

        [Test]
        public void RarerIsStrongerAtTheSameLevel()
        {
            var grades = new[] { Captains.Grade.Common, Captains.Grade.Rare, Captains.Grade.Epic,
                                 Captains.Grade.Legendary, Captains.Grade.Mythic };
            double last = 0d;
            foreach (var g in grades)
            {
                int c = Captains.OfGrade(g, 0);
                double worth = Captains.PerLevel(c, T);
                Assert.That(worth, Is.GreaterThan(last), "grade " + g + " is not worth more than the one below");
                last = worth;
            }
        }

        [Test]
        public void RepairIsShortenedButNeverErased()
        {
            int bosun = Find(Captains.Bosun, Captains.Grade.Mythic);
            Assert.That(bosun, Is.Not.EqualTo(-1));

            double prev = 1d;
            for (int level = 1; level <= Captains.MaxLevel; level++)
            {
                double m = Captains.RepairMultiplier(bosun, level, T);
                Assert.That(m, Is.InRange(T.MinRepairFraction, 1d), "level " + level);
                Assert.That(m, Is.LessThanOrEqualTo(prev), "a level must never lengthen the repair");
                prev = m;
            }
            Assert.That(Captains.RepairMultiplier(bosun, Captains.MaxLevel, T),
                        Is.GreaterThanOrEqualTo(T.MinRepairFraction),
                        "a failure with no cost is not a failure — the berth is where it is felt");
        }

        [Test]
        public void DirectedShareNeverExceedsEveryCard()
        {
            for (int i = 0; i < Captains.Count; i++)
                for (int level = 0; level <= Captains.MaxLevel; level++)
                    Assert.That(Captains.DirectedShare(i, level, T), Is.InRange(0d, 1d),
                                "captain " + i + " level " + level);
        }

        // ---- the two that guard the balance ------------------------------------------------------

        [Test]
        public void NothingCaptainsOwnIsAnArgumentToTheCardPayout()
        {
            // Docs/VOYAGES.md §21: the first defaults were wrong by ~2.5x because of "a multiplicative
            // stack — tier payout x hold x crew — where each factor was defensible alone and the
            // product was not." Those numbers were then re-solved against four constraints at once.
            //
            // So Cards() must keep exactly the arguments it had. This asserts the SIGNATURE rather
            // than a value, because a value test cannot tell the difference between "no captain factor
            // exists" and "the captain factor happened to be 1 here". If a future role wants to pay
            // cards, this is the test it has to argue with first.
            var m = typeof(Voyages).GetMethod("Cards");
            Assert.That(m, Is.Not.Null);

            var names = System.Array.ConvertAll(m.GetParameters(), p => p.Name);
            Assert.That(names, Is.EqualTo(new[] { "tier", "loadFraction", "holdLevel", "crewLevel", "t" }),
                        "Voyages.Cards gained or lost an argument — the §21 balance solve assumed these five");

            var f = typeof(Voyages).GetMethod("CardsOnFailure");
            Assert.That(f, Is.Not.Null);
            Assert.That(System.Array.ConvertAll(f.GetParameters(), p => p.Name),
                        Is.EqualTo(new[] { "tier", "loadFraction", "holdLevel", "crewLevel", "t" }));
        }

        [Test]
        public void TheBestPairOfOfficersStillLeavesTheFarReachARisk()
        {
            // A bosun's reduction STACKS with the foreman's. What keeps that safe is the size of the
            // numbers, and this is the test that holds them there. Docs/VOYAGES.md §10 refuses to SELL
            // guaranteed success; this refuses to let it be collected either.
            var vt = Voyages.Tuning.Default;
            int mythicBosun = Find(Captains.Bosun, Captains.Grade.Mythic);
            double cut = Captains.RiskReduction(mythicBosun, Captains.MaxLevel, T);

            double risk = Voyages.RiskFor(Voyages.TierCount - 1, Foremen.MaxStars, cut, vt);
            Assert.That(risk, Is.GreaterThan(0d),
                        "a maxed foreman beside a maxed Mythic bosun erases the far reach's risk");
        }

        [Test]
        public void TheRiskOverloadMatchesTheOriginalWhenNobodyElseIsAboard()
        {
            var vt = Voyages.Tuning.Default;
            for (int tier = 0; tier < Voyages.TierCount; tier++)
                for (int fl = 0; fl <= Foremen.MaxStars; fl++)
                    Assert.That(Voyages.RiskFor(tier, fl, 0d, vt),
                                Is.EqualTo(Voyages.RiskFor(tier, fl, vt)).Within(1e-12),
                                "tier " + tier + " foreman " + fl);
        }
    }
}
