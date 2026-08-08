using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The settings panel (panel_ayarlar + serit_ayarlar + satir_ayar from the Figma set): SFX and
    /// music sliders, a vibration switch, and the language / privacy / restore-purchases rows.
    /// Editor-authored — the whole hierarchy lives in the UI_Ayarlar prefab and every reference is
    /// wired in the Inspector, so rows, icons and spacing are all tunable from the hierarchy.
    ///
    /// Values apply to <see cref="AudioService"/> / <see cref="HapticService"/> immediately and are
    /// persisted in PlayerPrefs (options are device preferences, not save-game state). The language
    /// and restore rows are wired but inert until their systems exist; privacy opens
    /// <see cref="privacyUrl"/> once it is filled in.
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
        [SerializeField] private Button privacyButton;
        [SerializeField] private Button restoreButton;    // gerçek IAP SDK'sı gelince bağlanacak
        [Tooltip("Gizlilik politikası adresi — boşken satır hiçbir şey yapmaz.")]
        [SerializeField] private string privacyUrl = "";

        [Header("Dil paneli")]
        [Tooltip("Dil seçim ekranının parçaları. Çalışma anında kurulduğu için kendi Inspector'ı yok, " +
                 "sanatı buradan devralıyor. Boş bırakılan yuva UiSkin'e düşer.")]
        [SerializeField] private LanguageMenuUI.Skin languageSkin;

        private const float PreviewInterval = 0.14f;

        private AudioService _audio;
        private HapticService _haptic;
        private bool _hapticOn;
        private float _nextPreview;

        private void Start()
        {
            _audio = ServiceLocator.Get<AudioService>();
            _haptic = ServiceLocator.Get<HapticService>();

            // kayıtlı tercihleri yükle; ilk açılışta servislerin mevcut değerleri varsayılan olur
            float sfx = PlayerPrefs.GetFloat(KeySfx, _audio != null ? _audio.Sfx : 0.8f);
            float music = PlayerPrefs.GetFloat(KeyMusic, _audio != null ? _audio.Music : 0.6f);
            _hapticOn = PlayerPrefs.GetInt(KeyHaptic, _haptic != null && _haptic.Enabled ? 1 : 0) == 1;
            Apply(sfx, music, _hapticOn);

            if (sfxSlider != null) { sfxSlider.SetValueWithoutNotify(sfx); sfxSlider.onValueChanged.AddListener(OnSfx); }
            if (musicSlider != null) { musicSlider.SetValueWithoutNotify(music); musicSlider.onValueChanged.AddListener(OnMusic); }
            if (hapticButton != null) hapticButton.onClick.AddListener(OnHaptic);
            if (closeButton != null) closeButton.onClick.AddListener(Hide);
            if (privacyButton != null) privacyButton.onClick.AddListener(OnPrivacy);
            if (languageButton != null) languageButton.onClick.AddListener(OnLanguage);
            RefreshSwitch();

            BuildTestButton();

            if (panelRoot != null) panelRoot.SetActive(false);
            UiPanelSound.Attach(panelRoot);   // panel kapatıldıktan SONRA — açılış sesi boot'ta çalmasın
        }

        public void Toggle()
        {
            if (panelRoot != null) panelRoot.SetActive(!panelRoot.activeSelf);
            RefreshTestButton();   // başka bir yerden açılmış olabilir — etiket açılışta doğru olsun
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

        // ---------------- test modu ----------------

        // Her yerde bedava satın alma + oturum boyunca kayıt askıda, böylece her ada ve her yükseltme
        // gerçek kayda dokunmadan denenebilir. Eskiden istasyon ekranının sol üst köşesindeydi ve
        // #if UNITY_EDITOR || DEVELOPMENT_BUILD ile kapatılıyordu; telefondan çekilen videoda oyun
        // ekranının üstünde göründüğü için buraya taşındı ve derleme kapısı kaldırıldı. Ayarlar
        // penceresi kapalıyken bu düğme de kapalı, dolayısıyla kayda hiç girmiyor.
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

        private void BuildTestButton()
        {
            if (panelRoot == null) return;

            var go = new GameObject("TestModu", typeof(RectTransform));
            go.transform.SetParent(panelRoot.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 40f);
            rt.sizeDelta = new Vector2(560f, 92f);

            _testImage = go.AddComponent<Image>();
            _testImage.color = TestOffColor;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = _testImage;
            btn.onClick.AddListener(OnTestMode);

            var tgo = new GameObject("Etiket", typeof(RectTransform));
            tgo.transform.SetParent(go.transform, false);
            var trt = (RectTransform)tgo.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            _testLabel = tgo.AddComponent<TMPro.TextMeshProUGUI>();
            _testLabel.fontSize = 34;
            _testLabel.alignment = TMPro.TextAlignmentOptions.Center;
            _testLabel.raycastTarget = false;

            RefreshTestButton();
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
    }
}
