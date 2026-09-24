using System.Collections;
using Game.Core;
using Game.Data;
using Game.Gameplay;
using Game.Systems;
using TMPro;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// The onboarding. Players were arriving on an island that runs itself and could not tell what the
    /// trains and trucks were for, so nothing on the upgrade screen meant anything either.
    ///
    /// Three parts, in this order, and only the first two are ever seen back to back:
    ///
    ///   1. THE TOUR — short captions point out the production chain in the player's current island view.
    ///      The HUD is off and only DEVAM advances.
    ///   2. THE CONTROLS — the HUD comes back and Usta Max points at each relevant control. The control
    ///      stays visible inside the spotlight, but a full-screen blocker keeps it visual-only; DEVAM
    ///      remains the one active input until the onboarding finishes.
    ///   3. THE TIPS — one-shot cards fired much later, when the thing they describe first becomes
    ///      true: a contract falls due, a boost charges, an island becomes affordable. Never twice, and
    ///      never blocking.
    ///
    /// This decides what to show and when. Drawing it — Max, his card, the spotlight — is
    /// <see cref="TutorialPresenter"/>'s, built on first use from the art in the Inspector slots below,
    /// so the look stays tunable from the hierarchy like every other screen.
    /// </summary>
    public sealed class TutorialUI : MonoBehaviour
    {
        // Yalnızca iki durum var: hiç oynanmadı, ya da bitti. Otuz saniyelik bir eğitimin ortasından
        // devam etmek, oyunu ilk açtığında yarısını görmüş bir oyuncuya yarısını göstermek demek.
        private const int StepFresh = 0;
        private const int StepDone = 100;

        [Header("Kart görselleri")]
        [Tooltip("Lacivert tutorial paneli. Tek parça resim: oranı korunur, genişliği ekran belirler.")]
        [SerializeField] private Sprite tutorialPanel;
        [Tooltip("Altın çerçeveli mavi DEVAM butonu. Oranı korunur.")]
        [SerializeField] private Sprite tutorialButton;
        [Tooltip("Simgenin oturduğu yuvarlak — madalyon.")]
        [SerializeField] private Sprite medallion;
        [Tooltip("ATLA tuşunun zemini — pill_sayac.")]
        [SerializeField] private Sprite skipPill;
        [SerializeField] private Sprite pipOn;         // pip_dolu
        [SerializeField] private Sprite pipOff;        // pip_bos
        [Tooltip("Dünyadaki hedefin üstünde duran iğne — rozet_buradasin.")]
        [SerializeField] private Sprite worldPin;
        [Tooltip("Oyuncunun gerçekten dokunması beklenen anlarda halkanın ortasında beliren el. "
                 + "Boş bırakılırsa el hiç çıkmaz, eğitim aynen çalışır.")]
        [SerializeField] private Sprite tapHand;
        [Tooltip("Boşken sahnedeki ilk TMP yazı tipi ödünç alınır.")]
        [SerializeField] private TMP_FontAsset font;

        [Header("Anlatıcı — Usta Max")]
        [Tooltip("Hedef göstermeyen karşılama ve genel anlatım pozu.")]
        [SerializeField] private Sprite narratorWelcome;
        [Tooltip("Oyuna ilk girişte şapkasına dokunarak oyuncuyu selamlayan poz.")]
        [SerializeField] private Sprite narratorFirstEntry;
        [Tooltip("Tur anlatımında kullanılan düşünceli/açıklayıcı poz.")]
        [SerializeField] private Sprite narratorThoughtful;
        [Tooltip("Tur anlatımında önemli noktaları vurgulayan ciddi poz.")]
        [SerializeField] private Sprite narratorWarning;
        [Tooltip("Bütün tutorial tamamlandığında gösterilen mutlu poz.")]
        [SerializeField] private Sprite narratorHappy;
        [Tooltip("Sola işaret pozu. Sağdaki hedefler için Max sola geçer ve poz yatay çevrilir.")]
        [SerializeField] private Sprite narratorPoint;

        [Header("Tur simgeleri (maden, tren, depo, izabe, pazar, nakit)")]
        [SerializeField] private Sprite[] tourIcons = new Sprite[6];

        [Header("Dikey eğitim yerleşimi")]
        [Tooltip("Kartın en fazla genişliği. Telefonlarda güvenli alanın genişliği bundan dardır ve o kullanılır.")]
        [SerializeField, Min(600f)] private float cardMaxWidth = 960f;
        [Tooltip("Max'in boyu, ekran yüksekliğinin payı olarak. Aşağıdaki iki sınırla kırpılır.")]
        [SerializeField, Range(0.2f, 0.36f)] private float narratorHeightShare = 0.28f;
        [SerializeField, Min(240f)] private float narratorShortest = 420f;
        [SerializeField, Min(300f)] private float narratorTallest = 600f;
        [Tooltip("Max'in ayaklarının kartın ne kadar yukarısında durduğu, kart yüksekliğinin payı olarak. "
                 + "Kartın altında kalan kısmı görünmez.")]
        [SerializeField, Range(0f, 0.8f)] private float narratorSink = 0.35f;
        [Tooltip("Kart ile güvenli alanın kenarları arasındaki boşluk.")]
        [SerializeField, Min(0f)] private float edgeMargin = 24f;
        [Tooltip("Üste yerleşince güvenli alanın tepesinde boş bırakılan yer: nakit ve elmas çubuğu.")]
        [SerializeField, Min(0f)] private float topReserve = 190f;

        [Header("Zamanlama")]
        [Tooltip("Bağlamsal ipucu kartının ekranda kalma süresi.")]
        [SerializeField] private float tipSeconds = 5.5f;
        [Tooltip("Elin bir dokunuşunun süresi. Yavaş olsun: hızlısı dokunmuyor, titriyor gibi duruyor.")]
        [SerializeField] private float handTapSeconds = 0.9f;

        [Header("Renkler")]
        [SerializeField] private Color shadeColor = new Color(0.02f, 0.04f, 0.09f, 0.80f);
        [SerializeField] private Color ringColor = new Color32(0xFF, 0xC8, 0x3C, 0xFF);

        private const int SortingOrder = 250;      // hoş geldin ekranı 200; eğitim her şeyin üstünde

        // ------------------------------------------------------------------ tur
        private struct Beat
        {
            public int station;    // -1 = geniş açı, tek bir istasyon değil
            public string key;     // egitim.tur_<key>
            public int icon;
            public bool ride;      // işaretçi hareket eden treni izler
        }

        private static readonly Beat[] Tour =
        {
            new Beat { station = IslandEconomy.Mine,    key = "maden",  icon = 0 },
            new Beat { station = IslandEconomy.Train,   key = "tren",   icon = 1, ride = true },
            new Beat { station = IslandEconomy.Storage, key = "depo",   icon = 2 },
            new Beat { station = IslandEconomy.Smelter, key = "izabe",  icon = 3 },
            new Beat { station = IslandEconomy.Market,  key = "pazar",  icon = 4 },
            new Beat { station = -1,                    key = "zincir", icon = 5 },
        };

        private static readonly string[] ShopOverview = { "production", "sale", "growth" };

        /// <summary>One stop of the closing button pass. <see cref="StopRect"/> holds the matching rect.</summary>
        private struct Stop
        {
            public string key;     // egitim.<key>_b / _m
            public string tip;     // aynı şeyi anlatan ipucunun kimliği; tanıtılınca o ipucu bir daha çıkmaz
        }

        /// <summary>
        /// HUD'un okunma sırası: üst sıra soldan sağa, sağ ray yukarıdan aşağı, sonra alt sıra. Bazı
        /// duraklar mevcut ipucu metnini yeniden kullanıyor — aynı butonu iki farklı cümleyle anlatmanın
        /// bir faydası yok, üstelik o ipuçları on bir dile çevrilmiş durumda.
        /// </summary>
        private static readonly Stop[] Stops =
        {
            new Stop { key = "buton_ayarlar" },
            new Stop { key = "buton_magaza"  },
            new Stop { key = "ipucu_gunluk",  tip = "gunluk"  },
            new Stop { key = "buton_kontrat", tip = "kontrat" },
            new Stop { key = "buton_reklam"  },
            new Stop { key = "buton_teklif"  },
            new Stop { key = "ipucu_boost",   tip = "boost"   },
        };

        // ------------------------------------------------------------------ servisler
        private SaveData _data;
        private SaveService _save;
        private WalletService _wallet;
        private ContractService _contract;
        private DailyRewardService _daily;
        private AudioService _audio;
        private HapticService _haptic;
        private HudUI _hud;
        private CoalOperation _op;
        private Canvas _hudCanvas;
        private CanvasGroup _hudFade;
        private MarketService _market;

        /// <summary>
        /// The mining shop owns Main. The ore tour and its tips describe the old chain: shown now they would
        /// cover the shop's panel and point at stations the player cannot use. They wait instead; the saved
        /// step is never written here, so the tour resumes wherever it was if the shop is ever switched off.
        /// </summary>
        private bool ShopActive => _market != null && (_market.MiningShop != null || _market.MiningShopBusiness != null);

        // ------------------------------------------------------------------ durum
        private TutorialPresenter _view;
        private bool _running;
        private bool _skipped;
        private bool _tapped;
        private bool _tapAdvances;
        private bool _skipVisible;
        private float _wait;                   // açılışta her şeyin oturmasını bekleme sayacı
        private float _tipTimer = 6f;
        private bool _tipShowing;
        private float _featureTipCooldown;
        private MiningShopView _shopView;
        private static TutorialUI _instance;

        /// <summary>Lets feature screens request a one-shot explanation when the player opens them.</summary>
        public static void NotifyFeatureOpened(string id)
        {
            if (_instance != null) _instance.OnFeatureOpened(id);
        }

        // ------------------------------------------------------------------ giriş

        private void Start()
        {
            _instance = this;
            _data = ServiceLocator.Get<SaveData>();
            _save = ServiceLocator.Get<SaveService>();
            _wallet = ServiceLocator.Get<WalletService>();
            _contract = ServiceLocator.Get<ContractService>();
            _daily = ServiceLocator.Get<DailyRewardService>();
            _audio = ServiceLocator.Get<AudioService>();
            _haptic = ServiceLocator.Get<HapticService>();
            _hud = GetComponent<HudUI>();
            _hudCanvas = GetComponent<Canvas>();
            _market = ServiceLocator.Get<MarketService>();
        }

        private void Update()
        {
            if (_running) return;

            if (ShopActive)
            {
                ShopTutorialTick();
                return;
            }

            if (_op == null || !_op.enabled) BindOp();

            if (_data != null && _data.tutorialStep < StepDone)
            {
                // Ada oturana kadar bekle. Kamera daha çerçevelenmemişken ilk durağa süzülmek,
                // oyunun ilk saniyesinde iki kameranın birbiriyle kavga etmesi demek.
                _wait += Time.unscaledDeltaTime;
                if (_wait > 1.2f && Ready()) { StartCoroutine(Play()); return; }
            }

            TipTick();
        }

        /// <summary>Everything the opening needs before it can start: an island, a camera, no popup on top.</summary>
        private bool Ready()
        {
            if (_op == null || Camera.main == null) return false;
            var report = ServiceLocator.Get<OfflineReport>();
            if (report != null && report.Pending) return false;
            var boot = FindAnyObjectByType<OperationCameraBoot>();
            if (boot != null && !boot.Framed) return false;
            Vector3 ignore;
            return _op.StationAnchor(IslandEconomy.Mine, out ignore);
        }

        private void BindOp()
        {
            var ops = FindObjectsByType<CoalOperation>();
            for (int i = 0; i < ops.Length; i++)
                if (ops[i].enabled) { _op = ops[i]; return; }
            if (_op == null && ops.Length > 0) _op = ops[0];
        }

        /// <summary>
        /// Whether any of the onboarding is on screen — the tour's dimmer or a one-shot tip. Both
        /// live under the same runtime root, which is built on first use and switched off between
        /// beats. <see cref="OfferPopupUI"/> asks so an offer never lands on a hint the player is
        /// still reading; the tips fire long after the tour is finished, so its step counter alone
        /// would not catch them.
        /// </summary>
        public bool IsShowing => _view != null && _view.Visible;

        /// <summary>Replays the whole thing — the settings screen's EĞİTİM row.</summary>
        public void Replay()
        {
            if (_running || _data == null) return;
            _data.tutorialStep = StepFresh;
            for (int i = 0; i < ShopOverview.Length; i++)
                RemoveTutorialId("core.shop.overview." + ShopOverview[i]);
            _wait = 0f;
            _skipped = false;
            _save?.Save(_data);
        }

        private void ShopTutorialTick()
        {
            if (_data == null) return;
            if (_shopView == null) _shopView = FindAnyObjectByType<MiningShopView>();

            if (_featureTipCooldown > 0f) _featureTipCooldown -= Time.unscaledDeltaTime;
            if (_data.tutorialStep >= StepDone)
            {
                TipTick();
                return;
            }

            if (HasShopExperience())
            {
                _data.tutorialStep = StepDone;
                MarkTutorialId("core.shop.experienced");
                return;
            }

            if (_shopView == null || Camera.main == null) return;
            var report = ServiceLocator.Get<OfflineReport>();
            if (report != null && report.Pending) return;
            var boot = FindAnyObjectByType<OperationCameraBoot>();
            if (boot != null && !boot.Framed) return;

            _wait += Time.unscaledDeltaTime;
            if (_wait > 1.2f) StartCoroutine(PlayShopTutorial());
        }

        private bool HasShopExperience()
        {
            if (_data == null || _data.miningShopBusinesses == null) return false;
            for (int i = 0; i < _data.miningShopBusinesses.Count; i++)
            {
                MiningShopState state = _data.miningShopBusinesses[i];
                if (state == null || state.Business == null || state.Business.Lines == null) continue;
                if (state.Business.ReceiptSequence >= 5) return true;
                int builtLines = 0;
                for (int line = 0; line < state.Business.Lines.Count; line++)
                {
                    MiningShopProductLineState product = state.Business.Lines[line];
                    if (product == null) continue;
                    if (product.TableBuilt) builtLines++;
                    if (product.SpeedLevel > 1 || product.ValueLevel > 1) return true;
                }
                if (builtLines > 1) return true;
            }
            return false;
        }

        private IEnumerator PlayShopTutorial()
        {
            _running = true;
            _skipped = false;
            _tapped = false;
            Build();
            _tapAdvances = true;
            _view.ClearTarget();
            _view.SetRing(false, false);
            _view.BlockInput(false);
            _view.SetShadeActive(false);
            _view.SetPips(0, 0);
            ShowSkip(true);
            yield return null; // Let the portrait canvas settle before sizing the guide.

            for (int i = 0; i < ShopOverview.Length && !_skipped; i++)
            {
                string key = ShopOverview[i];
                string id = "core.shop.overview." + key;
                if (HasTutorialId(id)) continue;
                Card(Loc.T("egitim.shop_overview_" + key + "_b"), Loc.T("egitim.shop_overview_" + key + "_m"), null,
                     i == 0 && narratorFirstEntry != null ? narratorFirstEntry : narratorWelcome);
                Sound(SoundId.PanelOpen);
                yield return WaitContinue();
                if (!_skipped) MarkTutorialId(id);
                yield return _view.HideCard();
            }

            if (_skipped) MarkTutorialId("core.shop.skipped");
            _data.tutorialStep = StepDone;
            _save?.Save(_data);

            ClearGuide();
            _running = false;
            _tipTimer = 10f;
        }

        private void ClearGuide()
        {
            ShowSkip(false);
            if (_view != null) _view.Clear();
        }

        private void MarkTutorialId(string id)
        {
            if (_data == null || string.IsNullOrEmpty(id)) return;
            if (_data.tutorialTipsSeen == null) _data.tutorialTipsSeen = new System.Collections.Generic.List<string>();
            if (!_data.tutorialTipsSeen.Contains(id)) _data.tutorialTipsSeen.Add(id);
            _save?.Save(_data);
        }

        private bool HasTutorialId(string id)
            => _data != null && _data.tutorialTipsSeen != null && _data.tutorialTipsSeen.Contains(id);

        private void RemoveTutorialId(string id)
        {
            if (_data != null && _data.tutorialTipsSeen != null) _data.tutorialTipsSeen.Remove(id);
        }

        private void OnFeatureOpened(string id)
        {
            if (_running || _tipShowing || _featureTipCooldown > 0f || _data == null
                || _data.tutorialStep < StepDone || string.IsNullOrEmpty(id)) return;
            var report = ServiceLocator.Get<OfflineReport>();
            if (report != null && report.Pending) return;
            if (FindAnyObjectByType<RewardRevealUI>() != null) return;
            if (!FeatureReady(id)) return;
            if (HasTutorialId("feature." + id)) return;
            MarkTutorialId("feature." + id);
            _featureTipCooldown = 20f;
            StartCoroutine(TipCard(id, null));
        }

        private bool FeatureReady(string id)
        {
            if (id == "stage") return true;
            if (id == "crafting")
            {
                CraftingService crafting = ServiceLocator.Get<CraftingService>();
                return crafting != null && (crafting.Points > 0L || crafting.CurrentTier > 0);
            }
            if (id == "captain")
            {
                CaptainService captains = ServiceLocator.Get<CaptainService>();
                return captains != null && captains.OwnedCount > 0;
            }
            if (id == "pets")
            {
                PetService pets = ServiceLocator.Get<PetService>();
                if (pets == null) return false;
                for (int species = 0; species < Pets.SpeciesCount; species++)
                    if (pets.Owned(species)) return true;
                return false;
            }
            if (id == "collection")
            {
                CardCollectionService collection = ServiceLocator.Get<CardCollectionService>();
                return collection != null && (collection.UnopenedPackCount > 0 || collection.OwnedCardCount > 0);
            }
            return true;
        }

        // ------------------------------------------------------------------ akış

        private IEnumerator Play()
        {
            _running = true;
            _skipped = false;
            _tapped = false;
            Build();
            // Tekrar oynatıldığında ekran bir önceki turdan kapalı kalmış ve gölgeler bir ipucu kartı
            // yüzünden söndürülmüş olabilir; ikisini de baştan aç.
            _view.SetVisible(true);
            _view.BlockInput(true);
            ShowSkip(false);
            _view.SetShadeActive(true);
            _view.SetShadeAlpha(0f);
            yield return null;                       // düzen bir kare otursun, ölçüler doğru çıksın

            yield return WelcomePart();
            if (!_skipped) yield return TourPart();
            if (!_skipped) yield return UpgradePart();
            if (!_skipped) yield return ButtonsPart();
            if (!_skipped) yield return FinalePart();

            Finish();
        }

        /// <summary>Oyuncunun gördüğü ilk tutorial karesi: Usta Max doğrudan karşılayıp turu başlatır.</summary>
        private IEnumerator WelcomePart()
        {
            HudVisible(false);
            _tapAdvances = true;
            _view.ClearTarget();
            _view.SetRing(false, false);
            _view.SetPips(0, 0);
            yield return _view.FadeShade(shadeColor.a, 0.28f);
            Card(Loc.T("egitim.hosgeldin_b"), Loc.T("egitim.hosgeldin_m"), null,
                 narratorFirstEntry != null ? narratorFirstEntry : narratorWelcome);
            Sound(SoundId.PanelOpen);
            yield return WaitContinue();
            yield return _view.HideCard();
            _tapAdvances = false;
        }

        /// <summary>
        /// Part 1. Keep the player's current island view while each station is introduced.
        /// </summary>
        private IEnumerator TourPart()
        {
            HudVisible(false);

            _view.ClearTarget();
            _tapAdvances = true;
            ShowSkip(false);
            yield return _view.FadeShade(0.34f, 0.3f);   // dünyayı hafifçe bastır, karartma değil

            for (int i = 0; i < Tour.Length && !_skipped; i++)
            {
                Vector3 look = Vector3.zero;
                bool onStation = Tour[i].station >= 0 && _op.StationAnchor(Tour[i].station, out look);
                Transform ride = Tour[i].ride ? _op.TrainEngine : null;

                _view.SetPips(Tour.Length, i);
                if (ride != null) _view.TargetWorldPoint(ride);
                else if (onStation && worldPin != null) _view.TargetWorldPoint(look);
                else _view.ClearTarget();
                Card(Loc.T("egitim.tur_" + Tour[i].key + "_b"), Loc.T("egitim.tur_" + Tour[i].key + "_m"),
                     Icon(Tour[i].icon), TourNarrator(i));
                Sound(SoundId.PanelOpen);
                yield return WaitContinue();
                _view.ClearTarget();
                if (i < Tour.Length - 1) yield return _view.HideCard();
            }

            _view.SetPips(0, 0);
            if (!_skipped) yield return _view.HideCard();
            HudVisible(true);
            _tapAdvances = false;
        }

        /// <summary>
        /// Part 2. The upgrade button and the income counter are explained without opening gameplay
        /// panels. The highlighted controls are visual examples only; DEVAM is the sole active control.
        /// </summary>
        private IEnumerator UpgradePart()
        {
            RectTransform upgrade = _hud != null ? _hud.UpgradeRect : null;
            if (upgrade == null) yield break;

            _tapAdvances = true;
            _view.SetShadeActive(true);
            _view.TargetUi(upgrade);
            yield return _view.FadeShade(shadeColor.a, 0.28f);
            _view.SetRing(true, false);
            Card(Loc.T("egitim.adim1_b"), Loc.T("egitim.adim1_m"), Icon(5), null);
            yield return WaitContinue();
            if (_skipped) yield break;

            yield return _view.HideCard();
            RectTransform rate = _hud != null ? _hud.RateRect : null;
            if (rate != null) _view.TargetUi(rate);
            else _view.ClearTarget();
            _view.SetRing(rate != null, false);
            Card(Loc.T("egitim.adim3_b"), Loc.T("egitim.adim3_m"), Icon(5), null);
            Sound(SoundId.Coin);
            yield return WaitContinue();
            if (_skipped) yield break;

            yield return _view.HideCard();
            _view.ClearTarget();
            _view.SetRing(false, false);
            Card(Loc.T("egitim.adim4_b"), Loc.T("egitim.adim4_m"), Icon(5), null);
            yield return WaitContinue();
            _tapAdvances = false;
        }

        /// <summary>
        /// İlk selamdan sonra ifadeler tur boyunca sırayla değişir. Mutlu poz burada özellikle
        /// kullanılmaz; oyuncu onu ancak bütün anlatımı tamamladığında görür.
        /// </summary>
        private Sprite TourNarrator(int index)
        {
            switch (index)
            {
                case 0: return narratorThoughtful != null ? narratorThoughtful : narratorWelcome;
                case 1: return narratorWarning != null ? narratorWarning : narratorWelcome;
                case 2: return narratorWelcome;
                case 3: return narratorThoughtful != null ? narratorThoughtful : narratorWelcome;
                case 4: return narratorWarning != null ? narratorWarning : narratorWelcome;
                default: return narratorWelcome;
            }
        }

        /// <summary>
        /// The closing pass. Every button on the HUD, one spotlight each, fast — the player has just
        /// bought a level and the game is about to hand the screen back, so this is the last moment they
        /// will look at the frame rather than through it.
        ///
        /// A button that is not on screen yet is skipped rather than pointed at, and only the ones
        /// actually shown are struck off the tip list: the ×2 shortcut unlocks later, and
        /// they still deserve their card the day they appear.
        /// </summary>
        private IEnumerator ButtonsPart()
        {
            if (_hud == null) yield break;
            yield return _view.HideCard();
            yield return new WaitForSecondsRealtime(0.2f);

            _tapAdvances = true;
            ShowSkip(false);
            bool wrote = false;

            for (int i = 0; i < Stops.Length && !_skipped; i++)
            {
                RectTransform target = StopRect(i);
                if (target == null || !target.gameObject.activeInHierarchy) continue;

                _view.TargetUi(target);
                _view.SetRing(true, false);
                Card(Loc.T("egitim." + Stops[i].key + "_b"), Loc.T("egitim." + Stops[i].key + "_m"), null, null);
                Sound(SoundId.Tick);

                if (Stops[i].tip != null && _data != null && !_data.tutorialTipsSeen.Contains(Stops[i].tip))
                {
                    _data.tutorialTipsSeen.Add(Stops[i].tip);
                    wrote = true;
                }

                yield return WaitContinue();
                yield return _view.HideCard();
            }

            // Tek yazma: dokuz durak için dokuz kez şifreleyip diske yazmanın anlamı yok.
            if (wrote && _save != null) _save.Save(_data);

            _view.ClearTarget();
            _view.SetRing(false, false);
            ShowSkip(false);
            _tapAdvances = false;
        }

        /// <summary>Son kontrol de anlatıldıktan sonra oyuncuyu mutlu pozla oyuna uğurlar.</summary>
        private IEnumerator FinalePart()
        {
            _view.ClearTarget();
            _view.SetRing(false, false);
            _view.SetPips(0, 0);
            _tapAdvances = true;
            Card(Loc.T("egitim.bitti_b"), Loc.T("egitim.bitti_m"), null,
                 narratorHappy != null ? narratorHappy : narratorWelcome);
            Sound(SoundId.PanelOpen);
            yield return WaitContinue();
            yield return _view.HideCard();
            _tapAdvances = false;
        }

        private RectTransform StopRect(int i)
        {
            switch (i)
            {
                case 0: return _hud.SettingsRect;
                case 1: return _hud.StoreRect;
                case 2: return _hud.DailyRect;
                case 3: return _hud.ContractRect;
                case 4: return _hud.AdRect;
                case 5: return _hud.OfferRect;
                default: return _hud.BoostRect;
            }
        }

        private void Finish()
        {
            if (_data != null)
            {
                _data.tutorialStep = StepDone;
                if (_save != null) _save.Save(_data);
            }
            ShowSkip(false);
            if (_view != null) _view.Clear();
            HudVisible(true);
            _running = false;
            // Kapanış kartının hemen ardına bir ipucu yapıştırmak eğitimi bitirmemiş gibi gösteriyor.
            // İlk ipucu için oyuncunun adayla bir süre yalnız kalması lazım.
            _tipTimer = 25f;
        }

        // ------------------------------------------------------------------ ipuçları

        /// <summary>
        /// Part 3. One card, the first time each of these becomes true, and never again. Deliberately
        /// polled on a slow timer rather than wired to seven events: every one of these is a state the
        /// player can also arrive at while the game was closed, and a missed event teaches nothing.
        /// </summary>
        private void TipTick()
        {
            if (_data == null || _data.tutorialStep < StepDone || _tipShowing) return;
            _tipTimer -= Time.unscaledDeltaTime;
            if (_tipTimer > 0f) return;
            _tipTimer = 2f;

            if (_contract != null && _contract.Claimable && Tip("kontrat", _hud != null ? _hud.ContractRect : null)) return;
            if (_hud != null && _hud.BoostReady && Tip("boost", _hud.BoostRect)) return;
            if (_daily != null && _daily.CanClaim() && Tip("gunluk", _hud != null ? _hud.DailyRect : null)) return;
            if (!ShopActive && PhaseMoved() && Tip("faz", null)) return;
            if (!ShopActive && _op != null && _op.StationLevelTotal(IslandEconomy.Mine) >= 6
                && Tip("genisletme", null)) return;
        }

        /// <summary>True once any station has been carried past its first phase — the island visibly rebuilt.</summary>
        private bool PhaseMoved()
        {
            if (_op == null) return false;
            for (int s = 0; s < _op.StationCount; s++)
                if (_op.PhaseForStation(s) > 1) return true;
            return false;
        }

        private bool Tip(string id, RectTransform target)
        {
            if (_data.tutorialTipsSeen == null)
                _data.tutorialTipsSeen = new System.Collections.Generic.List<string>();
            if (_data.tutorialTipsSeen.Contains(id)) return false;
            _data.tutorialTipsSeen.Add(id);
            if (_save != null) _save.Save(_data);
            StartCoroutine(TipCard(id, target));
            return true;
        }

        private IEnumerator TipCard(string id, RectTransform target)
        {
            _tipShowing = true;
            Build();
            _view.BlockInput(false);
            _view.SetShadeActive(false);              // ipucu hiçbir şeyi engellemez
            _view.SetPips(0, 0);
            _tapAdvances = true;
            ShowSkip(false);
            if (target != null) _view.TargetUi(target);
            else _view.ClearTarget();
            _view.SetRing(target != null, false);

            string localizationId = "ipucu_" + id;
            Card(Loc.T("egitim." + localizationId + "_b"), Loc.T("egitim." + localizationId + "_m"), null, null);
            Sound(SoundId.Tick);
            yield return WaitTap(tipSeconds);
            yield return _view.HideCard();

            _view.Clear();
            _tipTimer = 8f;
            _tipShowing = false;
            _tapAdvances = false;
        }

        // ------------------------------------------------------------------ kart

        /// <summary>One card through the presenter, with this flow's DEVAM and ATLA state.</summary>
        private void Card(string title, string body, Sprite icon, Sprite pose)
        {
            _view.ShowCard(title, body, pose, icon, _tapAdvances, _skipVisible);
        }

        private IEnumerator WaitTap(float seconds)
        {
            _tapped = false;
            float t = 0f;
            while (t < seconds && !_tapped && !_skipped)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            _tapped = false;
        }

        /// <summary>Main onboarding steps never time out; only the visible DEVAM button advances.</summary>
        private IEnumerator WaitContinue()
        {
            _tapped = false;
            while (!_tapped && !_skipped) yield return null;
            _tapped = false;
        }

        // ------------------------------------------------------------------ küçük anahtarlar

        private void ShowSkip(bool on) => _skipVisible = on;

        private void HudVisible(bool on)
        {
            if (_hudCanvas == null) return;
            _hudCanvas.enabled = on;
            if (!on) return;
            if (_hudFade == null) _hudFade = gameObject.GetComponent<CanvasGroup>();
            if (_hudFade == null) _hudFade = gameObject.AddComponent<CanvasGroup>();
            StopCoroutine("HudIn");
            StartCoroutine("HudIn");
        }

        private IEnumerator HudIn()
        {
            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / 0.3f;
                _hudFade.alpha = Mathf.Clamp01(t);
                yield return null;
            }
            _hudFade.alpha = 1f;
        }

        private Sprite Icon(int i)
        {
            return tourIcons != null && i >= 0 && i < tourIcons.Length ? tourIcons[i] : null;
        }

        private void Sound(SoundId id)
        {
            if (_audio != null) _audio.Play(id);
        }

        private void OnShadeTap()
        {
            if (_tapAdvances) { _tapped = true; Sound(SoundId.Tap); }
        }

        private void OnContinue()
        {
            if (!_tapAdvances) return;
            _tapped = true;
            Sound(SoundId.Tap);
            if (_haptic != null) _haptic.Light();
        }

        private void OnSkip()
        {
            _skipped = true;
            _tapped = true;
            Sound(SoundId.Back);
            if (_haptic != null) _haptic.Light();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            if (_view != null) Destroy(_view.gameObject);
        }

        // ══════════════════════════════════════════════════════════════════ kuruluş

        private void Build()
        {
            if (_view != null) return;
            if (font == null) font = FindFont();
            var art = new TutorialPresenter.Art
            {
                Panel = tutorialPanel,
                Button = tutorialButton,
                Medallion = medallion,
                SkipPill = skipPill,
                PipOn = pipOn,
                PipOff = pipOff,
                WorldPin = worldPin,
                TapHand = tapHand,
                PointPose = narratorPoint,
                DefaultPose = narratorWelcome != null ? narratorWelcome : narratorPoint,
                Font = font,
                Shade = shadeColor,
                Ring = ringColor,
                HandTapSeconds = handTapSeconds
            };
            var layout = new TutorialPresenter.Layout
            {
                CardMaxWidth = cardMaxWidth,
                NarratorHeightShare = narratorHeightShare,
                NarratorMinHeight = narratorShortest,
                NarratorMaxHeight = Mathf.Max(narratorShortest, narratorTallest),
                NarratorSink = narratorSink,
                EdgeMargin = edgeMargin,
                TopReserve = topReserve
            };
            _view = TutorialPresenter.Create(art, layout, SortingOrder);
            _view.Continued += OnContinue;
            _view.Skipped += OnSkip;
            _view.ShadeTapped += OnShadeTap;
        }

        private static TMP_FontAsset FindFont()
        {
            var any = FindObjectsByType<TMP_Text>(FindObjectsInactive.Include);
            for (int i = 0; i < any.Length; i++)
                if (any[i].font != null) return any[i].font;
            return null;
        }
    }
}
