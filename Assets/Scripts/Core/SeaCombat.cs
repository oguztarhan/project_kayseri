using System;

namespace Game.Core
{
    /// <summary>
    /// The sea adventure as pure maths: a full RPG STAT BLOCK, items that roll stats, enemies with
    /// signature abilities, and a turn engine of nothing but the exchange — one ball at a time,
    /// ours then theirs, until a hull gives. THERE ARE NO ABILITY BUTTONS. Every fight is decided
    /// by the sheet alone, which is what makes every stat on it worth grinding for.
    ///
    /// The sheet is four CORE stats and nine PROCS, each renamed for a ship:
    ///
    ///   CESARET (hull) · TOP (attack) · SAVUNMA (defence, shaves every ball) · SÜRAT (speed,
    ///   the faster side fires first)
    ///
    ///   KRİTİK (crit) · MANEVRA (dodge) · SALVO (extra ball) · SERSEMLETME (stun) · ONARIM
    ///   (regen, per turn) · CAN ÇALMA (lifesteal, heals off the ball) · YAĞMA (plunder, pays
    ///   scrap) · YANGIN (burn: % of the victim's MAX hull per turn) · ZEHİR (poison: a flat bite
    ///   baked from the poisoner's TOP per turn)
    ///
    /// THE RULE STILL HOLDS (Docs/FIVE_LAYERS.md §4): nothing here can touch the voyage. A lost
    /// fight costs the energy it cost and nothing else. ENERGY IS THE GOVERNOR — one search, one
    /// energy, wall-clock refill. GEAR IS A CLOSED LOOP — an item's stats exist only inside this
    /// file's fights (the one honest exception: YAĞMA pays salvage, which is already the sea's own
    /// closed currency).
    ///
    /// WHERE POWER COMES FROM, unchanged in spirit: DERIVED, never stored. The ship's stat block is
    /// rebuilt every fight from the crew track, the captain (each ROLE carries its own
    /// secondary — Gunner crits, Quartermaster dodges, Bosun mends, Purser plunders) and the five
    /// worn items. The POWER number the panel shows is a formula over that block, not a field.
    ///
    /// THE ENGINE TAKES ITS DICE AS ARGUMENTS. Every roll enters through <see cref="ShotRolls"/>,
    /// so a test can replay any fight exactly; a roll of 1 can never proc anything, which is what
    /// the ladder tests use to pin the proc-free skeleton.
    /// </summary>
    public static class SeaCombat
    {
        /// <summary>
        /// What can pop up, each with a signature the details card can warn about: the raider
        /// crits, the beast stuns, the fireship burns, the ghost dodges — and the derelict cannot
        /// answer at all. Indices are seeded by <see cref="KindFor"/> and name loc keys; append only.
        /// </summary>
        public const int Raider = 0, Beast = 1, Derelict = 2, Fireship = 3, Ghost = 4;
        public const int KindCount = 5;

        /// <summary>
        /// The five gear slots. The first four were shipped in the original sea-combat save contract
        /// and must never move; Rigging is appended as slot 4. TOP is the cannon, ZIRH the plating,
        /// DÜRBÜN the spyglass (also the drop luck), TILSIM the charm, and RIGGING the new handling
        /// slot.
        /// </summary>
        public const int SlotCannon = 0, SlotPlating = 1, SlotSpyglass = 2, SlotCharm = 3, SlotRigging = 4;
        public const int LegacySlotCount = 4;
        public const int SlotCount = 5;

        /// <summary>No item in the slot. The save stores grade + 1 — 0 is empty.</summary>
        public const int GearEmpty = 0;

        /// <summary>
        /// The secondary stats an item (or a role, or an enemy kind) can carry. Saved by index in
        /// seaGearSec — append only. None is 0 so an empty save cell reads as no secondary.
        /// </summary>
        public const int SecNone = 0, SecCrit = 1, SecDodge = 2, SecStun = 3, SecMend = 4,
                         SecBurn = 5, SecPlunder = 6, SecSalvo = 7, SecSteal = 8, SecPoison = 9;
        public const int SecCount = 10;

        // Chance stats are capped so no stack of gear turns a fight into a coin with one face.
        // MEND is per-turn healing as a fraction of max hull and capped hardest — an outheal loop
        // would make the far reach a stalemate instead of a wall. STEAL is a fraction of each
        // ball healed back, capped for the same reason.
        public const double CritCap = 0.60d, DodgeCap = 0.50d, StunCap = 0.40d, MendCap = 0.10d,
                            BurnCap = 0.60d, PlunderCap = 0.50d, SalvoCap = 0.50d,
                            StealCap = 0.40d, PoisonCap = 0.60d;

