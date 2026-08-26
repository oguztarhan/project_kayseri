using System;

namespace Game.Core
{
    /// <summary>
    /// The sea adventure as pure maths, rebuilt to the reference game's full shape: a STAT BLOCK
    /// instead of two numbers, items that roll stats, enemies with signature abilities, and a turn
    /// engine where every shot can crit, miss, stun, burn, plunder or chain. What used to be
    /// shot/nerve/cut is now nine stats — the same nine the reference sheet shows (HP, ATK, CRIT,
    /// DODGE, COMBO, STUN, REGEN, STEAL, POISON), each renamed for a ship:
    ///
    ///   CESARET (hull/nerve) · TOP (shot) · KRİTİK (crit) · MANEVRA (dodge) · SALVO (extra shot)
    ///   SERSEMLETME (stun) · ONARIM (mend) · YAĞMA (plunder) · YANGIN (burn)
    ///
    /// THE RULE STILL HOLDS (Docs/FIVE_LAYERS.md §4): nothing here can touch the voyage. A lost
    /// fight costs the energy it cost and nothing else. ENERGY IS THE GOVERNOR — one search, one
    /// energy, wall-clock refill. GEAR IS A CLOSED LOOP — an item's stats exist only inside this
    /// file's fights (the one honest exception: YAĞMA pays salvage, which is already the sea's own
    /// closed currency).
    ///
    /// WHERE POWER COMES FROM, unchanged in spirit: DERIVED, never stored. The ship's stat block is
    /// rebuilt every fight from the crew track, the captain (each ROLE now carries its own
    /// secondary — Gunner crits, Quartermaster dodges, Bosun mends, Purser plunders) and the four
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
        /// The four gear slots. Two dress the ship, two the captain. Saves address these by index —
        /// never reorder. TOP is the cannon, ZIRH the plating, DÜRBÜN the spyglass (also the drop
        /// luck), TILSIM the charm.
        /// </summary>
        public const int SlotCannon = 0, SlotPlating = 1, SlotSpyglass = 2, SlotCharm = 3;
        public const int SlotCount = 4;

        /// <summary>No item in the slot. The save stores grade + 1 — 0 is empty.</summary>
        public const int GearEmpty = 0;

        /// <summary>
        /// The secondary stats an item (or a role, or an enemy kind) can carry. Saved by index in
        /// seaGearSec — append only. None is 0 so an empty save cell reads as no secondary.
        /// </summary>
        public const int SecNone = 0, SecCrit = 1, SecDodge = 2, SecStun = 3, SecMend = 4,
                         SecBurn = 5, SecPlunder = 6, SecSalvo = 7;
        public const int SecCount = 8;

        // Chance stats are capped so no stack of gear turns a fight into a coin with one face.
        // MEND is per-turn healing as a fraction of max hull and capped hardest — an outheal loop
        // would make the far reach a stalemate instead of a wall.
        public const double CritCap = 0.60d, DodgeCap = 0.50d, StunCap = 0.40d, MendCap = 0.10d,
                            BurnCap = 0.60d, PlunderCap = 0.50d, SalvoCap = 0.50d;

        // Per-tier enemy scale, and per-kind flavour on top of it.
        public static readonly double[] ThreatHull   = { 90d, 160d, 300d, 520d };
        public static readonly double[] ThreatMenace = { 2.5d, 4d, 7d, 11d };
        public static readonly double[] KindHull   = { 1d, 1.6d, 0.55d, 0.8d, 0.9d };
        public static readonly double[] KindMenace = { 1d, 0.7d, 0d, 0.9d, 0.8d };
        public static readonly double[] KindLoot   = { 1d, 1.25d, 0.8d, 1.15d, 1.1d };

        /// <summary>What one grade is worth as a multiplier on an item's stat budget.</summary>
        public static readonly double[] GradeMult = { 1d, 1.5d, 2.2d, 3.2d, 4.5d };

