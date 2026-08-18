using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// The state of repair of every island, and the only thing allowed to change it.
    ///
    /// Wear is charged per ABSENCE, not per second. <see cref="Evaluate"/> is called on launch, on
    /// coming back from the background, and on a slow tick while the game is open; each call measures
    /// the gap since the last one and hands it to <see cref="Maintenance.Decay"/>, which throws away
    /// the first few hours of it. So an open game wears nothing (the gaps are a minute long), a daily
    /// player wears nothing (the gaps are under the grace window), and a fortnight away comes back to
    /// an island that has visibly been left.
    ///
    /// TWO ARRAYS PER ISLAND, deliberately. <see cref="IslandCondition.station"/> is the state of
    /// repair and belongs to the save. What the simulation reads is <see cref="Conditions"/>, the same
    /// eight numbers with any running maintenance bonus folded in — a bonus is a buff, not a state of
    /// repair, and writing 1.1 into a save field called "condition" would decay from 1.1 the next time
    /// the player went away. The derived array is only ever written here, and only when something
    /// actually changes: a decay, a repair step, a bonus starting or running out. The island shares it
    /// the way it already shares its level vectors, so reading it costs nothing per frame.
    /// </summary>
    public sealed class MaintenanceService
    {
        /// <summary>How often a running repair advances. It is a rate ramp, not an animation.</summary>
        private const float RepairStepSeconds = 0.25f;

        /// <summary>
        /// How often the absence clock is re-read while the game is open. Frequent enough that a kill
        /// without a pause loses almost nothing, rare enough to be free.
        /// </summary>
        private const float EvaluateSeconds = 60f;

        private sealed class Row
        {
            public IslandCondition save;
            public float[] effective;      // save.station × the running bonus; what the island reads
            public bool bonusApplied;      // whether effective currently carries the bonus
        }

        private readonly SaveData _data;
        private readonly TimeService _time;
        private readonly WalletService _wallet;
        private readonly Maintenance.Tuning _tuning;
        private readonly bool _enabled;

        private readonly Dictionary<string, Row> _rows = new Dictionary<string, Row>();
        private readonly List<Row> _order = new List<Row>();   // stable iteration without allocating
        private Row _scratch;                                  // answers for an island with no key

        private float _stepAccum, _evaluateAccum;

        /// <summary>Raised when an island's condition changes: island key. The art and the pins listen.</summary>
        public event Action<string> Changed;

        /// <summary>Raised when a repair finishes: island key, station (-1 = the whole island).</summary>
        public event Action<string, int> Repaired;

        public MaintenanceService(SaveData data, TimeService time, WalletService wallet,
                                  Maintenance.Tuning tuning, bool enabled)
        {
            _data = data;
            _time = time;
            _wallet = wallet;
            _tuning = tuning;
            _enabled = enabled;

            if (_data != null && _data.conditions != null)
                for (int i = 0; i < _data.conditions.Count; i++)
                    Adopt(_data.conditions[i]);
        }

        public bool Enabled => _enabled;
        public Maintenance.Tuning Tuning => _tuning;

        // ---- reading -------------------------------------------------------------------------

        /// <summary>
        /// The eight numbers the island's economy multiplies its throughput by. The SAME array every
        /// time it is asked for, so the caller can hold onto it and read it per frame.
        /// </summary>
        public float[] Conditions(string island) => Get(island).effective;

        public float Condition(string island, int station)
        {
            float[] c = Get(island).effective;
            return station >= 0 && station < c.Length ? c[station] : 1f;
        }

        /// <summary>What the island as a whole is running at — its worst station. See Maintenance.</summary>
        public float IslandCondition(string island) => Maintenance.IslandCondition(Get(island).effective);

        /// <summary>The state of repair with no bonus in it — what the repair sheet quotes against.</summary>
        public float StateOfRepair(string island, int station)
        {
            float[] c = Get(island).save.station;
            return station >= 0 && station < c.Length ? c[station] : 1f;
        }

        /// <summary>True when anything on this island is worth sending a crew to.</summary>
        public bool NeedsRepair(string island)
        {
            if (!_enabled) return false;
            float[] c = Get(island).save.station;
            for (int s = 0; s < c.Length; s++)
                if (c[s] < 1f) return true;
            return false;
        }

        /// <summary>How many of the island's stations are worn. Drives the "Hepsini Onar" button.</summary>
        public int WornCount(string island)
        {
            if (!_enabled) return 0;
            float[] c = Get(island).save.station;
            int n = 0;
            for (int s = 0; s < c.Length; s++)
                if (c[s] < 1f) n++;
            return n;
        }

        public bool Repairing(string island) => Get(island).save.repairEndUnix > 0L;

        /// <summary>Which station the crew is on; -1 while they are doing the whole island.</summary>
        public int RepairingStation(string island) => Get(island).save.repairStation;

        /// <summary>How far through the running repair, 0..1. Zero when nothing is being repaired.</summary>
        public float RepairProgress(string island)
        {
            IslandCondition row = Get(island).save;
            if (row.repairEndUnix <= 0L || row.repairSeconds <= 0) return 0f;
            long left = row.repairEndUnix - _time.NowUnix();
            if (left <= 0L) return 1f;
            float p = 1f - left / (float)row.repairSeconds;
            return p < 0f ? 0f : (p > 1f ? 1f : p);
        }

        public float RepairSecondsLeft(string island)
        {
            IslandCondition row = Get(island).save;
            long left = row.repairEndUnix - _time.NowUnix();
            return left > 0L ? left : 0f;
        }

        public double RepairCost(string island, int station, double islandRatePerMinute)
            => _enabled ? Maintenance.RepairCost(StateOfRepair(island, station), islandRatePerMinute, _tuning) : 0d;

        public double RepairCostAll(string island, double islandRatePerMinute)
            => _enabled ? Maintenance.RepairCostAll(Get(island).save.station, islandRatePerMinute, _tuning) : 0d;

        /// <summary>True while the island is running its post-repair maintenance bonus.</summary>
        public bool BonusActive(string island) => Get(island).save.bonusEndUnix > _time.NowUnix();

        public float BonusSecondsLeft(string island)
        {
            long left = Get(island).save.bonusEndUnix - _time.NowUnix();
            return left > 0L ? left : 0f;
        }

        // ---- the shield ----------------------------------------------------------------------

        /// <summary>
        /// True while the store's maintenance shield is running. Empire-wide on purpose: the thing
        /// being suspended is an ABSENCE, and the player is either away or they are not — a shield
        /// that covered one island while the seven beside it rotted would be selling the same
        /// product eight times for one journey.
        /// </summary>
        public bool ShieldActive => _enabled && _data != null && _time != null
                                    && _data.shieldEndUnix > _time.NowUnix();

        public float ShieldSecondsLeft
        {
            get
            {
                if (_data == null || _time == null) return 0f;
                long left = _data.shieldEndUnix - _time.NowUnix();
                return left > 0L ? left : 0f;
            }
        }

        /// <summary>
        /// Puts a bought shield on the clock, and makes its promise true on the spot.
        ///
        /// Extends rather than replaces, for the reason <see cref="BoostService.AddBoost"/> does:
        /// a player who buys the 24-hour card on top of a running 8-hour one must not be left
        /// holding the shorter of the two. A shield carries no multiplier, so unlike a boost there
        /// is nothing to convert — the hours simply add on to whatever is left.
        ///
        /// The card says the buildings STAY at 100%, so the first thing it does is put them there.
        /// Freezing a half-worn island at half would be selling a sentence the screen contradicts.
        /// </summary>
        public void AddShield(float hours)
        {
            if (!_enabled || _data == null || _time == null || hours <= 0f) return;

            long now = _time.NowUnix();
            long from = _data.shieldEndUnix > now ? _data.shieldEndUnix : now;
            _data.shieldEndUnix = from + (long)(hours * 3600f);

            RestoreAll();
        }

        /// <summary>
        /// Every island back to new, with no crew, no bill and no bonus. What the shield hands over
        /// at the moment of purchase.
        ///
        /// A repair already in flight is dropped rather than finished through
        /// <see cref="Complete"/>: the crew's whole job was to reach the state this has just
        /// granted for free, and running them through the finish would hand out the completion
        /// bonus as a silent extra on top of the sale.
        /// </summary>
        public void RestoreAll()
        {
            for (int i = 0; i < _order.Count; i++)
            {
                Row row = _order[i];
                float[] state = row.save.station;

                bool moved = row.save.repairEndUnix > 0L;
                for (int st = 0; st < state.Length; st++)
                {
                    if (state[st] >= 1f) continue;
                    state[st] = 1f;
                    moved = true;
                }
                if (!moved) continue;

                row.save.repairEndUnix = 0L;
                row.save.repairStation = -1;
                row.save.repairSeconds = 0;
                row.save.repairFrom = null;

                Refresh(row);
                Changed?.Invoke(row.save.id);
            }
        }

        // ---- wearing -------------------------------------------------------------------------

        /// <summary>
        /// Charges every island for the time since this was last called, and restamps.
        ///
        /// The reference is the LATER of the service's own stamp and the last disk write — the
        /// freshest evidence that the player was actually present. The stamp is refreshed every
        /// minute and is normally the newer of the two; the save time covers the minute after it, and
        /// carries the whole thing when a process is killed without ever pausing. Taking the later
        /// always errs toward charging LESS wear, which is the right direction for a mechanic that
        /// takes money off someone who has just come back.
        /// </summary>
        public void Evaluate()
        {
            if (!_enabled || _data == null || _time == null) return;

            long now = _time.NowUnix();
            long since = _data.conditionStampUnix;
            if (_data.savedUnixSeconds > since) since = _data.savedUnixSeconds;

            _data.conditionStampUnix = now;
            if (since <= 0L) return;          // first launch since the update: nothing to charge for

            long elapsed = now - since;
            if (elapsed <= 0L) return;        // clock rolled back, or two calls inside one second

            // A bought shield is spent before the grace window is, not after it. Charging the grace
            // first and the shield against the remainder would quietly refund nothing on any absence
            // shorter than the free hours — the player would have paid for cover they never used.
            elapsed -= Maintenance.ShieldedSeconds(since, now, _data.shieldEndUnix);
            if (elapsed <= 0L) return;        // the whole gap was paid for

            long biting = Maintenance.BitingSeconds(elapsed, _tuning);
            if (biting <= 0L) return;         // inside the grace window, which is the common case

            for (int i = 0; i < _order.Count; i++)
            {
                Row row = _order[i];
                // A crew that was on site when the player left finishes the job. The grace window is
                // hours and a repair is minutes, so this only ever describes an absence that began
                // mid-repair — and charging wear against a station that is being mended at that
                // moment would show the player a bar going backwards for no reason they can see.
                if (row.save.repairEndUnix > 0L) continue;

                bool moved = false;
                for (int s = 0; s < row.save.station.Length; s++)
                {
                    float was = row.save.station[s];
                    float worn = Maintenance.Decay(was, elapsed, Wear(s), _tuning);
                    if (worn == was) continue;
                    row.save.station[s] = worn;
                    moved = true;
                }
                if (moved)
                {
                    Refresh(row);
                    Changed?.Invoke(row.save.id);
                }
            }
        }

        // ---- repairing -----------------------------------------------------------------------

        /// <summary>
        /// Pays for a repair and puts the crew on site. <paramref name="station"/> below zero repairs
        /// the whole island.
        ///
        /// The condition does not jump — it climbs to full over the crew's working time, so the
        /// island visibly speeds up while they work rather than snapping fixed the moment the money
        /// leaves the wallet.
        /// </summary>
        public bool TryRepair(string island, int station, double islandRatePerMinute)
        {
            if (!_enabled) return false;

            Row row = Get(island);
            if (row.save.repairEndUnix > 0L) return false;   // a crew is already out

            bool whole = station < 0;
            float[] state = row.save.station;
            if (!whole && (station >= state.Length || state[station] >= 1f)) return false;
            if (whole && !NeedsRepair(island)) return false;

            double cost = whole
                ? Maintenance.RepairCostAll(state, islandRatePerMinute, _tuning)
                : Maintenance.RepairCost(state[station], islandRatePerMinute, _tuning);
            if (cost > 0d && !_wallet.TrySpendCash(new BigDouble(cost))) return false;

            float seconds = whole
                ? Maintenance.RepairSecondsAll(state, _tuning)
                : Maintenance.RepairSeconds(state[station], _tuning);

            if (row.save.repairFrom == null || row.save.repairFrom.Length != state.Length)
                row.save.repairFrom = new float[state.Length];
            for (int s = 0; s < state.Length; s++) row.save.repairFrom[s] = state[s];

            row.save.repairStation = whole ? -1 : station;
            row.save.repairSeconds = (int)(seconds < 1f ? 1f : seconds);
            row.save.repairEndUnix = _time.NowUnix() + row.save.repairSeconds;

            Changed?.Invoke(island);
            return true;
        }

        /// <summary>
        /// Finishes the running repair on the spot — what the rewarded ad and the gem skip buy. Does
        /// not touch the wallet: whoever sold the skip has already collected for it.
        /// </summary>
        public void SkipRepair(string island)
        {
            Row row = Get(island);
            if (row.save.repairEndUnix <= 0L) return;
            Complete(row);
        }

        /// <summary>
        /// Puts an island back to new with no crew, no cost and no bonus. For an island the player has
        /// just BOUGHT — rows wear whether or not anyone owns them, and handing someone a
        /// fresh purchase that is already filthy is indefensible — and for the prestige wipe.
        /// </summary>
        public void Reset(string island)
        {
            Row row = Get(island);
            for (int s = 0; s < row.save.station.Length; s++) row.save.station[s] = 1f;
            row.save.repairEndUnix = 0L;
            row.save.repairStation = -1;
            row.save.repairSeconds = 0;
            row.save.bonusEndUnix = 0L;
            Refresh(row);
            Changed?.Invoke(island);
        }

        // ---- driving -------------------------------------------------------------------------

        /// <summary>Driven from <see cref="GameBootstrap"/>, so repairs keep running across a scene load.</summary>
        public void Tick(float dt)
        {
            if (!_enabled) return;

            _evaluateAccum += dt;
            if (_evaluateAccum >= EvaluateSeconds)
            {
                _evaluateAccum = 0f;
                Evaluate();
            }

            _stepAccum += dt;
            if (_stepAccum < RepairStepSeconds) return;
            _stepAccum = 0f;

            long now = _time.NowUnix();
            for (int i = 0; i < _order.Count; i++)
            {
                Row row = _order[i];

                // The bonus expiring is a change to what the island runs at, and nothing else raises
                // an event for it — so it is watched here rather than being noticed a minute later by
                // whatever happened to ask next.
                if (row.bonusApplied && row.save.bonusEndUnix <= now)
                {
                    Refresh(row);
                    Changed?.Invoke(row.save.id);
                }

                if (row.save.repairEndUnix <= 0L) continue;
                if (now >= row.save.repairEndUnix) { Complete(row); continue; }
                Advance(row, now);
            }
        }

        /// <summary>One step of a running repair: every station under the crew climbs toward new.</summary>
        private void Advance(Row row, long now)
        {
            long left = row.save.repairEndUnix - now;
            float p = 1f - left / (float)row.save.repairSeconds;
            if (p < 0f) p = 0f;

            float[] state = row.save.station;
            float[] from = row.save.repairFrom;
            int only = row.save.repairStation;

            for (int s = 0; s < state.Length; s++)
            {
                if (only >= 0 && s != only) continue;
                float start = from != null && s < from.Length ? from[s] : state[s];
                state[s] = start + (1f - start) * p;
            }
            Refresh(row);
            Changed?.Invoke(row.save.id);
        }

        private void Complete(Row row)
        {
            float[] state = row.save.station;
            int only = row.save.repairStation;
            for (int s = 0; s < state.Length; s++)
                if (only < 0 || s == only) state[s] = 1f;

            // The bonus is for putting the WHOLE island right. Awarded on a station repair that
            // happens to leave nothing else worn too, because the player who fixes their last dirty
            // building one at a time has done the same job as the one who tapped Hepsini Onar, and
            // being paid less for the tidier habit would be a strange thing to teach.
            bool whole = true;
            for (int s = 0; s < state.Length; s++)
                if (state[s] < 1f) { whole = false; break; }
            if (whole && _tuning.BonusMultiplier > 1f && _tuning.BonusMinutes > 0f)
                row.save.bonusEndUnix = _time.NowUnix() + (long)(_tuning.BonusMinutes * 60f);

            row.save.repairEndUnix = 0L;
            row.save.repairSeconds = 0;
            row.save.repairFrom = null;

            Refresh(row);
            Changed?.Invoke(row.save.id);
            Repaired?.Invoke(row.save.id, only);
            row.save.repairStation = -1;
        }

        // ---- rows ----------------------------------------------------------------------------

        private static float Wear(int station)
            => station >= 0 && station < Maintenance.Wear.Length ? Maintenance.Wear[station] : 1f;

        private Row Get(string island)
        {
            Row row;
            if (string.IsNullOrEmpty(island))
            {
                // An island with no key cannot be looked up again, so a row made for one would be a
                // fresh row every call and a save file that grew forever. It gets one scratch row
                // that reads as perfect and is never written down.
                if (_scratch == null) _scratch = Adopt(new IslandCondition { station = Maintenance.NewConditions() }, false);
                return _scratch;
            }
            if (_rows.TryGetValue(island, out row)) return row;

            var save = new IslandCondition { id = island, station = Maintenance.NewConditions() };
            if (_data != null && _data.conditions != null) _data.conditions.Add(save);
            return Adopt(save, true);
        }

        /// <summary>
        /// Wraps a save row — loaded or brand new — in its runtime one. A row that is not
        /// <paramref name="live"/> is the scratch one: it answers questions but never wears and never
        /// joins the tick.
        /// </summary>
        private Row Adopt(IslandCondition save, bool live = true)
        {
            if (save.station == null || save.station.Length != Maintenance.Stations)
                save.station = Maintenance.NewConditions();

            var row = new Row { save = save, effective = new float[Maintenance.Stations] };
            Refresh(row);
            if (!live) return row;

            if (!string.IsNullOrEmpty(save.id)) _rows[save.id] = row;
            _order.Add(row);
            return row;
        }

        /// <summary>Rebuilds what the simulation reads from the state of repair and the bonus.</summary>
        private void Refresh(Row row)
        {
            bool bonus = row.save.bonusEndUnix > (_time != null ? _time.NowUnix() : 0L);
            float mult = bonus ? _tuning.BonusMultiplier : 1f;
            for (int s = 0; s < row.effective.Length; s++) row.effective[s] = row.save.station[s] * mult;
            row.bonusApplied = bonus;
        }
    }
}
