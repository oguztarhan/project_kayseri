using System.Collections.Generic;
using System.Reflection;
using Game.Core;
using Game.Data;
using Game.Gameplay;
using Game.Systems;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// The integration boundary between the numbered-stage projection and the existing island-theme
    /// view. Objectives must change only the stage label; the island changes only when the player
    /// confirms the completed chapter and ChapterProgressionService opens the next namespace.
    /// </summary>
    public sealed class StageThemeBoundaryTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            _created.Clear();
            ServiceLocator.Clear();
        }

        [Test]
        public void StagesKeepTheirChaptersThemeUntilThePlayerConfirmsTheNextChapter()
        {
            Material grass = CreateMaterial("grass");
            Material chapterOne = CreateMaterial("chapter-one");
            Material chapterTwo = CreateMaterial("chapter-two");
            IslandThemeSet themes = ThemeSet(Theme(grass, chapterOne, 0), Theme(grass, chapterTwo, 1));

            var data = new SaveData();
            var chapters = new ChapterService(data, new WalletService(data.wallet), null, Chapters.Tuning.Default);
            var progression = new ChapterProgressionService(data, chapters, null);
            var stages = new StageService(chapters);
            ServiceLocator.Register(progression);

            Renderer renderer;
            IslandThemeView view = View(themes, grass, out renderer);
            Assert.That(stages.CurrentLabel(), Is.EqualTo("1-1"));
            Assert.That(renderer.sharedMaterial, Is.SameAs(chapterOne));

            data.islandLevels.Add(new StationLevel
            {
                id = "coal#0#0",
                level = Chapters.Tuning.Default.FirstSmokeLevels,
            });
            Assert.That(stages.CurrentLabel(), Is.EqualTo("1-2"));
            Assert.That(renderer.sharedMaterial, Is.SameAs(chapterOne));

            for (int unlock = 0; unlock < Chapters.Tuning.Default.WorksUnlocks; unlock++)
                data.islandLevels.Add(new StationLevel { id = "coalu#" + unlock, level = 1 });
            Assert.That(stages.CurrentLabel(), Is.EqualTo("1-3"));
            Assert.That(renderer.sharedMaterial, Is.SameAs(chapterOne));

            data.idleMarketYards.Add(new IdleMarketYard
            {
                schemaVersion = IdleMarketMigration.SchemaVersion,
                id = "coal",
                hireCarry = MarketFlow.MaxHireLevel,
                hireServe = MarketFlow.MaxHireLevel,
                dispatchLevel = MarketFlow.MaxHireLevel,
            });
            Assert.That(stages.CurrentLabel(), Is.EqualTo("1-4"));
            Assert.That(renderer.sharedMaterial, Is.SameAs(chapterOne));

            data.islandLevels[0].level = Chapters.Tuning.Default.FullSteamLevels;
            for (int unlock = Chapters.Tuning.Default.WorksUnlocks;
                 unlock < Chapters.Tuning.Default.FullSteamUnlocks; unlock++)
                data.islandLevels.Add(new StationLevel { id = "coalu#" + unlock, level = 1 });

            Assert.That(chapters.Complete(0), Is.True);
            Assert.That(stages.CurrentLabel(), Is.EqualTo("1-4"));
            Assert.That(renderer.sharedMaterial, Is.SameAs(chapterOne),
                        "a finished chapter is not yet permission to repaint the island");

            Assert.That(progression.TryAdvance(), Is.True);
            Assert.That(stages.CurrentLabel(), Is.EqualTo("2-1"));
            Assert.That(view.Worn, Is.Not.Null);
            Assert.That(renderer.sharedMaterial, Is.SameAs(chapterTwo));
        }

        private IslandThemeView View(IslandThemeSet themes, Material material, out Renderer renderer)
        {
            var root = new GameObject("Island_StageThemeBoundary");
            _created.Add(root);
            var child = new GameObject("Renderer");
            child.transform.SetParent(root.transform, false);
            renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            var view = root.AddComponent<IslandThemeView>();
            Set(view, "islandRootName", root.name);
            Set(view, "themes", themes);
            Call(view, "Awake");
            return view;
        }

        private IslandThemeSet ThemeSet(params IslandThemeDefinition[] definitions)
        {
            var set = ScriptableObject.CreateInstance<IslandThemeSet>();
            _created.Add(set);
            Set(set, "themes", definitions);
            return set;
        }

        private IslandThemeDefinition Theme(Material source, Material replacement, int chapter)
        {
            var theme = ScriptableObject.CreateInstance<IslandThemeDefinition>();
            _created.Add(theme);
            Set(theme, "fromChapter", chapter);
            Set(theme, "swaps", new[]
            {
                new IslandThemeDefinition.MaterialSwap { source = source, replacement = replacement },
            });
            return theme;
        }

        private Material CreateMaterial(string name)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Unlit/Color");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader) { name = name };
            _created.Add(material);
            return material;
        }

        private static void Call(object target, string name)
            => typeof(IslandThemeView).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                                      .Invoke(target, null);

        private static void Set(Object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(info, Is.Not.Null, "missing serialized field: " + field);
            info.SetValue(target, value);
        }
    }
}
