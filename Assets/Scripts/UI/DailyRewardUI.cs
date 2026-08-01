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
    /// The 7-day daily-reward screen (Figma "ekran_gunluk"): six day tiles in a 2×3 grid, the day-7
    /// chest card, and one claim pill. Editor-authored — the hierarchy lives in the UI_GunlukOdul
    /// prefab and every tile, sprite and label is wired below, so the whole screen is tunable from
    /// the Inspector, including what each day pays (<see cref="ladder"/>).
    ///
    /// <see cref="DailyRewardService"/> owns the streak state; this screen prices and grants the
    /// rewards, because "minutes of income" can only be valued scene-side.
    /// </summary>
    public sealed class DailyRewardUI : MonoBehaviour
    {
        /// <summary>One tile's scene references; sprite state is swapped on the single image.</summary>
        [Serializable]
        public sealed class DayTile
        {
            public Image card;
            [Tooltip("Karo sprite'ları ikonu sabit bastığı için ödül tipine göre üstüne doğru ikon binmeli.")]
            public Image icon;
            [Tooltip("Alınmış günün tiki. Karo sanatındaki tik madalyonun altında kaldığı için üste binen ayrı bir rozet.")]
            public GameObject doneBadge;
            public TMP_Text dayLabel;
            public TMP_Text valueLabel;
        }

        /// <summary>What a day pays. Gems are flat; income minutes are priced at claim time.</summary>
        [Serializable]
        public sealed class DayReward
        {
            public long gems;
            [Tooltip("Bu kadar dakikalık gelir kadar nakit verir (0 = nakit yok).")]
            public float incomeMinutes;
        }

        [Header("Panel (UI_GunlukOdul prefabında bağlı)")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button claimButton;
        [SerializeField] private TMP_Text claimLabel;

        [Header("Gün karoları (1..6) + 7. gün kartı")]
        [SerializeField] private List<DayTile> tiles = new List<DayTile>();
        [SerializeField] private TMP_Text heroValueLabel;
        [SerializeField] private Sprite tileLocked;    // kart_gun
        [SerializeField] private Sprite tileToday;     // kart_gun_bugun
        [SerializeField] private Sprite tileClaimed;   // kart_gun_alindi
        [SerializeField] private Sprite iconGem;       // ikon_elmas
        [SerializeField] private Sprite iconGold;      // ikon_altin
        [Tooltip("Alınmış günlerde ikonun soldurulmuş rengi.")]
        [SerializeField] private Color claimedIconTint = new Color(0.78f, 0.82f, 0.90f, 1f);

        [Header("Ödül merdiveni (7 gün)")]
        [SerializeField] private List<DayReward> ladder = new List<DayReward>();

        private DailyRewardService _daily;
        private WalletService _wallet;
        private HapticService _haptic;
        private WorldIslands _world;
        private CoalOperation _op;
        private SaveData _data;

        private void Start()
        {
            _daily = ServiceLocator.Get<DailyRewardService>();
            _wallet = ServiceLocator.Get<WalletService>();
            _haptic = ServiceLocator.Get<HapticService>();
            _data = ServiceLocator.Get<SaveData>();
            _world = FindAnyObjectByType<WorldIslands>();

            if (closeButton != null) closeButton.onClick.AddListener(Hide);
            if (claimButton != null) claimButton.onClick.AddListener(OnClaim);

            if (panelRoot != null) panelRoot.SetActive(false);
        }

        public void Toggle()
        {
            if (panelRoot == null) return;
            bool show = !panelRoot.activeSelf;
            panelRoot.SetActive(show);
            if (show) Refresh();
        }

        public void Hide()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void OnClaim()
        {
            if (_daily == null || _wallet == null) return;
            int day = _daily.Claim();
            if (day < 0) return;

            DayReward reward = day < ladder.Count ? ladder[day] : null;
            double mult = RewardMultiplier();
            long gems = Stipend();   // the store's permanent daily gems ride every claim, ladder or not
            if (reward != null)
            {
                gems += (long)(reward.gems * mult);
                if (reward.incomeMinutes > 0f)
                    _wallet.AddCash(new BigDouble(reward.incomeMinutes * mult * IncomePerMinute()));
            }
            if (gems > 0) _wallet.AddGems(gems);
            if (_haptic != null) _haptic.Light();
            Refresh();
        }

        /// <summary>Same fallback ladder the HUD uses: whole empire if the world manager exists.</summary>
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

        private void Refresh()
        {
            if (_daily == null) return;
            bool canClaim = _daily.CanClaim();
            int streak = _daily.EffectiveStreak;
            // bu döngüde alınmış gün sayısı; bugün alındıysa bugünü de kapsar
            int done = canClaim ? streak % DailyRewardService.CycleDays
                                : (streak > 0 ? (streak - 1) % DailyRewardService.CycleDays + 1 : 0);

            for (int i = 0; i < tiles.Count; i++)
            {
                DayTile t = tiles[i];
                if (t == null || t.card == null) continue;
                if (i < done) t.card.sprite = tileClaimed;
                else if (i == done && canClaim) t.card.sprite = tileToday;
                else t.card.sprite = tileLocked;

                if (t.icon != null)
                {
                    DayReward r = i < ladder.Count ? ladder[i] : null;
                    t.icon.sprite = r != null && r.gems > 0 ? iconGem : iconGold;
                    t.icon.color = i < done ? claimedIconTint : Color.white;
                }
                if (t.doneBadge != null && t.doneBadge.activeSelf != (i < done)) t.doneBadge.SetActive(i < done);
                if (t.dayLabel != null) t.dayLabel.text = (i + 1) + ". GÜN";
                if (t.valueLabel != null) t.valueLabel.text = ValueText(i);
            }
            if (heroValueLabel != null) heroValueLabel.text = ValueText(DailyRewardService.CycleDays - 1);

            if (claimButton != null)
            {
                claimButton.interactable = canClaim;
                // ColorTint geçişi, panel aynı karede açılıp durum değişince bayat kalabiliyor
                // (yarı saydam disabled beyazı takılı kalıyor) — doğru rengi anında bas.
                if (claimButton.targetGraphic != null)
                    claimButton.targetGraphic.CrossFadeColor(
                        canClaim ? Color.white : claimButton.colors.disabledColor, 0f, true, true);
            }
            if (claimLabel != null) claimLabel.text = canClaim ? "ÖDÜL AL" : "YARIN GEL";
        }

        /// <summary>
        /// Permanent store-bought multiplier on the daily reward (the "Günlük Hazine" offer). Both the
        /// claim and the tile labels run through it, or the ladder would advertise one number and pay
        /// another.
        /// </summary>
        private double RewardMultiplier() => _data != null ? 1d + _data.dailyRewardBonusMult : 1d;

        /// <summary>
        /// Flat gems the store sells on top of the ladder (the "Günlük Hazine" offer), paid on every
        /// claim. It is not multiplied: the card sells the stipend and the ×2 as two separate lines,
        /// so doubling this as well would hand out more than the card promises.
        /// </summary>
        private long Stipend() => _data != null ? _data.dailyGemStipend : 0L;

        private string ValueText(int day)
        {
            DayReward r = day < ladder.Count ? ladder[day] : null;
            if (r == null) return "";
            double mult = RewardMultiplier();
            // the tile prints what the claim will actually pay, so both perks have to be folded in here
            long gems = (long)(r.gems * mult) + Stipend();
            int minutes = Mathf.RoundToInt((float)(r.incomeMinutes * mult));
            if (gems > 0 && minutes > 0) return gems + " ELMAS + " + minutes + " DK";
            if (gems > 0) return gems + (day == DailyRewardService.CycleDays - 1 ? " ELMAS" : "");
            return minutes + " DK";
        }
    }
}