        // Per-tier enemy scale, and per-kind flavour on top of it. DEF shaves every ball we land;
        // SPD decides who opens the exchange (the ghost is quick, the beast ponderous, and the
        // derelict — speed 0 — always lets us open, which is the whole mercy of the thing).
        public static readonly double[] ThreatHull   = { 90d, 160d, 300d, 520d };
        public static readonly double[] ThreatMenace = { 2.5d, 4d, 7d, 11d };
        public static readonly double[] ThreatDef    = { 2d, 5d, 10d, 18d };
        public static readonly double[] ThreatSpd    = { 8d, 11d, 15d, 20d };
        public static readonly double[] KindHull   = { 1d, 1.6d, 0.55d, 0.8d, 0.9d };
        public static readonly double[] KindMenace = { 1d, 0.7d, 0d, 0.9d, 0.8d };
        public static readonly double[] KindDef    = { 1d, 1.3d, 1.5d, 0.7d, 0d };
        public static readonly double[] KindSpd    = { 1.1d, 0.6d, 0d, 0.9d, 1.35d };
        public static readonly double[] KindLoot   = { 1d, 1.25d, 0.8d, 1.15d, 1.1d };

        /// <summary>What one grade is worth as a multiplier on an item's stat budget.</summary>
        public static readonly double[] GradeMult = { 1d, 1.5d, 2.2d, 3.2d, 4.5d };

        /// <summary>
        /// Every item carries ALL FOUR core stats, split by the slot's nature — a cannon is
        /// shot-heavy, plating hull-and-defence-heavy, the spyglass is the lookout (speed, so a
        /// good glass wins the first ball). Rows are slots, columns tiers: what a Common drop from
        /// that route rolls.
        /// </summary>
        public static readonly double[][] SlotHull =
        {
            new[] { 6d, 10d, 17d, 28d },        // cannon
            new[] { 26d, 44d, 74d, 120d },      // plating
            new[] { 10d, 17d, 28d, 46d },       // spyglass
            new[] { 20d, 34d, 56d, 92d },       // charm
            new[] { 4d, 7d, 12d, 20d },         // rigging (appended handling slot; deliberately lighter)
        };
        public static readonly double[][] SlotShot =
        {
            new[] { 3.5d, 6d, 10d, 17d },
            new[] { 0.6d, 1d, 1.7d, 2.8d },
            new[] { 1.8d, 3d, 5d, 8.5d },
            new[] { 1.1d, 1.9d, 3.2d, 5.2d },
            new[] { 0.4d, 0.7d, 1.2d, 2d },
        };
        public static readonly double[][] SlotDef =
        {
            new[] { 0.5d, 1d, 2d, 3d },
            new[] { 3d, 5d, 9d, 15d },
            new[] { 1d, 2d, 3d, 5d },
            new[] { 2d, 3d, 5d, 8d },
            new[] { 0.4d, 0.7d, 1.2d, 2d },
        };
        public static readonly double[][] SlotSpd =
        {
            new[] { 1d, 2d, 3d, 5d },
            new[] { 0.5d, 1d, 2d, 3d },
            new[] { 4d, 7d, 11d, 18d },
            new[] { 2d, 3d, 5d, 8d },
            new[] { 1.2d, 2d, 3.3d, 5.3d },
        };

        /// <summary>
        /// Which secondaries a slot can roll — the slot's flavour. Every secondary is reachable
        /// from at least one slot: venom shells ride the cannon, the vampiric charm steals life.
        /// </summary>
        public static readonly int[][] SlotSecPool =
        {
            new[] { SecCrit, SecSalvo, SecBurn, SecPoison },    // cannon: ways to hit harder
            new[] { SecDodge, SecStun, SecMend },               // plating: ways to be hit less
            new[] { SecCrit, SecPlunder, SecDodge },            // spyglass: the sharp eye
            new[] { SecSteal, SecMend, SecSalvo, SecStun },     // charm: the uncanny ones
            new[] { SecDodge, SecCrit, SecSalvo },              // rigging: handling and tempo
        };

        /// <summary>A secondary's value at RARE, by Sec index; SecGradeMult scales it up-grade.
        /// COMMON ITEMS CARRY NO SECONDARY — that is what makes a rare drop feel different in kind,
        /// not just in size.</summary>
        public static readonly double[] SecBase =
            { 0d, 0.05d, 0.04d, 0.04d, 0.015d, 0.06d, 0.08d, 0.05d, 0.05d, 0.06d };
        public static readonly double[] SecGradeMult = { 0d, 1d, 1.6d, 2.4d, 3.4d };

        /// <summary>Salvage a scrapped item pays, by grade. Small: scrap is the consolation.</summary>
        public static readonly long[] ScrapSalvage = { 2L, 4L, 8L, 16L, 30L };

        /// <summary>What one YAĞMA proc grabs mid-fight, by tier. A trickle beside the win trickle.</summary>
        public static readonly long[] PlunderScrap = { 1L, 2L, 4L, 8L };

        // ------------------------------------------------------------------ tuning
        public struct Tuning
        {
            /// <summary>The energy pool and its wall-clock refill. One search, one energy.</summary>
            public int EnergyMax;
            public double EnergyRegenSeconds;

