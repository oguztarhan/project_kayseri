using Game.Core;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>A compact, non-blocking card shown during gameplay at a positive moment.</summary>
    public sealed class RatingPromptUI : MonoBehaviour
    {
        [SerializeField] private GameObject promptRoot;
        [SerializeField] private Button rateButton;
        [SerializeField] private Button laterButton;
        [SerializeField] private Button closeButton;
        [SerializeField, Min(0f)] private float showDelay = 1.5f;

        private RatingPromptService _service;
        private SettingsUI _settings;
        private ContractUI _contracts;
        private WelcomeBackUI _welcome;
        private bool _pending;
        private float _showAt;
        private float _nextDueCheck;

        private void Start()
        {
            _service = ServiceLocator.Get<RatingPromptService>();
            _settings = GetComponent<SettingsUI>();
            _contracts = FindFirstObjectByType<ContractUI>();
            _welcome = FindFirstObjectByType<WelcomeBackUI>();

            if (rateButton != null) rateButton.onClick.AddListener(OnRate);
            if (laterButton != null) laterButton.onClick.AddListener(OnLater);
            if (closeButton != null) closeButton.onClick.AddListener(OnLater);
            if (promptRoot != null) promptRoot.SetActive(false);
            UiPanelSound.Attach(promptRoot);

            if (_service != null) _service.Requested += Queue;
        }

        private void OnDestroy()
        {
            if (_service != null) _service.Requested -= Queue;
        }

        private void Update()
        {
            if (_service == null) return;

            if (Time.unscaledTime >= _nextDueCheck)
            {
                _nextDueCheck = Time.unscaledTime + 1f;
                _service.TryRequestPostponed();
            }

            if (!_pending || Time.unscaledTime < _showAt || IsAnotherPanelOpen()) return;
            _pending = false;

            // iOS Apple'ın kendi sayfasını ister; önüne kendi "beğendin mi?" kartımızı koymak HIG
            // ihlali ve bilinen bir ret sebebi. Sayfa hiç açılmayabilir (yıllık kota işletim sisteminde),
            // ama tekrar sormak kendi zamanlama bütçemizi boşa harcar — o yüzden burada tamamlanmış sayılır.
            if (IOSReview.Available)
            {
                IOSReview.Request();
                _service.Complete();
                ServiceLocator.Get<IAnalytics>()?.Log("rating_prompt_native");
                return;
            }

            if (promptRoot != null) promptRoot.SetActive(true);
            ServiceLocator.Get<IAnalytics>()?.Log("rating_prompt_shown");
        }

        private bool IsAnotherPanelOpen()
            => (_contracts != null && _contracts.IsOpen)
               || (_settings != null && _settings.IsOpen)
               || (_welcome != null && _welcome.IsOpen);

        private void Queue()
        {
            _pending = true;
            _showAt = Time.unscaledTime + showDelay;
        }

        private void OnLater()
        {
            _service?.Postpone();
            _pending = false;
            if (promptRoot != null) promptRoot.SetActive(false);
        }

        private void OnRate()
        {
            _service?.Complete();
            _pending = false;
            if (promptRoot != null) promptRoot.SetActive(false);
            if (_settings != null) _settings.OpenStorePage();
            else StorePage.Open(string.Empty);
        }
    }
}
