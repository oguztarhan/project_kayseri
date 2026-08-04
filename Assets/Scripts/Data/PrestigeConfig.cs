using UnityEngine;

namespace Game.Data
{
    /// <summary>Prestige tuning (GDD §8). Designer-editable.</summary>
    [CreateAssetMenu(fileName = "PrestigeConfig", menuName = "Ore Empire/Prestige Config", order = 11)]
    public sealed class PrestigeConfig : ScriptableObject
    {
        [Header("Payout")]
        [Tooltip("Investors for cashing in exactly one tier's worth of lifetime cash. " +
                 "With bonusPerInvestor at 0.10 this makes a well-timed first reset worth ×2.")]
        [SerializeField] private double investorK = 10d;
        [SerializeField] private double bonusPerInvestor = 0.10d;   // +10% global income per investor

        [Header("Scale")]
        [Tooltip("Lifetime cash a full run on the FIRST island is worth — the yardstick every " +
                 "payout is measured against. Matches Copper's unlock price.")]
        [SerializeField] private double referenceLifetime = 1.1e6d;
        [Tooltip("How much that yardstick grows per island owned. Must match the ladder's " +
                 "tier step (WorldIslands.TierStep) or prestige drifts against the economy.")]
        [SerializeField] private double tierStep = 3.2d;

        [Header("Gate")]
        [Tooltip("Islands the player must own before prestige unlocks. The old gate was 1,000 " +
                 "lifetime cash — about two minutes of play.")]
        [SerializeField] private int minIslandsOwned = 3;
        [Tooltip("Fraction of a tier's run that must be banked before the button lights up.")]
        [SerializeField, Range(0.1f, 2f)] private float readyFraction = 0.5f;

        public double InvestorK => investorK;
        public double BonusPerInvestor => bonusPerInvestor;
        public double ReferenceLifetime => referenceLifetime;
        public double TierStep => tierStep;
        public int MinIslandsOwned => minIslandsOwned;
        public double ReadyFraction => readyFraction;
    }
}
