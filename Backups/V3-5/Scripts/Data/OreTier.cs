using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// A raw ore rarity tier (Coal → Diamond). Fields come from <see cref="ResourceDef"/>.
    /// The mesh/colour swap drives the recolour-per-tier art strategy (GDD §4, §13).
    /// </summary>
    [CreateAssetMenu(fileName = "OreTier", menuName = "Ore Empire/Ore Tier", order = 1)]
    public sealed class OreTier : ResourceDef
    {
    }
}
