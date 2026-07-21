using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// One ore's island in the archipelago (Coal → Diamond). The player unlocks islands one at a time; each
    /// unlocked island keeps generating cash forever (GDD §4/§8 progression). Data-driven: ore, unlock price,
    /// and its passive income rate are all editable here.
    /// </summary>
    [CreateAssetMenu(fileName = "Island", menuName = "Ore Empire/Island", order = 6)]
    public sealed class IslandDefinition : ScriptableObject
    {
        [SerializeField] private string displayName = "Island";
        [SerializeField] private OreTier ore;
        [SerializeField] private double unlockCost = 10000d;
        [SerializeField] private double baseIncomePerSec = 50d;
        [SerializeField] private int maxLevel = 10;                // upgrade the island this many times to "max" it
        [SerializeField] private double upgradeBaseCost = 5000d;   // first upgrade price (grows geometrically)
        [SerializeField] private bool starter;

        public string DisplayName => displayName;
        public OreTier Ore => ore;
        public double UnlockCost => unlockCost;
        public double BaseIncomePerSec => baseIncomePerSec;
        public int MaxLevel => maxLevel;
        public double UpgradeBaseCost => upgradeBaseCost;
        public bool Starter => starter;
    }
}
