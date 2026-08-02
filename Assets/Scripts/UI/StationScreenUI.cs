using System;
using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Gameplay;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The full-screen upgrade screen for one building — the mine, the depot, the refinery, the market.
    ///
    /// The four buildings the player actually watches used to be bought the same way as a train wagon:
    /// a row in one long list, behind a button in the corner. That made the map something to look at
    /// rather than something to touch, and it hid the one thing the money is really buying — the
    /// building growing. So those four came out of <see cref="UpgradePanelUI"/> entirely and got this
    /// instead: tap the chip floating over a building, and the building itself fills the top of the
    /// screen on a slowly swaying turntable while its upgrades slide up from the bottom like a keyboard.
    /// Everything else — the fleets, the power plant, the one-time expansions — stays in the old panel,
    /// which is still the complete list of what an island can become.
    ///
    /// Two moments are animated, and they are deliberately different sizes. A level is a punch: the
    /// model squashes, a ring goes out, the bar ticks up, half a second and you can buy the next one.
    /// A phase is a rebuild: the tray drops out of the way, the old building sinks into the ground, and
    /// the new one rises through the flash it left behind. That one is allowed to take its time, because
    /// it happens twice per building in the life of an island.
    ///
    /// The model is not art in a sprite — it is a clone of the island's own district geometry, shot in
    /// <see cref="StationPreviewStage"/>. What the player is looking at is what they are about to walk
    /// back out to.
    /// </summary>
    public sealed class StationScreenUI : MonoBehaviour
    {
        /// <summary>One building this screen owns. Its index is a <see cref="CoalOperation"/> station.</summary>
        [Serializable]
        public sealed class StationEntry
        {
            [Tooltip("CoalOperation istasyon indeksi — 0 maden, 2 depo, 4 izabe, 6 pazar.")]
            public int station;
            [Tooltip("Başlıkta yazacak ad. Boş bırakılırsa istasyonun kendi adı kullanılır.")]
            public string title = "";
            public Sprite icon;
        }

        [Header("Ekran (UI_IstasyonEkrani prefabında bağlı)")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Image dim;
        [SerializeField] private Button closeButton;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private Image titleIcon;

        [Header("Model sahnesi")]
        [SerializeField] private StationPreviewStage stage;
        [SerializeField] private RawImage modelView;
        [SerializeField] private RectTransform stageFrame;

        [Header("Faz göstergesi")]
        [SerializeField] private TMP_Text phaseText;
        [SerializeField] private Image phaseFill;
        [SerializeField] private TMP_Text phaseHint;
        [SerializeField] private Image[] phasePips;
        [SerializeField] private Sprite pipFull;
        [SerializeField] private Sprite pipEmpty;

        [Header("Tepsi")]
        [SerializeField] private RectTransform sheet;
        [SerializeField] private RectTransform sheetContent;
        [Tooltip("Pasif şablon: yükseltme kartı (İkon/Ad/Seviye/BtnFiyat/Rozet).")]
        [SerializeField] private GameObject cardTemplate;

        [Header("Durum görselleri")]
        [SerializeField] private Sprite priceGreen;
        [SerializeField] private Sprite priceGrey;
        [SerializeField] private Sprite badgeMax;

        [Header("Efekt")]
        [Tooltip("Satın alımda modelin tabanından yayılan halka.")]
        [SerializeField] private Image halo;
        [Tooltip("Faz değişiminde sahneyi basan beyazlık.")]
        [SerializeField] private Image flash;
        [Tooltip("Yeni bina yükselirken üstünden geçen parlama.")]
        [SerializeField] private RectTransform shine;
        [SerializeField] private RectTransform phaseBanner;
        [SerializeField] private TMP_Text phaseBannerText;

        [Header("İstasyonlar")]
        [SerializeField] private List<StationEntry> stations = new List<StationEntry>();
        [SerializeField] private float refreshInterval = 0.25f;

        private sealed class Row
        {
            public int axis;
            public TMP_Text name, level, price;
            public Button buyBtn;
            public Image buyImg, badge;
            public GameObject buyGO, badgeGO;
        }

        private readonly List<Row> _rows = new List<Row>();
        private WalletService _wallet;
        private CoalOperation _op;
        private Transform _model;
        private Vector2 _sheetHome;
        private float _dimAlpha = 0.8f;
        private float _timer;
        private int _station = -1;
        private int _builtFor = -1;
        private bool _busy;

        private void Awake()
        {
            if (sheet != null) _sheetHome = sheet.anchoredPosition;
            if (dim != null) _dimAlpha = dim.color.a;
            if (cardTemplate != null) cardTemplate.SetActive(false);
            HideFx();
        }

        private void Start()
        {
            _wallet = ServiceLocator.Get<WalletService>();
            BindEnabledOp();
            if (closeButton != null) closeButton.onClick.AddListener(Hide);
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void Update()
        {
            if (_wallet == null) _wallet = ServiceLocator.Get<WalletService>();
            if (_op == null || !_op.enabled) BindEnabledOp();
            if (panelRoot == null || !panelRoot.activeSelf) return;
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = refreshInterval;
            Refresh();
        }

        /// <summary>Whether this screen owns a station — what the map chips and the old panel ask.</summary>
        public bool Handles(int station)
        {
            for (int i = 0; i < stations.Count; i++)
                if (stations[i] != null && stations[i].station == station) return true;
            return false;
        }

        /// <summary>Opens the screen on one building. Called by that building's chip out on the map.</summary>
        public void Open(int station)
        {
            if (!Handles(station)) return;
            if (_op == null) BindEnabledOp();
            if (_op == null || panelRoot == null) return;

            StopAllCoroutines();
            _busy = false;
            _station = station;
            panelRoot.SetActive(true);
            BuildCards();
            MountModel();
            Refresh();
            StartCoroutine(OpenAnim());
        }

        public void Hide()
        {
            if (_busy) return;
            StopAllCoroutines();
            HideFx();
            if (stage != null) { stage.Live = false; stage.Clear(); }
            _model = null;
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void OnDisable()
        {
            if (stage != null) stage.Live = false;
        }

        /// <summary>Retarget at another island's operation — the catalog is identical everywhere.</summary>
        public void SetOperation(CoalOperation op)
        {
            if (op == null || op == _op) return;
            _op = op;
            _builtFor = -1;                       // axis names are shared, but the model is not
            if (panelRoot != null && panelRoot.activeSelf) Hide();
        }

        private void BindEnabledOp()
        {
            var ops = FindObjectsByType<CoalOperation>(FindObjectsSortMode.None);
            for (int i = 0; i < ops.Length; i++)
                if (ops[i].enabled) { SetOperation(ops[i]); return; }
        }

        // ---------- the building ----------
        private Kayseri.Island.IslandPhaseController Phases
        {
            get { return _op != null ? _op.Phases : null; }
        }

        private int CurrentPhase()
        {
            var ph = Phases;
            return ph != null ? ph.PhaseForStation(_op.StationName(_station)) : 1;
        }

        /// <summary>
        /// Puts one phase's building on the turntable. The geometry is cloned from the prefab, because
        /// the island's own copy is welded into a static batch and cannot be moved; its size, though,
        /// has to be read off that scene copy, since the batching is also the only thing that ever gave
        /// those meshes correct bounds.
        /// </summary>
        private Transform MountPhase(int phase)
        {
            var ph = Phases;
            if (stage == null || ph == null) return null;
            string station = _op.StationName(_station);
            Transform template = ph.DistrictModel(station, phase);
            if (template == null) return null;
            return stage.Mount(template, LocalBounds(ph.DistrictArt(station, phase)));
        }

        private static Bounds LocalBounds(Transform district)
        {
            if (district == null) return new Bounds(Vector3.zero, Vector3.one * 40f);
            var rs = district.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(Vector3.zero, Vector3.one * 40f);
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            b.center -= district.position;      // districts sit unrotated and unscaled on the island root
            return b;
        }

        private void MountModel()
        {
            if (stage == null) return;
            stage.Clear();
            _model = null;
            stage.Zoom = 1f;
            stage.Live = true;
            if (modelView != null) modelView.texture = stage.Texture;
            _model = MountPhase(CurrentPhase());
        }

        // ---------- construction ----------
        private void BuildCards()
        {
            if (_builtFor == _station) return;
            if (sheetContent == null || cardTemplate == null || _op == null) return;
            _builtFor = _station;

            for (int i = sheetContent.childCount - 1; i >= 0; i--)
            {
                GameObject child = sheetContent.GetChild(i).gameObject;
                if (child != cardTemplate) Destroy(child);
            }
            _rows.Clear();

            for (int a = 0; a < _op.AxisCount(_station); a++)
            {
                GameObject go = Instantiate(cardTemplate, sheetContent);
                go.name = "Kart_" + _op.AxisName(_station, a);
                go.SetActive(true);

                var row = new Row { axis = a };
                Transform t = go.transform.Find("Ikon");
                if (t != null)
                {
                    Image img = t.GetComponent<Image>();
                    if (img != null) img.sprite = IconFor(_station);
                }
                t = go.transform.Find("Ad");     if (t != null) row.name = t.GetComponent<TMP_Text>();
                t = go.transform.Find("Seviye"); if (t != null) row.level = t.GetComponent<TMP_Text>();
                t = go.transform.Find("BtnFiyat");
                if (t != null)
                {
                    row.buyGO = t.gameObject;
                    row.buyBtn = t.GetComponent<Button>();
                    row.buyImg = t.GetComponent<Image>();
                    Transform ft = t.Find("Fiyat");
                    if (ft != null) row.price = ft.GetComponent<TMP_Text>();
                }
                t = go.transform.Find("Rozet");
                if (t != null) { row.badgeGO = t.gameObject; row.badge = t.GetComponent<Image>(); }
                t = go.transform.Find("Kilit");
                if (t != null) t.gameObject.SetActive(false);   // nothing on these four is ever locked

                if (row.name != null) row.name.text = _op.AxisName(_station, a);
                if (row.buyBtn != null)
                {
                    Row captured = row;
                    row.buyBtn.onClick.AddListener(() => Buy(captured));
                }
                _rows.Add(row);
            }
        }

        private Sprite IconFor(int station)
        {
            for (int i = 0; i < stations.Count; i++)
                if (stations[i] != null && stations[i].station == station) return stations[i].icon;
            return null;
        }

        private string TitleFor(int station)
        {
            for (int i = 0; i < stations.Count; i++)
                if (stations[i] != null && stations[i].station == station && !string.IsNullOrEmpty(stations[i].title))
                    return stations[i].title;
            return _op != null ? _op.StationName(station) : "";
        }

        // ---------- refresh ----------
        private void Refresh()
        {
            if (_op == null || _station < 0) return;

            if (titleText != null) titleText.text = TitleFor(_station);
            if (titleIcon != null)
            {
                titleIcon.sprite = IconFor(_station);
                titleIcon.enabled = titleIcon.sprite != null;
            }

            int phase = CurrentPhase();
            int cap = _op.StationLevelCap(_station);
            int lv = _op.StationLevelTotal(_station);
            float third = cap / 3f;
            float lo = (phase - 1) * third;
            float hi = phase < 3 ? phase * third : cap;
            float p = hi > lo ? Mathf.Clamp01((lv - lo) / (hi - lo)) : 1f;

            if (phaseText != null) phaseText.text = "FAZ " + phase + " / 3";
            if (phaseFill != null) phaseFill.fillAmount = p;
            if (phaseHint != null)
            {
                int need = Mathf.Max(0, Mathf.CeilToInt(hi) - lv);
                phaseHint.text = phase < 3
                    ? "SONRAKİ FAZA " + need + " SEVİYE"
                    : (need > 0 ? "TAM DOLUYA " + need + " SEVİYE" : "TAMAMLANDI");
            }
            if (phasePips != null)
                for (int i = 0; i < phasePips.Length; i++)
                {
                    if (phasePips[i] == null) continue;
                    Sprite s = i < phase ? pipFull : pipEmpty;
                    if (s != null) phasePips[i].sprite = s;
                }

            for (int i = 0; i < _rows.Count; i++) RefreshRow(_rows[i]);
        }

        private void RefreshRow(Row r)
        {
            int lv = _op.AxisLevel(_station, r.axis);
            if (r.level != null) r.level.text = "Sv " + lv;

            if (_op.AxisMaxed(_station, r.axis))
            {
                if (r.buyGO != null && r.buyGO.activeSelf) r.buyGO.SetActive(false);
                if (r.badgeGO != null && !r.badgeGO.activeSelf) r.badgeGO.SetActive(true);
                if (r.badge != null && badgeMax != null) r.badge.sprite = badgeMax;
                return;
            }

            if (r.buyGO != null && !r.buyGO.activeSelf) r.buyGO.SetActive(true);
            if (r.badgeGO != null && r.badgeGO.activeSelf) r.badgeGO.SetActive(false);

            BigDouble cost = _op.AxisCost(_station, r.axis);
            bool afford = _wallet != null && _wallet.CanAfford(cost);
            if (r.price != null) r.price.text = "$" + NumberFormatter.Format(cost);
            if (r.buyImg != null) r.buyImg.sprite = afford ? priceGreen : priceGrey;
            if (r.buyBtn != null) r.buyBtn.interactable = afford && !_busy;
        }

        // ---------- buying ----------
        private void Buy(Row row)
        {
            if (_busy || _op == null) return;
            int before = CurrentPhase();
            if (!_op.TryUpgrade(_station, row.axis)) return;
            Refresh();

            int after = CurrentPhase();
            if (after != before) StartCoroutine(PhaseSequence(after));
            else StartCoroutine(LevelPunch(row));
        }

        // ---------- animation ----------
        private IEnumerator OpenAnim()
        {
            float hidden = HiddenSheetY();
            if (sheet != null) sheet.anchoredPosition = new Vector2(_sheetHome.x, hidden);
            if (dim != null) SetAlpha(dim, 0f);
            if (stageFrame != null) stageFrame.localScale = Vector3.one * 0.94f;

            float t = 0f;
            while (t < OpenSeconds)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / OpenSeconds);
                float ease = 1f - Mathf.Pow(1f - k, 3f);        // the phone-keyboard curve: fast out, soft stop
                if (dim != null) SetAlpha(dim, _dimAlpha * Mathf.Clamp01(k * 2f));
                if (sheet != null) sheet.anchoredPosition = new Vector2(_sheetHome.x, Mathf.Lerp(hidden, _sheetHome.y, ease));
                if (stageFrame != null) stageFrame.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, ease);
                yield return null;
            }
            if (sheet != null) sheet.anchoredPosition = _sheetHome;
            if (dim != null) SetAlpha(dim, _dimAlpha);
            if (stageFrame != null) stageFrame.localScale = Vector3.one;
        }

        /// <summary>
        /// What one level looks like. Short on purpose — a player buying six in a row must never wait
        /// on it, so nothing here locks the buttons and the whole thing is over in half a second.
        /// </summary>
        private IEnumerator LevelPunch(Row row)
        {
            RectTransform btn = row.buyGO != null ? (RectTransform)row.buyGO.transform : null;
            RectTransform lvl = row.level != null ? row.level.rectTransform : null;
            if (halo != null) { halo.gameObject.SetActive(true); halo.rectTransform.localScale = Vector3.one * 0.3f; }

            float t = 0f;
            while (t < PunchSeconds)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / PunchSeconds);
                float pop = Mathf.Sin(k * Mathf.PI);            // 0 → 1 → 0

                // A building squashes wider as it settles rather than swelling evenly — the same
                // shape a stamped foundation makes, which is what a bought level is supposed to be.
                if (_model != null)
                    _model.localScale = new Vector3(1f + pop * 0.055f, 1f + pop * 0.10f, 1f + pop * 0.055f);
                if (btn != null) btn.localScale = Vector3.one * (1f + pop * 0.13f);
                if (lvl != null) lvl.localScale = Vector3.one * (1f + pop * 0.18f);
                if (halo != null)
                {
                    halo.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.3f, 1.55f, k);
                    SetAlpha(halo, (1f - k) * 0.8f);
                }
                yield return null;
            }

            if (_model != null) _model.localScale = Vector3.one;
            if (btn != null) btn.localScale = Vector3.one;
            if (lvl != null) lvl.localScale = Vector3.one;
            if (halo != null) halo.gameObject.SetActive(false);
        }

        /// <summary>
        /// What a phase looks like. The tray leaves, the old building goes into the ground, and the new
        /// one comes up through the flash — which is only possible because the stage can hold both at
        /// once. Buttons stay dead for the whole sequence: this is the payoff for two thirds of a
        /// station's upgrade track, and buying through it would step on it.
        /// </summary>
        private IEnumerator PhaseSequence(int phase)
        {
            _busy = true;
            SetButtons(false);
            if (closeButton != null) closeButton.interactable = false;

            // 1 · clear the stage
            yield return SlideSheet(_sheetHome.y, HiddenSheetY(), SheetOutSeconds);

            // 2 · the old building sinks, and the camera leans in after it
            Transform old = _model;
            float drop = stage != null ? stage.FocusRadius * 1.4f : 40f;
            float t = 0f;
            while (t < SinkSeconds)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / SinkSeconds);
                float ease = k * k;                              // slow to let go, then gone
                if (old != null)
                {
                    old.localPosition = Vector3.down * (drop * ease);
                    old.localScale = Vector3.one * Mathf.Lerp(1f, 0.82f, ease);
                }
                if (stage != null) stage.Zoom = Mathf.Lerp(1f, 1.14f, ease);
                yield return null;
            }
            if (old != null) Destroy(old.gameObject);
            _model = null;

            // 3 · the flash the new one arrives through
            _model = MountPhase(phase);
            if (_model != null)
            {
                _model.localPosition = Vector3.down * drop;
                _model.localScale = Vector3.one * 0.6f;
            }
            if (flash != null) flash.gameObject.SetActive(true);
            t = 0f;
            while (t < FlashSeconds)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / FlashSeconds);
                if (flash != null) SetAlpha(flash, Mathf.Sin(k * Mathf.PI) * 0.9f);
                yield return null;
            }
            if (flash != null) { SetAlpha(flash, 0f); flash.gameObject.SetActive(false); }

            // 4 · it rises, overshoots, settles — with a sweep of light across it
            if (shine != null) shine.gameObject.SetActive(true);
            float shineFrom = -StageHalfWidth() - 220f;
            float shineTo = StageHalfWidth() + 220f;
            t = 0f;
            while (t < RiseSeconds)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / RiseSeconds);
                float ease = 1f - Mathf.Pow(1f - k, 3f);
                float over = 1f + Mathf.Sin(k * Mathf.PI) * 0.07f;
                if (_model != null)
                {
                    _model.localPosition = Vector3.down * Mathf.Lerp(drop, 0f, ease);
                    _model.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, ease) * over;
                }
                if (stage != null) stage.Zoom = Mathf.Lerp(1.14f, 1f, ease);
                if (shine != null) shine.anchoredPosition = new Vector2(Mathf.Lerp(shineFrom, shineTo, ease), 0f);
                yield return null;
            }
            if (_model != null) { _model.localPosition = Vector3.zero; _model.localScale = Vector3.one; }
            if (stage != null) stage.Zoom = 1f;
            if (shine != null) shine.gameObject.SetActive(false);

            // 5 · name what just happened
            Refresh();
            if (phaseBanner != null)
            {
                if (phaseBannerText != null) phaseBannerText.text = "FAZ " + phase;
                phaseBanner.gameObject.SetActive(true);
                float from = StageHalfWidth() * 2f + 400f;
                t = 0f;
                while (t < BannerInSeconds)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(t / BannerInSeconds);
                    float ease = 1f - Mathf.Pow(1f - k, 4f);
                    phaseBanner.anchoredPosition = new Vector2(Mathf.Lerp(from, 0f, ease), phaseBanner.anchoredPosition.y);
                    phaseBanner.localScale = Vector3.one * (1f + Mathf.Sin(k * Mathf.PI) * 0.10f);
                    yield return null;
                }
                phaseBanner.anchoredPosition = new Vector2(0f, phaseBanner.anchoredPosition.y);
                phaseBanner.localScale = Vector3.one;

                t = 0f;
                while (t < BannerHoldSeconds) { t += Time.unscaledDeltaTime; yield return null; }

                var group = phaseBanner.GetComponent<CanvasGroup>();
                t = 0f;
                while (t < BannerOutSeconds)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(t / BannerOutSeconds);
                    if (group != null) group.alpha = 1f - k;
                    phaseBanner.localScale = Vector3.one * (1f - k * 0.12f);
                    yield return null;
                }
                if (group != null) group.alpha = 1f;
                phaseBanner.localScale = Vector3.one;
                phaseBanner.gameObject.SetActive(false);
            }

            // 6 · and back to shopping
            yield return SlideSheet(HiddenSheetY(), _sheetHome.y, SheetInSeconds);
            _busy = false;
            if (closeButton != null) closeButton.interactable = true;
            Refresh();
        }

        private IEnumerator SlideSheet(float from, float to, float seconds)
        {
            if (sheet == null) yield break;
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / seconds);
                float ease = 1f - Mathf.Pow(1f - k, 3f);
                sheet.anchoredPosition = new Vector2(_sheetHome.x, Mathf.Lerp(from, to, ease));
                yield return null;
            }
            sheet.anchoredPosition = new Vector2(_sheetHome.x, to);
        }

        private float HiddenSheetY()
        {
            float h = sheet != null ? sheet.rect.height : 700f;
            return _sheetHome.y - h - 60f;
        }

        private float StageHalfWidth()
        {
            return stageFrame != null ? stageFrame.rect.width * 0.5f : 480f;
        }

        private void SetButtons(bool on)
        {
            for (int i = 0; i < _rows.Count; i++)
                if (_rows[i].buyBtn != null) _rows[i].buyBtn.interactable = on;
        }

        private void HideFx()
        {
            if (halo != null) halo.gameObject.SetActive(false);
            if (flash != null) flash.gameObject.SetActive(false);
            if (shine != null) shine.gameObject.SetActive(false);
            if (phaseBanner != null) phaseBanner.gameObject.SetActive(false);
        }

        private static void SetAlpha(Graphic g, float a)
        {
            Color c = g.color;
            if (Mathf.Abs(c.a - a) < 0.004f) return;
            c.a = a;
            g.color = c;
        }

        private const float OpenSeconds = 0.30f;
        private const float PunchSeconds = 0.45f;
        private const float SheetOutSeconds = 0.22f;
        private const float SinkSeconds = 0.45f;
        private const float FlashSeconds = 0.26f;
        private const float RiseSeconds = 0.70f;
        private const float BannerInSeconds = 0.45f;
        private const float BannerHoldSeconds = 0.55f;
        private const float BannerOutSeconds = 0.30f;
        private const float SheetInSeconds = 0.28f;
    }
}