        /// <summary>
        /// Every item now carries BOTH hull and shot, split by the slot's nature — a cannon is
        /// shot-heavy, plating hull-heavy — exactly the reference game's HP+ATK on every piece.
        /// Rows are slots, columns tiers: what a Common drop from that route rolls.
        /// </summary>
        public static readonly double[][] SlotHull =
        {
            new[] { 6d, 10d, 17d, 28d },        // cannon
            new[] { 26d, 44d, 74d, 120d },      // plating
            new[] { 10d, 17d, 28d, 46d },       // spyglass
            new[] { 20d, 34d, 56d, 92d },       // charm
        };
        public static readonly double[][] SlotShot =
        {
            new[] { 3.5d, 6d, 10d, 17d },
            new[] { 0.6d, 1d, 1.7d, 2.8d },
            new[] { 1.8d, 3d, 5d, 8.5d },
            new[] { 1.1d, 1.9d, 3.2d, 5.2d },
        };

        /// <summary>
        /// Which secondaries a slot can roll — the slot's flavour, like the reference game's
        /// poison-on-gems. Every secondary is reachable from at least one slot.
        /// </summary>
        public static readonly int[][] SlotSecPool =
        {
            new[] { SecCrit, SecSalvo, SecBurn },       // cannon: ways to hit harder
            new[] { SecDodge, SecStun, SecMend },       // plating: ways to be hit less
            new[] { SecCrit, SecPlunder, SecDodge },    // spyglass: the sharp eye
            new[] { SecMend, SecPlunder, SecSalvo, SecStun },   // charm: a little of anything
        };

        /// <summary>A secondary's value at RARE, by Sec index; SecGradeMult scales it up-grade.
        /// COMMON ITEMS CARRY NO SECONDARY — that is what makes a rare drop feel different in kind,
        /// not just in size.</summary>
        public static readonly double[] SecBase =
            { 0d, 0.05d, 0.04d, 0.04d, 0.015d, 0.06d, 0.08d, 0.05d };
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

            /// <summary>Their per-shot damage = menace x this.</summary>
            public double EnemyShotScale;

            /// <summary>A crit's multiplier, a burn's length in the victim's turns, and what each
            /// burning turn costs as a fraction of max hull.</summary>
            public double CritMult;
            public int BurnTurns;
            public double BurnFrac;

            /// <summary>A captain's role secondary = roster worth x this, capped by the stat's cap.
            /// Gunner to KRİTİK, Quartermaster to MANEVRA, Bosun to ONARIM, Purser to YAĞMA.</summary>
            public double RoleSecFactor;

            /// <summary>The kinds' signature chances — what the details card warns about.</summary>
            public double RaiderCrit, BeastStun, FireshipBurn, GhostDodge, GhostMend;

            /// <summary>BORDA arms the NEXT shot x this; cooldowns count TURNS.</summary>
            public double BroadsideMult;
            public int BroadsideCdTurns;

            /// <summary>SİPER: the next LANDING shot x this. A dodged shot does not spend it.</summary>
            public double BraceFactor;
            public int BraceCdTurns;

            /// <summary>KANCA: the enemy's next turn simply does not happen.</summary>
            public int HookCdTurns;

            /// <summary>A win's chart/salvage trickle, as a share of a full hold.</summary>
            public double EncounterChartShare, EncounterSalvageShare;

            /// <summary>Grade odds for a drop, shifted up by tier and by spyglass luck.</summary>
            public double DropCommon, DropRare, DropEpic, DropLegendary, DropMythic;
            public double DropTierBonus, DropLuckBonus;

            /// <summary>The POWER formula's weights — the one number the panel leads with, and what
            /// the TEHLİKELİ label compares. A label, never a mechanic.</summary>
            public double PowerHullWeight, PowerShotWeight, PowerSecWeight;

