using UnityEngine;

namespace Game.Data
{
    /// <summary>Prestige tuning (GDD §8). Designer-editable.</summary>
    [CreateAssetMenu(fileName = "PrestigeConfig", menuName = "Ore Empire/Prestige Config", order = 11)]
    public sealed class PrestigeConfig : ScriptableObject
    {
        [SerializeField] private double investorK = 1d;
        [SerializeField] private double bonusPerInvestor = 0.02d;   // +2% global income per investor
        [SerializeField] private double lifetimeCashThreshold = 1000d;

        public double InvestorK => investorK;
        public double BonusPerInvestor => bonusPerInvestor;
        public double Threshold => lifetimeCashThreshold;
    }
}
