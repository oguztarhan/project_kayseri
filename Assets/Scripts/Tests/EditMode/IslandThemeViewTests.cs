using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Game.Core;
using Game.Data;
using Game.Gameplay;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>
    /// What a theme does to the island once it is on it.
    ///
    /// The applier's whole contract is "rewrite material slots and touch nothing else", so most of
    /// this is about the swap table: that it dresses what it names, leaves alone what it does not,
    /// and — the part that is easy to get wrong — that it can take one chapter's look OFF while it
    /// puts the next one on, no matter which of the two a given renderer is wearing.
    ///
    /// Which theme a chapter gets is <see cref="IslandThemeTests"/>; this assumes that works.
    /// </summary>
    public class IslandThemeViewTests
    {
        private readonly List<UnityEngine.Object> _junk = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _junk.Count; i++)
                if (_junk[i] != null) UnityEngine.Object.DestroyImmediate(_junk[i]);
            _junk.Clear();
            ServiceLocator.Clear();
        }

        // ------------------------------------------------------------------ the table
        [Test]
        public void AnIslandWithNoThemeAtAllIsLeftExactlyAsAuthored()
        {
            var map = new Dictionary<Material, Material>();

            IslandThemeView.BuildSwapMap(null, null, map);

            Assert.That(map, Is.Empty);
            Assert.That(IslandThemeView.Repaint(Island(Mat("grass")), map, new List<Renderer>()),
                        Is.Zero, "an empty table must not rewrite a single slot");
        }

        [Test]
        public void AThemeDressesEverySlotThatNamesItsMaterial()
        {
            Material grass = Mat("grass"), rock = Mat("rock");
            Material snow = Mat("snow"), ice = Mat("ice");
            Transform island = Island(grass, rock);
            Renderer pair = Renderers(island)[1];
            pair.sharedMaterials = new[] { grass, rock };

            Wear(island, Theme(Swap(grass, snow), Swap(rock, ice)));

            Assert.That(Renderers(island)[0].sharedMaterial, Is.SameAs(snow));
            Assert.That(pair.sharedMaterials[0], Is.SameAs(snow));
            Assert.That(pair.sharedMaterials[1], Is.SameAs(ice), "a second slot is dressed like the first");
        }

        /// <summary>
        /// The rule that lets half-finished theme art ship: an empty replacement is not a hole, it is
        /// "keep what is there".
        /// </summary>
        [Test]
        public void ARowWithNoReplacementLeavesItsMaterialAlone()
        {
            Material grass = Mat("grass"), rock = Mat("rock"), snow = Mat("snow");
            Transform island = Island(grass, rock);

            Wear(island, Theme(Swap(grass, snow), Swap(rock, null)));

            Assert.That(Renderers(island)[0].sharedMaterial, Is.SameAs(snow));
            Assert.That(Renderers(island)[1].sharedMaterial, Is.SameAs(rock), "the unfinished row keeps the island's own");
        }

        [Test]
        public void ANewChapterTakesTheLastOnesLookOffAsItPutsItsOwnOn()
        {
            Material grass = Mat("grass"), snow = Mat("snow"), sand = Mat("sand");
            Transform island = Island(grass);
            IslandThemeDefinition winter = Theme(Swap(grass, snow));

            Wear(island, winter);
            Assert.That(Renderers(island)[0].sharedMaterial, Is.SameAs(snow));

            Wear(island, winter, Theme(Swap(grass, sand)));

            Assert.That(Renderers(island)[0].sharedMaterial, Is.SameAs(sand),
                        "the island must not be left one chapter behind");
        }

        /// <summary>
        /// A chapter is allowed to dress less than the one before it. What the new theme has nothing
        /// to say about goes back to the island's own material rather than keeping the old paint.
        /// </summary>
        [Test]
        public void AMaterialTheNewChapterDoesNotDressGoesBackToTheIslandsOwn()
        {
            Material grass = Mat("grass"), rock = Mat("rock");
            Material snow = Mat("snow"), frost = Mat("frost"), sand = Mat("sand");
            Transform island = Island(grass, rock);
            IslandThemeDefinition winter = Theme(Swap(grass, snow), Swap(rock, frost));

            Wear(island, winter);
            Wear(island, winter, Theme(Swap(grass, sand)));

            Assert.That(Renderers(island)[0].sharedMaterial, Is.SameAs(sand));
            Assert.That(Renderers(island)[1].sharedMaterial, Is.SameAs(rock), "frost is not this chapter's");
        }

        /// <summary>
        /// Nothing is remembered per renderer, so a renderer that was not there for the last theme is
        /// still dressed correctly by the next one — it is holding the island's own material, and the
        /// table answers for that as well as for the old theme's.
        /// </summary>
        [Test]
        public void ARendererThatAppearedAfterTheLastChapterIsStillDressed()
        {
            Material grass = Mat("grass"), snow = Mat("snow"), sand = Mat("sand");
            Transform island = Island(grass);
            IslandThemeDefinition winter = Theme(Swap(grass, snow));
            Wear(island, winter);

            Renderer latecomer = Add(island, grass);   // built after the theme went on

            Wear(island, winter, Theme(Swap(grass, sand)));

            Assert.That(latecomer.sharedMaterial, Is.SameAs(sand));
            Assert.That(Renderers(island)[0].sharedMaterial, Is.SameAs(sand), "and the one that was dressed already");
        }

        [Test]
        public void SweepingTwiceWithTheSameThemeIsWorkTheSecondTimeDoesNotDo()
        {
            Material grass = Mat("grass"), snow = Mat("snow");
            Transform island = Island(grass);
            IslandThemeDefinition winter = Theme(Swap(grass, snow));

            Assert.That(Wear(island, winter), Is.EqualTo(1));
            Assert.That(Wear(island, winter, winter), Is.Zero,
                        "re-applying the theme it is wearing must rewrite nothing");
        }

        /// <summary>A building the player has not bought yet is switched off, and still has to match.</summary>
        [Test]
        public void ABuildingThatIsSwitchedOffIsDressedTooReadyForTheDayItIsBought()
        {
            Material grass = Mat("grass"), snow = Mat("snow");
            Transform island = Island(grass);
            Renderer ghost = Add(island, grass);
            ghost.gameObject.SetActive(false);

            Wear(island, Theme(Swap(grass, snow)));

            Assert.That(ghost.sharedMaterial, Is.SameAs(snow));
        }

        // ------------------------------------------------------------------ the lamps
        /// <summary>
        /// <c>BuildingLights</c> and <c>StreetLamps</c> find their emissive material by NAME at
        /// startup. A theme that renamed it would put the island's night lights out with nothing
        /// logged, so the row is refused and the reason is said out loud.
        /// </summary>
        [Test]
        public void AThemeCannotRenameTheMaterialTheNightLightsLookFor()
        {
            Material lamp = Mat("lamp_glow"), blue = Mat("lamp_glow_blue");
            var map = new Dictionary<Material, Material>();
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("lamp_glow"));

            IslandThemeView.BuildSwapMap(null, Theme(Swap(lamp, blue)), map);

            Assert.That(map, Is.Empty, "the lamps keep their material whatever the theme says");
        }

        [Test]
        public void ALampMaterialMayBeThemedWhenTheReplacementKeepsTheName()
        {
            Material lamp = Mat("lamp_glow"), colder = Mat("lamp_glow");
            var map = new Dictionary<Material, Material>();

            IslandThemeView.BuildSwapMap(null, Theme(Swap(lamp, colder)), map);

            Assert.That(map[lamp], Is.SameAs(colder));
        }

        // ------------------------------------------------------------------ the component
        [Test]
        public void TheIslandWearsTheThemeOfTheChapterItIsOn()
        {
            Material grass = Mat("grass"), snow = Mat("snow"), sand = Mat("sand");
            Transform island = Island(grass);
            IslandThemeSet set = Set(Theme(Swap(grass, snow), 0), Theme(Swap(grass, sand), 3));
            Progression(3);

            View(island, set);

            Assert.That(Renderers(island)[0].sharedMaterial, Is.SameAs(sand),
                        "loading into chapter 4 must not show chapter 1's island");
        }

        [Test]
        public void AdvancingAChapterRepaintsTheIslandThereAndThen()
        {
            Material grass = Mat("grass"), snow = Mat("snow"), sand = Mat("sand");
            Transform island = Island(grass);
            IslandThemeSet set = Set(Theme(Swap(grass, snow), 0), Theme(Swap(grass, sand), 1));
            ChapterProgressionService progression = Progression(0);
            View(island, set);
            Assert.That(Renderers(island)[0].sharedMaterial, Is.SameAs(snow));

            Advance(progression, 1);

            Assert.That(Renderers(island)[0].sharedMaterial, Is.SameAs(sand),
                        "the new chapter's look is the signal that the chapter changed");
        }

        [Test]
        public void AnIslandWithNoThemeSetKeepsItsOwnMaterialsOnEveryChapter()
        {
            Material grass = Mat("grass");
            Transform island = Island(grass);
            Progression(5);

            View(island, null);

            Assert.That(Renderers(island)[0].sharedMaterial, Is.SameAs(grass));
        }

        [Test]
        public void AViewThatCannotFindItsIslandSaysSoAndStops()
        {
            Material grass = Mat("grass");
            Transform island = Island(grass);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("IslandThemeView"));

            var view = island.gameObject.AddComponent<IslandThemeView>();
            Set(view, "islandRootName", "Island_ThatIsNotHere");
            Awake(view);

            Assert.That(view.enabled, Is.False, "half-applying a theme is worse than not applying one");
        }

        // ------------------------------------------------------------------ helpers
        /// <summary>Puts <paramref name="to"/> on the island over <paramref name="from"/>; returns renderers changed.</summary>
        private static int Wear(Transform island, IslandThemeDefinition to) => Wear(island, null, to);

        private static int Wear(Transform island, IslandThemeDefinition from, IslandThemeDefinition to)
        {
            var map = new Dictionary<Material, Material>();
            IslandThemeView.BuildSwapMap(from, to, map);
            return IslandThemeView.Repaint(island, map, new List<Renderer>());
        }

        /// <summary>A scene root with one child renderer per material, which is the shape the map has.</summary>
        private Transform Island(params Material[] materials)
        {
            var root = new GameObject("Island_ThemeViewTest");
            _junk.Add(root);
            for (int i = 0; i < materials.Length; i++) Add(root.transform, materials[i]);
            return root.transform;
        }

        private static Renderer Add(Transform island, Material material)
        {
            var child = new GameObject("part" + island.childCount);
            child.transform.SetParent(island, false);
            var renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        private static Renderer[] Renderers(Transform island) => island.GetComponentsInChildren<Renderer>(true);

        private IslandThemeView View(Transform island, IslandThemeSet themes)
        {
            var view = island.gameObject.AddComponent<IslandThemeView>();
            Set(view, "islandRootName", island.name);
            Set(view, "themes", themes);
            Awake(view);
            return view;
        }

        /// <summary>
        /// Chapters the save says are owned, so <c>Current</c> is <paramref name="chapter"/>. The
        /// service is registered because the view looks it up rather than being handed it.
        /// </summary>
        private ChapterProgressionService Progression(int chapter)
        {
            var data = new SaveData();
            for (int c = 1; c <= chapter; c++) data.unlockedIslands.Add(Chapters.Namespace(c));
            var wallet = new WalletService(data.wallet);
            var chapters = new ChapterService(data, wallet, null, Chapters.Tuning.Default);
            var progression = new ChapterProgressionService(data, chapters, null);
            ServiceLocator.Register(progression);
            return progression;
        }

        /// <summary>
        /// Raises <c>Advanced</c> without playing a whole chapter to earn it. Reached through the
        /// event's backing field on purpose: what is under test is that the view is SUBSCRIBED.
        /// </summary>
        private static void Advance(ChapterProgressionService progression, int chapter)
        {
            FieldInfo field = typeof(ChapterProgressionService)
                .GetField("Advanced", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Advanced is no longer a field-like event");
            var handler = (Action<int>)field.GetValue(progression);
            Assert.That(handler, Is.Not.Null, "the view did not subscribe");
            handler(chapter);
        }

        private IslandThemeDefinition.MaterialSwap Swap(Material source, Material replacement)
            => new IslandThemeDefinition.MaterialSwap { source = source, replacement = replacement };

        private IslandThemeDefinition Theme(params IslandThemeDefinition.MaterialSwap[] swaps)
            => Theme(swaps, 0);

        private IslandThemeDefinition Theme(IslandThemeDefinition.MaterialSwap swap, int fromChapter)
            => Theme(new[] { swap }, fromChapter);

        private IslandThemeDefinition Theme(IslandThemeDefinition.MaterialSwap[] swaps, int fromChapter)
        {
            var theme = ScriptableObject.CreateInstance<IslandThemeDefinition>();
            _junk.Add(theme);
            Set(theme, "swaps", swaps);
            Set(theme, "fromChapter", fromChapter);
            return theme;
        }

        private IslandThemeSet Set(params IslandThemeDefinition[] themes)
        {
            var set = ScriptableObject.CreateInstance<IslandThemeSet>();
            _junk.Add(set);
            Set(set, "themes", themes);
            return set;
        }

        private Material Mat(string name)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Unlit/Color");
            Assert.That(shader, Is.Not.Null, "no shader to build a test material on");
            var material = new Material(shader) { name = name };
            _junk.Add(material);
            return material;
        }

        private static void Set(UnityEngine.Object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(info, Is.Not.Null, "missing serialized field: " + field);
            info.SetValue(target, value);
        }

        private static void Awake(IslandThemeView view)
            => typeof(IslandThemeView).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                                      .Invoke(view, null);
    }
}
