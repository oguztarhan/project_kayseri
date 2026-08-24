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

        private const string ResourcePath = "Diller/metinler";

        /// <summary>
        /// English: where a device whose language the game does not speak lands, and what a key another
        /// language has not been given yet falls back to.
        ///
        /// It was Turkish, on the reasoning that the table is authored in Turkish so that column is the
        /// one always filled. The tracking permission dialog was reasoned about the same way and that
        /// one cost a rejection — see <c>IOSBuildPostProcess.BaseLanguage</c>. English is the language a
        /// review specialist anywhere can read, the <c>en</c> column is as complete as the <c>tr</c> one,
        /// and a Turkish device still gets Turkish through <see cref="FromSystem"/>. Nobody loses.
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
            SetLanguage(Stored());
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
            if (string.IsNullOrEmpty(code)) code = FallbackCode;
            if (IndexOf(code) < 0) code = FallbackCode;
            if (code == _code) return;

            _code = code;
            Fill(_text, code);
            Fill(_fallback, FallbackCode);
            PlayerPrefs.SetString(PrefKey, code);
            PlayerPrefs.Save();
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
                into[c[0]] = v.Replace("\\n", "\n");
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

        /// <summary>Saved choice, else the device language when the game speaks it, else English.</summary>
        private string Stored()
        {
            string saved = PlayerPrefs.GetString(PrefKey, "");
            if (!string.IsNullOrEmpty(saved) && IndexOf(saved) >= 0) return saved;

            string device = FromSystem(Application.systemLanguage);
            return IndexOf(device) >= 0 ? device : FallbackCode;
        }

        private static string FromSystem(SystemLanguage l)
        {
            switch (l)
            {
                case SystemLanguage.Turkish: return "tr";
                case SystemLanguage.German: return "de";
                case SystemLanguage.French: return "fr";
                case SystemLanguage.Spanish: return "es";
                case SystemLanguage.Portuguese: return "pt";
                case SystemLanguage.Italian: return "it";
                case SystemLanguage.Polish: return "pl";
                case SystemLanguage.Russian: return "ru";
                case SystemLanguage.Indonesian: return "id";
                case SystemLanguage.Vietnamese: return "vi";
                case SystemLanguage.English: return "en";
                // Konuşmadığımız bir cihaz İngilizce'ye düşer. Japon, Çinli, Arap bir oyuncunun — ve
                // App Review uzmanının — okuyabileceği tek dil bu; Türk cihaz zaten yukarıdaki
                // satırdan Türkçe alıyor.
                default: return FallbackCode;
            }
        }
    }
}
