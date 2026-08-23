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
    /// The pop-up offer (GDD §10). Three consumable packs — SEFER, VARDİYA, KASA — themed to the
    /// island the player is standing on, each buyable once per island. It owns three jobs that are
    /// only sane together: the catalogue, when a pop-up is allowed to interrupt, and the countdown
    /// the HUD button reads. Splitting them would mean three objects reading the same six save
    /// fields.
    ///
    /// Two decisions worth knowing before retuning anything:
    ///
    /// Every pack is priced in HOURS OF THE PLAYER'S OWN INCOME, never in a fixed sum. The store's
    /// gold packs show why: the biggest one is a million in cash, which is ten minutes of a maxed
    /// coal island and about five seconds of a gold one. A fixed number is a different product on
    /// every island and has to be rebalanced whenever the curve moves; a number of income-hours is
    /// the same product everywhere and never needs touching again. The gem cards already work this
    /// way (<c>incomeMinutes</c>), and this reuses that exact path.
    ///
    /// The IAP skus are CONSUMABLE and shared by all eight islands, so <c>purchasedOffers</c> cannot
    /// gate them — writing the sku there would lock the small pack on every island the moment it was
    /// bought on one. The per-island receipts live in <c>islandOffersBought</c> instead, and the
    /// bindings handed to the store carry <c>oneTime = false</c> so the store never files the shared
    /// sku away.
    /// </summary>
    // The offer owns an authored landscape arrangement. It has to land before LetterboxRoot measures
    // the card, otherwise the generic portrait-fold pass measures the old 900x1240 stack first.
    [DefaultExecutionOrder(-110)]
    public sealed class OfferPopupUI : MonoBehaviour
    {
        /// <summary>
        /// One rung of the ladder. Everything here is what the player is buying; which island it is
        /// dressed as comes from wherever they happen to be standing when it fires.
        /// </summary>
        [Serializable]
        public sealed class Tier
        {
            [Tooltip("Play Console ürün kimliği. Tüketilir olmalı — aynı ürün her adada bir kez satılır.")]
            public string sku = "";
            [Tooltip("Kartta yazan fiyat, ör. \"₺19,99\". Gerçek tahsilat mağazanın kendi fiyatından yapılır.")]
            public string priceLabel = "";
            [Tooltip("Başlık satırı: SEFER / VARDİYA / KASA — metinler.txt anahtarı.")]
            public string nameKey = "";
            [Tooltip("Kaç saatlik imparatorluk geliri verir. Sabit tutar değil: oyuncunun kendi hızıyla çarpılır.")]
            public float incomeHours = 6f;
            [Tooltip("Hızlandırıcının süresi, saat. 0 ise satır hiç görünmez.")]
            public float boostHours = 4f;
            public double boostMultiplier = 2d;
            [Tooltip("Verilen elmas. 0 ise satır hiç görünmez.")]
            public long gemAmount = 0;
        }

        [Header("Pencere (UI_Teklif prefabında bağlı)")]
        [SerializeField] private GameObject panelRoot;
        [Tooltip("Arkadaki karartma — dokununca pencere kapanır.")]
        [SerializeField] private Button dimButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button buyButton;

        [Header("Yazılar")]
        [Tooltip("Üst satır: teklifin ait olduğu adanın adı.")]
        [SerializeField] private TMP_Text islandTitle;
        [Tooltip("Alt satır: paketin adı (SEFER / VARDİYA / KASA).")]
        [SerializeField] private TMP_Text tierTitle;

        [Header("Ödül kartları")]
        [Tooltip("Kartların dizileceği satır. Boş bırakılırsa kart kurulmaz ve panel yalnız başlık, "
                 + "fiyat ve geri sayımdan ibaret kalır.")]
        [SerializeField] private RectTransform rewardRow;
        [SerializeField] private Sprite cardBack;
        [SerializeField] private Sprite incomeIcon;
        [SerializeField] private Sprite boostIcon;
        [SerializeField] private Sprite rewardGemIcon;
        [SerializeField] private TMP_FontAsset cardFont;
        [SerializeField] private Vector2 cardSize = new Vector2(216f, 300f);
        [SerializeField] private float cardGap = 14f;
        [SerializeField] private float cardIconSize = 104f;
        [SerializeField] private TMP_Text priceLabel;
        [Tooltip("Pencerenin içindeki geri sayım — \"ss:dd:sn\".")]
        [SerializeField] private TMP_Text countdownLabel;
        [Tooltip("Adanın cevher rengine boyanan şerit; boş bırakılabilir.")]
        [SerializeField] private Image oreTint;

        [Header("Yatay yerleşim")]
        [Tooltip("Kartın beyaz ana gövdesi. Yatay ekranda geniş ve kısa olacak şekilde düzenlenir.")]
        [SerializeField] private RectTransform offerCard;
        [Tooltip("SINIRLI FIRSAT satırı.")]
        [SerializeField] private RectTransform limitedOfferBadge;
        [Tooltip("Paket adının altındaki ayırıcı çizgi.")]
        [SerializeField] private RectTransform titleDivider;
        [Tooltip("Kartın arkasındaki dekoratif ışık. Ölçüme dikey kart yüksekliğini taşımaması için yataylaşır.")]
        [SerializeField] private RectTransform backdropGlow;

        [Header("Ne satıyoruz")]
        [Tooltip("Üç basamak, ucuzdan pahalıya. Sırayla döner: alınmamış olanların arasında gezinir.")]
        [SerializeField]
        private List<Tier> tiers = new List<Tier>
        {
            new Tier { sku = "teklif_kucuk", priceLabel = "₺19,99", nameKey = "teklif.sefer",
                       incomeHours = 6f,  boostHours = 4f,  gemAmount = 0 },
            new Tier { sku = "teklif_orta",  priceLabel = "₺49,99", nameKey = "teklif.vardiya",
                       incomeHours = 14f, boostHours = 8f,  gemAmount = 150 },
            new Tier { sku = "teklif_buyuk", priceLabel = "₺99,99", nameKey = "teklif.kasa",
                       incomeHours = 30f, boostHours = 24f, gemAmount = 400 },
        };

        [Header("Ne zaman rahatsız edebilir")]
        [Tooltip("Oyuna girdikten kaç saniye sonra ilk pencere açılabilir. Her oturumda geçerli.")]
        [SerializeField, Min(0f)] private float firstDelaySeconds = 90f;
        [Tooltip("Yeni bir adaya vardıktan kaç saniye sonra teklifi sunabilir. Varış gösterisi bitsin diye.")]
        [SerializeField, Min(0f)] private float islandDelaySeconds = 25f;
        [Tooltip("İki pencere arasındaki en az süre, saat.")]
        [SerializeField, Min(0f)] private float gapHours = 4f;
        [Tooltip("Günde ve haftada en fazla kaç pencere açılabilir.")]
        [SerializeField, Min(1)] private int maxPerDay = 2;
        [SerializeField, Min(1)] private int maxPerWeek = 5;
        [Tooltip("Teklifin geçerli olduğu süre, saat. Dolunca sıra bir sonraki pakete geçer.")]
        [SerializeField, Min(1f)] private float windowHours = 24f;
        [Tooltip("Bu köklerden biri açıkken pencere açılmaz — iptal olmaz, ilk uygun ana ertelenir.")]
        [SerializeField] private GameObject[] busyWhenOpen;
        [Tooltip("Eğitim ekranı. Turun ve tek seferlik ipuçlarının üstüne pencere açılmaz; ipuçları "
                 + "eğitim bittikten çok sonra da çıktığı için adım sayacı tek başına yetmiyor.")]
        [SerializeField] private TutorialUI tutorial;

        [Header("Bağlantılar")]
        [Tooltip("Satın alma buradan geçer: aynı IAP akışı, aynı ödül dağıtımı, aynı efektler. "
                 + "Editörde bedava test için mağazanın kendi devFreeIAP anahtarını aç.")]
        [SerializeField] private PremiumStoreUI store;

        private SaveData _data;
        private SaveService _save;
        private TimeService _time;
        private WorldIslands _world;
        private readonly char[] _clock = new char[8];   // "ss:dd:sn", rewritten in place
        private float _sessionTimer;
        private float _checkTimer;
        private float _quietUntil;                      // session time before which nothing may pop
        private int _island = -1;                       // last island seen, to notice a move
        private int _paintedSecond = -1;
        private bool _autoOpened;                       // a hand-opened window must not count as a refusal
        private IIAPService _iap;

        private void Awake()
        {
            ApplyLandscapeLayout();
        }

        /// <summary>Whether a pack is armed and buyable — what the HUD's clock chip rides on.</summary>
        public bool HasLiveOffer => SecondsLeft() > 0L;

        /// <summary>
        /// Seconds left on the armed offer; 0 when there is nothing to buy. The clock does not start
        /// when the offer is armed — it starts when the pop-up actually interrupts, the same trick
        /// <see cref="OfferCountdown"/> plays with the starter offer. Otherwise a player who opened
        /// the game for ten seconds and came back the next day would find the pack they were never
        /// shown already gone.
        /// </summary>
        public long SecondsLeft()
        {
            if (_data == null || _time == null || string.IsNullOrEmpty(_data.offerLiveKey)) return 0L;
            long window = (long)(windowHours * 3600f);
            if (_data.offerLiveStartUnix <= 0L) return window;
            long left = window - _time.ElapsedSince(_data.offerLiveStartUnix);
            return left > 0L ? left : 0L;
        }

        private void Start()
        {
            BuildCards();
            _data = ServiceLocator.Get<SaveData>();
            _save = ServiceLocator.Get<SaveService>();
            _time = ServiceLocator.Get<TimeService>();
            _iap = ServiceLocator.Get<IIAPService>();
            _world = FindAnyObjectByType<WorldIslands>();
            if (_iap != null) _iap.ProductsUpdated += OnProductsUpdated;

            if (buyButton != null) buyButton.onClick.AddListener(Buy);
            if (closeButton != null) closeButton.onClick.AddListener(Dismiss);
            if (dimButton != null) dimButton.onClick.AddListener(Dismiss);

            if (panelRoot != null)
            {
                panelRoot.SetActive(false);
                UiPanelSound.Attach(panelRoot);   // after the switch-off, or boot plays open+close
            }
        }

        private void OnDestroy()
        {
            if (_iap != null) _iap.ProductsUpdated -= OnProductsUpdated;
        }

        private void OnProductsUpdated()
        {
            if (panelRoot != null && panelRoot.activeInHierarchy && _data != null) Paint();
        }

        /// <summary>
        /// The portrait card is a 900x1240 vertical stack. Letting the generic letterbox helper fold
        /// that stack made the title overlap the rewards and left the action controls floating at the
        /// bottom edge. Landscape gets a deliberate top-to-bottom composition instead: identity below
        /// the ribbon, three generous reward cards through the middle, and both actions on one lower row.
        /// </summary>
        private void ApplyLandscapeLayout()
        {
            if (Screen.width <= Screen.height || offerCard == null) return;

            SetRect(backdropGlow, Vector2.zero, new Vector2(1900f, 760f));
            SetRect(offerCard, Vector2.zero, new Vector2(2100f, 820f));

            // Kartın kendi mavi başlık şeridi üstteki 100 birimi kaplıyor: başlık onun ortasına.
            RectTransform ribbon = islandTitle != null ? islandTitle.rectTransform.parent as RectTransform : null;
            SetRect(ribbon, new Vector2(0f, 360f), new Vector2(900f, 180f));
            SetRect(limitedOfferBadge, new Vector2(-650f, 165f), new Vector2(600f, 58f));
            SetRect(tierTitle != null ? tierTitle.rectTransform : null,
                    new Vector2(-650f, 95f), new Vector2(700f, 86f));
            SetRect(titleDivider, new Vector2(-650f, 25f), new Vector2(560f, 8f));

            cardSize = new Vector2(270f, 330f);
            cardGap = 22f;
            cardIconSize = 112f;
            SetRect(rewardRow, new Vector2(200f, 45f), new Vector2(900f, 360f));

            RectTransform timer = countdownLabel != null
                ? countdownLabel.rectTransform.parent as RectTransform
                : null;
            SetRect(timer, new Vector2(-240f, -275f), new Vector2(390f, 140f));
            SetRect(buyButton != null ? buyButton.transform as RectTransform : null,
                    new Vector2(300f, -275f), new Vector2(520f, 150f));
            SetRect(closeButton != null ? closeButton.transform as RectTransform : null,
                    new Vector2(1000f, 360f), new Vector2(84f, 84f));
        }

        private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private void Update()
        {
            _sessionTimer += Time.unscaledDeltaTime;

            if (panelRoot != null && panelRoot.activeSelf) { PaintCountdown(); return; }

            // Asking once a second is enough for a thing that fires twice a day, and it keeps the
            // island sweep and the receipt scan out of the frame budget.
            _checkTimer -= Time.unscaledDeltaTime;
            if (_checkTimer > 0f) return;
            _checkTimer = 1f;
            WatchIsland();
            Arm();
            if (DueNow()) AutoOpen();
        }

        /// <summary>
        /// Keeps an offer armed whenever this island still has one to sell, so the HUD button is
        /// always there to walk back into. Arming is silent — it is not an interruption and does not
        /// spend the day's budget; only <see cref="AutoOpen"/> does that.
        /// </summary>
        private void Arm()
        {
            if (_data == null || _time == null || _world == null) return;
            if (SecondsLeft() > 0L) return;                 // one is already armed or running
            int tier = NextTier();
            if (tier < 0) { _data.offerLiveKey = ""; return; }

            _data.offerLiveKey = _world.IslandKey(_world.ActiveIndex) + ":" + tier;
            _data.offerLiveStartUnix = 0L;                  // clock starts when the pop-up does
            Save();
        }

        /// <summary>
        /// A new island starts its own ladder at the cheapest pack, exactly as coal does, and does not
        /// have to wait out the gap left by the island before it: arriving somewhere new is the moment
        /// its pack is worth the most, and the gap was measured against a place the player has left.
        /// </summary>
        private void WatchIsland()
        {
            if (_world == null || _data == null) return;
            int active = _world.ActiveIndex;
            if (active == _island) return;
            int previous = _island;
            _island = active;
            if (previous < 0) return;                       // first bind of the session, not a move

            _data.offerLiveKey = "";
            _data.offerLiveStartUnix = 0L;
            _data.offerPoppedKey = "";
            _data.offerShownUnix = 0L;
            _quietUntil = _sessionTimer + islandDelaySeconds;
            Save();
        }

        // ---- when may we interrupt -------------------------------------------------------------

        /// <summary>
        /// Every gate the pop-up has to pass, cheapest first. Read top to bottom it is the whole
        /// politeness policy: never before the player knows the game, never twice in a short stretch,
        /// never more than twice a day however long they play, and never on top of something they
        /// opened themselves.
        /// </summary>
        private bool DueNow()
        {
            if (_data == null || _time == null) return false;
            if (_sessionTimer < firstDelaySeconds || _sessionTimer < _quietUntil) return false;
            if (_data.tutorialStep < 100) return false;
            if (string.IsNullOrEmpty(_data.offerLiveKey)) return false;   // nothing armed to show
            // One pop-up per offer: once this one has interrupted, the HUD button is the whole
            // reminder and stays lit until the pack is bought or its window rolls over to the next.
            if (_data.offerLiveKey == _data.offerPoppedKey) return false;

            if (_data.offerShownUnix > 0L)
            {
                long gap = (long)(gapHours * 3600f) * DeclineFactor();
                if (_time.ElapsedSince(_data.offerShownUnix) < gap) return false;
            }
            if (CapReached()) return false;
            if (AnythingOpen()) return false;
            return IncomePerMinute() > 0d;               // a pack of income-hours is worth nothing yet
        }

        /// <summary>
        /// Three refusals in a row and the gap doubles; six and it quadruples. A player who keeps
        /// closing it is answering the question, and the answer should cost them less often.
        /// </summary>
        private int DeclineFactor()
        {
            int streak = _data.offerDeclineStreak;
            if (streak >= 6) return 4;
            if (streak >= 3) return 2;
            return 1;
        }

        /// <summary>
        /// Rolls the day and week counters over as a side effect — they are only ever read here, and
        /// resetting them on read is what keeps a stale count from last week out of today's budget.
        /// </summary>
        private bool CapReached()
        {
            int day = (int)(_time.NowUnix() / 86400L);
            int week = day / 7;
            if (_data.offerDayNumber != day) { _data.offerDayNumber = day; _data.offerShownToday = 0; }
            if (_data.offerWeekNumber != week) { _data.offerWeekNumber = week; _data.offerShownThisWeek = 0; }
            return _data.offerShownToday >= maxPerDay || _data.offerShownThisWeek >= maxPerWeek;
        }

        private bool AnythingOpen()
        {
            if (tutorial != null && tutorial.IsShowing) return true;
            if (busyWhenOpen == null) return false;
            for (int i = 0; i < busyWhenOpen.Length; i++)
                if (busyWhenOpen[i] != null && busyWhenOpen[i].activeInHierarchy) return true;
            return false;
        }

        // ---- which offer ------------------------------------------------------------------------

        /// <summary>
        /// The next pack to put in front of the player on this island, or -1 when it has none left.
        /// It rotates rather than always offering the cheapest: someone who did not buy the small
        /// pack has already seen it, and a third showing is how an offer starts reading as wallpaper.
        /// </summary>
        private int NextTier()
        {
            if (_world == null || tiers.Count == 0) return -1;
            string island = _world.IslandKey(_world.ActiveIndex);
            if (string.IsNullOrEmpty(island)) return -1;

            int start = 0;
            int last = TierOf(_data.offerLiveKey);
            if (last >= 0) start = last + 1;

            for (int step = 0; step < tiers.Count; step++)
            {
                int t = (start + step) % tiers.Count;
                if (!Bought(island, t)) return t;
            }
            return -1;
        }

        /// <summary>
        /// Receipts are compared piece by piece rather than by building "island:tier" and testing that:
        /// this runs on the once-a-second path, and a string built there is a string collected there.
        /// </summary>
        private bool Bought(string island, int tier)
        {
            if (_data == null || string.IsNullOrEmpty(island)) return false;
            for (int i = 0; i < _data.islandOffersBought.Count; i++)
            {
                string receipt = _data.islandOffersBought[i];
                if (receipt == null) continue;
                int colon = receipt.LastIndexOf(':');
                if (colon != island.Length) continue;
                if (string.CompareOrdinal(receipt, 0, island, 0, colon) != 0) continue;
                if (TierOf(receipt) == tier) return true;
            }
            return false;
        }

        /// <summary>Tier index out of an "islandKey:tier" receipt, or -1 when it is not one.</summary>
        private int TierOf(string key)
        {
            if (string.IsNullOrEmpty(key)) return -1;
            int colon = key.LastIndexOf(':');
            if (colon < 0 || colon + 1 >= key.Length) return -1;
            int tier = 0;
            for (int i = colon + 1; i < key.Length; i++)
            {
                char c = key[i];
                if (c < '0' || c > '9') return -1;
                tier = tier * 10 + (c - '0');
            }
            return tier < tiers.Count ? tier : -1;
        }

        private int IslandOf(string key)
        {
            if (_world == null || string.IsNullOrEmpty(key)) return -1;
            int colon = key.LastIndexOf(':');
            if (colon <= 0) return -1;
            for (int i = 0; i < _world.Count; i++)
            {
                string island = _world.IslandKey(i);
                if (island != null && island.Length == colon
                    && string.CompareOrdinal(island, 0, key, 0, colon) == 0) return i;
            }
            return -1;
        }

        /// <summary>What the empire earns a minute — the same sum the store and the HUD price against.</summary>
        private double IncomePerMinute()
        {
            if (_world == null) return 0d;
            double sum = 0d;
            for (int i = 0; i < _world.Count; i++) if (_world.IsOwned(i)) sum += _world.RatePerMin(i);
            return sum;
        }

        // ---- open / close -----------------------------------------------------------------------

        private void AutoOpen()
        {
            _data.offerPoppedKey = _data.offerLiveKey;
            _data.offerShownUnix = _time.NowUnix();
            _data.offerShownToday++;
            _data.offerShownThisWeek++;
            Save();

            _autoOpened = true;
            Show();
        }

        /// <summary>
        /// Reopening from the HUD button — same window, and closing it counts as no refusal. The button
        /// is permanent furniture, so this has to answer even on an island with nothing left to sell:
        /// the store is where someone pressing the offer shortcut was heading anyway, and a control that
        /// does nothing when pressed reads as broken.
        /// </summary>
        public void Open()
        {
            if (!HasLiveOffer)
            {
                if (store != null) store.Show();
                return;
            }
            _autoOpened = false;
            Show();
        }

        private void Show()
        {
            // The window starts the first time the card is actually looked at, however it was opened.
            if (_data != null && _time != null && _data.offerLiveStartUnix <= 0L)
            {
                _data.offerLiveStartUnix = _time.NowUnix();
                Save();
            }
            Paint();
            if (panelRoot != null) panelRoot.SetActive(true);
            _paintedSecond = -1;
            PaintCountdown();
        }

        private void Dismiss()
        {
            // Only an interruption can be refused. Closing a window the player opened themselves says
            // nothing about whether they want to be asked again.
            if (_autoOpened && _data != null)
            {
                _data.offerDeclineStreak++;
                Save();
            }
            Hide();
        }

        private void Hide()
        {
            _autoOpened = false;
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void Buy()
        {
            if (store == null || _data == null) return;
            int tier = TierOf(_data.offerLiveKey);
            if (tier < 0) return;

            string key = _data.offerLiveKey;
            if (buyButton != null) buyButton.interactable = false;
            store.BuyOffer(Binding(tiers[tier]), ok => Settle(key, ok));
        }

        /// <summary>Files the receipt under the island, not the sku — see the class summary.</summary>
        private void Settle(string key, bool ok)
        {
            if (buyButton != null) buyButton.interactable = true;
            if (!ok) return;

            _data.islandOffersBought.Add(key);
            _data.offerDeclineStreak = 0;
            _data.offerLiveKey = "";
            _data.offerLiveStartUnix = 0L;
            _data.offerPoppedKey = "";
            // Arm the next pack in this same frame instead of waiting for the once-a-second tick. The
            // HUD's clock reads the armed offer four times a second, so leaving the gap open would show
            // the player an empty slot for up to a second at the very moment they paid.
            Arm();
            if (SecondsLeft() <= 0L) Save();   // island sold out: Arm() wrote nothing, the receipt must still land
            Hide();
        }

        /// <summary>
        /// Yarıda kalmış bir ada teklifini yeniden kurar. Hangi adada satıldığı kayıtta tutulmaz, o
        /// yüzden içerik bugünkü gelire göre yeniden ölçülür; kademenin elmas ve hız ödülleri zaten
        /// tablodan gelir. Sku tabloda yoksa null döner ve çağıran siparişi onaylamadan bırakır.
        /// </summary>
        public PremiumStoreUI.OfferBinding OrphanBinding(string sku)
        {
            for (int i = 0; i < tiers.Count; i++)
                if (tiers[i] != null && tiers[i].sku == sku) return Binding(tiers[i]);
            return null;
        }

        private PremiumStoreUI.OfferBinding Binding(Tier tier)
        {
            return new PremiumStoreUI.OfferBinding
            {
                name = tier.nameKey,
                sku = tier.sku,
                button = buyButton,
                incomeMinutes = tier.incomeHours * 60f,
                boostMultiplier = tier.boostMultiplier,
                boostSeconds = tier.boostHours * 3600d,
                gemAmount = tier.gemAmount,
                oneTime = false,
            };
        }

        private void Save()
        {
            if (_save != null && _data != null) _save.Save(_data);
        }

        // ---- painting ---------------------------------------------------------------------------

        private void Paint()
        {
            int tier = TierOf(_data.offerLiveKey);
            if (tier < 0) return;
            Tier pack = tiers[tier];

            int island = IslandOf(_data.offerLiveKey);
            if (islandTitle != null && island >= 0) islandTitle.text = _world.IslandName(island);
            if (tierTitle != null) tierTitle.text = Loc.T(pack.nameKey);
            if (priceLabel != null)
                priceLabel.text = store != null
                    ? store.LocalizedPrice(pack.sku, pack.priceLabel)
                    : pack.priceLabel;
            if (oreTint != null && island >= 0)
                // The ore colours are the map's, and coal's is nearly black — lift it, or the ribbon
                // reads as a hole in the card.
                oreTint.color = Color.Lerp(_world.OreColor(island), Color.white, 0.25f);

            PaintCards(pack);
        }

        /// <summary>
        /// What the pack contains, as the same kind of card the store sells things on. It used to be
        /// three lines of text — accurate, and completely flat next to a store full of illustrated
        /// packs. An offer is the one screen that has to look worth more than it costs.
        ///
        /// The cheapest pack has no gems, so it fills two cards where the dearest fills three. The row
        /// is therefore centred on however many are live rather than anchored to a fixed ladder: a gap
        /// where a third card should be reads as something failed to load.
        /// </summary>
        private void PaintCards(Tier pack)
        {
            if (_cards == null) return;
            bool income = pack.incomeHours > 0f;
            bool boost = pack.boostHours > 0f && pack.boostMultiplier > 1d;
            bool gems = pack.gemAmount > 0;
            int count = Mathf.Min((income ? 1 : 0) + (boost ? 1 : 0) + (gems ? 1 : 0), _cards.Length);

            int i = 0;
            if (income && i < _cards.Length)
            {
                // Her iki yarısı da gerekli: saat teklifin oyuncunun kendi ilerlemesinin ne kadarına
                // denk geldiğini, tutar cüzdana ne düşeceğini söylüyor. Tek başına ikisi de ölçüsüz.
                double cash = IncomePerMinute() * pack.incomeHours * 60d;
                Card(i++, count, incomeIcon, "$" + NumberFormatter.Format(new BigDouble(cash)),
                     string.Format(Loc.T("teklif.kart_gelir"), Whole(pack.incomeHours)));
            }
            if (boost && i < _cards.Length)
                Card(i++, count, boostIcon,
                     "×" + pack.boostMultiplier.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
                     string.Format(Loc.T("teklif.kart_hiz"), Whole(pack.boostHours)));
            if (gems && i < _cards.Length)
                Card(i++, count, rewardGemIcon, pack.gemAmount.ToString(), Loc.T("teklif.kart_elmas"));

            for (; i < _cards.Length; i++)
                if (_cards[i].root.activeSelf) _cards[i].root.SetActive(false);
        }

        private void Card(int i, int count, Sprite icon, string value, string caption)
        {
            RewardCard card = _cards[i];
            if (!card.root.activeSelf) card.root.SetActive(true);
            card.icon.enabled = icon != null;
            card.icon.sprite = icon;
            card.value.text = value;
            card.caption.text = caption;
            card.rect.anchoredPosition =
                new Vector2((i - (count - 1) * 0.5f) * (cardSize.x + cardGap), 0f);
        }

        /// <summary>
        /// Builds the three cards once. Code rather than prefab because they are one shape repeated,
        /// and three authored copies drift apart the first time one of them is nudged.
        /// </summary>
        private void BuildCards()
        {
            if (rewardRow == null) return;
            _cards = new RewardCard[3];
            for (int i = 0; i < _cards.Length; i++)
            {
                var go = new GameObject("Kart" + i, typeof(RectTransform), typeof(Image));
                var rt = (RectTransform)go.transform;
                rt.SetParent(rewardRow, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = cardSize;
                var back = go.GetComponent<Image>();
                back.sprite = cardBack;
                back.type = Image.Type.Sliced;
                back.raycastTarget = false;

                var iconGo = new GameObject("Ikon", typeof(RectTransform), typeof(Image));
                var irt = (RectTransform)iconGo.transform;
                irt.SetParent(rt, false);
                irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 1f);
                irt.pivot = new Vector2(0.5f, 1f);
                irt.sizeDelta = new Vector2(cardIconSize, cardIconSize);
                irt.anchoredPosition = new Vector2(0f, -22f);
                var im = iconGo.GetComponent<Image>();
                im.raycastTarget = false;
                im.preserveAspect = true;

                _cards[i] = new RewardCard
                {
                    root = go,
                    rect = rt,
                    icon = im,
                    // The two boxes used to be -138..-208 and -196..-264, which overlapped by twelve
                    // pixels: at the sizes auto-sizing actually settles on, both fill their box, so the
                    // value's last line sat on top of the caption's first. They are stacked with a gap
                    // now, and the caption is taller because nine of the eleven languages wrap it onto
                    // two lines ("24 SAATLİK GELİR", "24 GODZ. DOPALACZA") — at 68 high that forced it
                    // down to 21pt on some cards and 27pt on others, which read as a mistake.
                    value = CardText(rt, "Deger", 54f, new Vector2(0f, -(cardIconSize + 28f)), 74f,
                                     new Color32(0x2A, 0x3A, 0x5C, 0xFF)),
                    caption = CardText(rt, "Alt", 28f, new Vector2(0f, -(cardIconSize + 106f)), 80f,
                                       new Color32(0x6B, 0x7A, 0x99, 0xFF)),
                };
                go.SetActive(false);
            }
        }

        private TMP_Text CardText(RectTransform parent, string name, float size, Vector2 pos,
                                  float height, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            // 18 rather than 10: the caption is the widest thing on the card and at 10 it measured the
            // full 196 of a 216-wide card, so the text ran up against the frame the sprite paints
            // around the edge and read as spilling out of it.
            rt.offsetMin = new Vector2(18f, 0f);
            rt.offsetMax = new Vector2(-18f, 0f);
            rt.sizeDelta = new Vector2(-36f, height);
            rt.anchoredPosition = pos;
            var tmp = go.GetComponent<TextMeshProUGUI>();
            if (cardFont != null) tmp.font = cardFont;
            tmp.fontSize = size;
            tmp.alignment = TextAlignmentOptions.Top;
            tmp.color = color;
            tmp.raycastTarget = false;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = size * 0.55f;
            tmp.fontSizeMax = size;
            return tmp;
        }

        private struct RewardCard
        {
            public GameObject root;
            public RectTransform rect;
            public Image icon;
            public TMP_Text value;
            public TMP_Text caption;
        }

        private RewardCard[] _cards;

        private static string Whole(float hours) =>
            hours.ToString(hours % 1f == 0f ? "0" : "0.#",
                           System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// Rewrites the clock in place once a second. Building "03:41:07" every frame would allocate a
        /// string sixty times a second for as long as the window is open.
        /// </summary>
        private void PaintCountdown()
        {
            long left = SecondsLeft();
            // A window that runs out while the player is looking at it closes itself, and that is not
            // a refusal — they never got the chance to say no.
            if (left <= 0L) { Hide(); return; }
            if (countdownLabel == null) return;

            int whole = (int)left;
            if (whole == _paintedSecond) return;
            _paintedSecond = whole;

            int h = whole / 3600; if (h > 99) h = 99;
            int m = whole / 60 % 60;
            int s = whole % 60;
            _clock[0] = (char)('0' + h / 10); _clock[1] = (char)('0' + h % 10); _clock[2] = ':';
            _clock[3] = (char)('0' + m / 10); _clock[4] = (char)('0' + m % 10); _clock[5] = ':';
            _clock[6] = (char)('0' + s / 10); _clock[7] = (char)('0' + s % 10);
            countdownLabel.SetCharArray(_clock, 0, 8);
        }
    }
}
