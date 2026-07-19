using System;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// Root serializable save payload. Grows one section per milestone (stations, unlocks,
    /// prestige, settings...). Serialized with Unity JsonUtility, so every field is public
    /// and every nested type is [Serializable]. Bump <see cref="version"/> on schema changes
    /// so migrations can run.
    /// </summary>
    [Serializable]
    public class SaveData
    {
        public int version = 1;
        public long savedUnixSeconds;
        public WalletData wallet = new WalletData();
        public StationLevels stations = new StationLevels();
    }

    [Serializable]
    public class WalletData
    {
        public BigDouble cash;     // BigDouble is [Serializable] with public fields
        public long gems;          // premium currency
        public double investors;   // prestige currency (added to in M4)
    }

    [Serializable]
    public class StationLevels
    {
        public int mine;
        public int storage;
        public int market;
        public int train;
    }
}
