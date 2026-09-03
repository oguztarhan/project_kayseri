using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// Owns what the save knows about live events: which slot has been worked towards, and which has
    /// been paid out. The rules are in <see cref="LiveEvents"/>; the content belongs to whichever
    /// module the event's Kind names. This is the middle layer and it is deliberately thin.
    ///
    /// IT DOES NOT KNOW WHAT "EARNED" MEANS. The service counts and it remembers; whether 40 bars is a
    /// finished milestone is the module's arithmetic, because the target lives in the module's own
    /// tuning. Teaching this class about targets would put half of every future event's balance here
    /// and half in the module, which is the split that makes a live-ops layer impossible to reason
    /// about. Progress in, claim flags out, nothing in between.
    ///
    /// NO MODULE REGISTRY, AND THAT IS THE POINT. The plan called for a Register(IModule) seam so
    /// events could plug in "without a giant switch". Building it turned out to be the switch in a
    /// different coat: everything a module would register — its targets, its rewards, its screen — it
    /// owns anyway, and everything it needs from here is reachable by id. So there is no interface to
    /// implement and no list to join. A module calls <see cref="Record"/> and <see cref="MarkClaimed"/>
    /// with its own id and that is the entire contract. CLAUDE.md's rule applies literally here:
    /// abstract at three real use cases, and today there are none.
    ///
    /// ADDED WITHOUT A SAVE-VERSION BUMP, on the precedent ForemanService, VoyageService and
    /// ChapterService set: a bump wipes every player's run (see <see cref="SaveMigration"/>), and
    /// this only appends. A save written before events existed arrives with a null list, which reads
    /// as a player who has seen no event — which is every save that exists today.
    ///
    /// ROWS ARE MADE ON DEMAND. A config carrying ten future events costs an untouched save nothing:
    /// the row appears the first time something records against it.
    /// </summary>
    public sealed class LiveEventService
    {
        private readonly SaveData _data;
        private readonly TimeService _time;
        private readonly IAnalytics _analytics;
        private readonly List<LiveEvents.Definition> _defs;

        /// <summary>Raised when anything the hub shows has moved.</summary>
        public event Action Changed;

        /// <summary>
        /// Takes the definitions rather than the asset that made them, the same split
        /// <see cref="CraftingService"/> and <see cref="ForemanService"/> keep with their Tuning
        /// structs: the ScriptableObject is an authoring surface, and a service that reached for one
        /// could not be built in a test without one.
        /// </summary>
        public LiveEventService(SaveData data, List<LiveEvents.Definition> definitions,
                                TimeService time = null, IAnalytics analytics = null)
        {
            _data = data;
            _time = time;
            _analytics = analytics;
            _defs = definitions ?? new List<LiveEvents.Definition>();
            Normalise();
        }

        /// <summary>The same fallback ForemanService keeps: a service built without a clock in a test
        /// still reads a real one rather than sitting at the epoch.</summary>
        private long NowUnix()
            => _time != null ? _time.NowUnix() : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        /// <summary>Islands owned, the one eligibility axis the hub gates on. Coal is owned implicitly
        /// and is not in the list, so it is counted here — the same correction ChapterService.Progress
        /// makes for the same reason.</summary>
        private int IslandsOwned()
            => _data != null && _data.unlockedIslands != null ? _data.unlockedIslands.Count + 1 : 1;

        // ------------------------------------------------------------- save shape
        /// <summary>
        /// Makes the save's event rows safe to index. A row whose id no longer appears in the config is
        /// LEFT ALONE rather than deleted: an event pulled from one build and restored in the next
        /// would otherwise come back with the player's claims forgotten, and a few dead rows cost less
        /// than one duplicated payout.
        /// </summary>
        private void Normalise()
        {
            if (_data == null) return;
            if (_data.liveEvents == null) _data.liveEvents = new List<LiveEventState>();

            for (int i = 0; i < _data.liveEvents.Count; i++)
            {
                LiveEventState s = _data.liveEvents[i];
                if (s == null) { _data.liveEvents.RemoveAt(i--); continue; }

                if (!TryDefinition(s.id, out LiveEvents.Definition d)) continue;

                s.progress = FitLong(s.progress, d.Slots);
                s.claimed = FitBool(s.claimed, d.Slots);

                // The version drop, and the asymmetry that makes claims idempotent: counters earned
                // against retired targets go, flags recording a reward already handed over stay.
                if (!LiveEvents.ProgressSurvives(d, s.configVersion))
                {
                    Array.Clear(s.progress, 0, s.progress.Length);
                    s.configVersion = d.ConfigVersion;
                }
            }
        }

        private static long[] FitLong(long[] src, int len)
        {
            if (src != null && src.Length == len) return src;
            var fitted = new long[len];
            if (src != null)
            {
                int n = src.Length < len ? src.Length : len;
                for (int i = 0; i < n; i++) fitted[i] = src[i];
            }
            return fitted;
        }

        private static bool[] FitBool(bool[] src, int len)
        {
            if (src != null && src.Length == len) return src;
            var fitted = new bool[len];
            if (src != null)
            {
                int n = src.Length < len ? src.Length : len;
                for (int i = 0; i < n; i++) fitted[i] = src[i];
            }
            return fitted;
        }

        /// <summary>The row for an event, made the first time anything asks to write to it.</summary>
        private LiveEventState Row(in LiveEvents.Definition d)
        {
            if (_data == null) return null;
            if (_data.liveEvents == null) _data.liveEvents = new List<LiveEventState>();

            for (int i = 0; i < _data.liveEvents.Count; i++)
            {
                LiveEventState s = _data.liveEvents[i];
                if (s != null && string.Equals(s.id, d.Id, StringComparison.Ordinal)) return s;
            }

            var row = new LiveEventState
            {
                id = d.Id,
                configVersion = d.ConfigVersion,
                progress = new long[d.Slots],
                claimed = new bool[d.Slots],
            };
            _data.liveEvents.Add(row);
            return row;
        }

        /// <summary>The row only if it already exists — the read path, which must not grow the save
        /// just because a screen looked at an event.</summary>
        private LiveEventState Existing(string id)
        {
            if (_data == null || _data.liveEvents == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < _data.liveEvents.Count; i++)
            {
                LiveEventState s = _data.liveEvents[i];
                if (s != null && string.Equals(s.id, id, StringComparison.Ordinal)) return s;
            }
            return null;
        }

        // ------------------------------------------------------------------ read
        /// <summary>Every well-formed definition the build carries, in config order.</summary>
        public int Count => _defs.Count;

        public LiveEvents.Definition At(int index) => _defs[index];

        public bool TryDefinition(string id, out LiveEvents.Definition definition)
        {
            for (int i = 0; i < _defs.Count; i++)
            {
                if (string.Equals(_defs[i].Id, id, StringComparison.Ordinal))
                {
                    definition = _defs[i];
                    return true;
                }
            }
            definition = default;
            return false;
        }

        public LiveEvents.Phase PhaseOf(string id)
            => TryDefinition(id, out LiveEvents.Definition d)
                ? LiveEvents.PhaseAt(d, NowUnix())
                : LiveEvents.Phase.Closed;

        public long SecondsLeft(string id)
            => TryDefinition(id, out LiveEvents.Definition d) ? LiveEvents.SecondsLeft(d, NowUnix()) : 0L;

        public long SecondsUntilStart(string id)
            => TryDefinition(id, out LiveEvents.Definition d) ? LiveEvents.SecondsUntilStart(d, NowUnix()) : 0L;

        /// <summary>Whether the event should appear on the hub at all: it exists and the player is far
        /// enough in. A closed event still shows while it holds an unclaimed slot — that is the module's
        /// call, made with <see cref="HasUnclaimed"/>.</summary>
        public bool Visible(string id)
            => TryDefinition(id, out LiveEvents.Definition d) && LiveEvents.Eligible(d, IslandsOwned());

        /// <summary>Whether progress may accrue right now. Every module asks this before recording.</summary>
        public bool Accruing(string id)
            => TryDefinition(id, out LiveEvents.Definition d)
               && LiveEvents.Accruing(d, NowUnix(), IslandsOwned());

        public long Progress(string id, int slot)
        {
            LiveEventState s = Existing(id);
            return s != null && s.progress != null && slot >= 0 && slot < s.progress.Length
                ? s.progress[slot] : 0L;
        }

        public bool Claimed(string id, int slot)
        {
            LiveEventState s = Existing(id);
            return s != null && s.claimed != null && slot >= 0 && slot < s.claimed.Length && s.claimed[slot];
        }

        /// <summary>Whether any slot is still unclaimed. The hub uses it to keep a closed event on
        /// screen while it still owes something — the R3 rule made visible.</summary>
        public bool HasUnclaimed(string id)
        {
            LiveEventState s = Existing(id);
            if (s == null || s.claimed == null) return false;
            for (int i = 0; i < s.claimed.Length; i++) if (!s.claimed[i]) return true;
            return false;
        }

        // ---------------------------------------------------------------- write
        /// <summary>
        /// Adds to a slot's counter, but only while the window is open and the player qualifies. The
        /// refusal is the whole point: a module that records unconditionally would let a task
        /// completed the day after an event closed advance a milestone inside it.
        /// </summary>
        public bool Record(string id, int slot, long amount = 1L)
        {
            if (amount <= 0L) return false;
            if (!TryDefinition(id, out LiveEvents.Definition d)) return false;
            if (!LiveEvents.Accruing(d, NowUnix(), IslandsOwned())) return false;
            if (slot < 0 || slot >= d.Slots) return false;

            LiveEventState s = Row(d);
            if (s == null) return false;

            s.progress = FitLong(s.progress, d.Slots);
            s.progress[slot] += amount;
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Marks a slot paid and says whether THIS call was the one that did it. False means it was
        /// already claimed, and a caller that pays out on false pays twice — the one bug the flags
        /// exist to prevent.
        ///
        /// It does NOT check the window. A slot earned while the event ran stays claimable after it
        /// closes, which is FIVE_LAYERS.md R3: nothing expires. Whether the slot was earned at all is
        /// the module's arithmetic, and it must be settled before this is called.
        ///
        /// SAVED BEFORE ACKNOWLEDGED is the caller's job in the same breath: flag first, reward
        /// second, exactly as <c>ChapterService.Claim</c> and the IAP journal do it.
        /// </summary>
        public bool MarkClaimed(string id, int slot)
        {
            if (!TryDefinition(id, out LiveEvents.Definition d)) return false;
            if (slot < 0 || slot >= d.Slots) return false;

            LiveEventState s = Row(d);
            if (s == null) return false;

            s.claimed = FitBool(s.claimed, d.Slots);
            if (s.claimed[slot]) return false;

            s.claimed[slot] = true;
            _analytics?.Log("live_event_claim", "id", id + ":" + slot);
            Changed?.Invoke();
            return true;
        }
    }
}