            /// <summary>How many items the workshop's depo holds — see <see cref="GearStash"/>.</summary>
            public int StashCapacity;

            /// <summary>Sweep and slide-in seconds. Short — the button was just pressed.</summary>
            public double SearchSeconds, ApproachSeconds;

            /// <summary>The exchange's cadence: aim pause and ball flight. Damage lands ON impact.</summary>
            public double TurnAimSeconds, TurnFlightSeconds;

            /// <summary>Our shot before anyone improves it, and what each Crew level adds (fraction).</summary>
            public double PlayerShotBase, ShotPerCrewLevel;

            /// <summary>The Gunner's roster worth counts double toward the shot multiplier.</summary>
            public double GunnerFightBonus;

            /// <summary>How long we stand being shot at, and what Crew levels add.</summary>
            public double BaseNerve, NervePerCrewLevel;

            /// <summary>Our defence and speed before gear — gear is where both live.</summary>
            public double PlayerDefBase, PlayerSpdBase;

            /// <summary>Their per-shot damage = menace x this.</summary>
            public double EnemyShotScale;

            /// <summary>No SAVUNMA stack may eat a ball whole: a landing shot always deals at
            /// least this fraction of the attacker's TOP.</summary>
            public double MinShotFrac;

            /// <summary>A crit's multiplier, a burn's length in the victim's turns, and what each
            /// burning turn costs as a fraction of max hull.</summary>
            public double CritMult;
            public int BurnTurns;
            public double BurnFrac;

            /// <summary>A poison's length in the victim's turns, and each tick's bite as a
            /// fraction of the POISONER's shot — baked when it procs. Burn scales with the victim,
            /// poison with the attacker; that is the whole difference between the two fires.</summary>
            public int PoisonTurns;
            public double PoisonFrac;

            /// <summary>A captain's role secondary = roster worth x this, capped by the stat's cap.
            /// Gunner to KRİTİK, Quartermaster to MANEVRA, Bosun to ONARIM, Purser to YAĞMA.</summary>
            public double RoleSecFactor;

            /// <summary>The kinds' signature chances — what the details card warns about.</summary>
            public double RaiderCrit, BeastStun, FireshipBurn, GhostDodge, GhostMend;

            /// <summary>A win's chart/salvage trickle, as a share of a full hold.</summary>
            public double EncounterChartShare, EncounterSalvageShare;

            /// <summary>Grade odds for a drop, shifted up by tier and by spyglass luck.</summary>
            public double DropCommon, DropRare, DropEpic, DropLegendary, DropMythic;
            public double DropTierBonus, DropLuckBonus;

            /// <summary>The POWER formula's weights — the one number the panel leads with, and what
            /// the TEHLİKELİ label compares. A label, never a mechanic.</summary>
            public double PowerHullWeight, PowerShotWeight, PowerDefWeight, PowerSpdWeight,
                          PowerSecWeight;

            /// <summary>Enemy power over ours: above DangerRatio reads TEHLİKELİ, below EasyRatio
            /// reads KOLAY, between reads nothing.</summary>
            public double DangerRatio, EasyRatio;

            public static Tuning Default => new Tuning
            {
                // 30 deep so a sitting is a real grind session (~30 fights), 5 minutes a point so
                // the pool is back in 2.5 hours — the app-shut refill every timer here keeps.
                EnergyMax = 30,
                EnergyRegenSeconds = 300d,

                // Twenty items: five across by four down on the depo grid, and about two full
                // energy pools' worth of drops before a trip has to end at the workshop.
                StashCapacity = GearStash.DefaultCapacity,

                SearchSeconds = 0.9d,
                ApproachSeconds = 1.2d,
                TurnAimSeconds = 0.55d,
                TurnFlightSeconds = 0.45d,

                // Worked by hand against the tables above, proc-free: tier 0 falls to watching,
                // and every rung after falls only to the tier below's gear — the grind the drops
                // feed, with no buttons to lean on. SeaCombatTests pins every rung.
                PlayerShotBase = 18d, ShotPerCrewLevel = 0.06d,
                GunnerFightBonus = 2d,
                BaseNerve = 100d, NervePerCrewLevel = 8d,
                PlayerDefBase = 0d, PlayerSpdBase = 10d,
                EnemyShotScale = 3.4d,
                MinShotFrac = 0.25d,

                CritMult = 2.0d,
                BurnTurns = 3, BurnFrac = 0.06d,
                PoisonTurns = 4, PoisonFrac = 0.35d,
                RoleSecFactor = 0.12d,
                RaiderCrit = 0.22d, BeastStun = 0.25d, FireshipBurn = 0.40d,
                GhostDodge = 0.30d, GhostMend = 0.03d,

                EncounterChartShare = 0.12d, EncounterSalvageShare = 0.12d,

                DropCommon = 0.52d, DropRare = 0.27d, DropEpic = 0.13d,
                DropLegendary = 0.06d, DropMythic = 0.02d,
                DropTierBonus = 0.35d, DropLuckBonus = 0.04d,

                PowerHullWeight = 0.55d, PowerShotWeight = 3.2d,
                PowerDefWeight = 2.2d, PowerSpdWeight = 0.8d, PowerSecWeight = 0.9d,
                DangerRatio = 1.15d, EasyRatio = 0.70d,
            };
        }

