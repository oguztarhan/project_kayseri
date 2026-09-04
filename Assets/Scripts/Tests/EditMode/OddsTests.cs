using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// The odds sheet states a probability to the player, and both platforms hold us to it. So the
    /// test that matters is not "does the arithmetic look right" — it is "does the number the screen
    /// prints equal the number the game actually rolls", measured against the real roll.
    ///
    /// The sampling is STRATIFIED, not random: <see cref="CaptainCrate.RollGrade"/> and
    /// <see cref="MasterChest.RollSlot"/> are pure lookups over [0,1), so walking that interval in
    /// even steps measures the true frequency to within one step. A seeded RNG would only add noise
    /// and force a looser bound.
    /// </summary>
    public sealed class OddsTests
    {
        private const int Samples = 200000;
        /// <summary>One step of the walk, so a frequency can never be closer than this anyway.</summary>
        private const double Tolerance = 1.5d / Samples;

        private static double Sample(int i) => (i + 0.5d) / Samples;

        // ------------------------------------------------------------------ captain crate
        [Test]
        public void EveryPrintedCaptainChanceMatchesWhatTheCrateActuallyRolls()
        {
            CaptainCrate.Tuning t = CaptainCrate.Tuning.Default;
            var hits = new int[Captains.GradeCount];

            for (int i = 0; i < Samples; i++)
                hits[(int)CaptainCrate.RollGrade(Sample(i), 0, 0, t)]++;

            for (int grade = 0; grade < Captains.GradeCount; grade++)
            {
                double measured = (double)hits[grade] / Samples;
                Assert.That(Odds.CaptainGradeChance(grade, t), Is.EqualTo(measured).Within(Tolerance),
                            "the sheet would state a rate the crate does not roll, grade " + grade);
            }
        }

        [Test]
        public void TheCaptainTableSumsToOne()
        {
            CaptainCrate.Tuning t = CaptainCrate.Tuning.Default;
            double total = 0d;
            for (int grade = 0; grade < Captains.GradeCount; grade++)
                total += Odds.CaptainGradeChance(grade, t);
            Assert.That(total, Is.EqualTo(1d).Within(1e-9));
        }

        /// <summary>
        /// With a guarantee owed, the grades under it cannot come out at all — and the sheet has to
        /// agree, because the pull the player is about to make is the one being described.
        /// </summary>
        [Test]
        public void AnOwedGuaranteeClosesTheGradesBeneathIt()
        {
            CaptainCrate.Tuning t = CaptainCrate.Tuning.Default;
            int sinceEpic = t.EpicPity - 1;   // the very next pull is the guaranteed one

            var hits = new int[Captains.GradeCount];
            for (int i = 0; i < Samples; i++)
                hits[(int)CaptainCrate.RollGrade(Sample(i), sinceEpic, 0, t)]++;

            Assert.That(hits[(int)Captains.Grade.Common], Is.Zero);
            Assert.That(hits[(int)Captains.Grade.Rare], Is.Zero);

            for (int grade = 0; grade < Captains.GradeCount; grade++)
                Assert.That(Odds.CaptainGradeChance(grade, sinceEpic, 0, t),
                            Is.EqualTo((double)hits[grade] / Samples).Within(Tolerance),
                            "grade " + grade + " under an owed Epic guarantee");
        }

        [Test]
        public void TheSoftPityRampRaisesTheLegendaryChanceItAdvertises()
        {
            CaptainCrate.Tuning t = CaptainCrate.Tuning.Default;
            const int grade = (int)Captains.Grade.Legendary;

            double cold = Odds.CaptainGradeChance(grade, 0, 0, t);
            double warm = Odds.CaptainGradeChance(grade, 0, t.SoftPityStart + 10, t);
            Assert.That(warm, Is.GreaterThan(cold));

            // And the warmed number is still the one the crate rolls, which is the half that would
            // quietly stop being true if the ramp were ever folded in by hand.
            var hits = new int[Captains.GradeCount];
            for (int i = 0; i < Samples; i++)
                hits[(int)CaptainCrate.RollGrade(Sample(i), 0, t.SoftPityStart + 10, t)]++;
            Assert.That(warm, Is.EqualTo((double)hits[grade] / Samples).Within(Tolerance));
        }

        // ------------------------------------------------------------------ master chest
        [Test]
        public void EveryMasterIsAsLikelyAsTheSheetSays()
        {
            var hits = new int[Foremen.Count];
            for (int i = 0; i < Samples; i++) hits[MasterChest.RollSlot(Sample(i))]++;

            double stated = Odds.MasterSlotChance();
            for (int slot = 0; slot < Foremen.Count; slot++)
                Assert.That((double)hits[slot] / Samples, Is.EqualTo(stated).Within(Tolerance),
                            "master " + slot);
        }

        /// <summary>
        /// The directed card is not chance and must never be counted as one — a chest of three with
        /// one aimed card is two rolls, not three, and saying otherwise overstates the randomness.
        /// </summary>
        [Test]
        public void TheAimedCardIsNotCountedAmongTheRolledOnes()
        {
            MasterChest.Tuning t = MasterChest.Tuning.Default;
            Assert.That(Odds.MasterRolledCards(t),
                        Is.EqualTo(MasterChest.CardsFor(1, t) - MasterChest.DirectedIn(t)));
            Assert.That(Odds.MasterRolledCards(t), Is.LessThan(MasterChest.CardsFor(1, t)),
                        "the default chest aims one card, so not every card is a roll");
        }

        /// <summary>A misconfigured chest that aims more cards than it holds cannot report a negative
        /// number of rolls — <see cref="MasterChest.DirectedIn"/> already clamps, and this is what
        /// keeps the sheet from printing "-2 cards rolled at random".</summary>
        [Test]
        public void AChestThatAimsEverythingRollsNothing()
        {
            MasterChest.Tuning t = MasterChest.Tuning.Default;
            t.DirectedPerChest = t.CardsPerChest + 5;
            Assert.That(Odds.MasterRolledCards(t), Is.Zero);
        }
    }
}
