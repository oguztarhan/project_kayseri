using System;
using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The free-rewards screen (GDD §10, Figma "ekran_reklam"): daily gem charges and a recurring
    /// cash offer share the same panel. Editor-authored — the hierarchy lives in
    /// the UI_Reklam prefab and every reference below is wired in the Inspector, so rows can be added,
    /// reordered or retuned without touching code.
    ///
    /// The remove-ads upsell used to sit along the bottom and is gone: a screen the player opened to be
    /// given something should not end in a price tag. <see cref="PremiumStoreUI"/> still sells it.
    ///
    /// What each slot pays is deliberately a serialized field rather than a constant: these numbers are
    /// the economy's pressure valve and belong to whoever is balancing the game, not to this file.
    /// <see cref="FreeRewardService"/> owns only the state (charges spent, cooldown), which has to
    /// persist; everything a designer would want to change lives here.
    /// </summary>
    public sealed class AdRewardUI : MonoBehaviour
    {
        /// <summary>Which reward flow a prefab row represents.</summary>
        public enum RewardKind { Gems, RecurringCash, Boost }

        [Serializable]
        public sealed class Slot
        {
            [Tooltip("Kayıtta bu yuvayı tanımlayan sabit kimlik. Değiştirirsen haklar sıfırlanır.")]
            public string id = "elmas";
            public RewardKind kind = RewardKind.Gems;

            [Header("Ödül")]
            public long gems = 5;
            public double boostMultiplier = 2d;
            public float boostSeconds = 300f;

            [Header("Limitler")]
            [Min(1)] public int chargesPerDay = 3;
            [Tooltip("İki izleme arasındaki bekleme (saniye).")]
            [Min(0f)] public float cooldownSeconds = 300f;

            [Header("Sahnedeki parçalar")]
            public Image background;
            public Sprite backgroundReady;      // satir_reklam_bos
            public Sprite backgroundSpent;      // satir_bekleme_bos
            public TMP_Text label;
            public Image[] charges;             // soldan sağa haklar
            public Sprite chargeFull;
            public Sprite chargeEmpty;
            public Button watchButton;
            public Image watchImage;
            public Sprite watchReady;           // btn_izle
            public Sprite watchWaiting;         // btn_bekleme
        }

        [Header("Panel (UI_Reklam prefabında bağlı)")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Button closeButton;
        [Tooltip("Panel dışındaki karartma. Dokununca ekranı kapatır.")]
        [SerializeField] private Image dimmer;

        [Header("Yuvalar")]
        [SerializeField] private List<Slot> slots = new List<Slot>();

        [Header("Tekrarlanan nakit ödülü")]
        [SerializeField, Min(1f)] private float cashCooldownSeconds = 300f;
        [Tooltip("Ödülün reklamsız bir dakikalık toplam gelire oranı.")]
        [SerializeField, Min(0f)] private float cashIncomeMinutes = 1f;

        [Tooltip("Bekleme sayaçları akarken ekranın yenilenme aralığı (saniye).")]
        [SerializeField] private float refreshInterval = 0.25f;

        private FreeRewardService _free;
        private WalletService _wallet;
        private BoostService _boost;
        private IAdService _ad;
        private SaveService _save;
        private SaveData _data;
        private WorldIslands _world;
        private HudUI _hud;
        private CoalOperation _op;
        private float _timer;
        private bool _cashAdInFlight;

        private void Start()
        {
            _free = ServiceLocator.Get<FreeRewardService>();
            _wallet = ServiceLocator.Get<WalletService>();
            _boost = ServiceLocator.Get<BoostService>();
            _ad = ServiceLocator.Get<IAdService>();
            _save = ServiceLocator.Get<SaveService>();
            _data = ServiceLocator.Get<SaveData>();
            _world = FindAnyObjectByType<WorldIslands>();
            _hud = FindAnyObjectByType<HudUI>();

            for (int i = 0; i < slots.Count; i++)
            {
                Slot slot = slots[i];
                if (slot == null || slot.watchButton == null) continue;
                Slot captured = slot;
                slot.watchButton.onClick.AddListener(() => Watch(captured));
                if (slot.kind == RewardKind.RecurringCash && slot.label != null)
                {
                    slot.label.enableAutoSizing = true;
                    slot.label.fontSizeMin = 18f;
                    slot.label.fontSizeMax = slot.label.fontSize;
                }
            }
            if (closeButton != null) closeButton.onClick.AddListener(Hide);
            if (dimmer != null)
            {
                Button dimButton = dimmer.GetComponent<Button>();
                if (dimButton == null) dimButton = dimmer.gameObject.AddComponent<Button>();
                dimButton.transition = Selectable.Transition.None;
                dimButton.onClick.AddListener(Hide);
            }

            if (panelRoot != null) panelRoot.SetActive(false);
            UiPanelSound.Attach(panelRoot);   // panel kapatıldıktan SONRA — açılış sesi boot'ta çalmasın
        }

        private void Update()
        {
            if (_free == null) _free = ServiceLocator.Get<FreeRewardService>();
            if (_wallet == null) _wallet = ServiceLocator.Get<WalletService>();
            if (_ad == null) _ad = ServiceLocator.Get<IAdService>();
            if (_save == null) _save = ServiceLocator.Get<SaveService>();
            if (_data == null) _data = ServiceLocator.Get<SaveData>();
            if (panelRoot == null || !panelRoot.activeSelf) return;
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = refreshInterval;
            Refresh();
        }

        public void Toggle()
        {
            if (panelRoot == null) return;
            if (panelRoot.activeSelf) { Hide(); return; }
            Open();
        }

        public void Open()
        {
            if (panelRoot == null) return;
            Refresh();
            panelRoot.SetActive(true);
        }

        public void Hide()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void Refresh()
        {
            if (_free == null) return;

            for (int i = 0; i < slots.Count; i++)
            {
                Slot slot = slots[i];
                if (slot == null) continue;

                bool cash = slot.kind == RewardKind.RecurringCash;
                int charges = cash ? 0 : Charges(slot);
                int left = cash ? 0 : _free.ChargesLeft(slot.id, charges);
                float cooldown = cash ? CashCooldownLeft : _free.CooldownLeft(slot.id, slot.cooldownSeconds);
                // AdReady de şart: hak ve bekleme uygunken bile yüklü reklam yoksa Watch() sessizce
                // geri dönüyordu, yani düğme etkin görünüp hiçbir şey yapmıyordu. HUD kısayolu
                // (BoostReady) bu kontrolü zaten yapıyordu; satırların yapmaması bir gözden kaçmaydı.
                bool ready = (cash ? CashReady && !_cashAdInFlight : left > 0 && cooldown <= 0f)
                             && AdReady;

                if (slot.background != null && slot.backgroundReady != null && slot.backgroundSpent != null)
                    slot.background.sprite = ready ? slot.backgroundReady : slot.backgroundSpent;

                if (!cash && slot.charges != null)
                    for (int c = 0; c < slot.charges.Length; c++)
                    {
                        if (slot.charges[c] == null) continue;
                        // A row with fewer charges than dots hides the spares rather than drawing a
                        // limit the slot does not actually have.
                        bool exists = c < charges;
                        slot.charges[c].gameObject.SetActive(exists);
                        if (exists) slot.charges[c].sprite = c < left ? slot.chargeFull : slot.chargeEmpty;
                    }

                if (slot.label != null) slot.label.text = cash ? CashLabel(cooldown) : LabelFor(slot, left, cooldown);

                if (slot.watchButton != null) slot.watchButton.interactable = ready;
                if (slot.watchImage != null && slot.watchReady != null && slot.watchWaiting != null)
                {
                    slot.watchImage.sprite = ready ? slot.watchReady : slot.watchWaiting;
                    // uGUI's disabled tint latches onto the graphic; force the right colour back on.
                    slot.watchImage.CrossFadeColor(Color.white, 0f, true, true);
                }
            }
        }

        /// <summary>
        /// The row's one line of text: what it pays while it can be watched, why it cannot otherwise.
        /// The cooldown reads as a clock and the day limit as a plain sentence, so the two "not now"
        /// states are never mistaken for each other.
        /// </summary>
        private string LabelFor(Slot slot, int chargesLeft, float cooldown)
        {
            if (chargesLeft <= 0) return Loc.T("reklam.yarin_gel");
            if (cooldown > 0f) return string.Format(Loc.T("reklam.sonra"), ContractUI.ClockText(cooldown));
            // Oyuncunun kendi durumundan kaynaklanan iki hâl önce gelir; bu üçüncüsü geçicidir ve
            // reklam yüklenir yüklenmez kendiliğinden kalkar (Refresh, refreshInterval ile dönüyor).
            if (!AdReady) return Loc.T("reklam.hazir_degil");
            switch (slot.kind)
            {
                case RewardKind.Gems:
                    return string.Format(Loc.T("reklam.elmas"), slot.gems);
                default:
                    return string.Format(Loc.T("reklam.gelir"),
                        slot.boostMultiplier.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
                        Minutes((float)BoostService.RewardedAdSeconds));
            }
        }

        private static string Minutes(float seconds)
        {
            int m = Mathf.RoundToInt(seconds / 60f);
            return m > 0 ? string.Format(Loc.T("ortak.sure_dk"), m)
                         : string.Format(Loc.T("ortak.sure_sn"), Mathf.RoundToInt(seconds));
        }

        /// <summary>
        /// The slot's daily charges plus anything the store sold on top (the "Günlük Hazine" offer).
        /// Every read of the limit goes through here, or the row would draw four dots and only let the
        /// player spend three.
        /// </summary>
        private int Charges(Slot slot)
        {
            int extra = _data != null ? _data.freeRewardBonusCharges : 0;
            return slot.chargesPerDay + (extra > 0 ? extra : 0);
        }

        /// <summary>
        /// The boost slot, if the designer authored one. The HUD's shortcut button drives this rather
        /// than keeping its own charges and cooldown — two copies of the rules would disagree the
        /// moment either one is spent.
        /// </summary>
        private Slot BoostSlot()
        {
            for (int i = 0; i < slots.Count; i++)
                if (slots[i] != null && slots[i].kind == RewardKind.Boost) return slots[i];
            return null;
        }

        /// <summary>Charges left, cooldown expired and an ad actually loaded.</summary>
        public bool BoostReady
        {
            get
            {
                Slot s = BoostSlot();
                return s != null && _free != null && AdReady
                       && _free.CanWatch(s.id, Charges(s), s.cooldownSeconds);
            }
        }

        /// <summary>At least one visible reward has a remaining claim, even while an ad is loading.</summary>
        public bool HasAvailableReward
        {
            get
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    Slot slot = slots[i];
                    if (slot == null || slot.background == null || !slot.background.gameObject.activeSelf) continue;
                    if (slot.kind == RewardKind.RecurringCash)
                    {
                        if (CashReady) return true;
                    }
                    else if (_free != null && _free.CanWatch(slot.id, Charges(slot), slot.cooldownSeconds))
                        return true;
                }
                return false;
            }
        }

        /// <summary>Seconds until the boost is watchable again; 0 when it is ready or out for the day.</summary>
        public float BoostCooldown
        {
            get
            {
                Slot s = BoostSlot();
                return s != null && _free != null ? _free.CooldownLeft(s.id, s.cooldownSeconds) : 0f;
            }
        }

        /// <summary>What the boost pays, so a shortcut button can label itself: "×2" for a 2× slot.</summary>
        public double BoostMultiplier
        {
            get
            {
                Slot s = BoostSlot();
                return s != null ? s.boostMultiplier : 1d;
            }
        }

        /// <summary>Play the rewarded ad for the boost slot — the HUD shortcut, same rules as the row.</summary>
        public void WatchBoost()
        {
            Watch(BoostSlot());
        }

        /// <summary>
        /// Reklamın yerine geçen tek şey reklamsız paketidir. Satın alan oyuncu ödülü izlemeden alır;
        /// günlük hak ve bekleme yine geçerli, yoksa bu ekran sınırsız para basardı.
        /// </summary>
        private bool AdReady => (_free != null && _free.AdsRemoved) || (_ad != null && _ad.Available);

        private void Watch(Slot slot)
        {
            if (slot == null) return;
            if (slot.kind == RewardKind.RecurringCash) { WatchCash(); return; }
            if (_free == null) return;
            if (!_free.CanWatch(slot.id, Charges(slot), slot.cooldownSeconds)) return;

            if (_free.AdsRemoved) { Payout(slot); return; }
            if (_ad == null || !_ad.Available) return;

            _ad.ShowRewarded(() => Payout(slot));
        }

        private void Payout(Slot slot)
        {
            switch (slot.kind)
            {
                case RewardKind.Gems:
                    if (_wallet != null) _wallet.AddGems(slot.gems);
                    break;
                default:
                    if (_boost != null) _boost.AddRewardedAdBoost(slot.boostMultiplier);
                    break;
            }
            _free.Consume(slot.id);
            var audio = ServiceLocator.Get<AudioService>();
            if (audio != null) audio.Play(SoundId.Reward);
            var haptic = ServiceLocator.Get<HapticService>();
            if (haptic != null) haptic.Medium();
            // Charges are the thing a player would reload the app to get back; write them now.
            if (_save != null && _data != null) _save.Save(_data);
            Refresh();
        }

        /// <summary>Same fallback ladder the HUD and the daily screen use: whole empire if it exists.</summary>
        private double IncomePerMinute()
        {
            if (_world != null)
            {
                double sum = 0d;
                for (int i = 0; i < _world.Count; i++) if (_world.IsOwned(i)) sum += _world.RatePerMin(i);
                if (sum > 0d) return sum;
            }
            if (_op == null || !_op.enabled)
            {
                var ops = FindObjectsByType<CoalOperation>();
                for (int i = 0; i < ops.Length; i++) if (ops[i].enabled) { _op = ops[i]; break; }
            }
            return _op != null ? _op.CashPerMinute : 0d;
        }

        private bool CashReady => _data != null && _free != null && _wallet != null
                                  && _data.rewardedCashNextAvailableUnix <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private float CashCooldownLeft
        {
            get
            {
                if (_data == null) return 0f;
                long left = _data.rewardedCashNextAvailableUnix - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                return left > 0L ? left : 0f;
            }
        }

        private string CashLabel(float cooldown)
        {
            if (cooldown > 0f) return string.Format(Loc.T("reklam.sonra"), ContractUI.ClockText(cooldown));
            if (!AdReady || _cashAdInFlight) return Loc.T("reklam.hazir_degil");
            return "+$" + NumberFormatter.Format(CashAmount());
        }

        private BigDouble CashAmount()
        {
            double income = _hud != null ? _hud.CurrentUnboostedIncomePerMinute : 0d;
            if (income <= 0d) income = IncomePerMinute();
            if (income <= 0d && _data != null) income = _data.incomeRatePerSec * 60d;
            return new BigDouble(System.Math.Max(0d, income * cashIncomeMinutes));
        }

        private void WatchCash()
        {
            if (!CashReady || _cashAdInFlight || _wallet == null || _free == null) return;
            if (_free.AdsRemoved) { PayoutCash(); return; }
            if (_ad == null || !_ad.Available) return;
            _cashAdInFlight = true;
            Refresh();
            _ad.ShowRewarded(PayoutCash, OnCashAdWithoutReward);
        }

        private void PayoutCash()
        {
            if (!CashReady || _wallet == null || _data == null) return;
            BigDouble amount = CashAmount();
            _data.rewardedCashNextAvailableUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                                                   + Mathf.Max(1, Mathf.CeilToInt(cashCooldownSeconds));
            _cashAdInFlight = false;
            _wallet.AddCash(amount);
            _save?.Save(_data);
            ServiceLocator.Get<AudioService>()?.Play(SoundId.Reward);
            ServiceLocator.Get<HapticService>()?.Medium();
            Refresh();
        }

        private void OnCashAdWithoutReward()
        {
            _cashAdInFlight = false;
            Refresh();
        }
    }
}
