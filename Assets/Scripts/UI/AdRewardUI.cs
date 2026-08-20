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
    /// The free-rewards screen (GDD §10, Figma "ekran_reklam"): a short list of rewarded-ad slots, each
    /// with a few charges a day and a cooldown between watches. Editor-authored — the hierarchy lives in
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
    // This screen has a deliberate landscape composition; it must be placed before LetterboxRoot
    // measures the portrait-authored panel.
    [DefaultExecutionOrder(-110)]
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
        [Tooltip("Panel dışındaki karartma. Dokununca ekranı kapatır.")]
        [SerializeField] private Image dimmer;

        [Header("Yatay yerleşim")]
        [SerializeField] private RectTransform layoutPanel;
        [SerializeField] private RectTransform title;
        [SerializeField] private RectTransform description;
        [Tooltip("Ayarlar ekranında kullanılan başlık şeridi.")]
        [SerializeField] private Sprite titleRibbonSprite;

        [Header("Yuvalar")]
        [SerializeField] private List<Slot> slots = new List<Slot>();

        [Tooltip("Bekleme sayaçları akarken ekranın yenilenme aralığı (saniye).")]
        [SerializeField] private float refreshInterval = 0.25f;

        private FreeRewardService _free;
        private WalletService _wallet;
        private BoostService _boost;
        private IAdService _ad;
        private SaveService _save;
        private SaveData _data;
        private WorldIslands _world;
        private CoalOperation _op;
        private float _timer;
        private RectTransform _titleRibbon;

        private void Awake()
        {
            ApplyLandscapeLayout();
        }

        private void Start()
        {
            _free = ServiceLocator.Get<FreeRewardService>();
            _wallet = ServiceLocator.Get<WalletService>();
            _boost = ServiceLocator.Get<BoostService>();
            _ad = ServiceLocator.Get<IAdService>();
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

        /// <summary>
        /// Keeps every reward row at its authored size so the controls inside do not distort, then
        /// places the three rows side by side inside one centred, wide panel.
        /// </summary>
        private void ApplyLandscapeLayout()
        {
            if (Screen.width <= Screen.height || layoutPanel == null) return;

            // Wide and deliberately shallow: the previous 980-high shell left almost a third of the
            // landscape card empty below the rewards.
            SetRect(layoutPanel, Vector2.zero, new Vector2(2500f, 760f));
            BuildLandscapeRibbon();
            SetRect(description, new Vector2(0f, 145f), new Vector2(1100f, 62f));

            float[] x = { -830f, 0f, 830f };
            int count = Mathf.Min(slots.Count, x.Length);
            for (int i = 0; i < count; i++)
            {
                Slot slot = slots[i];
                if (slot == null || slot.background == null) continue;
                RectTransform row = slot.background.rectTransform;
                SetRect(row, new Vector2(x[i], -70f), new Vector2(916f, 360f));
                row.localScale = Vector3.one * 0.88f;
            }

            SetRect(closeButton != null ? closeButton.transform as RectTransform : null,
                    new Vector2(1150f, 300f), new Vector2(120f, 120f));
            if (closeButton != null) closeButton.transform.SetAsLastSibling();
        }

        private void BuildLandscapeRibbon()
        {
            if (_titleRibbon == null)
            {
                var go = new GameObject("BaslikSeridi", typeof(RectTransform), typeof(Image));
                _titleRibbon = (RectTransform)go.transform;
                _titleRibbon.SetParent(layoutPanel, false);
                var image = go.GetComponent<Image>();
                image.sprite = titleRibbonSprite;
                image.type = Image.Type.Simple;
                image.raycastTarget = false;
            }

            SetRect(_titleRibbon, new Vector2(0f, 300f), new Vector2(980f, 230f));
            if (title == null) return;
            if (title.parent != _titleRibbon) title.SetParent(_titleRibbon, false);
            SetRect(title, new Vector2(0f, -6f), new Vector2(600f, 120f));
        }

        private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
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
            switch (slot.kind)
            {
                case RewardKind.Gems:
                    return string.Format(Loc.T("reklam.elmas"), slot.gems);
                case RewardKind.IncomeMinutes:
                    return "+$" + NumberFormatter.Format(new BigDouble(IncomePerMinute() * slot.incomeMinutes));
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
            if (_free == null || slot == null) return;
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
                case RewardKind.IncomeMinutes:
                    if (_wallet != null) _wallet.AddCash(new BigDouble(IncomePerMinute() * slot.incomeMinutes));
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
                var ops = FindObjectsByType<CoalOperation>(FindObjectsSortMode.None);
                for (int i = 0; i < ops.Length; i++) if (ops[i].enabled) { _op = ops[i]; break; }
            }
            return _op != null ? _op.CashPerMinute : 0d;
        }
    }
}
