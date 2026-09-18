using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Game.Core;
using Game.Data;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>
    /// The themes the game actually ships, read from the real assets.
    ///
    /// The rest of the theme tests build their own throwaway materials to test the RULES. These test
    /// the CONTENT: that every chapter after the first has a theme, that each theme only repaints the
    /// island rather than restructuring it, and that the things the brief put out of bounds — the lights,
    /// the sea, the coins — are left alone in every chapter.
    ///
    /// All of that is silent when it breaks. A theme with a source row pointing at the wrong material
    /// simply does nothing; a copy that picked up a different shader simply draws on its own batch. So
    /// the shape of the shipped data is asserted here rather than trusted.
    /// </summary>
    public class ChapterThemeAssetTests
    {
        private const string SetPath = "Assets/Data/IslandThemeSet.asset";
        private const string IslandMaterials = "Assets/Art/IndustrialReference/Materials/";
        private const string NeutralMeadow = "Assets/Art/IndustrialReference/Themes/_Shared/Meadow_Neutral.png";

        /// <summary>The chapters in order. Chapter one is the island as authored.</summary>
        private static readonly string[] Expected =
        {
            null, "ch2_sunburnt", "ch3_slate", "ch4_frost", "ch5_harvest", "ch6_crimson", "ch7_jungle", "ch8_crystal",
        };

        /// <summary>Emissive materials read as light, and lighting is out of scope for a theme.</summary>
        private static readonly string[] Emissive = { "foam", "goldGlow", "greenUI", "lightCore", "lamp_glow" };

        /// <summary>The sea stays the sea; the road material is shared with the coin rims, which stay gold.</summary>
        private static readonly string[] OutOfBounds = { "Ocean", "road" };

        private static IslandThemeSet Set()
        {
            var set = AssetDatabase.LoadAssetAtPath<IslandThemeSet>(SetPath);
            Assert.That(set, Is.Not.Null, SetPath + " is missing");
            return set;
        }

        private static IEnumerable<IslandThemeDefinition> Themes()
        {
            IslandThemeSet set = Set();
            for (int i = 0; i < set.Count; i++) yield return set.At(i);
        }

        // ------------------------------------------------------------------ the ladder
        [Test]
        public void EveryChapterAfterTheFirstWearsItsOwnTheme()
        {
            IslandThemeSet set = Set();

            Assert.That(set.ForChapter(0), Is.Null, "chapter one is the island as it was authored");
            for (int chapter = 1; chapter < Chapters.Count; chapter++)
            {
                IslandThemeDefinition theme = set.ForChapter(chapter);
                Assert.That(theme, Is.Not.Null, "chapter " + (chapter + 1) + " has no theme");
                Assert.That(theme.ThemeId, Is.EqualTo(Expected[chapter]), "chapter " + (chapter + 1));
                Assert.That(theme.FromChapter, Is.EqualTo(chapter), "chapter " + (chapter + 1) + " is wearing a theme it inherited");
            }
        }

        /// <summary>
        /// Two themes opening at one chapter have no right answer — the later one wins and the other is
        /// dead weight that only looks live in the Inspector.
        /// </summary>
        [Test]
        public void ExactlyOneThemeOpensAtEachChapter()
        {
            var opens = new HashSet<int>();
            foreach (IslandThemeDefinition theme in Themes())
            {
                Assert.That(theme, Is.Not.Null, "the set has an empty slot");
                Assert.That(opens.Add(theme.FromChapter), Is.True, "two themes open at chapter " + (theme.FromChapter + 1));
            }
            Assert.That(opens.Count, Is.EqualTo(Chapters.Count - 1));
        }

        /// <summary>
        /// Walks a save through all eight chapters the way the player does — finish, advance — and checks
        /// the island is dressed for the chapter the save says it is on at every step.
        /// </summary>
        [Test]
        public void WalkingTheChaptersInOrderWearsEachChaptersTheme()
        {
            IslandThemeSet set = Set();
            var data = new SaveData();
            var chapters = new ChapterService(data, new WalletService(data.wallet), null, Chapters.Tuning.Default);
            var progression = new ChapterProgressionService(data, chapters, null);

            for (int step = 0; step < Chapters.Count; step++)
            {
                Assert.That(progression.Current, Is.EqualTo(step));
                IslandThemeDefinition worn = set.ForChapter(progression.Current);
                Assert.That(worn == null ? null : worn.ThemeId, Is.EqualTo(Expected[step]), "chapter " + (step + 1));

                if (step == Chapters.Count - 1) break;
                Finish(data, step);
                Assert.That(progression.TryAdvance(), Is.True, "could not leave chapter " + (step + 1));
            }
        }

        // ------------------------------------------------------------------ paint, not structure
        [Test]
        public void EveryRowSwapsAnIslandMaterialForItsOwnThemesCopy()
        {
            foreach (IslandThemeDefinition theme in Themes())
            {
                string folder = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(theme)).Replace('\\', '/') + "/";
                Assert.That(theme.SwapCount, Is.GreaterThan(0), theme.ThemeId + " changes nothing");

                for (int i = 0; i < theme.SwapCount; i++)
                {
                    IslandThemeDefinition.MaterialSwap row = theme.SwapAt(i);
                    string where = theme.ThemeId + " row " + i;
                    Assert.That(row.source, Is.Not.Null, where);
                    Assert.That(row.replacement, Is.Not.Null, where);
                    Assert.That(AssetDatabase.GetAssetPath(row.source), Does.StartWith(IslandMaterials),
                                where + ": keyed on something a freshly loaded island never has");
                    Assert.That(AssetDatabase.GetAssetPath(row.replacement), Does.StartWith(folder),
                                where + ": the replacement lives outside its own theme");
                    Assert.That(row.replacement.name, Is.EqualTo(row.source.name), where);
                }
            }
        }

        /// <summary>
        /// A copy on a different shader would still draw — on its own batch. Keeping every replacement on
        /// the island's shader, with the same surface settings, is what keeps a chapter change free.
        /// </summary>
        [Test]
        public void AThemeChangesColourAndNeverTheSurface()
        {
            foreach (IslandThemeDefinition theme in Themes())
                for (int i = 0; i < theme.SwapCount; i++)
                {
                    Material from = theme.SwapAt(i).source, to = theme.SwapAt(i).replacement;
                    string where = theme.ThemeId + "/" + from.name;
                    Assert.That(to.shader, Is.SameAs(from.shader), where);
                    Assert.That(to.GetFloat("_Metallic"), Is.EqualTo(from.GetFloat("_Metallic")), where);
                    Assert.That(to.GetFloat("_Smoothness"), Is.EqualTo(from.GetFloat("_Smoothness")), where);
                    Assert.That(to.IsKeywordEnabled("_EMISSION"), Is.EqualTo(from.IsKeywordEnabled("_EMISSION")), where);
                }
        }

        [Test]
        public void NoThemeTouchesTheLightsTheSeaOrTheCoins()
        {
            foreach (IslandThemeDefinition theme in Themes())
                for (int i = 0; i < theme.SwapCount; i++)
                {
                    Material source = theme.SwapAt(i).source;
                    Assert.That(System.Array.IndexOf(Emissive, source.name), Is.LessThan(0), theme.ThemeId + " repaints a light: " + source.name);
                    Assert.That(source.IsKeywordEnabled("_EMISSION"), Is.False, theme.ThemeId + " repaints an emissive material: " + source.name);
                    Assert.That(System.Array.IndexOf(OutOfBounds, source.name), Is.LessThan(0), theme.ThemeId + " repaints " + source.name);
                }
        }

        /// <summary>
        /// The only texture work in the whole set: one grey meadow, shared, so each chapter's ground
        /// colour can BE the colour. A per-chapter texture here would be seven copies of one image.
        /// </summary>
        [Test]
        public void EveryChaptersGroundSharesTheOneNeutralMeadow()
        {
            var meadow = AssetDatabase.LoadAssetAtPath<Texture2D>(NeutralMeadow);
            Assert.That(meadow, Is.Not.Null, NeutralMeadow + " is missing");

            foreach (IslandThemeDefinition theme in Themes())
            {
                Material grass = null;
                for (int i = 0; i < theme.SwapCount; i++)
                    if (theme.SwapAt(i).source.name == "grass") grass = theme.SwapAt(i).replacement;

                Assert.That(grass, Is.Not.Null, theme.ThemeId + " leaves the island's main ground unthemed");
                Assert.That(grass.GetTexture("_BaseMap"), Is.SameAs(meadow), theme.ThemeId);
            }
        }

        // ------------------------------------------------------------------ helpers
        /// <summary>Builds a chapter out to all five beats, in its own namespace.</summary>
        private static void Finish(SaveData data, int chapter)
        {
            string ns = Chapters.Namespace(chapter);
            Chapters.Tuning t = Chapters.TuningFor(chapter, Chapters.Tuning.Default);
            if (chapter > 0 && !data.unlockedIslands.Contains(ns)) data.unlockedIslands.Add(ns);

            data.islandLevels.Add(new StationLevel { id = ns + "#0#0", level = t.FullSteamLevels });
            for (int u = 0; u < t.FullSteamUnlocks; u++)
                data.islandLevels.Add(new StationLevel { id = ns + "u#" + u, level = 1 });

            data.idleMarketYards.Add(new IdleMarketYard
            {
                schemaVersion = IdleMarketMigration.SchemaVersion,
                id = ns,
                hireCarry = MarketFlow.MaxHireLevel,
                hireServe = MarketFlow.MaxHireLevel,
                dispatchLevel = MarketFlow.MaxHireLevel,
            });
        }
    }
}
