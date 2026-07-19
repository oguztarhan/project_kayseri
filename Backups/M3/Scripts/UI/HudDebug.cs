using System.Collections.Generic;
using Game.Core;
using Game.Gameplay;
using Game.Systems;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// Placeholder greybox HUD (IMGUI): cash/gems, tap-all-mines, per-station upgrade + hire-manager
    /// buttons, and an offline welcome-back popup (GDD §6, §7). Themed uGUI + floating text = M6.
    /// </summary>
    public sealed class HudDebug : MonoBehaviour
    {
        private WalletService _wallet;
        private EconomyService _economy;
        private GameWorld _world;
        private OfflineReport _offline;
        private GUIStyle _big, _btn;
        private Vector2 _scroll;

        private void Start()
        {
            _wallet = ServiceLocator.Get<WalletService>();
            _economy = ServiceLocator.Get<EconomyService>();
            _offline = ServiceLocator.Get<OfflineReport>();
            _world = FindFirstObjectByType<GameWorld>();
        }

        private void OnGUI()
        {
            if (_wallet == null || _world == null) return;
            if (_big == null)
            {
                _big = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold };
                _btn = new GUIStyle(GUI.skin.button) { fontSize = 15 };
            }

            if (_offline != null && _offline.Pending)
            {
                GUILayout.BeginArea(new Rect(Screen.width / 2 - 180, Screen.height / 2 - 90, 360, 180), GUI.skin.box);
                GUILayout.Label("Welcome back!", _big);
                GUILayout.Label("While away you earned:", _btn);
                GUILayout.Label(NumberFormatter.Format(_offline.Amount) + " cash", _big);
                if (GUILayout.Button("Collect", _btn, GUILayout.Height(46))) _offline.Pending = false;
                GUILayout.EndArea();
            }

            GUILayout.BeginArea(new Rect(20, 20, 520, 940));
            GUILayout.Label("Cash: " + NumberFormatter.Format(_wallet.Cash) + "     Gems: " + _wallet.Gems, _big);
            GUILayout.Space(6);
            if (_world.Mines.Count > 0 && GUILayout.Button("MINE!  (tap all mines)", _btn, GUILayout.Height(44)))
                _world.TapAllMines();

            GUILayout.Space(8);
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(820));
            List<IUpgradable> ups = _world.Upgradables;
            for (int i = 0; i < ups.Count; i++)
            {
                IUpgradable u = ups[i];
                if (u == null) continue;

                GUILayout.BeginHorizontal();
                BigDouble cost = u.UpgradeCost(_economy);
                GUI.enabled = _wallet.Cash >= cost;
                if (GUILayout.Button(u.Label + " Lv." + u.Level + "  Up: " + NumberFormatter.Format(cost), _btn, GUILayout.Height(38)))
                    _world.TryUpgrade(u);
                GUI.enabled = true;

                if (_world.CanManage(u))
                {
                    if (_world.HasManager(u))
                    {
                        GUI.enabled = false;
                        GUILayout.Button("Mgr OK", _btn, GUILayout.Width(96), GUILayout.Height(38));
                        GUI.enabled = true;
                    }
                    else
                    {
                        BigDouble mc = _world.ManagerCost(u);
                        GUI.enabled = _wallet.Cash >= mc;
                        if (GUILayout.Button("Mgr " + NumberFormatter.Format(mc), _btn, GUILayout.Width(120), GUILayout.Height(38)))
                            _world.TryHireManager(u);
                        GUI.enabled = true;
                    }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
