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
        [Tooltip("Altın hapının kendisi. Üstündeki + rozeti mağazayı vaat ediyor, o yüzden hap da mağazayı açar.")]
        [SerializeField] private Button goldButton;
        [Tooltip("Elmas hapının kendisi — altın hapı gibi mağazayı açar.")]
        [SerializeField] private Button gemsButton;

        [Header("Sağ ray")]
        [SerializeField] private Button storeButton;
        [Tooltip("Reklam butonunun altındaki fırsat kısayolu. HUD'un kalıcı parçası: açık teklif yokken "
                 + "de yerinde durur, o hâlde mağazayı açar.")]
        [SerializeField] private Button offerButton;
        [Tooltip("Fırsat butonunun altındaki geri sayım.")]
        [SerializeField] private TMP_Text offerTimerValue;
        [Tooltip("Geri sayımın kapsülü. Satacak paket kalmayınca kapanır — buton kalır, sayaç gider.")]
        [SerializeField] private GameObject offerTimerChip;
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
        [Tooltip("Butonun içindeki tek satır: \"×2 GELİR\". Kalan süre üstteki hızlandırıcı göstergesinde.")]
        [SerializeField] private TMP_Text boostButtonTitle;

        [Header("Ekran bağlantıları (sahne nesneleri)")]
        [SerializeField] private PremiumStoreUI store;
        [Tooltip("Yükseltme ekranı. Tek seferlik genişletmeler oradaki şeridin son yuvasından açılır, " +
                 "yani HUD'un eski uzun listeye bağlanacak bir işi kalmadı.")]
        [SerializeField] private StationScreenUI stationScreen;
        [SerializeField] private IslandMapUI islandMap;
        [SerializeField] private SettingsUI settings;
        [SerializeField] private DailyRewardUI dailyScreen;
        [SerializeField] private PrestigeUI prestigeScreen;
        [SerializeField] private ContractUI contractScreen;
        [SerializeField] private AdRewardUI adScreen;
        [Tooltip("Açılır fırsat penceresi. Kendi zamanlamasını kendi yönetir; HUD sadece butonu ona açar.")]
        [SerializeField] private OfferPopupUI offerScreen;

        [SerializeField] private float refreshInterval = 0.25f;

        [Header("Sayaç vuruşu")]
        [Tooltip("Para geldiğinde sayının ne kadar büyüdüğü. Hapın kendisi değil, içindeki sayı zıplar — "
                 + "hapa dokunma yaylanması yazıyor, ikisi aynı ölçeği paylaşamaz.")]
        [SerializeField] private float counterPunch = 0.16f;
        [SerializeField] private float counterPunchSeconds = 0.28f;
        [Tooltip("Elmas sayacının dolma hızı. Nakitinki 9 — para saniyede bir damlıyor, elmas ise "
                 + "yılda birkaç kez; yavaş sayması izlenecek bir şey oluyor. Küçük değer = yavaş.")]
        [SerializeField] private float gemRollSpeed = 5.5f;

        private WalletService _wallet;
        private ContractService _contract;
        private BoostService _boost;
        private WorldIslands _world;
        private CoalOperation _op;
        private float _timer;
        private double _shownCash;        // eased display value behind the real balance
        private bool _haveShownCash;
        private double _shownGems;        // same easing for gems — they used to snap
        private long _writtenGems = -1;   // last integer actually written, so a settled counter allocates nothing
        private bool _haveShownGems;
        private float _goldPunch;         // seconds left on the pop
        private float _gemPunch;

        private void Start()
        {
            _wallet = ServiceLocator.Get<WalletService>();
            _contract = ServiceLocator.Get<ContractService>();
            _boost = ServiceLocator.Get<BoostService>();
            _world = FindAnyObjectByType<WorldIslands>();
            BindEnabledOp();

            if (storeButton != null) storeButton.onClick.AddListener(OnStore);
            if (goldButton != null) goldButton.onClick.AddListener(OnStore);
            if (gemsButton != null) gemsButton.onClick.AddListener(OnStore);
            if (dailyButton != null) dailyButton.onClick.AddListener(OnDaily);
            if (mapButton != null) mapButton.onClick.AddListener(OnMap);
            if (contractButton != null) contractButton.onClick.AddListener(OnContract);
            if (adButton != null) adButton.onClick.AddListener(OnAds);
            if (offerButton != null) offerButton.onClick.AddListener(OnOffer);
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
            RollGems(Time.unscaledDeltaTime);
            Punch(goldValue, ref _goldPunch, Time.unscaledDeltaTime);
            Punch(gemsValue, ref _gemPunch, Time.unscaledDeltaTime);
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
                if (diff < 0d || System.Math.Abs(diff) > System.Math.Max(1d, target * 0.35d))
                {
                    // The jump is also the only cash worth celebrating. Income arrives every second of
                    // the game; a pop on that would be a counter that never stops twitching.
                    if (diff > 0d) _goldPunch = counterPunchSeconds;
                    _shownCash = target;
                }
                else _shownCash += diff * (1d - System.Math.Exp(-9d * dt));
            }
            goldValue.text = NumberFormatter.Format(new BigDouble(_shownCash));
        }

        /// <summary>
        /// Gems used to snap from one integer to the next, which made buying a hundred of them look
        /// exactly like spending one. They roll now, the same way cash does — and because gems only ever
        /// move when the player did something, every rise is worth a pop.
        ///
        /// The text is written only when the whole number it shows actually changes, so a settled
        /// counter costs nothing per frame.
        /// </summary>
        private void RollGems(float dt)
        {
            if (_wallet == null || gemsValue == null) return;
            double target = _wallet.Gems;
            if (!_haveShownGems) { _shownGems = target; _haveShownGems = true; }
            else if (target < _shownGems) _shownGems = target;    // spending lands at once
            else _shownGems += (target - _shownGems) * (1d - System.Math.Exp(-gemRollSpeed * dt));

            long show = (long)(_shownGems + 0.5d);
            if (show == _writtenGems) return;
            _writtenGems = show;
            gemsValue.text = show.ToString();
        }

        /// <summary>A short rise-and-fall on a counter that just grew. Rests at exactly 1.</summary>
        private void Punch(TMP_Text label, ref float left, float dt)
        {
            if (left <= 0f || label == null) return;
            left -= dt;
            float s = left > 0f
                ? 1f + counterPunch * Mathf.Sin(Mathf.Clamp01(1f - left / counterPunchSeconds) * Mathf.PI)
                : 1f;
            label.transform.localScale = new Vector3(s, s, 1f);
            if (left <= 0f) left = 0f;
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
            if (contractTimerValue != null && _contract != null) contractTimerValue.text = ContractChip();

            RefreshOfferButton();

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
            // Not gated on "no boost running" any more. That gate existed because a second boost used to
            // wipe the first, so tapping this while a package ran destroyed the package. Boosts stack
            // now (BoostService.AddBoost), so locking the shortcut would only mean a player who bought
            // the 24-hour offer loses their three free charges for the day.
            bool ready = adScreen != null && adScreen.BoostReady;
            boostButton.interactable = ready;

            if (boostButtonImage != null)
                // uGUI's disabled tint latches onto the graphic; stamp the state's colour back on
                boostButtonImage.CrossFadeColor(ready ? Color.white : DimBoost, 0f, true, true);

            if (boostButtonTitle == null) return;
            // The label sits inside the button now, so it has to take the dim itself:
            // CrossFadeColor only paints the graphic it is called on, never the children.
            boostButtonTitle.color = ready ? Color.white : DimBoost;
            // While a boost runs, the headline is whatever is actually multiplying the income —
            // a store offer can set a different one, and the button must not claim the slot's.
            double mult = boosted ? _boost.ActiveMultiplier
                                  : (adScreen != null ? adScreen.BoostMultiplier : 2d);
            boostButtonTitle.text = string.Format(Loc.T("hud.gelir"),
                mult.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// The offer shortcut is the quiet half of the pop-up: the window itself interrupts once, and
        /// from then on this button is the only reminder the player gets. It is furniture rather than a
        /// notification, so it never switches itself off — paying for a pack clears the offer and arms
        /// the next one, and a button tied to that would blink out and back in the player's face at the
        /// exact moment they handed over money. Only the clock chip comes and goes, and only where
        /// there is genuinely no clock: an island with nothing left to sell.
        /// </summary>
        private void RefreshOfferButton()
        {
            if (offerButton == null) return;
            bool live = offerScreen != null && offerScreen.HasLiveOffer;
            if (offerTimerChip != null && offerTimerChip.activeSelf != live) offerTimerChip.SetActive(live);
            if (!live || offerTimerValue == null) return;

            // The contract clock counts minutes because a contract runs for minutes; an offer runs for
            // a day, and "1439:56" is not a number anyone reads as "a day left".
            long left = offerScreen.SecondsLeft();
            offerTimerValue.text = left >= 3600L
                ? (left / 3600L) + ":" + (left / 60L % 60L).ToString("00")
                : ContractUI.ClockText(left);
        }

        /// <summary>
        /// The line under the contract button. It has to answer "is there anything at the port right
        /// now?" in one glance, so the states that want the player are words — READY to claim, READY to
        /// pick — and the states that do not are just a clock: the running job's, or the countdown to the
        /// next ship.
        /// </summary>
        private string ContractChip()
        {
            switch (_contract.State)
            {
                case ContractService.PortState.Reward:
                case ContractService.PortState.Offering:
                    return Loc.T("ortak.hazir");
                case ContractService.PortState.Active:
                    return ContractUI.ClockText(_contract.SecondsLeft);
                case ContractService.PortState.Away:
                    return ContractUI.ClockText(_contract.SecondsToShip);
                default:
                    return Loc.T("kontrat.gemi_kisa");
            }
        }

        private static readonly Color DimBoost = new Color(0.55f, 0.58f, 0.66f, 1f);

        // ---- what the tutorial points at -------------------------------------------------------
        // Read-only rects, so the onboarding can cut a hole over a real control instead of drawing a
        // copy of it somewhere and hoping the two stay in the same place. Nothing here can move a
        // button; the screen the player taps is still this one's.
        public RectTransform UpgradeRect => Rect(upgradeButton);
        public RectTransform ContractRect => Rect(contractButton);
        public RectTransform BoostRect => Rect(boostButton);
        public RectTransform DailyRect => Rect(dailyButton);
        public RectTransform MapRect => Rect(mapButton);
        public RectTransform PrestigeRect => Rect(prestigeButton);
        public RectTransform GoldRect => Rect(goldButton);
        public RectTransform SettingsRect => Rect(settingsButton);
        public RectTransform StoreRect => Rect(storeButton);
        public RectTransform AdRect => Rect(adButton);
        public RectTransform OfferRect => Rect(offerButton);
        /// <summary>The $/min pill, not the label inside it — the highlight has to sit on the art.</summary>
        public RectTransform RateRect
        {
            get
            {
                if (rateValue == null) return null;
                var parent = rateValue.transform.parent as RectTransform;
                return parent != null ? parent : (RectTransform)rateValue.transform;
            }
        }

        /// <summary>Whether the ×2 shortcut has a charge — the tip about it waits for this.</summary>
        public bool BoostReady => adScreen != null && adScreen.BoostReady;

        private static RectTransform Rect(Button b) => b != null ? (RectTransform)b.transform : null;

        /// <summary>The number itself rolls in <see cref="RollGems"/>; this only notices that it went up.</summary>
        private void RefreshGems()
        {
            if (_wallet == null || !_haveShownGems) return;
            if (_wallet.Gems > _shownGems) _gemPunch = counterPunchSeconds;
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
            if (stationScreen != null) stationScreen.Open();
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

        private void OnOffer()
        {
            if (offerScreen != null) offerScreen.Open();
        }

        /// <summary>Straight to the ad — the shortcut exists precisely to skip opening the ad screen.</summary>
        private void OnBoost()
        {
            if (adScreen != null) adScreen.WatchBoost();
        }
    }
}
