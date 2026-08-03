using System;
using System.Collections.Generic;
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
    /// The expansions panel (panel_yukseltme + kart_yukseltme from the Figma set): a modal sheet with a
    /// scrollable list of the one-time ghost-building unlocks, opened from the last tile on
    /// <see cref="StationScreenUI"/>'s strip. It used to hold every station's levels too; those moved to
    /// that screen, where the thing being bought is on a turntable while you buy it. Editor-authored: the
    /// hierarchy lives in the UI_YukseltmePanel prefab, rows are cloned from the inactive template
    /// cards inside the scroll content, and every reference below is wired in the Inspector — so
    /// panel size, card layout, icons and fonts are all tunable without touching code.
    ///
    /// Buying goes through <see cref="CoalOperation.TryUpgrade"/> / <see cref="CoalOperation.TryUnlock"/>,
    /// which spend via <see cref="WalletService"/>. The upgrade catalog is identical on every island,
    /// so island travel just rebinds the rows (<see cref="SetOperation"/>).
    /// </summary>
    public sealed class UpgradePanelUI : MonoBehaviour
    {
        /// <summary>Maps a station/unlock display name onto an icon — first match wins, edit freely.</summary>
        [Serializable]
        public sealed class IconRule
        {
            [Tooltip("Ad bu metni içeriyorsa (büyük/küçük harf duyarsız) bu ikon kullanılır.")]
            public string nameContains = "";
            public Sprite icon;
        }

        [Header("Panel (UI_YukseltmePanel prefabında bağlı)")]
        [SerializeField] private GameObject panelRoot;
        [Tooltip("Şeridin altındaki ada adı. Ekranın kendi başlığı şeride sabit yazılıdır — diğer paneller gibi.")]
        [SerializeField] private TMP_Text islandNameText;
        [SerializeField] private Button closeButton;
        [SerializeField] private RectTransform content;
        [Tooltip("Pasif şablon: istasyon başlığı satırı (İkon + Ad).")]
        [SerializeField] private GameObject headerTemplate;
        [Tooltip("Pasif şablon: yükseltme kartı (İkon/Ad/Seviye/BtnFiyat/Rozet/Kilit).")]
        [SerializeField] private GameObject cardTemplate;

        [Header("Durum görselleri")]
        [SerializeField] private Sprite priceGreen;    // btn_fiyat_yesil — alınabilir
        [SerializeField] private Sprite priceGrey;     // btn_fiyat_gri — para yetmiyor
        [SerializeField] private Sprite badgeMax;      // rozet_max — eksen tavanda
        [SerializeField] private Sprite badgeBuilt;    // rozet_tamam — tek seferlik satın alım bitti

        [Header("İstasyon ikonları")]
        [SerializeField] private List<IconRule> iconRules = new List<IconRule>();
        [SerializeField] private Sprite fallbackIcon;

        [SerializeField] private float refreshInterval = 0.25f;

        private WalletService _wallet;
        private CoalOperation _op;
        private float _timer;
        private bool _built;

        private sealed class Row
        {
            public int station, axis;     // axis row when unlock < 0
            public int unlock = -1;       // unlock row when >= 0
            public string note;           // unlock rows: the effect, shown on the level line
            public TMP_Text name, level, price;
            public Button buyBtn;
            public Image buyImg, badge;
            public GameObject buyGO, badgeGO, lockGO;
        }
        private readonly List<Row> _rows = new List<Row>();
        // One per station, in station order, so a tap on a building's chip can scroll straight to it.
        private readonly List<RectTransform> _headers = new List<RectTransform>();

        private void Start()
        {
            _wallet = ServiceLocator.Get<WalletService>();
            BindEnabledOp();
            if (closeButton != null) closeButton.onClick.AddListener(Hide);
            BuildRows();
            if (panelRoot != null) panelRoot.SetActive(false);
            BuildDevButton();
            UiPanelSound.Attach(panelRoot);   // panel kapatıldıktan SONRA — açılış sesi boot'ta çalmasın
        }

        /// <summary>
        /// Sounds a purchase attempt and passes its result straight through, so a buy handler stays the
        /// one line it was. A refused buy is the more important of the two: it is the only way the player
        /// finds out the button did nothing.
        /// </summary>
        private bool Bought(bool ok)
        {
            if (_audio == null) _audio = ServiceLocator.Get<AudioService>();
            if (_haptic == null) _haptic = ServiceLocator.Get<HapticService>();
            if (_audio != null) _audio.Play(ok ? SoundId.Upgrade : SoundId.Denied);
            if (ok && _haptic != null) _haptic.Medium();
            return ok;
        }

        private AudioService _audio;
        private HapticService _haptic;

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

        /// <summary>Open/close the panel — called by the HUD's pickaxe button.</summary>
        public void Toggle()
        {
            if (panelRoot == null) return;
            bool on = !panelRoot.activeSelf;
            panelRoot.SetActive(on);
            if (on) Refresh();
        }

        public void Hide()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        /// <summary>
        /// Steps out of the way when a district rebuilds. A phase change is the biggest thing
        /// the player's spending produces, and it happens on the map - which is exactly what
        /// this panel is covering at the moment of purchase, so it went unseen.
        /// </summary>
        private void OnDistrictPhaseChanged(string district, int phase)
        {
            Hide();

            // Bu bileşen paneli kapalıyken de çalışır ve fazı dinleyen tek yer burasıdır, bu yüzden
            // faz sesi buradan çalar: yükseltme hangi ekrandan alınırsa alınsın duyulur. Aynı karede
            // birkaç bölge birden değişirse AudioLibrary'deki tekrar kapısı tek sese indirir.
            //
            // Ama açılışta değil. Ada önce faz 1'de kuruluyor, sonra kayıttaki seviyeler uygulanınca
            // bulunduğu faza sıçrıyor; bu da bölge bölge PhaseChanged olarak rapor ediliyor. Oyuncu
            // hiçbir şey yapmadan fanfar duyardı. Ölçüldü: sahne yüklendikten ~1 sn sonra oluyor.
            if (Time.timeSinceLevelLoad < 3f) return;

            if (_audio == null) _audio = ServiceLocator.Get<AudioService>();
            if (_haptic == null) _haptic = ServiceLocator.Get<HapticService>();
            if (_audio != null) _audio.Play(SoundId.PhaseUp);
            if (_haptic != null) _haptic.Heavy();
        }

        private void OnEnable()
        {
            _phases = FindAnyObjectByType<Kayseri.Island.IslandPhaseController>();
            if (_phases != null) _phases.PhaseChanged += OnDistrictPhaseChanged;
            _loc = ServiceLocator.Get<LocalizationService>();
            if (_loc != null) _loc.Changed += Rebuild;
        }

        private void OnDisable()
        {
            if (_phases != null) _phases.PhaseChanged -= OnDistrictPhaseChanged;
            _phases = null;
            if (_loc != null) _loc.Changed -= Rebuild;
        }

        /// <summary>
        /// Throws the list away and builds it again in the new language. Station headers, axis names and
        /// expansion titles are written once at build time and never touched afterwards — rewriting them
        /// in <see cref="Refresh"/> instead would allocate a string per row several times a second, for a
        /// change the player makes about once.
        /// </summary>
        private void Rebuild()
        {
            // Ada adı satırlarla birlikte kurulmuyor — yalnızca SetOperation'da, yani ada değişince
            // bir kez yazılıyor. Burada tazelenmezse panelin gerisi yeni dile geçer, başlık kurulduğu
            // dilde kalır. _built kapısının önünde, çünkü liste hiç kurulmamışken de doğru olmalı.
            if (_op != null && islandNameText != null) islandNameText.text = Loc.Id("ada", _op.IslandKey);

            if (!_built || content == null) return;

            // Destroy sona ertelendiği için önce ayır ve kapat: aksi halde eski satırlar bir kare boyunca
            // yenilerinin yanında durur ve liste ikiye katlanmış görünür.
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                GameObject child = content.GetChild(i).gameObject;
                if (child == headerTemplate || child == cardTemplate) continue;
                child.SetActive(false);
                child.transform.SetParent(null, false);
                Destroy(child);
            }
            _rows.Clear();
            _headers.Clear();
            _built = false;
            BuildRows();
            Refresh();
        }

        private LocalizationService _loc;

        private Kayseri.Island.IslandPhaseController _phases;

        /// <summary>
        /// Opens the panel already scrolled to one station's rows — what a tap on that building's chip
        /// out on the map does.
        ///
        /// Without it the map was something to watch rather than something to touch: every purchase meant
        /// opening one long list and hunting for the building you were already looking at. Landing on the
        /// right rows closes that loop, which is most of what makes a tycoon map feel playable.
        /// </summary>
        public void OpenAtStation(int station)
        {
            if (panelRoot == null) return;
            panelRoot.SetActive(true);
            Refresh();
            ScrollTo(station);
        }

        /// <summary>
        /// Puts a station's header at the top of the view.
        ///
        /// Measured from world corners rather than from anchoredPosition, because the rows are laid out by
        /// a layout group and their pivots and anchors are whatever the template happened to carry — the
        /// corners are the one reading that means the same thing for any of them.
        /// </summary>
        private void ScrollTo(int station)
        {
            if (content == null || station < 0 || station >= _headers.Count) return;
            RectTransform header = _headers[station];
            if (header == null) return;
            var scroll = content.GetComponentInParent<ScrollRect>();
            if (scroll == null || scroll.viewport == null) return;

            // The rows were only just enabled, so their layout is still a frame behind.
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);

            var c = new Vector3[4]; content.GetWorldCorners(c);
            var v = new Vector3[4]; scroll.viewport.GetWorldCorners(v);
            var h = new Vector3[4]; header.GetWorldCorners(h);

            float span = (c[1].y - c[0].y) - (v[1].y - v[0].y);   // scrollable travel, in world units
            if (span <= 0.001f) return;                            // whole list already fits
            float fromTop = c[1].y - h[1].y;
            scroll.verticalNormalizedPosition = Mathf.Clamp01(1f - fromTop / span);
        }

        /// <summary>Retarget every row at another island's operation (world-map travel). The catalog is
        /// identical on every island, so only the binding and the island name change.</summary>
        public void SetOperation(CoalOperation op)
        {
            if (op == null) return;
            _op = op;
            if (islandNameText != null) islandNameText.text = Loc.Id("ada", op.IslandKey);
            if (panelRoot != null && panelRoot.activeSelf) Refresh();
        }

        /// <summary>Several operations live on the controller (one per island) — bind the enabled one.</summary>
        private void BindEnabledOp()
        {
            var ops = FindObjectsByType<CoalOperation>();
            for (int i = 0; i < ops.Length; i++)
                if (ops[i].enabled) { SetOperation(ops[i]); return; }
            if (_op == null && ops.Length > 0) _op = ops[0];   // catalog shape for building rows pre-boot
        }

        /// <summary>
        /// First matching rule wins. Section headers pass <paramref name="useFallback"/> false: a
        /// header with no rule of its own gets no icon at all rather than a stand-in, which is what
        /// "GENİŞLETMELER" wants — it is a group of unlocks, not a station.
        /// </summary>
        private Sprite IconFor(string label, bool useFallback = true)
        {
            for (int i = 0; i < iconRules.Count; i++)
            {
                IconRule r = iconRules[i];
                if (r != null && r.icon != null && !string.IsNullOrEmpty(r.nameContains) &&
                    label.IndexOf(r.nameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                    return r.icon;
            }
            return useFallback ? fallbackIcon : null;
        }

        // ---------- construction ----------
        private void BuildRows()
        {
            if (_built || _op == null || content == null || headerTemplate == null || cardTemplate == null) return;
            _built = true;
            headerTemplate.SetActive(false);
            cardTemplate.SetActive(false);

            // The four buildings moved out to <see cref="StationScreenUI"/>, which shows the model
            // being bought. Listing them here too would give one purchase two homes and undo the
            // point of moving them. A null header keeps the list indexed by station, so a chip that
            // still routes here (the power plant) scrolls to the right place.
            var screen = FindAnyObjectByType<StationScreenUI>(FindObjectsInactive.Include);

            for (int s = 0; s < _op.StationCount; s++)
            {
                if (screen != null && screen.Handles(s)) { _headers.Add(null); continue; }
                // İkon ham id'den seçiliyor (IconFor anahtar kelimeye bakar), başlık çevrilmiş halden.
                string station = _op.StationName(s);
                _headers.Add((RectTransform)AddHeader(Loc.Id("istasyon", station)).transform);
                for (int a = 0; a < _op.AxisCount(s); a++)
                {
                    Row row = AddCard(station, Loc.Id("eksen", _op.AxisName(s, a)));
                    row.station = s; row.axis = a;
                    int cs = s, ca = a;
                    row.buyBtn.onClick.AddListener(() => { if (Bought(_op != null && _op.TryUpgrade(cs, ca))) Refresh(); });
                }
            }

            if (_op.UnlockCount > 0)
            {
                // Şeritte bir başlık yok: ekranın kendi şeridi zaten "GENİŞLETMELER" diyor ve bugün bu
                // listede başka bir bölüm kalmadı. Bir istasyon ekrandan geri alınırsa buraya bir
                // AddHeader satırı geri gelmeli, yoksa iki bölüm arasında ayrım kalmaz.
                for (int u = 0; u < _op.UnlockCount; u++)
                {
                    // The unlock's title and its effect are two separate lines on the card, and the
                    // table keeps them as two separate rows ("kilit.5" / "kilit.5.not") rather than one
                    // string with a bracket in it — a translator should never have to preserve
                    // punctuation for a parser. Keyed by index, which the UnlockXxx constants pin.
                    // The power plant's name carries its island's ore, so it takes a {0}.
                    string title = string.Format(Loc.T("kilit." + u), Loc.Id("cevher", _op.IslandKey));
                    Row row = AddCard(_op.UnlockName(u), title);   // ikon yine ham addan
                    string note = "kilit." + u + ".not";
                    row.note = Loc.T(note) != note ? Loc.T(note) : null;
                    row.unlock = u;
                    int cu = u;
                    row.buyBtn.onClick.AddListener(() => { if (Bought(_op != null && _op.TryUnlock(cu))) Refresh(); });
                }
            }
        }

        /// <summary>Adds a section header and returns it, so callers can keep it for scrolling.</summary>
        private GameObject AddHeader(string label)
        {
            GameObject go = Instantiate(headerTemplate, content);
            go.name = "Baslik_" + label;
            go.SetActive(true);
            Transform iconT = go.transform.Find("Ikon");
            if (iconT != null)
            {
                Image img = iconT.GetComponent<Image>();
                if (img != null)
                {
                    img.sprite = IconFor(label, false);
                    // no sprite means no slot — an empty Image would draw a white square
                    iconT.gameObject.SetActive(img.sprite != null);
                }
            }
            Transform adT = go.transform.Find("Ad");
            if (adT != null)
            {
                TMP_Text t = adT.GetComponent<TMP_Text>();
                if (t != null) t.text = label;
            }
            return go;
        }

        private Row AddCard(string iconLabel, string title)
        {
            GameObject go = Instantiate(cardTemplate, content);
            go.name = "Kart_" + title;
            go.SetActive(true);
            var row = new Row();
            Transform t;
            t = go.transform.Find("Ikon");
            if (t != null) { Image img = t.GetComponent<Image>(); if (img != null) img.sprite = IconFor(iconLabel); }
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
            if (t != null) row.lockGO = t.gameObject;
            if (row.name != null) row.name.text = title;
            _rows.Add(row);
            return row;
        }

        // ---------- refresh ----------
        private void Refresh()
        {
            if (_op == null) return;
            for (int i = 0; i < _rows.Count; i++)
            {
                Row r = _rows[i];
                if (r.unlock >= 0) RefreshUnlock(r);
                else RefreshAxis(r);
            }
        }

        private void RefreshAxis(Row r)
        {
            if (_op.AxisLocked(r.station, r.axis))
            {
                SetState(r, false, false, true);
                if (r.level != null)
                    r.level.text = string.Format(Loc.T("yukseltme.kilitli_ile"),
                        Loc.Id("istasyon", "POWER PLANT"));
                return;
            }
            int lv = _op.AxisLevel(r.station, r.axis);
            if (_op.AxisMaxed(r.station, r.axis))
            {
                SetState(r, false, true, false);
                if (r.badge != null && badgeMax != null) r.badge.sprite = badgeMax;
                if (r.level != null) r.level.text = string.Format(Loc.T("yukseltme.seviye"), lv);
                return;
            }
            BigDouble cost = _op.AxisCost(r.station, r.axis);
            bool afford = _wallet != null && _wallet.CanAfford(cost);
            SetState(r, true, false, false);
            if (r.level != null) r.level.text = string.Format(Loc.T("yukseltme.seviye"), lv);
            if (r.price != null) r.price.text = "$" + NumberFormatter.Format(cost);
            if (r.buyImg != null) r.buyImg.sprite = afford ? priceGreen : priceGrey;
            if (r.buyBtn != null) r.buyBtn.interactable = afford;
        }

        private void RefreshUnlock(Row r)
        {
            if (_op.IsUnlocked(r.unlock))
            {
                SetState(r, false, true, false);
                if (r.badge != null && badgeBuilt != null) r.badge.sprite = badgeBuilt;
                if (r.level != null) r.level.text = Loc.T("yukseltme.insa_edildi");
                return;
            }
            BigDouble cost = _op.UnlockCost(r.unlock);
            bool afford = _wallet != null && _wallet.CanAfford(cost);
            SetState(r, true, false, false);
            if (r.level != null) r.level.text = string.IsNullOrEmpty(r.note) ? Loc.T("yukseltme.tek_seferlik") : r.note;
            if (r.price != null) r.price.text = "$" + NumberFormatter.Format(cost);
            if (r.buyImg != null) r.buyImg.sprite = afford ? priceGreen : priceGrey;
            if (r.buyBtn != null) r.buyBtn.interactable = afford;
        }

        private static void SetState(Row r, bool buy, bool badge, bool locked)
        {
            if (r.buyGO != null && r.buyGO.activeSelf != buy) r.buyGO.SetActive(buy);
            if (r.badgeGO != null && r.badgeGO.activeSelf != badge) r.badgeGO.SetActive(badge);
            if (r.lockGO != null && r.lockGO.activeSelf != locked) r.lockGO.SetActive(locked);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // TEST MODE (dev only, carried over from CoalHud): free purchases everywhere + saving
        // suspended for the session, so every island/upgrade can be tried without touching the real
        // save. Code-built on purpose — it never ships, so it has no place in the prefab.
        private Button _testBtn;
        private TMP_Text _testLabel;

        private void BuildDevButton()
        {
            var canvas = GetComponentInChildren<Canvas>(true);
            if (canvas == null) return;
            var go = new GameObject("TestModu", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(16f, -460f); rt.sizeDelta = new Vector2(440f, 70f);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.24f, 0.27f, 0.32f, 0.92f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var tgo = new GameObject("Etiket", typeof(RectTransform));
            tgo.transform.SetParent(go.transform, false);
            var trt = (RectTransform)tgo.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            _testLabel = tgo.AddComponent<TextMeshProUGUI>();
            _testLabel.fontSize = 30; _testLabel.alignment = TextAlignmentOptions.Center;
            _testLabel.text = "TEST MODU: KAPALI"; _testLabel.raycastTarget = false;
            _testBtn = btn;
            btn.onClick.AddListener(ToggleTestMode);
        }

        private void ToggleTestMode()
        {
            if (_wallet == null) return;
            bool on = !_wallet.FreePurchases;
            _wallet.FreePurchases = on;
            if (on)
            {
                var save = ServiceLocator.Get<SaveService>();
                if (save != null) save.Suspended = true;   // sticky: once test mode has run, this session never saves
            }
            _testLabel.text = on ? "TEST AÇIK — KAYIT YOK" : "TEST MODU: KAPALI";
            _testBtn.GetComponent<Image>().color = on ? new Color(0.75f, 0.20f, 0.20f, 0.92f) : new Color(0.24f, 0.27f, 0.32f, 0.92f);
            Refresh();
        }
#else
        private void BuildDevButton() { }
#endif
    }
}