        // ----------------------------------------------------------------- energy
        /// <summary>The pool at <paramref name="nowUnix"/>, given what was stored and when. Pure —
        /// the service only writes the two numbers back after a spend.</summary>
        public static int EnergyAt(int stored, long stampUnix, long nowUnix, in Tuning t)
        {
            int max = t.EnergyMax < 1 ? 1 : t.EnergyMax;
            if (stored >= max) return max;
            if (stored < 0) stored = 0;
            if (t.EnergyRegenSeconds <= 0d || nowUnix <= stampUnix) return stored;
            long refilled = (long)((nowUnix - stampUnix) / t.EnergyRegenSeconds);
            long energy = stored + refilled;
            return energy >= max ? max : (int)energy;
        }

        /// <summary>Seconds until the next point, or 0 at the cap.</summary>
        public static double SecondsToNextEnergy(int stored, long stampUnix, long nowUnix, in Tuning t)
        {
            if (EnergyAt(stored, stampUnix, nowUnix, t) >= t.EnergyMax) return 0d;
            if (t.EnergyRegenSeconds <= 0d) return 0d;
            double since = (nowUnix - stampUnix) % t.EnergyRegenSeconds;
            return t.EnergyRegenSeconds - since;
        }

        // ------------------------------------------------------------------ stats
        /// <summary>One side's whole sheet. Hull doubles as our nerve — losing never sinks US,
        /// only drives us off, which is what keeps a loss costing nothing but the energy.</summary>
        public struct Stats
        {
            public double Hull, Shot, Def, Spd;
            public double Crit, Dodge, Stun, Mend, Burn, Plunder, Salvo, Steal, Poison;
        }

        /// <summary>The panel's headline number and the danger label's yardstick. A weighted read
        /// of the sheet — display maths, deliberately outside every fight formula.</summary>
        public static double PowerFor(in Stats s, in Tuning t)
        {
            double p = Math.Max(0d, s.Hull) * Math.Max(0d, t.PowerHullWeight)
                     + Math.Max(0d, s.Shot) * Math.Max(0d, t.PowerShotWeight)
                     + Math.Max(0d, s.Def) * Math.Max(0d, t.PowerDefWeight)
                     + Math.Max(0d, s.Spd) * Math.Max(0d, t.PowerSpdWeight);
            double sec = s.Crit + s.Dodge + s.Stun + s.Mend * 3d + s.Burn + s.Plunder * 0.5d
                       + s.Salvo + s.Steal * 1.5d + s.Poison;
            return p * (1d + Math.Max(0d, t.PowerSecWeight) * Math.Max(0d, sec));
        }

        /// <summary>0 = KOLAY, 1 = says nothing, 2 = TEHLİKELİ. A reading, never a rule.</summary>
        public static int Menace(double ourPower, double theirPower, in Tuning t)
        {
            if (ourPower < 1d) ourPower = 1d;
            double ratio = theirPower / ourPower;
            if (ratio >= Math.Max(1d, t.DangerRatio)) return 2;
            return ratio <= Clamp01(t.EasyRatio) ? 0 : 1;
        }

        /// <summary>Who fires the opening ball: the faster ship, ours on a tie. SÜRAT's whole
        /// meaning, and the details card says it before the fight is taken.</summary>
        public static bool UsOpens(in Stats ours, in Stats theirs) => ours.Spd >= theirs.Spd;

        // ---------------------------------------------------------------- enemies
        /// <summary>The Nth find of a voyage — deterministic, like <see cref="Goals.DailyIndex"/>.</summary>
        public static int KindFor(long sailedUnix, int index)
        {
            unchecked
            {
                long h = sailedUnix * 2654435761L + index * 40503L;
                h ^= h >> 13;
                h *= 1099511628211L;
                h ^= h >> 17;
                int k = (int)(h % KindCount);
                return k < 0 ? k + KindCount : k;
            }
        }

        /// <summary>An enemy's whole sheet: tier scale, kind flavour, signature secondary.</summary>
        public static Stats ThreatStats(int tier, int kind, in Tuning t)
        {
            int row = Clamp(tier, 0, ThreatHull.Length - 1);
            int k = Clamp(kind, 0, KindCount - 1);
            var s = new Stats
            {
                Hull = Math.Max(1d, ThreatHull[row] * KindHull[k]),
                Shot = Math.Max(0d, ThreatMenace[row] * KindMenace[k] * Math.Max(0d, t.EnemyShotScale)),
                Def = Math.Max(0d, ThreatDef[row] * KindDef[k]),
                Spd = Math.Max(0d, ThreatSpd[row] * KindSpd[k]),
            };
            switch (k)
            {
                case Raider:   s.Crit = Clamp01(t.RaiderCrit); break;
                case Beast:    s.Stun = Clamp01(t.BeastStun); break;
                case Fireship: s.Burn = Clamp01(t.FireshipBurn); break;
                case Ghost:    s.Dodge = Clamp01(t.GhostDodge); s.Mend = Clamp01(t.GhostMend); break;
            }
            return s;
        }

