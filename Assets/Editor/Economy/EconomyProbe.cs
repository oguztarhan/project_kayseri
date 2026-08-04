using System.Collections.Generic;
using System.Text;
using Game.Core;
using Game.Gameplay;
using Game.Systems;
using UnityEditor;
using UnityEngine;

namespace Kayseri.EconomyTools
{
    /// <summary>
    /// Measures what an island actually earns at a given set of upgrade levels.
    ///
    /// Income here is not a formula — it emerges from trains and trucks driving real
    /// routes, stalling on each other and queueing at yards. So the only honest way to
    /// know what a balance change did is to run the game and watch the meter. This
    /// drives that: buy levels through the same <see cref="CoalOperation.TryUpgrade"/>
    /// the player uses, let the sim settle, read the rate, repeat.
    ///
    /// Two things it deliberately does:
    ///
    /// <list type="bullet">
    /// <item>It lifts the island's income CAP for the duration. Coal's cap is set to what
    /// a maxed island earns, so measuring near the top would return the clamp instead of
    /// the truth — and the clamp is one of the numbers we are trying to re-derive.</item>
    /// <item>It only ever buys UP. There is no un-upgrade, so a run is a monotonically
    /// rising script and every measurement is taken on the way past.</item>
    /// </list>
    ///
    /// Runs in play mode, stepped from <see cref="EditorApplication.update"/>. Results
    /// land in <see cref="Report"/> as TSV.
    /// </summary>
    public static class EconomyProbe
    {
        private enum Op { Measure, Buy, Unlock }

        private struct Step
        {
            public Op Op;
            public string Label;
            public int S, A, Count, U;
        }

        private static readonly List<Step> _script = new List<Step>();
        private static readonly StringBuilder _out = new StringBuilder();
        private static CoalOperation _op;
        private static WalletService _wallet;
        private static int _at;
        private static float _settleLeft;
        private static bool _filling;      // settling after a purchase, or filling the meter
        private static double _capWas;
        private static float _scaleWas, _maxDtWas;
        private static float _settleSeconds = 8f;

        // The player's progress, put back exactly as it was when the run ends. A probe
        // buys its way to a maxed island and pours 1e12 a frame into the wallet; every
        // one of those writes goes through the ordinary save path, so without this a
        // measurement run silently overwrites the save on disk.
        private static List<StationLevel> _saveWas;
        private static BigDouble _cashWas, _lifetimeWas;
        private static double _investorsWas, _rateWas;

        public static bool Running { get; private set; }
        public static string Report => _out.ToString();
        public static string Status { get; private set; } = "idle";

        /// <summary>
        /// Clears the script. Call before queueing a new run.
        ///
        /// <paramref name="settleSeconds"/> has to be generous, and this is the trap that
        /// cost a whole balance pass: CoalOperation.WarmStart fills the ore yard and the bar
        /// yard to <c>warmStartFill</c> at startup, and until that buffer drains the island
        /// SELLS FASTER THAN IT CAN PRODUCE. At level 0 the drain takes about two minutes,
        /// and a 10-second settle read 540 $/min against a true steady state of 360 — 50%
        /// high, at the exact end of the curve the onboarding is tuned against. The bias
        /// shrinks as levels rise (bigger throughput drains the buffer sooner), so it also
        /// tilts the whole curve, not just its anchor.
        /// </summary>
        public static void Begin(float settleSeconds = 150f)
        {
            _script.Clear();
            _out.Length = 0;
            _settleSeconds = settleSeconds;
            Status = "queued";
        }

        /// <summary>Record the island's rate under the label, once the meter has refilled.</summary>
        public static void Measure(string label) =>
            _script.Add(new Step { Op = Op.Measure, Label = label });

        /// <summary>Buy <paramref name="count"/> levels on one axis (stops early if it caps).</summary>
        public static void Buy(int s, int a, int count) =>
            _script.Add(new Step { Op = Op.Buy, S = s, A = a, Count = count });

        /// <summary>
        /// Buy <paramref name="levels"/> more on every axis of every station. Axes that
        /// have hit their own cap (the fleet counts stop at 2) simply stop buying, so a
        /// sweep can call this repeatedly to walk a balanced build up the track.
        /// </summary>
        public static void BuyEveryAxis(int levels)
        {
            for (int s = 0; s < IslandEconomy.Axes.Length; s++)
                for (int a = 0; a < IslandEconomy.Axes[s].Length; a++)
                    Buy(s, a, levels);
        }

