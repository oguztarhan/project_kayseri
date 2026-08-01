using System;
using System.Collections.Generic;
using Game.Core;
using Game.Gameplay;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The free-rewards screen (GDD §10, Figma "ekran_reklam"): a short list of rewarded-ad slots, each
    /// with a few charges a day and a cooldown between watches, plus the remove-ads upsell along the
    /// bottom. Editor-authored — the hierarchy lives in the UI_Reklam prefab and every reference below is
    /// wired in the Inspector, so rows can be added, reordered or retuned without touching code.
    ///
    /// What each slot pays is deliberately a serialized field rather than a constant: these numbers are
    /// the economy's pressure valve and belong to whoever is balancing the game, not to this file.
    /// <see cref="FreeRewardService"/> owns only the state (charges spent, cooldown), which has to
    /// persist; everything a designer would want to change lives here.
    /// </summary>
    public sealed class AdRewardUI : MonoBehaviour
    {
        /// <summary>What a slot pays out. Exactly one of the three is normally non-zero.</summary>
        public enum RewardKind { Gems, IncomeMinutes, Boost }

        [Serializable]
        public sealed class Slot
        {
            [Tooltip("Kayıtta bu yuvayı tanımlayan sabit kimlik. Değiştirirsen haklar sıfırlanır.")]
            public string id = "elmas";
            public RewardKind kind = RewardKind.Gems;

            [Header("Ödül")]
            public long gems = 5;
            [Tooltip("Kaç dakikalık gelir verilecek.")]
            public float incomeMinutes = 15f;
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

        [Header("Yuvalar")]
        [SerializeField] private List<Slot> slots = new List<Slot>();

        [Header("Reklamsız şeridi")]
        [SerializeField] private Button removeAdsButton;
        [SerializeField] private TMP_Text removeAdsLabel;
        [SerializeField] private TMP_Text removeAdsPrice;
        [SerializeField] private string removeAdsSku = "noads";
        [Tooltip("Editör testi: IAP stub'ı her satın almayı reddettiği için, bu açıkken satın alma bedava geçer. Cihaz sürümünde yok sayılır.")]
        [SerializeField] private bool devFreeIAP;

        [Tooltip("Bekleme sayaçları akarken ekranın yenilenme aralığı (saniye).")]
        [SerializeField] private float refreshInterval = 0.25f;

        private FreeRewardService _free;
        private WalletService _wallet;
        private BoostService _boost;
        private IAdService _ad;
        private IIAPService _iap;
        private SaveService _save;
        private SaveData _data;
        private WorldIslands _world;
        private CoalOperation _op;
        private float _timer;

        private void Start()
        {
            _free = ServiceLocator.Get<FreeRewardService>();
            _wallet = ServiceLocator.Get<WalletService>();
            _boost = ServiceLocator.Get<BoostService>();
            _ad = ServiceLocator.Get<IAdService>();
            _iap = ServiceLocator.Get<IIAPService>();
            _save = ServiceLocator.Get<SaveService>();
            _data = ServiceLocator.Get<SaveData>();
            _world = FindAnyObjectByType<WorldIslands>();

            for (int i = 0; i < slots.Count; i++)
            {
                Slot slot = slots[i];
                if (slot == null || slot.watchButton == null) continue;
                Slot captured = slot;
                slot.watchButton.onClick.AddListener(() => Watch(captured));
            }
            if (closeButton != null) closeButton.onClick.AddListener(Hide);
            if (removeAdsButton != null) removeAdsButton.onClick.AddListener(BuyRemoveAds);

            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void Update()
        {
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

                int charges = Charges(slot);
                int left = _free.ChargesLeft(slot.id, charges);
                float cooldown = _free.CooldownLeft(slot.id, slot.cooldownSeconds);
                bool ready = left > 0 && cooldown <= 0f;

                if (slot.background != null && slot.backgroundReady != null && slot.backgroundSpent != null)
                    slot.background.sprite = ready ? slot.backgroundReady : slot.backgroundSpent;

                if (slot.charges != null)
                    for (int c = 0; c < slot.charges.Length; c++)
                    {
                        if (slot.charges[c] == null) continue;
                        // A row with fewer charges than dots hides the spares rather than drawing a
                        // limit the slot does not actually have.
                        bool exists = c < charges;
                        slot.charges[c].gameObject.SetActive(exists);
                        if (exists) slot.charges[c].sprite = c < left ? slot.chargeFull : slot.chargeEmpty;
                    }

                if (slot.label != null) slot.label.text = LabelFor(slot, left, cooldown);

                if (slot.watchButton != null) slot.watchButton.interactable = ready;
                if (slot.watchImage != null && slot.watchReady != null && slot.watchWaiting != null)
                {
                    slot.watchImage.sprite = ready ? slot.watchReady : slot.watchWaiting;
                    // uGUI's disabled tint latches onto the graphic; force the right colour back on.
                    slot.watchImage.CrossFadeColor(Color.white, 0f, true, true);
                }
            }

            bool owned = _free.AdsRemoved;
            if (removeAdsButton != null) removeAdsButton.interactable = !owned;
            if (removeAdsLabel != null) removeAdsLabel.text = "REKLAMLARI KALDIR";
            if (removeAdsPrice != null) removeAdsPrice.text = owned ? "SENİN" : "SATIN AL";
        }

        /// <summary>
        /// The row's one line of text: what it pays while it can be watched, why it cannot otherwise.
        /// The cooldown reads as a clock and the day limit as a plain sentence, so the two "not now"
        /// states are never mistaken for each other.
        /// </summary>
        private string LabelFor(Slot slot, int chargesLeft, float cooldown)
        {
            if (chargesLeft <= 0) return "YARIN TEKRAR GEL";
            if (cooldown > 0f) return ContractUI.ClockText(cooldown) + " SONRA";
            switch (slot.kind)
            {
                case RewardKind.Gems:
                    return "+" + slot.gems + " ELMAS";
                case RewardKind.IncomeMinutes:
                    return "+$" + NumberFormatter.Format(new BigDouble(IncomePerMinute() * slot.incomeMinutes));
                default:
                    return "×" + slot.boostMultiplier.ToString("0.#",
                        System.Globalization.CultureInfo.InvariantCulture) + " GELİR · " + Minutes(slot.boostSeconds);
            }
        }

        private static string Minutes(float seconds)
        {
            int m = Mathf.RoundToInt(seconds / 60f);
            return m > 0 ? m + " DK" : Mathf.RoundToInt(seconds) + " SN";
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

        private void Watch(Slot slot)
        {
            if (_free == null || slot == null) return;
            if (!_free.CanWatch(slot.id, Charges(slot), slot.cooldownSeconds)) return;
            if (_ad == null || !_ad.Available) return;

            _ad.ShowRewarded(() =>
            {
                switch (slot.kind)
                {
                    case RewardKind.Gems:
                        if (_wallet != null) _wallet.AddGems(slot.gems);
                        break;
                    case RewardKind.IncomeMinutes:
                        if (_wallet != null) _wallet.AddCash(new BigDouble(IncomePerMinute() * slot.incomeMinutes));
                        break;
                    default:
                        if (_boost != null) _boost.SetBoost(slot.boostMultiplier, slot.boostSeconds);
                        break;
                }
                _free.Consume(slot.id);
                // Charges are the thing a player would reload the app to get back; write them now.
                if (_save != null && _data != null) _save.Save(_data);
                Refresh();
            });
        }

        private void BuyRemoveAds()
        {
            if (_free == null || _free.AdsRemoved) return;
            Action<bool> done = ok =>
            {
                if (!ok) return;
                _free.AdsRemoved = true;
                if (_save != null && _data != null) _save.Save(_data);
                Refresh();
            };
            if (devFreeIAP && (Application.isEditor || Debug.isDebugBuild)) { done(true); return; }
            if (_iap != null) _iap.Purchase(removeAdsSku, done);
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
                var ops = FindObjectsByType<CoalOperation>(FindObjectsSortMode.None);
                for (int i = 0; i < ops.Length; i++) if (ops[i].enabled) { _op = ops[i]; break; }
            }
            return _op != null ? _op.CashPerMinute : 0d;
        }
    }
}
