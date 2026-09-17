using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Game.Core;
using Game.Systems;
using Game.UI;

namespace Game.Tests
{
    /// <summary>
    /// The chapter screen's two jobs in a game where chapters actually move: showing the chapters the
    /// player has reached, and offering the way into the next one.
    ///
    /// BOTH OF THESE WERE BROKEN BY OMISSION. The tab list asked the world ladder which islands the
    /// game had — one, forever — so a player who advanced would have found no tab for the chapter they
    /// were standing in. And nothing anywhere called
    /// <see cref="ChapterProgressionService.TryAdvance"/>, so a finished chapter was the end of the
    /// game. Neither failure would have raised anything.
    ///
    /// The screen is built WITHOUT its Awake on purpose: Awake also hangs an opener on the HUD, and
    /// <c>FindAnyObjectByType</c> reaches into whatever scene the editor happens to have open. A test
    /// has no business adding a button to the shipped scene. What is under test is the sheet.
    /// </summary>
    public sealed class ChapterAdvanceUiTests
    {
        private static Chapters.Tuning T => Chapters.Tuning.Default;

        private GameObject _host;
        private SaveData _data;
        private ChapterService _chapters;
        private ChapterProgressionService _progression;

        [SetUp]
        public void SetUp()
        {
            ServiceLocator.Clear();
            _data = new SaveData();
            var wallet = new WalletService(_data.wallet);
            _chapters = new ChapterService(_data, wallet, null, T);
            _progression = new ChapterProgressionService(_data, _chapters, null);
            ServiceLocator.Register(_data);
            ServiceLocator.Register(wallet);
            ServiceLocator.Register(_chapters);
            ServiceLocator.Register(_progression);
            ServiceLocator.Register(new LocalizationService());
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
            ServiceLocator.Clear();
        }

        // ------------------------------------------------------------------ the tabs
        [Test]
        public void AFreshSaveShowsOneChapterAndNoSelector()
        {
            Show();

            Assert.That(Visible(), Is.EqualTo(new[] { 0 }));
            Assert.That(Tabs(), Is.Zero, "one chapter needs no selector — the beats take the full width");
        }

