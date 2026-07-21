using NUnit.Framework;
using Game.Core;

namespace Game.Tests
{
    public class RefiningTests
    {
        [Test]
        public void CombineRecipe_LimitedByScarcestInput()
        {
            var input = new Inventory<string>(new BigDouble(1000d));
            var output = new Inventory<string>(new BigDouble(1000d));
            input.Add("iron", new BigDouble(10d));
            input.Add("coal", new BigDouble(4d));

            // Steel = 2 iron + 1 coal -> 1 steel
            var inputs = new (string, BigDouble)[] { ("iron", new BigDouble(2d)), ("coal", new BigDouble(1d)) };
            var made = Refining.Process(input, output, inputs, "steel", new BigDouble(1d), new BigDouble(100d));

            Assert.That(made.ToDouble(), Is.EqualTo(4d).Within(1e-6));           // coal caps it at 4
            Assert.That(output.Get("steel").ToDouble(), Is.EqualTo(4d).Within(1e-6));
            Assert.That(input.Get("coal").ToDouble(), Is.EqualTo(0d).Within(1e-6));
            Assert.That(input.Get("iron").ToDouble(), Is.EqualTo(2d).Within(1e-6)); // 10 - 4*2
        }

        [Test]
        public void LimitedByOutputSpace()
        {
            var input = new Inventory<string>(new BigDouble(1000d));
            var output = new Inventory<string>(new BigDouble(3d));
            input.Add("coal", new BigDouble(100d));

            var inputs = new (string, BigDouble)[] { ("coal", new BigDouble(1d)) };
            var made = Refining.Process(input, output, inputs, "coke", new BigDouble(1d), new BigDouble(100d));

            Assert.That(made.ToDouble(), Is.EqualTo(3d).Within(1e-6));
            Assert.IsTrue(output.IsFull);
        }

        [Test]
        public void NoInput_MakesNothing()
        {
            var input = new Inventory<string>(new BigDouble(100d));
            var output = new Inventory<string>(new BigDouble(100d));
            var inputs = new (string, BigDouble)[] { ("coal", new BigDouble(1d)) };
            var made = Refining.Process(input, output, inputs, "coke", new BigDouble(1d), new BigDouble(100d));
            Assert.That(made.ToDouble(), Is.EqualTo(0d).Within(1e-9));
        }
    }
}
