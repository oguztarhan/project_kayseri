using Game.Core;
using Game.Systems;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Minting, and the one rule around it: the id on a save is minted once and then never moves.
    /// Everything here runs with a null <see cref="SaveService"/>, which is the no-disk path — the
    /// order of the write against the return is asserted by reading the code, not by writing a file
    /// into the editor's persistent data path from a test.
    /// </summary>
    public sealed class PlayerIdentityTests
    {
        [Test]
        public void MintsAnIdOnTheFirstAsk()
        {
            var data = new SaveData();
            Assert.IsEmpty(data.playerId);

            string id = PlayerIdentity.Ensure(data, null);

            Assert.IsTrue(PlayerId.IsValid(id), id);
            Assert.AreEqual(id, data.playerId);
        }

        [Test]
        public void KeepsTheIdItAlreadyHas()
        {
            var data = new SaveData();
            string first = PlayerIdentity.Ensure(data, null);

            Assert.AreEqual(first, PlayerIdentity.Ensure(data, null));
            Assert.AreEqual(first, PlayerIdentity.Ensure(data, null));
        }

        /// <summary>A save carrying something that is not one of ours — hand-edited, half-written, or
        /// from a format we no longer use — is given a real id rather than shown that.</summary>
        [TestCase("")]
        [TestCase("0")]
        [TestCase("hello")]
        [TestCase("abcd-efgh-jkmn")]
        public void ReplacesAnIdItCouldNotHaveMinted(string stored)
        {
            var data = new SaveData { playerId = stored };

            string id = PlayerIdentity.Ensure(data, null);

            Assert.AreNotEqual(stored, id);
            Assert.IsTrue(PlayerId.IsValid(id), id);
        }

        /// <summary>
        /// Unity hands back thirty-two zeros rather than an empty string when there is no build to name,
        /// which is exactly the state a test runs in — so this asserts the case that put
        /// `v1.0 · 00000000` on the screen once, from the one place it can be asserted honestly.
        /// </summary>
        [Test]
        public void AnUnbuiltRunNamesNoBuild()
        {
            Assert.IsEmpty(PlayerIdentity.Build());
            Assert.AreEqual("v" + UnityEngine.Application.version, PlayerIdentity.VersionLine());
            StringAssert.DoesNotContain("·", PlayerIdentity.VersionLine());
        }

        [Test]
        public void WithoutASaveThereIsNoId()
        {
            Assert.AreEqual(string.Empty, PlayerIdentity.Ensure(null, null));
        }
    }
}
