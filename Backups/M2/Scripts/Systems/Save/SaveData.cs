using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// Root serializable save payload (Unity JsonUtility). Station upgrade levels are stored as a
    /// list keyed by station id (GameObject name) so any number of stations persist without a schema
    /// change. Bump <see cref="version"/> on breaking changes.
    /// </summary>
    [Serializable]
    public class SaveData
    {
        public int version = 2;
        public long savedUnixSeconds;
        public WalletData wallet = new WalletData();
        public List<StationLevel> stationLevels = new List<StationLevel>();
    }

    [Serializable]
    public class WalletData
    {
        public BigDouble cash;
        public long gems;
        public double investors;
    }

    [Serializable]
    public class StationLevel
    {
        public string id;
        public int level;
    }
}
