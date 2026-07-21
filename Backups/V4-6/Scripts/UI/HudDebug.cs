using Game.Core;
using Game.Gameplay;
using Game.Systems;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// Mobile idle-tycoon HUD (IMGUI, resolution-scaled): a top currency bar and a bottom tabbed sheet
    /// (Upgrades / Managers / Orders / Extras) anchored to the screen edges — the play area stays clear
    /// in the middle. Placeholder for a full uGUI/TMP pass, but laid out like a shipping idle game.
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
        private IncomeMeter _income;
        [SerializeField] private bool worldLabelsOnly;   // when the uGUI HUD owns the main UI, keep only world labels

        private MountainManager _mountains;
        private Camera _cam;
        private TruckFleet[] _fleets;

        private int _tab;
        private bool _open;
        private Vector2 _scroll;
        private bool _init;
        private GUIStyle _bar, _panel, _cur, _curR, _rate, _btn, _tabOn, _tabOff, _title, _worldBtn;

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
            _income = FindFirstObjectByType<IncomeMeter>();
            _mountains = FindFirstObjectByType<MountainManager>();
            _cam = Camera.main;
            _fleets = FindObjectsByType<TruckFleet>(FindObjectsSortMode.None);
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c); t.Apply();
            return t;
        }

        private void EnsureStyles(float s)
        {
            if (!_init)
            {
                _bar = new GUIStyle(); _bar.normal.background = Solid(new Color(0.11f, 0.13f, 0.17f, 0.95f));
                _panel = new GUIStyle(); _panel.normal.background = Solid(new Color(0.15f, 0.17f, 0.21f, 0.97f));
                _cur = new GUIStyle { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
                _cur.normal.textColor = Color.white; _cur.padding = new RectOffset(14, 8, 0, 0);
                _curR = new GUIStyle(_cur) { alignment = TextAnchor.MiddleRight };
                _curR.padding = new RectOffset(8, 16, 0, 0);
                _rate = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                _rate.normal.textColor = new Color(0.45f, 0.9f, 0.5f);
                _btn = new GUIStyle(GUI.skin.button);
                _tabOn = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
                _tabOn.normal.background = Solid(new Color(0.20f, 0.56f, 0.45f, 1f)); _tabOn.normal.textColor = Color.white;
                _tabOff = new GUIStyle(GUI.skin.button);
                _tabOff.normal.background = Solid(new Color(0.20f, 0.23f, 0.28f, 1f)); _tabOff.normal.textColor = new Color(0.82f, 0.82f, 0.82f);
                _title = new GUIStyle { fontStyle = FontStyle.Bold }; _title.normal.textColor = Color.white;
                _worldBtn = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                _worldBtn.normal.background = Solid(new Color(0.13f, 0.52f, 0.30f, 0.96f)); _worldBtn.normal.textColor = Color.white;
                _worldBtn.active.background = Solid(new Color(0.10f, 0.42f, 0.24f, 0.96f)); _worldBtn.active.textColor = Color.white;
                _worldBtn.hover.background = _worldBtn.normal.background; _worldBtn.hover.textColor = Color.white;
                _init = true;
            }
            _cur.fontSize = _curR.fontSize = (int)(30 * s);
            _rate.fontSize = (int)(22 * s);
            _worldBtn.fontSize = (int)(17 * s);
            _btn.fontSize = (int)(20 * s);
            _tabOn.fontSize = _tabOff.fontSize = (int)(22 * s);
            _title.fontSize = (int)(23 * s);
        }

        private void OnGUI()
        {
            if (_wallet == null || _world == null) return;
            float w = Screen.width, h = Screen.height;
            float s = Mathf.Clamp(h / 1080f, 0.5f, 2.5f);
            EnsureStyles(s);

            float topH = 70f * s;
            if (worldLabelsOnly) { DrawWorldUnlocks(s, topH); return; }

            // ---- top currency bar ----
            GUI.Box(new Rect(0, 0, w, topH), GUIContent.none, _bar);
            string boost = (_boost != null && _boost.IsActive) ? "   (x2!)" : "";
            GUI.Label(new Rect(0, 0, w * 0.40f, topH), "$ " + NumberFormatter.Format(_wallet.Cash) + boost, _cur);
            if (_income != null)
                GUI.Label(new Rect(w * 0.30f, 0, w * 0.40f, topH), "+ " + NumberFormatter.Format(new BigDouble(_income.RatePerSec * 60d)) + " /min", _rate);
            string inv = _prestige != null ? _prestige.Investors.ToString("F0") : "0";
            GUI.Label(new Rect(w * 0.55f, 0, w * 0.45f, topH), "Gems " + _wallet.Gems + "     Investors " + inv, _curR);

            // ---- tap-to-mine (top-left, out of the middle) ----
            if (_world.Mines.Count > 0 && GUI.Button(new Rect(14f * s, topH + 12f * s, 170f * s, 66f * s), "TAP MINE", _btn))
                _world.TapAllMines();

            // ---- floating "Unlock" labels over ghosted future content (GDD §12) ----
            DrawWorldUnlocks(s, topH);

            // ---- bottom tab bar (tap a tab to open its sheet; tap again to close) ----
            float tabH = 78f * s;
            string[] tabs = { "Upgrades", "Managers", "Orders", "Extras" };
            float tw = w / tabs.Length;
            for (int i = 0; i < tabs.Length; i++)
                if (GUI.Button(new Rect(i * tw, h - tabH, tw, tabH), tabs[i], (_open && i == _tab) ? _tabOn : _tabOff))
                {
                    if (_open && _tab == i) _open = false;
                    else { _tab = i; _open = true; }
                }

            // ---- bottom sheet panel (only while open, so the play area is free) ----
            if (_open)
            {
                float panelH = h * 0.40f;
                float py = h - tabH - panelH;
                GUI.Box(new Rect(0, py, w, panelH), GUIContent.none, _panel);
                if (GUI.Button(new Rect(w - 54f * s, py + 6f * s, 46f * s, 46f * s), "X", _btn)) _open = false;
                GUILayout.BeginArea(new Rect(12f * s, py + 10f * s, w - 24f * s - 54f * s, panelH - 20f * s));
                _scroll = GUILayout.BeginScrollView(_scroll);
                DrawTab(s);
                GUILayout.EndScrollView();
                GUILayout.EndArea();
            }

            // ---- welcome-back modal ----
            if (_offline != null && _offline.Pending)
            {
                float pw = 440f * s, ph = 230f * s;
                GUI.Box(new Rect(w / 2 - pw / 2, h / 2 - ph / 2, pw, ph), GUIContent.none, _panel);
                GUILayout.BeginArea(new Rect(w / 2 - pw / 2 + 22f * s, h / 2 - ph / 2 + 22f * s, pw - 44f * s, ph - 44f * s));
                GUILayout.Label("Welcome back! While away you earned", _title);
                GUILayout.Label(NumberFormatter.Format(_offline.Amount) + " cash", _title);
                GUILayout.Space(8f * s);
                if (GUILayout.Button("Collect", _btn, GUILayout.Height(58f * s))) _offline.Pending = false;
                GUILayout.EndArea();
            }
        }

        // Floating, tappable "Unlock <thing> — $price" buttons projected over ghosted future content.
        private void DrawWorldUnlocks(float s, float topH)
        {
            if (_cam == null) return;
            float w = Screen.width, h = Screen.height;
            float botLimit = _open ? h - 78f * s - h * 0.40f : h - 78f * s;   // stay clear of the bottom sheet/tabs
            float bw = 210f * s, bh = 52f * s;

            // ghosted locked mountains
            if (_mountains != null && _mountains.Mountains != null)
            {
                var ms = _mountains.Mountains;
                for (int i = 0; i < ms.Length; i++)
                {
                    MountainMine m = ms[i];
                    if (m == null || m.Def == null || _mountains.IsUnlocked(m)) continue;
                    if (!ToRect(m.LabelAnchor, bw, bh, topH, botLimit, out Rect r)) continue;
                    BigDouble c = new BigDouble(_mountains.Cost(m));
                    GUI.enabled = _wallet.Cash >= c;
                    if (GUI.Button(r, "Unlock " + m.Def.DisplayName + "\n$" + NumberFormatter.Format(c), _worldBtn)) _mountains.TryUnlock(m);
                    GUI.enabled = true;
                }
            }

            // "+1 truck" ghost sitting in each fleet's depot bay (parking lot)
            if (_fleets != null && _economy != null && _world != null)
            {
                for (int i = 0; i < _fleets.Length; i++)
                {
                    TruckFleet f = _fleets[i];
                    if (f == null || !f.HasNextTruck) continue;
                    GameWorld.Group g = _world.GroupOf(f); if (g == null) continue;
                    if (!ToRect(f.NextTruckAnchor, bw, bh, topH, botLimit, out Rect r)) continue;
                    BigDouble fc = g.Rep.TrackCost(0, _economy);   // Trucks track = 0
                    GUI.enabled = _wallet.Cash >= fc;
                    if (GUI.Button(r, "+1 " + f.Label + "\n$" + NumberFormatter.Format(fc), _worldBtn)) _world.TryUpgradeGroup(g, 0);
                    GUI.enabled = true;
                }
            }
        }

        // Project a world anchor to a clamped on-screen button rect. Returns false if behind the camera.
        private bool ToRect(Vector3 world, float bw, float bh, float topH, float botLimit, out Rect r)
        {
            Vector3 sp = _cam.WorldToScreenPoint(world);
            r = default;
            if (sp.z <= 0f) return false;
            float w = Screen.width, h = Screen.height;
            float rx = Mathf.Clamp(sp.x - bw / 2f, 4f, w - bw - 4f);
            float ry = Mathf.Clamp((h - sp.y) - bh, topH + 4f, botLimit - bh - 4f);
            r = new Rect(rx, ry, bw, bh);
            return true;
        }

        private void DrawTab(float s)
        {
            float bh = 46f * s;
            var groups = _world.Groups;

            if (_tab == 0) // Upgrades — one labelled button per track (fleets grouped) so it's clear what each buys
            {
                for (int i = 0; i < groups.Count; i++)
                {
                    GameWorld.Group g = groups[i]; if (g == null || g.Rep == null) continue;
                    string fleet = g.Members.Count > 1 ? " x" + g.Members.Count : "";
                    for (int t = 0; t < g.Rep.TrackCount; t++)
                    {
                        BigDouble c = g.Rep.TrackCost(t, _economy);
                        GUI.enabled = _wallet.Cash >= c;
                        if (GUILayout.Button(g.Label + fleet + "  —  " + g.Rep.TrackName(t) + "   Lv." + g.Rep.TrackLevel(t) + "      $" + NumberFormatter.Format(c), _btn, GUILayout.Height(bh)))
                            _world.TryUpgradeGroup(g, t);
                        GUI.enabled = true;
                    }
                }
            }
            else if (_tab == 1) // Managers
            {
                for (int i = 0; i < groups.Count; i++)
                {
                    GameWorld.Group g = groups[i]; if (g == null || !_world.GroupCanManage(g)) continue;
                    if (_world.GroupHasManager(g)) { GUI.enabled = false; GUILayout.Button(g.Label + "   Manager hired  (x2 rate)", _btn, GUILayout.Height(bh)); GUI.enabled = true; }
                    else
                    {
                        BigDouble mc = _world.GroupManagerCost(g);
                        GUI.enabled = _wallet.Cash >= mc;
                        if (GUILayout.Button("Hire " + g.Label + " Manager    $" + NumberFormatter.Format(mc), _btn, GUILayout.Height(bh))) _world.TryHireManagerGroup(g);
                        GUI.enabled = true;
                    }
                }
            }
            else if (_tab == 2) // Orders
            {
                if (_contract != null)
                    GUILayout.Label("Contract:  sell " + NumberFormatter.Format(_contract.Progress) + " / " + NumberFormatter.Format(_contract.Target) +
                                    "   (" + Mathf.CeilToInt(_contract.TimeLeft) + "s left,  +" + _contract.RewardGems + " gems)", _title);
                GUILayout.Space(10f * s);
                if (_daily != null) { GUI.enabled = _daily.CanClaim(); if (GUILayout.Button("Claim daily reward   (+" + _daily.RewardGems + " gems)", _btn, GUILayout.Height(bh))) _daily.Claim(_wallet); GUI.enabled = true; }
            }
            else // Extras
            {
                if (_mountains != null && _mountains.Mountains != null)
                {
                    GUILayout.Label("Unlock Mountains  (new ore tiers):", _title);
                    var ms = _mountains.Mountains;
                    for (int i = 0; i < ms.Length; i++)
                    {
                        MountainMine m = ms[i]; if (m == null || m.Def == null) continue;
                        if (_mountains.IsUnlocked(m)) { GUI.enabled = false; GUILayout.Button(m.Def.DisplayName + "   (unlocked)", _btn, GUILayout.Height(bh)); GUI.enabled = true; }
                        else
                        {
                            BigDouble c = new BigDouble(_mountains.Cost(m));
                            GUI.enabled = _wallet.Cash >= c;
                            if (GUILayout.Button("Unlock " + m.Def.DisplayName + "   $" + NumberFormatter.Format(c), _btn, GUILayout.Height(bh))) _mountains.TryUnlock(m);
                            GUI.enabled = true;
                        }
                    }
                    GUILayout.Space(12f * s);
                }
                if (_prestige != null)
                {
                    GUILayout.Label("Income x" + _prestige.IncomeMultiplier.ToString("0.00") + "     Investors " + _prestige.Investors.ToString("F0"), _title);
                    GUI.enabled = _prestige.CanPrestige();
                    if (GUILayout.Button("PRESTIGE   (+" + NumberFormatter.Format(_prestige.PendingInvestors()) + " investors)", _btn, GUILayout.Height(bh))) _world.DoPrestige();
                    GUI.enabled = true;
                }
                GUILayout.Space(10f * s);
                if (_ad != null)
                {
                    if (GUILayout.Button("Watch Ad  ->  +10 gems", _btn, GUILayout.Height(bh))) _ad.ShowRewarded(() => _wallet.AddGems(10));
                    if (GUILayout.Button("Watch Ad  ->  2x income for 30s", _btn, GUILayout.Height(bh))) _ad.ShowRewarded(() => { if (_boost != null) _boost.SetBoost(2d, 30d); });
                }
            }
        }
    }
}
