using Game.Core;
using Game.Data;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>Hauls raw ore from storage to the refinery's input (GDD §3). The storage yard's "Loading"
    /// upgrade lets it fetch more per trip.</summary>
    public sealed class OreTruck : Hauler
    {
        [SerializeField] private StorageYard storage;
        [SerializeField] private Refinery refinery;

        protected override Inventory<ResourceDef> Source => storage != null ? storage.Ore : null;
        protected override Inventory<ResourceDef> Dest => refinery != null ? refinery.Input : null;
        protected override double LoadBonus => storage != null ? storage.LoadMultiplier : 1d;
    }
}
