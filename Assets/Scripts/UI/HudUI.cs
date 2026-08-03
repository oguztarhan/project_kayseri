using Game.Core;
using Game.Gameplay;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The always-on HUD — first screen of the editor-authored UI set (Figma "hud_montaj"): gold and
    /// gem pills with live TMP values, the income-rate pill under the cash pill, the right rail of
    /// openers (store, offer, daily reward, map) and the big UPGRADE button bottom-right. Thin
    /// controller: every reference below is wired in the Inspector on the UI_HUD prefab, so layout,
    /// sprites and spacing are all tunable from the hierarchy without touching code.
    ///
    /// Replaces the code-built top bar and all of MetaHud. <see cref="ContractService"/>
    /// ticking lives here now (MetaHud used to do it): the HUD is the one screen that is always
    /// loaded, and it is also the only thing that knows the whole empire's income per minute, which
    /// is what sizes each contract. <see cref="ContractUI"/> only reads and claims.
    /// </summary>
    public sealed class HudUI : MonoBehaviour
    {
        [Header("Üst bar")]
        [SerializeField] private TMP_Text goldValue;
        [SerializeField] private TMP_Text gemsValue;
        [SerializeField] private TMP_Text rateValue;
        [SerializeField] private Button settingsButton;

        [Header("Sağ ray")]
        [SerializeField] private Button storeButton;
        [SerializeField] private Button offerButton;      // Faz 10: popup_teklif açacak
        [SerializeField] private Button dailyButton;
        [SerializeField] private Button mapButton;
        [SerializeField] private Button contractButton;
        [Tooltip("Kontrat butonunun altındaki canlı sayaç.")]
        [SerializeField] private TMP_Text contractTimerValue;
        [SerializeField] private Button adButton;

        [Header("Hızlandırıcı göstergesi")]
        [Tooltip("Sadece bir hızlandırıcı çalışırken açılır.")]
        [SerializeField] private GameObject boostIndicator;
        [SerializeField] private TMP_Text boostValue;

        [Header("Alt")]
        [SerializeField] private Button upgradeButton;
        [SerializeField] private Button prestigeButton;
        [Tooltip("Yükseltmenin solundaki kısayol: reklam izle, gelir 2× olsun. Hak ve bekleme süresi UI_Reklam'daki yuvanın.")]
        [SerializeField] private Button boostButton;
        [SerializeField] private Image boostButtonImage;
        [Tooltip("Butonun ne sattığını yazan üst satır: \"×2 GELİR\".")]
        [SerializeField] private TMP_Text boostButtonTitle;
        [Tooltip("Alt satır: hazırken bedeli, çalışırken ve beklerken sayaç.")]
        [SerializeField] private TMP_Text boostButtonLabel;

        [Header("Ekran bağlantıları (sahne nesneleri)")]
        [SerializeField] private PremiumStoreUI store;
        [SerializeField] private UpgradePanelUI upgradePanel;
        [SerializeField] private IslandMapUI islandMap;
        [SerializeField] private SettingsUI settings;
        [SerializeField] private DailyRewardUI dailyScreen;
        [SerializeField] private PrestigeUI prestigeScreen;
        [SerializeField] private ContractUI contractScreen;
        [SerializeField] private AdRewardUI adScreen;

        [SerializeField] private float refreshInterval = 0.25f;

        private WalletService _wallet;
        private ContractService _contract;
        private BoostService _boost;
        private WorldIslands _world;
        private CoalOperation _op;
        private float _timer;
        private double _shownCash;        // eased display value behind the real balance
        private bool _haveShownCash;

        private void Start()
        {
            _wallet = ServiceLocator.Get<WalletService>();
            _contract = ServiceLocator.Get<ContractService>();
            _boost = ServiceLocator.Get<BoostService>();
            _world = FindAnyObjectByType<WorldIslands>();
            BindEnabledOp();

            if (storeButton != null) storeButton.onClick.AddListener(OnStore);
            if (dailyButton != null) dailyButton.onClick.AddListener(OnDaily);
            if (mapButton != null) mapButton.onClick.AddListener(OnMap);
            if (contractButton != null) contractButton.onClick.AddListener(OnContract);
            if (adButton != null) adButton.onClick.AddListener(OnAds);
            if (upgradeButton != null) upgradeButton.onClick.AddListener(OnUpgrades);
            if (boostButton != null) boostButton.onClick.AddListener(OnBoost);
            if (prestigeButton != null) prestigeButton.onClick.AddListener(OnPrestige);
            if (settingsButton != null) settingsButton.onClick.AddListener(OnSettings);

            if (_wallet != null) _wallet.GemsChanged += RefreshGems;
            RefreshGems();
            Refresh();

            // HUD hiç açılıp kapanmaz — sadece tıklama sesi, whoosh yok.
            UiPanelSound.AttachButtonsOnly(gameObject);
        }

        private void OnDestroy()
        {
            if (_wallet != null) _wallet.GemsChanged -= RefreshGems;
        }

        private void Update()
        {
            if (_wallet == null) _wallet = ServiceLocator.Get<WalletService>();
            if (_op == null || !_op.enabled) BindEnabledOp();
            if (_contract != null) _contract.Tick(Time.deltaTime, IncomePerMinute());
            RollCash(Time.unscaledDeltaTime);
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = refreshInterval;
            Refresh();
        }

        /// <summary>
        /// Ease the displayed balance toward the real one instead of snapping every quarter second.
        /// The counter climbing is most of what makes the money feel like it's flowing in.
        /// </summary>
        private void RollCash(float dt)
        {
            if (_wallet == null || goldValue == null) return;
            double target = _wallet.Cash.ToDouble();
            if (!_haveShownCash) { _shownCash = target; _haveShownCash = true; }
            else
            {
                double diff = target - _shownCash;
                // snap on a big jump (a purchase, an offline grant) so the counter never crawls for seconds
                if (diff < 0d || System.Math.Abs(diff) > System.Math.Max(1d, target * 0.35d)) _shownCash = target;
                else _shownCash += diff * (1d - System.Math.Exp(-9d * dt));
            }
            goldValue.text = NumberFormatter.Format(new BigDouble(_shownCash));
        }

        /// <summary>Several operations live on the controller (one per island) — bind the enabled one.</summary>
        private void BindEnabledOp()
        {
            var ops = FindObjectsByType<CoalOperation>();
            for (int i = 0; i < ops.Length; i++)
                if (ops[i].enabled) { _op = ops[i]; return; }
            if (_op == null && ops.Length > 0) _op = ops[0];
        }

        /// <summary>
        /// What the empire earns a minute — sizes the next contract. Falls back to the active island
        /// alone if the world manager is missing, so this still works on a bare scene.
        /// </summary>
        private double IncomePerMinute()
        {
            if (_world != null)
            {
                double sum = 0d;
                for (int i = 0; i < _world.Count; i++) if (_world.IsOwned(i)) sum += _world.RatePerMin(i);
                if (sum > 0d) return sum;
            }
            return _op != null ? _op.CashPerMinute : 0d;
        }

        private void Refresh()
        {
            if (rateValue != null && _op != null)
                rateValue.text = string.Format(Loc.T("ortak.dakika_basina"),
                                               "$" + NumberFormatter.Format(new BigDouble(_op.CashPerMinute)));
            if (contractTimerValue != null && _contract != null)
                contractTimerValue.text = _contract.Claimable ? Loc.T("ortak.hazir") : ContractUI.ClockText(_contract.SecondsLeft);

            bool boosted = _boost != null && _boost.IsActive;
            if (boostIndicator != null)
            {
                if (boostIndicator.activeSelf != boosted) boostIndicator.SetActive(boosted);
                if (boosted && boostValue != null)
                    boostValue.text = "×" + _boost.ActiveMultiplier.ToString("0.#",
                        System.Globalization.CultureInfo.InvariantCulture)
                        + "  " + ContractUI.ClockText(_boost.SecondsLeft);
            }
            RefreshBoostButton(boosted);
        }

        /// <summary>
        /// The shortcut next to the upgrade button. It has no state of its own — everything it shows
        /// is read back off the ad screen's boost slot, so spending the charge from either place leaves
        /// both looking the same.
        /// </summary>
        private void RefreshBoostButton(bool boosted)
        {
            if (boostButton == null) return;
            bool ready = !boosted && adScreen != null && adScreen.BoostReady;
            boostButton.interactable = ready;

            if (boostButtonImage != null)
                // uGUI's disabled tint latches onto the graphic; stamp the state's colour back on
                boostButtonImage.CrossFadeColor(ready ? Color.white : DimBoost, 0f, true, true);

            if (boostButtonTitle != null)
            {
                // While a boost runs, the headline is whatever is actually multiplying the income —
                // a store offer can set a different one, and the button must not claim the slot's.
                double mult = boosted ? _boost.ActiveMultiplier
                                      : (adScreen != null ? adScreen.BoostMultiplier : 2d);
                boostButtonTitle.text = string.Format(Loc.T("hud.gelir"),
                    mult.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
            }

            if (boostButtonLabel == null) return;
            if (boosted)
            {
                boostButtonLabel.text = ContractUI.ClockText(_boost.SecondsLeft);
                return;
            }
            // ready: say what it costs, because the ad badge no longer says it
            if (ready) { boostButtonLabel.text = Loc.T("hud.reklam_izle"); return; }
            // not ready for one of two reasons, and the player needs to be able to tell them apart
            float wait = adScreen != null ? adScreen.BoostCooldown : 0f;
            boostButtonLabel.text = wait > 0f ? ContractUI.ClockText(wait) : Loc.T("ortak.yarin");
        }

        private static readonly Color DimBoost = new Color(0.55f, 0.58f, 0.66f, 1f);

        private void RefreshGems()
        {
            if (gemsValue != null && _wallet != null) gemsValue.text = _wallet.Gems.ToString();
        }

        private void OnStore()
        {
            if (store != null) store.Show();
        }

        private void OnDaily()
        {
            if (dailyScreen != null) dailyScreen.Toggle();
        }

        private void OnMap()
        {
            if (islandMap != null) islandMap.ToggleMap();
        }

        private void OnUpgrades()
        {
            if (upgradePanel != null) upgradePanel.Toggle();
        }

        private void OnSettings()
        {
            if (settings != null) settings.Toggle();
        }

        private void OnPrestige()
        {
            if (prestigeScreen != null) prestigeScreen.Toggle();
        }

        private void OnContract()
        {
            if (contractScreen != null) contractScreen.Toggle();
        }

        private void OnAds()
        {
            if (adScreen != null) adScreen.Toggle();
        }

        /// <summary>Straight to the ad — the shortcut exists precisely to skip opening the ad screen.</summary>
        private void OnBoost()
        {
            if (adScreen != null) adScreen.WatchBoost();
        }
    }
}
