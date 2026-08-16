using Game.Core;
using Game.Gameplay;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Floats a badge over the pier whenever the ship at the port wants the player: three jobs on the
    /// table, or a delivered one waiting to be claimed. Tapping it opens the contract screen.
    ///
    /// The port is the only place in this game that ever has business of its own, and a ship that sailed
    /// in while the player was staring at the smelter is business they would miss. The HUD chip says
    /// READY, but the HUD is a row of nine buttons — the badge says WHERE, and the where is the point:
    /// the contract belongs to the harbour, not to a button.
    ///
    /// Same construction as <see cref="UpgradeReadyMarkers"/>, and for the same reasons: one screen-space
    /// canvas rather than a world-space one, so the badge stays a constant size across the zoom range and
    /// costs one batch.
    /// </summary>
    public sealed class PortContractMarker : MonoBehaviour
    {
        [Header("Görsel")]
        [Tooltip("İskelenin üstünde duracak rozet. Boş bırakılırsa yükseltme rozetinin sanatı kullanılır.")]
        [SerializeField] private Sprite badge;
        [Tooltip("Rozetin kenar uzunluğu, referans çözünürlükte piksel. Yükseltme rozetlerinden biraz " +
                 "büyük: limandaki iş nadir ve tek.")]
        [SerializeField] private float size = 124f;
        [Tooltip("Rozetin iskelenin üstünde ne kadar yukarıda duracağı, dünya birimi.")]
        [SerializeField] private float worldLift = 12f;
        [Tooltip("Yükseltme rozetleri 92'de. Liman rozeti onlarla aynı katmanda ama HUD'un altında.")]
        [SerializeField] private int sortingOrder = 92;
        [Tooltip("Liman kadrajın dışındayken rozet ekran kenarına yapışır. Bu, kenardan bırakacağı " +
                 "boşluk — HUD'un alt sırası referans çözünürlükte ~230 piksel, rozet onun üstünde kalmalı.")]
        [SerializeField] private float edgeMargin = 200f;

        [Header("Hareket")]
        [SerializeField] private float bobPixels = 6f;
        [SerializeField] private float bobSeconds = 2.2f;
        [SerializeField] private float pulseAmount = 0.05f;
        [SerializeField] private float popSeconds = 0.32f;

        // easeOutBack's overshoot constant, the standard 1.70158.
        private const float PopOvershoot = 1.70158f;

        private Camera _cam;
        private CoalOperation _op;
        private ContractService _contract;
        private ContractUI _screen;

        private RectTransform _canvasRect;
        private RectTransform _rect;
        private GameObject _root;
        private bool _shown;
        private float _shownAt;
        private float _rebindIn;

        public RectTransform MarkerRect => _rect;

        private void Awake()
        {
            _cam = Camera.main;

            var go = new GameObject("LimanRozetiKanvas", typeof(Canvas), typeof(CanvasScaler),
                                    typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var sc = go.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1080f, 1920f);
            sc.matchWidthOrHeight = 0.5f;
            _canvasRect = (RectTransform)go.transform;

            _root = new GameObject("LimanRozeti", typeof(RectTransform), typeof(Image), typeof(Button));
            _rect = (RectTransform)_root.transform;
            _rect.SetParent(_canvasRect, false);
            _rect.anchorMin = _rect.anchorMax = new Vector2(0.5f, 0.5f);
            _rect.pivot = new Vector2(0.5f, 0.5f);
            // The authored scene used to override this to 170 px, which read like a second HUD button.
            // Keep the harbour marker deliberately small and local to the pier.
            float markerSize = Mathf.Min(size, 86f);
            _rect.sizeDelta = new Vector2(markerSize, markerSize);

            var img = _root.GetComponent<Image>();
            img.sprite = badge;
            img.preserveAspect = true;
            img.raycastTarget = true;    // the badge is the tap target

            _root.GetComponent<Button>().onClick.AddListener(OpenScreen);
            _root.SetActive(false);

            // Kontrat erişimi yeniden ana HUD butonunda; limandaki geçici kopya kapalı kalsın.
            enabled = false;
        }

        /// <summary>
        /// Travelling enables a different <see cref="CoalOperation"/>, so the binding is re-checked on a
        /// slow timer rather than taken once — the same reason <see cref="UpgradeReadyMarkers"/> does it.
        /// </summary>
        private void Rebind()
        {
            if (_cam == null) _cam = Camera.main;
            if (_contract == null) _contract = ServiceLocator.Get<ContractService>();
            if (_screen == null) _screen = FindAnyObjectByType<ContractUI>(FindObjectsInactive.Include);
            if (_op != null && _op.enabled) return;

            var all = FindObjectsByType<CoalOperation>(FindObjectsInactive.Exclude);
            for (int i = 0; i < all.Length; i++)
                if (all[i].enabled) { _op = all[i]; return; }
        }

        private void OpenScreen()
        {
            if (_screen == null) _screen = FindAnyObjectByType<ContractUI>(FindObjectsInactive.Include);
            if (_screen != null) _screen.Open();
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _rebindIn -= dt;
            if (_rebindIn <= 0f) { _rebindIn = 1f; Rebind(); }
            if (_contract == null || _op == null || _cam == null) { Hide(); return; }

            // Offers to pick or a reward to claim. A running job is deliberately not badged: the clock
            // is on the HUD, and a badge that sat over the harbour for the whole ten minutes would be
            // decoration rather than a call to act. Behind the contract screen it would be pointing at
            // a panel that is already open.
            bool want = (_contract.HasOffers || _contract.Claimable)
                        && (_screen == null || !_screen.IsOpen);
            Vector3 world;
            if (!want || !_op.PortAnchor(out world)) { Hide(); return; }

            world.y += worldLift;
            Vector3 screen = _cam.WorldToScreenPoint(world);

            // It belongs to the harbour. When the harbour leaves the camera, the marker leaves too;
            // clamping it to an edge made it follow the player around like a HUD control.
            const float viewportMargin = 18f;
            bool onScreen = screen.z > 0f
                            && screen.x >= viewportMargin && screen.x <= Screen.width - viewportMargin
                            && screen.y >= viewportMargin && screen.y <= Screen.height - viewportMargin;
            if (!onScreen) { Hide(); return; }

            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect, new Vector2(screen.x, screen.y), null, out local);

            if (!_shown)
            {
                _shown = true;
                _shownAt = Time.unscaledTime;
                _root.SetActive(true);
            }

            float now = Time.unscaledTime;
            float age = now - _shownAt;
            float pop = 1f;
            if (age < popSeconds && popSeconds > 0f)
            {
                float u = age / popSeconds - 1f;
                pop = u * u * ((PopOvershoot + 1f) * u + PopOvershoot) + 1f;
            }

            _rect.anchoredPosition = local;
            float scale = pop;
            _rect.localScale = new Vector3(scale, scale, 1f);
        }

        private void Hide()
        {
            if (!_shown) return;
            _shown = false;
            if (_root != null) _root.SetActive(false);
        }
    }
}