        /// <summary>The kind's signature as a Sec index for the details card's tag chip, or
        /// SecNone for the derelict — its tag is that it cannot answer.</summary>
        public static int SignatureOf(int kind)
        {
            switch (Clamp(kind, 0, KindCount - 1))
            {
                case Raider:   return SecCrit;
                case Beast:    return SecStun;
                case Fireship: return SecBurn;
                case Ghost:    return SecDodge;
                default:       return SecNone;
            }
        }

        // --------------------------------------------------------------- the ship
        /// <summary>A captain role's combat secondary: Gunner KRİTİK, Quartermaster MANEVRA,
        /// Bosun ONARIM, Purser YAĞMA — the roster's four jobs, given teeth at sea.</summary>
        public static int RoleSec(int role)
        {
            switch (role)
            {
                case Captains.Gunner:        return SecCrit;
                case Captains.Quartermaster: return SecDodge;
                case Captains.Bosun:         return SecMend;
                case Captains.Purser:        return SecPlunder;
                default:                     return SecNone;
            }
        }

        /// <summary>
        /// Our whole sheet, derived on the spot: crew track (hull + shot fraction), captain
        /// (worth multiplies shot — Gunner doubled — and the role pays its secondary), and the
        /// four worn items (<paramref name="gear"/> by slot; a null array or Grade &lt; 0 cell is
        /// an empty slot). Defence and speed come almost entirely from gear. Nothing here is ever
        /// stored.
        /// </summary>
        public static Stats OurStats(int captain, int captainLevel, int crewTrackLevel,
                                     Item[] gear, in Captains.Tuning ct, in Tuning t)
        {
            int crew = Clamp(crewTrackLevel, 0, Voyages.MaxShipLevel);
            double hull = Math.Max(1d, t.BaseNerve) + Math.Max(0d, t.NervePerCrewLevel) * crew;
            double flat = Math.Max(1d, t.PlayerShotBase);
            double def = Math.Max(0d, t.PlayerDefBase);
            double spd = Math.Max(0d, t.PlayerSpdBase);
            var s = new Stats();

            if (gear != null)
            {
                int n = gear.Length < SlotCount ? gear.Length : SlotCount;
                for (int i = 0; i < n; i++)
                {
                    if (gear[i].Grade < 0) continue;
                    hull += Math.Max(0d, gear[i].Hull);
                    flat += Math.Max(0d, gear[i].Shot);
                    def += Math.Max(0d, gear[i].Def);
                    spd += Math.Max(0d, gear[i].Spd);
                    AddSec(ref s, gear[i].Sec, gear[i].SecAmt);
                }
            }

            double crewMult = 1d + Math.Max(0d, t.ShotPerCrewLevel) * crew;
            double officer = 1d;
            if (Captains.Exists(captain) && captainLevel > Captains.NotOwned)
            {
                double worth = Captains.PerLevel(captain, ct) * Clamp(captainLevel, 0, Captains.MaxLevel);
                if (Captains.RoleOf(captain) == Captains.Gunner) worth *= Math.Max(1d, t.GunnerFightBonus);
                officer += worth;
                AddSec(ref s, RoleSec(Captains.RoleOf(captain)),
                       Captains.PerLevel(captain, ct) * Clamp(captainLevel, 0, Captains.MaxLevel)
                       * Math.Max(0d, t.RoleSecFactor));
            }

            s.Hull = hull;
            s.Shot = Math.Max(1d, flat * crewMult * officer);
            s.Def = def;
            s.Spd = spd;
            s.Crit = Math.Min(s.Crit, CritCap);
            s.Dodge = Math.Min(s.Dodge, DodgeCap);
            s.Stun = Math.Min(s.Stun, StunCap);
            s.Mend = Math.Min(s.Mend, MendCap);
            s.Burn = Math.Min(s.Burn, BurnCap);
            s.Plunder = Math.Min(s.Plunder, PlunderCap);
            s.Salvo = Math.Min(s.Salvo, SalvoCap);
            s.Steal = Math.Min(s.Steal, StealCap);
            s.Poison = Math.Min(s.Poison, PoisonCap);
            return s;
        }

        private static void AddSec(ref Stats s, int sec, double amount)
        {
            if (amount <= 0d) return;
            switch (sec)
            {
                case SecCrit:    s.Crit += amount; break;
                case SecDodge:   s.Dodge += amount; break;
                case SecStun:    s.Stun += amount; break;
                case SecMend:    s.Mend += amount; break;
                case SecBurn:    s.Burn += amount; break;
                case SecPlunder: s.Plunder += amount; break;
                case SecSalvo:   s.Salvo += amount; break;
                case SecSteal:   s.Steal += amount; break;
                case SecPoison:  s.Poison += amount; break;
            }
        }

