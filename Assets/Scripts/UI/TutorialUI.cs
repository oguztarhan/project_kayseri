using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The onboarding, in two parts:
    ///
    ///   1. THE BASICS — the mining shop's whole loop, taught by watching it and then doing it: a bench
    ///      crafts, the carrier stocks the shelf, a customer pays, the cash buys the bench's first speed
    ///      level. Most lessons end when the game does the thing, not when the player taps DEVAM; the
    ///      order and what has been taught live in <see cref="TutorialProgress"/>, so a player who
    ///      closes the app halfway resumes at the first lesson missing.
    ///   2. THE TIPS — one-shot cards fired much later, when the thing they describe first becomes
    ///      true: a contract falls due, a boost charges, a feature screen opens. Never twice, and
    ///      never blocking.
    ///
    /// The camera never moves for any of it. The lessons point at the world as the player left it, and
    /// hold the camera's drag and pinch while they point, so the spotlight cannot slide off its target.
    ///
    /// This decides what to show and when. Drawing it — Max, his card, the spotlight — is
    /// <see cref="TutorialPresenter"/>'s, built on first use from the art in the Inspector slots below,
    /// so the look stays tunable from the hierarchy like every other screen.
    /// </summary>
    public sealed class TutorialUI : MonoBehaviour
    {
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
        [Tooltip("Düşünceli/açıklayıcı poz: taşıma dersi ve hedefsiz özellik tanıtımları.")]
        [SerializeField] private Sprite narratorThoughtful;
        [Tooltip("Önemli noktayı vurgulayan ciddi poz: satış dersi.")]
        [SerializeField] private Sprite narratorWarning;
        [Tooltip("Temel dersler tamamlandığında gösterilen mutlu poz.")]
        [SerializeField] private Sprite narratorHappy;
        [Tooltip("Sola işaret pozu. Sağdaki hedefler için Max sola geçer ve poz yatay çevrilir.")]
        [SerializeField] private Sprite narratorPoint;

        [Header("Tur simgeleri (maden, tren, depo, izabe, pazar, nakit)")]
        [Tooltip("Derslerde yalnızca sonuncusu, nakit, kullanılır: satış ve para biriktirme kartlarında.")]
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
        [Tooltip("İzleme derslerinde (üretim, taşıma, satış) DEVAM'ın ortaya çıkmasından önceki bekleme. "
                 + "Dükkân bir sebeple takılırsa oyuncu aynı kartta kalmasın.")]
        [SerializeField, Min(3f)] private float lessonPatienceSeconds = 20f;
        [Tooltip("Bir izleme dersi tamamlandıktan sonra kartın kalkmadan önce beklediği süre: "
                 + "oyuncu sonucu görsün.")]
        [SerializeField, Min(0f)] private float lessonResultSeconds = 0.7f;
        [Tooltip("İzleme dersinin kartı, dükkân işi daha önce bitirse bile en az bu kadar ekranda kalır. "
                 + "Hazırlanmakta olan bir kazma kartı iki saniyede kapatıp okunmaz yapıyordu.")]
        [SerializeField, Min(0f)] private float lessonReadSeconds = 3.5f;
        [Tooltip("Oyun ilerledikçe gelen derslerde (ikinci tezgâh, görevler) DEVAM'ın çıkmasından önceki bekleme. "
                 + "Bu dersler hiçbir şeyi kilitlemez; oyuncu isterse yapar, istemezse kartı kapatır.")]
        [SerializeField, Min(3f)] private float introPatienceSeconds = 8f;
        [Tooltip("İki ipucu ya da ilerleme dersi arasında en az bu kadar saniye geçer: arka arkaya kart yağmasın.")]
        [SerializeField, Min(2f)] private float introGapSeconds = 30f;
        [Tooltip("Denize açıl dersi, oyuncu tezgâhlarda toplam bu kadar yükseltme aldıktan sonra gelir: "
                 + "önce dükkânı tanısın, sonra açıklara çıksın.")]
        [SerializeField, Min(0)] private int seaAfterUpgrades = 3;

        [Header("Renkler")]
        [SerializeField] private Color shadeColor = new Color(0.02f, 0.04f, 0.09f, 0.80f);
        [Tooltip("İzleme derslerinin karartması. Oyuncu dükkânın işleyişini görsün diye hafif.")]
        [SerializeField, Range(0f, 1f)] private float watchShadeAlpha = 0.5f;
        [SerializeField] private Color ringColor = new Color32(0xFF, 0xC8, 0x3C, 0xFF);

        [Header("Başka ekranlarla çakışma")]
        [Tooltip("Bu sıralamada ya da üstünde bir ekran adanın ortasını kapatınca eğitim gizlenip bekler, "
                 + "ekran kapanınca kaldığı yerden sürer. Daha Fazla menüsü 104, HUD ve tezgâh paneli altında.")]
        [SerializeField] private int screenSortingFloor = 104;

        private const int SortingOrder = 250;      // hoş geldin ekranı 200; eğitim her şeyin üstünde
        private const int CashIcon = 5;
        private const string ExperiencedMarker = "core.shop.experienced";
        private const string GoalsOpener = "BtnGorev";

        // ------------------------------------------------------------------ servisler
        private SaveData _data;
        private SaveService _save;
        private WalletService _wallet;
        private ContractService _contract;
        private DailyRewardService _daily;
        private AudioService _audio;
        private HapticService _haptic;
        private HudUI _hud;
        private MarketService _market;
        private MiningShopBusinessService _shop;
        private MiningShopView _shopView;
        private MiningShopUpgradeUI _benchPanel;
        private CameraController _camera;

        /// <summary>The mining shop owns Main. Without it there is no loop to teach; only the tips run.</summary>
        private bool ShopActive => _market != null && (_market.MiningShop != null || _market.MiningShopBusiness != null);

        // ------------------------------------------------------------------ durum
        private TutorialProgress _progress;
        private List<string> _progressList;
        private TutorialPresenter _view;
        private bool _running;
        private bool _replaying;
        private bool _skipped;
        private bool _tapped;
        private bool _tapAdvances;
        private bool _skipVisible;
        private int _speedTarget;              // hız dersinde halkanın durduğu yer: 0 yok, 1 tezgâh, 2 HIZ tuşu
        private int _introTarget;              // ilerleme derslerinde halkanın durduğu yer, aynı amaçla
        private bool _passMore;                // görevler dersi Daha Fazla menüsünün içini gösterirken o menü "kapatmaz"
        private GoalsUI _goals;
        private ExpeditionService _expedition;
        private string _introTitle, _introBody;
        private float _wait;                   // açılışta her şeyin oturmasını bekleme sayacı
        private float _tipTimer = 6f;
        private bool _tipShowing;
        private float _featureTipCooldown;
        private bool _covered;
        private float _coverCheckAt;
        private PointerEventData _probe;
        private readonly List<RaycastResult> _probeHits = new List<RaycastResult>();
        private static TutorialUI _instance;

        /// <summary>
        /// Whether the tutorial has the player's attention: a lesson under way (even while it waits
        /// behind a screen the player opened) or a tip up. Everything that opens by itself — the
        /// rating prompt, welcome back, the repair button — holds off while it is set, so nothing
        /// lands on a card the player is still reading.
        /// </summary>
        public static bool Blocking => _instance != null && (_instance._running || _instance._tipShowing);

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
            _market = ServiceLocator.Get<MarketService>();
            _expedition = ServiceLocator.Get<ExpeditionService>();
        }

        private void Update()
        {
            if (_running) return;
            // A flow cut short while the island was parked leaves its card behind; parking wakes the
            // overlay again with the island, so it is put away here, with nothing left running it.
            if (!_tipShowing && _view != null && _view.Visible) _view.Clear();
            if (_featureTipCooldown > 0f) _featureTipCooldown -= Time.unscaledDeltaTime;
            if (ShopActive) CoreTick();
            if (!_running) TipTick();
        }

        /// <summary>
        /// Whether any of the onboarding is on screen — a lesson or a one-shot tip. <see cref="OfferPopupUI"/>
        /// asks so an offer never lands on a hint the player is still reading; the tips fire long after
        /// the basics are finished, so the step counter alone would not catch them.
        /// </summary>
        public bool IsShowing => _view != null && _view.Visible;

        /// <summary>Replays the basics — the settings screen's EĞİTİM row. Introductions already given stay given.</summary>
        public void Replay()
        {
            if (_running || _data == null) return;
            Progress().ResetCore();
            Write();
            _replaying = true;
            _wait = 0f;
            _skipped = false;
        }

        /// <summary>
        /// The save's own list, read through <see cref="TutorialProgress"/>. Rebuilt only when the save
        /// hands over a different list or someone else moved the step — a debug menu, a loaded save.
        /// </summary>
        private TutorialProgress Progress()
        {
            if (_data.tutorialTipsSeen == null) _data.tutorialTipsSeen = new List<string>();
            if (_progress == null || _progressList != _data.tutorialTipsSeen || _progress.Step != _data.tutorialStep)
            {
                _progressList = _data.tutorialTipsSeen;
                _progress = new TutorialProgress(_progressList, _data.tutorialStep);
                // An old save can hold a step between 0 and 100; pin it so this does not rebuild every frame.
                _data.tutorialStep = _progress.Step;
            }
            return _progress;
        }

        private void Write()
        {
            _data.tutorialStep = _progress.Step;
            _save?.Save(_data);
        }

        private void CoreTick()
        {
            if (_data == null) return;
            TutorialProgress progress = Progress();
            if (progress.CoreDone) return;

            if (_shop == null) _shop = _market.MiningShopBusiness;
            if (_shopView == null)
            {
                _shopView = FindAnyObjectByType<MiningShopView>();
                if (_shopView != null) _benchPanel = _shopView.GetComponent<MiningShopUpgradeUI>();
            }
            if (_camera == null) _camera = FindAnyObjectByType<CameraController>();

            // A shop that already ran for a while belongs to a player who knows it — an old save from
            // before these lessons. Only asked before the first lesson: a new player's own shop gets
            // just as busy while they are being taught, and must not end the lessons early.
            if (!_replaying && progress.NextCoreIndex == 0 && HasShopExperience())
            {
                progress.FinishCore(false);
                progress.Complete(ExperiencedMarker);
                Write();
                return;
            }

            if (_shop == null || _shopView == null || _shopView.TableCollider == null || Camera.main == null) return;
            var report = ServiceLocator.Get<OfflineReport>();
            if (report != null && report.Pending) return;
            var boot = FindAnyObjectByType<OperationCameraBoot>();
            if (boot != null && !boot.Framed) return;
            if (Covered()) { _wait = 0f; return; }

            _wait += Time.unscaledDeltaTime;
            if (_wait > 1.2f) Run(PlayCore());
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

        // ------------------------------------------------------------------ temel dersler

        private IEnumerator PlayCore()
        {
            _running = true;
            _skipped = false;
            _tapped = false;
            _wait = 0f;
            Build();
            _view.SetVisible(true);
            _view.ClearTarget();
            _view.SetRing(false, false);
            _view.BlockInput(false);
            _view.SetShadeActive(false);
            _view.SetShadeAlpha(0f);
            ShowSkip(true);
            yield return null; // Let the portrait canvas settle before sizing the guide.

            TutorialProgress progress = Progress();
            int count = TutorialProgress.CoreLessons.Length;
            while (!_skipped)
            {
                int index = progress.NextCoreIndex;
                if (index < 0) break;
                TutorialProgress.Lesson lesson = TutorialProgress.CoreLessons[index];
                TutorialProgress.Facts start = ReadFacts();
                // Asked to save up for what they can already buy, or to buy what they already bought:
                // passed over, not shown.
                if (!TutorialProgress.AlreadyDone(lesson.Goal, start))
                {
                    yield return HoldWhileCovered();
                    _view.SetPips(count, index);
                    yield return Teach(lesson, start);
                    if (_skipped) break;
                    yield return _view.HideCard();
                }
                if (progress.Complete(lesson.Id)) Write();
            }

            if (_skipped && progress.FinishCore(true)) Write();

            _replaying = false;
            _running = false;
            // Kapanış kartının hemen ardına bir ipucu yapıştırmak eğitimi bitirmemiş gibi gösteriyor.
            // İlk ipucu için oyuncunun adayla bir süre yalnız kalması lazım.
            _tipTimer = 25f;
            LockCamera(false);
            ClearGuide();   // last: it switches off the overlay this flow runs on
        }

        /// <summary>
        /// One lesson: point at its subject, put up its card, wait for its goal. Watching lessons hold
        /// every tap; doing lessons leave the spotlight's hole open, and only it.
        /// </summary>
        private IEnumerator Teach(TutorialProgress.Lesson lesson, TutorialProgress.Facts start)
        {
            string title = Loc.T(TutorialProgress.TextKey(lesson.Id, true));
            string body = Loc.T(TutorialProgress.TextKey(lesson.Id, false));
            Collider bench = _shopView != null ? _shopView.TableCollider : null;

            switch (lesson.Id)
            {
                case "ftue.welcome":
                    _view.ClearTarget();
                    _view.SetRing(false, false);
                    yield return Stage(watchShadeAlpha, true, true);
                    _tapAdvances = true;
                    Card(title, body, null, narratorFirstEntry != null ? narratorFirstEntry : narratorWelcome);
                    Sound(SoundId.PanelOpen);
                    yield return WaitContinue();
                    break;

                case "ftue.craft":
                    if (bench != null) _view.TargetWorldArea(bench);
                    else _view.ClearTarget();
                    _view.SetRing(bench != null, false);
                    yield return Stage(watchShadeAlpha, true, true);
                    _tapAdvances = false;
                    Card(title, body, null, null);
                    Sound(SoundId.PanelOpen);
                    yield return WaitGoal(lesson.Goal, start, true);
                    break;

                case "ftue.carry":
                    Transform carrier = _shopView != null ? _shopView.TutorialCarrier : null;
                    if (carrier != null) _view.TargetWorldPoint(carrier);
                    else _view.ClearTarget();
                    _view.SetRing(false, false);
                    yield return Stage(0f, true, true);
                    _tapAdvances = false;
                    Card(title, body, null, narratorThoughtful);
                    Sound(SoundId.PanelOpen);
                    yield return WaitGoal(lesson.Goal, start, true);
                    break;

                case "ftue.sell":
                    Transform shelf = _shopView != null ? _shopView.TutorialShelf : null;
                    if (shelf != null) _view.TargetWorldPoint(shelf);
                    else _view.ClearTarget();
                    _view.SetRing(false, false);
                    yield return Stage(0f, true, true);
                    _tapAdvances = false;
                    Card(title, body, Icon(CashIcon), narratorWarning);
                    Sound(SoundId.PanelOpen);
                    yield return WaitGoal(lesson.Goal, start, true);
                    break;

                case "ftue.save_up":
                    // The one lesson that hands the island back: the shop earns while the player watches
                    // the counter climb, and nothing is dimmed or held.
                    RectTransform cash = _hud != null ? _hud.GoldRect : null;
                    if (cash != null) _view.TargetUi(cash);
                    else _view.ClearTarget();
                    _view.SetRing(cash != null, false);
                    yield return Stage(0f, false, false);
                    _tapAdvances = false;
                    Card(title, SaveUpText(body), Icon(CashIcon), null);
                    Sound(SoundId.PanelOpen);
                    yield return WaitGoal(lesson.Goal, start, false);
                    break;

                case "ftue.open_bench":
                    if (bench != null) _view.TargetWorldArea(bench);
                    else _view.ClearTarget();
                    _view.SetRing(bench != null, true);
                    yield return Stage(shadeColor.a, false, true);
                    _tapAdvances = false;
                    Card(title, body, null, null);
                    Sound(SoundId.PanelOpen);
                    yield return WaitGoal(lesson.Goal, start, false);
                    break;

                case "ftue.buy_speed":
                    _speedTarget = 0;
                    PointAtSpeed();
                    yield return Stage(shadeColor.a, false, true);
                    _tapAdvances = false;
                    Card(title, body, null, null);
                    Sound(SoundId.PanelOpen);
                    yield return WaitGoal(lesson.Goal, start, false);
                    break;

                case "ftue.speed_value":
                    RectTransform panel = BenchPanelRect();
                    if (panel != null) _view.TargetUi(panel);
                    else _view.ClearTarget();
                    _view.SetRing(panel != null, false);
                    yield return Stage(shadeColor.a, true, true);
                    _tapAdvances = true;
                    Card(title, body, null, null);
                    Sound(SoundId.PanelOpen);
                    yield return WaitContinue();
                    break;

                default: // "ftue.loop"
                    _view.ClearTarget();
                    _view.SetRing(false, false);
                    yield return Stage(watchShadeAlpha, true, true);
                    _tapAdvances = true;
                    Card(title, body, null, narratorHappy != null ? narratorHappy : narratorWelcome);
                    Sound(SoundId.Upgrade);
                    yield return WaitContinue();
                    break;
            }
        }

        /// <summary>
        /// How much of the world a lesson dims, whether it holds every tap, and whether it holds the
        /// camera. A shade of 0 is none at all: the world is shown as it is, with the card beside it.
        /// </summary>
        private IEnumerator Stage(float shade, bool block, bool lockCamera)
        {
            LockCamera(lockCamera);
            _view.BlockInput(block);
            if (shade <= 0f)
            {
                _view.SetShadeActive(false);
                yield break;
            }
            _view.SetShadeActive(true);
            yield return _view.FadeShade(shade, 0.25f);
        }

        /// <summary>
        /// Until the game meets the lesson's goal. A watching lesson that runs long offers DEVAM, so a
        /// shop that stalls — a full shelf, no customer — can never hold the player on one card.
        /// </summary>
        private IEnumerator WaitGoal(TutorialProgress.Goal goal, TutorialProgress.Facts start, bool patient)
        {
            _tapped = false;
            float waited = 0f;
            float refresh = 0f;
            bool offered = false;
            bool met = false;
            string saveUp = goal == TutorialProgress.Goal.Afford
                ? Loc.T(TutorialProgress.TextKey("ftue.save_up", false)) : null;

            while (!_skipped)
            {
                // A screen over the island stops the lesson's clock: patience and reading time are
                // the player's time with the card, not time spent in the store.
                if (Covered()) { yield return HoldWhileCovered(); continue; }

                // Met is latched: a watching card stays up for its reading time even when the shop
                // finishes first, and the goal cannot un-happen while it waits.
                if (!met && TutorialProgress.GoalMet(goal, ReadFacts(), start)) met = true;
                if (met && (!patient || waited >= lessonReadSeconds)) break;
                if (offered && _tapped) break;

                float dt = Time.unscaledDeltaTime;
                waited += dt;
                if (patient && !offered && waited >= lessonPatienceSeconds)
                {
                    offered = true;
                    _tapAdvances = true;
                    _view.ShowContinue(true);
                }
                if (saveUp != null)
                {
                    refresh -= dt;
                    if (refresh <= 0f)
                    {
                        refresh = 0.25f;
                        _view.SetBody(SaveUpText(saveUp));
                    }
                }
                if (goal == TutorialProgress.Goal.BuySpeed) PointAtSpeed();
                yield return null;
            }

            _tapped = false;
            _tapAdvances = false;
            if (!met || _skipped) yield break;
            Sound(SoundId.Tick);
            if (_haptic != null) _haptic.Light();
            if (patient && lessonResultSeconds > 0f) yield return new WaitForSecondsRealtime(lessonResultSeconds);
        }

        /// <summary>
        /// The speed lesson's spotlight: the bench while its panel is shut, the HIZ button once it is
        /// open. The player can close the panel halfway; the ring follows rather than pointing at a
        /// button that is no longer there.
        /// </summary>
        private void PointAtSpeed()
        {
            RectTransform speed = _benchPanel != null && _benchPanel.TutorialPanelOpen ? _benchPanel.TutorialSpeedRect : null;
            Collider bench = _shopView != null ? _shopView.TableCollider : null;
            int want = speed != null ? 2 : (bench != null ? 1 : 0);
            if (want == _speedTarget) return;
            _speedTarget = want;
            if (want == 2) _view.TargetUi(speed);
            else if (want == 1) _view.TargetWorldArea(bench);
            else _view.ClearTarget();
            _view.SetRing(want != 0, true);
        }

        /// <summary>The bench panel itself — the parent of its two buttons — so both are inside one ring.</summary>
        private RectTransform BenchPanelRect()
        {
            if (_benchPanel == null || !_benchPanel.TutorialPanelOpen) return null;
            RectTransform value = _benchPanel.TutorialValueRect;
            return value != null ? value.parent as RectTransform : null;
        }

        private string SaveUpText(string format)
        {
            double cost = _shop != null ? _shop.UpgradeCost(0, true) : 0d;
            BigDouble cash = _wallet != null ? _wallet.Cash : new BigDouble(0d);
            // "$20 / $40" is one reading; a line break inside it left "$20 /" on one line and "$40" on the next.
            return string.Format(format.Replace(" / ", " / "),
                                 "$" + NumberFormatter.Format(cash), "$" + NumberFormatter.Format(new BigDouble(cost)));
        }

        /// <summary>The shop, read now. No allocation: it is read every frame a lesson waits.</summary>
        private TutorialProgress.Facts ReadFacts()
        {
            var facts = new TutorialProgress.Facts { SpeedLevel = 1 };
            if (_shop == null) return facts;

            MiningShopBusinessSimulation.Snapshot view = _shop.View;
            int lines = Mathf.Min(view.AvailableProductCount, MiningShopCampaign.ProductCount);
            for (int i = 0; i < lines; i++)
            {
                MiningShopBusinessSimulation.ProductSnapshot product = view.ProductAt(i);
                facts.Produced += product.Produced;
                facts.Sold += product.Sold;
                // Everything that ever reached the shelf: on it, being handed over, or sold.
                facts.Carried += product.ShelfStock + product.Sold + (view.Serving && view.ServiceProductIndex == i ? 1 : 0);
            }

            facts.SpeedLevel = view.ProductAt(0).SpeedLevel;
            double cost = _shop.UpgradeCost(0, true);
            facts.CanAffordSpeed = cost > 0d && _wallet != null && _wallet.CanAfford(new BigDouble(cost));
            facts.BenchPanelOpen = _benchPanel != null && _benchPanel.TutorialPanelOpen;
            return facts;
        }

        private void LockCamera(bool on)
        {
            if (_camera != null) _camera.InputLocked = on;
        }

        /// <summary>
        /// Waits out a screen that covers the island — one the player opened from the HUD, or one
        /// that opened by itself. The guide steps aside meanwhile and comes back exactly as it was.
        /// </summary>
        private IEnumerator HoldWhileCovered()
        {
            if (_view == null || !Covered()) yield break;
            _view.SetSuspended(true);
            while (Covered()) yield return null;
            _view.SetSuspended(false);
        }

        /// <summary>
        /// Whether a screen at <see cref="screenSortingFloor"/> or above is over the middle of the
        /// island. Asked of the UI raycasters rather than of a list of screens: every screen in the
        /// game blocks taps to the island behind it, so one that is not listed anywhere still counts.
        /// Re-asked five times a second, not every frame.
        /// </summary>
        private bool Covered()
        {
            if (Time.unscaledTime < _coverCheckAt) return _covered;
            _coverCheckAt = Time.unscaledTime + 0.2f;
            _covered = ScreenAt(0.5f) || ScreenAt(0.25f);
            return _covered;
        }

        private bool ScreenAt(float height)
        {
            EventSystem events = EventSystem.current;
            if (events == null) return false;
            if (_probe == null) _probe = new PointerEventData(events);
            _probe.position = new Vector2(Screen.width * 0.5f, Screen.height * height);
            _probeHits.Clear();
            events.RaycastAll(_probe, _probeHits);
            Transform own = _view != null ? _view.transform : null;
            for (int i = 0; i < _probeHits.Count; i++)
            {
                RaycastResult hit = _probeHits[i];
                if (!(hit.module is GraphicRaycaster) || hit.gameObject == null) continue;
                if (own != null && hit.gameObject.transform.IsChildOf(own)) continue;
                if (_passMore && _hud != null && _hud.InMoreSheet(hit.gameObject.transform)) continue;
                if (hit.sortingOrder >= screenSortingFloor) return true;
            }
            return false;
        }

        private void ClearGuide()
        {
            ShowSkip(false);
            if (_view != null) _view.Clear();
        }

        private void MarkTutorialId(string id)
        {
            if (_data == null || string.IsNullOrEmpty(id)) return;
            if (_data.tutorialTipsSeen == null) _data.tutorialTipsSeen = new List<string>();
            if (!_data.tutorialTipsSeen.Contains(id)) _data.tutorialTipsSeen.Add(id);
            _save?.Save(_data);
        }

        private bool HasTutorialId(string id)
            => _data != null && _data.tutorialTipsSeen != null && _data.tutorialTipsSeen.Contains(id);

        private void OnFeatureOpened(string id)
        {
            if (_running || _tipShowing || _featureTipCooldown > 0f || _data == null
                || _data.tutorialStep < TutorialProgress.StepDone || string.IsNullOrEmpty(id)) return;
            var report = ServiceLocator.Get<OfflineReport>();
            if (report != null && report.Pending) return;
            if (FindAnyObjectByType<RewardRevealUI>() != null) return;
            // Not usable yet: asked again the next time the screen opens. Used already: written down
            // without a card — a player from before these intros meets only what they never touched.
            if (!Intro("feature." + id, FeatureReady(id), FeatureUsed(id))) return;
            _featureTipCooldown = 20f;
            Run(TipCard(id, null, false));   // over the feature's own screen, so never held for it
        }

        /// <summary>
        /// Whether a tutorial card is on screen right now — up and not stepped aside for another
        /// screen. A screen that opens cards of its own (the league) waits for this to clear.
        /// </summary>
        public static bool CardOnScreen
            => _instance != null && _instance._view != null && _instance._view.OnScreen && _instance._view.CardShowing;

        /// <summary>What the save shows the player has already done with each feature.</summary>
        private bool FeatureUsed(string id)
        {
            if (id == "stage")
            {
                List<ChapterState> chapters = _data.chapters;
                if (chapters == null) return false;
                for (int c = 0; c < chapters.Count; c++)
                {
                    bool[] claimed = chapters[c] != null ? chapters[c].claimed : null;
                    if (claimed == null) continue;
                    for (int b = 0; b < claimed.Length; b++) if (claimed[b]) return true;
                }
                return false;
            }
            if (id == "crafting") return _data.craftXp > 0L || _data.craftGatesCleared > 0;
            if (id == "captain")
            {
                int[] levels = _data.captainLevels;
                if (levels == null) return false;
                for (int i = 0; i < levels.Length; i++) if (levels[i] > 1) return true;
                return false;
            }
            if (id == "pets")
            {
                int[] equipped = _data.pets != null ? _data.pets.equippedSpecies : null;
                if (equipped == null) return false;
                for (int i = 0; i < equipped.Length; i++) if (equipped[i] >= 0) return true;
                return false;
            }
            if (id == "collection")
            {
                CardCollectionService collection = ServiceLocator.Get<CardCollectionService>();
                return collection != null && collection.OwnedCardCount > 0;
            }
            if (id == "events")
            {
                List<LiveEventState> events = _data.liveEvents;
                if (events == null) return false;
                for (int e = 0; e < events.Count; e++)
                {
                    bool[] claimed = events[e] != null ? events[e].claimed : null;
                    if (claimed == null) continue;
                    for (int s = 0; s < claimed.Length; s++) if (claimed[s]) return true;
                }
                return false;
            }
            if (id == "league")
            {
                PlayerProfileService profiles = ServiceLocator.Get<PlayerProfileService>();
                return profiles != null && profiles.Prompted;
            }
            if (id == "gear")
            {
                int[] worn = _data.miningGearGrade;
                if (worn == null) return false;
                for (int i = 0; i < worn.Length; i++) if (worn[i] > 0) return true;
                return false;
            }
            return false;
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
            if (id == "league")
            {
                LadderService ladder = ServiceLocator.Get<LadderService>();
                return ladder != null && ladder.Available;
            }
            if (id == "gear")
            {
                // A craft to make, or something already worn to look at.
                MiningGearService gear = ServiceLocator.Get<MiningGearService>();
                return gear != null && (gear.CanCraft || FeatureUsed(id));
            }
            return true;
        }

        // ------------------------------------------------------------------ ipuçları

        /// <summary>
        /// One card, the first time each of these becomes true, and never again. Deliberately polled on
        /// a slow timer rather than wired to events: every one of these is a state the player can also
        /// arrive at while the game was closed, and a missed event teaches nothing.
        /// </summary>
        private void TipTick()
        {
            if (_data == null || _data.tutorialStep < TutorialProgress.StepDone || _tipShowing) return;
            _tipTimer -= Time.unscaledDeltaTime;
            if (_tipTimer > 0f) return;
            _tipTimer = 2f;
            if (Covered()) return;
            // The bench panel sits low, where the card docks; a tip would cover its buttons mid-purchase.
            // Found here too, on this two-second beat: a finished player never runs the lessons that find it.
            if (_benchPanel == null && ShopActive)
            {
                if (_shopView == null) _shopView = FindAnyObjectByType<MiningShopView>();
                if (_shopView != null) _benchPanel = _shopView.GetComponent<MiningShopUpgradeUI>();
            }
            if (_benchPanel != null && _benchPanel.TutorialPanelOpen) return;
            var report = ServiceLocator.Get<OfflineReport>();
            if (report != null && report.Pending) return;

            _shop = _market != null ? _market.MiningShopBusiness : null;

            // First the things the player can do something about, then the reminders. Each is taught
            // once, when it first matters, and passed over for a save that shows it already in use —
            // a player from before these lessons gets only what they have never touched.
            if (Intro(TutorialProgress.SecondBenchLesson, SecondBenchDue(), SecondBenchBuilt()))
            {
                Run(SecondBench());
                return;
            }
            if (Intro(TutorialProgress.GoalsLesson, GoalsDue(), GoalsUsed()))
            {
                Run(GoalsIntro());
                return;
            }
            if (Intro(TutorialProgress.SailLesson, SailDue(), SeaUsed()))
            {
                Run(SailIntro());
                return;
            }
            if (Intro("kontrat", _contract != null && _contract.Claimable, _data.goals.lifetime[Goals.Contracts] > 0L))
            {
                Run(TipCard("kontrat", _hud != null ? _hud.ContractRect : null, true));
                return;
            }
            if (Intro("boost", _hud != null && _hud.BoostReady, _data.boostEndUnix > 0L))
            {
                Run(TipCard("boost", _hud.BoostRect, true));
                return;
            }
            if (Intro("gunluk", _daily != null && _daily.CanClaim(), _data.lastDailyClaimUnix > 0L))
                Run(TipCard("gunluk", _hud != null ? _hud.DailyRect : null, true));
        }

        /// <summary>
        /// True when an introduction should start now, and it is then already written down: a card
        /// the player closes the app on is not owed again. One passed over because the save shows
        /// the feature in use is written down too.
        /// </summary>
        private bool Intro(string id, bool relevant, bool used)
        {
            TutorialProgress progress = Progress();
            bool had = progress.Has(id);
            TutorialProgress.Intro decision = progress.DecideIntro(id, relevant, used);
            if (decision == TutorialProgress.Intro.Show)
            {
                progress.Complete(id);
                Write();
                return true;
            }
            if (!had && progress.Has(id)) Write();
            return false;
        }

        // ------------------------------------------------------------------ ilerleme dersleri

        /// <summary>Only where a second product is on offer, and only once the player can pay for its bench.</summary>
        private bool SecondBenchDue()
        {
            Collider pad = _shopView != null ? _shopView.TutorialPad(1) : null;
            if (_shop == null || pad == null || !pad.gameObject.activeInHierarchy) return false;
            MiningShopBusinessSimulation.Snapshot view = _shop.View;
            if (view.AvailableProductCount < 2 || view.ProductAt(1).TableBuilt) return false;
            double cost = _shop.TableCost(1);
            return cost > 0d && _wallet != null && _wallet.CanAfford(new BigDouble(cost));
        }

        private bool SecondBenchBuilt()
            => _shop != null && _shop.View.AvailableProductCount >= 2 && _shop.View.ProductAt(1).TableBuilt;

        /// <summary>A reward is waiting behind Goals. Taught at the moment there is something to collect.</summary>
        private bool GoalsDue()
        {
            if (_hud == null) return false;
            bool inMore;
            if (_hud.OpenerRect(GoalsOpener, out inMore) == null) return false;
            GoalService goals = ServiceLocator.Get<GoalService>();
            return goals != null && goals.PendingCount() > 0;
        }

        /// <summary>Anything ever collected from Goals — a daily, a weekly milestone or an achievement tier.</summary>
        private bool GoalsUsed()
        {
            GoalSaveData goals = _data.goals;
            if (goals == null) return false;
            if (goals.weeklyMilestonesClaimed != null && goals.weeklyMilestonesClaimed.Length > 0) return true;
            if (goals.tiersClaimed != null)
                for (int i = 0; i < goals.tiersClaimed.Length; i++) if (goals.tiersClaimed[i] > 0) return true;
            if (goals.dailyClaimed != null)
                for (int i = 0; i < goals.dailyClaimed.Length; i++) if (goals.dailyClaimed[i]) return true;
            return false;
        }

        /// <summary>The pad, then the panel's build button. Ends when the bench stands.</summary>
        private IEnumerator SecondBench()
        {
            BeginIntro();
            _introTarget = -1;
            PointAtBuild();
            LockCamera(true);
            string id = TutorialProgress.SecondBenchLesson;
            _introTitle = Loc.T(TutorialProgress.TextKey(id, true));
            _introBody = Loc.T(TutorialProgress.TextKey(id, false));
            Card(_introTitle, _introBody, null, null);
            Sound(SoundId.PanelOpen);
            yield return WaitIntro(SecondBenchBuilt, PointAtBuild);
            LockCamera(false);
            yield return EndIntro();
        }

        /// <summary>
        /// Goals lives in the More sheet on the phone layouts: the ring goes to the More button, then
        /// to the Goals row once the sheet is open, and follows back if the sheet is shut again.
        /// </summary>
        private IEnumerator GoalsIntro()
        {
            BeginIntro();
            if (_goals == null) _goals = FindAnyObjectByType<GoalsUI>(FindObjectsInactive.Include);
            SetPassMore(true);
            _introTarget = -1;
            PointAtGoals();
            string id = TutorialProgress.GoalsLesson;
            _introTitle = Loc.T(TutorialProgress.TextKey(id, true));
            _introBody = Loc.T(TutorialProgress.TextKey(id, false));
            Card(_introTitle, _introBody, null, null);
            Sound(SoundId.PanelOpen);
            yield return WaitIntro(GoalsOpen, PointAtGoals);
            SetPassMore(false);
            yield return EndIntro();
        }

        private bool GoalsOpen() => _goals != null && _goals.IsOpen;

        /// <summary>
        /// Once the shop has had a few upgrades — the sea is the next thing to do with a running
        /// island, not the first. Where there is no sail button there is nothing to point at.
        /// </summary>
        private bool SailDue()
        {
            if (_hud == null || _expedition == null || _expedition.Active || _shop == null) return false;
            bool inMore;
            if (_hud.OpenerRect(HudUI.SailButtonName, out inMore) == null) return false;
            MiningShopBusinessSimulation.Snapshot view = _shop.View;
            int lines = Mathf.Min(view.AvailableProductCount, MiningShopCampaign.ProductCount);
            int bought = 0;
            for (int i = 0; i < lines; i++)
            {
                MiningShopBusinessSimulation.ProductSnapshot product = view.ProductAt(i);
                if (!product.TableBuilt) continue;
                bought += product.SpeedLevel - 1 + product.ValueLevel - 1;
            }
            return bought >= seaAfterUpgrades;
        }

        /// <summary>A fight won, or the old sea hint seen: this player has been out already.</summary>
        private bool SeaUsed() => _data.seaFightsWon > 0 || HasTutorialId(TutorialProgress.SeaFightLesson);

        /// <summary>
        /// The sail button: on the rail, or through More on the 4:3 layout where it lives in the
        /// sheet. Ends the moment the ship sets sail — before the curtain parks the island and this
        /// overlay with it, so nothing is left half-shown for the way back.
        /// </summary>
        private IEnumerator SailIntro()
        {
            BeginIntro();
            SetPassMore(true);
            _introTarget = -1;
            PointAtSail();
            string id = TutorialProgress.SailLesson;
            _introTitle = Loc.T(TutorialProgress.TextKey(id, true));
            _introBody = Loc.T(TutorialProgress.TextKey(id, false));
            Card(_introTitle, _introBody, null, null);
            Sound(SoundId.PanelOpen);
            yield return WaitIntro(Sailing, PointAtSail);
            SetPassMore(false);
            if (!Sailing())
            {
                yield return EndIntro();
                yield break;
            }
            // No slide out: the curtain is already coming down over the island.
            _tipTimer = introGapSeconds;
            _tipShowing = false;
            _tapAdvances = false;
            _view.Clear();   // last: it switches off the overlay this flow runs on
        }

        private bool Sailing() => SceneCurtain.Busy || (_expedition != null && _expedition.Active);

        private void BeginIntro()
        {
            _tipShowing = true;
            Build();
            _view.BlockInput(false);
            _view.SetShadeActive(false);            // ilerleme dersleri hiçbir şeyi engellemez
            _view.SetPips(0, 0);
            _tapAdvances = false;
            ShowSkip(false);
        }

        private IEnumerator EndIntro()
        {
            yield return _view.HideCard();
            _tipTimer = introGapSeconds;
            _tipShowing = false;
            _tapAdvances = false;
            _view.Clear();   // last: it switches off the overlay this flow runs on
        }

        /// <summary>
        /// Until the player does the thing, or takes DEVAM once it is offered. Done is asked before
        /// the cover check: the screen the lesson leads to is itself a screen over the island.
        /// </summary>
        private IEnumerator WaitIntro(System.Func<bool> done, System.Action retarget)
        {
            _tapped = false;
            float waited = 0f;
            bool offered = false;
            while (true)
            {
                if (done())
                {
                    Sound(SoundId.Tick);
                    if (_haptic != null) _haptic.Light();
                    break;
                }
                if (Covered()) { yield return HoldWhileCovered(); continue; }
                if (offered && _tapped) break;
                waited += Time.unscaledDeltaTime;
                if (!offered && waited >= introPatienceSeconds)
                {
                    offered = true;
                    _tapAdvances = true;
                    _view.ShowContinue(true);
                }
                retarget();
                yield return null;
            }
            _tapped = false;
            _tapAdvances = false;
        }

        /// <summary>The empty pad while the panel is shut or shows another bench; the build button once it is open on this one.</summary>
        private void PointAtBuild()
        {
            bool panel = _benchPanel != null && _benchPanel.TutorialPanelOpen && _benchPanel.TutorialProduct == 1;
            RectTransform build = panel ? _benchPanel.TutorialSpeedRect : null;
            Collider pad = _shopView != null ? _shopView.TutorialPad(1) : null;
            int want = build != null ? 2 : (pad != null && pad.gameObject.activeInHierarchy ? 1 : 0);
            if (want == _introTarget) return;
            _introTarget = want;
            if (want == 2) _view.TargetUi(build);
            else if (want == 1) _view.TargetWorldArea(pad);
            else _view.ClearTarget();
            _view.SetRing(want != 0, true);
            Redock();
        }

        private void PointAtGoals() => PointAtOpener(GoalsOpener);

        private void PointAtSail() => PointAtOpener(HudUI.SailButtonName);

        /// <summary>A HUD opener on the rail; or, when it lives in the More sheet, More first and then its row.</summary>
        private void PointAtOpener(string opener)
        {
            bool inMore = false;
            RectTransform row = _hud != null ? _hud.OpenerRect(opener, out inMore) : null;
            RectTransform target;
            int want;
            if (row == null) { target = null; want = 0; }
            else if (!inMore) { target = row; want = 1; }
            else if (_hud.MoreOpen) { target = row; want = 2; }
            else { target = _hud.MoreButtonRect; want = 3; }
            if (want == _introTarget) return;
            _introTarget = want;
            if (target != null) _view.TargetUi(target);
            else _view.ClearTarget();
            _view.SetRing(target != null, true);
            Redock();
        }

        /// <summary>
        /// The card picks its side against the target it was shown with and keeps it. When a lesson
        /// moves on to its next target — the pad, then the build button — the card is put up again,
        /// so it is never left lying over the thing it now points at.
        /// </summary>
        private void Redock()
        {
            if (_view.CardShowing) Card(_introTitle, _introBody, null, null);
        }

        private void SetPassMore(bool on)
        {
            _passMore = on;
            _coverCheckAt = 0f;   // the next ask must see the change, not a cached answer
        }

        private IEnumerator TipCard(string id, RectTransform target, bool holdForScreens)
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
            Card(Loc.T("egitim." + localizationId + "_b"), Loc.T("egitim." + localizationId + "_m"), null,
                 target != null ? null : narratorThoughtful);
            Sound(SoundId.Tick);
            yield return WaitTap(tipSeconds, holdForScreens);
            yield return _view.HideCard();

            _tipTimer = introGapSeconds;
            _tipShowing = false;
            _tapAdvances = false;
            _view.Clear();   // last: it switches off the overlay this flow runs on
        }

        // ------------------------------------------------------------------ kart

        /// <summary>One card through the presenter, with this flow's DEVAM and ATLA state.</summary>
        private void Card(string title, string body, Sprite icon, Sprite pose)
        {
            _view.ShowCard(title, body, pose, icon, _tapAdvances, _skipVisible);
        }

        private IEnumerator WaitTap(float seconds, bool holdForScreens)
        {
            _tapped = false;
            float t = 0f;
            while (t < seconds && !_tapped && !_skipped)
            {
                if (holdForScreens && Covered()) { yield return HoldWhileCovered(); continue; }
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            _tapped = false;
        }

        /// <summary>Tap lessons never time out; only DEVAM (or a tap on the shade) advances.</summary>
        private IEnumerator WaitContinue()
        {
            _tapped = false;
            while (!_tapped && !_skipped)
            {
                if (Covered()) yield return HoldWhileCovered();
                else yield return null;
            }
            _tapped = false;
        }

        // ------------------------------------------------------------------ küçük anahtarlar

        /// <summary>
        /// Runs a lesson or a tip on the overlay's own object, not this one. This sits on the HUD,
        /// and full-screen screens (the store among them) switch the HUD off while they are open;
        /// a coroutine here died with it, froze its card over the store and left the flow marked as
        /// running for good. The overlay is never switched off mid-flow — only by Clear at its end.
        /// </summary>
        private void Run(IEnumerator flow)
        {
            Build();
            _view.SetVisible(true);   // a coroutine cannot start on an inactive object; the flow sets up the rest
            _view.StartCoroutine(flow);
        }

        private void ShowSkip(bool on) => _skipVisible = on;

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
            LockCamera(false);
            if (_view != null) Destroy(_view.gameObject);
        }

        // ══════════════════════════════════════════════════════════════════ kuruluş

        private void Build()
        {
            if (_view != null) return;
            _view = NewPresenter();
            _view.Continued += OnContinue;
            _view.Skipped += OnSkip;
            _view.ShadeTapped += OnShadeTap;
            _view.Disabled += OnGuideDisabled;
        }

        /// <summary>
        /// The overlay went dark under a flow that was still running: the island was parked for the
        /// sea or the market, and the flow died with it. A core lesson starts again from the save when
        /// the island is back; an intro was written down when it began, so it is simply over.
        /// </summary>
        private void OnGuideDisabled()
        {
            if (!_running && !_tipShowing) return;
            _running = false;
            _tipShowing = false;
            _tapAdvances = false;
            _tapped = false;
            _wait = 0f;
            _tipTimer = introGapSeconds;
            SetPassMore(false);
            ShowSkip(false);
            LockCamera(false);
        }

        /// <summary>
        /// The same guide — Max, his card, the ring — for a scene that has no HUD of its own: the sea.
        /// Built from this component's art, which stays loaded while the island is parked. Null when
        /// there is no island to borrow it from; the caller owns the result and destroys it.
        /// </summary>
        public static TutorialPresenter CreateGuide()
        {
            return _instance != null ? _instance.NewPresenter() : null;
        }

        private TutorialPresenter NewPresenter()
        {
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
            return TutorialPresenter.Create(art, layout, SortingOrder);
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