            /// <summary>Enemy power over ours: above DangerRatio reads TEHLİKELİ, below EasyRatio
            /// reads KOLAY, between reads nothing.</summary>
            public double DangerRatio, EasyRatio;

            public static Tuning Default => new Tuning
            {
                // 30 deep so a sitting is a real grind session (~30 fights), 5 minutes a point so
                // the pool is back in 2.5 hours — the app-shut refill every timer here keeps.
                EnergyMax = 30,
                EnergyRegenSeconds = 300d,

                SearchSeconds = 0.9d,
                ApproachSeconds = 1.2d,
                TurnAimSeconds = 0.55d,
                TurnFlightSeconds = 0.45d,

                // Worked by hand against the tables above, proc-free: tier 0 falls to watching,
                // tier 1 to the buttons, tier 2 up only to gear — the grind the drops feed.
                // SeaCombatTests pins all three rungs.
                PlayerShotBase = 18d, ShotPerCrewLevel = 0.06d,
                GunnerFightBonus = 2d,
                BaseNerve = 100d, NervePerCrewLevel = 8d,
                EnemyShotScale = 3.4d,

                CritMult = 2.0d,
                BurnTurns = 3, BurnFrac = 0.06d,
                RoleSecFactor = 0.12d,
                RaiderCrit = 0.22d, BeastStun = 0.25d, FireshipBurn = 0.40d,
                GhostDodge = 0.30d, GhostMend = 0.03d,

                BroadsideMult = 2.2d, BroadsideCdTurns = 3,
                BraceFactor = 0.35d, BraceCdTurns = 3,
                HookCdTurns = 4,

                EncounterChartShare = 0.12d, EncounterSalvageShare = 0.12d,

                DropCommon = 0.52d, DropRare = 0.27d, DropEpic = 0.13d,
                DropLegendary = 0.06d, DropMythic = 0.02d,
                DropTierBonus = 0.35d, DropLuckBonus = 0.04d,

                PowerHullWeight = 0.55d, PowerShotWeight = 3.2d, PowerSecWeight = 0.9d,
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
            public double Hull, Shot;
            public double Crit, Dodge, Stun, Mend, Burn, Plunder, Salvo;
        }

