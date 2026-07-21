using Game.Core;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Drives the archipelago progression (GDD §4/§8). The player starts on the Coal home island, unlocks the
    /// first ore island with cash, then <b>maxes each island's upgrades before the next one can be unlocked</b> —
    /// "goes on like that". Every unlocked island keeps earning cash forever. The HUD reads this to show the
    /// floating "Unlock / Upgrade &lt;Ore&gt; Island" labels over each island.
    /// </summary>
    public sealed class IslandManager : MonoBehaviour
    {
        [SerializeField] private Island[] islands;   // in unlock order (Iron → Copper → … → Diamond)

        private WalletService _wallet;
        private SaveData _data;

        public Island[] Islands => islands;

        private void Start()
        {
            _wallet = ServiceLocator.Get<WalletService>();
            _data = ServiceLocator.Get<SaveData>();
            if (islands == null || _data == null) return;
            for (int i = 0; i < islands.Length; i++)
            {
                Island isl = islands[i];
                if (isl == null || isl.Def == null) continue;
                if (_data.unlockedIslands.Contains(isl.Def.name)) isl.Unlock();
                for (int j = 0; j < _data.islandLevels.Count; j++)
                    if (_data.islandLevels[j].id == isl.Def.name) { isl.SetLevel(_data.islandLevels[j].level); break; }
            }
        }

        public bool IsUnlocked(Island i) => i != null && i.Unlocked;
        public double Cost(Island i) => (i != null && i.Def != null) ? i.Def.UnlockCost : 0d;

        /// <summary>Index of an island in the ordered array, or -1.</summary>
        public int IndexOf(Island i)
        {
            if (islands == null || i == null) return -1;
            for (int k = 0; k < islands.Length; k++) if (islands[k] == i) return k;
            return -1;
        }

        /// <summary>An island can be bought only once the previous island in the chain is fully maxed.</summary>
        public bool CanUnlock(Island i)
        {
            if (i == null || i.Unlocked) return false;
            int idx = IndexOf(i);
            if (idx < 0) return false;
            return idx == 0 || (islands[idx - 1] != null && islands[idx - 1].IsMaxed);
        }

        /// <summary>The single locked island the player is currently working toward (first locked in the chain).</summary>
        public Island NextUnlockable
        {
            get
            {
                if (islands == null) return null;
                for (int k = 0; k < islands.Length; k++)
                    if (islands[k] != null && !islands[k].Unlocked) return islands[k];
                return null;
            }
        }

        public bool TryUnlock(Island i)
        {
            if (!CanUnlock(i) || _wallet == null) return false;
            if (!_wallet.TrySpendCash(new BigDouble(i.Def.UnlockCost))) return false;
            i.Unlock();
            if (_data != null && !_data.unlockedIslands.Contains(i.Def.name)) _data.unlockedIslands.Add(i.Def.name);
            return true;
        }

        public bool TryUpgrade(Island i)
        {
            if (i == null || i.Def == null || !i.Unlocked || i.IsMaxed || _wallet == null) return false;
            if (!_wallet.TrySpendCash(new BigDouble(i.UpgradeCost))) return false;
            i.Upgrade();
            SaveLevel(i);
            return true;
        }

        private void SaveLevel(Island i)
        {
            if (_data == null) return;
            for (int j = 0; j < _data.islandLevels.Count; j++)
                if (_data.islandLevels[j].id == i.Def.name) { _data.islandLevels[j].level = i.Level; return; }
            _data.islandLevels.Add(new StationLevel { id = i.Def.name, level = i.Level });
        }
    }
}
