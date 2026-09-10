using Game.Core;
using Game.Systems;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// Every line the pet panel and the fight sheet's pet strip read, in every launch language. The
    /// table tests already refuse blank cells; this one refuses a key the pet screens ask for that
    /// the table does not have at all — which would otherwise render as the raw key.
    /// </summary>
    public sealed class PetLocalizationTests
    {
        private static readonly string[] Keys =
        {
            "dost.baslik", "dost.sandik", "dost.son", "dost.son_yok", "dost.inci", "dost.oz",
            "dost.merhamet", "dost.yuva", "dost.acik", "dost.yuva_kilit",
            "dost.fusyon", "dost.vazgec", "dost.onayla", "dost.fusyon_eksik", "dost.fusyon_oz_ile",
            "dost.fusyon_zirve", "dost.yok", "dost.en_iyi", "dost.bonus", "dost.oduller",
            "dost.kisa.0", "dost.kisa.1", "dost.kisa.2", "dost.kisa.3", "dost.kisa.4",
            // Borrowed from the captain chest, the foreman crate and the fight sheet on purpose.
            "kaptan.derece.0", "kaptan.derece.1", "kaptan.derece.2", "kaptan.derece.3", "kaptan.derece.4",
            "kaptan.ac", "kaptan.acCok", "kaptan.teselli", "kaptan.bulunmadi", "usta.devam",
            "deniz.st.manevra", "deniz.st.salvo", "deniz.st.sersem", "deniz.st.cancalma",
            "deniz.st.savunma", "deniz.cesaret", "deniz.rotaKilit", "ortak.kilitli", "gorev.odul_alindi",
        };

        private static readonly string[] OneArgument =
        {
            "dost.son", "dost.yuva", "dost.yuva_kilit", "dost.fusyon_eksik", "dost.en_iyi", "dost.bonus",
            "kaptan.acCok", "deniz.rotaKilit",
        };

        [Test]
        public void EveryPetLineExistsInEveryLaunchLanguage()
        {
            ForEachLanguage((loc, code) =>
            {
                for (int i = 0; i < Keys.Length; i++)
                    Assert.That(loc.Has(Keys[i]), Is.True, code + ": " + Keys[i]);
            });
        }

        [Test]
        public void EverySpeciesIsNamedInEveryLaunchLanguage()
        {
            ForEachLanguage((loc, code) =>
            {
                for (int species = 0; species < Pets.SpeciesCount; species++)
                {
                    string key = "dost." + Pets.IdOf(species) + ".ad";
                    Assert.That(loc.Has(key), Is.True, code + ": " + key);
                }
            });
        }

        [Test]
        public void EveryFormattedPetLineTakesItsArguments()
        {
            ForEachLanguage((loc, code) =>
            {
                for (int i = 0; i < OneArgument.Length; i++)
                {
                    string key = OneArgument[i];
                    string line = null;
                    Assert.DoesNotThrow(() => line = string.Format(loc.Get(key), "7"), code + ": " + key);
                    Assert.That(line, Does.Contain("7"), code + ": " + key + " drops its number");
                }
                string pity = string.Format(loc.Get("kaptan.teselli"), "EPIC", "7");
                Assert.That(pity, Does.Contain("EPIC").And.Contain("7"), code + ": kaptan.teselli");
            });
        }

        /// <summary>Walks every language, then puts the player's own choice back — SetLanguage
        /// writes PlayerPrefs, and a test has no business leaving the editor in Vietnamese.</summary>
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
