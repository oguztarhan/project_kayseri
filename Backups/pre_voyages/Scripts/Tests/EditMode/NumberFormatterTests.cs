using NUnit.Framework;
using Game.Core;

namespace Game.Tests
{
    public class NumberFormatterTests
    {
        [Test]
        public void Zero() => Assert.AreEqual("0", NumberFormatter.Format(new BigDouble(0d)));

        [Test]
        public void SmallNumber() => Assert.AreEqual("42", NumberFormatter.Format(new BigDouble(42d)));

        [Test]
        public void Thousands() => Assert.AreEqual("1.5K", NumberFormatter.Format(new BigDouble(1500d)));

        [Test]
        public void Millions() => Assert.AreEqual("2.3M", NumberFormatter.Format(new BigDouble(2300000d)));

        [Test]
        public void Billions() => Assert.AreEqual("1B", NumberFormatter.Format(new BigDouble(1000000000d)));

        [Test]
        public void LetterTier_aa() => Assert.AreEqual("1aa", NumberFormatter.Format(new BigDouble(1d, 15)));

        [Test]
        public void Suffixes()
        {
            Assert.AreEqual("K", NumberFormatter.SuffixFor(1));
            Assert.AreEqual("T", NumberFormatter.SuffixFor(4));
            Assert.AreEqual("aa", NumberFormatter.SuffixFor(5));
            Assert.AreEqual("ab", NumberFormatter.SuffixFor(6));
        }
    }
}
