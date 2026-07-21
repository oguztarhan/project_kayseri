using Game.Core;
using Game.Systems;

namespace Game.Gameplay
{
    /// <summary>
    /// Anything the player levels up. Each station exposes 2–3 independent upgrade <b>tracks</b> (e.g. the
    /// train's Wagons / Speed / Capacity), so the HUD can show exactly what a purchase improves and each
    /// axis is bought on its own (GDD §3 upgrade axes, §5).
    /// </summary>
    public interface IUpgradable
    {
        string Label { get; }
        int TrackCount { get; }
        string TrackName(int track);
        int TrackLevel(int track);
        BigDouble TrackCost(int track, EconomyService economy);
        void SetTrackLevel(int track, int level);
    }
}
