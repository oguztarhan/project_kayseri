using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// Root serializable save payload (Unity JsonUtility). Station levels and hired managers are lists
    /// keyed by station id so any number persist without a schema change.
    /// </summary>
    [Serializable]
    public class SaveData
    {
        public int version = 3;
        public long savedUnixSeconds;
        public long lastDailyClaimUnix;               // daily reward (GDD §11)
        public int dailyStreak;                       // consecutive daily claims; drives the 7-day ladder
        public double incomeRatePerSec;               // offline earnings (GDD §7)
        public WalletData wallet = new WalletData();
        public List<StationLevel> stationLevels = new List<StationLevel>();
        public List<string> hiredManagers = new List<string>();  // station ids with a manager (GDD §6)
        public List<string> unlockedMountains = new List<string>();  // mountain ids the player has bought (GDD §4/§8)
        public List<string> unlockedIslands = new List<string>();    // island ids the player has bought (archipelago progression)
        public List<StationLevel> islandLevels = new List<StationLevel>();  // per-island upgrade level (archipelago progression)
        public int freeRewardDay;                    // UTC day number the free-reward charges were last reset on
        public List<FreeRewardState> freeRewards = new List<FreeRewardState>();  // rewarded-ad slots (GDD §10)
        public bool adsRemoved;                      // the remove-ads purchase, so it survives a restart
    }

    /// <summary>One rewarded-ad slot's daily state: how many of today's charges are spent, and when.</summary>
    [Serializable]
    public class FreeRewardState
    {
        public string id;
        public int used;
        public long lastWatchUnix;
    }

    [Serializable]
    public class WalletData
    {
        public BigDouble cash;
        public long gems;
        public double investors;         // prestige currency (GDD §8)
        public BigDouble lifetimeCash;   // total cash earned this run, for prestige payout
    }

    [Serializable]
    public class StationLevel
    {
        public string id;
        public int level;
    }
}
