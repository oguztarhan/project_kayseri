using System.Collections.Generic;
using Game.Core;
using Game.Gameplay;
using Game.Systems;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// Placeholder greybox HUD (IMGUI): currencies, prestige, contract, daily reward, rewarded-ad stubs,
    /// tap-all-mines, per-station upgrade + manager buttons, offline welcome-back (GDD §6–10). uGUI = M6.
    /// </summary>
    public sealed class HudDebug : MonoBehaviour
    {
        private WalletService _wallet;
        private EconomyService _economy;
        private PrestigeService _prestige;
        private OfflineReport _offline;
        private DailyRewardService _daily;
        private IAdService _ad;
        private BoostService _boost;
        private GameWorld _world;
        private ContractManager _contract;
        private GUIStyle _big, _btn;
        private Vector2 _scroll;

        private void Start()
        {
            _wallet = ServiceLocator.Get<WalletService>();
            _economy = ServiceLocator.Get<EconomyService>();
            _prestige = ServiceLocator.Get<PrestigeService>();
            _offline = ServiceLocator.Get<OfflineReport>();
            _daily = ServiceLocator.Get<DailyRewardService>();
            _ad = ServiceLocator.Get<IAdService>();
            _boost = ServiceLocator.Get<BoostService>();
            _world = FindFirstObjectByType<GameWorld>();
            _contract = FindFirstObjectByType<ContractManager>();
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

            GUILayout.BeginArea(new Rect(20, 20, 540, 1000));
            string boostTag = (_boost != null && _boost.IsActive) ? "   [2x " + Mathf.CeilToInt(_boost.SecondsLeft) + "s]" : "";
            GUILayout.Label("Cash: " + NumberFormatter.Format(_wallet.Cash) + "   Gems: " + _wallet.Gems + boostTag, _big);

            if (_prestige != null)
            {
                GUILayout.Label("Investors: " + _prestige.Investors.ToString("F0") + "   (income x" + _prestige.IncomeMultiplier.ToString("0.00") + ")", _btn);
                GUI.enabled = _prestige.CanPrestige();
                if (GUILayout.Button("PRESTIGE  (+" + NumberFormatter.Format(_prestige.PendingInvestors()) + " investors)", _btn, GUILayout.Height(36)))
                    _world.DoPrestige();
                GUI.enabled = true;
            }

            if (_contract != null)
                GUILayout.Label("Contract: sell " + NumberFormatter.Format(_contract.Progress) + " / " + NumberFormatter.Format(_contract.Target) + " units  (" + Mathf.CeilToInt(_contract.TimeLeft) + "s, +" + _contract.RewardGems + " gems)", _btn);

            GUILayout.BeginHorizontal();
            if (_daily != null)
            {
                GUI.enabled = _daily.CanClaim();
                if (GUILayout.Button("Daily +" + _daily.RewardGems + " gems", _btn, GUILayout.Height(34))) _daily.Claim(_wallet);
                GUI.enabled = true;
            }
            if (_ad != null)
            {
                if (GUILayout.Button("Ad: +10 gems", _btn, GUILayout.Height(34))) _ad.ShowRewarded(() => _wallet.AddGems(10));
                if (GUILayout.Button("Ad: 2x 30s", _btn, GUILayout.Height(34))) _ad.ShowRewarded(() => { if (_boost != null) _boost.SetBoost(2d, 30d); });
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            if (_world.Mines.Count > 0 && GUILayout.Button("MINE!  (tap all mines)", _btn, GUILayout.Height(40)))
                _world.TapAllMines();

            GUILayout.Space(6);
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(720));
            List<IUpgradable> ups = _world.Upgradables;
            for (int i = 0; i < ups.Count; i++)
            {
                IUpgradable u = ups[i];
                if (u == null) continue;

                GUILayout.BeginHorizontal();
                BigDouble cost = u.UpgradeCost(_economy);
                GUI.enabled = _wallet.Cash >= cost;
                if (GUILayout.Button(u.Label + " Lv." + u.Level + "  Up: " + NumberFormatter.Format(cost), _btn, GUILayout.Height(36)))
                    _world.TryUpgrade(u);
                GUI.enabled = true;

                if (_world.CanManage(u))
                {
                    if (_world.HasManager(u))
                    {
                        GUI.enabled = false;
                        GUILayout.Button("Mgr OK", _btn, GUILayout.Width(92), GUILayout.Height(36));
                        GUI.enabled = true;
                    }
                    else
                    {
                        BigDouble mc = _world.ManagerCost(u);
                        GUI.enabled = _wallet.Cash >= mc;
                        if (GUILayout.Button("Mgr " + NumberFormatter.Format(mc), _btn, GUILayout.Width(118), GUILayout.Height(36)))
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
