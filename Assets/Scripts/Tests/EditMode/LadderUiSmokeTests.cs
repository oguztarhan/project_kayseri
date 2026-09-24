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

                // The screen's own canvas, plus the list's sub-canvas nested inside it.
                Canvas[] canvases = host.GetComponentsInChildren<Canvas>(true);
                Assert.That(canvases, Has.Length.EqualTo(2));
                Assert.That(canvases[0].name, Is.EqualTo("LigKanvas"));
                Assert.That(canvases[1].name, Is.EqualTo("Liste"));
                Assert.That(canvases[1].transform.parent.GetComponentInParent<Canvas>(true), Is.EqualTo(canvases[0]),
                            "the list canvas must be nested, not a second screen");

                Transform scrim = canvases[0].transform.Find("Karartma");
                Assert.That(scrim, Is.Not.Null);
                // Content sits in the safe-area wrapper UiBuild.InsetContent adds; the scrim
                // above it stays full-bleed so the dim still covers the notch and gesture bar.
                // Inside that, the column that caps the layout at the 9:16 reference shape.
                Assert.That(scrim.Find("Guvenli"), Is.Not.Null);
                Transform content = scrim.Find("Guvenli/Sutun");
                Assert.That(content, Is.Not.Null);
                Assert.That(content.GetComponent<AspectCap>(), Is.Not.Null);
                Assert.That(content.Find("Zemin"), Is.Not.Null);
                Assert.That(content.Find("Serit"), Is.Not.Null);
                // The bow and the crowns keep their drawn shape at every portrait width.
                if (LigKit.Board != null)
                    Assert.That(content.Find("Zemin").GetComponent<FrameFit>(), Is.Not.Null);
                if (LigKit.Get("odul_pano") != null)
                    foreach (string card in new[] { "OdulKarti", "PuanKarti", "ProfilKarti" })
                        Assert.That(content.Find(card + "/Kart").GetComponent<FrameFit>(), Is.Not.Null, card);

                // The podium is three cards with first in the middle, then a scroll list of ranks 4-50,
                // then the pinned "you" bar and the strip a closed season's reward waits on.
                Assert.That(content.Find("Podyum0"), Is.Not.Null, "first place");
                Assert.That(content.Find("Podyum1"), Is.Not.Null, "second place");
                Assert.That(content.Find("Podyum2"), Is.Not.Null, "third place");
                Assert.That(content.Find("Podyum0/Avatar"), Is.Not.Null, "an avatar on every podium card");
                Transform list = content.Find("Liste/Icerik");
                Assert.That(list, Is.Not.Null);
                Assert.That(list.Find("Satir3"), Is.Not.Null, "rank four");
                Assert.That(list.Find("Satir49"), Is.Not.Null, "rank fifty");
                Assert.That(list.Find("Satir50"), Is.Null, "a Top 50, no more");
                Assert.That(list.Find("OdulsuzSinir"), Is.Not.Null, "the line under the last paying rank");
                Assert.That(content.Find("Liste").GetComponent<ScrollRect>().content, Is.EqualTo(list));
                Assert.That(content.Find("SenSatiri"), Is.Not.Null);
                Assert.That(content.Find("SenSatiri/Duzenle").GetComponent<Button>(), Is.Not.Null, "the profile opener");
                Assert.That(content.Find("OdulSeridi/Al"), Is.Not.Null);

                // A chest slot on EVERY position, podium and row alike, and each one a real button — it
                // is the only place the payout table is readable before a season ends.
                Assert.That(content.Find("Podyum0/Sandik"), Is.Not.Null);
                Assert.That(list.Find("Satir8/Sandik"), Is.Not.Null);
                Assert.That(list.Find("Satir8/Sandik").GetComponent<UnityEngine.UI.Button>(), Is.Not.Null);
                Assert.That(list.Find("Satir8/Avatar/Yuz"), Is.Not.Null);

                // The profile card starts closed.
                Assert.That(content.Find("ProfilKarti").gameObject.activeSelf, Is.False);
                Assert.That(content.Find("ProfilKarti/Kart/Ad/Alan").GetComponent<InputField>(), Is.Not.Null);
                Assert.That(content.Find("ProfilKarti/Kart/Avatarlar").childCount, Is.EqualTo(PlayerProfiles.AvatarCount));

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
                var row = list.Find("Satir3").GetComponent<UnityEngine.UI.Image>();
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

        /// <summary>Every key the Top 50 and the profile card read, in all twelve columns — a key with a
        /// short row would print the key itself in the languages it is missing from.</summary>
        [Test]
        public void EveryProfileAndTop50KeyIsInTheLanguageTable()
        {
            var table = Resources.Load<TextAsset>("Diller/metinler");
            Assert.That(table, Is.Not.Null);
            string[] lines = table.text.Split('\n');
            foreach (string key in new[] { "lig.temsili", "lig.sim_rakip", "lig.odulsuz", "lig.profil_baslik",
                                           "lig.profil_ad", "lig.profil_kural", "lig.profil_avatar", "lig.profil_kaydet",
                                           "lig.profil_iptal", "lig.profil_duzenle", "lig.profil_ipucu" })
            {
                string row = System.Array.Find(lines, l => l.StartsWith(key + "\t", System.StringComparison.Ordinal));
                Assert.That(row, Is.Not.Null, key);
                Assert.That(row.TrimEnd('\r').Split('\t'), Has.Length.EqualTo(12), key);
            }
        }

        /// <summary>
        /// The profile card opens by itself on the first visit, ahead of the points card. A name the
        /// rules refuse keeps it open and writes nothing. A valid one is saved with the picked avatar,
        /// shown on the player's bar, and the card does not open by itself again.
        /// </summary>
        [Test]
        public void ProfileCardOpensOnceSavesTheChoiceAndTheBoardShowsIt()
        {
            var data = new SaveData();
            GameObject host = null;
            try
            {
                LadderService ladder = NewLadder(data, out _);
                var profiles = new PlayerProfileService(data, null);
                Transform content = BuildScreen(ladder, out host, out LadderUI screen);
                typeof(LadderUI).GetField("_profiles", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(screen, profiles);
                Transform card = content.Find("ProfilKarti");
                Transform points = content.Find("PuanKarti");

                screen.Show();
                Assert.That(card.gameObject.activeSelf, Is.True, "first visit opens the profile card");
                Assert.That(points.gameObject.activeSelf, Is.False, "the points card waits its turn");

                var input = card.Find("Kart/Ad/Alan").GetComponent<InputField>();
                Button save = card.Find("Kart/Kaydet").GetComponent<Button>();

                input.text = "ab";
                save.onClick.Invoke();
                Assert.That(card.gameObject.activeSelf, Is.True, "a two-letter name is refused");
                Assert.That(data.profile.name, Is.Empty);

                input.text = "Kaya Kartal";
                card.Find("Kart/Avatarlar/Avatar4").GetComponent<Button>().onClick.Invoke();
                save.onClick.Invoke();

                Assert.That(card.gameObject.activeSelf, Is.False);
                Assert.That(data.profile.name, Is.EqualTo("Kaya Kartal"));
                Assert.That(data.profile.avatar, Is.EqualTo(4));
                Assert.That(data.profile.prompted, Is.True);
                Assert.That(points.gameObject.activeSelf, Is.True, "then the points card opens");
                Assert.That(content.Find("SenSatiri/Ad/Text").GetComponent<Text>().text,
                            Is.EqualTo(EtkinlikKit.OneLine("Kaya Kartal")));

                points.Find("Kart/Kapat").GetComponent<Button>().onClick.Invoke();
                screen.Show();
                Assert.That(card.gameObject.activeSelf, Is.False, "the automatic open happens once");

                content.Find("SenSatiri/Duzenle").GetComponent<Button>().onClick.Invoke();
                Assert.That(card.gameObject.activeSelf, Is.True, "EDIT always opens it");
                Assert.That(input.text, Is.EqualTo("Kaya Kartal"), "on the saved name");
            }
            finally
            {
                if (host != null) Object.DestroyImmediate(host);
                ServiceLocator.Clear();
            }
        }

        /// <summary>Forty-seven list rows under the podium, every one seated. Ranks 31-50 carry no
        /// chest, and every rival row says it is simulated.</summary>
        [Test]
        public void EveryRankIsSeatedAndOnlyThePayingRanksCarryAChest()
        {
            var data = new SaveData();
            GameObject host = null;
            try
            {
                Transform content = BuildScreen(NewLadder(data, out _), out host, out LadderUI screen);
                screen.Show();

                Transform list = content.Find("Liste/Icerik");
                for (int i = PodiumCount; i < Leaderboards.CohortSize; i++)
                {
                    Transform row = list.Find("Satir" + i);
                    int rank = i + 1;
                    Assert.That(row.gameObject.activeSelf, Is.True, "rank " + rank);
                    Assert.That(row.Find("Sira/Text").GetComponent<Text>().text, Is.EqualTo(rank.ToString()));
                    Assert.That(row.Find("Sandik").GetComponent<Image>().enabled,
                                Is.EqualTo(rank <= Leaderboards.RewardedRanks), "chest on rank " + rank);
                    Assert.That(row.Find("Avatar/Yuz").GetComponent<Image>().sprite, Is.Not.Null, "avatar on rank " + rank);
                }

                // A player on zero points sits last; everyone above them is a labelled rival.
                for (int i = PodiumCount; i < Leaderboards.CohortSize - 1; i++)
                    Assert.That(list.Find("Satir" + i + "/Etiket/Text").GetComponent<Text>().text,
                                Is.EqualTo(EtkinlikKit.OneLine(Loc.T("lig.sim_rakip"))), "rank " + (i + 1));
            }
            finally
            {
                if (host != null) Object.DestroyImmediate(host);
                ServiceLocator.Clear();
            }
        }

        private const int PodiumCount = 3;

        /// <summary>The column is the whole parent on a phone-shaped box and a centred 9:16 strip on a
        /// wider one.</summary>
        [Test]
        public void AspectCapLeavesPhonesAloneAndCentresATabletColumn()
        {
            var parent = new GameObject("Kutu", typeof(RectTransform));
            try
            {
                var box = (RectTransform)parent.transform;
                var child = new GameObject("Sutun", typeof(RectTransform));
                child.transform.SetParent(box, false);
                var column = (RectTransform)child.transform;

                box.sizeDelta = new Vector2(1000f, 2000f);            // taller than 9:16
                AspectCap.Wrap(column, 0.5625f);
                Assert.That(column.rect.width, Is.EqualTo(1000f).Within(0.01f));

                box.sizeDelta = new Vector2(1500f, 2000f);            // a 3:4 tablet
                column.GetComponent<AspectCap>().Apply();
                Assert.That(column.rect.width, Is.EqualTo(1125f).Within(0.01f));
                Assert.That(column.rect.height, Is.EqualTo(2000f).Within(0.01f));
                Assert.That(column.offsetMin.x, Is.EqualTo(-column.offsetMax.x).Within(0.01f), "centred");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        /// <summary>FrameFit scales a frame's slice borders by box width over art width, and lands on
        /// the same number however often a layout pass re-runs it — an answer that fed on its own last
        /// value would shrink the frame's rim a little on every pass.</summary>
        [Test]
        public void FrameFitHoldsTheOrnamentsShapeAndSettles()
        {
            var tex = new Texture2D(100, 50);
            var sprite = Sprite.Create(tex, new Rect(0, 0, 100, 50), new Vector2(0.5f, 0.5f), 100f, 0u,
                                       SpriteMeshType.FullRect, new Vector4(10, 10, 10, 30));
            var go = new GameObject("Cerceve", typeof(RectTransform), typeof(Canvas), typeof(Image));
            try
            {
                var image = go.GetComponent<Image>();
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
                ((RectTransform)go.transform).sizeDelta = new Vector2(50f, 200f);

                FrameFit fit = go.AddComponent<FrameFit>();
                fit.Fit(); fit.Fit(); fit.Fit();
                float expected = 100f / (50f * image.pixelsPerUnit);
                Assert.That(image.pixelsPerUnitMultiplier, Is.EqualTo(expected).Within(1e-4f));
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(tex);
            }
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
            return host.GetComponentInChildren<Canvas>(true).transform.Find("Karartma/Guvenli/Sutun");
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
