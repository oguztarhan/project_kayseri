using Game.Core;
using Game.Gameplay;
using Game.Systems;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The way in. A button floating over the island's market building that opens the yard.
    ///
    /// Modelled on <see cref="UpgradeReadyMarkers"/>, and for its reasons: one screen-space canvas so
    /// the badge stays a constant size through the whole zoom range, and a slow re-bind rather than a
    /// cached operation, because travelling to another island swaps which
    /// <see cref="CoalOperation"/> is live and the market building moves with it.
    ///
    /// Deliberately separate from the MARKET upgrade panel rather than a button inside it. Walking into
    /// the yard and buying a price upgrade are different intentions, and burying the first one two taps
    /// deep inside the second is how a whole mode goes unnoticed.
    /// </summary>
    public sealed class MarketDoorMarker : MonoBehaviour
    {
        [Header("Hedef")]
        [Tooltip("Açılacak sahne. Build Settings'te ekli olmalı, yoksa dokunuş bir şey yapmaz.")]
        [SerializeField] private string marketSceneName = "Market";

        [Header("Görsel")]
        [Tooltip("Düğmenin genişliği ve yüksekliği, referans çözünürlükte piksel.")]
        [SerializeField] private Vector2 size = new Vector2(330f, 112f);

        [Tooltip("Yazı boyu. Kit sarısı üzerinde beyaz kalın yazı, oyunun geri kalanıyla aynı.")]
        [SerializeField, Min(10)] private int fontSize = 40;

        [Tooltip("Nefes alma miktarı. 0,04 = %4 büyüyüp küçülür. Sıfır yaparsan düğme cansız durur.")]
        [SerializeField, Range(0f, 0.2f)] private float pulseAmount = 0.045f;
        [Tooltip("Binanın tepesinden ne kadar yukarıda durduğu, dünya birimi.")]
        [SerializeField] private float worldLift = 4f;

        [Tooltip("Dünya noktasından sonra uygulanan ekran kayması, piksel. Negatif Y aşağı iter.\n\n" +
                 "Sıfır bırakırsan bu düğme MARKET'in yükseltme rozetiyle üst üste biner: ikisi de aynı " +
                 "binanın tepesine tutunuyor. Rozet yukarıda kalsın, kapı aşağıda dursun diye var.")]
        [SerializeField] private Vector2 screenOffset = new Vector2(0f, -150f);
        [Tooltip("HUD 100, satış yazıları 95, yükseltme rozetleri 92. Kapı rozetlerle aynı katta.")]
        [SerializeField] private int sortingOrder = 92;
        [SerializeField] private Color tint = new Color(0.16f, 0.18f, 0.24f, 0.94f);

        [Header("Hareket")]
        [Tooltip("Süzülme genliği, piksel.")]
        [SerializeField] private float bobPixels = 6f;
        [Tooltip("Bir süzülme turunun süresi.")]
        [SerializeField, Min(0.1f)] private float bobSeconds = 2.6f;

        // MARKET's index in IslandEconomy.Stations. Named here rather than reached for through the
        // simulation because this assembly has no business knowing the station order beyond this one.
        private const int MarketStation = 6;

        private Camera _cam;
        private CoalOperation _op;
        private RectTransform _canvasRect, _rect;
        private GameObject _root;
        private float _rebindIn;
        private bool _opening;

        private void Awake()
        {
            _cam = Camera.main;

            var go = new GameObject("MarketKapisiKanvas", typeof(Canvas), typeof(CanvasScaler),
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

            // The kit's primary-action button, the same art every other "do the thing" button in the
            // game wears. It used to pass null, which fell through to UiSkin's plain white quad tinted
            // dark grey — a flat rectangle sitting on a hand-drawn island, which is exactly as bad as
            // it sounds. UiBuild leaves pre-coloured kit art untinted, so the tint below is only ever
            // used on a project with no skin wired.
            Button button = UiBuild.Btn(_canvasRect, "MarketeGir", Loc.T("market.gir"),
                                        UiSkin.ButtonYellow, tint, fontSize, Open);
            _rect = (RectTransform)button.transform;
            _rect.anchorMin = _rect.anchorMax = new Vector2(0.5f, 0.5f);
            _rect.pivot = new Vector2(0.5f, 0.5f);
            _rect.sizeDelta = size;
            _root = button.gameObject;
            _root.SetActive(false);
        }

        private void Open()
        {
            if (_opening) return;
            _opening = true;
            // Async: the synchronous load blocked the frame for the whole swap, which on a phone
            // reads as the game freezing at the tap. The island keeps animating until Market is ready.
            SceneManager.LoadSceneAsync(marketSceneName, LoadSceneMode.Single);
        }

        /// <summary>Travelling enables a different operation, so which one is live is re-checked on a timer.</summary>
        private void Rebind()
        {
            if (_cam == null) _cam = Camera.main;
            if (_op != null && _op.enabled) return;

            var all = FindObjectsByType<CoalOperation>(FindObjectsInactive.Exclude);
            for (int i = 0; i < all.Length; i++)
                if (all[i].enabled) { _op = all[i]; return; }
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _rebindIn -= dt;
            if (_rebindIn <= 0f) { _rebindIn = 1f; Rebind(); }
            if (_op == null || _cam == null) { Hide(); return; }

            Vector3 world;
            if (!_op.StationAnchor(MarketStation, out world)) { Hide(); return; }

            world.y += worldLift;
            Vector3 screen = _cam.WorldToScreenPoint(world);
            if (screen.z <= 0f) { Hide(); return; }      // behind the camera

            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect, new Vector2(screen.x, screen.y), null, out local);

            float cycle = Time.unscaledTime / bobSeconds * Mathf.PI * 2f;
            float bob = Mathf.Sin(cycle) * bobPixels;
            // The upgrade badge owns the space above the building; this sits below it, so the two can
            // both be pointing at the market without landing on top of each other.
            _rect.anchoredPosition = new Vector2(local.x + screenOffset.x, local.y + screenOffset.y + bob);

            // A quarter-cycle behind the bob, the same offset the upgrade badges breathe on: widest as
            // it passes through the middle of its rise rather than at the top, which is what stops it
            // reading as a mechanical pump.
            float breath = 1f + Mathf.Sin(cycle - Mathf.PI * 0.5f) * pulseAmount;
            _rect.localScale = new Vector3(breath, breath, 1f);

            if (!_root.activeSelf) _root.SetActive(true);
        }

        private void Hide()
        {
            if (_root != null && _root.activeSelf) _root.SetActive(false);
        }
    }
}
