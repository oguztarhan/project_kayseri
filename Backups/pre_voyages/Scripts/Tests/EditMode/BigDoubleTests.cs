using System;
using NUnit.Framework;
using Game.Core;

namespace Game.Tests
{
    public class BigDoubleTests
    {
        [Test]
        public void Normalizes_LargeValue()
        {
            var v = new BigDouble(12345d); // 1.2345e4
            Assert.AreEqual(4L, v.Exponent);
            Assert.That(v.Mantissa, Is.EqualTo(1.2345d).Within(1e-9));
        }

        [Test]
        public void Zero_IsZero()
        {
            Assert.IsTrue(BigDouble.Zero.IsZero);
            Assert.IsTrue(new BigDouble(0d).IsZero);
        }

        [Test]
        public void Add_SameMagnitude()
        {
            var r = new BigDouble(100d) + new BigDouble(50d);
            Assert.That(r.ToDouble(), Is.EqualTo(150d).Within(1e-6));
        }

        [Test]
        public void Add_WildlyDifferentMagnitudes_ReturnsLarger()
        {
            var big = new BigDouble(1d, 40);
            var small = new BigDouble(1d, 0);
            var r = big + small;
            Assert.AreEqual(40L, r.Exponent);
            Assert.That(r.Mantissa, Is.EqualTo(1d).Within(1e-9));
        }

        [Test]
        public void Multiply_AddsExponents()
        {
            var r = new BigDouble(2d, 5) * new BigDouble(3d, 7); // 6e12
            Assert.AreEqual(12L, r.Exponent);
            Assert.That(r.Mantissa, Is.EqualTo(6d).Within(1e-9));
        }

        [Test]
        public void Divide_SubtractsExponents()
        {
            var r = new BigDouble(6d, 12) / new BigDouble(3d, 7); // 2e5
            Assert.AreEqual(5L, r.Exponent);
            Assert.That(r.Mantissa, Is.EqualTo(2d).Within(1e-9));
        }

        [Test]
        public void Subtract_ToZero()
        {
            var r = new BigDouble(500d) - new BigDouble(500d);
            Assert.IsTrue(r.IsZero);
        }

        [Test]
        public void Comparison_Works()
        {
            Assert.IsTrue(new BigDouble(1d, 10) > new BigDouble(9d, 9));
            Assert.IsTrue(new BigDouble(5d) < new BigDouble(1d, 3));
            Assert.IsTrue(new BigDouble(100d) == new BigDouble(1d, 2));
        }

        [Test]
        public void Pow_Static_MatchesExpected()
        {
            double expected = Math.Pow(1.09d, 100d);
            var r = BigDouble.Pow(1.09d, 100d);
            Assert.That(r.ToDouble(), Is.EqualTo(expected).Within(expected * 1e-2));
        }

        [Test]
        public void HugeValue_StaysExact_InExponent()
        {
            var r = BigDouble.Pow(10d, 500d); // 1e500 — far beyond double range
            Assert.AreEqual(500L, r.Exponent);
            Assert.That(r.Mantissa, Is.EqualTo(1d).Within(1e-6));
        }
    }
}