        public static void Unlock(int u) => _script.Add(new Step { Op = Op.Unlock, U = u });

        /// <summary>Buy all ten ghost buildings.</summary>
        public static void UnlockAll()
        {
            for (int u = 0; u < 10; u++) Unlock(u);
        }

        /// <summary>
        /// Start running the queued script against the island the player is standing on.
        /// <paramref name="timeScale"/> trades wall-clock for fidelity: the vehicles step
        /// per frame, so too high and they overshoot their waypoints. 6 is measured to
        /// agree with real time; check with a scale-1 control run before trusting more.
        /// </summary>
        public static string Run(float timeScale = 6f)
        {
            if (!EditorApplication.isPlaying) return "not in play mode";
            var world = Object.FindAnyObjectByType<WorldIslands>();
            if (world == null) return "no WorldIslands in the scene";
            _op = world.Operation(world.ActiveIndex);
            if (_op == null || !_op.enabled) return "the active island's operation is not running";
            _wallet = ServiceLocator.Get<WalletService>();
            if (_wallet == null) return "no WalletService";

            // A run can only buy upward, so it has to start from nothing or every
            // reading is taken on top of the last run's purchases. There is no
            // un-upgrade: fleet bodies wake and ghost buildings turn solid, and neither
            // reverses. Clear progress and restart play mode to get a clean island.
            for (int s = 0; s < _op.StationCount; s++)
                if (_op.StationLevelTotal(s) > 0)
                    return "island '" + _op.IslandKey + "' is already at level "
                         + _op.StationLevelTotal(s) + " on " + _op.StationName(s)
                         + " — run Kayseri/Economy/Clear Save Progress, then restart play mode";

            SnapshotSave();

            // Lift the cap: it is set to what a maxed island earns, so it would clamp
            // exactly the measurements we care about most.
            var so = new SerializedObject(_op);
            var capProp = so.FindProperty("incomeCapPerMin");
            _capWas = capProp.doubleValue;
            capProp.doubleValue = 1e15d;
            so.ApplyModifiedPropertiesWithoutUndo();

            _scaleWas = Time.timeScale;
            _maxDtWas = Time.maximumDeltaTime;
            Time.timeScale = Mathf.Max(1f, timeScale);
            // CoalOperation.TickIncome consumes at most ONE second-bucket per frame, so a
            // frame carrying more than a second of game time silently mis-scales the meter.
            // With the editor in the background it renders slowly and hits exactly that.
            // Clamping the frame keeps every bucket worth a real second.
            Time.maximumDeltaTime = 0.5f / Mathf.Max(1f, timeScale);
            _at = 0;
            _settleLeft = 0f;
            _filling = false;
            Running = true;
            Status = "running";
            _out.AppendLine("label\t$/min\t$/hr\tlevels\tunlocks");
            EditorApplication.update += Tick;
            return "probing " + _op.IslandKey + ": " + _script.Count + " steps at x" + Time.timeScale;
        }