        /// <summary>The spyglass's find-luck, from its grade alone (-1 = no glass). Out-of-fight
        /// work for an out-of-fight slot; its in-fight stats ride the item like everyone else's.</summary>
        public static double SpyglassLuck(int grade)
            => grade < 0 ? 0d : (Clamp(grade, 0, GradeMult.Length - 1) + 1) * 6d;

        // ------------------------------------------------------------------- gear
        /// <summary>
        /// One item, whole: where it goes, how rare it rolled, and what it does. Saved field by
        /// field (grade+1, hull, shot, def, spd, sec, secAmt) — the score is recomputed, never
        /// trusted.
        /// </summary>
        public struct Item
        {
            public int Slot, Grade, Sec;
            public double Hull, Shot, Def, Spd, SecAmt;
        }

        /// <summary>Which slot a win drops for. Flat across the four.</summary>
        public static int RollSlot(double roll)
        {
            if (roll < 0d) roll = 0d;
            if (roll >= 1d) roll = 0.9999999999d;
            return (int)(roll * SlotCount);
        }

        /// <summary>The drop's grade. Tier and spyglass luck push weight UP the table — the grind
        /// loop in one function: fight where you can, find better glass, fight further.</summary>
        public static int RollGrade(double roll, int tier, double luck, in Tuning t)
        {
            if (roll < 0d) roll = 0d;
            if (roll >= 1d) roll = 0.9999999999d;

            double bump = 1d + Math.Max(0d, t.DropTierBonus) * Clamp(tier, 0, Voyages.TierCount - 1)
                             + Math.Max(0d, t.DropLuckBonus) * Math.Max(0d, luck);
            double c = Math.Max(0d, t.DropCommon);
            double r = Math.Max(0d, t.DropRare) * bump;
            double e = Math.Max(0d, t.DropEpic) * bump;
            double l = Math.Max(0d, t.DropLegendary) * bump;
            double m = Math.Max(0d, t.DropMythic) * bump;
            double total = c + r + e + l + m;
            if (total <= 0d) return 0;

            double target = roll * total;
            if ((target -= c) < 0d) return 0;
            if ((target -= r) < 0d) return 1;
            if ((target -= e) < 0d) return 2;
            if ((target -= l) < 0d) return 3;
            return 4;
        }

        /// <summary>
        /// The same table <see cref="RollGrade"/> rolls against, read out as probabilities — what
        /// the panel prints so the player can see WHY the far reach is worth the danger, before
        /// they have spent an energy finding out.
        ///
        /// Fills <paramref name="into"/> (one cell per grade) and allocates nothing: the caller
        /// owns the array, because this is read on a UI refresh and a fresh array every half second
        /// is a garbage collection nobody asked for.
        ///
        /// A table that sums to nothing reads as all-Common, which is exactly what RollGrade
        /// returns in that case — the two must never disagree about a degenerate config.
        /// </summary>
        public static void GradeOdds(int tier, double luck, in Tuning t, double[] into)
        {
            if (into == null || into.Length < GradeMult.Length) return;
            for (int g = 0; g < GradeMult.Length; g++) into[g] = 0d;

            double bump = 1d + Math.Max(0d, t.DropTierBonus) * Clamp(tier, 0, Voyages.TierCount - 1)
                             + Math.Max(0d, t.DropLuckBonus) * Math.Max(0d, luck);
            into[0] = Math.Max(0d, t.DropCommon);
            into[1] = Math.Max(0d, t.DropRare) * bump;
            into[2] = Math.Max(0d, t.DropEpic) * bump;
            into[3] = Math.Max(0d, t.DropLegendary) * bump;
            into[4] = Math.Max(0d, t.DropMythic) * bump;

            double total = into[0] + into[1] + into[2] + into[3] + into[4];
            if (total <= 0d) { into[0] = 1d; return; }
            for (int g = 0; g < GradeMult.Length; g++) into[g] /= total;
        }

        /// <summary>
        /// Build the item a win dropped: all four core stats from the slot's tables x grade,
        /// and — RARE AND UP — one secondary from the slot's pool, sized by grade.
        /// <paramref name="secRoll"/> picks which; Common ignores it and carries none.
        /// </summary>
        public static Item ItemFor(int slot, int tier, int grade, double secRoll, in Tuning t)
        {
            int s = Clamp(slot, 0, SlotCount - 1);
            int row = Clamp(tier, 0, Voyages.TierCount - 1);
            int g = Clamp(grade, 0, GradeMult.Length - 1);
            var item = new Item
            {
                Slot = s,
                Grade = g,
                Sec = SecNone,
                Hull = Math.Round(SlotHull[s][row] * GradeMult[g], MidpointRounding.AwayFromZero),
                Shot = Math.Round(SlotShot[s][row] * GradeMult[g] * 10d, MidpointRounding.AwayFromZero) / 10d,
                Def = Math.Round(SlotDef[s][row] * GradeMult[g] * 10d, MidpointRounding.AwayFromZero) / 10d,
                Spd = Math.Round(SlotSpd[s][row] * GradeMult[g] * 10d, MidpointRounding.AwayFromZero) / 10d,
            };
            if (g > 0)
            {
                if (secRoll < 0d) secRoll = 0d;
                if (secRoll >= 1d) secRoll = 0.9999999999d;
                int[] pool = SlotSecPool[s];
                item.Sec = pool[(int)(secRoll * pool.Length)];
                item.SecAmt = SecBase[item.Sec] * SecGradeMult[g];
            }
            return item;
        }

