using Game.Core;
using Game.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    /// <summary>
    /// The sheet is built with no services registered and no scene around it, which is the state the
    /// roster screens make it in — both of them build themselves at runtime.
    /// </summary>
    public sealed class OddsSheetUiSmokeTests
    {
        private static RectTransform Host(GameObject go)
        {
            var rt = go.AddComponent<RectTransform>();
            return rt;
        }

        [Test]
        public void TheSheetStartsClosedAndOpensOnEachCrate()
        {
            ServiceLocator.Clear();
            var host = new GameObject("OranHost");
            try
            {
                var sheet = new OddsSheetUI(Host(host));
                Assert.That(sheet.Visible, Is.False, "the sheet must not be open before it is asked for");

                sheet.ShowMasterChest(MasterChest.Tuning.Default);
                Assert.That(sheet.Visible, Is.True);

                sheet.ShowCaptainCrate(CaptainCrate.Tuning.Default);
                Assert.That(sheet.Visible, Is.True);

                sheet.Hide();
                Assert.That(sheet.Visible, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(host);
                ServiceLocator.Clear();
            }
        }

        /// <summary>
        /// THE ROW LEAK. The two crates need different numbers of rows, and the rows are built once and
        /// reused. A captain sheet (five grades) opened after a master sheet (four lines) must not leave
        /// the fifth row showing a master line, and the other way round must not leave a stale grade.
        /// </summary>
        [Test]
        public void RowsLeftOverFromTheOtherCrateAreSwitchedOff()
        {
            ServiceLocator.Clear();
            var host = new GameObject("OranHost");
            try
            {
                var sheet = new OddsSheetUI(Host(host));

                sheet.ShowCaptainCrate(CaptainCrate.Tuning.Default);
                int captainRows = ActiveRows(host);
                Assert.That(captainRows, Is.GreaterThan(0), "every populated grade is a row");

                sheet.ShowMasterChest(MasterChest.Tuning.Default);
                Assert.That(ActiveRows(host), Is.EqualTo(4),
                            "the master chest states four lines and must show exactly four");
            }
            finally
            {
                Object.DestroyImmediate(host);
                ServiceLocator.Clear();
            }
        }

        /// <summary>
        /// The percentages have to come out in the game's own number language, not the machine's. On a
        /// Turkish handset an uncultured format writes "10,5%" here while the roster card an inch away
        /// writes "10.5%" — two number languages on one screen.
        /// </summary>
        [Test]
        public void PercentagesUseTheGamesOwnDecimalSeparator()
        {
            ServiceLocator.Clear();
            System.Globalization.CultureInfo before = System.Threading.Thread.CurrentThread.CurrentCulture;
            var host = new GameObject("OranHost");
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture =
                    new System.Globalization.CultureInfo("tr-TR");

                var sheet = new OddsSheetUI(Host(host));
                sheet.ShowCaptainCrate(CaptainCrate.Tuning.Default);

                bool sawFraction = false;
                Text[] labels = host.GetComponentsInChildren<Text>(true);
                for (int i = 0; i < labels.Length; i++)
                {
                    string text = labels[i].text;
                    if (text == null || !text.EndsWith("%")) continue;
                    Assert.That(text, Does.Not.Contain(","), "a comma is the handset's separator, not ours");
                    if (text.Contains(".")) sawFraction = true;
                }
                Assert.That(sawFraction, Is.True,
                            "the default table has a fractional rate, so one row must prove the separator");
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = before;
                Object.DestroyImmediate(host);
                ServiceLocator.Clear();
            }
        }

        private static int ActiveRows(GameObject host)
        {
            int active = 0;
            Transform[] all = host.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i].name.StartsWith("Satir") && all[i].gameObject.activeSelf) active++;
            return active;
        }
    }
}
