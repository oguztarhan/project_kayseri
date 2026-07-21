using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// A mountain / biome the player can unlock to add a new raw-ore source to the shared map (GDD §4, §8).
    /// Data-driven: the ore it yields, its unlock cost, extraction rate, and biome tag are all editable here.
    /// </summary>
    [CreateAssetMenu(fileName = "Mountain", menuName = "Ore Empire/Mountain", order = 5)]
    public sealed class MountainDefinition : ScriptableObject
    {
        [SerializeField] private string displayName = "Mountain";
        [SerializeField] private OreTier ore;
        [SerializeField] private double unlockCost = 1000d;
        [SerializeField] private double baseRate = 3d;
        [SerializeField] private string biome = "Rocky";

        public string DisplayName => displayName;
        public OreTier Ore => ore;
        public double UnlockCost => unlockCost;
        public double BaseRate => baseRate;
        public string Biome => biome;
    }
}