        /// <summary>An item's own POWER — the compare number on the loot card, in the same weights
        /// the ship's headline uses so a +12 here is a +12 there.</summary>
        public static int ItemScore(in Item item, in Tuning t)
        {
            if (item.Grade < 0) return 0;
            var alone = new Stats { Hull = item.Hull, Shot = item.Shot, Def = item.Def, Spd = item.Spd };
            AddSec(ref alone, item.Sec, item.SecAmt);
            int n = (int)Math.Round(PowerFor(alone, t), MidpointRounding.AwayFromZero);
            return n < 1 ? 1 : n;
        }

        /// <summary>Salvage for scrapping a drop instead of wearing it.</summary>
        public static long ScrapFor(int grade)
            => ScrapSalvage[Clamp(grade, 0, ScrapSalvage.Length - 1)];

        // ------------------------------------------------------------------- loot
        public static int ChartsFor(int tier, int kind, in Voyages.Tuning vt, in Tuning t)
            => LootFor(vt.ChartRate, tier, kind, t.EncounterChartShare);

        public static int SalvageFor(int tier, int kind, in Voyages.Tuning vt, in Tuning t)
            => LootFor(vt.SalvageRate, tier, kind, t.EncounterSalvageShare);

        private static int LootFor(double rate, int tier, int kind, double share)
        {
            int row = Clamp(tier, 0, Voyages.TierCount - 1);
            double paid = Math.Max(0d, rate) * Voyages.PayoutMult[row]
                        * Math.Max(0d, share) * KindLoot[Clamp(kind, 0, KindCount - 1)];
            int n = (int)Math.Round(paid, MidpointRounding.AwayFromZero);
            return n < 1 ? 1 : n;
        }

        /// <summary>What one of our YAĞMA procs grabs on this route.</summary>
        public static long PlunderFor(int tier)
            => PlunderScrap[Clamp(tier, 0, PlunderScrap.Length - 1)];

        // ------------------------------------------------------------------ fight
        /// <summary>One side mid-fight: its sheet, its remaining hull, and its afflictions. A
        /// poison's bite is baked from the poisoner's shot at proc time — the venom does not
        /// care what its maker does afterwards.</summary>
        public struct Side
        {
            public Stats S;
            public double Hull, HullMax;
            public int BurnLeft;
            public int PoisonLeft;
            public double PoisonBite;
            public bool Stunned;
        }

        /// <summary>
        /// The exchange, as a value: one ball at a time, the faster side's first, until a hull
        /// gives. The scene owns WHEN each moment lands (so the ball's flight and the number
        /// agree); this owns WHAT it does. There are no buttons — the sheet is the fight.
        /// </summary>
        public struct Fight
        {
            public int Tier, Kind;
            public Side Us, Them;
            public bool UsOpen;
            public bool Over, Won;
        }

        /// <summary>The dice one shot needs. A roll of 1 can never proc — the deterministic tests
        /// pass exactly that.</summary>
        public struct ShotRolls
        {
            public double Dodge, Crit, Stun, Burn, Poison, Plunder, Salvo;

            public static ShotRolls None => new ShotRolls
                { Dodge = 1d, Crit = 1d, Stun = 1d, Burn = 1d, Poison = 1d, Plunder = 1d, Salvo = 1d };
        }

        /// <summary>What one shot did — everything the theater needs to say it out loud.</summary>
        public struct ShotReport
        {
            public bool Dodged, Crit, StunProc, BurnProc, PoisonProc, SalvoProc;
            public double Damage, Stolen;
            public long Plundered;
        }

        /// <summary>What a turn's opening did: the fires' bites and the mend's patch.</summary>
        public struct TurnReport
        {
            public double BurnDamage, PoisonDamage, Mended;
        }

        public static Fight Begin(int tier, int kind, in Stats ours, in Tuning t)
        {
            int row = Clamp(tier, 0, Voyages.TierCount - 1);
            int k = Clamp(kind, 0, KindCount - 1);
            Stats theirs = ThreatStats(row, k, t);
            return new Fight
            {
                Tier = row,
                Kind = k,
                Us = new Side { S = ours, Hull = Math.Max(1d, ours.Hull), HullMax = Math.Max(1d, ours.Hull) },
                Them = new Side { S = theirs, Hull = theirs.Hull, HullMax = theirs.Hull },
                UsOpen = UsOpens(ours, theirs),
            };
        }

