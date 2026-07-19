using System.Collections.Generic;
using Game.Core;
using Game.Gameplay;
using Game.Systems;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// Placeholder greybox HUD (IMGUI): cash/gems readout, a tap-all-mines button, and a scrollable
    /// upgrade button per station via <see cref="IUpgradable"/>. Themed uGUI + floating text = M6.
    /// </summary>
    public sealed class HudDebug : MonoBehaviour
    {
        private WalletService _wallet;
        private EconomyService _economy;
        private GameWorld _world;
        private GUIStyle _big, _btn;
        private Vector2 _scroll;

        private void Start()
        {
            _wallet = ServiceLocator.Get<WalletService>();
            _economy = ServiceLocator.Get<EconomyService>();
            _world = FindFirstObjectByType<GameWorld>();
        }

        private void OnGUI()
        {
            if (_wallet == null || _world == null) return;
            if (_big == null)
            {
                _big = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold };
                _btn = new GUIStyle(GUI.skin.button) { fontSize = 16 };
            }

            GUILayout.BeginArea(new Rect(20, 20, 490, 900));
            GUILayout.Label("Cash: " + NumberFormatter.Format(_wallet.Cash) + "     Gems: " + _wallet.Gems, _big);
            GUILayout.Space(6);

            if (_world.Mines.Count > 0 && GUILayout.Button("MINE!  (tap all mines)", _btn, GUILayout.Height(48)))
                _world.TapAllMines();

            GUILayout.Space(10);
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(760));
            List<IUpgradable> ups = _world.Upgradables;
            for (int i = 0; i < ups.Count; i++)
            {
                IUpgradable u = ups[i];
                if (u == null) continue;
                BigDouble cost = u.UpgradeCost(_economy);
                GUI.enabled = _wallet.Cash >= cost;
                if (GUILayout.Button(u.Label + "  Lv." + u.Level + "     Upgrade: " + NumberFormatter.Format(cost), _btn, GUILayout.Height(40)))
                    _world.TryUpgrade(u);
                GUI.enabled = true;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
