using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// The string table itself, checked as data rather than through
    /// <see cref="Game.Systems.LocalizationService"/> — the service writes the chosen language to
    /// PlayerPrefs, and a test has no business changing which language the editor opens in.
    ///
    /// THE TABLE IS POSITIONAL AND FAILS QUIETLY. A row with one column too few does not raise
    /// anything: the languages past the gap simply never see that key and the screen shows the key
    /// itself, on a device, in a language nobody on the team reads. Fifteen rows were sitting in
    /// exactly that state — Turkish and English only — when these tests were written, which is why the
    /// shape of every row is asserted here rather than the shape of the rows somebody remembered.
    /// </summary>
    public sealed class LocalizationTableTests
    {
        private const string ResourcePath = "Diller/metinler";

        /// <summary>Keys the settings screen and its support page ask for by name. A key that is
        /// renamed in the table and not in the code ships as its own name on a button.</summary>
        private static readonly string[] SettingsKeys =
        {
            "ayarlar.baslik", "ayarlar.dil", "ayarlar.ses", "ayarlar.muzik", "ayarlar.titresim",
            "ayarlar.gizlilik", "ayarlar.reklam_tercihleri", "ayarlar.degerlendir",
            "ayarlar.geri_yukle", "ayarlar.geri_yukleniyor", "ayarlar.geri_basarili", "ayarlar.geri_basarisiz",
            "ayarlar.destek", "ayarlar.bize_ulasin", "ayarlar.git", "ayarlar.katil",
            "ayarlar.oyuncu_no", "ayarlar.kopyala", "ayarlar.kopyalandi",
            "ayarlar.destek_konu", "ayarlar.destek_mesaj",
        };

        private static string[] Lines()
        {
            var asset = Resources.Load<TextAsset>(ResourcePath);
            Assert.IsNotNull(asset, ResourcePath + " not found");
            return asset.text.Split('\n');
        }

        private static string[] Cells(string line) => line.TrimEnd('\r', '\n').Split('\t');

        private static bool IsRow(string line)
            => !string.IsNullOrEmpty(line.Trim()) && line[0] != '#';

        [Test]
        public void HeaderNamesEveryLanguageTheGameShips()
        {
            string[] header = Cells(Lines()[0]);

            Assert.AreEqual(12, header.Length, "one key column and eleven languages");
            CollectionAssert.AreEqual(
                new[] { "anahtar", "tr", "en", "de", "fr", "es", "pt", "it", "pl", "ru", "id", "vi" },
                Trimmed(header));
        }

        /// <summary>The one that catches a translator's spreadsheet dropping a trailing empty cell.</summary>
        [Test]
        public void EveryRowFillsEveryColumn()
        {
            string[] lines = Lines();
            int columns = Cells(lines[0]).Length;

            var short_ = new List<string>();
            for (int i = 1; i < lines.Length; i++)
            {
                if (!IsRow(lines[i])) continue;
                string[] cells = Cells(lines[i]);
                if (cells.Length != columns) short_.Add(cells[0] + " (" + cells.Length + ")");
            }

            CollectionAssert.IsEmpty(short_, "rows with the wrong number of columns");
        }

        [Test]
        public void NoCellIsBlank()
        {
            string[] lines = Lines();
            string[] header = Cells(lines[0]);

            var blank = new List<string>();
            for (int i = 1; i < lines.Length; i++)
            {
                if (!IsRow(lines[i])) continue;
                string[] cells = Cells(lines[i]);
                for (int c = 1; c < cells.Length && c < header.Length; c++)
                    if (cells[c].Trim().Length == 0) blank.Add(cells[0] + "/" + header[c].Trim());
            }

            CollectionAssert.IsEmpty(blank, "empty cells fall back to English without saying so");
        }

        /// <summary>A duplicated key is not an error to the loader — the last one wins, silently, and
        /// the row somebody is looking at in the file is not the row on the screen.</summary>
        [Test]
        public void NoKeyAppearsTwice()
        {
            string[] lines = Lines();
            var seen = new HashSet<string>();
            var twice = new List<string>();

            for (int i = 1; i < lines.Length; i++)
            {
                if (!IsRow(lines[i])) continue;
                string key = Cells(lines[i])[0];
                if (!seen.Add(key)) twice.Add(key);
            }

            CollectionAssert.IsEmpty(twice);
        }

        [Test]
        public void SettingsScreenFindsEveryKeyItAsksFor()
        {
            string[] lines = Lines();
            var keys = new HashSet<string>();
            for (int i = 1; i < lines.Length; i++)
                if (IsRow(lines[i])) keys.Add(Cells(lines[i])[0]);

            var missing = new List<string>();
            for (int i = 0; i < SettingsKeys.Length; i++)
                if (!keys.Contains(SettingsKeys[i])) missing.Add(SettingsKeys[i]);

            CollectionAssert.IsEmpty(missing);
        }

        /// <summary>Every language's own name, for the picker's rows — a language whose name is missing
        /// shows up in the list as its two-letter code, which is not a word anybody chose.</summary>
        [Test]
        public void EveryLanguageNamesItselfForThePicker()
        {
            string[] lines = Lines();
            for (int i = 1; i < lines.Length; i++)
            {
                if (!IsRow(lines[i])) continue;
                string[] cells = Cells(lines[i]);
                if (cells[0] != "_dil_adi") continue;

                Assert.AreEqual(Cells(lines[0]).Length, cells.Length);
                for (int c = 1; c < cells.Length; c++)
                    Assert.IsNotEmpty(cells[c].Trim());
                return;
            }
            Assert.Fail("_dil_adi row missing — the language picker would list codes, not names");
        }

        private static string[] Trimmed(string[] cells)
        {
            var outp = new string[cells.Length];
            for (int i = 0; i < cells.Length; i++) outp[i] = cells[i].Trim();
            return outp;
        }
    }
}
