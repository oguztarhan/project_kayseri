using System;
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
    /// The skinned upgrade panel (panel_yukseltme + kart_yukseltme from the Figma set): a modal
    /// sheet with a scrollable list — one header per station, one card per upgrade axis, then the
    /// ghost-building unlocks. Replaces CoalHud's code-built list entirely. Editor-authored: the
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
            if (islandNameText != null) islandNameText.text = op.IslandDisplayName;
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

            for (int s = 0; s < _op.StationCount; s++)
            {
                string station = _op.StationName(s);
                _headers.Add((RectTransform)AddHeader(station).transform);
                for (int a = 0; a < _op.AxisCount(s); a++)
                {
                    Row row = AddCard(station, _op.AxisName(s, a));
                    row.station = s; row.axis = a;
                    int cs = s, ca = a;
                    row.buyBtn.onClick.AddListener(() => { if (_op != null && _op.TryUpgrade(cs, ca)) Refresh(); });
                }
            }

            if (_op.UnlockCount > 0)
            {
                AddHeader("GENİŞLETMELER");
                for (int u = 0; u < _op.UnlockCount; u++)
                {
                    // Unlock names carry their effect in brackets ("TRAIN DEPOT (+25% train speed)").
                    // The whole string does not fit the title line and slides under the price button,
                    // so the bracket goes to the level line — which for an unlock only said
                    // "Tek seferlik" and had room to spare.
                    string unlockName = _op.UnlockName(u);
                    int paren = unlockName.IndexOf('(');
                    string title = paren > 0 ? unlockName.Substring(0, paren).TrimEnd() : unlockName;
                    Row row = AddCard(unlockName, title);
                    row.note = paren > 0 ? unlockName.Substring(paren).Trim('(', ')', ' ') : null;
                    row.unlock = u;
                    int cu = u;
                    row.buyBtn.onClick.AddListener(() => { if (_op != null && _op.TryUnlock(cu)) Refresh(); });
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
                if (r.level != null) r.level.text = "KİLİTLİ — " + _op.PowerPlantName;
                return;
            }
            int lv = _op.AxisLevel(r.station, r.axis);
            if (_op.AxisMaxed(r.station, r.axis))
            {
                SetState(r, false, true, false);
                if (r.badge != null && badgeMax != null) r.badge.sprite = badgeMax;
                if (r.level != null) r.level.text = "Sv " + lv;
                return;
            }
            BigDouble cost = _op.AxisCost(r.station, r.axis);
            bool afford = _wallet != null && _wallet.CanAfford(cost);
            SetState(r, true, false, false);
            if (r.level != null) r.level.text = "Sv " + lv;
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
                if (r.level != null) r.level.text = "İnşa edildi";
                return;
            }
            BigDouble cost = _op.UnlockCost(r.unlock);
            bool afford = _wallet != null && _wallet.CanAfford(cost);
            SetState(r, true, false, false);
            if (r.level != null) r.level.text = string.IsNullOrEmpty(r.note) ? "Tek seferlik" : r.note;
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
