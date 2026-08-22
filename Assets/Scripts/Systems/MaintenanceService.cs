using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// The state of repair of every island, and the only thing allowed to change it.
    ///
    /// ONE CREW PER STATION, and as many as the player will pay for. Every station carries its own
    /// deadline in the save, so tapping four dirty buildings puts four crews out at once and each
    /// comes back on its own damage-scaled clock. A single row-wide deadline — which is what this
    /// used to hold — meant the second tap silently did nothing, on exactly the islands where
    /// everything needs seeing to at the same time.
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

        /// <summary>Raised when one station's repair finishes: island key, that station.</summary>
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

        /// <summary>True while a crew is out anywhere on this island.</summary>
        /// <summary>
        /// Set after construction rather than taken in the constructor: wear is evaluated during boot,
        /// before the goal system exists, and reordering the two would charge the absence after the
        /// island had already been asked how fast it runs. Null until then, which counts nothing.
        /// </summary>
        public GoalService Goals { get; set; }

        public bool Repairing(string island)
        {
            long[] end = Get(island).save.repairEnd;
            for (int s = 0; s < end.Length; s++)
                if (end[s] > 0L) return true;
            return false;
        }

        /// <summary>True while a crew is out on this one station.</summary>
        public bool Repairing(string island, int station)
        {
            long[] end = Get(island).save.repairEnd;
            return station >= 0 && station < end.Length && end[station] > 0L;
        }

        /// <summary>How far through one station's repair, 0..1. Zero when nobody is on it.</summary>
        public float RepairProgress(string island, int station)
        {
            IslandCondition row = Get(island).save;
            if (station < 0 || station >= row.repairEnd.Length) return 0f;
            if (row.repairEnd[station] <= 0L || row.repairSecs[station] <= 0) return 0f;

            long left = row.repairEnd[station] - _time.NowUnix();
            if (left <= 0L) return 1f;
            float p = 1f - left / (float)row.repairSecs[station];
            return p < 0f ? 0f : (p > 1f ? 1f : p);
        }

        public float RepairSecondsLeft(string island, int station)
        {
            IslandCondition row = Get(island).save;
            if (station < 0 || station >= row.repairEnd.Length) return 0f;
            long left = row.repairEnd[station] - _time.NowUnix();
            return left > 0L ? left : 0f;
        }

        public double RepairCost(string island, int station, double islandRatePerMinute)
            => _enabled ? Maintenance.RepairCost(StateOfRepair(island, station), islandRatePerMinute, _tuning) : 0d;

        /// <summary>
        /// The bill for everything that is worn and NOT already being seen to — what HEPSİNİ ONAR
        /// will actually start. Quoting for the buildings that already have a crew on them would be
        /// a button lying about its own price, since it is not going to start them a second time.
        /// </summary>
        public double RepairCostAll(string island, double islandRatePerMinute)
        {
            if (!_enabled) return 0d;
            IslandCondition row = Get(island).save;
            double total = 0d;
            for (int s = 0; s < row.station.Length; s++)
            {
                if (row.repairEnd[s] > 0L) continue;
                total += Maintenance.RepairCost(row.station[s], islandRatePerMinute, _tuning);
            }
            return total;
        }

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

                bool moved = false;
                for (int st = 0; st < state.Length; st++)
                {
                    if (row.save.repairEnd[st] > 0L) moved = true;
                    if (state[st] >= 1f) continue;
                    state[st] = 1f;
                    moved = true;
                }
                if (!moved) continue;

                Idle(row);

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

                bool moved = false;
                for (int s = 0; s < row.save.station.Length; s++)
                {
                    // A crew that was on site when the player left finishes the job. The grace window
                    // is hours and a repair is minutes, so this only ever describes an absence that
                    // began mid-repair — and charging wear against a station that is being mended at
                    // that moment would show the player a bar going backwards for no reason they can
                    // see. Per station now, so the four buildings nobody is on still wear.
                    if (row.save.repairEnd[s] > 0L) continue;

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
        /// Pays for a repair and puts a crew on site. <paramref name="station"/> below zero takes on
        /// every worn building at once.
        ///
        /// Only work that is not already under way is quoted for or started, so tapping HEPSİNİ ONAR
        /// while two crews are out prices and begins the OTHER six — the player is never charged
        /// twice for the same building.
        ///
        /// Each station gets its own clock, scaled to its own damage, so a lightly scuffed depot is
        /// back before the wrecked mine is. And the condition does not jump: it climbs to full over
        /// the crew's working time, so the island visibly speeds up while they work rather than
        /// snapping fixed the moment the money leaves the wallet.
        /// </summary>
        public bool TryRepair(string island, int station, double islandRatePerMinute)
        {
            if (!_enabled) return false;

            Row row = Get(island);
            float[] state = row.save.station;
            long[] end = row.save.repairEnd;
            bool whole = station < 0;

            // Price what this call would actually start, and nothing else. An out-of-range station
            // simply matches nothing and falls out at the jobs check below.
            double cost = 0d;
            int jobs = 0;
            for (int s = 0; s < state.Length; s++)
            {
                if (!whole && s != station) continue;
                if (end[s] > 0L || state[s] >= 1f) continue;
                cost += Maintenance.RepairCost(state[s], islandRatePerMinute, _tuning);
                jobs++;
            }
            if (jobs == 0) return false;
            if (cost > 0d && !_wallet.TrySpendCash(new BigDouble(cost))) return false;

            long now = _time.NowUnix();
            for (int s = 0; s < state.Length; s++)
            {
                if (!whole && s != station) continue;
                if (end[s] > 0L || state[s] >= 1f) continue;

                float seconds = Maintenance.RepairSeconds(state[s], _tuning);
                row.save.repairFrom[s] = state[s];
                row.save.repairSecs[s] = (int)(seconds < 1f ? 1f : seconds);
                end[s] = now + row.save.repairSecs[s];
            }

            Goals?.Record(Game.Core.Goals.Repairs, jobs);
            Changed?.Invoke(island);
            return true;
        }

        /// <summary>
        /// Finishes every running repair on the island on the spot — what the rewarded ad and the gem
        /// skip buy. Does not touch the wallet: whoever sold the skip has already collected for it.
        /// </summary>
        public void SkipRepair(string island)
        {
            Row row = Get(island);
            long[] end = row.save.repairEnd;
            for (int s = 0; s < end.Length; s++)
                if (end[s] > 0L) Complete(row, s);
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
            row.save.bonusEndUnix = 0L;
            Idle(row);
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

                long[] end = row.save.repairEnd;
                bool moved = false;
                for (int s = 0; s < end.Length; s++)
                {
                    if (end[s] <= 0L) continue;
                    if (now >= end[s]) { Complete(row, s); continue; }   // raises its own events
                    Advance(row, s, now);
                    moved = true;
                }
                // One rebuild and one event for the row, however many crews are out on it. Four
                // buildings climbing at once is still one change to what the island runs at.
                if (moved)
                {
                    Refresh(row);
                    Changed?.Invoke(row.save.id);
                }
            }
        }

        /// <summary>One step of one station's repair: it climbs from where the crew found it.</summary>
        private void Advance(Row row, int station, long now)
        {
            long left = row.save.repairEnd[station] - now;
            float p = 1f - left / (float)row.save.repairSecs[station];
            if (p < 0f) p = 0f;

            float start = row.save.repairFrom[station];
            row.save.station[station] = start + (1f - start) * p;
        }

        private void Complete(Row row, int station)
        {
            float[] state = row.save.station;
            state[station] = 1f;
            row.save.repairEnd[station] = 0L;
            row.save.repairSecs[station] = 0;

            // The bonus is for putting the WHOLE island right. Awarded on the station repair that
            // happens to leave nothing else worn, because the player who fixes their buildings one
            // at a time has done the same job as the one who tapped HEPSİNİ ONAR, and being paid
            // less for the tidier habit would be a strange thing to teach.
            bool whole = true;
            for (int s = 0; s < state.Length; s++)
                if (state[s] < 1f) { whole = false; break; }
            if (whole && _tuning.BonusMultiplier > 1f && _tuning.BonusMinutes > 0f)
                row.save.bonusEndUnix = _time.NowUnix() + (long)(_tuning.BonusMinutes * 60f);

            Refresh(row);
            Changed?.Invoke(row.save.id);
            Repaired?.Invoke(row.save.id, station);
        }

        /// <summary>Sends every crew home without finishing the job. For a wipe, a shield or a reset.</summary>
        private static void Idle(Row row)
        {
            for (int s = 0; s < row.save.repairEnd.Length; s++)
            {
                row.save.repairEnd[s] = 0L;
                row.save.repairSecs[s] = 0;
                row.save.repairFrom[s] = row.save.station[s];
            }
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
            // Sized here and never again, so every read below can index them without a guard. A save
            // written before repairs were per-station arrives with these null and simply starts idle:
            // whatever one crew was halfway through is lost, which costs the player minutes.
            if (save.repairEnd == null || save.repairEnd.Length != Maintenance.Stations)
                save.repairEnd = new long[Maintenance.Stations];
            if (save.repairSecs == null || save.repairSecs.Length != Maintenance.Stations)
                save.repairSecs = new int[Maintenance.Stations];
            if (save.repairFrom == null || save.repairFrom.Length != Maintenance.Stations)
                save.repairFrom = new float[Maintenance.Stations];

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
