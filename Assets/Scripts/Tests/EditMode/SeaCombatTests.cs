using NUnit.Framework;
using Game.Core;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>
    /// The sea adventure. The groups that matter most: the exchange LADDER — each rung falls only
    /// to the gear the rung below drops, which is the whole grind, and with NO ability buttons
    /// left there is nothing else it could fall to; the PROCS, each pinned through the
    /// rolls-as-arguments door so every fight is replayable; the ENERGY maths, the governor on all
    /// of it; and the GEAR rules, which keep every item's power inside the fight and out of the
    /// economy.
    ///
    /// The ladder is pinned PROC-FREE (every roll 1, so nothing procs, and Common items carry no
    /// secondary at all) — the deterministic skeleton under the noise. Live fights sit on both
    /// sides of it: enemy signatures push down, our secondaries push up.
    /// </summary>
    public class SeaCombatTests
    {
        private static SeaCombat.Tuning T => SeaCombat.Tuning.Default;
        private static Captains.Tuning CT => Captains.Tuning.Default;

        private static SeaCombat.Stats Bare => SeaCombat.OurStats(-1, 0, 0, null, CT, T);

        private static SeaCombat.Item[] Loadout(int tier, int grade)
        {
            var gear = new SeaCombat.Item[SeaCombat.SlotCount];
            for (int slot = 0; slot < SeaCombat.SlotCount; slot++)
                gear[slot] = SeaCombat.ItemFor(slot, tier, grade, 0.3d, T);
            return gear;
        }

        /// <summary>A full set of that route's COMMONS — the honest rung measure, because a Common
        /// carries no secondary and so cannot smuggle a proc into a proc-free pin.</summary>
        private static SeaCombat.Stats Commons(int tier)
            => SeaCombat.OurStats(-1, 0, 0, Loadout(tier, (int)Captains.Grade.Common), CT, T);

        /// <summary>
        /// A whole exchange with the dice removed (rolls of 1 proc nothing). SÜRAT picks who opens,
        /// exactly as <see cref="SeaCombat.Begin"/> settled it, and the sides then alternate — the
        /// same order <see cref="Game.Gameplay.EncounterController"/> steps through.
        /// </summary>
        private static SeaCombat.Fight Exchange(int tier, int kind, SeaCombat.Stats ours)
        {
            SeaCombat.Fight f = SeaCombat.Begin(tier, kind, ours, T);
            SeaCombat.ShotRolls none = SeaCombat.ShotRolls.None;
            bool our = f.UsOpen;
            for (int step = 0; step < 2000 && !f.Over; step++)
            {
                SeaCombat.TurnStart(ref f, our, T);
                if (f.Over) break;
                if (our)
                {
                    if (f.Us.Stunned) SeaCombat.TurnSkipped(ref f, true);
                    else SeaCombat.ShotLands(ref f, true, none, T);
                }
                else if (!SeaCombat.EnemyWillFire(f)) SeaCombat.TurnSkipped(ref f, false);
                else SeaCombat.ShotLands(ref f, false, none, T);
                our = !our;
            }
            Assert.That(f.Over, Is.True, "an exchange must always end");
            return f;
        }

        // ---- the ladder --------------------------------------------------------------------------

        [Test]
        public void TierZeroFallsToWatchingWhateverPopsUp()
        {
            for (int kind = 0; kind < SeaCombat.KindCount; kind++)
                Assert.That(Exchange(0, kind, Bare).Won, Is.True,
                            "kind " + kind + " beat a bare, untended ship on tier 0");
        }

        [Test]
        public void EveryRungAboveTheFirstNeedsTheRungBelowsGear()
        {
            // The whole economy in one table: what you are wearing is the ONLY thing that decides
            // the rung, now that there are no buttons to lean on. Each row is "this route refuses
            // the gear from two routes back, and yields to the gear from one."
            Assert.That(Exchange(1, SeaCombat.Raider, Bare).Won, Is.False,
                        "tier 1 fell to a bare ship — tier 0's drops have nothing to sell");
            Assert.That(Exchange(1, SeaCombat.Raider, Commons(0)).Won, Is.True,
                        "tier 0's own commons do not clear tier 1 — the ladder has a gap");

            Assert.That(Exchange(2, SeaCombat.Raider, Commons(0)).Won, Is.False,
                        "tier 2 fell to tier-0 gear; the middle of the grind is decoration");
            Assert.That(Exchange(2, SeaCombat.Raider, Commons(1)).Won, Is.True,
                        "tier 1's commons do not clear tier 2 — the ladder has a gap");

            Assert.That(Exchange(3, SeaCombat.Raider, Commons(2)).Won, Is.False,
                        "the far reach fell to tier-2 gear; the last rung buys nothing");
            Assert.That(Exchange(3, SeaCombat.Raider, Commons(3)).Won, Is.True,
                        "tier-3 gear does not clear tier 3 — the ladder has no top");
        }

        [Test]
        public void TheOpeningBallIsBoughtWithSurat()
        {
            // SÜRAT's whole meaning, and the reason the spyglass is not just a luck stat: a bare
            // ship is second to fire on every real route, and one set of commons buys the ball back.
            Assert.That(SeaCombat.Begin(0, SeaCombat.Raider, Bare, T).UsOpen, Is.True,
                        "the home route must not open against a starting ship");
            for (int tier = 1; tier < Voyages.TierCount; tier++)
            {
                Assert.That(SeaCombat.Begin(tier, SeaCombat.Raider, Bare, T).UsOpen, Is.False,
                            "tier " + tier + ": a bare ship should be outrun");
                Assert.That(SeaCombat.Begin(tier, SeaCombat.Raider, Commons(tier), T).UsOpen, Is.True,
                            "tier " + tier + ": that route's own gear must win the opening ball");
            }
        }

        [Test]
        public void TheDerelictNeverAnswers()
        {
            SeaCombat.Fight f = SeaCombat.Begin(2, SeaCombat.Derelict, Bare, T);
            Assert.That(SeaCombat.EnemyWillFire(f), Is.False);
            SeaCombat.Fight done = Exchange(3, SeaCombat.Derelict, Bare);
            Assert.That(done.Won, Is.True, "a fight nobody shoots back in can only be won");
            Assert.That(done.Us.Hull, Is.EqualTo(done.Us.HullMax), "and it must cost nothing to win");
        }

        // ---- the exchange ------------------------------------------------------------------------

        [Test]
        public void DamageIsPerShotAndSavunmaComesOffEveryBall()
        {
            SeaCombat.Stats them = SeaCombat.ThreatStats(1, SeaCombat.Raider, T);
            SeaCombat.Fight f = SeaCombat.Begin(1, SeaCombat.Raider, Bare, T);
            double hull = f.Them.Hull;
            SeaCombat.ShotLands(ref f, true, SeaCombat.ShotRolls.None, T);
            Assert.That(hull - f.Them.Hull, Is.EqualTo(T.PlayerShotBase - them.Def).Within(0.05d),
                        "their SAVUNMA must come off our ball");

            double nerve = f.Us.Hull;
            SeaCombat.ShotLands(ref f, false, SeaCombat.ShotRolls.None, T);
            Assert.That(nerve - f.Us.Hull, Is.EqualTo(them.Shot).Within(0.05d),
                        "a bare ship has no SAVUNMA to shave theirs with");
        }

        [Test]
        public void SavunmaShavesTheBallButCanNeverEatItWhole()
        {
            // The anti-stalemate rule. Without the floor, a defence stack taller than the other
            // side's TOP would make the fight unwinnable in both directions and simply never end.
            SeaCombat.Stats them = SeaCombat.ThreatStats(3, SeaCombat.Derelict, T);
            Assert.That(them.Def, Is.GreaterThan(T.PlayerShotBase),
                        "the premise: this hulk out-armours a bare ship's whole shot");

            SeaCombat.Fight f = SeaCombat.Begin(3, SeaCombat.Derelict, Bare, T);
            double hull = f.Them.Hull;
            SeaCombat.ShotLands(ref f, true, SeaCombat.ShotRolls.None, T);
            Assert.That(hull - f.Them.Hull,
                        Is.EqualTo(T.PlayerShotBase * T.MinShotFrac).Within(0.05d),
                        "a ball must always land for at least its floor");
        }

        [Test]
        public void TheFasterSheetOpensAndATieGoesToUs()
        {
            SeaCombat.Stats quick = Bare; quick.Spd = 500d;
            Assert.That(SeaCombat.Begin(3, SeaCombat.Ghost, quick, T).UsOpen, Is.True);

            SeaCombat.Stats slow = Bare; slow.Spd = 0d;
            Assert.That(SeaCombat.Begin(3, SeaCombat.Ghost, slow, T).UsOpen, Is.False);

            SeaCombat.Stats tied = Bare;
            tied.Spd = SeaCombat.ThreatStats(2, SeaCombat.Raider, T).Spd;
            Assert.That(SeaCombat.Begin(2, SeaCombat.Raider, tied, T).UsOpen, Is.True,
                        "a dead heat belongs to the player");
        }

        [Test]
        public void AFinishedFightCannotBeMoved()
        {
            SeaCombat.Fight f = Exchange(0, SeaCombat.Raider, Bare);
            SeaCombat.Fight before = f;
            SeaCombat.ShotLands(ref f, true, SeaCombat.ShotRolls.None, T);
            SeaCombat.ShotLands(ref f, false, SeaCombat.ShotRolls.None, T);
            SeaCombat.TurnSkipped(ref f, false);
            SeaCombat.TurnStart(ref f, true, T);
            Assert.That(f.Them.Hull, Is.EqualTo(before.Them.Hull));
            Assert.That(f.Us.Hull, Is.EqualTo(before.Us.Hull));
        }

        // ---- the procs ---------------------------------------------------------------------------

        [Test]
        public void ACritMultipliesTheBallItLandsOn()
        {
            // Measured against the plain ball rather than against TOP, because SAVUNMA has already
            // come off by the time the crit multiplies anything.
            SeaCombat.Stats s = Bare; s.Crit = 0.5d;
            SeaCombat.Fight f = SeaCombat.Begin(3, SeaCombat.Raider, s, T);

            double hull = f.Them.Hull;
            SeaCombat.ShotLands(ref f, true, SeaCombat.ShotRolls.None, T);
            double plain = hull - f.Them.Hull;

            var rolls = SeaCombat.ShotRolls.None; rolls.Crit = 0d;
            hull = f.Them.Hull;
            SeaCombat.ShotReport r = SeaCombat.ShotLands(ref f, true, rolls, T);
            Assert.That(r.Crit, Is.True);
            Assert.That(hull - f.Them.Hull, Is.EqualTo(plain * T.CritMult).Within(0.06d));
        }

        [Test]
        public void ADodgedShotDoesNothingAtAll()
        {
            SeaCombat.Stats s = Bare; s.Dodge = 0.5d;
            SeaCombat.Fight f = SeaCombat.Begin(1, SeaCombat.Raider, s, T);
            var rolls = SeaCombat.ShotRolls.None;
            rolls.Dodge = 0d;
            // Every rider is armed: a voided ball must carry none of them through.
            rolls.Crit = 0d; rolls.Stun = 0d; rolls.Burn = 0d; rolls.Poison = 0d;
            double nerve = f.Us.Hull;
            SeaCombat.ShotReport r = SeaCombat.ShotLands(ref f, false, rolls, T);
            Assert.That(r.Dodged, Is.True);
            Assert.That(f.Us.Hull, Is.EqualTo(nerve), "a dodged ball must not bite");
            Assert.That(r.Damage, Is.Zero);
            Assert.That(f.Us.Stunned, Is.False, "nor stun");
            Assert.That(f.Us.BurnLeft + f.Us.PoisonLeft, Is.Zero, "nor set anything alight");
        }

        [Test]
        public void AStunProcStealsExactlyOneEnemyTurn()
        {
            SeaCombat.Stats s = Bare; s.Stun = 0.5d; s.Shot = 1d;
            SeaCombat.Fight f = SeaCombat.Begin(3, SeaCombat.Raider, s, T);
            var rolls = SeaCombat.ShotRolls.None; rolls.Stun = 0d;
            SeaCombat.ShotReport r = SeaCombat.ShotLands(ref f, true, rolls, T);
            Assert.That(r.StunProc, Is.True);
            Assert.That(SeaCombat.EnemyWillFire(f), Is.False);
            SeaCombat.TurnSkipped(ref f, false);
            Assert.That(SeaCombat.EnemyWillFire(f), Is.True);
        }

        [Test]
        public void ABeastCanStunUsOutOfAShot()
        {
            SeaCombat.Fight f = SeaCombat.Begin(1, SeaCombat.Beast, Bare, T);
            var rolls = SeaCombat.ShotRolls.None; rolls.Stun = 0d;
            SeaCombat.ShotReport r = SeaCombat.ShotLands(ref f, false, rolls, T);
            Assert.That(r.StunProc, Is.True, "the beast's signature must be able to land");
            Assert.That(f.Us.Stunned, Is.True);
            SeaCombat.TurnSkipped(ref f, true);
            Assert.That(f.Us.Stunned, Is.False, "one turn, exactly");
        }

        [Test]
        public void ABurnTicksItsTurnsThenStops()
        {
            SeaCombat.Stats s = Bare; s.Burn = 0.5d; s.Shot = 1d;
            SeaCombat.Fight f = SeaCombat.Begin(3, SeaCombat.Raider, s, T);
            var rolls = SeaCombat.ShotRolls.None; rolls.Burn = 0d;
            Assert.That(SeaCombat.ShotLands(ref f, true, rolls, T).BurnProc, Is.True);
            Assert.That(f.Them.BurnLeft, Is.EqualTo(T.BurnTurns));

            double burned = 0d;
            for (int i = 0; i < T.BurnTurns; i++)
            {
                SeaCombat.TurnReport tick = SeaCombat.TurnStart(ref f, false, T);
                Assert.That(tick.BurnDamage, Is.GreaterThan(0d), "tick " + i);
                burned += tick.BurnDamage;
            }
            Assert.That(SeaCombat.TurnStart(ref f, false, T).BurnDamage, Is.Zero, "the fire goes out");
            Assert.That(burned, Is.EqualTo(f.Them.HullMax * T.BurnFrac * T.BurnTurns).Within(0.5d));
        }

        [Test]
        public void ABurnCanFinishAFight()
        {
            SeaCombat.Stats s = Bare;
            SeaCombat.Fight f = SeaCombat.Begin(0, SeaCombat.Raider, s, T);
            f.Them.BurnLeft = 1;
            f.Them.Hull = 0.05d;
            SeaCombat.TurnStart(ref f, false, T);
            Assert.That(f.Over, Is.True);
            Assert.That(f.Won, Is.True, "a burn that finishes THEM is our win");
        }

        [Test]
        public void AMendPatchesAtTheTurnStartAndNeverPastFull()
        {
            SeaCombat.Stats s = Bare; s.Mend = 0.05d;
            SeaCombat.Fight f = SeaCombat.Begin(1, SeaCombat.Raider, s, T);
            SeaCombat.ShotLands(ref f, false, SeaCombat.ShotRolls.None, T);
            double hurt = f.Us.Hull;
            SeaCombat.TurnReport r = SeaCombat.TurnStart(ref f, true, T);
            Assert.That(r.Mended, Is.GreaterThan(0d));
            Assert.That(f.Us.Hull, Is.EqualTo(hurt + r.Mended).Within(1e-9));

            f.Us.Hull = f.Us.HullMax;
            Assert.That(SeaCombat.TurnStart(ref f, true, T).Mended, Is.Zero,
                        "a whole hull has nothing to patch");
        }

        [Test]
        public void APoisonBitesTheSameAmountEveryTurnThenClears()
        {
            SeaCombat.Stats s = Bare; s.Poison = 0.5d;
            SeaCombat.Fight f = SeaCombat.Begin(3, SeaCombat.Raider, s, T);
            var rolls = SeaCombat.ShotRolls.None; rolls.Poison = 0d;
            Assert.That(SeaCombat.ShotLands(ref f, true, rolls, T).PoisonProc, Is.True);
            Assert.That(f.Them.PoisonLeft, Is.EqualTo(T.PoisonTurns));

            double bite = System.Math.Round(T.PlayerShotBase * T.PoisonFrac * 10d) / 10d;
            for (int i = 0; i < T.PoisonTurns; i++)
                Assert.That(SeaCombat.TurnStart(ref f, false, T).PoisonDamage,
                            Is.EqualTo(bite).Within(1e-9), "tick " + i);
            Assert.That(SeaCombat.TurnStart(ref f, false, T).PoisonDamage, Is.Zero,
                        "the venom runs out");
        }

        [Test]
        public void BurnScalesWithTheVictimAndPoisonWithThePoisoner()
        {
            // The whole reason both fires exist. A burn is worth more the bigger the thing you set
            // alight; a poison is worth what YOUR guns are worth, whoever swallowed it.
            SeaCombat.TurnReport Bite(int tier)
            {
                SeaCombat.Stats s = Bare; s.Burn = 0.5d; s.Poison = 0.5d;
                SeaCombat.Fight f = SeaCombat.Begin(tier, SeaCombat.Raider, s, T);
                var rolls = SeaCombat.ShotRolls.None; rolls.Burn = 0d; rolls.Poison = 0d;
                SeaCombat.ShotLands(ref f, true, rolls, T);
                return SeaCombat.TurnStart(ref f, false, T);
            }

            SeaCombat.TurnReport near = Bite(0), far = Bite(3);
            Assert.That(far.BurnDamage, Is.GreaterThan(near.BurnDamage),
                        "a fatter hull must burn for more");
            Assert.That(far.PoisonDamage, Is.EqualTo(near.PoisonDamage).Within(1e-9),
                        "the same guns must poison for the same, whatever swallowed it");
        }

        [Test]
        public void LifestealHealsOffTheBallAndNeverPastFull()
        {
            SeaCombat.Stats s = Bare; s.Steal = 0.5d;
            SeaCombat.Fight f = SeaCombat.Begin(3, SeaCombat.Raider, s, T);
            f.Us.Hull = 10d;                            // hurt, so there is room to patch

            SeaCombat.ShotReport r = SeaCombat.ShotLands(ref f, true, SeaCombat.ShotRolls.None, T);
            Assert.That(r.Damage, Is.GreaterThan(0d));
            Assert.That(r.Stolen, Is.EqualTo(r.Damage * 0.5d).Within(0.06d));
            Assert.That(f.Us.Hull, Is.EqualTo(10d + r.Stolen).Within(1e-9));

            f.Us.Hull = f.Us.HullMax;
            Assert.That(SeaCombat.ShotLands(ref f, true, SeaCombat.ShotRolls.None, T).Stolen, Is.Zero,
                        "a whole hull has nothing to steal into");
        }

        [Test]
        public void ASalvoOffersAnotherBall()
        {
            SeaCombat.Stats s = Bare; s.Salvo = 0.5d; s.Shot = 1d;
            SeaCombat.Fight f = SeaCombat.Begin(3, SeaCombat.Raider, s, T);
            var rolls = SeaCombat.ShotRolls.None; rolls.Salvo = 0d;
            Assert.That(SeaCombat.ShotLands(ref f, true, rolls, T).SalvoProc, Is.True);
        }

        [Test]
        public void APlunderProcPaysTheRoutesScrap()
        {
            SeaCombat.Stats s = Bare; s.Plunder = 0.5d; s.Shot = 1d;
            SeaCombat.Fight f = SeaCombat.Begin(2, SeaCombat.Raider, s, T);
            var rolls = SeaCombat.ShotRolls.None; rolls.Plunder = 0d;
            Assert.That(SeaCombat.ShotLands(ref f, true, rolls, T).Plundered,
                        Is.EqualTo(SeaCombat.PlunderFor(2)));
            Assert.That(SeaCombat.ShotLands(ref f, false, rolls, T).Plundered, Is.Zero,
                        "plunder is OUR trade — enemies take nothing off the ledger");
        }

        // ---- enemies -----------------------------------------------------------------------------

        [Test]
        public void EverySignatureMatchesItsKind()
        {
            Assert.That(SeaCombat.ThreatStats(1, SeaCombat.Raider, T).Crit, Is.GreaterThan(0d));
            Assert.That(SeaCombat.ThreatStats(1, SeaCombat.Beast, T).Stun, Is.GreaterThan(0d));
            Assert.That(SeaCombat.ThreatStats(1, SeaCombat.Fireship, T).Burn, Is.GreaterThan(0d));
            Assert.That(SeaCombat.ThreatStats(1, SeaCombat.Ghost, T).Dodge, Is.GreaterThan(0d));
            SeaCombat.Stats hulk = SeaCombat.ThreatStats(1, SeaCombat.Derelict, T);
            Assert.That(hulk.Shot, Is.Zero);
            Assert.That(hulk.Crit + hulk.Stun + hulk.Burn + hulk.Dodge, Is.Zero,
                        "the derelict's whole identity is having nothing");
            Assert.That(SeaCombat.SignatureOf(SeaCombat.Fireship), Is.EqualTo(SeaCombat.SecBurn));
            Assert.That(SeaCombat.SignatureOf(SeaCombat.Derelict), Is.EqualTo(SeaCombat.SecNone));
        }

        [Test]
        public void TheCoreStatsCarryTheirOwnKindFlavour()
        {
            // SAVUNMA and SÜRAT are read straight off the details card, so each kind has to mean
            // something in them: the hulk is armour that cannot move, the ghost speed with no armour.
            for (int tier = 0; tier < Voyages.TierCount; tier++)
            {
                Assert.That(SeaCombat.ThreatStats(tier, SeaCombat.Derelict, T).Spd, Is.Zero,
                            "tier " + tier + ": a drifting hulk must never win the opening ball");
                Assert.That(SeaCombat.ThreatStats(tier, SeaCombat.Ghost, T).Def, Is.Zero,
                            "tier " + tier + ": there is nothing solid to a ghost");
                Assert.That(SeaCombat.ThreatStats(tier, SeaCombat.Ghost, T).Spd,
                            Is.GreaterThan(SeaCombat.ThreatStats(tier, SeaCombat.Beast, T).Spd),
                            "tier " + tier + ": the ghost must outrun the beast");
                Assert.That(SeaCombat.ThreatStats(tier, SeaCombat.Beast, T).Def,
                            Is.GreaterThan(SeaCombat.ThreatStats(tier, SeaCombat.Fireship, T).Def),
                            "tier " + tier + ": the beast must be the harder hide");
            }
        }

        [Test]
        public void AllFiveKindsActuallyTurnUp()
        {
            var kinds = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < 200; i++) kinds.Add(SeaCombat.KindFor(1_700_000_000L, i));
            Assert.That(kinds.Count, Is.EqualTo(SeaCombat.KindCount),
                        "a kind nobody can meet is dead content");
        }

        [Test]
        public void TheDangerLabelOnlySpeaksWhenTheGapIsReal()
        {
            Assert.That(SeaCombat.Menace(100d, 200d, T), Is.EqualTo(2), "outgunned reads TEHLİKELİ");
            Assert.That(SeaCombat.Menace(100d, 105d, T), Is.EqualTo(1), "a near match says nothing");
            Assert.That(SeaCombat.Menace(100d, 50d, T), Is.EqualTo(0), "a pushover reads KOLAY");
        }

        // ---- where power comes from --------------------------------------------------------------

        [Test]
        public void NobodyAboardAndNoGearMeansBaseNumbers()
        {
            Assert.That(Bare.Shot, Is.EqualTo(T.PlayerShotBase).Within(1e-9));
            Assert.That(Bare.Hull, Is.EqualTo(T.BaseNerve).Within(1e-9));
            Assert.That(Bare.Def, Is.EqualTo(T.PlayerDefBase).Within(1e-9));
            Assert.That(Bare.Spd, Is.EqualTo(T.PlayerSpdBase).Within(1e-9));
            Assert.That(Bare.Crit + Bare.Dodge + Bare.Stun + Bare.Mend + Bare.Burn
                        + Bare.Plunder + Bare.Salvo + Bare.Steal + Bare.Poison, Is.Zero);
        }

        [Test]
        public void GearFeedsTheSheetFlatAndWhole()
        {
            var gear = new SeaCombat.Item[SeaCombat.SlotCount];
            for (int i = 0; i < gear.Length; i++) gear[i] = new SeaCombat.Item { Slot = i, Grade = -1 };
            gear[SeaCombat.SlotCannon] = new SeaCombat.Item
                { Slot = SeaCombat.SlotCannon, Grade = 0, Hull = 6d, Shot = 12d, Def = 4d, Spd = 7d };
            SeaCombat.Stats s = SeaCombat.OurStats(-1, 0, 0, gear, CT, T);
            Assert.That(s.Shot, Is.EqualTo(T.PlayerShotBase + 12d).Within(1e-9));
            Assert.That(s.Hull, Is.EqualTo(T.BaseNerve + 6d).Within(1e-9));
            Assert.That(s.Def, Is.EqualTo(T.PlayerDefBase + 4d).Within(1e-9));
            Assert.That(s.Spd, Is.EqualTo(T.PlayerSpdBase + 7d).Within(1e-9));
        }

        [Test]
        public void AGunnerOutfightsEveryOtherRoleAtTheSameWorth()
        {
            int gunner = -1, other = -1;
            for (int i = 0; i < Captains.Count; i++)
            {
                if (Captains.RankOf(i) != Captains.Grade.Common) continue;
                if (Captains.RoleOf(i) == Captains.Gunner) gunner = i;
                else if (other < 0) other = i;
            }
            Assert.That(SeaCombat.OurStats(gunner, 5, 0, null, CT, T).Shot,
                        Is.GreaterThan(SeaCombat.OurStats(other, 5, 0, null, CT, T).Shot));
        }

        [Test]
        public void EveryRoleCarriesItsOwnSecondaryToSea()
        {
            // The four Commons are one of each role — the roster file promises it.
            SeaCombat.Stats qm = SeaCombat.OurStats(0, 5, 0, null, CT, T);
            SeaCombat.Stats gunner = SeaCombat.OurStats(1, 5, 0, null, CT, T);
            SeaCombat.Stats bosun = SeaCombat.OurStats(2, 5, 0, null, CT, T);
            SeaCombat.Stats purser = SeaCombat.OurStats(3, 5, 0, null, CT, T);
            Assert.That(qm.Dodge, Is.GreaterThan(0d), "the Quartermaster steers away");
            Assert.That(gunner.Crit, Is.GreaterThan(0d), "the Gunner aims for the magazine");
            Assert.That(bosun.Mend, Is.GreaterThan(0d), "the Bosun patches as she fights");
            Assert.That(purser.Plunder, Is.GreaterThan(0d), "the Purser robs mid-broadside");
        }

        [Test]
        public void EveryChanceStatIsCapped()
        {
            double Stacked(int sec)
            {
                var gear = new SeaCombat.Item[1];
                gear[0] = new SeaCombat.Item { Slot = 0, Grade = 4, Sec = sec, SecAmt = 5d };
                SeaCombat.Stats s = SeaCombat.OurStats(-1, 0, 0, gear, CT, T);
                switch (sec)
                {
                    case SeaCombat.SecCrit:    return s.Crit;
                    case SeaCombat.SecDodge:   return s.Dodge;
                    case SeaCombat.SecStun:    return s.Stun;
                    case SeaCombat.SecMend:    return s.Mend;
                    case SeaCombat.SecBurn:    return s.Burn;
                    case SeaCombat.SecPlunder: return s.Plunder;
                    case SeaCombat.SecSalvo:   return s.Salvo;
                    case SeaCombat.SecSteal:   return s.Steal;
                    default:                   return s.Poison;
                }
            }

            // An uncapped chance stat turns the far reach into a coin with one face — and an
            // uncapped ONARIM or CAN ÇALMA is worse: the fight simply never ends.
            Assert.That(Stacked(SeaCombat.SecCrit), Is.EqualTo(SeaCombat.CritCap));
            Assert.That(Stacked(SeaCombat.SecDodge), Is.EqualTo(SeaCombat.DodgeCap));
            Assert.That(Stacked(SeaCombat.SecStun), Is.EqualTo(SeaCombat.StunCap));
            Assert.That(Stacked(SeaCombat.SecMend), Is.EqualTo(SeaCombat.MendCap));
            Assert.That(Stacked(SeaCombat.SecBurn), Is.EqualTo(SeaCombat.BurnCap));
            Assert.That(Stacked(SeaCombat.SecPlunder), Is.EqualTo(SeaCombat.PlunderCap));
            Assert.That(Stacked(SeaCombat.SecSalvo), Is.EqualTo(SeaCombat.SalvoCap));
            Assert.That(Stacked(SeaCombat.SecSteal), Is.EqualTo(SeaCombat.StealCap));
            Assert.That(Stacked(SeaCombat.SecPoison), Is.EqualTo(SeaCombat.PoisonCap));
        }

        [Test]
        public void FightingPowerIsDerivedNotStored()
        {
            // Docs/VOYAGES.md §21's lesson, held structurally: no combat stat may live on the
            // captain. If a future change adds one, this is the conversation it has to have first.
            Assert.That(typeof(Captains.Card).GetFields().Length, Is.EqualTo(3),
                        "the captain card grew a field — combat power is DERIVED, see SeaCombat");
        }

        [Test]
        public void ThePowerReadingRisesWithEverythingItWeighs()
        {
            SeaCombat.Stats s = Bare;
            double baseline = SeaCombat.PowerFor(s, T);
            SeaCombat.Stats hull = s; hull.Hull += 50d;
            SeaCombat.Stats shot = s; shot.Shot += 10d;
            SeaCombat.Stats def = s; def.Def += 10d;
            SeaCombat.Stats spd = s; spd.Spd += 10d;
            Assert.That(SeaCombat.PowerFor(hull, T), Is.GreaterThan(baseline));
            Assert.That(SeaCombat.PowerFor(shot, T), Is.GreaterThan(baseline));
            Assert.That(SeaCombat.PowerFor(def, T), Is.GreaterThan(baseline), "SAVUNMA is power");
            Assert.That(SeaCombat.PowerFor(spd, T), Is.GreaterThan(baseline), "SÜRAT is power");

            // Every proc has to move the headline too, or the sheet is lying about what it weighs.
            // Measured on an item with a real core, because PowerFor scales secondaries BY the core.
            var plain = new SeaCombat.Item { Slot = 0, Grade = 0, Hull = 10d, Shot = 5d };
            for (int sec = SeaCombat.SecNone + 1; sec < SeaCombat.SecCount; sec++)
            {
                SeaCombat.Item item = plain;
                item.Sec = sec;
                item.SecAmt = 0.2d;
                Assert.That(SeaCombat.ItemScore(item, T),
                            Is.GreaterThan(SeaCombat.ItemScore(plain, T)),
                            "secondary " + sec + " weighs nothing on the headline");
            }
        }

        // ---- energy ------------------------------------------------------------------------------

        [Test]
        public void EnergyRefillsOnTheWallClockAndStopsAtTheCap()
        {
            long stamp = 1_000_000L;
            Assert.That(SeaCombat.EnergyAt(3, stamp, stamp, T), Is.EqualTo(3));
            Assert.That(SeaCombat.EnergyAt(3, stamp, stamp + (long)T.EnergyRegenSeconds - 1, T), Is.EqualTo(3));
            Assert.That(SeaCombat.EnergyAt(3, stamp, stamp + (long)T.EnergyRegenSeconds, T), Is.EqualTo(4));
            Assert.That(SeaCombat.EnergyAt(3, stamp, stamp + (long)T.EnergyRegenSeconds * 5000, T),
                        Is.EqualTo(T.EnergyMax), "a week away must not overflow the pool");
        }

        [Test]
        public void TheCountdownReachesZeroExactlyAtTheNextPoint()
        {
            long stamp = 1_000_000L;
            double left = SeaCombat.SecondsToNextEnergy(3, stamp, stamp + 100L, T);
            Assert.That(left, Is.EqualTo(T.EnergyRegenSeconds - 100d).Within(1e-6));
            Assert.That(SeaCombat.SecondsToNextEnergy(T.EnergyMax, stamp, stamp, T), Is.Zero,
                        "a full pool counts down toward nothing");
        }

        [Test]
        public void SpendingFromAFullPoolStartsTheClockThen()
        {
            var data = new SaveData();
            var sea = new ExpeditionService(null, new TimeService(), data, null, T);
            Assert.That(sea.Energy, Is.EqualTo(T.EnergyMax), "a pre-feature save starts full");
            Assert.That(sea.TrySpendEnergy(), Is.True);
            Assert.That(sea.Energy, Is.EqualTo(T.EnergyMax - 1));
            Assert.That(data.seaEnergyStampUnix, Is.GreaterThan(0L),
                        "the refill clock starts at the moment the pool stops being full");
        }

        // ---- the loot table, read out ------------------------------------------------------------

        [Test]
        public void TheOddsAreTheTableRollGradeActuallyRollsAgainst()
        {
            var odds = new double[SeaCombat.GradeMult.Length];
            for (int tier = 0; tier < Voyages.TierCount; tier++)
            {
                SeaCombat.GradeOdds(tier, 0d, T, odds);

                double sum = 0d;
                for (int g = 0; g < odds.Length; g++) sum += odds[g];
                Assert.That(sum, Is.EqualTo(1d).Within(1e-9), "tier " + tier + ": odds have to be odds");

                // The panel promises what the dice deliver: rolling the middle of each slice has to
                // land on the grade the readout says that slice belongs to.
                double below = 0d;
                for (int g = 0; g < odds.Length; g++)
                {
                    if (odds[g] <= 0d) continue;
                    Assert.That(SeaCombat.RollGrade(below + odds[g] * 0.5d, tier, 0d, T), Is.EqualTo(g),
                                "tier " + tier + ", grade " + g + ": the readout and the dice disagree");
                    below += odds[g];
                }
            }
        }

        [Test]
        public void TheFarWatersAndABetterGlassBothTiltTheTableUp()
        {
            var near = new double[SeaCombat.GradeMult.Length];
            var far = new double[SeaCombat.GradeMult.Length];
            SeaCombat.GradeOdds(0, 0d, T, near);
            SeaCombat.GradeOdds(Voyages.TierCount - 1, 0d, T, far);
            Assert.That(far[0], Is.LessThan(near[0]), "the far reach drops fewer Commons");
            Assert.That(far[4], Is.GreaterThan(near[4]), "and more Mythics — the reason to sail out");

            var glass = new double[SeaCombat.GradeMult.Length];
            SeaCombat.GradeOdds(0, SeaCombat.SpyglassLuck(4), T, glass);
            Assert.That(glass[0], Is.LessThan(near[0]), "a better glass finds better things");
        }

        [Test]
        public void AnOddsReadoutWithNowhereToWriteIsHarmless()
        {
            var stub = new double[2];
            SeaCombat.GradeOdds(0, 0d, T, null);
            SeaCombat.GradeOdds(0, 0d, T, stub);
            Assert.That(stub[0], Is.Zero, "an array too short to hold the table is left alone");
        }

        [Test]
        public void AnEmptyPoolRefusesTheSearch()
        {
            var data = new SaveData();
            var sea = new ExpeditionService(null, new TimeService(), data, null, T);
            for (int i = 0; i < T.EnergyMax; i++) Assert.That(sea.TrySpendEnergy(), Is.True, "spend " + i);
            Assert.That(sea.TrySpendEnergy(), Is.False, "energy is the governor — no overdraft");
        }

        // ---- items -------------------------------------------------------------------------------

        [Test]
        public void AnItemsBudgetRisesWithGradeAndWithTheRouteItFellOn()
        {
            for (int slot = 0; slot < SeaCombat.SlotCount; slot++)
            {
                SeaCombat.Item common = SeaCombat.ItemFor(slot, 0, 0, 0d, T);
                SeaCombat.Item mythic = SeaCombat.ItemFor(slot, 0, 4, 0d, T);
                SeaCombat.Item far = SeaCombat.ItemFor(slot, 3, 0, 0d, T);
                double Core(SeaCombat.Item i) => i.Hull + i.Shot + i.Def + i.Spd;
                Assert.That(Core(mythic), Is.GreaterThan(Core(common)), "slot " + slot + " grade");
                Assert.That(Core(far), Is.GreaterThan(Core(common)), "slot " + slot + " tier");

                // Every piece carries all four, so no slot is dead weight in a stat the sheet shows.
                Assert.That(common.Hull, Is.GreaterThan(0d), "slot " + slot + " hull");
                Assert.That(common.Shot, Is.GreaterThan(0d), "slot " + slot + " shot");
                Assert.That(common.Def, Is.GreaterThan(0d), "slot " + slot + " def");
                Assert.That(common.Spd, Is.GreaterThan(0d), "slot " + slot + " spd");
            }

            // And each slot keeps its nature: the glass is the lookout, the plating the wall.
            Assert.That(SeaCombat.ItemFor(SeaCombat.SlotSpyglass, 2, 0, 0d, T).Spd,
                        Is.GreaterThan(SeaCombat.ItemFor(SeaCombat.SlotPlating, 2, 0, 0d, T).Spd));
            Assert.That(SeaCombat.ItemFor(SeaCombat.SlotPlating, 2, 0, 0d, T).Def,
                        Is.GreaterThan(SeaCombat.ItemFor(SeaCombat.SlotSpyglass, 2, 0, 0d, T).Def));
        }

        [Test]
        public void OnlyRareAndUpCarriesASecondary()
        {
            for (int slot = 0; slot < SeaCombat.SlotCount; slot++)
            {
                Assert.That(SeaCombat.ItemFor(slot, 2, 0, 0.9d, T).Sec, Is.EqualTo(SeaCombat.SecNone),
                            "a Common with a secondary makes rarity a number instead of a kind");
                SeaCombat.Item rare = SeaCombat.ItemFor(slot, 2, 1, 0.5d, T);
                Assert.That(rare.Sec, Is.Not.EqualTo(SeaCombat.SecNone));
                Assert.That(rare.SecAmt, Is.GreaterThan(0d));
                SeaCombat.Item mythic = SeaCombat.ItemFor(slot, 2, 4, 0.5d, T);
                Assert.That(mythic.SecAmt, Is.GreaterThan(rare.SecAmt),
                            "the secondary must grow with the grade");
            }
        }

        [Test]
        public void EverySlotEveryGradeAndEverySecondaryCanDrop()
        {
            var slots = new System.Collections.Generic.HashSet<int>();
            var grades = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i <= 400; i++)
            {
                slots.Add(SeaCombat.RollSlot(i / 400d));
                grades.Add(SeaCombat.RollGrade(i / 400d, 0, 0d, T));
            }
            Assert.That(slots.Count, Is.EqualTo(SeaCombat.SlotCount));
            Assert.That(grades.Count, Is.EqualTo(Captains.GradeCount),
                        "a grade nobody can drop is dead content");

            var secs = new System.Collections.Generic.HashSet<int>();
            for (int slot = 0; slot < SeaCombat.SlotCount; slot++)
                for (int i = 0; i < 20; i++)
                    secs.Add(SeaCombat.ItemFor(slot, 0, 1, i / 20d, T).Sec);
            Assert.That(secs.Count, Is.EqualTo(SeaCombat.SecCount - 1),
                        "a secondary no slot can roll is dead content");
        }

        [Test]
        public void TheRouteAndTheSpyglassBothLeanOnTheOdds()
        {
            int Above(int tier, double luck)
            {
                int n = 0;
                for (int i = 0; i < 4000; i++)
                    if (SeaCombat.RollGrade((i + 0.5d) / 4000d, tier, luck, T) > 0) n++;
                return n;
            }
            int bare = Above(0, 0d);
            Assert.That(Above(3, 0d), Is.GreaterThan(bare), "a far route must drop better");
            Assert.That(Above(0, SeaCombat.SpyglassLuck(4)), Is.GreaterThan(bare),
                        "a better spyglass must find better");
            Assert.That(SeaCombat.SpyglassLuck(-1), Is.Zero, "no glass, no luck");
            Assert.That(SeaCombat.SpyglassLuck(4), Is.GreaterThan(SeaCombat.SpyglassLuck(0)));
        }

        [Test]
        public void TheItemScoreSpeaksTheSameLanguageAsThePanel()
        {
            SeaCombat.Item small = SeaCombat.ItemFor(SeaCombat.SlotCannon, 0, 0, 0d, T);
            SeaCombat.Item big = SeaCombat.ItemFor(SeaCombat.SlotCannon, 3, 4, 0d, T);
            Assert.That(SeaCombat.ItemScore(big, T), Is.GreaterThan(SeaCombat.ItemScore(small, T)));
            Assert.That(SeaCombat.ItemScore(new SeaCombat.Item { Grade = -1 }, T), Is.Zero,
                        "an empty slot weighs nothing");
        }

        [Test]
        public void ScrapRisesWithGradeAndStaysSmall()
        {
            for (int g = 1; g < Captains.GradeCount; g++)
                Assert.That(SeaCombat.ScrapFor(g), Is.GreaterThan(SeaCombat.ScrapFor(g - 1)));
            Assert.That(SeaCombat.ScrapFor(Captains.GradeCount - 1), Is.LessThan(100L),
                        "scrap is the consolation, not the prize");
        }

        // ---- the service's gear ledger -----------------------------------------------------------

        [Test]
        public void WearingOverAnOldItemScrapsItIntoSalvage()
        {
            var data = new SaveData();
            var sea = new ExpeditionService(null, new TimeService(), data, null, T);

            Assert.That(sea.GearGrade(SeaCombat.SlotCannon), Is.EqualTo(-1), "starts empty");
            SeaCombat.Item first = SeaCombat.ItemFor(SeaCombat.SlotCannon, 1, 2, 0.5d, T);
            Assert.That(sea.Equip(first), Is.Zero, "an empty slot scraps nothing");
            Assert.That(sea.GearGrade(SeaCombat.SlotCannon), Is.EqualTo(2));
            Assert.That(sea.GearItem(SeaCombat.SlotCannon).Shot, Is.EqualTo(first.Shot));

            long before = data.salvage;
            long scrap = sea.Equip(SeaCombat.ItemFor(SeaCombat.SlotCannon, 3, 4, 0.5d, T));
            Assert.That(scrap, Is.EqualTo(SeaCombat.ScrapFor(2)));
            Assert.That(data.salvage, Is.EqualTo(before + scrap), "nothing earned is destroyed");
            Assert.That(sea.GearGrade(SeaCombat.SlotCannon), Is.EqualTo(4));
        }

        [Test]
        public void StrippingAWornItemPaysItsSalvageAndEmptiesTheSlot()
        {
            var data = new SaveData();
            var sea = new ExpeditionService(null, new TimeService(), data, null, T);
            sea.Equip(SeaCombat.ItemFor(SeaCombat.SlotCharm, 1, 3, 0.5d, T));
            long scrap = sea.ScrapWorn(SeaCombat.SlotCharm);
            Assert.That(scrap, Is.EqualTo(SeaCombat.ScrapFor(3)));
            Assert.That(data.salvage, Is.EqualTo(scrap));
            Assert.That(sea.GearGrade(SeaCombat.SlotCharm), Is.EqualTo(-1));
            Assert.That(sea.ScrapWorn(SeaCombat.SlotCharm), Is.Zero, "an empty slot strips nothing");
        }

        [Test]
        public void APreStatItemGrowsStatsWithoutLosingItsGrade()
        {
            // The first build baked ONE power number per item. That save arrives here mid-grind —
            // the item must come through wearing today's stat shape, not evaporate.
            var data = new SaveData();
            data.seaGearGrade = new[] { 3, 0, 0, 0 };    // a grade-2 cannon...
            data.seaGearPower = new[] { 26, 0, 0, 0 };   // ...whose whole body was "26"
            data.seaGearHull = null;
            data.seaGearShot = null;
            var sea = new ExpeditionService(null, new TimeService(), data, null, T);
            SeaCombat.Item cannon = sea.GearItem(SeaCombat.SlotCannon);
            Assert.That(cannon.Grade, Is.EqualTo(2), "the grade survives");
            Assert.That(cannon.Shot, Is.EqualTo(26d), "a cannon's old power was its shot");
            Assert.That(cannon.Sec, Is.EqualTo(SeaCombat.SecNone), "history rolls no new dice");
            Assert.That(sea.GearScore(SeaCombat.SlotCannon), Is.GreaterThan(0), "the score is re-derived");
        }

        [Test]
        public void APreDefenceItemGrowsTheNewCoreStatsInPlace()
        {
            // The build before this one had no SAVUNMA or SÜRAT on items. A save from it must not
            // arrive with a worn Legendary that is slower and softer than a fresh Common — which is
            // exactly what an unmigrated zero would mean now that both stats decide fights.
            var data = new SaveData();
            data.seaGearGrade = new[] { 0, 4, 0, 0 };        // a Legendary plating...
            data.seaGearHull = new[] { 0d, 240d, 0d, 0d };   // ...with the old two-stat body
            data.seaGearShot = new[] { 0d, 3d, 0d, 0d };
            data.seaGearDef = null;
            data.seaGearSpd = null;
            var sea = new ExpeditionService(null, new TimeService(), data, null, T);

            SeaCombat.Item plating = sea.GearItem(SeaCombat.SlotPlating);
            Assert.That(plating.Grade, Is.EqualTo(3), "the grade survives");
            Assert.That(plating.Hull, Is.EqualTo(240d), "and so does everything it already had");
            SeaCombat.Item fresh = SeaCombat.ItemFor(SeaCombat.SlotPlating, 0, 0, 0d, T);
            Assert.That(plating.Def, Is.GreaterThanOrEqualTo(fresh.Def),
                        "an owned Legendary must not be softer than a fresh Common");
            Assert.That(plating.Spd, Is.GreaterThanOrEqualTo(fresh.Spd),
                        "nor slower");
            Assert.That(sea.GearScore(SeaCombat.SlotPlating), Is.GreaterThan(0),
                        "the score is re-derived over the grown block");
        }

        [Test]
        public void APreFeatureSaveArrivesSafely()
        {
            var data = new SaveData();
            data.seaGearGrade = null;
            data.seaGearPower = null;
            data.seaGearSec = null;
            data.seaGearSecAmt = null;
            data.seaGearDef = null;
            data.seaGearSpd = null;
            data.seaEnergy = -1;
            var sea = new ExpeditionService(null, new TimeService(), data, null, T);
            Assert.That(data.seaGearGrade.Length, Is.EqualTo(SeaCombat.SlotCount));
            Assert.That(data.seaGearSecAmt.Length, Is.EqualTo(SeaCombat.SlotCount));
            Assert.That(data.seaGearDef.Length, Is.EqualTo(SeaCombat.SlotCount));
            Assert.That(data.seaGearSpd.Length, Is.EqualTo(SeaCombat.SlotCount));
            Assert.That(sea.Energy, Is.EqualTo(T.EnergyMax),
                        "the first thing a returning player sees must not be a wait");
        }

        // ---- banking -----------------------------------------------------------------------------

        private sealed class Terms : IIslandSaleTerms
        {
            public double BarPriceRaw { get; set; }
            public double IncomeCapPerMinuteRaw { get; set; }
            public double UpgradeTreeCostRaw { get; set; }
        }

        private static ExpeditionService Rig(out SaveData data, out MarketService market,
                                             out VoyageService dock, out CaptainService captains)
        {
            data = new SaveData();
            var wallet = new WalletService(data.wallet);
            market = new MarketService(data, wallet, null);
            market.Register("coal", new Terms { BarPriceRaw = 10d, IncomeCapPerMinuteRaw = 1e12d });
            market.SetActiveIsland("coal");
            market.Row("coal").deliveredPerMin = 600d;
            var foremen = new ForemanService(data, wallet, Foremen.Tuning.Default);
            captains = new CaptainService(data, Captains.Tuning.Default, CaptainCrate.Tuning.Default,
                                          new System.Random(7));
            dock = new VoyageService(data, market, foremen, wallet, new TimeService(),
                                     Voyages.Tuning.Default, captains);
            return new ExpeditionService(dock, new TimeService(), data, captains, T);
        }

        private static void Sail(VoyageService dock, MarketService market)
        {
            if (dock.At(0) == null) dock.TryStart("coal", 0);
            market.Deliver("coal", dock.At(0).holdSize * 2d);
            dock.Tick((float)Voyages.SecondsToFill(0, Voyages.Tuning.Default) + 1f);
        }

        [Test]
        public void AKillBanksItsTrickleAndTouchesNothingOnTheVoyage()
        {
            SaveData data; MarketService market; VoyageService dock; CaptainService captains;
            ExpeditionService sea = Rig(out data, out market, out dock, out captains);
            Sail(dock, market);
            sea.SetSail("coal");

            VoyageState v = dock.At(0);
            long returns = v.returnsUnix; double held = v.held; int cards = v.payoutCards;

            long chartsBefore = captains.Charts;
            Assert.That(sea.RegisterKill(3, 2), Is.True);
            Assert.That(captains.Charts, Is.EqualTo(chartsBefore + 3));
            Assert.That(data.salvage, Is.EqualTo(2L));

            Assert.That(v.returnsUnix, Is.EqualTo(returns), "a kill must not shorten the crossing");
            Assert.That(v.held, Is.EqualTo(held));
            Assert.That(v.payoutCards, Is.EqualTo(cards));
        }

        [Test]
        public void NothingBanksAshore()
        {
            SaveData data; MarketService market; VoyageService dock; CaptainService captains;
            ExpeditionService sea = Rig(out data, out market, out dock, out captains);
            Assert.That(sea.RegisterKill(5, 5), Is.False);
            Assert.That(captains.Charts, Is.Zero);
        }

        [Test]
        public void AKillsTrickleStaysATrickleBesideTheHold()
        {
            var vt = Voyages.Tuning.Default;
            for (int tier = 0; tier < Voyages.TierCount; tier++)
                Assert.That(SeaCombat.ChartsFor(tier, SeaCombat.Beast, vt, T),
                            Is.LessThan(Voyages.Charts(tier, 1d, 0, true, vt) / 2 + 1),
                            "tier " + tier + ": a single kill rivals the hold — the gear is meant to be the prize");
        }

        [Test]
        public void AServiceWithNothingWiredIsInert()
        {
            var sea = new ExpeditionService(null, null);
            Assert.That(sea.Energy, Is.Zero);
            Assert.That(sea.TrySpendEnergy(), Is.False);
            Assert.That(sea.RegisterKill(1, 1), Is.False);
            Assert.That(sea.GearGrade(0), Is.EqualTo(-1));
            Assert.That(sea.GearItem(0).Grade, Is.EqualTo(-1));
            Assert.That(sea.Equip(new SeaCombat.Item { Slot = 0, Grade = 2 }), Is.Zero);
            Assert.That(sea.ScrapWorn(0), Is.Zero);
        }
    }
}