        /// <summary>The panel's headline number and the danger label's yardstick. A weighted read
        /// of the sheet — display maths, deliberately outside every fight formula.</summary>
        public static double PowerFor(in Stats s, in Tuning t)
        {
            double p = Math.Max(0d, s.Hull) * Math.Max(0d, t.PowerHullWeight)
                     + Math.Max(0d, s.Shot) * Math.Max(0d, t.PowerShotWeight);
            double sec = s.Crit + s.Dodge + s.Stun + s.Mend * 3d + s.Burn + s.Plunder * 0.5d + s.Salvo;
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
        /// an empty slot). Nothing here is ever stored.
        /// </summary>
        public static Stats OurStats(int captain, int captainLevel, int crewTrackLevel,
                                     Item[] gear, in Captains.Tuning ct, in Tuning t)
        {
            int crew = Clamp(crewTrackLevel, 0, Voyages.MaxShipLevel);
            double hull = Math.Max(1d, t.BaseNerve) + Math.Max(0d, t.NervePerCrewLevel) * crew;
            double flat = Math.Max(1d, t.PlayerShotBase);
            var s = new Stats();

            if (gear != null)
            {
                int n = gear.Length < SlotCount ? gear.Length : SlotCount;
                for (int i = 0; i < n; i++)
                {
                    if (gear[i].Grade < 0) continue;
                    hull += Math.Max(0d, gear[i].Hull);
                    flat += Math.Max(0d, gear[i].Shot);
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
            s.Crit = Math.Min(s.Crit, CritCap);
            s.Dodge = Math.Min(s.Dodge, DodgeCap);
            s.Stun = Math.Min(s.Stun, StunCap);
            s.Mend = Math.Min(s.Mend, MendCap);
            s.Burn = Math.Min(s.Burn, BurnCap);
            s.Plunder = Math.Min(s.Plunder, PlunderCap);
            s.Salvo = Math.Min(s.Salvo, SalvoCap);
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
            }
        }

        /// <summary>The spyglass's find-luck, from its grade alone (-1 = no glass). Out-of-fight
        /// work for an out-of-fight slot; its in-fight stats ride the item like everyone else's.</summary>
        public static double SpyglassLuck(int grade)
            => grade < 0 ? 0d : (Clamp(grade, 0, GradeMult.Length - 1) + 1) * 6d;

        // ------------------------------------------------------------------- gear
        /// <summary>
        /// One item, whole: where it goes, how rare it rolled, and what it does. Saved field by
        /// field (grade+1, hull, shot, sec, secAmt) — the score is recomputed, never trusted.
        /// </summary>
        public struct Item
        {
            public int Slot, Grade, Sec;
            public double Hull, Shot, SecAmt;
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
        /// Build the item a win dropped: hull and shot from the slot's tables x grade, and — RARE
        /// AND UP — one secondary from the slot's pool, sized by grade. <paramref name="secRoll"/>
        /// picks which; Common ignores it and carries none.
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
            var alone = new Stats { Hull = item.Hull, Shot = item.Shot };
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
        /// <summary>One side mid-fight: its sheet, its remaining hull, and its afflictions.</summary>
        public struct Side
        {
            public Stats S;
            public double Hull, HullMax;
            public int BurnLeft;
            public bool Stunned;
        }

        /// <summary>
        /// The exchange, as a value: our shot, then theirs, until a hull gives. The scene owns WHEN
        /// each moment lands (so the ball's flight and the number agree); this owns WHAT it does.
        /// KANCA is Them.Stunned — the hook and a beast's stun are the same held turn.
        /// </summary>
        public struct Fight
        {
            public int Tier, Kind;
            public Side Us, Them;
            public bool VolleyArmed, BraceArmed;
            public int VolleyCd, BraceCd, HookCd;
            public bool Over, Won;
        }

        /// <summary>The dice one shot needs. A roll of 1 can never proc — the deterministic tests
        /// pass exactly that.</summary>
        public struct ShotRolls
        {
            public double Dodge, Crit, Stun, Burn, Plunder, Salvo;

            public static ShotRolls None => new ShotRolls
                { Dodge = 1d, Crit = 1d, Stun = 1d, Burn = 1d, Plunder = 1d, Salvo = 1d };
        }

        /// <summary>What one shot did — everything the theater needs to say it out loud.</summary>
        public struct ShotReport
        {
            public bool Dodged, Crit, Braced, StunProc, BurnProc, SalvoProc;
            public double Damage;
            public long Plundered;
        }

        /// <summary>What a turn's opening did: the burn's bite and the mend's patch.</summary>
        public struct TurnReport
        {
            public double BurnDamage, Mended;
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
            };
        }

        /// <summary>
        /// The top of a side's turn: the burn bites (and can end the fight), then the mend patches.
        /// Call once per turn, before asking whether the side is stunned.
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
            if (side.S.Mend > 0d && side.Hull < side.HullMax)
            {
                report.Mended = Math.Min(side.HullMax - side.Hull, side.HullMax * side.S.Mend);
                report.Mended = Math.Round(report.Mended * 10d) / 10d;
                side.Hull += report.Mended;
            }
            return report;
        }

        /// <summary>
        /// A turn that does not fire: stunned (theirs by KANCA or our stun procs, ours by a
        /// beast's), or nothing to fire with (the derelict). The stun is consumed and the side's
        /// cooldowns still tick — a held turn is still a turn.
        /// </summary>
        public static void TurnSkipped(ref Fight f, bool ours)
        {
            if (f.Over) return;
            if (ours)
            {
                if (f.VolleyCd > 0) f.VolleyCd--;
                f.Us.Stunned = false;
            }
            else
            {
                if (f.BraceCd > 0) f.BraceCd--;
                if (f.HookCd > 0) f.HookCd--;
                f.Them.Stunned = false;
            }
        }

        /// <summary>Whether their turn will put a ball in the air: not held, and armed at all.</summary>
        public static bool EnemyWillFire(in Fight f)
            => !f.Over && !f.Them.Stunned && f.Them.S.Shot > 0d;

        /// <summary>
        /// One ball lands. The whole proc chain in order: dodge voids it (and spends nothing but
        /// the volley's powder), crit doubles it, SİPER softens it, then the hit's riders — stun,
        /// burn, plunder — and last the salvo roll that offers another ball. Cooldowns tick here:
        /// ours on our shots, theirs on their turns, exactly as the buttons' "N more turns" reads.
        /// </summary>
        public static ShotReport ShotLands(ref Fight f, bool ours, in ShotRolls r, in Tuning t)
        {
            var report = new ShotReport();
            if (f.Over) return report;

            ref Side attacker = ref ours ? ref f.Us : ref f.Them;
            ref Side defender = ref ours ? ref f.Them : ref f.Us;

            double dmg = attacker.S.Shot;
            if (ours)
            {
                if (f.VolleyArmed) { dmg *= Math.Max(1d, t.BroadsideMult); f.VolleyArmed = false; }
                if (f.VolleyCd > 0) f.VolleyCd--;
            }
            else
            {
                if (f.BraceCd > 0) f.BraceCd--;
                if (f.HookCd > 0) f.HookCd--;
            }

            if (r.Dodge < defender.S.Dodge)
            {
                report.Dodged = true;
                return report;
            }

            if (r.Crit < attacker.S.Crit)
            {
                report.Crit = true;
                dmg *= Math.Max(1d, t.CritMult);
            }
            if (!ours && f.BraceArmed)
            {
                report.Braced = true;
                dmg *= Clamp01(t.BraceFactor);
                f.BraceArmed = false;
            }

            dmg = Math.Round(dmg * 10d) / 10d;
            report.Damage = dmg;
            defender.Hull -= dmg;
            if (defender.Hull <= 0d)
            {
                defender.Hull = 0d;
                f.Over = true;
                f.Won = ours;
            }

            if (r.Stun < attacker.S.Stun) { report.StunProc = true; defender.Stunned = true; }
            if (r.Burn < attacker.S.Burn) { report.BurnProc = true; defender.BurnLeft = Math.Max(1, t.BurnTurns); }
            if (ours && r.Plunder < attacker.S.Plunder)
                report.Plundered = PlunderFor(f.Tier);
            if (!f.Over && r.Salvo < attacker.S.Salvo) report.SalvoProc = true;
            return report;
        }

        public static bool TryBroadside(ref Fight f, in Tuning t)
        {
            if (f.Over || f.VolleyArmed || f.VolleyCd > 0) return false;
            f.VolleyArmed = true;
            f.VolleyCd = Math.Max(1, t.BroadsideCdTurns);
            return true;
        }

        public static bool TryBrace(ref Fight f, in Tuning t)
        {
            if (f.Over || f.BraceArmed || f.BraceCd > 0) return false;
            f.BraceArmed = true;
            f.BraceCd = Math.Max(1, t.BraceCdTurns);
            return true;
        }

        /// <summary>KANCA holds their next turn — the same Stunned flag a stun proc raises, so a
        /// held turn has exactly one meaning everywhere.</summary>
        public static bool TryGrapple(ref Fight f, in Tuning t)
        {
            if (f.Over || f.Them.Stunned || f.HookCd > 0) return false;
            f.Them.Stunned = true;
            f.HookCd = Math.Max(1, t.HookCdTurns);
            return true;
        }

        private static double Clamp01(double v) => v < 0d ? 0d : (v > 1d ? 1d : v);

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
