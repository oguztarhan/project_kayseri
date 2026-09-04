using Game.Systems;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class RosterLocalizationTests
    {
        private static readonly string[] Keys =
        {
            "kadro.sirala.0", "kadro.sirala.1", "kadro.sirala.2", "kadro.sirala.3",
            "kadro.filtre.0", "kadro.filtre.1", "kadro.filtre.2", "kadro.filtre.3",
            "kadro.bos", "kadro.simdi", "kadro.sonraki", "kadro.ilerleme",
        };

        [Test]
        public void SharedRosterCopyExistsInEveryLaunchLanguage()
        {
            var loc = new LocalizationService();
            for (int language = 0; language < loc.Languages.Count; language++)
            {
                loc.SetLanguage(loc.Languages[language].Code);
                for (int key = 0; key < Keys.Length; key++)
                {
                    string value = loc.Get(Keys[key]);
                    Assert.That(value, Is.Not.Empty, loc.Languages[language].Code + ": " + Keys[key]);
                    Assert.That(value, Is.Not.EqualTo(Keys[key]), loc.Languages[language].Code + ": " + Keys[key]);
                }

                Assert.DoesNotThrow(() => string.Format(loc.Get("kadro.simdi"), "+25%"));
                Assert.DoesNotThrow(() => string.Format(loc.Get("kadro.sonraki"), "+30%"));
                Assert.DoesNotThrow(() => string.Format(loc.Get("kadro.ilerleme"), 3, 6));
            }
        }
    }
}