        /// <summary>
        /// The top of a side's turn: the burn bites, the poison bites (either can end the fight),
        /// then the mend patches. Call once per turn, before asking whether the side is stunned.
        /// </summary>
        public static TurnReport TurnStart(ref Fight f, bool ours, in Tuning t)
        {
            var report = new TurnReport();
            if (f.Over) return report;

            ref Side side = ref ours ? ref f.Us : ref f.Them;
            if (side.BurnLeft > 0)
            {
                side.BurnLeft--;
                report.BurnDamage = side.HullMax * Math.Max(0d, t.BurnFrac);
                report.BurnDamage = Math.Round(report.BurnDamage * 10d) / 10d;
                side.Hull -= report.BurnDamage;
                if (side.Hull <= 0d)
                {
                    side.Hull = 0d;
                    f.Over = true;
                    f.Won = !ours;
                    return report;
                }
            }
            if (side.PoisonLeft > 0)
            {
                side.PoisonLeft--;
                report.PoisonDamage = Math.Round(Math.Max(0d, side.PoisonBite) * 10d) / 10d;
                side.Hull -= report.PoisonDamage;
                if (side.Hull <= 0d)
                {
                    side.Hull = 0d;
                    f.Over = true;
                    f.Won = !ours;
                    return report;
                }
            }
            if (side.S.Mend > 0d && side.Hull < side.HullMax)
            {
                report.Mended = Math.Min(side.HullMax - side.Hull, side.HullMax * side.S.Mend);
                report.Mended = Math.Round(report.Mended * 10d) / 10d;
                side.Hull += report.Mended;
            }
            return report;
        }

        /// <summary>
        /// A turn that does not fire: stunned, or nothing to fire with (the derelict). The stun
        /// is consumed — a held turn is still a turn.
        /// </summary>
        public static void TurnSkipped(ref Fight f, bool ours)
        {
            if (f.Over) return;
            if (ours) f.Us.Stunned = false;
            else f.Them.Stunned = false;
        }

        /// <summary>Whether their turn will put a ball in the air: not held, and armed at all.</summary>
        public static bool EnemyWillFire(in Fight f)
            => !f.Over && !f.Them.Stunned && f.Them.S.Shot > 0d;

        /// <summary>
        /// One ball lands. The whole chain in order: dodge voids it, SAVUNMA shaves it (never
        /// below MinShotFrac of the shot), crit multiplies what is left, CAN ÇALMA heals the
        /// attacker off it, then the hit's riders — stun, burn, poison, plunder — and last the
        /// salvo roll that offers another ball.
        /// </summary>
        public static ShotReport ShotLands(ref Fight f, bool ours, in ShotRolls r, in Tuning t)
        {
            var report = new ShotReport();
            if (f.Over) return report;

            ref Side attacker = ref ours ? ref f.Us : ref f.Them;
            ref Side defender = ref ours ? ref f.Them : ref f.Us;

            if (r.Dodge < defender.S.Dodge)
            {
                report.Dodged = true;
                return report;
            }

            double raw = attacker.S.Shot;
            double dmg = raw - Math.Max(0d, defender.S.Def);
            double least = raw * Clamp01(t.MinShotFrac);
            if (dmg < least) dmg = least;
            if (dmg < 1d) dmg = 1d;

            if (r.Crit < attacker.S.Crit)
            {
                report.Crit = true;
                dmg *= Math.Max(1d, t.CritMult);
            }

            dmg = Math.Round(dmg * 10d) / 10d;
            report.Damage = dmg;
            defender.Hull -= dmg;

            if (attacker.S.Steal > 0d && attacker.Hull < attacker.HullMax)
            {
                report.Stolen = Math.Min(attacker.HullMax - attacker.Hull, dmg * attacker.S.Steal);
                report.Stolen = Math.Round(report.Stolen * 10d) / 10d;
                attacker.Hull += report.Stolen;
            }

            if (defender.Hull <= 0d)
            {
                defender.Hull = 0d;
                f.Over = true;
                f.Won = ours;
            }

            if (r.Stun < attacker.S.Stun) { report.StunProc = true; defender.Stunned = true; }
            if (r.Burn < attacker.S.Burn) { report.BurnProc = true; defender.BurnLeft = Math.Max(1, t.BurnTurns); }
            if (r.Poison < attacker.S.Poison)
            {
                report.PoisonProc = true;
                defender.PoisonLeft = Math.Max(1, t.PoisonTurns);
                defender.PoisonBite = raw * Math.Max(0d, t.PoisonFrac);
            }
            if (ours && r.Plunder < attacker.S.Plunder)
                report.Plundered = PlunderFor(f.Tier);
            if (!f.Over && r.Salvo < attacker.S.Salvo) report.SalvoProc = true;
            return report;
        }

        private static double Clamp01(double v) => v < 0d ? 0d : (v > 1d ? 1d : v);

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
