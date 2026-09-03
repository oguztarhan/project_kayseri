using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// The support id. Everything here is about a string a human has to handle: it must look the same
    /// every time it is derived, it must not contain a character that can be misread, and a string that
    /// is not one of ours must be recognised as not one of ours.
    /// </summary>
    public sealed class PlayerIdTests
    {
        private static readonly Guid Sample = new Guid("6f1c2b40-9a8d-4e17-b3d5-0c9e77a41f28");

        [Test]
        public void FormattedInThreeGroupsOfFour()
        {
            string id = PlayerId.From(Sample);

            Assert.AreEqual(14, id.Length);
            Assert.AreEqual('-', id[4]);
            Assert.AreEqual('-', id[9]);
            Assert.IsTrue(PlayerId.IsValid(id), id);
        }

        [Test]
        public void SameGuidAlwaysGivesTheSameId()
        {
            Assert.AreEqual(PlayerId.From(Sample), PlayerId.From(Sample));
        }

        /// <summary>
        /// Two installs must not land on one id. Five thousand draws is far short of proving that, but
        /// it is enough to catch the mistakes that actually happen — folding the guid down to a byte,
        /// dividing where a modulo was meant, or seeding from a clock.
        /// </summary>
        [Test]
        public void DifferentGuidsGiveDifferentIds()
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < 5000; i++) seen.Add(PlayerId.From(Guid.NewGuid()));

            Assert.AreEqual(5000, seen.Count);
        }

        /// <summary>0/O and 1/I/L are what a support desk loses time to, so they are not in the alphabet
        /// at all — including U, which is dropped so no draw can spell a word nobody meant.</summary>
        [Test]
        public void NeverDrawsALookAlikeSymbol()
        {
            for (int i = 0; i < 2000; i++)
            {
                string id = PlayerId.From(Guid.NewGuid());
                foreach (char c in "01ILOU")
                    Assert.AreEqual(-1, id.IndexOf(c), "id " + id + " contains " + c);
            }
        }

        [Test]
        public void EveryDrawIsValid()
        {
            for (int i = 0; i < 2000; i++)
            {
                string id = PlayerId.From(Guid.NewGuid());
                Assert.IsTrue(PlayerId.IsValid(id), id);
            }
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("ABCD-EFGH-JKM")]          // one symbol short
        [TestCase("ABCD-EFGH-JKMNP")]        // one symbol long
        [TestCase("ABCDEFGHJKMN")]           // no separators
        [TestCase("ABCD-EFGHJ-KMN")]         // separator in the wrong place
        [TestCase("ABC-DEFGH-JKMN")]
        [TestCase("abcd-efgh-jkmn")]         // lower case is not what we ever show
        [TestCase("ABCD EFGH JKMN")]         // spaces are not separators
        [TestCase("ABCD-EFGH-JKM0")]         // zero is not in the alphabet
        [TestCase("ABCD-EFGH-JKMI")]
        [TestCase("ABCD-EFGH-JK@N")]
        public void RejectsAnythingWeDidNotMint(string id)
        {
            Assert.IsFalse(PlayerId.IsValid(id));
        }
    }
}
