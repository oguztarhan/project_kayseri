using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// Owns the chapter state: which beats have been collected, and what the save says about how far
    /// each island has actually been built. The rules are in <see cref="Chapters"/>; this reads the
    /// save and pays out.
    ///
    /// NOTHING REPORTS INTO IT. <see cref="GoalService"/> needed a Record() call in six files;
    /// this needed none. Every beat is derived from state the save already carries — levels bought,
    /// buildings raised, hires made — so the island simulation, the yards and the dock go on knowing
    /// nothing about chapters, and a beat cannot be missed because somebody forgot to call in.
    ///
    /// KEYED BY ISLAND, NOT BY INDEX. The rows are a list keyed on the island's own save key and each
    /// carries its OWN beat array, which is what makes appending a sixth beat free: a short array is
    /// padded on load, and no entry ever changes meaning. A single flat Count x BeatCount array would
    /// have been smaller and would have re-labelled every chapter after the first the moment the beat
    /// list grew.
    ///
    /// ADDED WITHOUT A SAVE-VERSION BUMP, on the precedent ForemanService and VoyageService set: a
    /// bump wipes every player's progress (see <see cref="SaveMigration"/>), and this only adds
    /// fields. A save written before chapters existed arrives with a null list, which is a player who
    /// has claimed nothing — and since beats are observed rather than reported, their existing
    /// islands light up whatever they have already earned the first time they open the screen.
    /// </summary>
    public sealed class ChapterService
    {
        private readonly SaveData _data;
        private readonly WalletService _wallet;
        private readonly ForemanService _foremen;
        private Chapters.Tuning _tuning;

        /// <summary>Scratch for the three hire levels <see cref="MarketFlow.IsMaxed"/> wants. Held
        /// rather than made per call: the screen refreshes eight of these on every change.</summary>
        private readonly int[] _hires = new int[MarketFlow.JobCount];

        /// <summary>Raised when anything the chapter screen shows has moved.</summary>
        public event Action Changed;

        public ChapterService(SaveData data, WalletService wallet, ForemanService foremen,
                              Chapters.Tuning tuning)
        {
            _data = data;
            _wallet = wallet;
            _foremen = foremen;
            _tuning = tuning;
            Normalise();
        }

        public Chapters.Tuning Tuning => _tuning;

        // ------------------------------------------------------------- save shape
        /// <summary>
        /// Makes the save's chapter rows safe to index. Rows are created on demand rather than all at
        /// once so an untouched save stays small, and a row's beat array is grown in place — never
        /// re-ordered — so a build that adds a beat inherits the claims the player already has.
        /// </summary>
        private void Normalise()
        {
            if (_data == null) return;
            if (_data.chapters == null) _data.chapters = new List<ChapterState>();
            for (int i = 0; i < _data.chapters.Count; i++)
            {
                ChapterState c = _data.chapters[i];
                if (c == null) { _data.chapters[i] = new ChapterState(); continue; }
                c.claimed = Fit(c.claimed, Chapters.BeatCount);
            }
        }

        private static bool[] Fit(bool[] src, int len)
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

        /// <summary>The row for a chapter, made the first time anything asks for it.</summary>
        private ChapterState Row(int chapter)
        {
            if (_data == null || chapter < 0 || chapter >= Chapters.Count) return null;
            string key = Chapters.Island(chapter);
            for (int i = 0; i < _data.chapters.Count; i++)
                if (_data.chapters[i] != null && _data.chapters[i].id == key) return _data.chapters[i];

            var row = new ChapterState { id = key, claimed = new bool[Chapters.BeatCount] };
            _data.chapters.Add(row);
            return row;
        }

        // ------------------------------------------------------------------ read
        /// <summary>
        /// One island's state, read straight out of the save.
        ///
        /// Coal is owned implicitly — <c>WorldIslands.IsOwned</c> returns true for index 0 without
        /// consulting the list, because the game starts you on it and nothing ever adds it.
        /// </summary>
        public Chapters.Progress Progress(int chapter)
        {
            var p = new Chapters.Progress();
            if (_data == null || chapter < 0 || chapter >= Chapters.Count) return p;

            string key = Chapters.Island(chapter);
            p.Owned = chapter == 0
                   || (_data.unlockedIslands != null && _data.unlockedIslands.Contains(key));
            if (!p.Owned) return p;

            CountLevels(key, out p.AxisLevels, out p.Unlocks);
            p.YardStaffed = YardStaffed(key);
            return p;
        }

        /// <summary>
        /// Walks the level list once for both tallies.
        ///
        /// The two key shapes are <c>"coal#4#1"</c> for an axis and <c>"coalu#8"</c> for a ghost
        /// building (CoalOperation.cs:1101 and :1129 — the "u" is what keeps them from colliding).
        /// Both are matched on their full prefix rather than on StartsWith(key) alone, so a future
        /// island whose key begins with another's cannot be counted twice.
        /// </summary>
        private void CountLevels(string key, out int axisLevels, out int unlocks)
        {
            axisLevels = 0;
            unlocks = 0;
            if (_data.islandLevels == null) return;

            string axisPrefix = key + "#";
            string unlockPrefix = key + "u#";

            for (int i = 0; i < _data.islandLevels.Count; i++)
            {
                StationLevel e = _data.islandLevels[i];
                if (e == null || string.IsNullOrEmpty(e.id) || e.level <= 0) continue;

                if (e.id.StartsWith(unlockPrefix, StringComparison.Ordinal)) unlocks++;
                else if (e.id.StartsWith(axisPrefix, StringComparison.Ordinal)) axisLevels += e.level;
            }
        }

        /// <summary>
        /// Whether this island's yard runs itself. Asks <see cref="MarketFlow.IsMaxed"/> rather than
        /// comparing three numbers here, because that is the flag the yard's whole design hangs on and
        /// a second copy of it is a second thing to keep in step.
        /// </summary>
        private bool YardStaffed(string key)
        {
            if (_data.marketYards == null) return false;
            for (int i = 0; i < _data.marketYards.Count; i++)
            {
                MarketYard y = _data.marketYards[i];
                if (y == null || y.id != key) continue;
                _hires[MarketFlow.Carry] = y.hireCarry;
                _hires[MarketFlow.Serve] = y.hireServe;
                _hires[MarketFlow.Collect] = y.hireCollect;
                return MarketFlow.IsMaxed(_hires);
            }
            return false;
        }

        public bool Owned(int chapter) => Progress(chapter).Owned;

        public bool Satisfied(int chapter, int beat)
            => Chapters.Satisfied(beat, Progress(chapter), _tuning);

        public bool Claimed(int chapter, int beat)
        {
            ChapterState row = Row(chapter);
            return row != null && beat >= 0 && beat < row.claimed.Length && row.claimed[beat];
        }

        public bool CanClaim(int chapter, int beat)
            => Satisfied(chapter, beat) && !Claimed(chapter, beat);

        public bool Complete(int chapter) => Chapters.Complete(Progress(chapter), _tuning);

        /// <summary>
        /// The chapter the player is in: the furthest one they own. Not the furthest INCOMPLETE one —
        /// an island can be bought long before the one behind it is finished, and the screen should
        /// open where the player actually is rather than sending them back.
        /// </summary>
        public int Current
        {
            get
            {
                int current = 0;
                for (int c = Chapters.Count - 1; c >= 0; c--)
                    if (Owned(c)) { current = c; break; }
                return current;
            }
        }

        /// <summary>How many beats are waiting to be collected — the number on the opener's badge.</summary>
        public int PendingCount()
        {
            int n = 0;
            for (int c = 0; c < Chapters.Count; c++)
            {
                if (!Owned(c)) continue;
                for (int b = 0; b < Chapters.BeatCount; b++) if (CanClaim(c, b)) n++;
            }
            return n;
        }

        /// <summary>Whether this chapter's opening card has been shown. Set by <see cref="MarkIntroSeen"/>.</summary>
        public bool IntroSeen(int chapter)
        {
            ChapterState row = Row(chapter);
            return row != null && row.introSeen;
        }

        public void MarkIntroSeen(int chapter)
        {
            ChapterState row = Row(chapter);
            if (row == null || row.introSeen) return;
            row.introSeen = true;
            Changed?.Invoke();
        }

        // ----------------------------------------------------------------- claim
        public bool Claim(int chapter, int beat)
        {
            if (!CanClaim(chapter, beat)) return false;
            ChapterState row = Row(chapter);
            if (row == null) return false;

            row.claimed[beat] = true;
            Pay(Chapters.BeatGems(chapter, beat, _tuning),
                Chapters.BeatCards(chapter, beat, _tuning));
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Collects everything owed in one chapter. A player coming back to a finished island has all
        /// five beats waiting, and paying them one tap at a time is five taps for one decision.
        /// </summary>
        public int ClaimChapter(int chapter)
        {
            long gems = 0L;
            int cards = 0, taken = 0;
            for (int b = 0; b < Chapters.BeatCount; b++)
            {
                if (!CanClaim(chapter, b)) continue;
                Row(chapter).claimed[b] = true;
                gems += Chapters.BeatGems(chapter, b, _tuning);
                cards += Chapters.BeatCards(chapter, b, _tuning);
                taken++;
            }
            if (taken == 0) return 0;
            Pay(gems, cards);
            Changed?.Invoke();
            return taken;
        }

        private void Pay(long gems, int cards)
        {
            if (gems > 0L && _wallet != null) _wallet.AddGems(gems);
            if (cards > 0 && _foremen != null) _foremen.GrantRandomDuplicates(cards);
        }
    }
}
