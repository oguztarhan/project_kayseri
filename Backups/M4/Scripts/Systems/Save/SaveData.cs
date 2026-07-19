using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// Root serializable save payload (Unity JsonUtility). Station upgrade levels and hired managers
    /// are stored as lists keyed by station id so any number persist without a schema change.
    /// </summary>
    [Serializable]
    public class SaveData
    {
        public int version = 3;
        public long savedUnixSeconds;
        public double incomeRatePerSec;               // for offline earnings (GDD §7)
        public WalletData wallet = new WalletData();
        public List<StationLevel> stationLevels = new List<StationLevel>();
        public List<string> hiredManagers = new List<string>();  // station ids with a manager (GDD §6)
    }

    [Serializable]
    public class WalletData
    {
        public BigDouble cash;
        public long gems;
        public double investors;         // prestige currency (GDD §8)
        public BigDouble lifetimeCash;   // total cash ever earned this run, for prestige payout
    }

    [Serializable]
    public class StationLevel
    {
        public string id;
        public int level;
    }
}
