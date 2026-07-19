using NUnit.Framework;
using Game.Core;
using Game.Systems;

namespace Game.Tests
{
    public class SaveServiceTests
    {
        [Test]
        public void RoundTrip_PreservesData()
        {
            var svc = new SaveService();
            var data = new SaveData { version = 3 };
            data.wallet.cash = new BigDouble(1.2345d, 20);
            data.wallet.gems = 77;

            byte[] blob = svc.Encrypt(data);
            var loaded = svc.Decrypt(blob, out bool tampered);

            Assert.IsFalse(tampered);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(3, loaded.version);
            Assert.AreEqual(77L, loaded.wallet.gems);
            Assert.AreEqual(20L, loaded.wallet.cash.Exponent);
            Assert.That(loaded.wallet.cash.Mantissa, Is.EqualTo(1.2345d).Within(1e-9));
        }

        [Test]
        public void Tamper_IsDetected()
        {
            var svc = new SaveService();
            var data = new SaveData();
            data.wallet.gems = 5;
            byte[] blob = svc.Encrypt(data);

            blob[blob.Length - 1] ^= 0xFF; // corrupt a ciphertext byte

            var loaded = svc.Decrypt(blob, out bool tampered);
            Assert.IsTrue(tampered);
            Assert.IsNull(loaded);
        }
    }
}
