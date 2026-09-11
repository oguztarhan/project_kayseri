using System;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// The captain's mining loadout at runtime: the four worn slots, the Mining Points that pay for a
    /// craft, and the wall-clock accrual that fills them. The maths is all in <see cref="MiningGear"/>
    /// — this owns the dice, the save fields and the clock.
    ///
    /// ONE CRAFT, ONE OUTCOME. Unlike <see cref="CraftingService"/>'s bench, there is no pending cell
    /// and no shelf: a craft is compared to what is worn on the spot and the better of the two is kept,
    /// the other scrapped. See <see cref="MiningGear"/>'s header for why that is enough here and is not
    /// enough for <see cref="Game.Core.GearStash"/>.
    ///
    /// THE MULTIPLIER IS CACHED, NOT RECOMPUTED PER READ. <see cref="IncomeMultiplier"/> is read by
    /// <see cref="MarketService"/> on every tick, so it is a field updated only when the loadout
    /// actually changes rather than a sum walked on every call — the same reason nothing here builds a
    /// fresh array to hand to <see cref="MiningGear.IncomeMultiplier"/>.
    /// </summary>
    public sealed class MiningGearService
    {
        private readonly SaveData _data;
        private readonly SaveService _save;
        private readonly TimeService _time;
        private readonly MiningGear.Tuning _tuning;
        private readonly Random _random;

        // Reused rather than allocated per craft or per income read — see the class header.
        private readonly int[] _gradesBuf = new int[MiningGear.SlotCount];
        private double _incomeMultiplier = 1d;

        /// <summary>Raised on any move: a craft, a scrap, points landing.</summary>
        public event Action Changed;

        public MiningGearService(SaveData data, SaveService save, TimeService time,
                                 MiningGear.Tuning? tuning = null, Random random = null)
        {
            _data = data;
            _save = save;
            _time = time;
            _tuning = tuning ?? MiningGear.Tuning.Default;
            _random = random ?? new Random();
            Normalise();
            AccruePoints(NowUnix());
            RecomputeIncomeMultiplier();
        }

        /// <summary>
        /// A save from before mining gear existed arrives all-zero, which already reads as an empty
        /// loadout and a full purse of nothing. What is repaired here is damage: negatives clamped, a
        /// short or null grade array padded like every other roster array in this save, and a grade
        /// cell outside the table reset to empty rather than trusted.
        /// </summary>
        private void Normalise()
        {
            if (_data == null) return;
            if (_data.miningPoints < 0L) _data.miningPoints = 0L;
            if (_data.miningPointsStampUnix < 0L) _data.miningPointsStampUnix = 0L;
            if (_data.miningScrap < 0L) _data.miningScrap = 0L;

            _data.miningGearGrade = Fit(_data.miningGearGrade, MiningGear.SlotCount);
            for (int i = 0; i < _data.miningGearGrade.Length; i++)
            {
                int cell = _data.miningGearGrade[i];
                if (cell < 0 || cell > Captains.GradeCount) _data.miningGearGrade[i] = 0;
            }
        }

        private static int[] Fit(int[] src, int len)
        {
            if (src != null && src.Length == len) return src;
            var fitted = new int[len];
            if (src != null)
            {
                int n = src.Length < len ? src.Length : len;
                for (int i = 0; i < n; i++) fitted[i] = src[i];
            }
            return fitted;
        }

        private long NowUnix() => _time != null
            ? _time.NowUnix()
            : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // ------------------------------------------------------------------- read
        public MiningGear.Tuning Tuning => _tuning;

        public long Points => _data != null ? _data.miningPoints : 0L;

        public long PointCap => _tuning.PointCap < 0L ? 0L : _tuning.PointCap;

        public long CraftCost => _tuning.CraftCost < 0L ? 0L : _tuning.CraftCost;

        public long Scrap => _data != null ? _data.miningScrap : 0L;

        public bool CanCraft => _data != null && _data.miningPoints >= CraftCost;

        /// <summary>Scrap required to target a slot. Empty slots use the Common rung.</summary>
        public long TargetedScrapCost(int slot)
        {
            return slot < 0 || slot >= MiningGear.SlotCount
                ? long.MaxValue
                : MiningGear.TargetedScrapCost(WornGrade(slot));
        }

        public bool CanTargetedCraft(int slot)
        {
            long scrapCost = TargetedScrapCost(slot);
            return _data != null && scrapCost != long.MaxValue
                && _data.miningPoints >= CraftCost && _data.miningScrap >= scrapCost;
        }

        /// <summary>The island income multiplier this loadout is worth right now.</summary>
        public double IncomeMultiplier => _incomeMultiplier;

        /// <summary>The grade worn in a slot, or <see cref="MiningGear.NoGrade"/> for empty or an
        /// out-of-range slot.</summary>
        public int WornGrade(int slot)
        {
            if (_data == null || slot < 0 || slot >= MiningGear.SlotCount) return MiningGear.NoGrade;
            return _data.miningGearGrade[slot] - 1;
        }

        /// <summary>Seconds until the pool's next point, 0 once the cap is standing full.</summary>
        public double SecondsToNextPoint
        {
            get
            {
                if (_data == null) return 0d;
                if (PointCap > 0L && _data.miningPoints >= PointCap) return 0d;
                double tick = _tuning.TickSeconds <= 0d ? 1d : _tuning.TickSeconds;
                if (_data.miningPointsStampUnix <= 0L) return tick;
                long into = NowUnix() - _data.miningPointsStampUnix;
                double remainder = tick - into % tick;
                return remainder <= 0d ? tick : remainder;
            }
        }

        private void RecomputeIncomeMultiplier()
        {
            if (_data == null) { _incomeMultiplier = 1d; return; }
            for (int i = 0; i < MiningGear.SlotCount; i++) _gradesBuf[i] = WornGrade(i);
            _incomeMultiplier = MiningGear.IncomeMultiplier(_gradesBuf, _tuning);
        }

        // ---------------------------------------------------------------- points
        /// <summary>
        /// Advances the point pool off the wall clock — the captain earns duty pay just by being
        /// assigned, the same shape <c>seaEnergy</c> regenerates in. The stamp only ever advances by
        /// whole ticks consumed, so a part-tick in progress is never lost to a read that lands mid-tick.
        /// </summary>
        private bool AccruePoints(long now)
        {
            if (_data == null) return false;
            if (_data.miningPointsStampUnix <= 0L)
            {
                _data.miningPointsStampUnix = now;
                return false;
            }

            long elapsed = now - _data.miningPointsStampUnix;
            double tick = _tuning.TickSeconds <= 0d ? 1d : _tuning.TickSeconds;
            long ticks = (long)(elapsed / tick);
            if (ticks <= 0L) return false;

            long perTick = _tuning.PointsPerTick < 0 ? 0 : _tuning.PointsPerTick;
            long total = _data.miningPoints + ticks * perTick;
            long cap = PointCap;
            if (cap > 0L && total > cap) total = cap;

            bool changed = total != _data.miningPoints;
            _data.miningPoints = total;
            _data.miningPointsStampUnix += (long)(ticks * tick);
            return changed;
        }

        /// <summary>The panel's once-a-second pulse.</summary>
        public void Poll()
        {
            if (!AccruePoints(NowUnix())) return;
            _save?.Save(_data);
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ craft
        /// <summary>One craft's outcome. <see cref="Crafted"/> is false when either required balance
        /// was short — nothing else on the struct means anything in that case.</summary>
        public readonly struct CraftResult
        {
            public readonly bool Crafted;
            public readonly bool Targeted;
            public readonly int Slot;
            public readonly int Grade;
            public readonly bool Equipped;
            public readonly long PointsSpent;
            public readonly long ScrapSpent;
            public readonly long ScrapEarned;

            public CraftResult(bool crafted, bool targeted, int slot, int grade, bool equipped,
                               long pointsSpent, long scrapSpent, long scrapEarned)
            {
                Crafted = crafted;
                Targeted = targeted;
                Slot = slot;
                Grade = grade;
                Equipped = equipped;
                PointsSpent = pointsSpent;
                ScrapSpent = scrapSpent;
                ScrapEarned = scrapEarned;
            }

            public static CraftResult None => new CraftResult(false, false, 0, MiningGear.NoGrade,
                                                              false, 0L, 0L, 0L);
        }

        /// <summary>
        /// Spend one craft's points and roll an item: a random slot, a grade off the craft weights.
        /// Compared to what is worn on the spot — the winner is kept, the loser scrapped for a small
        /// refund. Refused, spending nothing, when the pool is short of <see cref="CraftCost"/>.
        /// </summary>
        public CraftResult TryCraft() => TryCraftWithRolls(_random.NextDouble(), _random.NextDouble());

        /// <summary>The same craft, with the dice supplied — what a test drives directly.</summary>
        public CraftResult TryCraftWithRolls(double slotRoll, double gradeRoll)
        {
            int slot = MiningGear.RollSlot(slotRoll);
            return Craft(slot, false, gradeRoll);
        }

        /// <summary>Target one slot while keeping the normal grade probability table.</summary>
        public CraftResult TryTargetedCraft(int slot)
        {
            return TryTargetedCraftWithRoll(slot, _random.NextDouble());
        }

        /// <summary>The targeted craft with a supplied grade roll, used by deterministic tests.</summary>
        public CraftResult TryTargetedCraftWithRoll(int slot, double gradeRoll)
        {
            return Craft(slot, true, gradeRoll);
        }

        /// <summary>Plural alias matching the normal test helper's naming.</summary>
        public CraftResult TryTargetedCraftWithRolls(int slot, double gradeRoll)
        {
            return TryTargetedCraftWithRoll(slot, gradeRoll);
        }

        private CraftResult Craft(int slot, bool targeted, double gradeRoll)
        {
            if (_data == null || slot < 0 || slot >= MiningGear.SlotCount) return CraftResult.None;

            long pointsCost = CraftCost;
            long scrapCost = targeted ? TargetedScrapCost(slot) : 0L;
            if (_data.miningPoints < pointsCost || _data.miningScrap < scrapCost)
                return CraftResult.None;

            // All costs and the rolled outcome are committed before the caller can reveal feedback.
            // A force-close therefore cannot refund the targeted fee or reroll the result.
            _data.miningPoints -= pointsCost;
            _data.miningScrap -= scrapCost;
            int grade = MiningGear.RollGrade(gradeRoll, _tuning);
            int worn = WornGrade(slot);

            long scrapEarned;
            bool equipped = MiningGear.IsUpgrade(grade, worn);
            if (equipped)
            {
                scrapEarned = MiningGear.ScrapFor(worn);
                _data.miningGearGrade[slot] = grade + 1;
                RecomputeIncomeMultiplier();
            }
            else
            {
                scrapEarned = MiningGear.ScrapFor(grade);
            }
            _data.miningScrap += scrapEarned;

            _save?.Save(_data);
            Changed?.Invoke();
            return new CraftResult(true, targeted, slot, grade, equipped, pointsCost, scrapCost, scrapEarned);
        }
    }
}
