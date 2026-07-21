using Game.Core;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Owns the map's unlockable mountains (GDD §4/§8). Applies saved unlocks on load and routes a purchase:
    /// spend cash → unlock the mountain → persist. The HUD reads this to show "Unlock &lt;Ore&gt; Mountain — $X".
    /// </summary>
    public sealed class MountainManager : MonoBehaviour
    {
        [SerializeField] private MountainMine[] mountains;

        private WalletService _wallet;
        private SaveData _data;

        public MountainMine[] Mountains => mountains;

        private void Start()
        {
            _wallet = ServiceLocator.Get<WalletService>();
            _data = ServiceLocator.Get<SaveData>();
            if (mountains == null || _data == null) return;
            for (int i = 0; i < mountains.Length; i++)
            {
                MountainMine m = mountains[i];
                if (m == null || m.Def == null) continue;
                if (_data.unlockedMountains.Contains(m.Def.name)) m.Unlock();
            }
        }

        public bool IsUnlocked(MountainMine m) => m != null && m.Unlocked;
        public double Cost(MountainMine m) => (m != null && m.Def != null) ? m.Def.UnlockCost : 0d;

        public bool TryUnlock(MountainMine m)
        {
            if (m == null || m.Def == null || m.Unlocked || _wallet == null) return false;
            if (!_wallet.TrySpendCash(new BigDouble(m.Def.UnlockCost))) return false;
            m.Unlock();
            if (_data != null && !_data.unlockedMountains.Contains(m.Def.name)) _data.unlockedMountains.Add(m.Def.name);
            return true;
        }
    }
}
