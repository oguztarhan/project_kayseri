using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// A refined product (Coke, Steel Beam, Gold Bar, ...) produced by the refinery and sold at the
    /// market for far more than its raw ore (GDD §4). Fields come from <see cref="ResourceDef"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "Product", menuName = "Ore Empire/Product", order = 3)]
    public sealed class Product : ResourceDef
    {
    }
}
