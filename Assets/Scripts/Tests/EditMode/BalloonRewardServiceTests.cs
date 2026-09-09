using NUnit.Framework;
using Game.Systems;

namespace Game.Tests
{
    public sealed class BalloonRewardServiceTests
    {
        [Test]
        public void DiamondRollPaysFixedGemsAndStartsTheThreeMinuteCooldown()
        {
            var data = new SaveData();
            var time = new TimeService();
            var free = new FreeRewardService(data, time);
            var wallet = new WalletService(data.wallet);
            var balloon = new BalloonRewardService(free, wallet, null, data);

            BalloonRewardService.Receipt receipt = balloon.TryClaim(100d, 5d, 100d, 0.10d, 12L, 0.05d);

            Assert.That(receipt.Paid, Is.True);
            Assert.That(receipt.Gems, Is.True);
            Assert.That(wallet.Gems, Is.EqualTo(12L));
            Assert.That(balloon.Ready, Is.False);
            Assert.That(balloon.CooldownLeft, Is.GreaterThan(175f));
        }

        [Test]
        public void CashRewardUsesIncomeButNeverFallsBelowTheNewPlayerFloor()
        {
            var data = new SaveData();
            var time = new TimeService();
            var free = new FreeRewardService(data, time);
            var wallet = new WalletService(data.wallet);
            var balloon = new BalloonRewardService(free, wallet, null, data);

            BalloonRewardService.Receipt receipt = balloon.TryClaim(7d, 5d, 100d, 0.10d, 12L, 0.5d);

            Assert.That(receipt.Paid, Is.True);
            Assert.That(receipt.Gems, Is.False);
            Assert.That(receipt.CashAmount, Is.EqualTo(100d));
            Assert.That(wallet.Cash.ToDouble(), Is.EqualTo(100d));
        }
    }
}
