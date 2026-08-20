using System.Collections;
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
    /// The onboarding. Players were arriving on an island that runs itself and could not tell what the
    /// trains and trucks were for, so nothing on the upgrade screen meant anything either.
    ///
    /// Three parts, in this order, and only the first two are ever seen back to back:
    ///
    ///   1. THE TOUR — the camera walks the production chain, one stop per stage, with a caption. This
    ///      coal leaves the mountain and comes back as money. The HUD is off and only DEVAM advances.
    ///   2. THE CONTROLS — the HUD comes back and Usta Max points at each relevant control. The control
    ///      stays visible inside the spotlight, but a full-screen blocker keeps it visual-only; DEVAM
    ///      remains the one active input until the onboarding finishes.
    ///   3. THE TIPS — one-shot cards fired much later, when the thing they describe first becomes
    ///      true: a contract falls due, a boost charges, an island becomes affordable. Never twice, and
    ///      never blocking.
    ///
    /// Built in code rather than authored, because almost none of it is a static layout: the shade is
    /// four quads solved every frame against a moving target rect, and the cards are the same card
    /// re-dressed sixteen times. What IS art arrives through the Inspector slots below, so the look
    /// stays tunable from the hierarchy like every other screen.
    ///
    /// Four shade quads leave a moving spotlight around the explained control. A separate transparent
    /// blocker covers even that hole, so highlighted game controls can be seen but cannot be pressed.
    /// </summary>
    public sealed class TutorialUI : MonoBehaviour
    {
        // Yalnızca iki durum var: hiç oynanmadı, ya da bitti. Otuz saniyelik bir eğitimin ortasından
        // devam etmek, oyunu ilk açtığında yarısını görmüş bir oyuncuya yarısını göstermek demek.
        private const int StepFresh = 0;
        private const int StepDone = 100;

        [Header("Kart görselleri")]
        [Tooltip("Kartın gövdesi — panel_ayarlar. Dilimli olduğu için her boya gerilir.")]
        [SerializeField] private Sprite cardPanel;
        [Tooltip("Kartın üstündeki başlık şeridi — serit_baslik.")]
        [SerializeField] private Sprite cardRibbon;
        [Tooltip("Yeni lacivert tutorial paneli. Atanırsa eski panel ve ayrı şerit yerine kullanılır.")]
        [SerializeField] private Sprite tutorialPanel;
        [Tooltip("Yeni altın çerçeveli mavi DEVAM butonu.")]
        [SerializeField] private Sprite tutorialButton;
        [Tooltip("Simgenin oturduğu yuvarlak — madalyon.")]
        [SerializeField] private Sprite medallion;
        [Tooltip("ATLA tuşunun zemini — pill_sayac.")]
        [SerializeField] private Sprite skipPill;
        [SerializeField] private Sprite pipOn;         // pip_dolu
        [SerializeField] private Sprite pipOff;        // pip_bos
        [Tooltip("Turda dünyadaki durağın üstünde duran iğne — rozet_buradasin.")]
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
        [Tooltip("Sola işaret pozu. Sağdaki hedefler için kod tarafından yatay çevrilir.")]
        [SerializeField] private Sprite narratorPoint;

        [Header("Tur simgeleri (maden, tren, depo, izabe, pazar, nakit)")]
        [SerializeField] private Sprite[] tourIcons = new Sprite[6];

        [Header("Zamanlama")]
        [Tooltip("Kameranın bir duraktan diğerine süzülme süresi.")]
        [SerializeField] private float flySeconds = 1.15f;
        [Tooltip("Bir durakta ekranın dikeyde kapsadığı EN AZ dünya birimi. Bölgesi ölçülebilen " +
                 "duraklar kendi sınırlarından daha genişini isteyebilir; bu taban, tren gibi tek " +
                 "gövdeli durakların da fazla yakın çekilmemesi için.")]
        [SerializeField] private float beatSpan = 110f;
        [Tooltip("Bölge sınırının yatay çapı bu katsayıyla çarpılıp kadraj ona açılır — kenarlarda " +
                 "nefes payı. Depo 76 birim; 1.5 ile 114'lük kadraja oturur ve tamamı görünür.")]
        [SerializeField] private float focusPadding = 1.6f;
        [Tooltip("Durağın ekranın ne kadar yukarısına oturacağı, ekran yüksekliğinin oranı olarak. " +
                 "Kart alt üçte biri kaplıyor; bölge merkezi kartın ÜSTÜNDE kalan alanın ortasına " +
                 "oturmalı — kapladığı oranın yarısı. DİKKAT: bu alanlar UI_HUD prefabında serileşmiş " +
                 "durumda; buradaki varsayılanı değiştirmek yetmez, prefabı da güncellemek gerekir.")]
        [SerializeField] private float beatRise = 0.15f;
        [Tooltip("Bağlamsal ipucu kartının ekranda kalma süresi.")]
        [SerializeField] private float tipSeconds = 5.5f;
        [Tooltip("Elin bir dokunuşunun süresi. Yavaş olsun: hızlısı dokunmuyor, titriyor gibi duruyor.")]
        [SerializeField] private float handTapSeconds = 0.9f;

        [Header("Renkler")]
        [SerializeField] private Color shadeColor = new Color(0.02f, 0.04f, 0.09f, 0.80f);
        [SerializeField] private Color inkColor = new Color32(0x2A, 0x3A, 0x5C, 0xFF);
        [SerializeField] private Color ringColor = new Color32(0xFF, 0xC8, 0x3C, 0xFF);

        // ------------------------------------------------------------------ ölçüler
        private const float CanvasWidth = 1920f;
        private const float CanvasHeight = 1080f;
        private const int SortingOrder = 250;      // hoş geldin ekranı 200; eğitim her şeyin üstünde

        // panel_ayarlar'ın dilim kenarı 120 px; çarpansız 940×330'luk kartta üst+alt kenar 240 px yer
        // yiyor ve iç kapsül bir şeride iniyordu — madalyon da yazı da süslü kenarın üstüne taşıyordu.
        // Ayarlar penceresinin kendi çözümü neyse o: çarpan 2, kenar 60'a iner, içerik 60'ın içinde kalır.
        private const float CardPpu = 2f;
        private const float CardInset = 60f;
        private const float CardWidth = 1160f;
        private const float CardHeight = 455f;
        private const float RibbonWidth = 660f;
        private const float RibbonHeight = 124f;
        private const float MedalSize = 126f;
        private const float PipSize = 22f;
        private const float ContinueWidth = 300f;
        private const float ContinueHeight = 112f;
        private const float CardBottomY = 34f;
        private const float CardTopY = -34f;
        private const float RingPad = 26f;         // deliğin kenarından halkanın dışına
        private const float CardGap = 44f;         // halkanın dışından kartın kenarına — kalan nefes payı
        private const float RibbonRise = 12f;
        private const float PipDrop = 42f;

        // ------------------------------------------------------------------ tur
        private struct Beat
        {
            public int station;    // -1 = geniş açı, tek bir istasyon değil
            public string key;     // egitim.tur_<key>
            public int icon;
            public bool ride;      // kamera duran bir noktayı değil, hareket eden treni izler
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
            new Stop { key = "ipucu_ada",     tip = "ada"     },
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
        private WorldIslands _world;
        private CoalOperation _op;
        private Canvas _hudCanvas;
        private CanvasGroup _hudFade;

        // ------------------------------------------------------------------ ekran
        private RectTransform _root;
        private RectTransform _canvasRect;
        private Image[] _shade;
        private Image _inputBlocker;
        private Image _ring;
        private Image _pulse;
        private Image _narrator;
        private bool _narratorLeft = true;
        private RectTransform _card;
        private CanvasGroup _cardFade;
        private Image _cardIcon;
        private TMP_Text _cardTitle;
        private TMP_Text _cardBody;
        private RectTransform _pips;
        private Image[] _pip;
        private RectTransform _next;
        private TMP_Text _nextText;
        private RectTransform _skip;
        private TMP_Text _skipText;
        private RectTransform _pin;
        private RectTransform _hand;

        private Sprite _ringSprite;

        // ------------------------------------------------------------------ durum
        private bool _running;
        private bool _skipped;
        private bool _tapped;
        private bool _tapAdvances;
        private RectTransform _targetRect;     // deliğin takip ettiği arayüz parçası
        private Vector3 _targetWorld;          // ya da dünyadaki bir nokta (iğne için)
        private Transform _ride;               // tren durağında kameranın kilitlendiği lokomotif
        private float _wait;                   // açılışta her şeyin oturmasını bekleme sayacı
        private float _tipTimer = 6f;
        private bool _tipShowing;

        // ------------------------------------------------------------------ giriş

        private void Start()
        {
            _data = ServiceLocator.Get<SaveData>();
            _save = ServiceLocator.Get<SaveService>();
            _wallet = ServiceLocator.Get<WalletService>();
            _contract = ServiceLocator.Get<ContractService>();
            _daily = ServiceLocator.Get<DailyRewardService>();
            _audio = ServiceLocator.Get<AudioService>();
            _haptic = ServiceLocator.Get<HapticService>();
            _hud = GetComponent<HudUI>();
            _hudCanvas = GetComponent<Canvas>();
            _world = FindAnyObjectByType<WorldIslands>();
            BuildDevButton();
        }

        private void Update()
        {
            if (_running) return;

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
            var ops = FindObjectsByType<CoalOperation>(FindObjectsSortMode.None);
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
        public bool IsShowing => _root != null && _root.gameObject.activeSelf;

        /// <summary>Replays the whole thing — the settings screen's EĞİTİM row.</summary>
        public void Replay()
        {
            if (_running || _data == null) return;
            _data.tutorialStep = StepFresh;
            _wait = 0f;
            _skipped = false;
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
            _root.gameObject.SetActive(true);
            BlockInput(true);
            ShowSkip(false);
            for (int i = 0; i < _shade.Length; i++)
            {
                _shade[i].enabled = true;
                var c = _shade[i].color; c.a = 0f; _shade[i].color = c;
            }
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
            _targetRect = null;
            _tapAdvances = true;
            ShowRing(false);
            ShowPips(false);
            SetHole(new Rect());
            yield return Fade(_shade[0].color.a, shadeColor.a, 0.28f);
            ShowCard(Loc.T("egitim.hosgeldin_b"), Loc.T("egitim.hosgeldin_m"), null,
                     CardBottomY, true,
                     narratorFirstEntry != null ? narratorFirstEntry : narratorWelcome);
            Sound(SoundId.PanelOpen);
            yield return WaitContinue();
            yield return HideCard();
            _tapAdvances = false;
        }

        /// <summary>
        /// Part 1. The HUD is switched off and the camera is taken off the player for the duration, so
        /// what is on screen is only the island and one line of text about the part of it being looked at.
        /// </summary>
        private IEnumerator TourPart()
        {
            var cam = Camera.main;
            var cc = FindAnyObjectByType<CameraController>();
            Vector3 home = cam.transform.position;
            Quaternion rot = cam.transform.rotation;
            if (cc != null) cc.enabled = false;
            HudVisible(false);

            SetHole(new Rect());
            _tapAdvances = true;
            ShowSkip(false);
            ShowPips(true);
            yield return Fade(_shade[0].color.a, 0.34f, 0.3f);   // dünyayı hafifçe bastır, karartma değil

            for (int i = 0; i < Tour.Length && !_skipped; i++)
            {
                // Son durak tek bir istasyon değil, zincirin tamamı: açılış çerçevesine geri süzülür.
                // Bölgesi ölçülebilen duraklarda hedef bölgenin MERKEZİ, kadraj bölgenin çapı — nokta
                // hedefler (zemin hizasındaki işaretçiler) depoyu iki yığın ve yarım baraka yapıyordu.
                Vector3 look = Vector3.zero;
                float span = beatSpan;
                bool onStation = Tour[i].station >= 0 && _op.StationAnchor(Tour[i].station, out look);
                Bounds area;
                if (onStation && _op.StationFocus(Tour[i].station, out area))
                {
                    look = area.center;
                    span = Mathf.Max(span, Mathf.Max(area.size.x, area.size.z) * focusPadding);
                }
                _ride = Tour[i].ride ? _op.TrainEngine : null;
                if (_ride != null) look = _ride.position;

                if (onStation) yield return Fly(cam, rot, look, span);
                else yield return FlyHome(cam, home);

                SetPips(i);
                _targetWorld = look;
                if (_pin != null) _pin.gameObject.SetActive(onStation && worldPin != null);
                ShowCard(Loc.T("egitim.tur_" + Tour[i].key + "_b"),
                         Loc.T("egitim.tur_" + Tour[i].key + "_m"),
                         Icon(Tour[i].icon), CardBottomY, true, TourNarrator(i));
                Sound(SoundId.PanelOpen);
                yield return _ride != null ? Ride(cam, rot) : WaitContinue();
                _ride = null;
                if (_pin != null) _pin.gameObject.SetActive(false);
                if (i < Tour.Length - 1) yield return HideCard();
            }

            ShowPips(false);
            if (!_skipped) yield return HideCard();
            yield return FlyHome(cam, home);
            if (cc != null) { cc.enabled = true; cc.FrameTo(home, rot, cc.CurrentZoom); }
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
            ShadeBlocks(true);
            _targetRect = upgrade;
            yield return Fade(_shade[0].color.a, shadeColor.a, 0.28f);
            ShowRing(true);
            ShowCard(Loc.T("egitim.adim1_b"), Loc.T("egitim.adim1_m"), Icon(5), PlaceFor(upgrade), false);
            yield return WaitContinue();
            if (_skipped) yield break;

            yield return HideCard();
            RectTransform rate = _hud != null ? _hud.RateRect : null;
            _targetRect = rate;
            ShowRing(rate != null);
            ShowCard(Loc.T("egitim.adim3_b"), Loc.T("egitim.adim3_m"), Icon(5),
                     rate != null ? PlaceFor(rate) : CardBottomY, false);
            Sound(SoundId.Coin);
            yield return WaitContinue();
            if (_skipped) yield break;

            yield return HideCard();
            _targetRect = null;
            ShowRing(false);
            SetHole(new Rect());
            ShowCard(Loc.T("egitim.adim4_b"), Loc.T("egitim.adim4_m"), Icon(5), CardBottomY, true);
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
            yield return HideCard();
            yield return new WaitForSecondsRealtime(0.2f);

            _tapAdvances = true;
            ShowSkip(false);
            bool wrote = false;

            for (int i = 0; i < Stops.Length && !_skipped; i++)
            {
                RectTransform target = StopRect(i);
                Rect r;
                if (target == null || !CanvasRectOf(target, out r)) continue;

                _targetRect = target;
                SetHole(r);
                PlaceRing(r);
                ShowRing(true);
                ShowCard(Loc.T("egitim." + Stops[i].key + "_b"), Loc.T("egitim." + Stops[i].key + "_m"),
                         null, PlaceFor(target), false);
                Sound(SoundId.Tick);

                if (Stops[i].tip != null && _data != null && !_data.tutorialTipsSeen.Contains(Stops[i].tip))
                {
                    _data.tutorialTipsSeen.Add(Stops[i].tip);
                    wrote = true;
                }

                yield return WaitContinue();
                yield return HideCard();
            }

            // Tek yazma: dokuz durak için dokuz kez şifreleyip diske yazmanın anlamı yok.
            if (wrote && _save != null) _save.Save(_data);

            _targetRect = null;
            ShowRing(false);
            ShowSkip(false);
            SetHole(new Rect());
            _tapAdvances = false;
        }

        /// <summary>Son kontrol de anlatıldıktan sonra oyuncuyu mutlu pozla oyuna uğurlar.</summary>
        private IEnumerator FinalePart()
        {
            _targetRect = null;
            ShowRing(false);
            ShowPips(false);
            SetHole(new Rect());
            _tapAdvances = true;
            ShowCard(Loc.T("egitim.bitti_b"), Loc.T("egitim.bitti_m"), null,
                     CardBottomY, true, narratorHappy != null ? narratorHappy : narratorWelcome);
            Sound(SoundId.PanelOpen);
            yield return WaitContinue();
            yield return HideCard();
            _tapAdvances = false;
        }

        private RectTransform StopRect(int i)
        {
            switch (i)
            {
                case 0: return _hud.SettingsRect;
                case 1: return _hud.StoreRect;
                case 2: return _hud.DailyRect;
                case 3: return _hud.MapRect;
                case 4: return _hud.ContractRect;
                case 5: return _hud.AdRect;
                case 6: return _hud.OfferRect;
                default: return _hud.BoostRect;
            }
        }

        /// <summary>"$180 / $500" — the balance against the price of the level being waited for.</summary>
        private string Progress(BigDouble price)
        {
            return string.Format(Loc.T("egitim.adim0_m"),
                "$" + NumberFormatter.Format(_wallet.Cash) + " / $" + NumberFormatter.Format(price));
        }

        private void Finish()
        {
            if (_data != null)
            {
                _data.tutorialStep = StepDone;
                if (_save != null) _save.Save(_data);
            }
            ShowRing(false);
            ShowSkip(false);
            ShowPips(false);
            _targetRect = null;
            if (_card != null) _card.gameObject.SetActive(false);
            if (_root != null) _root.gameObject.SetActive(false);
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
            if (NextIslandAffordable() && Tip("ada", _hud != null ? _hud.MapRect : null)) return;
            if (PhaseMoved() && Tip("faz", null)) return;
            if (_op != null && _op.StationLevelTotal(IslandEconomy.Mine) >= 6 && Tip("genisletme", null)) return;
        }

        private bool NextIslandAffordable()
        {
            if (_world == null || _wallet == null) return false;
            for (int i = 1; i < _world.Count; i++)
            {
                if (_world.IsOwned(i)) continue;
                return _wallet.CanAfford(new BigDouble(_world.UnlockCost(i)));
            }
            return false;
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
            if (_data.tutorialTipsSeen.Contains(id)) return false;
            _data.tutorialTipsSeen.Add(id);
            if (_save != null) _save.Save(_data);
            StartCoroutine(TipCard(id, target));
            return true;
        }

        private IEnumerator TipCard(string id, RectTransform target)
        {
            _tipShowing = true;
            if (_root == null) Build();
            _root.gameObject.SetActive(true);
            BlockInput(false);
            SetHole(new Rect());
            ShadeBlocks(false);              // ipucu hiçbir şeyi engellemez
            _targetRect = target;
            ShowRing(target != null);

            ShowCard(Loc.T("egitim.ipucu_" + id + "_b"), Loc.T("egitim.ipucu_" + id + "_m"),
                     null, PlaceFor(target), false);
            Sound(SoundId.Tick);
            yield return WaitTap(tipSeconds);
            yield return HideCard();

            ShowRing(false);
            _targetRect = null;
            ShadeBlocks(true);
            _root.gameObject.SetActive(false);
            _tipTimer = 8f;
            _tipShowing = false;
        }

        // ------------------------------------------------------------------ kamera

        /// <summary>Eases the camera onto a world point, framing <paramref name="span"/> world units vertically.</summary>
        private IEnumerator Fly(Camera cam, Quaternion rot, Vector3 look, float span)
        {
            yield return Glide(cam, cam.transform.position, Framing(cam, rot, look, span), flySeconds);
        }

        /// <summary>
        /// Where the camera has to stand to hold <paramref name="look"/> above the caption card. Sliding
        /// down the camera's own up-axis pushes the subject up the screen without tilting — the same trick
        /// <see cref="OperationCameraBoot"/> uses to keep the island clear of the HUD.
        /// </summary>
        private Vector3 Framing(Camera cam, Quaternion rot, Vector3 look, float span)
        {
            float vTan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float dist = span / (2f * vTan);
            return look - rot * Vector3.forward * dist - rot * Vector3.up * (beatRise * 2f * dist * vTan);
        }

        /// <summary>
        /// The TRAIN beat: the camera holds the locomotive while the caption is up. Eased rather than
        /// pinned — a camera welded to a moving object reads as the world sliding past, and the whole
        /// point of the shot is that the train is the thing moving.
        /// </summary>
        private IEnumerator Ride(Camera cam, Quaternion rot)
        {
            _tapped = false;
            while (!_tapped && !_skipped)
            {
                if (_ride != null)
                {
                    _targetWorld = _ride.position;
                    Vector3 want = Framing(cam, rot, _targetWorld, beatSpan);
                    cam.transform.position = Vector3.Lerp(cam.transform.position, want,
                                                          1f - Mathf.Exp(-3.2f * Time.unscaledDeltaTime));
                }
                yield return null;
            }
            _tapped = false;
        }

        private IEnumerator FlyHome(Camera cam, Vector3 home)
        {
            yield return Glide(cam, cam.transform.position, home, flySeconds);
        }

        private IEnumerator Glide(Camera cam, Vector3 from, Vector3 to, float seconds)
        {
            if ((to - from).sqrMagnitude < 0.01f) yield break;
            float t = 0f;
            while (t < 1f && !_skipped)
            {
                t += Time.unscaledDeltaTime / Mathf.Max(0.05f, seconds);
                float e = t >= 1f ? 1f : t * t * (3f - 2f * t);   // smoothstep: iki uçta da yumuşak
                cam.transform.position = Vector3.LerpUnclamped(from, to, e);
                yield return null;
            }
            cam.transform.position = to;
        }

        // ------------------------------------------------------------------ kart

        private void ShowCard(string title, string body, Sprite icon, float y, bool bottomAnchored,
                              Sprite narratorOverride = null)
        {
            if (_card == null) return;
            PlaceNarrator(_targetRect, narratorOverride);
            _card.gameObject.SetActive(true);
            bool newPanel = tutorialPanel != null;
            _cardTitle.text = newPanel ? "USTA MAX" : "USTA MAX  •  " + title;
            _cardBody.text = newPanel ? "<b>" + title + "</b>\n" + body : body;
            _cardIcon.transform.parent.gameObject.SetActive(icon != null);
            _cardIcon.sprite = icon;
            // Simgesiz kartta (ipuçları) yazı madalyonun boşluğuna kadar genişler.
            var brt = (RectTransform)_cardBody.transform;
            brt.offsetMin = new Vector2(icon != null ? CardInset + MedalSize + 24f : CardInset + 26f,
                                        brt.offsetMin.y);
            brt.offsetMax = new Vector2(-(_tapAdvances ? CardInset + ContinueWidth + 34f : CardInset + 26f),
                                        brt.offsetMax.y);
            _next.gameObject.SetActive(_tapAdvances);

            bool top = y < 0f;
            float side = _narratorLeft ? 1f : 0f;
            _card.anchorMin = _card.anchorMax = new Vector2(side, top ? 1f : 0f);
            _card.pivot = new Vector2(side, top ? 1f : 0f);
            _card.anchoredPosition = new Vector2(_narratorLeft ? -28f : 28f, y);
            if (_pips != null) _pips.gameObject.SetActive(_pipsOn && !top);
            StopCoroutine("CardIn");
            StartCoroutine("CardIn");
        }

        /// <summary>
        /// Max stands opposite the highlighted control and points at it. The source pose points left;
        /// placing him on the left flips it so one clean asset covers both screen directions.
        /// </summary>
        private void PlaceNarrator(RectTransform target, Sprite narratorOverride = null)
        {
            if (_narrator == null) return;

            Rect targetRect = new Rect();
            bool pointing = narratorOverride == null && target != null && narratorPoint != null &&
                            CanvasRectOf(target, out targetRect);
            float canvasWidth = _canvasRect != null ? _canvasRect.rect.width : CanvasWidth;
            float canvasHeight = _canvasRect != null ? _canvasRect.rect.height : CanvasHeight;
            _narratorLeft = !pointing || targetRect.center.x >= canvasWidth * 0.5f;

            Sprite sprite = narratorOverride != null ? narratorOverride :
                            (pointing ? narratorPoint : narratorWelcome);
            if (sprite == null) sprite = narratorPoint;
            _narrator.sprite = sprite;
            _narrator.gameObject.SetActive(sprite != null);
            if (sprite == null) return;

            RectTransform rect = _narrator.rectTransform;
            float height = Mathf.Min(canvasHeight * 0.82f, 780f);
            float width = height * sprite.rect.width / Mathf.Max(1f, sprite.rect.height);
            float maxWidth = canvasWidth * 0.36f;
            if (width > maxWidth)
            {
                height *= maxWidth / width;
                width = maxWidth;
            }

            rect.sizeDelta = new Vector2(width, height);
            rect.anchorMin = rect.anchorMax = new Vector2(_narratorLeft ? 0f : 1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(_narratorLeft ? width * 0.5f + 8f : -width * 0.5f - 8f, 0f);
            rect.localScale = new Vector3(pointing && _narratorLeft ? -1f : 1f, 1f, 1f);
        }

        private IEnumerator CardIn()
        {
            float t = 0f;
            Vector2 home = _card.anchoredPosition;
            float dir = _card.pivot.y > 0.5f ? 1f : -1f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / 0.34f;
                float e = t >= 1f ? 1f : 1f - Mathf.Pow(1f - t, 3f);           // ease-out-cubic
                float pop = t >= 1f ? 1f : 1f + 0.10f * Mathf.Sin(e * Mathf.PI);  // sona doğru hafif şişer
                _cardFade.alpha = Mathf.Clamp01(t * 1.6f);
                _card.anchoredPosition = home + new Vector2(0f, (1f - e) * 110f * dir);
                _card.localScale = new Vector3(pop, pop, 1f);
                yield return null;
            }
            _card.anchoredPosition = home;
            _card.localScale = Vector3.one;
            _cardFade.alpha = 1f;
        }

        private IEnumerator HideCard()
        {
            if (_card == null || !_card.gameObject.activeSelf) yield break;
            StopCoroutine("CardIn");
            float t = 0f;
            Vector2 home = _card.anchoredPosition;
            float dir = _card.pivot.y > 0.5f ? 1f : -1f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / 0.18f;
                float e = t >= 1f ? 1f : t * t;
                _cardFade.alpha = 1f - e;
                _card.anchoredPosition = home + new Vector2(0f, e * 70f * dir);
                yield return null;
            }
            _card.gameObject.SetActive(false);
            if (_narrator != null) _narrator.gameObject.SetActive(false);
            _card.anchoredPosition = home;
            _cardFade.alpha = 1f;
        }

        /// <summary>
        /// Which end of the screen the card goes to so that it cannot cover the control it is talking
        /// about. Halves were not enough: a button sitting just above the middle still ended up under a
        /// card anchored to the top, and the daily-reward button — the one the card was pointing at —
        /// was the one it hid. This measures both bands against the highlighted rect instead of guessing.
        ///
        /// The bottom is the default: that is where the thumb already is, and a card there covers the
        /// world rather than the HUD.
        /// </summary>
        private float PlaceFor(RectTransform target)
        {
            Rect r;
            if (!CanvasRectOf(target, out r)) return CardBottomY;

            // Halka deliğin kenarından dışarı taşıyor; kart onun da dışında kalmalı.
            float keepLo = r.yMin - RingPad - CardGap;
            float keepHi = r.yMax + RingPad + CardGap;

            float bLo, bHi, tLo, tHi;
            CardBand(CardBottomY, out bLo, out bHi);
            CardBand(CardTopY, out tLo, out tHi);

            float bottomOver = Mathf.Min(bHi, keepHi) - Mathf.Max(bLo, keepLo);
            if (bottomOver <= 0f) return CardBottomY;
            float topOver = Mathf.Min(tHi, keepHi) - Mathf.Max(tLo, keepLo);
            if (topOver <= 0f) return CardTopY;
            // İkisi de çakışıyorsa (ekranı boydan boya kaplayan bir hedef) az olanı seç.
            return bottomOver <= topOver ? CardBottomY : CardTopY;
        }

        /// <summary>The strip of canvas the card would occupy at <paramref name="y"/>, ribbon and pips included.</summary>
        private void CardBand(float y, out float lo, out float hi)
        {
            float h = _canvasRect != null ? _canvasRect.rect.height : CanvasHeight;
            float bottom = y < 0f ? h + y - CardHeight : y;   // eksi y = üstten sarkar
            lo = bottom - PipDrop;
            hi = bottom + CardHeight + RibbonRise;
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

        // ------------------------------------------------------------------ delik, halka, iğne

        private void LateUpdate()
        {
            if (_root == null || !_root.gameObject.activeSelf) return;

            if (_targetRect != null)
            {
                Rect r;
                if (CanvasRectOf(_targetRect, out r))
                {
                    SetHole(r);
                    PlaceRing(r);
                }
            }

            if (_pulse != null && _pulse.enabled)
            {
                // Nabız: halka hafifçe büyüyüp soluyor. Duran bir çerçeve göze çarpmıyor.
                float p = Mathf.PingPong(Time.unscaledTime * 1.25f, 1f);
                float s = 1f + 0.09f * p;
                _pulse.rectTransform.localScale = new Vector3(s, s, 1f);
                var c = ringColor; c.a = 0.55f * (1f - p);
                _pulse.color = c;
            }

            // El yalnız oyuncunun gerçekten o kontrole dokunması beklenirken çıkar. Kartın "devam"
            // beklediği duraklarda ekranın her yeri geçerli bir dokunuş, orada el yanlış yeri gösterir.
            if (_hand != null)
            {
                bool want = _targetRect != null && _ring != null && _ring.enabled && !_tapAdvances;
                if (_hand.gameObject.activeSelf != want) _hand.gameObject.SetActive(want);
                if (want)
                {
                    float p = Mathf.PingPong(Time.unscaledTime * (2f / Mathf.Max(0.1f, handTapSeconds)), 1f);
                    float s = 1f - 0.13f * (p * p * (3f - 2f * p));
                    _hand.localScale = new Vector3(s, s, 1f);
                }
            }

            if (_pin != null && _pin.gameObject.activeSelf) PlacePin();
        }

        private void PlacePin()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 sp = cam.WorldToScreenPoint(_targetWorld);
            if (sp.z < 0f) { _pin.gameObject.SetActive(false); return; }
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, sp, null, out local);
            float bob = Mathf.Sin(Time.unscaledTime * 2.6f) * 12f;
            _pin.anchoredPosition = local + new Vector2(0f, 96f + bob);
        }

        /// <summary>The four quads that surround <paramref name="hole"/>. An empty hole shades the lot.</summary>
        private void SetHole(Rect hole)
        {
            if (_shade == null) return;
            float w = CanvasWidth, h = CanvasHeight;
            if (_canvasRect != null) { w = _canvasRect.rect.width; h = _canvasRect.rect.height; }

            if (hole.width <= 0f || hole.height <= 0f)
                hole = new Rect(w * 0.5f, h * 0.5f, 0f, 0f);

            float l = Mathf.Clamp(hole.xMin, 0f, w), r = Mathf.Clamp(hole.xMax, 0f, w);
            float b = Mathf.Clamp(hole.yMin, 0f, h), t = Mathf.Clamp(hole.yMax, 0f, h);

            Quad(_shade[0], 0f, t, w, h - t);        // üst
            Quad(_shade[1], 0f, 0f, w, b);           // alt
            Quad(_shade[2], 0f, b, l, t - b);        // sol
            Quad(_shade[3], r, b, w - r, t - b);     // sağ
        }

        private static void Quad(Image img, float x, float y, float w, float h)
        {
            var rt = img.rectTransform;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(Mathf.Max(0f, w), Mathf.Max(0f, h));
        }

        private void PlaceRing(Rect hole)
        {
            if (_ring == null) return;
            var size = new Vector2(hole.width + RingPad * 2f, hole.height + RingPad * 2f);
            _ring.rectTransform.anchoredPosition = hole.center;
            _ring.rectTransform.sizeDelta = size;
            _pulse.rectTransform.anchoredPosition = hole.center;
            _pulse.rectTransform.sizeDelta = size;

            if (_hand == null) return;
            float hh = Mathf.Clamp(hole.height * 0.95f, 110f, 190f);
            _hand.sizeDelta = new Vector2(hh * (256f / 340f), hh);
            _hand.anchoredPosition = hole.center;
        }

        /// <summary>A UI rect in this canvas's own bottom-left-origin coordinates.</summary>
        private bool CanvasRectOf(RectTransform target, out Rect result)
        {
            result = new Rect();
            if (target == null || _canvasRect == null || !target.gameObject.activeInHierarchy) return false;

            target.GetWorldCorners(_corners);
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                Vector2 sp = RectTransformUtility.WorldToScreenPoint(null, _corners[i]);
                Vector2 local;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, sp, null, out local)) return false;
                local += _canvasRect.rect.size * 0.5f;
                if (local.x < minX) minX = local.x;
                if (local.y < minY) minY = local.y;
                if (local.x > maxX) maxX = local.x;
                if (local.y > maxY) maxY = local.y;
            }
            result = new Rect(minX, minY, maxX - minX, maxY - minY);
            return result.width > 1f && result.height > 1f;
        }

        private readonly Vector3[] _corners = new Vector3[4];

        // ------------------------------------------------------------------ küçük anahtarlar

        /// <summary>
        /// Whether the four quads are drawn at all. Off means the tutorial is watching rather than
        /// holding: no tint, and — the part that matters — no raycast target, so every control under
        /// them works normally.
        /// </summary>
        private void ShadeBlocks(bool on)
        {
            if (_shade == null) return;
            for (int i = 0; i < _shade.Length; i++) _shade[i].enabled = on;
        }

        private void BlockInput(bool on)
        {
            if (_inputBlocker == null) return;
            _inputBlocker.enabled = on;
            _inputBlocker.raycastTarget = on;
        }

        private void ShowRing(bool on)
        {
            if (_ring == null) return;
            _ring.enabled = on;
            _pulse.enabled = on;
        }

        private void ShowSkip(bool on)
        {
            if (_skip != null) _skip.gameObject.SetActive(on);
        }

        private bool _pipsOn;

        private void ShowPips(bool on)
        {
            _pipsOn = on;
            if (_pips != null) _pips.gameObject.SetActive(on);
        }

        private void SetPips(int live)
        {
            if (_pip == null) return;
            for (int i = 0; i < _pip.Length; i++)
            {
                _pip[i].sprite = i <= live ? pipOn : pipOff;
                if (_pip[i].sprite == null) _pip[i].color = i <= live ? ringColor : new Color(1f, 1f, 1f, 0.3f);
            }
        }

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

        private IEnumerator Fade(float from, float to, float seconds)
        {
            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / Mathf.Max(0.05f, seconds);
                float a = Mathf.Lerp(from, to, t >= 1f ? 1f : t);
                for (int i = 0; i < _shade.Length; i++)
                {
                    var c = _shade[i].color; c.a = a; _shade[i].color = c;
                }
                yield return null;
            }
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
            if (_root != null) Destroy(_root.gameObject);
        }

        // ══════════════════════════════════════════════════════════════════ kuruluş

        private void Build()
        {
            if (_root != null) return;
            if (font == null) font = FindFont();
            _ringSprite = MakeRing(112, 44f, 9f);

            var go = new GameObject("UI_Egitim", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(CanvasWidth, CanvasHeight);
            scaler.matchWidthOrHeight = 0.5f;
            _root = (RectTransform)go.transform;
            _canvasRect = _root;

            BuildShade();
            BuildInputBlocker();
            BuildRing();
            BuildPin();
            BuildNarrator();
            BuildCard();
            BuildSkip();
        }

        private void BuildShade()
        {
            _shade = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject("Golge_" + i, typeof(RectTransform), typeof(Image), typeof(Button));
                var rt = (RectTransform)go.transform;
                rt.SetParent(_root, false);
                rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
                _shade[i] = go.GetComponent<Image>();
                var c = shadeColor; c.a = 0f;
                _shade[i].color = c;
                var b = go.GetComponent<Button>();
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(OnShadeTap);
            }
            SetHole(new Rect());
        }

        /// <summary>
        /// Sits above every game canvas and every spotlight hole, but below the tutorial artwork and
        /// DEVAM button. This is what makes highlighted HUD controls visual-only until onboarding ends.
        /// </summary>
        private void BuildInputBlocker()
        {
            var go = new GameObject("GirisKilidi", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_root, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            _inputBlocker = go.GetComponent<Image>();
            _inputBlocker.color = new Color(0f, 0f, 0f, 0.001f);
            _inputBlocker.raycastTarget = true;
        }

        private void BuildRing()
        {
            _pulse = RingImage("Nabiz");
            _ring = RingImage("Halka");
            var c = ringColor; c.a = 0.95f;
            _ring.color = c;
            ShowRing(false);
            BuildHand();          // halkadan sonra: el her zaman halkanın üstünde çizilsin
        }

        /// <summary>
        /// The hand that taps. Its pivot sits on the fingertip, so wherever the hand is put is where the
        /// finger lands, and the press animation — shrinking towards that pivot — keeps the fingertip
        /// still while the rest of the hand moves into it.
        /// </summary>
        private void BuildHand()
        {
            if (tapHand == null) return;
            var go = new GameObject("El", typeof(RectTransform), typeof(Image));
            _hand = (RectTransform)go.transform;
            _hand.SetParent(_root, false);
            _hand.anchorMin = _hand.anchorMax = Vector2.zero;
            _hand.pivot = new Vector2(0.40f, 0.87f);     // parmak ucunun resim içindeki yeri
            var img = go.GetComponent<Image>();
            img.sprite = tapHand;
            img.raycastTarget = false;
            img.preserveAspect = true;
            go.SetActive(false);
        }

        private Image RingImage(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_root, false);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            var img = go.GetComponent<Image>();
            img.sprite = _ringSprite;
            img.type = Image.Type.Sliced;
            img.color = ringColor;
            img.raycastTarget = false;
            return img;
        }

        private void BuildPin()
        {
            var go = new GameObject("Igne", typeof(RectTransform), typeof(Image));
            _pin = (RectTransform)go.transform;
            _pin.SetParent(_root, false);
            _pin.anchorMin = _pin.anchorMax = new Vector2(0.5f, 0.5f);
            _pin.sizeDelta = new Vector2(128f, 128f);
            var img = go.GetComponent<Image>();
            img.sprite = worldPin;
            img.raycastTarget = false;
            img.preserveAspect = true;
            _pin.gameObject.SetActive(false);
        }

        private void BuildNarrator()
        {
            var go = new GameObject("UstaMax", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_root, false);
            _narrator = go.GetComponent<Image>();
            _narrator.raycastTarget = false;
            _narrator.preserveAspect = true;
            go.SetActive(false);
        }

        private void BuildCard()
        {
            bool newPanel = tutorialPanel != null;
            var go = new GameObject("Kart", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            _card = (RectTransform)go.transform;
            _card.SetParent(_root, false);
            _card.sizeDelta = new Vector2(CardWidth, CardHeight);
            _cardFade = go.GetComponent<CanvasGroup>();
            var body = go.GetComponent<Image>();
            body.sprite = newPanel ? tutorialPanel : (cardPanel != null ? cardPanel : UiSkin.Panel);
            body.type = newPanel ? Image.Type.Simple : Image.Type.Sliced;
            body.pixelsPerUnitMultiplier = CardPpu;
            body.color = newPanel || cardPanel != null ? Color.white : new Color(0.97f, 0.95f, 0.88f, 1f);
            body.raycastTarget = false;

            // Yeni panelin altın isimliği görselin kendi içinde. Eski panelde ayrı şerit kullanılır.
            var rib = new GameObject("Serit", typeof(RectTransform), typeof(Image));
            var rrt = (RectTransform)rib.transform;
            rrt.SetParent(_card, false);
            var ribImg = rib.GetComponent<Image>();
            if (newPanel)
            {
                rrt.anchorMin = rrt.anchorMax = new Vector2(0f, 1f);
                rrt.pivot = new Vector2(0f, 1f);
                rrt.sizeDelta = new Vector2(338f, 88f);
                rrt.anchoredPosition = new Vector2(84f, -22f);
                ribImg.enabled = false;
            }
            else
            {
                rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 1f);
                rrt.sizeDelta = new Vector2(RibbonWidth, RibbonHeight);
                rrt.anchoredPosition = new Vector2(0f, 8f);
                ribImg.sprite = cardRibbon != null ? cardRibbon : UiSkin.ButtonYellow;
                ribImg.type = Image.Type.Sliced;
            }
            ribImg.raycastTarget = false;
            _cardTitle = Text(rrt, "Baslik", newPanel ? 40f : 52f, TextAlignmentOptions.Center,
                              newPanel ? new Color32(0x58, 0x32, 0x0B, 0xFF) : Color.white);
            var trt = (RectTransform)_cardTitle.transform;
            trt.offsetMin = newPanel ? new Vector2(24f, 14f) : new Vector2(120f, 26f);
            trt.offsetMax = newPanel ? new Vector2(-24f, -10f) : new Vector2(-120f, -12f);
            _cardTitle.enableAutoSizing = true;
            _cardTitle.fontSize = newPanel ? 40f : 52f;
            _cardTitle.fontSizeMin = newPanel ? 26f : 30f;
            _cardTitle.fontSizeMax = newPanel ? 40f : 52f;

            // madalyon + simge
            var med = new GameObject("Madalyon", typeof(RectTransform), typeof(Image));
            var mrt = (RectTransform)med.transform;
            mrt.SetParent(_card, false);
            mrt.anchorMin = mrt.anchorMax = new Vector2(0f, 0.5f);
            mrt.sizeDelta = new Vector2(MedalSize, MedalSize);
            // Merkez = kenar + yarıçap: madalyon iç kapsülün İÇİNDE durur, çizgisine binmez.
            // -20: şerit üstten pay yediği için içeriğin görsel ortası kart ortasının azıcık altında.
            mrt.anchoredPosition = new Vector2(CardInset + MedalSize * 0.5f, -20f);
            var medImg = med.GetComponent<Image>();
            medImg.sprite = medallion;
            medImg.raycastTarget = false;
            medImg.preserveAspect = true;
            medImg.enabled = medallion != null;

            var ico = new GameObject("Simge", typeof(RectTransform), typeof(Image));
            var irt = (RectTransform)ico.transform;
            irt.SetParent(mrt, false);
            irt.anchorMin = new Vector2(0.5f, 0.5f);
            irt.anchorMax = new Vector2(0.5f, 0.5f);
            irt.sizeDelta = new Vector2(MedalSize * 0.62f, MedalSize * 0.62f);
            _cardIcon = ico.GetComponent<Image>();
            _cardIcon.raycastTarget = false;
            _cardIcon.preserveAspect = true;

            // gövde yazısı — sol kenarı simgeye göre ShowCard ayarlıyor (ipuçlarının simgesi yok)
            _cardBody = Text(_card, "Metin", 42f, TextAlignmentOptions.Left,
                             newPanel ? Color.white : inkColor);
            var brt = (RectTransform)_cardBody.transform;
            brt.offsetMin = new Vector2(CardInset + MedalSize + 24f, newPanel ? 72f : CardInset - 4f);
            brt.offsetMax = new Vector2(-(CardInset + 26f), newPanel ? -116f : -(RibbonHeight * 0.5f + 8f + 22f));
            _cardBody.enableAutoSizing = true;
            _cardBody.fontSize = 42f;
            _cardBody.fontSizeMin = 28f;
            _cardBody.fontSizeMax = 44f;

            // Referanstaki gibi açık bir DEVAM düğmesi; bütün diğer girişler kilitliyken tek ilerleme yolu.
            var nx = new GameObject("BtnDevam", typeof(RectTransform), typeof(Image), typeof(Button));
            _next = (RectTransform)nx.transform;
            _next.SetParent(_card, false);
            _next.anchorMin = _next.anchorMax = newPanel ? new Vector2(1f, 0f) : new Vector2(1f, 0.5f);
            _next.pivot = newPanel ? new Vector2(1f, 0f) : new Vector2(0.5f, 0.5f);
            _next.sizeDelta = new Vector2(ContinueWidth, ContinueHeight);
            _next.anchoredPosition = newPanel
                ? new Vector2(-54f, 48f)
                : new Vector2(-(CardInset + ContinueWidth * 0.5f - 8f), -20f);
            var nimg = nx.GetComponent<Image>();
            nimg.sprite = tutorialButton != null ? tutorialButton : UiSkin.ButtonBlue;
            nimg.type = tutorialButton != null ? Image.Type.Simple : Image.Type.Sliced;
            nimg.color = Color.white;
            var nextButton = nx.GetComponent<Button>();
            nextButton.targetGraphic = nimg;
            nextButton.onClick.AddListener(OnContinue);
            TapBounce.Attach(nextButton);
            _nextText = Text(_next, "Yazi", 46f, TextAlignmentOptions.Center, Color.white);
            _nextText.text = Loc.T("egitim.devam");
            _nextText.enableAutoSizing = true;
            _nextText.fontSizeMin = 30f;
            _nextText.fontSizeMax = 46f;
            _next.gameObject.SetActive(false);

            BuildPips();
            _card.gameObject.SetActive(false);
        }

        private void BuildPips()
        {
            var go = new GameObject("Adimlar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            _pips = (RectTransform)go.transform;
            _pips.SetParent(_card, false);
            _pips.anchorMin = _pips.anchorMax = new Vector2(0.5f, 0f);
            _pips.pivot = new Vector2(0.5f, 1f);
            _pips.sizeDelta = new Vector2(CardWidth, 40f);
            _pips.anchoredPosition = new Vector2(0f, -18f);
            var lay = go.GetComponent<HorizontalLayoutGroup>();
            lay.spacing = 14f;
            lay.childAlignment = TextAnchor.MiddleCenter;
            lay.childForceExpandWidth = false;
            lay.childForceExpandHeight = false;

            _pip = new Image[Tour.Length];
            for (int i = 0; i < Tour.Length; i++)
            {
                var p = new GameObject("Pip_" + i, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                p.transform.SetParent(_pips, false);
                var le = p.GetComponent<LayoutElement>();
                le.preferredWidth = PipSize;
                le.preferredHeight = PipSize;
                _pip[i] = p.GetComponent<Image>();
                _pip[i].sprite = pipOff;
                _pip[i].raycastTarget = false;
                _pip[i].preserveAspect = true;
            }
            _pips.gameObject.SetActive(false);
        }

        private void BuildSkip()
        {
            var go = new GameObject("BtnAtla", typeof(RectTransform), typeof(Image), typeof(Button));
            _skip = (RectTransform)go.transform;
            _skip.SetParent(_root, false);
            _skip.anchorMin = _skip.anchorMax = new Vector2(1f, 1f);
            _skip.pivot = new Vector2(1f, 1f);
            _skip.sizeDelta = new Vector2(240f, 96f);
            _skip.anchoredPosition = new Vector2(-40f, -150f);
            var img = go.GetComponent<Image>();
            img.sprite = skipPill != null ? skipPill : UiSkin.Flat;
            img.type = Image.Type.Sliced;
            img.color = skipPill != null ? Color.white : new Color(0f, 0f, 0f, 0.55f);
            var b = go.GetComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(OnSkip);
            // Yalnız bu butona: kalkanlar da Button ama onlar dokunuş yiyen görünmez levhalar,
            // ölçeklenirlerse aralarından delik açılır.
            TapBounce.Attach(b);
            // Hap koyu lacivert; kartın mürekkebi de öyle. Bu yazı beyaz olmak zorunda.
            _skipText = Text(_skip, "Yazi", 40f, TextAlignmentOptions.Center, Color.white);
            _skipText.text = Loc.T("egitim.atla");
            _skip.gameObject.SetActive(false);
        }

        private TMP_Text Text(RectTransform parent, string name, float size, TextAlignmentOptions align, Color ink)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var t = go.AddComponent<TextMeshProUGUI>();
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            if (font != null) t.font = font;
            t.fontSize = size;
            t.alignment = align;
            t.color = ink;
            t.raycastTarget = false;
            return t;
        }

        private static TMP_FontAsset FindFont()
        {
            var any = FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < any.Length; i++)
                if (any[i].font != null) return any[i].font;
            return null;
        }

        /// <summary>
        /// The highlight outline, generated rather than authored: it has to fit a 1200-wide button and a
        /// 150-wide pill equally well, which a stretched PNG cannot do. A rounded-rect stroke drawn from
        /// its signed distance field, then 9-sliced past the corner radius so the corners never squash.
        /// </summary>
        private static Sprite MakeRing(int size, float radius, float stroke)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[size * size];
            float half = size * 0.5f;
            float inner = half - radius;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - half) - inner;
                    float dy = Mathf.Abs(y + 0.5f - half) - inner;
                    float qx = Mathf.Max(dx, 0f), qy = Mathf.Max(dy, 0f);
                    float d = Mathf.Sqrt(qx * qx + qy * qy) + Mathf.Min(Mathf.Max(dx, dy), 0f) - radius;
                    // |d| kenardan uzaklık; şeridin içinde 1, dışında 0, arada bir piksellik yumuşama
                    float a = 1f - Mathf.Clamp01((Mathf.Abs(d) - stroke * 0.5f) / 1.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            float border = radius + stroke;
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                 SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// Watch the opening again without wiping the save. Behind the same guard as the station
        /// screen's TEST MODU button, and deliberately not a settings row: the settings panel is full
        /// to its bottom border, and a seventh row only fits there by squashing the other six.
        /// </summary>
        private void BuildDevButton()
        {
            var go = new GameObject("EgitimTekrar", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(transform, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(300f, 54f);
            rt.anchoredPosition = new Vector2(12f, -430f);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.24f, 0.27f, 0.32f, 0.92f);
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(Replay);
            var label = Text(rt, "Etiket", 24f, TextAlignmentOptions.Center, Color.white);
            label.text = "EĞİTİMİ TEKRAR OYNAT";
        }
#else
        private void BuildDevButton() { }
#endif
    }
}
