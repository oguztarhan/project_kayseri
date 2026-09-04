using System.Reflection;
using Game.Core;
using Game.UI;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// The league screen builds itself with no service registered and no scene around it — which is
    /// exactly the state it is made in, since <c>HudUI</c> adds the component at runtime rather than
    /// the scene carrying one.
    /// </summary>
    public sealed class LadderUiSmokeTests
    {
        [Test]
        public void ScreenBuildsItsBoardAndClaimStripWithoutSceneReferences()
        {
            ServiceLocator.Clear();
            var host = new GameObject("LadderUiSmoke");
            try
            {
                LadderUI screen = host.AddComponent<LadderUI>();
                MethodInfo awake = typeof(LadderUI).GetMethod("Awake",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(awake, Is.Not.Null);
                awake.Invoke(screen, null);

                Canvas[] canvases = host.GetComponentsInChildren<Canvas>(true);
                Assert.That(canvases, Has.Length.EqualTo(1));

                Transform scrim = canvases[0].transform.Find("Karartma");
                Assert.That(scrim, Is.Not.Null);
                Assert.That(scrim.Find("Zemin"), Is.Not.Null);
                Assert.That(scrim.Find("Serit"), Is.Not.Null);

                // The podium is three cards with first in the middle, then six rows, then the pinned
                // "you" row and the strip a closed season's reward waits on.
                Assert.That(scrim.Find("Podyum0"), Is.Not.Null, "first place");
                Assert.That(scrim.Find("Podyum1"), Is.Not.Null, "second place");
                Assert.That(scrim.Find("Podyum2"), Is.Not.Null, "third place");
                Assert.That(scrim.Find("Satir3"), Is.Not.Null, "rank four");
                Assert.That(scrim.Find("Satir8"), Is.Not.Null, "rank nine");
                Assert.That(scrim.Find("SenSatiri"), Is.Not.Null);
                Assert.That(scrim.Find("OdulSeridi/Al"), Is.Not.Null);

                // A chest on EVERY position, podium and row alike, and each one a real button — it is
                // the only place the payout table is readable before a season ends.
                Assert.That(scrim.Find("Podyum0/Sandik"), Is.Not.Null);
                Assert.That(scrim.Find("Satir8/Sandik"), Is.Not.Null);
                Assert.That(scrim.Find("Satir8/Sandik").GetComponent<UnityEngine.UI.Button>(), Is.Not.Null);

                // The chest opens this, and it must start closed.
                Transform reward = scrim.Find("OdulKarti");
                Assert.That(reward, Is.Not.Null);
                Assert.That(reward.gameObject.activeSelf, Is.False);
                Assert.That(reward.Find("Kart/Sandik"), Is.Not.Null);

                // Decision D4's label has to exist before any board is drawn, because the refresh
                // only ever hides it — it is never the thing that creates it.
                Assert.That(scrim.Find("Temsili"), Is.Not.Null);

                // THE REGRESSION THAT MADE THE BOARD UNREADABLE. Every sprite slot is empty on a
                // runtime-built screen, so the panels come from UiSkin — and the slice has to be
                // decided from the sprite actually used. Typed Simple with preserveAspect on, a row
                // renders as an aspect-locked square instead of filling its rect, which is what put
                // the whole board in a pile.
                var row = scrim.Find("Satir3").GetComponent<UnityEngine.UI.Image>();
                Assert.That(row.sprite, Is.Not.Null, "a row must fall back to the kit panel");
                Assert.That(row.preserveAspect, Is.False,
                            "a row must stretch to its rect, not lock to the sprite's aspect");
            }
            finally
            {
                Object.DestroyImmediate(host);
                ServiceLocator.Clear();
            }
        }

        /// <summary>
        /// THE REGRESSION THIS SHIPPED WITH ONCE. Hung inside another Canvas, the screen's own
        /// ScreenSpaceOverlay canvas silently becomes a sub-canvas laid out in the parent's rect, and
        /// the whole board collapses into one pile of overlapping text. It has to leave any canvas it
        /// is created under before building.
        /// </summary>
        [Test]
        public void ScreenLeavesAnyCanvasItWasCreatedInside()
        {
            ServiceLocator.Clear();
            var parentCanvas = new GameObject("SahteHudKanvas", typeof(Canvas));
            var host = new GameObject("LigEkrani");
            host.transform.SetParent(parentCanvas.transform, false);
            try
            {
                LadderUI screen = host.AddComponent<LadderUI>();
                MethodInfo awake = typeof(LadderUI).GetMethod("Awake",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                awake.Invoke(screen, null);

                Assert.That(host.transform.parent, Is.Null,
                            "the screen must leave a canvas it was parented under");

                Canvas own = host.GetComponentInChildren<Canvas>(true);
                Assert.That(own, Is.Not.Null);
                Assert.That(own.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
                Assert.That(own.transform.parent, Is.EqualTo(host.transform),
                            "its canvas must be the outermost one, not nested in another");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(parentCanvas);
                ServiceLocator.Clear();
            }
        }
    }
}
