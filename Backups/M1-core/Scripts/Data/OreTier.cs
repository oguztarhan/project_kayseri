using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// One ore rarity tier (Coal → Diamond). Designer-editable; the mesh/colour swap here drives
    /// the recolour-per-tier art strategy (GDD §4, §13). M1 ships Coal only.
    /// </summary>
    [CreateAssetMenu(fileName = "OreTier", menuName = "Ore Empire/Ore Tier", order = 1)]
    public sealed class OreTier : ScriptableObject
    {
        [SerializeField] private string displayName = "Coal";
        [SerializeField] private int tierIndex = 0;
        [SerializeField] private Color color = new Color(0.17f, 0.18f, 0.21f);
        [SerializeField] private double baseValue = 1d;

        public string DisplayName => displayName;
        public int TierIndex => tierIndex;
        public Color Color => color;
        public double BaseValue => baseValue;
    }
}
