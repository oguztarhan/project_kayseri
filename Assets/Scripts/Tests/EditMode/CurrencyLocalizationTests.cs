using System.Collections.Generic;
using Game.Systems;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Phase 5: every line the wallet, the receipts and the reward reveals read for a currency. The
    /// table tests already refuse blank cells; these refuse a missing key, a Turkish or English cell
    /// that is only the other language copied across, two currencies sharing one name, and a
    /// formatted line that drops its number.
    /// </summary>
    public sealed class CurrencyLocalizationTests
    {
        /// <summary>Formatted currency lines and how many arguments each takes.</summary>
        private static readonly KeyValuePair<string, int>[] Formatted =
        {
            new KeyValuePair<string, int>("currency.gain", 2),
            new KeyValuePair<string, int>("currency.cost", 2),
            new KeyValuePair<string, int>("currency.amount", 2),
            new KeyValuePair<string, int>("wallet.next_regen", 1),
            new KeyValuePair<string, int>("madenci.kusanildi", 2),
            new KeyValuePair<string, int>("madenci.hurdaya_dondu", 2),
            new KeyValuePair<string, int>("madenci.hedef.maliyet", 4),
        };

        private static readonly string[] WalletChrome =
        {
            "wallet.title", "wallet.open", "wallet.close", "wallet.full", "wallet.next_regen",
            "wallet.group.main", "wallet.group.sea", "wallet.group.crafting", "wallet.group.mining",
            "wallet.group.pets",
        };

        [Test]
        public void EveryCurrencyHasANameAndADescriptionInEveryLaunchLanguage()
        {
            ForEachLanguage((loc, code) =>
            {
                foreach (CurrencyDefinition d in Definitions())
                {
                    Assert.That(loc.Has(d.LocalizedNameKey), Is.True, code + ": " + d.LocalizedNameKey);
                    Assert.That(loc.Has(d.LocalizedDescriptionKey), Is.True, code + ": " + d.LocalizedDescriptionKey);
                }
                for (int i = 0; i < WalletChrome.Length; i++)
                    Assert.That(loc.Has(WalletChrome[i]), Is.True, code + ": " + WalletChrome[i]);
            });
        }

        /// <summary>Turkish is the team's language and English the fallback; neither may be the other
        /// copied across, and a description is more than its name repeated.</summary>
        [Test]
        public void TurkishAndEnglishAreEachWrittenInTheirOwnLanguage()
        {
            var tr = new Dictionary<string, string>();
            var en = new Dictionary<string, string>();
            ForEachLanguage((loc, code) =>
            {
                if (code != "tr" && code != "en") return;
                Dictionary<string, string> into = code == "tr" ? tr : en;
                foreach (CurrencyDefinition d in Definitions())
                {
                    into[d.LocalizedNameKey] = loc.Get(d.LocalizedNameKey);
                    into[d.LocalizedDescriptionKey] = loc.Get(d.LocalizedDescriptionKey);
                }
                into["wallet.next_regen"] = loc.Get("wallet.next_regen");
                into["madenci.hurdaya_dondu"] = loc.Get("madenci.hurdaya_dondu");
            });

            Assert.That(tr.Count, Is.EqualTo(en.Count).And.GreaterThan(0));
            foreach (KeyValuePair<string, string> pair in tr)
                Assert.That(pair.Value, Is.Not.EqualTo(en[pair.Key]), pair.Key + " is the same in Turkish and English");
            foreach (CurrencyDefinition d in Definitions())
            {
                Assert.That(tr[d.LocalizedDescriptionKey], Is.Not.EqualTo(tr[d.LocalizedNameKey]), "tr " + d.Id);
                Assert.That(en[d.LocalizedDescriptionKey], Is.Not.EqualTo(en[d.LocalizedNameKey]), "en " + d.Id);
            }
        }

        /// <summary>The two collisions this phase exists to end: Craft vs Mining Points both read
        /// "PTS", and sea Salvage vs Mining Scrap both read "hurda". No language may bring either back.</summary>
        [Test]
        public void NoTwoCurrenciesShareANameInAnyLanguage()
        {
            ForEachLanguage((loc, code) =>
            {
                var seen = new Dictionary<string, CurrencyId>();
                foreach (CurrencyDefinition d in Definitions())
                {
                    string name = loc.Get(d.LocalizedNameKey);
                    Assert.That(seen.ContainsKey(name), Is.False,
                                code + ": " + d.Id + " and " + (seen.ContainsKey(name) ? seen[name].ToString() : "") +
                                " are both called " + name);
                    seen[name] = d.Id;
                }
            });
        }

        [Test]
        public void EveryFormattedCurrencyLineTakesItsArgumentsInEveryLanguage()
        {
            ForEachLanguage((loc, code) =>
            {
                for (int i = 0; i < Formatted.Length; i++)
                {
                    string key = Formatted[i].Key;
                    var args = new object[Formatted[i].Value];
                    for (int a = 0; a < args.Length; a++) args[a] = "<" + a + ">";
                    string line = null;
                    Assert.DoesNotThrow(() => line = string.Format(loc.Get(key), args), code + ": " + key);
                    for (int a = 0; a < args.Length; a++)
                        Assert.That(line, Does.Contain((string)args[a]), code + ": " + key + " drops argument " + a);
                }
            });
        }

        [Test]
        public void TheRetiredMiningLinesAreGoneFromTheTable()
        {
            // Replaced by an outcome line plus a named receipt; madenci.hedef.hurda asked for four
            // arguments where the screen passed three and would have thrown on the targeted scrap path.
            var loc = new LocalizationService();
            Assert.That(loc.Has("madenci.hurda"), Is.False);
            Assert.That(loc.Has("madenci.hedef.kusanildi"), Is.False);
            Assert.That(loc.Has("madenci.hedef.hurda"), Is.False);
            Assert.That(loc.Has("deniz.ganimet"), Is.False);
        }

        private static IEnumerable<CurrencyDefinition> Definitions()
        {
            foreach (CurrencyId id in System.Enum.GetValues(typeof(CurrencyId)))
            {
                Assert.That(CurrencyRegistry.TryDescribe(id, out CurrencyDefinition d), Is.True, id.ToString());
                yield return d;
            }
        }

        /// <summary>Walks every language, then puts the player's own choice back — SetLanguage
        /// writes PlayerPrefs, and a test has no business leaving the editor in another language.</summary>
        private static void ForEachLanguage(System.Action<LocalizationService, string> check)
        {
            var loc = new LocalizationService();
            string original = loc.Code;
            try
            {
                for (int language = 0; language < loc.Languages.Count; language++)
                {
                    string code = loc.Languages[language].Code;
                    loc.SetLanguage(code);
                    check(loc, code);
                }
            }
            finally
            {
                loc.SetLanguage(original);
            }
        }
    }
}
