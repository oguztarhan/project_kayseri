using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Game.Core;
using Game.Data;

namespace Game.Tests
{
    /// <summary>
    /// The chapter-to-theme rule, and the asset that feeds it.
    ///
    /// Everything here is about WHICH theme a chapter wears. What a theme does to the island's
    /// materials is the applier's job and is tested with it.
    /// </summary>
    public class IslandThemeTests
    {
        private readonly List<Object> _assets = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _assets.Count; i++)
                if (_assets[i] != null) Object.DestroyImmediate(_assets[i]);
            _assets.Clear();
        }

        // ------------------------------------------------------------------ the rule
        [Test]
        public void AnIslandWithNoThemeTableKeepsItsOwnMaterials()
        {
            Assert.That(IslandThemes.IndexFor(0, null), Is.EqualTo(IslandThemes.None));
            Assert.That(IslandThemes.IndexFor(0, new int[0]), Is.EqualTo(IslandThemes.None));
        }

        [Test]
        public void EightThemesOpeningInOrderGiveEachChapterItsOwn()
        {
            var opens = new[] { 0, 1, 2, 3, 4, 5, 6, 7 };
            for (int chapter = 0; chapter < 8; chapter++)
                Assert.That(IslandThemes.IndexFor(chapter, opens), Is.EqualTo(chapter),
                            "chapter " + chapter);
        }

        /// <summary>The grouping that was asked for first: 1-2, 3-5, 6-8 as three themes.</summary>
        [Test]
        public void AThemeHoldsUntilTheNextOneOpens()
        {
            var opens = new[] { 0, 2, 5 };
            var expected = new[] { 0, 0, 1, 1, 1, 2, 2, 2 };
            for (int chapter = 0; chapter < expected.Length; chapter++)
                Assert.That(IslandThemes.IndexFor(chapter, opens), Is.EqualTo(expected[chapter]),
                            "chapter " + chapter);
        }

        [Test]
        public void TheLastThemeCoversEveryChapterAfterIt()
        {
            var opens = new[] { 0, 3 };
            Assert.That(IslandThemes.IndexFor(7, opens), Is.EqualTo(1));
            Assert.That(IslandThemes.IndexFor(99, opens), Is.EqualTo(1));
        }

        [Test]
        public void AChapterBeforeTheFirstThemeOpensHasNone()
        {
            Assert.That(IslandThemes.IndexFor(0, new[] { 2, 5 }), Is.EqualTo(IslandThemes.None));
            Assert.That(IslandThemes.IndexFor(1, new[] { 2, 5 }), Is.EqualTo(IslandThemes.None));
            Assert.That(IslandThemes.IndexFor(2, new[] { 2, 5 }), Is.EqualTo(0));
        }

        [Test]
        public void ANegativeChapterHasNoTheme()
            => Assert.That(IslandThemes.IndexFor(-1, new[] { 0 }), Is.EqualTo(IslandThemes.None));

        /// <summary>
        /// A hole in a half-authored set costs that one theme and nothing else. Reading the hole as
        /// chapter 0 would hand every early chapter a theme nobody authored for it.
        /// </summary>
        [Test]
        public void AnEmptySlotIsSkippedRatherThanTreatedAsChapterZero()
        {
            var opens = new[] { 0, IslandThemes.None, 4 };
            Assert.That(IslandThemes.IndexFor(0, opens), Is.EqualTo(0));
            Assert.That(IslandThemes.IndexFor(3, opens), Is.EqualTo(0));
            Assert.That(IslandThemes.IndexFor(4, opens), Is.EqualTo(2));
        }

        [Test]
        public void ThemesDoNotHaveToBeAuthoredInOrder()
        {
            var opens = new[] { 5, 0, 2 };
            Assert.That(IslandThemes.IndexFor(0, opens), Is.EqualTo(1));
            Assert.That(IslandThemes.IndexFor(3, opens), Is.EqualTo(2));
            Assert.That(IslandThemes.IndexFor(6, opens), Is.EqualTo(0));
        }

        [Test]
        public void TwoThemesOpeningAtTheSameChapterResolveToTheLater()
            => Assert.That(IslandThemes.IndexFor(3, new[] { 0, 3, 3 }), Is.EqualTo(2));

        // ------------------------------------------------------------------ the asset
        [Test]
        public void AFreshSetIsEmptyAndLeavesEveryChapterUnthemed()
        {
            IslandThemeSet set = Set();
            Assert.That(set.Count, Is.Zero);
            Assert.That(set.IndexForChapter(0), Is.EqualTo(IslandThemes.None));
            Assert.That(set.ForChapter(0), Is.Null);
        }

        [Test]
        public void TheSetResolvesAChapterToItsAuthoredTheme()
        {
            IslandThemeDefinition first = Theme("industrial", 0);
            IslandThemeDefinition second = Theme("winter", 3);
            IslandThemeSet set = Set(first, second);

            Assert.That(set.ForChapter(0), Is.SameAs(first));
            Assert.That(set.ForChapter(2), Is.SameAs(first));
            Assert.That(set.ForChapter(3), Is.SameAs(second));
            Assert.That(set.ForChapter(7), Is.SameAs(second));
        }

        /// <summary>
        /// The set is allowed to be shorter than the chapter ladder while the art is being made: the
        /// chapters past the end keep the last authored look rather than failing.
        /// </summary>
        [Test]
        public void ASetShorterThanTheChapterLadderStillAnswersEveryChapter()
        {
            IslandThemeSet set = Set(Theme("industrial", 0));
            for (int chapter = 0; chapter < Chapters.Count; chapter++)
                Assert.That(set.IndexForChapter(chapter), Is.EqualTo(0), "chapter " + chapter);
        }

        [Test]
        public void AnUnfilledSlotInTheSetIsSkippedRatherThanThrowing()
        {
            IslandThemeDefinition authored = Theme("industrial", 0);
            IslandThemeSet set = Set(null, authored);

            Assert.That(set.Count, Is.EqualTo(2));
            Assert.That(set.ForChapter(0), Is.SameAs(authored));
            Assert.That(set.At(0), Is.Null);
            Assert.That(set.At(9), Is.Null);
        }

        /// <summary>
        /// Editing a theme's opening chapter takes effect without the set being touched — there is no
        /// cached answer in the set to go stale.
        /// </summary>
        [Test]
        public void MovingAThemesOpeningChapterTakesEffectImmediately()
        {
            IslandThemeDefinition theme = Theme("winter", 5);
            IslandThemeSet set = Set(theme);
            Assert.That(set.ForChapter(2), Is.Null);

            Field(theme, "fromChapter").SetValue(theme, 2);

            Assert.That(set.ForChapter(2), Is.SameAs(theme));
        }

        [Test]
        public void ASwapWithNoReplacementIsHowAThemeKeepsTheOriginalMaterial()
        {
            IslandThemeDefinition theme = Theme("winter", 0);
            Field(theme, "swaps").SetValue(theme, new[] { new IslandThemeDefinition.MaterialSwap() });

            Assert.That(theme.SwapCount, Is.EqualTo(1));
            Assert.That(theme.SwapAt(0).source, Is.Null);
            Assert.That(theme.SwapAt(0).replacement, Is.Null);
        }

        // ------------------------------------------------------------------ helpers
        private IslandThemeDefinition Theme(string id, int fromChapter)
        {
            var theme = ScriptableObject.CreateInstance<IslandThemeDefinition>();
            _assets.Add(theme);
            Field(theme, "themeId").SetValue(theme, id);
            Field(theme, "fromChapter").SetValue(theme, fromChapter);
            return theme;
        }

        private IslandThemeSet Set(params IslandThemeDefinition[] themes)
        {
            var set = ScriptableObject.CreateInstance<IslandThemeSet>();
            _assets.Add(set);
            if (themes != null && themes.Length > 0) Field(set, "themes").SetValue(set, themes);
            return set;
        }

        private static FieldInfo Field(Object asset, string name)
        {
            FieldInfo field = asset.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "missing serialized field: " + name);
            return field;
        }
    }
}
