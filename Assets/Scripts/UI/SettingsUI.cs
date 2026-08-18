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
    /// The settings panel (panel_ayarlar + serit_ayarlar + satir_ayar from the Figma set): SFX and
    /// music sliders, a vibration switch, and the language / rate / privacy / restore-purchases rows.
    /// Editor-authored — the whole hierarchy lives in the UI_Ayarlar prefab and every reference is
    /// wired in the Inspector, so rows, icons and spacing are all tunable from the hierarchy.
    ///
    /// Values apply to <see cref="AudioService"/> / <see cref="HapticService"/> immediately and are
    /// persisted in PlayerPrefs (options are device preferences, not save-game state). The language
    /// restore row talks to Unity IAP on mobile; privacy opens <see cref="privacyUrl"/> once filled.
    /// </summary>
    public sealed class SettingsUI : MonoBehaviour
    {
        private const string KeySfx = "ayar_sfx";
        private const string KeyMusic = "ayar_muzik";
        private const string KeyHaptic = "ayar_titresim";

        [Header("Panel (UI_Ayarlar prefabında bağlı)")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Button closeButton;

        [Header("Ses / müzik")]
        [SerializeField] private Slider sfxSlider;
        [SerializeField] private Slider musicSlider;

        [Header("Titreşim")]
        [SerializeField] private Button hapticButton;
        [SerializeField] private Image hapticImage;
        [SerializeField] private Sprite switchOn;      // anahtar_acik
        [SerializeField] private Sprite switchOff;     // anahtar_kapali

        [Header("Liste satırları")]
        [SerializeField] private Button languageButton;
        [SerializeField] private Button rateButton;
        [SerializeField] private Button privacyButton;
        [SerializeField] private Button restoreButton;
        [Tooltip("UMP rıza formunu yeniden açar. GDPR bölgesinde Google kalıcı bir giriş şart koşuyor; " +
                 "gerekmeyen bölgelerde satır kendini gizler.")]
        [SerializeField] private Button privacyOptionsButton;
        [Tooltip("App Store Connect'teki sayısal Apple ID. Boşken uygulama adına göre App Store araması açılır.")]
        [SerializeField] private string iosAppStoreId = "";
        [Tooltip("Gizlilik politikası adresi — boşken satır hiçbir şey yapmaz.")]
        [SerializeField] private string privacyUrl = "";

        [Header("Dil paneli")]
        [Tooltip("Dil seçim ekranının parçaları. Çalışma anında kurulduğu için kendi Inspector'ı yok, " +
                 "sanatı buradan devralıyor. Boş bırakılan yuva UiSkin'e düşer.")]
        [SerializeField] private LanguageMenuUI.Skin languageSkin;

        private const float PreviewInterval = 0.14f;

        private AudioService _audio;
        private HapticService _haptic;
        private IIAPService _iap;
        private TMP_Text _restoreLabel;
        private Coroutine _restoreFeedback;
        private bool _hapticOn;
        private float _nextPreview;

        private void Start()
        {
            _audio = ServiceLocator.Get<AudioService>();
            _haptic = ServiceLocator.Get<HapticService>();
            _iap = ServiceLocator.Get<IIAPService>();

            // kayıtlı tercihleri yükle; ilk açılışta servislerin mevcut değerleri varsayılan olur
            float sfx = PlayerPrefs.GetFloat(KeySfx, _audio != null ? _audio.Sfx : 0.8f);
            float music = PlayerPrefs.GetFloat(KeyMusic, _audio != null ? _audio.Music : 0.6f);
            _hapticOn = PlayerPrefs.GetInt(KeyHaptic, _haptic != null && _haptic.Enabled ? 1 : 0) == 1;
            Apply(sfx, music, _hapticOn);

            if (sfxSlider != null) { sfxSlider.SetValueWithoutNotify(sfx); sfxSlider.onValueChanged.AddListener(OnSfx); }
            if (musicSlider != null) { musicSlider.SetValueWithoutNotify(music); musicSlider.onValueChanged.AddListener(OnMusic); }
            if (hapticButton != null) hapticButton.onClick.AddListener(OnHaptic);
            if (closeButton != null) closeButton.onClick.AddListener(Hide);
            if (rateButton != null) rateButton.onClick.AddListener(OnRate);
            if (privacyButton != null) privacyButton.onClick.AddListener(OnPrivacy);
            if (languageButton != null) languageButton.onClick.AddListener(OnLanguage);
            if (restoreButton != null)
            {
                restoreButton.onClick.AddListener(OnRestorePurchases);
                _restoreLabel = restoreButton.GetComponentInChildren<TMP_Text>(true);
            }
            if (privacyOptionsButton != null)
            {
                privacyOptionsButton.onClick.AddListener(OnPrivacyOptions);
                // Satır yalnız UMP gerektiğini söylediğinde durur: GDPR dışındaki bir oyuncuya
                // hiçbir şey yapmayan bir düğme göstermek, düğmeyi hiç göstermemekten kötü.
                privacyOptionsButton.gameObject.SetActive(
                    ServiceLocator.Get<IConsent>() is UmpConsentService ump && ump.PrivacyOptionsRequired);
            }
            RefreshSwitch();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            BuildTestButtons();
#endif

            if (panelRoot != null) panelRoot.SetActive(false);
            UiPanelSound.Attach(panelRoot);   // panel kapatıldıktan SONRA — açılış sesi boot'ta çalmasın
        }

        public void Toggle()
        {
            if (panelRoot != null) panelRoot.SetActive(!panelRoot.activeSelf);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // başka bir yerden açılmış olabilir — etiketler açılışta doğru olsun
            RefreshTestButton();
            RefreshTimeButton();
            RefreshMaxButton();
            RefreshMaintenanceButtons();
#endif
        }

        public void Hide()
        {
            // Dil ekranı bu pencerenin kardeşi, çocuğu değil — pencereyi kapatmak onu kapatmaz.
            if (_languages != null) _languages.Hide();
            if (panelRoot != null) panelRoot.SetActive(false);
            // Ses tercihleri PlayerPrefs'te ve Unity onları normalde çıkışta yazar. Android'de uygulama
            // öldürülerek kapatılabildiği için, panel kapanırken diske indir.
            PlayerPrefs.Save();
        }

        private void Apply(float sfx, float music, bool haptic)
        {
            if (_audio != null) { _audio.Sfx = sfx; _audio.Music = music; }
            if (_haptic != null) _haptic.Enabled = haptic;
        }

        private void OnSfx(float v)
        {
            if (_audio != null)
            {
                _audio.Sfx = v;
                // Efekt sürgüsünü sessizce sürüklemek kör bir iş olurdu — müzik yatağı anında duyulur
                // ama efektler yalnızca bir şey olduğunda çalar. Sürüklerken tık ver ki ayarladığın
                // seviyeyi duyasın. Sesin kendi tekrar kapısı 0.03 sn, sürüklemek için fazla sık.
                float now = Time.unscaledTime;
                if (now >= _nextPreview)
                {
                    _nextPreview = now + PreviewInterval;
                    _audio.Play(SoundId.Tick);
                }
            }
            PlayerPrefs.SetFloat(KeySfx, v);
        }

        private void OnMusic(float v)
        {
            if (_audio != null) _audio.Music = v;
            PlayerPrefs.SetFloat(KeyMusic, v);
        }

        private void OnHaptic()
        {
            _hapticOn = !_hapticOn;
            if (_haptic != null)
            {
                _haptic.Enabled = _hapticOn;
                if (_hapticOn) _haptic.Light();   // anında hissedilir geri bildirim
            }
            PlayerPrefs.SetInt(KeyHaptic, _hapticOn ? 1 : 0);
            RefreshSwitch();
        }

        private void RefreshSwitch()
        {
            if (hapticImage != null) hapticImage.sprite = _hapticOn ? switchOn : switchOff;
        }

        private void OnPrivacy()
        {
            if (!string.IsNullOrEmpty(privacyUrl)) Application.OpenURL(privacyUrl);
        }

        private void OnPrivacyOptions()
        {
            if (ServiceLocator.Get<IConsent>() is UmpConsentService ump) ump.ShowPrivacyOptions();
        }

        public bool IsOpen => panelRoot != null && panelRoot.activeInHierarchy;

        private void OnRate() => OpenStorePage();

        public void OpenStorePage()
        {
            ServiceLocator.Get<IAnalytics>()?.Log("rate_store_opened");
            StorePage.Open(iosAppStoreId);
        }

        private void OnRestorePurchases()
        {
            if (_iap == null)
            {
                RestoreFinished(false, "Mağaza kullanılamıyor.");
                return;
            }

            if (restoreButton != null) restoreButton.interactable = false;
            if (_restoreLabel != null) _restoreLabel.text = Loc.T("ayarlar.geri_yukleniyor");
            _iap.RestorePurchases(RestoreFinished);
        }

        private void RestoreFinished(bool success, string error)
        {
            if (restoreButton != null) restoreButton.interactable = true;
            if (_restoreLabel == null) return;
            _restoreLabel.text = Loc.T(success ? "ayarlar.geri_basarili" : "ayarlar.geri_basarisiz");
            if (!success && !string.IsNullOrEmpty(error)) Debug.LogWarning("[IAP] " + error);
            if (_restoreFeedback != null) StopCoroutine(_restoreFeedback);
            _restoreFeedback = StartCoroutine(ResetRestoreLabel());
        }

        private IEnumerator ResetRestoreLabel()
        {
            yield return new WaitForSecondsRealtime(3f);
            if (_restoreLabel != null) _restoreLabel.text = Loc.T("ayarlar.geri_yukle");
            _restoreFeedback = null;
        }

        /// <summary>
        /// Opens the language picker, which builds itself on first use. Held on this object rather than
        /// authored, because its rows come from the string table's columns and there is nothing for a
        /// prefab to hold.
        /// </summary>
        private void OnLanguage()
        {
            if (_languages == null) _languages = gameObject.AddComponent<LanguageMenuUI>();
            _languages.Show(languageSkin);
        }

        private LanguageMenuUI _languages;

        // ---------------- test şeritleri (yalnızca Editor / Development Build) ----------------

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Her yerde bedava satın alma + oturum boyunca kayıt askıda, böylece her ada ve her yükseltme
        // gerçek kayda dokunmadan denenebilir. Eskiden istasyon ekranının sol üst köşesindeydi;
        // telefondan çekilen videoda oyun ekranının üstünde göründüğü için buraya taşındı. Aşağıdaki
        // iki şerit de derleme kapısının arkasında: Play'e giden release build'de hiç derlenmiyor,
        // Development Build işaretli test derlemelerinde görünüyor.
        //
        // Figma panelinin (976x1520) son satırı -1448'de bitiyor ve altında yalnızca 72 piksel var,
        // yani yedinci bir satır paneli taşırırdı. Bunun yerine pencerenin altına, panelin dışına
        // ayrı bir şerit olarak kuruluyor: sanatçının paneline hiç dokunulmuyor ve yayına çıkmadan
        // önce tek parça hâlinde sökülebiliyor.
        private const string TestOffLabel = "TEST MODU: KAPALI";
        private const string TestOnLabel = "TEST AÇIK — KAYIT YOK";
        private static readonly Color TestOffColor = new Color(0.24f, 0.27f, 0.32f, 0.92f);
        private static readonly Color TestOnColor = new Color(0.75f, 0.20f, 0.20f, 0.92f);

        private Image _testImage;
        private TMPro.TextMeshProUGUI _testLabel;

        private void BuildTestButtons()
        {
            if (panelRoot == null) return;

            var test = BuildStrip("TestModu", 40f, out _testImage, out _testLabel);
            test.onClick.AddListener(OnTestMode);
            RefreshTestButton();

            // Gün/gece anahtarı test şeridinin hemen üstünde — ikisi de panelin dışında, ikisi de
            // yayına çıkmadan önce birlikte sökülüyor.
            var time = BuildStrip("TestZaman", 140f, out _timeImage, out _timeLabel);
            time.onClick.AddListener(OnTimeMode);
            RefreshTimeButton();

            var max = BuildStrip("TestMaks", 240f, out _maxImage, out _maxLabel);
            max.onClick.AddListener(OnMaxIsland);
            RefreshMaxButton();

            var wear = BuildStrip("TestBakim", 340f, out _wearImage, out _wearLabel);
            wear.onClick.AddListener(OnWear);
            RefreshWearButton();

            var repair = BuildStrip("TestOnar", 440f, out _repairImage, out _repairLabel);
            repair.onClick.AddListener(OnRepair);
            RefreshRepairButton();

            var grime = BuildStrip("TestKir", 540f, out _grimeImage, out _grimeLabel);
            grime.onClick.AddListener(OnGrime);
            RefreshGrimeButton();
        }

        /// <summary>Every test strip's label at once — state one of them changes is state another shows.</summary>
        private void RefreshMaintenanceButtons()
        {
            RefreshWearButton();
            RefreshRepairButton();
            RefreshGrimeButton();
        }

        /// <summary>
        /// The island the player is standing on. The two strips below both act on it, and neither can
        /// be wired in the Inspector: this panel is in a UI prefab and the operations are in the island
        /// scene. Looked up through <see cref="WorldIslands"/> rather than by
        /// <c>FindAnyObjectByType</c>, because all eight operations exist at once and seven of them are
        /// disabled — the first one found is not the live one.
        /// </summary>
        private CoalOperation LiveOperation()
        {
            if (_world == null) _world = FindAnyObjectByType<WorldIslands>();
            return _world != null ? _world.Operation(_world.ActiveIndex) : null;
        }

        private WorldIslands _world;

        /// <summary>One full-width strip under the artist's panel. Two of these now, and each is
        /// twenty-odd lines of the same RectTransform setup, so they share one builder.</summary>
        private Button BuildStrip(string name, float y, out Image background, out TMPro.TextMeshProUGUI label)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(panelRoot.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(560f, 92f);

            background = go.AddComponent<Image>();
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = background;

            var tgo = new GameObject("Etiket", typeof(RectTransform));
            tgo.transform.SetParent(go.transform, false);
            var trt = (RectTransform)tgo.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            label = tgo.AddComponent<TMPro.TextMeshProUGUI>();
            label.fontSize = 34;
            label.alignment = TMPro.TextAlignmentOptions.Center;
            label.raycastTarget = false;

            return btn;
        }

        private void OnTestMode()
        {
            var wallet = ServiceLocator.Get<WalletService>();
            if (wallet == null) return;
            bool on = !wallet.FreePurchases;
            wallet.FreePurchases = on;
            if (on)
            {
                var save = ServiceLocator.Get<SaveService>();
                if (save != null) save.Suspended = true;   // yapışkan: bir kez çalıştıysa bu oturum kayıt yazmaz
            }
            if (_haptic != null) _haptic.Light();
            RefreshTestButton();
            // İstasyon ekranı zaten kendi Update'inde refreshInterval'de bir yenileniyor,
            // fiyatların bedavaya dönmesi için buradan onu dürtmeye gerek yok.
        }

        private void RefreshTestButton()
        {
            if (_testLabel == null) return;
            var wallet = ServiceLocator.Get<WalletService>();
            bool on = wallet != null && wallet.FreePurchases;
            _testLabel.text = on ? TestOnLabel : TestOffLabel;
            if (_testImage != null) _testImage.color = on ? TestOnColor : TestOffColor;
        }

        // ---------------- gün / gece anahtarı ----------------

        // Gece 45. dakikada geliyor, yani ışıkları görmek için yarım saat beklemek gerekebiliyordu.
        // Bu düğme sırayla OTOMATİK -> GÜNDÜZ -> GECE dolaşıyor ve anında geçiyor: geçiş animasyonunu
        // beklemek, gece görünümünü kontrol etmek isteyen birinin işine yaramaz.
        private const string TimeAutoLabel = "ZAMAN: OTOMATİK";
        private const string TimeDayLabel = "ZAMAN: GÜNDÜZ";
        private const string TimeNightLabel = "ZAMAN: GECE";
        private static readonly Color TimeAutoColor = new Color(0.24f, 0.27f, 0.32f, 0.92f);
        private static readonly Color TimeDayColor = new Color(0.85f, 0.62f, 0.16f, 0.92f);
        private static readonly Color TimeNightColor = new Color(0.15f, 0.20f, 0.44f, 0.92f);

        private Image _timeImage;
        private TMPro.TextMeshProUGUI _timeLabel;
        private DayNightCycle _cycle;

        /// <summary>The cycle lives on the island scene and this panel in a UI prefab, so there is
        /// nothing for the Inspector to wire the two together with. Looked up once and kept.</summary>
        private DayNightCycle Cycle()
        {
            if (_cycle == null) _cycle = FindAnyObjectByType<DayNightCycle>();
            return _cycle;
        }

        private void OnTimeMode()
        {
            var cycle = Cycle();
            if (cycle == null) return;

            switch (cycle.Override)
            {
                case DayNightCycle.TimeOverride.Auto: cycle.Override = DayNightCycle.TimeOverride.Day; break;
                case DayNightCycle.TimeOverride.Day: cycle.Override = DayNightCycle.TimeOverride.Night; break;
                default: cycle.Override = DayNightCycle.TimeOverride.Auto; break;
            }

            if (_haptic != null) _haptic.Light();
            RefreshTimeButton();
        }

        private void RefreshTimeButton()
        {
            if (_timeLabel == null) return;

            var cycle = Cycle();
            var mode = cycle != null ? cycle.Override : DayNightCycle.TimeOverride.Auto;

            switch (mode)
            {
                case DayNightCycle.TimeOverride.Day:
                    _timeLabel.text = TimeDayLabel;
                    if (_timeImage != null) _timeImage.color = TimeDayColor;
                    break;
                case DayNightCycle.TimeOverride.Night:
                    _timeLabel.text = TimeNightLabel;
                    if (_timeImage != null) _timeImage.color = TimeNightColor;
                    break;
                default:
                    _timeLabel.text = TimeAutoLabel;
                    if (_timeImage != null) _timeImage.color = TimeAutoColor;
                    break;
            }
        }

        // ---------------- adayı tek dokunuşta maksimuma çıkar ----------------

        // Faz 3 sanatını, tam hızlı zinciri ve bakım hasarının gerçekten ne kadara mal olduğunu görmek
        // için adanın maksimum hâli gerekiyor; elle almak yüzlerce dokunuş. Bu şerit her ekseni tavana,
        // her hayalet binayı ayakta ve pazar avlusunu tam kadroya çıkarır.
        //
        // Satın almalar oyunun KENDİ yollarından geçiyor (TryUnlock / TryUpgrade / pazar satırı), yani
        // faz geçişleri, filo uyanmaları ve kayıt yazımı gerçekte olduğu gibi işliyor. Kısayol yalnızca
        // parada: cüzdan bu iş boyunca bedava moda alınıp eski hâline bırakılıyor.
        private const string MaxLabel = "ADAYI MAKS YÜKSELT";
        private const string MaxedLabel = "ADA ZATEN MAKSİMUM";
        private static readonly Color MaxColor = new Color(0.20f, 0.45f, 0.30f, 0.92f);
        private static readonly Color MaxedColor = new Color(0.24f, 0.27f, 0.32f, 0.92f);

        private Image _maxImage;
        private TMPro.TextMeshProUGUI _maxLabel;

        private void OnMaxIsland()
        {
            CoalOperation op = LiveOperation();
            if (op == null) return;

            var wallet = ServiceLocator.Get<WalletService>();
            bool wasFree = wallet != null && wallet.FreePurchases;
            if (wallet != null) wallet.FreePurchases = true;

            // Kilitler önce: POWER PLANT'in eksenleri santral açılana kadar kilitli, sonra alınırsa
            // o istasyon seviye 0'da kalırdı.
            for (int u = 0; u < op.UnlockCount; u++)
                if (!op.IsUnlocked(u)) op.TryUnlock(u);

            for (int s = 0; s < op.StationCount; s++)
                for (int a = 0; a < op.AxisCount(s); a++)
                {
                    // Tavan eksen başına 50, ama sayı buradan okunmuyor: TryUpgrade false dönene kadar
                    // alıyor. Sayaç yalnızca bir gün kural değişirse diye duran sonsuz döngü freni.
                    int guard = 0;
                    while (!op.AxisMaxed(s, a) && guard++ < 500)
                        if (!op.TryUpgrade(s, a)) break;
                }

            // Pazar avlusu adanın gelirinin geçtiği tek kapı, ve kadrosuz bir avlu tam yüklü bir adayı
            // bile boğar. Satırı doğrudan tavana yazıyor: MarketService.TryBuy henüz ölçülmemiş bir
            // gelir tavanına karşı fiyat bulamayıp "satılık bir şey yok" diyebiliyor, ki bu da tam
            // olarak yeni maksimuma çıkarılmış bir adanın ilk saniyelerindeki durum.
            var market = ServiceLocator.Get<MarketService>();
            if (market != null)
            {
                MarketYard yard = market.Row(op.IslandKey);
                yard.depositSlots = MarketPrices.MaxLevel(YardUpgrade.DepositSlot);
                yard.queueSlots = MarketPrices.MaxLevel(YardUpgrade.QueueSlot);
                yard.hireCarry = yard.hireServe = yard.hireCollect = MarketFlow.MaxHireLevel;
                var data = ServiceLocator.Get<SaveData>();
                if (data != null) data.marketCarryLevel = MarketPrices.MaxCarryLevel;
            }

            if (wallet != null) wallet.FreePurchases = wasFree;
            if (_haptic != null) _haptic.Light();
            RefreshMaxButton();
            RefreshMaintenanceButtons();   // onarım faturası adanın gelirinden çıkıyor, bu onu da değiştirdi
        }

        private void RefreshMaxButton()
        {
            if (_maxLabel == null) return;
            CoalOperation op = LiveOperation();
            bool maxed = op != null && op.FullyMaxed;
            _maxLabel.text = maxed ? MaxedLabel : MaxLabel;
            if (_maxImage != null) _maxImage.color = maxed ? MaxedColor : MaxColor;
        }

        // ---------------- binaları eskit (bakım testi) ----------------

        // Yıpranma gerçek zamanla ölçülüyor, yani hasarlı hâli görmek normalde telefonu bir gün kenara
        // bırakmak demek. Bu şerit bunun yerine SAATİ geri alıp servisin kendi Evaluate'ini çağırıyor:
        // kısayol yok, hasarı üreten kod tam olarak oyuncunun başına gelecek kod. Her dokunuş bir günlük
        // yokluk ekliyor ve üst üste biniyor; ada tabana oturduğunda aynı düğme onu sıfırlıyor.
        private const float TestWearHours = 24f;
        private static readonly Color WearCleanColor = new Color(0.24f, 0.27f, 0.32f, 0.92f);
        private static readonly Color WearWornColor = new Color(0.55f, 0.35f, 0.12f, 0.92f);
        private static readonly Color WearFloorColor = new Color(0.75f, 0.20f, 0.20f, 0.92f);

        private Image _wearImage;
        private TMPro.TextMeshProUGUI _wearLabel;

        private void OnWear()
        {
            var maintenance = ServiceLocator.Get<MaintenanceService>();
            var data = ServiceLocator.Get<SaveData>();
            CoalOperation op = LiveOperation();
            if (maintenance == null || data == null || op == null || !maintenance.Enabled) return;

            if (AtFloor(maintenance, op.IslandKey)) maintenance.Reset(op.IslandKey);
            else
            {
                // İki damga birden: Evaluate ikisinin GEÇ olanını referans alıyor, yani yalnızca birini
                // geri almak hiçbir şey yapmaz.
                long back = (long)(TestWearHours * 3600f);
                data.conditionStampUnix -= back;
                data.savedUnixSeconds -= back;
                maintenance.Evaluate();
            }

            if (op.Wear != null) op.Wear.Refresh();   // kir taramayı beklemeden görünsün

            if (_haptic != null) _haptic.Light();
            RefreshMaintenanceButtons();
        }

        private static bool AtFloor(MaintenanceService maintenance, string island)
            => maintenance.IslandCondition(island) <= maintenance.Tuning.Floor + 0.0001f;

        private void RefreshWearButton()
        {
            if (_wearLabel == null) return;

            var maintenance = ServiceLocator.Get<MaintenanceService>();
            CoalOperation op = LiveOperation();
            if (maintenance == null || op == null || !maintenance.Enabled)
            {
                _wearLabel.text = "BAKIM: KAPALI";
                if (_wearImage != null) _wearImage.color = WearCleanColor;
                return;
            }

            string island = op.IslandKey;
            float condition = maintenance.IslandCondition(island);
            int percent = Mathf.RoundToInt(condition * 100f);

            if (AtFloor(maintenance, island))
            {
                _wearLabel.text = "BAKIM %" + percent + " — SIFIRLA";
                if (_wearImage != null) _wearImage.color = WearFloorColor;
                return;
            }

            _wearLabel.text = "BAKIM %" + percent + " — +1 GÜN ESKİT";
            if (_wearImage != null) _wearImage.color = condition < 1f ? WearWornColor : WearCleanColor;
        }

        // ---------------- anında onar ----------------

        // Gerçek onarım para alır ve ekibi dakikalarca sahada tutar; ikisi de hasarın GERİ alınışını
        // kontrol etmek isteyen birinin işine yaramaz. Bu şerit servisin kendi yollarından geçiyor
        // ama faturayı sıfır dakikalık gelire karşı kesip (yani bedava) ardından reklamın satın aldığı
        // atlama yolunu çağırıyor: onarım kodu gerçek, yalnızca bekleme ve ücret yok.
        private static readonly Color RepairReadyColor = new Color(0.20f, 0.45f, 0.30f, 0.92f);
        private static readonly Color RepairIdleColor = new Color(0.24f, 0.27f, 0.32f, 0.92f);

        private Image _repairImage;
        private TMPro.TextMeshProUGUI _repairLabel;

        private void OnRepair()
        {
            var maintenance = ServiceLocator.Get<MaintenanceService>();
            CoalOperation op = LiveOperation();
            if (maintenance == null || op == null || !maintenance.Enabled) return;

            if (maintenance.Repairing(op.IslandKey)) maintenance.SkipRepair(op.IslandKey);
            else if (maintenance.TryRepair(op.IslandKey, -1, 0d)) maintenance.SkipRepair(op.IslandKey);

            // Kir kendi yavaş taramasında zaten kalkacak, ama düğmeye basan biri sonucu ŞİMDİ görmeli.
            if (op.Wear != null) op.Wear.Refresh();

            if (_haptic != null) _haptic.Light();
            RefreshMaintenanceButtons();
        }

        private void RefreshRepairButton()
        {
            if (_repairLabel == null) return;

            var maintenance = ServiceLocator.Get<MaintenanceService>();
            CoalOperation op = LiveOperation();
            bool needs = maintenance != null && op != null && maintenance.Enabled
                         && (maintenance.NeedsRepair(op.IslandKey) || maintenance.Repairing(op.IslandKey));

            _repairLabel.text = needs ? "ONAR (BEDAVA, ANINDA)" : "ONARACAK BİR ŞEY YOK";
            if (_repairImage != null) _repairImage.color = needs ? RepairReadyColor : RepairIdleColor;
        }

        // ---------------- kir kademesini zorla ----------------

        // Kirin dört kademesi var ve bir adanın bunların arasında kendi başına dolaşması günler sürer.
        // Bu şerit kademeyi doğrudan dayatıyor, hasardan bağımsız olarak: görünümün tamamı, ada
        // gerçekten yıpranmayı beklemeden tek tek gözden geçirilebilsin diye. OTO'ya dönünce
        // gerçek bakım durumu neyi gerektiriyorsa ona geri düşer.
        private static readonly Color GrimeAutoColor = new Color(0.24f, 0.27f, 0.32f, 0.92f);
        private static readonly Color GrimeForcedColor = new Color(0.45f, 0.33f, 0.16f, 0.92f);

        private Image _grimeImage;
        private TMPro.TextMeshProUGUI _grimeLabel;

        private void OnGrime()
        {
            CoalOperation op = LiveOperation();
            if (op == null || op.Wear == null) return;

            int tier = op.Wear.ForcedTier + 1;      // -1 OTO -> 0 temiz -> 1 -> 2 -> 3 -> OTO
            if (tier > 3) tier = -1;
            op.Wear.ForcedTier = tier;
            op.Wear.Refresh();

            if (_haptic != null) _haptic.Light();
            RefreshGrimeButton();
        }

        private void RefreshGrimeButton()
        {
            if (_grimeLabel == null) return;

            CoalOperation op = LiveOperation();
            int tier = op != null && op.Wear != null ? op.Wear.ForcedTier : -1;

            _grimeLabel.text = tier < 0 ? "KİR: OTOMATİK" : "KİR KADEMESİ: " + tier + " / 3";
            if (_grimeImage != null) _grimeImage.color = tier < 0 ? GrimeAutoColor : GrimeForcedColor;
        }
#endif
    }
}
