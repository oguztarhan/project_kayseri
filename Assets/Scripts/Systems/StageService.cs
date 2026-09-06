using System;
using System.Collections.Generic;
using Game.Core;
using Game.Data;

namespace Game.Systems
{
    /// <summary>
    /// Content/query adapter over ChapterService, not another progression ledger.
    /// Landfall remains the arrival reward; beats 1..4 complete stages 1..4.
    /// Only ChapterService claims rewards and WorldIslands purchases the next island.
    /// </summary>
    public sealed class StageService
    {
        private readonly ChapterService _chapters;
        private readonly Dictionary<string, StageDefinition> _stages = new Dictionary<string, StageDefinition>();

        public StageService(ChapterService chapters, StageDefinition[] definitions, string[] mapAnchors)
        {
            _chapters = chapters ?? throw new ArgumentNullException(nameof(chapters));
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            var coordinates = new HashSet<string>();
            var anchors = new HashSet<string>(mapAnchors ?? new string[0]);
            var resourceIds = new Dictionary<string, ResourceDef>();
            var resourceAssets = new Dictionary<ResourceDef, string>();
            foreach (var definition in definitions)
            {
                Validate(definition, anchors, resourceIds, resourceAssets);
                if (_stages.ContainsKey(definition.StageId)) throw new ArgumentException("Duplicate stage ID.");
                if (!coordinates.Add(Label(definition))) throw new ArgumentException("Duplicate chapter-stage coordinate.");
                _stages.Add(definition.StageId, definition);
            }
            // A partial first-chapter prototype is legal; holes within authored chapters are not.
            foreach (var definition in definitions)
                for (int beat = 1; beat < definition.CompletionBeat; beat++)
                    if (!coordinates.Contains((Chapters.Of(definition.IslandId) + 1) + "-" + beat))
                        throw new ArgumentException("Stage is missing an earlier stage in its chapter.");
        }

        public StageDefinition Definition(string id) => id != null && _stages.TryGetValue(id, out var value) ? value : null;

        public static string Label(StageDefinition definition)
            => (Chapters.Of(definition.IslandId) + 1) + "-" + definition.CompletionBeat;

        public bool IsUnlocked(string id)
        {
            var stage = Definition(id);
            if (stage == null) return false;
            int chapter = Chapters.Of(stage.IslandId);
            if (!_chapters.Owned(chapter)) return false;
            for (int beat = 1; beat < stage.CompletionBeat; beat++)
                if (!_chapters.Satisfied(chapter, beat) && !_chapters.Claimed(chapter, beat)) return false;
            return true;
        }

        public bool IsComplete(string id)
        {
            var stage = Definition(id);
            if (stage == null || !IsUnlocked(id)) return false;
            int chapter = Chapters.Of(stage.IslandId);
            return _chapters.Satisfied(chapter, stage.CompletionBeat) || _chapters.Claimed(chapter, stage.CompletionBeat);
        }

        private static void Validate(StageDefinition stage, HashSet<string> anchors,
                                     Dictionary<string, ResourceDef> ids, Dictionary<ResourceDef, string> assets)
        {
            if (stage == null || string.IsNullOrWhiteSpace(stage.StageId) || Chapters.Of(stage.IslandId) < 0)
                throw new ArgumentException("Stage requires a stable ID and an existing island ID.");
            if (stage.CompletionBeat < 1 || stage.CompletionBeat >= Chapters.BeatCount)
                throw new ArgumentException("Landfall is an arrival reward, not a business stage.");
            if (stage.WorkstationCount == 0) throw new ArgumentException("Stage requires a crafting workstation.");
            var available = new HashSet<ResourceDef>();
            var local = new HashSet<ResourceDef>();
            for (int i = 0; i < stage.ResourceCount; i++)
            {
                var binding = stage.ResourceAt(i);
                if (string.IsNullOrWhiteSpace(binding.id) || binding.resource == null || !local.Add(binding.resource))
                    throw new ArgumentException("Missing or duplicate resource binding.");
                if (ids.TryGetValue(binding.id, out var oldAsset) && oldAsset != binding.resource)
                    throw new ArgumentException("Resource ID refers to different assets across stages.");
                if (assets.TryGetValue(binding.resource, out var oldId) && oldId != binding.id)
                    throw new ArgumentException("Resource asset has inconsistent IDs across stages.");
                ids[binding.id] = binding.resource;
                assets[binding.resource] = binding.id;
                if (binding.extracted) available.Add(binding.resource);
            }
            var usedAnchors = new HashSet<string>();
            for (int i = 0; i < stage.WorkstationCount; i++)
            {
                var station = stage.WorkstationAt(i);
                if (string.IsNullOrWhiteSpace(station.anchorId) || !anchors.Contains(station.anchorId) || !usedAnchors.Add(station.anchorId))
                    throw new ArgumentException("Workstation needs a unique, existing map anchor.");
                var recipe = station.recipe;
                if (recipe == null || recipe.Output == null || !local.Contains(recipe.Output) ||
                    !Positive(recipe.OutputAmount) || !Positive(recipe.RefineSeconds) || recipe.Inputs == null || recipe.Inputs.Length == 0)
                    throw new ArgumentException("Invalid or unbound recipe output.");
                foreach (var input in recipe.Inputs)
                    if (input.resource == null || !local.Contains(input.resource) || !Positive(input.amount))
                        throw new ArgumentException("Invalid or unbound recipe ingredient.");
            }
            // Reachability, rather than list order, permits intermediate products and rejects cycles
            // with no mine-supplied entry point. Runs once when a catalogue is loaded.
            for (int pass = 0; pass < stage.WorkstationCount; pass++)
                for (int i = 0; i < stage.WorkstationCount; i++)
                {
                    var recipe = stage.WorkstationAt(i).recipe;
                    bool ready = true;
                    foreach (var input in recipe.Inputs) ready &= available.Contains(input.resource);
                    if (ready) available.Add(recipe.Output);
                }
            for (int i = 0; i < stage.WorkstationCount; i++)
                foreach (var input in stage.WorkstationAt(i).recipe.Inputs)
                    if (!available.Contains(input.resource)) throw new ArgumentException("Recipe inputs cannot be produced on this stage.");
        }

        private static bool Positive(double value) => value > 0 && !double.IsInfinity(value) && !double.IsNaN(value);
    }
}
