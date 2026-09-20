using System;
using System.Collections.Generic;
using Game.Core;
using Game.Data;

namespace Game.Systems
{
    /// <summary>
    /// Content/query adapter over ChapterService, not another progression ledger.
    /// Landfall remains the arrival reward; <see cref="Stages"/> defines the fixed 8x4 business-stage
    /// catalogue and beats 1..4 complete stages 1..4. Only ChapterService claims rewards and
    /// ChapterProgressionService opens the next chapter.
    /// </summary>
    public sealed class StageService
    {
        private readonly ChapterService _chapters;
        private readonly Dictionary<string, StageDefinition> _stages = new Dictionary<string, StageDefinition>();

        /// <summary>
        /// The progression-only catalogue used by the live game. Recipe/workstation definitions are
        /// authored separately and can be attached later without changing any stage coordinate, reward
        /// or saved player progress.
        /// </summary>
        public StageService(ChapterService chapters)
        {
            _chapters = chapters ?? throw new ArgumentNullException(nameof(chapters));
        }

        public StageService(ChapterService chapters, StageDefinition[] definitions, string[] mapAnchors)
            : this(chapters)
        {
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
            => definition == null ? string.Empty : Stages.Label(Chapters.Of(definition.IslandId), definition.CompletionBeat);

        /// <summary>Stable ID for one of the 32 approved chapter-stage coordinates.</summary>
        public static string Id(int chapter, int stage) => Stages.Id(chapter, stage);

        /// <summary>Player-facing coordinate for one of the 32 approved stages.</summary>
        public static string Label(int chapter, int stage) => Stages.Label(chapter, stage);

        /// <summary>
        /// The stage the player is currently playing in one owned chapter. A stage advances as soon
        /// as every earlier beat is observed as earned; it does not wait for the player to collect a
        /// reward. The final stage deliberately remains current after its own completion, because
        /// opening the next chapter is still the player's explicit ChapterProgressionService action.
        /// </summary>
        public int CurrentStage(int chapter)
        {
            if (!_chapters.Owned(chapter)) return 0;

            int current = 1;
            for (int stage = 2; stage <= Stages.PerChapter; stage++)
                if (IsUnlocked(chapter, stage)) current = stage;
                else break;
            return current;
        }

        /// <summary>The player-facing stage for the chapter currently being played.</summary>
        public string CurrentLabel()
        {
            int chapter = _chapters.Current;
            return Label(chapter, CurrentStage(chapter));
        }

        /// <summary>Whether a numbered stage is available under the current observed chapter progress.</summary>
        public bool IsUnlocked(int chapter, int stage)
        {
            if (!Stages.IsValid(chapter, stage) || !_chapters.Owned(chapter)) return false;
            for (int beat = 1; beat < stage; beat++)
                if (!_chapters.Satisfied(chapter, beat) && !_chapters.Claimed(chapter, beat)) return false;
            return true;
        }

        /// <summary>Whether a numbered stage's existing completion beat has been earned or claimed.</summary>
        public bool IsComplete(int chapter, int stage)
        {
            if (!IsUnlocked(chapter, stage)) return false;
            int beat = Stages.CompletionBeat(stage);
            return _chapters.Satisfied(chapter, beat) || _chapters.Claimed(chapter, beat);
        }

        public bool IsUnlocked(string id)
        {
            if (!Stages.TryCoordinate(id, out int chapter, out int number)) return false;
            return IsUnlocked(chapter, number);
        }

        public bool IsComplete(string id)
        {
            if (!Stages.TryCoordinate(id, out int chapter, out int number)) return false;
            return IsComplete(chapter, number);
        }

        private static void Validate(StageDefinition stage, HashSet<string> anchors,
                                     Dictionary<string, ResourceDef> ids, Dictionary<ResourceDef, string> assets)
        {
            if (stage == null || string.IsNullOrWhiteSpace(stage.StageId) || Chapters.Of(stage.IslandId) < 0)
                throw new ArgumentException("Stage requires a stable ID and an existing island ID.");
            if (stage.CompletionBeat < 1 || stage.CompletionBeat >= Chapters.BeatCount)
                throw new ArgumentException("Landfall is an arrival reward, not a business stage.");
            int chapter = Chapters.Of(stage.IslandId);
            if (stage.StageId != Stages.Id(chapter, stage.CompletionBeat))
                throw new ArgumentException("Stage ID must match its fixed chapter-stage coordinate.");
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
