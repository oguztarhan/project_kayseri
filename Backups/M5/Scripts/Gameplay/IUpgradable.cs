using Game.Core;
using Game.Systems;

namespace Game.Gameplay
{
    /// <summary>
    /// Anything the player can level up (mine, train, storage, market). The HUD's upgrade button
    /// works against this contract, so every station upgrades the same way (GDD §3, §5).
    /// </summary>
    public interface IUpgradable
    {
        string Label { get; }
        int Level { get; }
        BigDouble UpgradeCost(EconomyService economy);
        void ApplyLevel(int level);
    }
}
