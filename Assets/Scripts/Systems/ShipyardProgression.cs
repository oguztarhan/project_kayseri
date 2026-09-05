using System;
using System.Collections.Generic;

namespace Game.Systems
{
    /// <summary>Additive save payload; never remaps legacy equipment slots or island progress.</summary>
    [Serializable]
    public sealed class ShipyardProgression
    {
        public List<string> unlockedStations = new List<string>();
        public int completedOrders;

        public static readonly string[] StationIds =
        {
            "Station_Cannon", "Station_Hull", "Station_Rigging",
            "Station_Navigation", "Station_Figurehead"
        };

        public void Normalize()
        {
            if (unlockedStations == null) unlockedStations = new List<string>();
            if (!unlockedStations.Contains(StationIds[0])) unlockedStations.Add(StationIds[0]);
            completedOrders = Math.Max(0, completedOrders);
        }

        public bool IsUnlocked(string id)
        {
            return id == StationIds[0] || (unlockedStations != null && unlockedStations.Contains(id));
        }

        public string NextStation
        {
            get
            {
                foreach (var id in StationIds) if (!IsUnlocked(id)) return id;
                return null;
            }
        }

        // This is the commit boundary, not a shop purchase. The future order/economy service
        // must validate its costs/milestone before calling it. Missing art can never be bought.
        public bool TryUnlockNext(string id, bool milestoneSatisfied, bool artReady)
        {
            Normalize();
            if (!milestoneSatisfied || !artReady || id == null || id != NextStation) return false;
            unlockedStations.Add(id);
            return true;
        }
    }
}
