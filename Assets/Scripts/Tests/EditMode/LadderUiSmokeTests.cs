using System.Reflection;
using Game.Core;
using Game.Systems;
using Game.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

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
                // Content sits in the safe-area wrapper UiBuild.InsetContent adds; the scrim
                // above it stays full-bleed so the dim still covers the notch and gesture bar.
                Transform content = scrim.Find("Guvenli");
                Assert.That(content, Is.Not.Null);
                Assert.That(content.Find("Zemin"), Is.Not.Null);
                Assert.That(content.Find("Serit"), Is.Not.Null);

                // The podium is three cards with first in the middle, then six rows, then the pinned
                // "you" row and the strip a closed season's reward waits on.
                Assert.That(content.Find("Podyum0"), Is.Not.Null, "first place");
                Assert.That(content.Find("Podyum1"), Is.Not.Null, "second place");
                Assert.That(content.Find("Podyum2"), Is.Not.Null, "third place");
                Assert.That(content.Find("Satir3"), Is.Not.Null, "rank four");
                Assert.That(content.Find("Satir8"), Is.Not.Null, "rank nine");
                Assert.That(content.Find("SenSatiri"), Is.Not.Null);
                Assert.That(content.Find("OdulSeridi/Al"), Is.Not.Null);

                // A chest on EVERY position, podium and row alike, and each one a real button — it is
                // the only place the payout table is readable before a season ends.
                Assert.That(content.Find("Podyum0/Sandik"), Is.Not.Null);
                Assert.That(content.Find("Satir8/Sandik"), Is.Not.Null);
                Assert.That(content.Find("Satir8/Sandik").GetComponent<UnityEngine.UI.Button>(), Is.Not.Null);

                // The chest opens this, and it must start closed.
                Transform reward = content.Find("OdulKarti");
                Assert.That(reward, Is.Not.Null);
                Assert.That(reward.gameObject.activeSelf, Is.False);
                Assert.That(reward.Find("Kart/Sandik"), Is.Not.Null);

                // Decision D4's label has to exist before any board is drawn, because the refresh
                // only ever hides it — it is never the thing that creates it.
                Assert.That(content.Find("Temsili"), Is.Not.Null);

                // THE REGRESSION THAT MADE THE BOARD UNREADABLE. Every sprite slot is empty on a
                // runtime-built screen, so the panels come from UiSkin — and the slice has to be
                // decided from the sprite actually used. Typed Simple with preserveAspect on, a row
                // renders as an aspect-locked square instead of filling its rect, which is what put
                // the whole board in a pile.
                var row = content.Find("Satir3").GetComponent<UnityEngine.UI.Image>();
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
        /// The points card: an info button opens it, its rows are the scoring rules read at runtime with
        /// this season's progress, it opens by itself once, and closing it is what stops that.
        /// </summary>
        [Test]
        public void PointsCardListsTheScoringRulesAndOpensByItselfOnce()
        {
            var data = new SaveData();
            GameObject host = null;
            try
            {
                LadderService ladder = NewLadder(data, out GoalService goals);
                goals.Record(Goals.Upgrades, 3L);
                Transform content = BuildScreen(ladder, out host, out LadderUI screen);

                Transform card = content.Find("PuanKarti");
                Assert.That(card, Is.Not.Null);
                Assert.That(content.Find("Bilgi").GetComponent<Button>(), Is.Not.Null, "the info button");

                screen.Show();
                Assert.That(card.gameObject.activeSelf, Is.True, "first open in a points season shows the card");

                Ladder.ScoringRule[] rules = Ladder.Scoring;
                for (int i = 0; i < rules.Length; i++)
                {
                    Transform row = card.Find("Kart/Kural" + i);
                    Assert.That(row.gameObject.activeSelf, Is.True);
                    Assert.That(row.Find("Ad/Text").GetComponent<Text>().text, Is.Not.Empty);
                    Assert.That(row.Find("Deger/Text").GetComponent<Text>().text, Is.EqualTo("+" + rules[i].PointsPerAction));
                    Assert.That(row.Find("Ilerleme/Text").GetComponent<Text>().text,
                                Is.EqualTo(ladder.CountedActions(rules[i]) + " / " + rules[i].SeasonCap));
                }
                Assert.That(card.Find("Kart/Kural0/Ilerleme/Text").GetComponent<Text>().text, Is.EqualTo("3 / " + rules[0].SeasonCap));
                Assert.That(card.Find("Kart/Gecis/Text").gameObject.activeSelf, Is.False);

                card.Find("Kart/Kapat").GetComponent<Button>().onClick.Invoke();
                Assert.That(card.gameObject.activeSelf, Is.False);
                Assert.That(data.ladder.pointsHelpSeen, Is.True);

                screen.Show();
                Assert.That(card.gameObject.activeSelf, Is.False, "the automatic open happens once");

                content.Find("Bilgi").GetComponent<Button>().onClick.Invoke();
                Assert.That(card.gameObject.activeSelf, Is.True, "the info button always opens it");
            }
            finally
            {
                if (host != null) Object.DestroyImmediate(host);
                ServiceLocator.Clear();
            }
        }

        /// <summary>A season an older build opened still ranks bars, so the card says that rather than
        /// listing rules the board is not using — and does not open by itself for it.</summary>
        [Test]
        public void PointsCardExplainsTheBarsSeasonInsteadOfListingRules()
        {
            var data = new SaveData();
            data.ladder.seasonId = Leaderboards.SeasonId("lig", Leaderboards.SeasonIndex(Leaderboards.SeasonEpochUnix,
                Leaderboards.ThreeDayCadenceSeconds, System.DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            data.ladder.points = false;
            GameObject host = null;
            try
            {
                Transform content = BuildScreen(NewLadder(data, out _), out host, out LadderUI screen);
                Transform card = content.Find("PuanKarti");

                screen.Show();
                Assert.That(card.gameObject.activeSelf, Is.False);

                content.Find("Bilgi").GetComponent<Button>().onClick.Invoke();
                Assert.That(card.gameObject.activeSelf, Is.True);
                Assert.That(card.Find("Kart/Gecis/Text").gameObject.activeSelf, Is.True);
                Assert.That(card.Find("Kart/Kural0").gameObject.activeSelf, Is.False);
                Assert.That(card.Find("Kart/Sinir/Text").gameObject.activeSelf, Is.False);
            }
            finally
            {
                if (host != null) Object.DestroyImmediate(host);
                ServiceLocator.Clear();
            }
        }

        [Test]
        public void EveryPointsCardKeyIsInTheLanguageTable()
        {
            var table = Resources.Load<TextAsset>("Diller/metinler");
            Assert.That(table, Is.Not.Null);
            foreach (string key in new[] { "lig.puan_baslik", "lig.puan_aciklama", "lig.puan_sinir", "lig.puan_gecis" })
                Assert.That(table.text, Does.Contain("\n" + key + "\t"), key);
        }

        private static LadderService NewLadder(SaveData data, out GoalService goals)
        {
            var wallet = new WalletService(data.wallet);
            goals = new GoalService(data, wallet);
            var board = new LocalLeaderboardService(null, Leaderboards.SeasonEpochUnix, Leaderboards.ThreeDayCadenceSeconds);
            return new LadderService(data, null, goals, board, wallet);
        }

        /// <summary>
        /// Builds with NO service registered and hands the league over afterwards. Registered before
        /// Awake, a live league makes the screen attach its opener to the HudUI of whatever scene the
        /// editor has open — a test must not reach into that.
        /// </summary>
        private static Transform BuildScreen(LadderService ladder, out GameObject host, out LadderUI screen)
        {
            ServiceLocator.Clear();
            host = new GameObject("LadderUiSmoke");
            screen = host.AddComponent<LadderUI>();
            MethodInfo awake = typeof(LadderUI).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            awake.Invoke(screen, null);
            typeof(LadderUI).GetField("_ladder", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(screen, ladder);
            return host.GetComponentInChildren<Canvas>(true).transform.Find("Karartma/Guvenli");
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
