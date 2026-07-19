using Game.Core;
using Game.Gameplay;
using Game.Systems;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// Placeholder greybox HUD (IMGUI): cash/gems readout, a Mine tap button, and an upgrade button
    /// per station via <see cref="IUpgradable"/>. Deliberately unstyled — the themed uGUI canvas
    /// (currency bar, canvas split, floating text) is the M6 polish pass. Function first.
    /// </summary>
    public sealed class HudDebug : MonoBehaviour
    {
        private WalletService _wallet;
        private EconomyService _economy;
        private GameWorld _world;
        private IUpgradable[] _upgradables;
        private GUIStyle _big, _btn;

        private void Start()
        {
            _wallet = ServiceLocator.Get<WalletService>();
            _economy = ServiceLocator.Get<EconomyService>();
            _world = FindFirstObjectByType<GameWorld>();
            if (_world != null)
                _upgradables = new IUpgradable[] { _world.Mine, _world.Storage, _world.Market, _world.Train };
        }

        private void OnGUI()
        {
            if (_wallet == null || _world == null) return;

            if (_big == null)
            {
                _big = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold };
                _btn = new GUIStyle(GUI.skin.button) { fontSize = 18 };
            }

            GUILayout.BeginArea(new Rect(20, 20, 470, 720));

            GUILayout.Label("Cash: " + NumberFormatter.Format(_wallet.Cash) + "     Gems: " + _wallet.Gems, _big);
            GUILayout.Space(8);

            if (_world.Mine != null && GUILayout.Button("MINE!  (tap for ore)", _btn, GUILayout.Height(54)))
                _world.Mine.Tap();

            GUILayout.Space(12);
            if (_upgradables != null)
            {
                foreach (var u in _upgradables)
                {
                    if (u == null) continue;
                    BigDouble cost = u.UpgradeCost(_economy);
                    GUI.enabled = _wallet.Cash >= cost;
                    string label = u.Label + "   Lv." + u.Level + "     Upgrade: " + NumberFormatter.Format(cost);
                    if (GUILayout.Button(label, _btn, GUILayout.Height(46)))
                        _world.TryUpgrade(u);
                    GUI.enabled = true;
                }
            }

            GUILayout.EndArea();
        }
    }
}
