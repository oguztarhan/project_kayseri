using Game.Systems;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public sealed class StorePageTests
    {
        [Test]
        public void GooglePlayUrl_UsesPackageIdentifier()
        {
            Assert.AreEqual(
                "https://play.google.com/store/apps/details?id=com.intakeentertainment.islandminingtycoon",
                StorePage.GooglePlayUrl("com.intakeentertainment.islandminingtycoon"));
        }

        [TestCase("1234567890")]
        [TestCase("id1234567890")]
        public void AppStoreUrl_WithNumericId_UsesProductPage(string id)
        {
            Assert.AreEqual("https://apps.apple.com/app/id1234567890", StorePage.AppStoreUrl(id, "Ignored"));
        }

        [Test]
        public void AppStoreUrl_WithoutId_UsesEncodedSearch()
        {
            Assert.AreEqual(
                "https://apps.apple.com/us/search?term=Island%20Mining%20Tycoon",
                StorePage.AppStoreUrl("", "Island Mining Tycoon"));
        }
    }
}
