using Game.Core;
using Game.Gameplay;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Floats a badge over every building the player can afford to upgrade right now, and opens that
    /// building's upgrade screen when the badge is tapped.
    ///
    /// The signal is AFFORDABILITY, not "an upgrade exists". A station has upgrades left for most of the
    /// run, so a badge that only asked whether the track was finished would sit over all five buildings
    /// permanently and stop meaning anything. Tied to the wallet it does the one job worth doing in an
    /// idle game: it tells the player where the money they just accumulated should go, and it goes away
    /// once they have spent it.
    ///
    /// Only stations with a body get one — TRAIN, ORE TRUCKS and CARGO TRUCKS are fleets that own no
    /// structure, so a badge for them would hang over open grass. Their upgrades live in the panel.
    ///
    /// MARKET is skipped too, and for the opposite reason: it has a body and something else is already
    /// standing on it. <see cref="MarketDoorMarker"/> hangs the ENTER MARKET button over that roof, and
    /// two badges stacked on one building covered the building. The way IN to a mode beats a reminder
    /// that a price upgrade is affordable, so the door keeps the spot; MARKET's upgrades are still one
    /// tap away through the HUD's upgrade panel, which is where the fleets' have always lived.
    ///
    /// One screen-space canvas holds every badge rather than a world-space canvas each. That keeps the
    /// badges one batch, and it keeps them a constant size on screen: pinned to the world they would
    /// shrink to nothing at the far end of the zoom range, which is exactly when the player is looking
    /// at the whole island trying to decide where to spend.
    /// </summary>
    public sealed class UpgradeReadyMarkers : MonoBehaviour
    {
        [Header("Görsel")]
        [Tooltip("Binanın üstünde duracak rozet. rozet_yukselt.")]
        [SerializeField] private Sprite badge;
        [Tooltip("Rozetin kenar uzunluğu, referans çözünürlükte piksel.")]
        [SerializeField] private float size = 104f;
        [Tooltip("Rozetin binanın tepesinden ne kadar yukarıda duracağı, dünya birimi.")]
        [SerializeField] private float worldLift = 10f;
        [Tooltip("HUD 100, satış yazıları 95, istasyon çipleri 90. Rozet çiplerin üstünde ama " +
                 "HUD'un altında durmalı.")]
        [SerializeField] private int sortingOrder = 92;

        [Header("Hareket")]
        [Tooltip("Aşağı yukarı süzülme genliği, piksel. Küçük tut: rozet dikkat çekmeli ama " +
                 "binanın üstünde durduğu yer belli olmalı.")]
        [SerializeField] private float bobPixels = 5f;
        [Tooltip("Bir süzülme turunun süresi. Uzadıkça sakinleşir.")]
        [SerializeField] private float bobSeconds = 2.4f;
        [Tooltip("Nefes alma miktarı. 0,03 = %3 büyüyüp küçülür.")]
        [SerializeField] private float pulseAmount = 0.03f;
        [Tooltip("Rozet ilk belirdiğinde yerine oturma süresi.")]
        [SerializeField] private float popSeconds = 0.28f;

        [Header("Bütçe")]
        [Tooltip("Cüzdanın yükseltmelere yetip yetmediği bu sıklıkta bakılır. Her karede bakmak " +
                 "beş istasyonun bütün eksenlerini taramak demek ve bunun için bir sebep yok.")]
        [SerializeField] private float scanSeconds = 0.25f;

        // easeOutBack's overshoot constant, the standard 1.70158.
        private const float PopOvershoot = 1.70158f;

        /// <summary>MARKET's index in IslandEconomy.Stations — the one station that gets no badge.</summary>
        private const int MarketStation = 6;

        private Camera _cam;
        private CoalOperation _op;
        private StationScreenUI _screen;
        private WalletService _wallet;

        private RectTransform _canvasRect;
        private RectTransform[] _rects;
        private GameObject[] _roots;
        private int[] _stations;      // station index each badge belongs to
        private bool[] _ready;
        private float[] _shownAt;     // unscaled time the badge became ready, for the pop-in
        private int _count;

        private float _scanIn;
        private float _rebindIn;

        private void Awake()
        {
            _cam = Camera.main;

            var go = new GameObject("YukseltRozetleriKanvas", typeof(Canvas), typeof(CanvasScaler),
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
        }

        /// <summary>
        /// Travelling to another island enables a different <see cref="CoalOperation"/> and disables this
        /// one, so the binding is re-checked on a slow timer rather than taken once — the same reason
        /// <see cref="SaleFx"/> does it. A new operation means new buildings, so the badges are rebuilt.
        /// </summary>
        private void Rebind()
        {
            if (_cam == null) _cam = Camera.main;
            if (_screen == null) _screen = FindAnyObjectByType<StationScreenUI>(FindObjectsInactive.Include);
            if (_wallet == null) _wallet = ServiceLocator.Get<WalletService>();

            if (_op != null && _op.enabled) return;

            CoalOperation live = null;
            var all = FindObjectsByType<CoalOperation>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
                if (all[i].enabled) { live = all[i]; break; }
            if (live == null || live == _op) return;

            _op = live;
            Build();
        }

        private void Build()
        {
            if (_roots != null)
                for (int i = 0; i < _roots.Length; i++)
                    if (_roots[i] != null) Destroy(_roots[i]);

            int total = _op.StationCount;
            _rects = new RectTransform[total];
            _roots = new GameObject[total];
            _stations = new int[total];
            _ready = new bool[total];
            _shownAt = new float[total];
            _count = 0;

            for (int s = 0; s < total; s++)
            {
                if (s == MarketStation) continue;      // the door button owns that roof; see the summary
                if (!_op.StationHasBody(s)) continue;
                _stations[_count] = s;
                _roots[_count] = BuildBadge(s, out _rects[_count]);
                _count++;
            }
        }

        private GameObject BuildBadge(int station, out RectTransform rect)
        {
            var go = new GameObject("Rozet_" + _op.StationName(station),
                                    typeof(RectTransform), typeof(Image), typeof(Button));
            rect = (RectTransform)go.transform;
            rect.SetParent(_canvasRect, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);

            var img = go.GetComponent<Image>();
            img.sprite = badge;
            img.preserveAspect = true;
            // The badge is the tap target, so it has to be raycastable — unlike every other floating
            // decoration in this game, which is deliberately not.
            img.raycastTarget = true;

            int captured = station;
            go.GetComponent<Button>().onClick.AddListener(() => OpenUpgrades(captured));

            go.SetActive(false);
            return go;
        }

        private void OpenUpgrades(int station)
        {
            if (_screen == null) _screen = FindAnyObjectByType<StationScreenUI>(FindObjectsInactive.Include);
            if (_screen != null) _screen.Open(station);
        }

        /// <summary>Whether any axis on this station is unlocked, unfinished and payable right now.</summary>
        private bool CanUpgradeNow(int station)
        {
            if (_wallet == null) return false;
            int axes = _op.AxisCount(station);
            for (int a = 0; a < axes; a++)
            {
                if (_op.AxisMaxed(station, a) || _op.AxisLocked(station, a)) continue;
                if (_wallet.CanAfford(_op.AxisCost(station, a))) return true;
            }
            return false;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            _rebindIn -= dt;
            if (_rebindIn <= 0f) { _rebindIn = 1f; Rebind(); }
            if (_op == null || _count == 0 || _cam == null) return;

            // Behind the upgrade screen the badges would be pointing at a panel that is already open.
            bool suppressed = _screen != null && _screen.IsOpen;

            _scanIn -= dt;
            bool rescan = _scanIn <= 0f;
            if (rescan) _scanIn = scanSeconds;

            float now = Time.unscaledTime;

            for (int i = 0; i < _count; i++)
            {
                int station = _stations[i];

                if (rescan)
                {
                    bool ready = !suppressed && CanUpgradeNow(station);
                    if (ready && !_ready[i]) _shownAt[i] = now;   // fresh badge, pop it in
                    _ready[i] = ready;
                }

                Vector3 world = Vector3.zero;
                bool placed = _ready[i] && _op.StationAnchor(station, out world);
                if (!placed)
                {
                    if (_roots[i].activeSelf) _roots[i].SetActive(false);
                    continue;
                }

                world.y += worldLift;
                Vector3 screen = _cam.WorldToScreenPoint(world);
                if (screen.z <= 0f)                       // behind the camera
                {
                    if (_roots[i].activeSelf) _roots[i].SetActive(false);
                    continue;
                }

                Vector2 local;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRect, new Vector2(screen.x, screen.y), null, out local);

                // One period for both, with the breath a quarter-cycle behind the bob. They used to run
                // at different rates so the badge would not read as a single mechanical pump — but two
                // frequencies beating against each other never settle, and the badge came out restless
                // instead of alive. A quarter-cycle offset breaks the pump without the drift: the badge
                // is widest as it passes through the middle of its rise, not at the top of it.
                float phase = i * 0.7f;
                float cycle = (now / bobSeconds + phase) * Mathf.PI * 2f;
                float bob = Mathf.Sin(cycle) * bobPixels;
                float breath = 1f + Mathf.Sin(cycle - Mathf.PI * 0.5f) * pulseAmount;

                float age = now - _shownAt[i];
                float pop = 1f;
                if (age < popSeconds && popSeconds > 0f)
                {
                    float u = age / popSeconds - 1f;
                    pop = u * u * ((PopOvershoot + 1f) * u + PopOvershoot) + 1f;
                }

                _rects[i].anchoredPosition = new Vector2(local.x, local.y + bob);
                float scale = breath * pop;
                _rects[i].localScale = new Vector3(scale, scale, 1f);

                if (!_roots[i].activeSelf) _roots[i].SetActive(true);
            }
        }
    }
}