        public static void Stop()
        {
            if (!Running) return;
            EditorApplication.update -= Tick;
            Running = false;
            Time.timeScale = _scaleWas;
            Time.maximumDeltaTime = _maxDtWas;
            if (_op != null)
            {
                var so = new SerializedObject(_op);
                so.FindProperty("incomeCapPerMin").doubleValue = _capWas;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            RestoreSave();
            Status = "done";
        }

        private static void SnapshotSave()
        {
            var d = ServiceLocator.Get<SaveData>();
            if (d == null) return;
            _saveWas = new List<StationLevel>();
            if (d.islandLevels != null)
                foreach (var e in d.islandLevels)
                    _saveWas.Add(new StationLevel { id = e.id, level = e.level });
            _cashWas = d.wallet.cash;
            _lifetimeWas = d.wallet.lifetimeCash;
            _investorsWas = d.wallet.investors;
            _rateWas = d.incomeRatePerSec;
        }

        private static void RestoreSave()
        {
            var d = ServiceLocator.Get<SaveData>();
            if (d == null || _saveWas == null) return;
            d.islandLevels.Clear();
            foreach (var e in _saveWas) d.islandLevels.Add(e);
            if (d.islandRates != null) d.islandRates.Clear();
            d.wallet.cash = _cashWas;
            d.wallet.lifetimeCash = _lifetimeWas;
            d.wallet.investors = _investorsWas;
            d.incomeRatePerSec = _rateWas;
            ServiceLocator.Get<SaveService>()?.Save(d);
            _saveWas = null;
        }

        /// <summary>
        /// Wipes island progress and the wallet, then writes the save. The next play
        /// session starts on a level-0 island, which is what a probe run needs.
        /// </summary>
        [MenuItem("Kayseri/Economy/Clear Save Progress", false, 2)]
        private static void ClearProgress()
        {
            if (!EditorUtility.DisplayDialog("Clear save progress?",
                    "Deletes every island's upgrade levels and empties the wallet.\n\n" +
                    "This is what a probe run needs to start from. It cannot be undone.",
                    "Clear it", "Cancel")) return;

            var d = ServiceLocator.Get<SaveData>();
            var svc = ServiceLocator.Get<SaveService>();
            if (d == null || svc == null)
            {
                Debug.LogWarning("[Economy] No save loaded — enter play mode first.");
                return;
            }
            d.islandLevels.Clear();
            d.islandRates?.Clear();
            d.unlockedIslands.Clear();
            d.wallet.cash = BigDouble.Zero;
            d.wallet.lifetimeCash = BigDouble.Zero;
            d.wallet.investors = 0d;
            d.incomeRatePerSec = 0d;
            svc.Save(d);
            Debug.Log("[Economy] Save progress cleared — restart play mode for a level-0 island.");
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || _op == null) { Stop(); return; }

            // Keep the purse bottomless — this is a measurement, not a playthrough.
            _wallet.AddCash(new Game.Core.BigDouble(1e12d));

            if (_at >= _script.Count) { Stop(); return; }
            Step step = _script[_at];

            if (step.Op == Op.Buy)
            {
                for (int i = 0; i < step.Count; i++)
                    if (!_op.TryUpgrade(step.S, step.A)) break;
                _at++;
                _settleLeft = _settleSeconds;
                _filling = false;
                return;
            }
            if (step.Op == Op.Unlock)
            {
                _op.TryUnlock(step.U);
                _at++;
                _settleLeft = _settleSeconds;
                _filling = false;
                return;
            }

            // Measure, in two phases. First let the chain re-equilibrate after the
            // purchases - yards refill, trucks redistribute - then clear the meter and
            // wait for it to fill again.
            if (!_filling)
            {
                _settleLeft -= Time.deltaTime;
                if (_settleLeft > 0f) return;
                ResetMeter();
                _filling = true;
                return;
            }
            // Wait on the METER, not on a clock: the number of buckets filled is the only
            // thing that says the reading covers a full minute, whatever the frame rate did.
            if (Filled() < 60) return;

            double rate = _op.CashPerMinute;
            _out.AppendLine(string.Format("{0}\t{1:F0}\t{2:F0}\t{3}\t{4}",
                step.Label, rate, rate * 60d, LevelSummary(), UnlockCount()));
            Status = step.Label + " = " + rate.ToString("F0") + " $/min  (" + (_at + 1) + "/" + _script.Count + ")";
            _at++;
            _settleLeft = _settleSeconds;
            _filling = false;
        }

        private static int Filled() => (int)typeof(CoalOperation)
            .GetField("_minFilled", System.Reflection.BindingFlags.NonPublic
                                  | System.Reflection.BindingFlags.Instance)
            .GetValue(_op);

        /// <summary>Empties the trailing-rate window so the next reading is of this build alone.</summary>
        private static void ResetMeter()
        {
            var t = typeof(CoalOperation);
            const System.Reflection.BindingFlags F =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var buckets = (double[])t.GetField("_minuteBuckets", F).GetValue(_op);
            for (int i = 0; i < buckets.Length; i++) buckets[i] = 0d;
            t.GetField("_minIdx", F).SetValue(_op, 0);
            t.GetField("_minFilled", F).SetValue(_op, 0);
            t.GetField("_trailing", F).SetValue(_op, 0d);
            t.GetField("_earnedThisSecond", F).SetValue(_op, 0d);
        }

        private static string LevelSummary()
        {
            var sb = new StringBuilder();
            for (int s = 0; s < _op.StationCount; s++)
            {
                if (s > 0) sb.Append('/');
                sb.Append(_op.StationLevelTotal(s));
            }
            return sb.ToString();
        }

        private static int UnlockCount()
        {
            int n = 0;
            for (int u = 0; u < _op.UnlockCount; u++) if (_op.IsUnlocked(u)) n++;
            return n;
        }

        [MenuItem("Kayseri/Economy/Stop Probe", false, 1)]
        private static void StopMenu() => Stop();
    }
}
