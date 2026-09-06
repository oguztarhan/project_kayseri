using System;
using UnityEngine;

namespace Game.Data
{
    /// <summary>Content attached to an existing chapter beat; owns no progress or rewards.</summary>
    [CreateAssetMenu(fileName = "Stage", menuName = "Ore Empire/Stage")]
    public sealed class StageDefinition : ScriptableObject
    {
        [Serializable]
        public struct ResourceBinding
        {
            public string id;
            public ResourceDef resource;
            public bool extracted; // Available from this island's mine, without a recipe.
        }

        [Serializable]
        public struct Workstation
        {
            public string anchorId;
            public Recipe recipe;
        }

        [SerializeField] private string stageId;
        [SerializeField] private string islandId;
        [SerializeField] private int completionBeat = 1;
        [SerializeField] private ResourceBinding[] resources = new ResourceBinding[0];
        [SerializeField] private Workstation[] workstations = new Workstation[0];

        public string StageId => stageId;
        public string IslandId => islandId;
        public int CompletionBeat => completionBeat;
        public int ResourceCount => resources == null ? 0 : resources.Length;
        public int WorkstationCount => workstations == null ? 0 : workstations.Length;
        public ResourceBinding ResourceAt(int index) => resources[index];
        public Workstation WorkstationAt(int index) => workstations[index];
    }
}
