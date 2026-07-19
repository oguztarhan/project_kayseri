using UnityEngine;

namespace Game.Data
{
    /// <summary>Daily-reward tuning (GDD §11): gems granted once per calendar day.</summary>
    [CreateAssetMenu(fileName = "DailyRewardConfig", menuName = "Ore Empire/Daily Reward Config", order = 13)]
    public sealed class DailyRewardConfig : ScriptableObject
    {
        [SerializeField] private long rewardGems = 5;
        public long RewardGems => rewardGems;
    }
}
