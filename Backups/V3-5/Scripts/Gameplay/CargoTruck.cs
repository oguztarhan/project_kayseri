using Game.Core;
using Game.Data;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>Hauls finished products from the refinery's output to the market (GDD §3).</summary>
    public sealed class CargoTruck : Hauler
    {
        [SerializeField] private Refinery refinery;
        [SerializeField] private Market market;

        protected override Inventory<ResourceDef> Source => refinery != null ? refinery.Output : null;
        protected override Inventory<ResourceDef> Dest => market != null ? market.Products : null;
    }
}
