using UnityEngine;

namespace Game.Data
{
    /// <summary>Contract/order tuning (GDD §9): sell N product units within a time limit for a reward.</summary>
    [CreateAssetMenu(fileName = "ContractConfig", menuName = "Ore Empire/Contract Config", order = 12)]
    public sealed class ContractConfig : ScriptableObject
    {
        [SerializeField] private double targetUnits = 100d;
        [SerializeField] private float timeLimitSeconds = 60f;
        [SerializeField] private double rewardCash = 500d;
        [SerializeField] private long rewardGems = 2;

        public double TargetUnits => targetUnits;
        public float TimeLimitSeconds => timeLimitSeconds;
        public double RewardCash => rewardCash;
        public long RewardGems => rewardGems;
    }
}
