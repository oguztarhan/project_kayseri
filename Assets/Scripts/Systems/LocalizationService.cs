using System.Collections.Generic;
using UnityEngine;

namespace Game.Systems
{
    /// <summary>
    /// Every line of text the player reads, in every language the game ships (GDD §16).
    ///
    /// One tab-separated table in <c>Resources/Diller/metinler.txt</c> holds the lot: the first column is
    /// the key, every other column is a language. **Adding a language is a data change, not a code
    /// change** — append a column, give it a code in the header row and the language's own name in the
    /// <see cref="NameKey"/> row, and it appears in the picker. Tabs rather than commas so no line ever
    /// needs quoting and a translator can open it in a spreadsheet.
    ///
    /// Only the active language and the fallback are held in memory; switching re-reads the table, which
    /// costs a couple of hundred string splits and happens when a human taps a menu.
    ///
    /// A missing key falls back to English and then to the key itself. It never returns empty — a screen
    /// with a typo'd key shows the key, which is how you find it.
    /// </summary>
    public sealed class LocalizationService
    {
        public const string PrefKey = "ayar_dil";
        public const string UserChoicePrefKey = "ayar_dil_secildi";

        private const string ResourcePath = "Diller/metinler";

        /// <summary>
        /// English is the deterministic first-launch language and the fallback for missing translations.
        /// The device language is deliberately not used here: a player should see a readable game on
        /// first launch, then choose another language explicitly from Settings if desired.
        /// </summary>
        private const string FallbackCode = "en";

        /// <summary>The row whose cells hold each language's own name, for the picker.</summary>
        private const string NameKey = "_dil_adi";

        public struct Language
        {
            public string Code;
            public string Name;
        }

        private readonly Dictionary<string, string> _text = new Dictionary<string, string>(256);
        private readonly Dictionary<string, string> _fallback = new Dictionary<string, string>(256);
        private readonly List<Language> _languages = new List<Language>();

        private string[] _lines;
        private string[] _codes;
        private string _code;

        /// <summary>Raised after the language changes, so live screens can re-read their text.</summary>
        public event System.Action Changed;

        public string Code => _code;
        public IList<Language> Languages => _languages;

        public LocalizationService()
        {
            Load();
            ApplyLanguage(Stored(), false);
        }

        /// <summary>The line for <paramref name="key"/>. Never null.</summary>
        public string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            string v;
            if (_text.TryGetValue(key, out v)) return v;
            if (_fallback.TryGetValue(key, out v)) return v;
            return key;
        }

        public bool Has(string key) => !string.IsNullOrEmpty(key) && _text.ContainsKey(key);

        public void SetLanguage(string code)
        {
            ApplyLanguage(code, true);
        }

        private void ApplyLanguage(string code, bool persist)
        {
            if (string.IsNullOrEmpty(code)) code = FallbackCode;
            if (IndexOf(code) < 0) code = FallbackCode;
            if (code == _code) return;

            _code = code;
            Fill(_text, code);
            Fill(_fallback, FallbackCode);
            if (persist)
            {
                PlayerPrefs.SetString(PrefKey, code);
                PlayerPrefs.SetInt(UserChoicePrefKey, 1);
                PlayerPrefs.Save();
            }
            if (Changed != null) Changed();
        }

        // ------------------------------------------------------------------ table

        private void Load()
        {
            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogWarning("[Dil] " + ResourcePath + " bulunamadı — metinler anahtar olarak görünecek.");
                _lines = new string[0];
                _codes = new string[0];
                return;
            }

            _lines = asset.text.Split('\n');
            _codes = _lines.Length > 0 ? Cells(_lines[0]) : new string[0];

            // ilk sütun anahtar, gerisi dil
            string[] names = null;
            for (int i = 1; i < _lines.Length; i++)
            {
                string[] c = Cells(_lines[i]);
                if (c.Length > 0 && c[0] == NameKey) { names = c; break; }
            }

            _languages.Clear();
            for (int i = 1; i < _codes.Length; i++)
            {
                string code = _codes[i].Trim();
                if (code.Length == 0) continue;
                _languages.Add(new Language
                {
                    Code = code,
                    Name = names != null && i < names.Length && names[i].Length > 0 ? names[i] : code
                });
            }
        }

        private void Fill(Dictionary<string, string> into, string code)
        {
            into.Clear();
            int col = IndexOf(code);
            if (col < 0) return;

            for (int i = 1; i < _lines.Length; i++)
            {
                string[] c = Cells(_lines[i]);
                if (c.Length <= col || c[0].Length == 0 || c[0][0] == '#') continue;
                string v = c[col];
                if (v.Length == 0) continue;
                // Accept both one- and two-backslash escaped line breaks. This keeps imported
                // translator rows readable even when a spreadsheet/export pass doubles escapes.
                into[c[0]] = v.Replace("\\\\n", "\n").Replace("\\n", "\n");
            }
        }

        private int IndexOf(string code)
        {
            for (int i = 1; i < _codes.Length; i++)
                if (_codes[i].Trim() == code) return i;
            return -1;
        }

        private static string[] Cells(string line)
        {
            return line.TrimEnd('\r', '\n').Split('\t');
        }

        // ------------------------------------------------------------------ first run

        /// <summary>Explicitly saved choice, else English. Legacy automatic device-language values are
        /// ignored once so old installs also start in English.</summary>
        private string Stored()
        {
            string saved = PlayerPrefs.GetString(PrefKey, "");
            return PlayerPrefs.GetInt(UserChoicePrefKey, 0) == 1
                   && !string.IsNullOrEmpty(saved) && IndexOf(saved) >= 0
                ? saved : FallbackCode;
        }
    }
}
