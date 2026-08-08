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

        [Header("Bildirim test modu")]
        [Tooltip("Açıkken altı bildirim, gerçek saatler yerine GameBootstrap'taki test aralığıyla " +
                 "arka arkaya gider. Kurulu bir APK'yı yeniden derlemeden denemek için.")]
        [SerializeField] private Button testButton;
        [SerializeField] private Image testImage;

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
        private bool _testOn;
        private float _nextPreview;

        private void Start()
        {
            _audio = ServiceLocator.Get<AudioService>();
            _haptic = ServiceLocator.Get<HapticService>();

            // kayıtlı tercihleri yükle; ilk açılışta servislerin mevcut değerleri varsayılan olur
            float sfx = PlayerPrefs.GetFloat(KeySfx, _audio != null ? _audio.Sfx : 0.8f);
            float music = PlayerPrefs.GetFloat(KeyMusic, _audio != null ? _audio.Music : 0.6f);
            _hapticOn = PlayerPrefs.GetInt(KeyHaptic, _haptic != null && _haptic.Enabled ? 1 : 0) == 1;
            _testOn = PlayerPrefs.GetInt(GameBootstrap.NotificationTestKey, 0) == 1;
            Apply(sfx, music, _hapticOn);

            if (sfxSlider != null) { sfxSlider.SetValueWithoutNotify(sfx); sfxSlider.onValueChanged.AddListener(OnSfx); }
            if (musicSlider != null) { musicSlider.SetValueWithoutNotify(music); musicSlider.onValueChanged.AddListener(OnMusic); }
            if (hapticButton != null) hapticButton.onClick.AddListener(OnHaptic);
            if (testButton != null) testButton.onClick.AddListener(OnTestMode);
            if (closeButton != null) closeButton.onClick.AddListener(Hide);
            if (privacyButton != null) privacyButton.onClick.AddListener(OnPrivacy);
            if (languageButton != null) languageButton.onClick.AddListener(OnLanguage);
            RefreshSwitch();

            if (panelRoot != null) panelRoot.SetActive(false);
            UiPanelSound.Attach(panelRoot);   // panel kapatıldıktan SONRA — açılış sesi boot'ta çalmasın
        }

        public void Toggle()
        {
            if (panelRoot != null) panelRoot.SetActive(!panelRoot.activeSelf);
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
            if (testImage != null) testImage.sprite = _testOn ? switchOn : switchOff;
        }

        /// <summary>
        /// Flips the notification bench schedule. Writing the pref is what makes it survive a restart;
        /// pushing it into the live service is what makes it take effect without one.
        ///
        /// Nothing is rescheduled here on purpose — the queue is built when the app goes to background,
        /// so the switch is read at the moment it matters. Flip it, then leave the game.
        /// </summary>
        private void OnTestMode()
        {
            _testOn = !_testOn;
            PlayerPrefs.SetInt(GameBootstrap.NotificationTestKey, _testOn ? 1 : 0);
            PlayerPrefs.Save();

            var notifications = ServiceLocator.Get<NotificationService>();
            if (notifications != null) notifications.TestMode = _testOn;

            RefreshSwitch();
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
    }
}
