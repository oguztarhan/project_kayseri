using Game.Core;
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
        [SerializeField] private Button languageButton;   // dil seçimi ekranı gelince bağlanacak
        [SerializeField] private Button privacyButton;
        [SerializeField] private Button restoreButton;    // gerçek IAP SDK'sı gelince bağlanacak
        [Tooltip("Gizlilik politikası adresi — boşken satır hiçbir şey yapmaz.")]
        [SerializeField] private string privacyUrl = "";

        private AudioService _audio;
        private HapticService _haptic;
        private bool _hapticOn;

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
            RefreshSwitch();

            if (panelRoot != null) panelRoot.SetActive(false);
        }

        public void Toggle()
        {
            if (panelRoot != null) panelRoot.SetActive(!panelRoot.activeSelf);
        }

        public void Hide()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void Apply(float sfx, float music, bool haptic)
        {
            if (_audio != null) { _audio.Sfx = sfx; _audio.Music = music; }
            if (_haptic != null) _haptic.Enabled = haptic;
        }

        private void OnSfx(float v)
        {
            if (_audio != null) _audio.Sfx = v;
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
    }
}
