using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// The support mail. Two things matter here and neither is cosmetic: the desk must be able to read
    /// the build off the body, and a <c>mailto:</c> must never be built out of an address that could
    /// carry something other than an address.
    /// </summary>
    public sealed class SupportTicketTests
    {
        private static string Body(string prompt = "Write here:")
        {
            return SupportTicket.Body("ABCD-EFGH-JKMN", "1.4.2", "8f3c1a02", "Android 14",
                                      "Pixel 7a", "tr", 7, prompt);
        }

        [Test]
        public void BodyCarriesEverythingTheDeskAsksFor()
        {
            string body = Body();

            StringAssert.Contains("id: ABCD-EFGH-JKMN", body);
            StringAssert.Contains("app: 1.4.2 (8f3c1a02)", body);
            StringAssert.Contains("platform: Android 14", body);
            StringAssert.Contains("device: Pixel 7a", body);
            StringAssert.Contains("lang: tr", body);
            StringAssert.Contains("save: v7", body);
        }

        /// <summary>The cursor opens at the end of the body, so the invitation to write has to be the
        /// last thing in it — the player is then already typing where they were asked to.</summary>
        [Test]
        public void PromptComesLast()
        {
            string body = Body("Sorununuzu buraya yazın:");

            StringAssert.EndsWith("Sorununuzu buraya yazın:\n", body);
            Assert.Less(body.IndexOf("save: v7"), body.IndexOf("Sorununuzu"));
        }

        [Test]
        public void MissingFactsAreMarkedRatherThanLeftBlank()
        {
            string body = SupportTicket.Body("", "", "", "", "", "", 7, "");

            StringAssert.Contains("id: ?", body);
            StringAssert.Contains("app: ?", body);
            StringAssert.Contains("device: ?", body);
        }

        [Test]
        public void SubjectCarriesTheIdSoAStrippedBodyIsStillTraceable()
        {
            Assert.AreEqual("Destek talebi [ABCD-EFGH-JKMN]",
                            SupportTicket.Subject("Destek talebi", "ABCD-EFGH-JKMN"));
            Assert.AreEqual("Destek talebi", SupportTicket.Subject("Destek talebi", ""));
            Assert.AreEqual("Support", SupportTicket.Subject("", ""));
        }

        [Test]
        public void MailtoEscapesTheSubjectAndTheBody()
        {
            string url = SupportTicket.Mailto("destek@example.com", "A & B", "line\nnext #2");

            Assert.AreEqual("mailto:destek@example.com?subject=A%20%26%20B&body=line%0Anext%20%232", url);
        }

        [Test]
        public void MailtoOmitsPartsThatAreEmpty()
        {
            Assert.AreEqual("mailto:destek@example.com?body=hi",
                            SupportTicket.Mailto("destek@example.com", "", "hi"));
            Assert.AreEqual("mailto:destek@example.com", SupportTicket.Mailto("destek@example.com", "", ""));
        }

        /// <summary>
        /// An address that is not one plain address gets no URL at all. The newline case is the one that
        /// matters — that is how a mailto is made to carry headers nobody typed — but every one of these
        /// is a field somebody could fill in wrongly, and a refusal is cheaper than a half-built URL.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        [TestCase("destek")]
        [TestCase("@example.com")]
        [TestCase("destek@")]
        [TestCase("destek@example")]
        [TestCase("destek@.com")]
        [TestCase("destek@example.")]
        [TestCase("a@b@example.com")]
        [TestCase("destek@example.com, ikinci@example.com")]
        [TestCase("Destek <destek@example.com>")]
        [TestCase("destek@example.com\nbcc: someone@example.com")]
        [TestCase("destek @example.com")]
        public void MailtoRefusesAnythingButOnePlainAddress(string address)
        {
            Assert.IsFalse(SupportTicket.IsPlainAddress(address));
            Assert.AreEqual(string.Empty, SupportTicket.Mailto(address, "subject", "body"));
        }

        [TestCase("destek@example.com")]
        [TestCase("destek+oyun@example.co.uk")]
        [TestCase("destek.ekibi_1@alt.example.com")]
        public void PlainAddressesAreAccepted(string address)
        {
            Assert.IsTrue(SupportTicket.IsPlainAddress(address));
        }
    }
}
