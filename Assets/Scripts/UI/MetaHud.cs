using Game.Core;
using Game.Gameplay;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The layer that keeps something asking to be done on screen at all times: a rolling delivery
    /// contract across the top, and a column of action chips down the right — the daily reward, a 2×
    /// income boost, and the prestige button once it is available.
    ///
    /// All four systems already existed and none of them had any UI, so the player could not reach them:
    /// offline earnings were granted silently, the daily gems were never claimable, the boost multiplier
    /// was never set by anything, and prestige could not be triggered at all. This is the front end for
    /// <see cref="ContractService"/>, <see cref="DailyRewardService"/>, <see cref="BoostService"/> and
    /// <see cref="PrestigeService"/>.
    ///
    /// Sits at sorting order 98 — above the floating station badges and the coin-fly juice, below the
    /// HUD (100) so an opened upgrade panel covers it, and well below the world map (150).
    /// </summary>
    public sealed class MetaHud : MonoBehaviour
    {
        [SerializeField] private float refreshInterval = 0.15f;
        [SerializeField] private float boostMultiplier = 2f;   // the "2×" the button promises
        [SerializeField] private float boostSeconds = 30f;
        [SerializeField] private long boostGemCost = 1;      // gems, until a rewarded-ad SDK is wired in

        private WalletService _wallet;
        private BoostService _boost;
        private DailyRewardService _daily;
        private PrestigeService _prestige;
        private ContractService _contract;
        private WorldIslands _world;
        private PrestigeUI _prestigeUI;

        private Text _contractLabel, _contractTimer, _contractBtnText, _dailyLabel, _boostLabel, _prestigeLabel;
        private RectTransform _contractFill;
        private Image _contractBg, _dailyBg, _boostBg, _prestigeBg;
        private Button _contractBtn, _dailyBtn, _boostBtn, _prestigeBtn;
        private GameObject _prestigeChip;
        private float _timer;

        private static readonly Color Card = new Color(0.11f, 0.15f, 0.22f, 0.96f);
        private static readonly Color Track = new Color(0.06f, 0.09f, 0.13f, 1f);
        private static readonly Color Amber = new Color(0.92f, 0.66f, 0.18f, 1f);
        private static readonly Color Green = new Color(0.22f, 0.66f, 0.36f, 1f);
        private static readonly Color Grey = new Color(0.24f, 0.27f, 0.32f, 1f);
        private static readonly Color Violet = new Color(0.50f, 0.32f, 0.72f, 1f);
        private static readonly Color Dim = new Color(0.58f, 0.66f, 0.76f, 1f);

        private void Start()
        {
            _wallet = ServiceLocator.Get<WalletService>();
            _boost = ServiceLocator.Get<BoostService>();
            _daily = ServiceLocator.Get<DailyRewardService>();
            _prestige = ServiceLocator.Get<PrestigeService>();
            _contract = ServiceLocator.Get<ContractService>();
            _world = FindAnyObjectByType<WorldIslands>();
            _prestigeUI = FindAnyObjectByType<PrestigeUI>();
            Build();
            Refresh();
        }

        private void Update()
        {
            if (_contract != null) _contract.Tick(Time.deltaTime, IncomePerMinute());
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = refreshInterval;
            Refresh();
        }

        /// <summary>
        /// What the empire earns a minute, used to size the next contract. Falls back to the active
        /// island alone if the world manager is missing, so this still works on a bare scene.
        /// </summary>
        private double IncomePerMinute()
        {
            if (_world != null)
            {
                double sum = 0d;
                for (int i = 0; i < _world.Count; i++) if (_world.IsOwned(i)) sum += _world.RatePerMin(i);
                if (sum > 0d) return sum;
            }
            var op = FindAnyObjectByType<CoalOperation>();
            return op != null ? op.CashPerMinute : 0d;
        }

        private void Build()
        {
            RectTransform root = UiBuild.Canvas(transform, "MetaHudCanvas", 98);

            // Dark card, not the skin's panel art: the kit panel is near-white, and every label in this
            // HUD is white. Sits below the dev TEST MODE button rather than across it.
            RectTransform card = UiBuild.Flat(root, "Contract", Card, new Vector2(0.03f, 0.772f), new Vector2(0.97f, 0.840f));
            _contractBtn = UiBuild.Btn(card, "ClaimBtn", "", UiSkin.ButtonGreen, Green, 30, OnClaimContract);
            UiBuild.Anchor(_contractBtn.GetComponent<RectTransform>(), new Vector2(0.74f, 0.12f), new Vector2(0.985f, 0.88f));
            _contractBg = _contractBtn.GetComponent<Image>();
            _contractBtnText = _contractBtn.GetComponentInChildren<Text>();

            _contractLabel = UiBuild.Label(card, "Goal", "", 25, TextAnchor.UpperLeft);
            UiBuild.Anchor(_contractLabel.rectTransform, new Vector2(0.025f, 0.46f), new Vector2(0.73f, 0.98f));

            UiBuild.Bar(card, "Track", Track, Amber,
                        new Vector2(0.025f, 0.16f), new Vector2(0.73f, 0.40f), out _contractFill);

            _contractTimer = UiBuild.Label(card, "Timer", "", 23, TextAnchor.LowerRight);
            UiBuild.Anchor(_contractTimer.rectTransform, new Vector2(0.025f, 0.44f), new Vector2(0.73f, 0.98f));
            _contractTimer.color = Dim;

            // ---- action chips down the right edge, above the shop button ----
            // Stacked upward from just above the SHOP button, which owns the right edge around y=0.50.
            _dailyBtn = Chip(root, "Daily", 0.700f, out _dailyLabel, out _dailyBg, OnClaimDaily);
            _boostBtn = Chip(root, "Boost", 0.634f, out _boostLabel, out _boostBg, OnBoost);
            _prestigeBtn = Chip(root, "Prestige", 0.568f, out _prestigeLabel, out _prestigeBg, OnPrestige);
            _prestigeChip = _prestigeBtn.gameObject;
            _prestigeChip.SetActive(false);   // hidden until the run is actually worth cashing in
        }

        /// <summary>One right-edge action chip. <paramref name="bottom"/> is its lower edge in screen fractions.</summary>
        private Button Chip(RectTransform root, string name, float bottom, out Text label, out Image bg,
                            UnityEngine.Events.UnityAction onClick)
        {
            Button b = UiBuild.Btn(root, name, "", UiSkin.ButtonGrey, Grey, 24, onClick);
            UiBuild.Anchor(b.GetComponent<RectTransform>(), new Vector2(0.775f, bottom), new Vector2(0.97f, bottom + 0.058f));
            bg = b.GetComponent<Image>();
            label = b.GetComponentInChildren<Text>();
            label.fontSize = 24;
            return b;
        }

        private void Refresh()
        {
            RefreshContract();
            RefreshDaily();
            RefreshBoost();
            RefreshPrestige();
        }

        private void RefreshContract()
        {
            if (_contract == null) return;
            bool won = _contract.Claimable;
            string streak = _contract.Streak > 0 ? "  ×" + _contract.Streak : "";
            if (won)
            {
                string gems = _contract.RewardGems > 0 ? "   +" + _contract.RewardGems + " gems" : "";
                _contractLabel.text = "CONTRACT COMPLETE" + streak +
                                      "\n+$" + NumberFormatter.Format(_contract.Reward) + gems;
            }
            else
            {
                _contractLabel.text = "CONTRACT" + streak + "   deliver $" + NumberFormatter.Format(_contract.Target) +
                                      "\n$" + NumberFormatter.Format(_contract.Earned) + " so far";
            }
            _contractFill.anchorMax = new Vector2((float)_contract.Progress01, 1f);
            _contractTimer.text = won ? "" : UiBuild.Clock(_contract.SecondsLeft);
            _contractBtn.interactable = won;
            _contractBg.sprite = won ? UiSkin.ButtonGreen : UiSkin.ButtonGrey;
            if (!UiSkin.HasArt) _contractBg.color = won ? Green : Grey;
            _contractBtnText.text = won ? "CLAIM" : "IN PROGRESS";
        }

        private void RefreshDaily()
        {
            if (_daily == null) return;
            bool can = _daily.CanClaim();
            _dailyLabel.text = can ? "DAILY\n+" + _daily.RewardGems + " GEMS" : "DAILY\nCLAIMED";
            _dailyBtn.interactable = can;
            _dailyBg.sprite = can ? UiSkin.ButtonGreen : UiSkin.ButtonGrey;
            if (!UiSkin.HasArt) _dailyBg.color = can ? Green : Grey;
        }

        private void RefreshBoost()
        {
            if (_boost == null) return;
            if (_boost.IsActive)
            {
                _boostLabel.text = "BOOST\n" + UiBuild.Clock(_boost.SecondsLeft);
                _boostBtn.interactable = false;
                _boostBg.sprite = UiSkin.ButtonBlue;
                if (!UiSkin.HasArt) _boostBg.color = Amber;
                return;
            }
            bool afford = _wallet != null && _wallet.Gems >= boostGemCost;
            _boostLabel.text = "2× INCOME\n" + boostGemCost + " GEM" + (boostGemCost == 1 ? "" : "S");
            _boostBtn.interactable = afford;
            _boostBg.sprite = afford ? UiSkin.ButtonYellow : UiSkin.ButtonGrey;
            if (!UiSkin.HasArt) _boostBg.color = afford ? Amber : Grey;
        }

        private void RefreshPrestige()
        {
            if (_prestige == null) return;
            bool can = _prestige.CanPrestige();
            if (_prestigeChip.activeSelf != can) _prestigeChip.SetActive(can);
            if (!can) return;
            _prestigeLabel.text = "PRESTIGE\n+" + NumberFormatter.Format(_prestige.PendingInvestors());
            _prestigeBg.sprite = UiSkin.ButtonYellow;
            if (!UiSkin.HasArt) _prestigeBg.color = Violet;
        }

        private void OnClaimContract()
        {
            if (_contract == null) return;
            _contract.Claim(IncomePerMinute());
            Refresh();
        }

        private void OnClaimDaily()
        {
            if (_daily == null || _wallet == null) return;
            _daily.Claim(_wallet);
            Refresh();
        }

        private void OnBoost()
        {
            if (_boost == null || _wallet == null || _boost.IsActive) return;
            // Gems stand in for the rewarded ad until an ad SDK is installed; the cost is the gate either
            // way, so swapping in IAdService later only changes what happens on this line.
            if (!_wallet.TrySpendGems(boostGemCost)) return;
            _boost.SetBoost(boostMultiplier, boostSeconds);
            Refresh();
        }

        private void OnPrestige()
        {
            if (_prestigeUI == null) _prestigeUI = FindAnyObjectByType<PrestigeUI>();
            if (_prestigeUI != null) _prestigeUI.Open();
        }
    }
}
