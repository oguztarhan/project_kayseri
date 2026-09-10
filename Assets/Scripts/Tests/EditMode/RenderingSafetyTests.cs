using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class RenderingSafetyTests
    {
        [Test]
        public void OperationCameraFarPlaneIsSolvedFromTheViewportInsteadOfTheArchipelago()
        {
            Type type = Type.GetType("Game.UI.OperationCameraBoot, Game.UI");
            Assert.That(type, Is.Not.Null);
            MethodInfo method = type.GetMethod("RequiredFarClip", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            float far = (float)method.Invoke(null, new object[]
            {
                Quaternion.Euler(46f, 90f, 0f), 30f, 9f / 16f, 1000f, 400f,
            });

            Assert.That(far, Is.GreaterThan(1000f), "Maximum zoom must remain fully visible.");
            Assert.That(far, Is.LessThan(5000f), "The old 20,000-unit depth range must not return.");
        }
    }
}