        /// <summary>
        /// The regression this screen would have shipped: chapters two to eight are never on the world
        /// ladder, so filtering by it left the player's own chapter without a tab.
        /// </summary>
        [Test]
        public void EveryChapterThePlayerHasReachedGetsATab()
        {
            Own(1, 2, 3);

            Show();

            Assert.That(Visible(), Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(Tabs(), Is.EqualTo(4));
        }

        [Test]
        public void ChaptersThePlayerHasNotReachedYetGetNoTab()
        {
            Own(1);

            Show();

            Assert.That(Visible(), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(Tab(2), Is.Null, "seven rows of 'not yours yet' lead nowhere");
        }

        [Test]
        public void TheWholeLadderIsShownOnceTheWholeLadderIsPlayed()
        {
            for (int c = 1; c < Chapters.Count; c++) Own(c);

            Show();

            Assert.That(Visible().Length, Is.EqualTo(Chapters.Count));
        }

        // ------------------------------------------------------------------ the button
        [Test]
        public void AnUnfinishedChapterOffersToCollectAndNotToMoveOn()
        {
            ChapterUI ui = Show();

            Assert.That(Label(ui), Does.StartWith(Loc.T("bolum.hepsiniAl")));   // plus the ×N it owes
            Assert.That(Advance(ui), Is.False);
        }

        [Test]
        public void AFinishedChapterOffersTheNextOne()
        {
            Finish(0);

            ChapterUI ui = Show();

            Assert.That(Advance(ui), Is.True);
            Assert.That(Label(ui), Is.EqualTo(Loc.T("bolum.ilerle")));
            Assert.That(Button(ui).interactable, Is.True);
        }

        /// <summary>
        /// A player reading chapter one from chapter three is reading a finished chapter. The button
        /// must not offer to advance out of a chapter they are not standing in.
        /// </summary>
        [Test]
        public void LookingBackAtAFinishedChapterDoesNotOfferToAdvanceOutOfIt()
        {
            Finish(0);
            Own(1);

            ChapterUI ui = Show();
            Select(ui, 0);

            Assert.That(Shown(ui), Is.Zero, "the screen is on chapter one");
            Assert.That(_chapters.Complete(0), Is.True, "which is finished");
            Assert.That(Advance(ui), Is.False, "but it is not the chapter being played");
            Assert.That(Label(ui), Does.StartWith(Loc.T("bolum.hepsiniAl")));   // plus the ×N it owes
        }

        [Test]
        public void TheLastChapterHasNowhereToAdvanceTo()
        {
            for (int c = 1; c < Chapters.Count; c++) Own(c);
            Finish(Chapters.Count - 1);

            ChapterUI ui = Show();

            Assert.That(Shown(ui), Is.EqualTo(Chapters.Count - 1));
            Assert.That(_chapters.Complete(Chapters.Count - 1), Is.True);
            Assert.That(Advance(ui), Is.False, "there is no chapter nine");
            Assert.That(Label(ui), Does.StartWith(Loc.T("bolum.hepsiniAl")));   // plus the ×N it owes
        }

        /// <summary>
        /// Advance supersedes collect, so the rewards it sweeps must be the same ones collect would
        /// have paid. If that ever stops being true the button is hiding a reward behind itself.
        /// </summary>
        [Test]
        public void TheAdvanceButtonNeverHidesAnUnclaimedReward()
        {
            Finish(0);
            ChapterUI ui = Show();
            Assert.That(Advance(ui), Is.True, "and it is showing ADVANCE, not COLLECT");

            int owed = 0;
            for (int b = 0; b < Chapters.BeatCount; b++) if (_chapters.CanClaim(0, b)) owed++;
            Assert.That(owed, Is.GreaterThan(0), "there is something the collect button would have paid");

            Assert.That(_progression.TryAdvance(), Is.True);

            for (int b = 0; b < Chapters.BeatCount; b++)
                Assert.That(_chapters.CanClaim(0, b), Is.False, "beat " + b + " was swept on the way out");
        }

        // ------------------------------------------------------------------ the word
        /// <summary>
        /// The table is positional and fails silently — a row one column short shows the KEY on a
        /// button, on a device, in a language nobody on the team reads. See LocalizationTableTests.
        /// </summary>
        [Test]
        public void TheNextChapterButtonHasAWordInEveryLanguage()
        {
            var table = Resources.Load<TextAsset>("Diller/metinler");
            Assert.That(table, Is.Not.Null);

            string[] row = null;
            foreach (string line in table.text.Split('\n'))
                if (line.StartsWith("bolum.ilerle\t", System.StringComparison.Ordinal))
                    row = line.TrimEnd('\r', '\n').Split('\t');

            Assert.That(row, Is.Not.Null, "bolum.ilerle is missing from the table");
            Assert.That(row.Length, Is.EqualTo(12), "one key column and eleven languages");
            for (int i = 1; i < row.Length; i++)
                Assert.That(row[i].Trim(), Is.Not.Empty, "column " + i);
        }

        [Test]
        public void MovingOnAndCollectingDoNotReadTheSame()
        {
            Assert.That(Loc.T("bolum.ilerle"), Is.Not.EqualTo(Loc.T("bolum.hepsiniAl")),
                        "one button, two jobs — the words have to tell them apart");
        }

        // ------------------------------------------------------------------ helpers
        /// <summary>Marks a chapter owned without finishing it: the save entry is the whole unlock.</summary>
        private void Own(params int[] chapters)
        {
            for (int i = 0; i < chapters.Length; i++)
            {
                string ns = Chapters.Namespace(chapters[i]);
                if (!_data.unlockedIslands.Contains(ns)) _data.unlockedIslands.Add(ns);
            }
        }

        /// <summary>Builds a chapter out to all five beats, in its own namespace.</summary>
        private void Finish(int chapter)
        {
            string ns = Chapters.Namespace(chapter);
            Chapters.Tuning t = Chapters.TuningFor(chapter, T);
            Own(chapter);

            _data.islandLevels.Add(new StationLevel { id = ns + "#0#0", level = t.FullSteamLevels });
            for (int u = 0; u < t.FullSteamUnlocks; u++)
                _data.islandLevels.Add(new StationLevel { id = ns + "u#" + u, level = 1 });

            _data.idleMarketYards.Add(new IdleMarketYard
            {
                schemaVersion = IdleMarketMigration.SchemaVersion,
                id = ns,
                hireCarry = MarketFlow.MaxHireLevel,
                hireServe = MarketFlow.MaxHireLevel,
                dispatchLevel = MarketFlow.MaxHireLevel,
            });
        }

        /// <summary>
        /// The sheet, built and opened. Awake is skipped — see the class note — so the two services it
        /// would have looked up are handed over directly.
        /// </summary>
        private ChapterUI Show()
        {
            _host = new GameObject("BolumEkraniTest");
            var ui = _host.AddComponent<ChapterUI>();
            Field("_chapters").SetValue(ui, _chapters);
            Field("_progression").SetValue(ui, _progression);
            Call(ui, "LoadKit");
            Call(ui, "Build");
            ui.Show();
            return ui;
        }

        private static void Select(ChapterUI ui, int chapter)
            => typeof(ChapterUI).GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic)
                                .Invoke(ui, new object[] { chapter });

        private static bool Advance(ChapterUI ui)
            => (bool)typeof(ChapterUI).GetMethod("CanAdvanceHere", BindingFlags.Instance | BindingFlags.NonPublic)
                                      .Invoke(ui, null);

        private static int Shown(ChapterUI ui) => (int)Field("_shown").GetValue(ui);

        private int[] Visible() => (int[])Field("_visible").GetValue(_host.GetComponent<ChapterUI>());

        private static string Label(ChapterUI ui) => ((Text)Field("_claimAllText").GetValue(ui)).text;

        private static Button Button(ChapterUI ui) => (Button)Field("_claimAll").GetValue(ui);

        /// <summary>How many island tabs the selector actually built.</summary>
        private int Tabs()
        {
            int n = 0;
            for (int c = 0; c < Chapters.Count; c++) if (Tab(c) != null) n++;
            return n;
        }

        private Button Tab(int chapter)
        {
            var tabs = (Button[])Field("_tabBtn").GetValue(_host.GetComponent<ChapterUI>());
            return tabs[chapter];
        }

        private static FieldInfo Field(string name)
        {
            FieldInfo field = typeof(ChapterUI).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "missing field: " + name);
            return field;
        }

        private static void Call(ChapterUI ui, string method)
            => typeof(ChapterUI).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                                .Invoke(ui, null);
    }
}
