using System;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// The workshop bench at runtime: the point balance, the lifetime salvage XP, the retooling
    /// stops, and the one crafted-but-undecided item. The maths is all in
    /// <see cref="Crafting"/> — this owns the dice, the save fields, and the wall clock.
    ///
    /// ONE DECISION PER CRAFT, NO INVENTORY — the same contract the sea's drops keep. A craft
    /// writes the item into the save as a PENDING cell in the same breath as spending the point,
    /// and the next craft is refused until it is worn or fed back. An app killed mid-decision
    /// finds the item on the bench; a point can never buy nothing.
    ///
    /// THE LEVEL IS NEVER STORED. It is recomputed from lifetime XP and the stops cleared, so the
    /// three numbers cannot drift apart; the stops themselves are the usual wall-clock deadline
    /// pair, stamped when the XP hits a 10th level and cleared on read once it passes.
    ///
    /// EVERY SCRAP TEACHES THE BENCH. <see cref="ExpeditionService"/> routes its own scraps
    /// through <see cref="GrantScrapXp"/>, so sea drops feed the same ladder crafted items do —
    /// one lesson per item, wherever it came from.
    /// </summary>
    public sealed class CraftingService
    {
        private readonly SaveData _data;
        private readonly SaveService _save;
        private readonly TimeService _time;
        private readonly Crafting.Tuning _tuning;
        private readonly SeaCombat.Tuning _combat;

        /// <summary>The craft dice. Here and not in Core — Crafting takes rolls and stays testable.</summary>
        private readonly Random _random;

        /// <summary>Set by the bootstrap once the sea service exists; wearing a crafted item goes
        /// through it so the displaced item is scrapped (and teaches) exactly like any other.</summary>
        public ExpeditionService Expeditions { get; set; }

        /// <summary>Raised on any move: a craft, a decision, points or XP landing, a stop opening.</summary>
        public event Action Changed;

        public CraftingService(SaveData data, SaveService save, TimeService time,
                               Crafting.Tuning? tuning = null, SeaCombat.Tuning? combat = null,
                               Random random = null)
        {
            _data = data;
            _save = save;
            _time = time;
            _tuning = tuning ?? Crafting.Tuning.Default;
            _combat = combat ?? SeaCombat.Tuning.Default;
            _random = random ?? new Random();
            Normalise();
            Tick(NowUnix());
        }

        /// <summary>
        /// A save from before the bench exists arrives all-zero, which is already correct. What is
        /// repaired here is damage: negatives clamped, a stop count past the ladder pulled back,
        /// and a pending cell whose slot or grade points off the tables cleared outright — a point
        /// refund would be invisible, a broken card on the bench would not be.
        /// </summary>
        private void Normalise()
        {
            if (_data == null) return;
            if (_data.craftPoints < 0L) _data.craftPoints = 0L;
            if (_data.craftXp < 0L) _data.craftXp = 0L;
            if (_data.craftGatesCleared < 0) _data.craftGatesCleared = 0;
            if (_data.craftGatesCleared > Crafting.GateCount) _data.craftGatesCleared = Crafting.GateCount;
            if (_data.craftGateEndUnix < 0L) _data.craftGateEndUnix = 0L;

            if (_data.craftPendingGrade < 0 || _data.craftPendingGrade > Captains.GradeCount
                || _data.craftPendingSlot < 0 || _data.craftPendingSlot >= SeaCombat.SlotCount)
                ClearPending();
        }

        private long NowUnix() => _time != null
            ? _time.NowUnix()
            : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // ------------------------------------------------------------------- read
        public Crafting.Tuning Tuning => _tuning;

        public long Points => _data != null ? _data.craftPoints : 0L;

        public long Xp => _data != null ? _data.craftXp : 0L;

        /// <summary>The bench's level: earned by XP, held down by the retooling stops.</summary>
        public int Level => _data != null ? Crafting.LevelAt(_data.craftXp, _data.craftGatesCleared) : 1;

        /// <summary>The stat-budget column crafts are built from right now.</summary>
        public int CurrentTier => _data != null ? Crafting.TierFor(_data.craftGatesCleared) : 0;

        /// <summary>Whether a retooling stop is holding the level down right now.</summary>
        public bool IsGated => _data != null && _data.craftGateEndUnix > 0L
                                             && NowUnix() < _data.craftGateEndUnix;

        /// <summary>Seconds until the running stop opens, 0 with none running.</summary>
        public double GateSecondsLeft
        {
            get
            {
                if (_data == null || _data.craftGateEndUnix <= 0L) return 0d;
                long left = _data.craftGateEndUnix - NowUnix();
                return left > 0L ? left : 0d;
            }
        }

        /// <summary>A grade's share at the CURRENT level — what the panel prints.</summary>
        public double OddsOf(int grade) => Crafting.OddsOf(Level, grade);

        /// <summary>Whether an undecided craft is sitting on the bench.</summary>
        public bool HasPending => _data != null && _data.craftPendingGrade > 0;

        /// <summary>The undecided item, rebuilt from its save cell. Grade is -1 with none.</summary>
        public SeaCombat.Item PendingItem()
        {
            if (!HasPending) return new SeaCombat.Item { Slot = 0, Grade = -1 };
            return new SeaCombat.Item
            {
                Slot = _data.craftPendingSlot,
                Grade = _data.craftPendingGrade - 1,
                Sec = _data.craftPendingSec,
                Hull = _data.craftPendingHull,
                Shot = _data.craftPendingShot,
                Def = _data.craftPendingDef,
                Spd = _data.craftPendingSpd,
                SecAmt = _data.craftPendingSecAmt,
            };
        }

        // ------------------------------------------------------------------ gates
        /// <summary>
        /// Moves the stops on: stamp a deadline when the XP has hit a 10th level, open the stop
        /// once the deadline passes — and go round again, because banked XP can already be sitting
        /// on the NEXT stop the moment one opens. Returns whether anything moved. The rare
        /// transitions are saved on the spot: a stop's clock restarting after a crash would be
        /// hours handed back.
        /// </summary>
        private bool Tick(long now)
        {
            if (_data == null) return false;
            bool moved = false;
            for (int guard = 0; guard <= Crafting.GateCount; guard++)
            {
                if (_data.craftGateEndUnix > 0L)
                {
                    if (now < _data.craftGateEndUnix) break;
                    _data.craftGateEndUnix = 0L;
                    if (_data.craftGatesCleared < Crafting.GateCount) _data.craftGatesCleared++;
                    moved = true;
                    continue;
                }
                if (!Crafting.AtGate(_data.craftXp, _data.craftGatesCleared)) break;
                double seconds = Crafting.GateSeconds(_data.craftGatesCleared, _tuning);
                if (seconds <= 0d)   // a zeroed stop opens itself — the dev-tuning escape hatch
                {
                    _data.craftGatesCleared++;
                    moved = true;
                    continue;
                }
                _data.craftGateEndUnix = now + (long)seconds;
                moved = true;
                break;
            }
            if (moved) _save?.Save(_data);
            return moved;
        }

        /// <summary>The panel's once-a-second pulse: opens a stop whose deadline has passed.</summary>
        public void Poll()
        {
            if (Tick(NowUnix())) Changed?.Invoke();
        }

        // ------------------------------------------------------------------ craft
        /// <summary>
        /// Spend one craft's points and roll the item: slot flat across the four, grade off the
        /// CURRENT level's bracket, stats off the cleared stops' tier column. The item lands in
        /// the pending cell and the save is written before this returns — see the class header.
        /// Refused while an earlier craft is still undecided, or short of points.
        /// </summary>
        public bool TryCraft(out SeaCombat.Item item)
        {
            item = new SeaCombat.Item { Slot = 0, Grade = -1 };
            if (_data == null) return false;
            Tick(NowUnix());
            if (HasPending) return false;
            long cost = _tuning.CraftCost < 0L ? 0L : _tuning.CraftCost;
            if (_data.craftPoints < cost) return false;

            _data.craftPoints -= cost;
            int slot = SeaCombat.RollSlot(_random.NextDouble());
            int grade = Crafting.RollGrade(_random.NextDouble(), Level);
            int tier = Crafting.TierFor(_data.craftGatesCleared);
            item = SeaCombat.ItemFor(slot, tier, grade, _random.NextDouble(), _combat);

            _data.craftPendingGrade = item.Grade + 1;
            _data.craftPendingSlot = item.Slot;
            _data.craftPendingSec = item.Sec;
            _data.craftPendingHull = item.Hull;
            _data.craftPendingShot = item.Shot;
            _data.craftPendingDef = item.Def;
            _data.craftPendingSpd = item.Spd;
            _data.craftPendingSecAmt = item.SecAmt;

            _save?.Save(_data);
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Feed the pending item back to the bench: the grade's hurda, plus its lesson in XP.
        /// Returns the hurda paid; <paramref name="xp"/> is what it taught. 0/0 with nothing pending.
        /// </summary>
        public long SalvagePending(out long xp)
        {
            xp = 0L;
            if (_data == null || !HasPending) return 0L;
            int grade = _data.craftPendingGrade - 1;
            long scrap = SeaCombat.ScrapFor(grade);
            xp = Crafting.SalvageXpFor(grade);
            _data.salvage += scrap;
            _data.craftXp += xp;
            ClearPending();
            Tick(NowUnix());
            _save?.Save(_data);
            Changed?.Invoke();
            return scrap;
        }

        /// <summary>
        /// Wear the pending item. Goes through <see cref="ExpeditionService.Equip"/>, so whatever
        /// it displaces is scrapped — and teaches — exactly like any other scrap. Returns the
        /// displaced item's hurda; false-equivalent is -1 with nothing pending or no sea service.
        /// </summary>
        public long EquipPending()
        {
            if (_data == null || !HasPending || Expeditions == null) return -1L;
            SeaCombat.Item item = PendingItem();
            ClearPending();
            long scrap = Expeditions.Equip(item);
            _save?.Save(_data);
            Changed?.Invoke();
            return scrap;
        }

        private void ClearPending()
        {
            _data.craftPendingGrade = 0;
            _data.craftPendingSlot = 0;
            _data.craftPendingSec = SeaCombat.SecNone;
            _data.craftPendingHull = 0d;
            _data.craftPendingShot = 0d;
            _data.craftPendingDef = 0d;
            _data.craftPendingSpd = 0d;
            _data.craftPendingSecAmt = 0d;
        }

        // --------------------------------------------------------------- income
        /// <summary>
        /// A scrapped item's lesson, from wherever the scrap happened — the sea's SÖK buttons
        /// route through here. No save of its own: the drips ride the pause/quit save like every
        /// other sea trickle, and the rare stop-stamp inside Tick saves itself.
        /// </summary>
        public void GrantScrapXp(int grade)
        {
            if (_data == null || grade < 0) return;
            _data.craftXp += Crafting.SalvageXpFor(grade);
            Tick(NowUnix());
            Changed?.Invoke();
        }

        /// <summary>A won encounter's point roll. <paramref name="roll"/> is in [0,1).</summary>
        public bool TryDropPoint(double roll)
        {
            if (_data == null || _tuning.PointsPerWin <= 0) return false;
            if (roll >= _tuning.PointDropChance) return false;
            _data.craftPoints += _tuning.PointsPerWin;
            Changed?.Invoke();
            return true;
        }

        /// <summary>A claimed voyage's flat points.</summary>
        public void OnVoyageClaimed()
        {
            if (_data == null || _tuning.PointsPerVoyage <= 0) return;
            _data.craftPoints += _tuning.PointsPerVoyage;
            Changed?.Invoke();
        }

        /// <summary>Points from anywhere else — a store pack, the dev TEST grant.</summary>
        public void AddPoints(long amount)
        {
            if (_data == null || amount <= 0L) return;
            _data.craftPoints += amount;
            Changed?.Invoke();
        }
    }
}
