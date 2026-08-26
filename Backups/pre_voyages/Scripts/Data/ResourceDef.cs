using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Base for anything that flows through the chain and has a sell value — raw ore tiers and
    /// refined products (GDD §4). Shared serialized fields live here so ore and product assets are
    /// interchangeable as recipe ingredients and inventory keys.
    /// </summary>
    public abstract class ResourceDef : ScriptableObject
    {
        [SerializeField] private string displayName = "Resource";
        [SerializeField] private int tierIndex = 0;
        [SerializeField] private Color color = Color.gray;
        [SerializeField] private double baseValue = 1d;

        public string DisplayName => displayName;
        public int TierIndex => tierIndex;
        public Color Color => color;
        public double BaseValue => baseValue;
    }
}
