using Game.Core;
using Game.Data;
using Game.Gameplay;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The way to put a worn island right: a wrench badge over every building that needs it, and a
    /// HEPSİNİ ONAR button in the corner for everything at once.
    ///
    /// Until this existed the maintenance mechanic was one-way. <see cref="MaintenanceService"/> could
    /// wear an island down, <see cref="DistrictWear"/> painted the grime, and the only thing in the
    /// game that could hand a repair back was the developer button in <see cref="SettingsUI"/> — so a
    /// player who left for a fortnight came back to a dirty, slower island and no way to answer it.
    ///
    /// Built like <see cref="UpgradeReadyMarkers"/>, and for its reasons: screen-space canvases so the
    /// badges stay a readable size through the whole zoom range, and a slow re-bind rather than a
    /// cached operation, because travelling swaps which <see cref="CoalOperation"/> is live and every
    /// building moves with it.
    ///
    /// A WRENCH, not a button, and it sits ABOVE the upgrade badge rather than on it. The two share
    /// every roof on the island and they are answering different questions — spend the money you have,
    /// or undo the damage you took — so they are stacked, not merged, and the badge art is the same
    /// rounded-square rozet as the upgrade one in a different colour.
    ///
    /// NO TEXT ON IT. A price chip under the wrench was a black box hanging off a hand-drawn island,
    /// and a second label stacked under the upgrade badge on the same roof. So the badge says what it
    /// is and nothing else, and a running repair is drawn by filling the wrench itself back in —
    /// greyed out while the crew works, sweeping round to full colour as they finish. The bill for a
    /// building is not quoted before the tap; the one number the player gets is the total on
    /// HEPSİNİ ONAR.
    ///
    /// ONLY WHEN IT LOOKS DIRTY. The threshold is <see cref="DistrictWear.VisibleDamage"/>, the same
    /// number that puts the first tier of grime on a district — so a wrench never floats over a
    /// building that reads as spotless. Wear below that is real and still charged for by HEPSİNİ ONAR;
    /// it is simply not worth interrupting anyone about.
    ///
    /// AS MANY CREWS AS THE PLAYER WILL PAY FOR. Each building runs its own repair on its own clock, so
    /// four taps put four crews out and each badge counts its own down.
    ///
    /// TWO CANVASES, at different heights in the stack and against different design sheets. The wrench
    /// badges sit at 92 with the other world badges — under the HUD, which is where a thing floating
    /// over the island belongs — and against 1080x1920, so a wrench comes out exactly the size of the
    /// upgrade badge beside it. The HEPSİNİ ONAR button is HUD furniture: it goes at 102, above the
    /// HUD's own canvas so a transparent edge of a HUD button cannot eat the tap and below every panel
    /// (station screen 108, store 110, map 140) so an open screen still covers it, and against the
    /// HUD's own 1080x2340 sheet so its corner offsets mean the same thing the HUD's do.
    /// </summary>
    public sealed class RepairMarkers : MonoBehaviour
    {
        [Header("Bina rozeti")]
        [Tooltip("Binanın üstünde duracak anahtar rozeti. rozet_onarim.")]
        [SerializeField] private Sprite badge;
        [Tooltip("Rozetin kenar uzunluğu, referans çözünürlükte piksel. Yükseltme rozetiyle aynı olmalı.")]
        [SerializeField] private float badgeSize = 104f;
        [Tooltip("Binanın tepesinden ne kadar yukarıda durduğu, dünya birimi. Yükseltme rozetiyle aynı.")]
        [SerializeField] private float worldLift = 10f;
        [Tooltip("Dünya noktasından sonra uygulanan ekran kayması, piksel.\n\n" +
                 "Varsayılan yukarı itiyor: yükseltme rozeti aynı çatının tam üstünde duruyor ve iki " +
                 "rozet üst üste binince ikisi de okunmuyor. Aşağı sarkıtmak için Y'yi negatif yap.")]
        [SerializeField] private Vector2 badgeOffset = new Vector2(0f, 148f);
        [Tooltip("HUD 100, satış yazıları 95, yükseltme rozetleri 92. Anahtar rozetleri aynı katta.")]
        [SerializeField] private int badgeSortingOrder = 92;

        [Header("Hepsini onar düğmesi")]
        [Tooltip("Adada onarılacak bir şey varken ekranın köşesinde duran düğme. Kapatılırsa yalnızca " +
                 "bina rozetleri kalır — o zaman filo istasyonları hiç onarılamaz ve bakım bonusu hiç " +
                 "çalışmaz.")]
        [SerializeField] private bool showRepairAll = true;
        [Tooltip("Düğmenin ekrandaki tutunma noktası. Varsayılan (0,0) sol alt köşe: HUD'un alt " +
                 "düğme şeridi ortadan başlıyor, sol alt köşe boş.")]
        [SerializeField] private Vector2 pillAnchor = new Vector2(0f, 0f);
        [Tooltip("Tutunma noktasından kayma, HUD'un kendi 1080x2340 sayfasının pikselleri. Y=44 alt " +
                 "sıradaki düğmelerle aynı hizada durur.")]
        [SerializeField] private Vector2 pillOffset = new Vector2(34f, 44f);
        [SerializeField] private Vector2 pillSize = new Vector2(340f, 100f);
        [SerializeField, Min(10)] private int pillLabelSize = 30;
        [Tooltip("HUD'un üstünde (100), her panelin altında (istasyon ekranı 108, mağaza 110, harita " +
                 "140). Sınıf açıklamasına bak: ikisinin arasında olmak zorunda.")]
        [SerializeField] private int pillSortingOrder = 102;

        [Header("Hareket")]
        [Tooltip("Nefes alma miktarı. 0,03 = %3 büyüyüp küçülür. Sıfırlarsan rozet kıpırdamaz.")]
        [SerializeField] private float pulseAmount = 0.03f;
        [Tooltip("Bir nefes turunun süresi. Uzadıkça sakinleşir.")]
        [SerializeField] private float pulseSeconds = 2.4f;
        [Tooltip("Rozet ilk belirdiğinde yerine oturma süresi.")]
        [SerializeField] private float popSeconds = 0.28f;

        [Header("Bütçe")]
        [Tooltip("Onarım durumu bu sıklıkta taranır. Aşınma saatler ölçeğinde ilerler; her karede " +
                 "bakmanın bir anlamı yok.")]
        [SerializeField] private float scanSeconds = 0.25f;

        // easeOutBack's overshoot constant, the standard 1.70158 — the same pop UpgradeReadyMarkers uses.
        private const float PopOvershoot = 1.70158f;

        /// <summary>What <see cref="MaintenanceService.TryRepair"/> reads as "everything worn".</summary>
        private const int WholeIsland = -1;

        /// <summary>Greyed out: no money for this one, or a crew is already on it.</summary>
        private static readonly Color Unavailable = new Color(0.58f, 0.58f, 0.62f, 0.9f);

        private Camera _cam;
        private CoalOperation _op;
        private MaintenanceService _maintenance;
        private WalletService _wallet;
        private AudioService _audio;
        private HapticService _haptic;

        private RectTransform _worldRect, _hudRect;

        // One entry per bodied station, rebuilt when an operation binds.
        private GameObject[] _roots;
        private RectTransform[] _rects;
        private Image[] _images;
        private Image[] _progress;      // the same wrench again, radially filled as the crew works
        private int[] _stations;
        private bool[] _shown;
        private float[] _shownAt;
        private int _count;

        private GameObject _pillRoot;
        private RectTransform _pillRect;
        private Image _pillImage;
        private Text _pillLabel;
        private string _pillWritten;
        private float _pillShownAt;
        private bool _pillShown;

        private float _scanIn, _rebindIn;
        private bool _subscribed;

        private void Awake()
        {
            _cam = Camera.main;
            _worldRect = UiBuild.Canvas(transform, "OnarimRozetleriKanvas", badgeSortingOrder);
            BuildPill();
        }

        private void OnDestroy()
        {
            if (_subscribed && _maintenance != null) _maintenance.Repaired -= OnRepaired;
        }

        // ---- building --------------------------------------------------------------------------

        /// <summary>
        /// The HEPSİNİ ONAR button and the canvas it lives on.
        ///
        /// Hand-rolled rather than taken from <see cref="UiBuild.Canvas"/> because it needs the HUD's
        /// design sheet, not the world layer's: see the class summary. The safe-area child is the same
        /// one every authored screen sits inside, so the button cannot end up under a notch or a
        /// gesture bar on the corner it is pinned to.
        /// </summary>
        private void BuildPill()
        {
            if (!showRepairAll) return;

            var go = new GameObject("HepsiniOnarKanvas", typeof(Canvas), typeof(CanvasScaler),
                                    typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = pillSortingOrder;
            var sc = go.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1080f, 2340f);   // UI_HUD's sheet, so offsets match it
            sc.matchWidthOrHeight = 0.5f;
            UiBuild.EnsureEventSystem(transform);

            var safe = new GameObject("GuvenliAlan", typeof(RectTransform), typeof(SafeArea));
            safe.transform.SetParent(go.transform, false);
            _hudRect = UiBuild.Anchor((RectTransform)safe.transform, Vector2.zero, Vector2.one);

            Button button = UiBuild.Btn(_hudRect, "HepsiniOnar", Loc.T("onar.hepsi"), UiSkin.ButtonGreen,
                                        new Color(0.20f, 0.45f, 0.30f, 0.94f), pillLabelSize,
                                        () => Repair(WholeIsland));
            _pillRect = (RectTransform)button.transform;
            LayOutPill();

            _pillImage = button.GetComponent<Image>();
            _pillLabel = button.GetComponentInChildren<Text>();

            _pillRoot = button.gameObject;
            _pillRoot.SetActive(false);
        }

        /// <summary>
        /// Where the HEPSİNİ ONAR button sits. Re-applied on every scan rather than set once, so the
        /// anchor and the offset can be dragged into place in the Inspector with the game RUNNING —
        /// which is the only way to see whether it has landed on a HUD button.
        /// </summary>
        private void LayOutPill()
        {
            _pillRect.anchorMin = _pillRect.anchorMax = pillAnchor;
            _pillRect.pivot = pillAnchor;
            _pillRect.sizeDelta = pillSize;
            _pillRect.anchoredPosition = pillOffset;
        }

        /// <summary>One building's wrench, and the copy of it that fills back in as a repair runs.</summary>
        private GameObject BuildBadge(int station, out RectTransform rect, out Image image,
                                      out Image progress)
        {
            var go = new GameObject("Onar_" + _op.StationName(station),
                                    typeof(RectTransform), typeof(Image), typeof(Button));
            rect = (RectTransform)go.transform;
            rect.SetParent(_worldRect, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(badgeSize, badgeSize);

            image = go.GetComponent<Image>();
            image.sprite = badge;
            image.preserveAspect = true;
            // The wrench is the tap target, so unlike every other floating decoration in this game it
            // has to take raycasts.
            image.raycastTarget = true;

            int captured = station;
            go.GetComponent<Button>().onClick.AddListener(() => Repair(captured));

            // The progress sweep: the SAME sprite laid exactly over the greyed one and revealed
            // clockwise from the top. Drawing the countdown in the badge rather than beside it is what
            // lets the badge be a badge — there is nothing here to read, only something to watch.
            var over = new GameObject("Ilerleme", typeof(RectTransform), typeof(Image));
            RectTransform overRect = UiBuild.Anchor((RectTransform)over.transform, Vector2.zero, Vector2.one);
            overRect.SetParent(rect, false);
            UiBuild.Anchor(overRect, Vector2.zero, Vector2.one);

            progress = over.GetComponent<Image>();
            progress.sprite = badge;
            progress.preserveAspect = true;
            progress.raycastTarget = false;      // the wrench underneath owns the tap
            progress.type = Image.Type.Filled;
            progress.fillMethod = Image.FillMethod.Radial360;
            progress.fillOrigin = (int)Image.Origin360.Top;
            progress.fillClockwise = true;
            progress.fillAmount = 0f;
            over.SetActive(false);

            go.SetActive(false);
            return go;
        }

        // ---- binding ---------------------------------------------------------------------------

        /// <summary>
        /// Travelling to another island enables a different <see cref="CoalOperation"/> and disables
        /// this one, so the binding is re-checked on a slow timer rather than taken once — the same
        /// reason <see cref="UpgradeReadyMarkers"/> does it. A new operation means new buildings, so
        /// the badges are rebuilt.
        /// </summary>
        private void Rebind()
        {
            if (_cam == null) _cam = Camera.main;
            if (_wallet == null) _wallet = ServiceLocator.Get<WalletService>();
            if (_audio == null) _audio = ServiceLocator.Get<AudioService>();
            if (_haptic == null) _haptic = ServiceLocator.Get<HapticService>();

            if (_maintenance == null)
            {
                _maintenance = ServiceLocator.Get<MaintenanceService>();
                if (_maintenance != null) { _maintenance.Repaired += OnRepaired; _subscribed = true; }
            }

            if (_op != null && _op.enabled) return;

            CoalOperation live = null;
            var all = FindObjectsByType<CoalOperation>(FindObjectsInactive.Exclude);
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
            _roots = new GameObject[total];
            _rects = new RectTransform[total];
            _images = new Image[total];
            _progress = new Image[total];
            _stations = new int[total];
            _shown = new bool[total];
            _shownAt = new float[total];
            _count = 0;

            for (int s = 0; s < total; s++)
            {
                if (!_op.StationHasBody(s)) continue;      // fleets have nothing to hang a wrench over
                _stations[_count] = s;
                _roots[_count] = BuildBadge(s, out _rects[_count], out _images[_count],
                                            out _progress[_count]);
                _count++;
            }
        }

        // ---- repairing -------------------------------------------------------------------------

        /// <summary>
        /// The tap. <paramref name="station"/> below zero takes on everything worn at once.
        ///
        /// The price is checked here before the service is asked, so a player who cannot pay hears the
        /// refusal instead of watching a badge do nothing. It is already drawn grey by then — this is
        /// the second half of the same answer, not the only one.
        /// </summary>
        private void Repair(int station)
        {
            if (_op == null || _maintenance == null || !_maintenance.Enabled) return;

            string island = _op.IslandKey;
            double rate = _op.CashPerMinute;
            double cost = station < 0
                ? _maintenance.RepairCostAll(island, rate)
                : _maintenance.RepairCost(island, station, rate);

            bool refused = cost > 0d && (_wallet == null || !_wallet.CanAfford(new BigDouble(cost)));
            if (refused || !_maintenance.TryRepair(island, station, rate))
            {
                if (_audio != null) _audio.Play(SoundId.Denied);
                return;
            }

            if (_audio != null) _audio.Play(SoundId.Tap);
            if (_haptic != null) _haptic.Light();
            Scan();          // the badge has to answer the tap NOW, not at the next quarter-second
        }

        /// <summary>A crew finishing is worth a noise. Only for the island the player is stood on.</summary>
        private void OnRepaired(string island, int station)
        {
            if (_op == null || island != _op.IslandKey) return;
            if (_audio != null) _audio.Play(SoundId.Upgrade);
            if (_haptic != null) _haptic.Medium();
        }

        // ---- drawing ---------------------------------------------------------------------------

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            _rebindIn -= dt;
            if (_rebindIn <= 0f) { _rebindIn = 1f; Rebind(); }

            _scanIn -= dt;
            if (_scanIn <= 0f) { _scanIn = scanSeconds; Scan(); }

            Place();
        }

        /// <summary>
        /// Whether a station is worn enough to be worth a wrench — the same line at which
        /// <see cref="DistrictWear"/> puts the first grime on it. See the class summary.
        /// </summary>
        private bool LooksDirty(string island, int station)
        {
            float condition = _maintenance.StateOfRepair(island, station);
            return Maintenance.Damage(condition, _maintenance.Tuning) >= DistrictWear.VisibleDamage;
        }

        /// <summary>
        /// What each badge should say and whether it should be there at all. Runs a few times a second,
        /// not every frame: the state behind it moves on the scale of a repair, and every branch here
        /// ends in a string.
        /// </summary>
        private void Scan()
        {
            if (_op == null || _maintenance == null || !_maintenance.Enabled)
            {
                for (int i = 0; i < _count; i++) Show(i, false);
                ShowPill(false);
                return;
            }

            string island = _op.IslandKey;
            double rate = _op.CashPerMinute;
            bool anyIdleAndDirty = false;

            for (int i = 0; i < _count; i++)
            {
                int station = _stations[i];

                if (_maintenance.Repairing(island, station))
                {
                    Show(i, true);
                    Write(i, false, _maintenance.RepairProgress(island, station));
                    continue;
                }

                if (!LooksDirty(island, station)) { Show(i, false); continue; }
                anyIdleAndDirty = true;

                Show(i, true);
                Write(i, Payable(_maintenance.RepairCost(island, station, rate)), 0f);
            }

            if (!showRepairAll || _pillRoot == null) return;
            LayOutPill();

            // Nothing to offer once every dirty building already has a crew on it: the button would
            // charge nothing and start nothing. The fleets have no badge of their own, so they are
            // checked here directly — they are half of why this button exists.
            if (!anyIdleAndDirty) anyIdleAndDirty = FleetNeedsRepair(island);
            if (!anyIdleAndDirty) { ShowPill(false); return; }

            double all = _maintenance.RepairCostAll(island, rate);
            ShowPill(true);
            WritePill(Loc.T("onar.hepsi") + Price(all), Payable(all));
        }

        /// <summary>
        /// Whether a station with no building of its own — train, ore trucks, cargo trucks — is dirty
        /// and unattended. Their districts are the rail and the haul roads, which get visibly filthy
        /// like everything else; they simply have no roof to hang a wrench over.
        /// </summary>
        private bool FleetNeedsRepair(string island)
        {
            for (int s = 0; s < _op.StationCount; s++)
            {
                if (_op.StationHasBody(s)) continue;
                if (!_maintenance.Repairing(island, s) && LooksDirty(island, s)) return true;
            }
            return false;
        }

        private bool Payable(double cost)
            => cost <= 0d || (_wallet != null && _wallet.CanAfford(new BigDouble(cost)));

        /// <summary>
        /// The total on HEPSİNİ ONAR — the one price the player is quoted, now the badges carry no
        /// text. A free repair says so by saying nothing: a price of nothing is not worth a line.
        /// </summary>
        private static string Price(double cost)
            => cost > 0d ? "  " + NumberFormatter.Format(new BigDouble(cost)) : "";

        /// <summary>
        /// Pushes a badge's tint and its progress sweep. Grey covers both states the player cannot act
        /// on — no money for it, or a crew already on it — and the sweep tells the two apart.
        /// </summary>
        private void Write(int i, bool available, float progress)
        {
            _rects[i].sizeDelta = new Vector2(badgeSize, badgeSize);   // scan cadence, not per frame
            _images[i].color = available ? Color.white : Unavailable;

            bool working = progress > 0f;
            if (working) _progress[i].fillAmount = progress;
            if (_progress[i].gameObject.activeSelf != working) _progress[i].gameObject.SetActive(working);
        }

        private void WritePill(string text, bool available)
        {
            if (_pillWritten != text) { _pillLabel.text = text; _pillWritten = text; }

            Sprite art = available ? UiSkin.ButtonGreen : UiSkin.ButtonGrey;
            if (art != null)
            {
                if (_pillImage.sprite != art) _pillImage.sprite = art;
                _pillImage.color = Color.white;      // the kit art is pre-coloured; tinting muddies it
                return;
            }
            _pillImage.color = available ? new Color(0.20f, 0.45f, 0.30f, 0.94f) : Unavailable;
        }

        private void Show(int i, bool on)
        {
            if (_shown[i] == on) return;
            _shown[i] = on;
            if (on) _shownAt[i] = Time.unscaledTime;
            // Left for Place to switch ON — a badge whose building has not resolved yet would
            // otherwise appear in the middle of the screen for a frame.
            if (!on) _roots[i].SetActive(false);
        }

        private void ShowPill(bool on)
        {
            if (_pillRoot == null || _pillShown == on) return;
            _pillShown = on;
            if (on) _pillShownAt = Time.unscaledTime;
            _pillRoot.SetActive(on);
        }

        /// <summary>
        /// Follows the buildings. Every frame, unlike the scan: this is the camera's business, and a
        /// badge that lagged a quarter of a second behind a pan would swim over the roof it belongs to.
        /// </summary>
        private void Place()
        {
            float now = Time.unscaledTime;

            if (_pillShown && _pillRect != null)
                _pillRect.localScale = Life(now, _pillShownAt, 0f);

            if (_op == null || _cam == null || _count == 0) return;

            for (int i = 0; i < _count; i++)
            {
                if (!_shown[i]) continue;

                Vector3 world;
                if (!_op.StationAnchor(_stations[i], out world)) { _roots[i].SetActive(false); continue; }

                world.y += worldLift;
                Vector3 screen = _cam.WorldToScreenPoint(world);
                if (screen.z <= 0f) { _roots[i].SetActive(false); continue; }   // behind the camera

                Vector2 local;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _worldRect, new Vector2(screen.x, screen.y), null, out local);

                _rects[i].anchoredPosition = local + badgeOffset;
                _rects[i].localScale = Life(now, _shownAt[i], i * 0.7f);
                if (!_roots[i].activeSelf) _roots[i].SetActive(true);
            }
        }

        /// <summary>
        /// The pop-in and the breath, as one scale. Both are lifted from
        /// <see cref="UpgradeReadyMarkers"/> so a wrench and the upgrade badge under it move the same
        /// way; the phase offset keeps five of them from pumping in lockstep.
        /// </summary>
        private Vector3 Life(float now, float shownAt, float phase)
        {
            float breath = 1f;
            if (pulseAmount > 0f && pulseSeconds > 0f)
                breath = 1f + Mathf.Sin((now / pulseSeconds + phase) * Mathf.PI * 2f) * pulseAmount;

            float pop = 1f;
            float age = now - shownAt;
            if (age < popSeconds && popSeconds > 0f)
            {
                float u = age / popSeconds - 1f;
                pop = u * u * ((PopOvershoot + 1f) * u + PopOvershoot) + 1f;
            }

            float scale = breath * pop;
            return new Vector3(scale, scale, 1f);
        }
    }
}
