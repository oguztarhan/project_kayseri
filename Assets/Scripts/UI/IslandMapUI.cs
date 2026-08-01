using System.Collections;
using Game.Core;
using Game.Gameplay;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The world map (GDD §2 meta), reskinned with the Figma set. The archipelago is not a geography —
    /// every island root sits at the same place in the scene and <see cref="WorldIslands.Travel"/> just
    /// swaps which one is live — so the map is drawn as what the economy actually is: a ladder. One node
    /// per island down a rope, each carrying a card that answers the only question the player has here,
    /// "am I done with this island yet?", as a bar of live $/min against that island's cap.
    ///
    /// Editor-authored like every other screen: the hierarchy lives in the UI_Harita prefab, rows are
    /// cloned from an inactive template inside the scroll content, and every reference below is wired in
    /// the Inspector. Buying costs billions, so it goes through a confirm popup rather than one tap.
    /// </summary>
    public sealed class IslandMapUI : MonoBehaviour
    {
        [Header("Panel (UI_Harita prefabında bağlı)")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Button closeButton;
        [SerializeField] private RectTransform content;
        [Tooltip("Pasif şablon satır: Dugum(+Kilit) · Halat · Ad · Durum · Yatak(DolguAlani/Dolgu) · RozetBurada · BtnGit · BtnFiyat(Fiyat).")]
        [SerializeField] private GameObject rowTemplate;

        [Header("Durum görselleri")]
        [SerializeField] private Sprite nodeOwned;     // madalyon
        [SerializeField] private Sprite nodeLocked;    // madalyon_bekleme
        [SerializeField] private Sprite priceGreen;    // btn_fiyat_yesil — para yetiyor
        [SerializeField] private Sprite priceGrey;     // btn_fiyat_gri — yetmiyor
        [Tooltip("Kilitli adaların düğüm rengi. Sahip olunanlar cevher rengiyle boyanır.")]
        [SerializeField] private Color lockedNodeTint = new Color(0.62f, 0.67f, 0.78f, 1f);

        [Header("Satın alma onayı")]
        [SerializeField] private GameObject confirmRoot;
        [SerializeField] private TMP_Text confirmTitle;
        [SerializeField] private TMP_Text confirmNote;
        [SerializeField] private TMP_Text confirmPrice;
        [SerializeField] private Button confirmBuyButton;
        [SerializeField] private Image confirmBuyImage;
        [SerializeField] private Button confirmCancelButton;

        [Header("Geçiş (karartma)")]
        [Tooltip("Tam ekran siyah katman; ada değişimi tam karanlıkta olur, böylece kök takası görünmez.")]
        [SerializeField] private CanvasGroup fadeGroup;
        [SerializeField] private float fadeOutSeconds = 0.25f;
        [SerializeField] private float fadeHoldSeconds = 0.1f;
        [SerializeField] private float fadeInSeconds = 0.3f;

        [SerializeField] private float refreshInterval = 0.5f;

        private sealed class Row
        {
            public int index;
            public Image node, priceImage;
            public GameObject lockIcon, hereBadge, rope, barRoot, goGO, priceGO;
            public TMP_Text name, status, price;
            public Button goButton, priceButton;
            public RectTransform barFillArea;
            public float barFullWidth;
        }

        private WorldIslands _world;
        private WalletService _wallet;
        private Row[] _rows;
        private float _timer;
        private int _pending = -1;      // island waiting on the confirm popup
        private bool _sailing;

        // Resolved on the first travel, not in Start: these screens build themselves at their own pace
        // and a map that loads first would cache nulls forever.
        private OperationCameraBoot _camBoot;
        private UpgradePanelUI _upgrades;
        private StationBadges _badges;
        private HudJuice _juice;

        private void Start()
        {
            _world = FindAnyObjectByType<WorldIslands>();
            _wallet = ServiceLocator.Get<WalletService>();

            if (closeButton != null) closeButton.onClick.AddListener(Hide);
            if (confirmCancelButton != null) confirmCancelButton.onClick.AddListener(CloseConfirm);
            if (confirmBuyButton != null) confirmBuyButton.onClick.AddListener(OnConfirmBuy);

            BuildRows();
            CloseConfirm();
            if (fadeGroup != null) fadeGroup.gameObject.SetActive(false);
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        /// <summary>Open/close the world map — called by the HUD's map button.</summary>
        public void ToggleMap()
        {
            if (_sailing || panelRoot == null) return;
            if (panelRoot.activeSelf) { Hide(); return; }
            if (_wallet == null) _wallet = ServiceLocator.Get<WalletService>();
            Refresh();
            panelRoot.SetActive(true);
        }

        public void Hide()
        {
            if (_sailing) return;
            CloseConfirm();
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void Update()
        {
            if (panelRoot == null || !panelRoot.activeSelf) return;
            if (_wallet == null) _wallet = ServiceLocator.Get<WalletService>();
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = refreshInterval;
            Refresh();
        }

        // ---------- construction ----------
        private void BuildRows()
        {
            if (_world == null || content == null || rowTemplate == null) return;
            rowTemplate.SetActive(false);

            int n = _world.Count;
            _rows = new Row[n];
            for (int i = 0; i < n; i++)
            {
                GameObject go = Instantiate(rowTemplate, content);
                go.name = "Satir_" + _world.IslandKey(i);
                go.SetActive(true);

                var row = new Row { index = i };
                Transform t;
                t = go.transform.Find("Dugum");
                if (t != null)
                {
                    row.node = t.GetComponent<Image>();
                    Transform k = t.Find("Kilit");
                    if (k != null) row.lockIcon = k.gameObject;
                }
                t = go.transform.Find("Halat");      if (t != null) row.rope = t.gameObject;
                t = go.transform.Find("Ad");         if (t != null) row.name = t.GetComponent<TMP_Text>();
                t = go.transform.Find("Durum");      if (t != null) row.status = t.GetComponent<TMP_Text>();
                t = go.transform.Find("RozetBurada");if (t != null) row.hereBadge = t.gameObject;
                t = go.transform.Find("Yatak");
                if (t != null)
                {
                    row.barRoot = t.gameObject;
                    Transform fill = t.Find("DolguAlani");
                    if (fill != null)
                    {
                        row.barFillArea = (RectTransform)fill;
                        row.barFullWidth = row.barFillArea.rect.width;
                    }
                }
                t = go.transform.Find("BtnGit");
                if (t != null)
                {
                    row.goGO = t.gameObject;
                    row.goButton = t.GetComponent<Button>();
                    int ci = i;
                    if (row.goButton != null) row.goButton.onClick.AddListener(() => OnSail(ci));
                }
                t = go.transform.Find("BtnFiyat");
                if (t != null)
                {
                    row.priceGO = t.gameObject;
                    row.priceButton = t.GetComponent<Button>();
                    row.priceImage = t.GetComponent<Image>();
                    Transform ft = t.Find("Fiyat");
                    if (ft != null) row.price = ft.GetComponent<TMP_Text>();
                    int ci = i;
                    if (row.priceButton != null) row.priceButton.onClick.AddListener(() => OnAskBuy(ci));
                }

                // the rope joins this node to the next one, so the last island has nothing to join to
                if (row.rope != null && i == n - 1) row.rope.SetActive(false);
                _rows[i] = row;
            }
        }

        // ---------- refresh ----------
        private void Refresh()
        {
            if (_world == null || _rows == null) return;

            int next = -1;                                   // first island the player does not own yet
            for (int i = 0; i < _rows.Length; i++)
                if (!_world.IsOwned(i)) { next = i; break; }

            for (int i = 0; i < _rows.Length; i++) RefreshRow(_rows[i], next);
        }

        private void RefreshRow(Row r, int next)
        {
            int i = r.index;
            bool owned = _world.IsOwned(i);
            bool active = owned && i == _world.ActiveIndex;
            bool buyable = !owned && i == next;

            if (r.name != null) r.name.text = _world.IslandName(i);
            if (r.node != null)
            {
                r.node.sprite = owned ? nodeOwned : nodeLocked;
                r.node.color = owned ? _world.OreColor(i) : lockedNodeTint;
            }
            SetOn(r.lockIcon, !owned && !buyable);
            SetOn(r.hereBadge, active);

            if (owned)
            {
                double rate = _world.RatePerMin(i);
                double cap = _world.CapPerMin(i);
                SetOn(r.barRoot, true);
                if (r.barFillArea != null)
                {
                    float p = cap > 0d ? Mathf.Clamp01((float)(rate / cap)) : 1f;
                    r.barFillArea.sizeDelta = new Vector2(r.barFullWidth * p, r.barFillArea.sizeDelta.y);
                }
                if (r.status != null)
                {
                    string money = "$" + NumberFormatter.Format(new BigDouble(rate)) + "/dk";
                    r.status.text = _world.IsMaxed(i) ? money + " · TAVAN" : money + " · tavanın %" + Percent(rate, cap);
                }
                SetOn(r.goGO, !active);
                SetOn(r.priceGO, false);
            }
            else if (buyable)
            {
                SetOn(r.barRoot, false);
                // Only the island you can actually buy advertises its ceiling; showing all eight caps
                // turns the ladder into a wall of numbers.
                if (r.status != null)
                    r.status.text = "tavan $" + NumberFormatter.Format(new BigDouble(_world.CapPerMin(i))) + "/dk";
                SetOn(r.goGO, false);
                SetOn(r.priceGO, true);

                var cost = new BigDouble(_world.UnlockCost(i));
                bool afford = _wallet != null && _wallet.CanAfford(cost);
                if (r.price != null) r.price.text = "$" + NumberFormatter.Format(cost);
                if (r.priceImage != null)
                {
                    r.priceImage.sprite = afford ? priceGreen : priceGrey;
                    // the disabled tint latches on the target graphic; stamp the right colour now
                    r.priceImage.CrossFadeColor(Color.white, 0f, true, true);
                }
                if (r.priceButton != null) r.priceButton.interactable = afford;
            }
            else
            {
                SetOn(r.barRoot, false);
                if (r.status != null) r.status.text = "önce " + _world.IslandName(i - 1);
                SetOn(r.goGO, false);
                SetOn(r.priceGO, false);
            }
        }

        private static void SetOn(GameObject go, bool on)
        {
            if (go != null && go.activeSelf != on) go.SetActive(on);
        }

        /// <summary>Whole percent, so the card never jitters between "%77.4" and "%77.6".</summary>
        private static int Percent(double value, double of)
            => of > 0d ? Mathf.Clamp(Mathf.RoundToInt((float)(value / of * 100d)), 0, 100) : 100;

        // ---------- actions ----------
        private void OnSail(int i)
        {
            if (_sailing || _world == null) return;
            if (!_world.IsOwned(i) || i == _world.ActiveIndex) return;
            StartCoroutine(Travel(i));
        }

        private void OnAskBuy(int i)
        {
            if (_sailing || _world == null || confirmRoot == null) return;
            _pending = i;
            if (confirmTitle != null) confirmTitle.text = _world.IslandName(i);
            if (confirmNote != null)
                confirmNote.text = "tavan $" + NumberFormatter.Format(new BigDouble(_world.CapPerMin(i))) + "/dk";

            var cost = new BigDouble(_world.UnlockCost(i));
            bool afford = _wallet != null && _wallet.CanAfford(cost);
            if (confirmPrice != null) confirmPrice.text = "$" + NumberFormatter.Format(cost);
            if (confirmBuyImage != null)
            {
                confirmBuyImage.sprite = afford ? priceGreen : priceGrey;
                confirmBuyImage.CrossFadeColor(Color.white, 0f, true, true);
            }
            if (confirmBuyButton != null) confirmBuyButton.interactable = afford;
            confirmRoot.SetActive(true);
        }

        private void CloseConfirm()
        {
            _pending = -1;
            if (confirmRoot != null) confirmRoot.SetActive(false);
        }

        private void OnConfirmBuy()
        {
            if (_sailing || _pending < 0 || _world == null) return;
            int i = _pending;
            if (!_world.TryBuy(i)) { Refresh(); return; }   // price moved under us
            CloseConfirm();
            StartCoroutine(Travel(i));
        }

        /// <summary>
        /// Sail to an island behind a full black screen. The swap itself — island roots, operation,
        /// camera framing, and the three HUD screens that hold a per-island reference — happens at full
        /// darkness, which is the whole point of the fade: none of it is watchable mid-frame.
        /// </summary>
        private IEnumerator Travel(int i)
        {
            _sailing = true;

            if (fadeGroup != null)
            {
                fadeGroup.alpha = 0f;
                fadeGroup.blocksRaycasts = true;
                fadeGroup.gameObject.SetActive(true);
                yield return Fade(0f, 1f, fadeOutSeconds);
            }

            CoalOperation op = _world.Travel(i);
            if (op != null)
            {
                if (_camBoot == null) _camBoot = FindAnyObjectByType<OperationCameraBoot>();
                if (_upgrades == null) _upgrades = FindAnyObjectByType<UpgradePanelUI>();
                if (_badges == null) _badges = FindAnyObjectByType<StationBadges>();
                if (_juice == null) _juice = FindAnyObjectByType<HudJuice>();
                if (_camBoot != null) _camBoot.FrameOn(_world.RootName(i));
                if (_upgrades != null) _upgrades.SetOperation(op);
                if (_badges != null) _badges.SetOperation(op);
                if (_juice != null) _juice.SetOperation(op);
            }

            CloseConfirm();
            if (panelRoot != null) panelRoot.SetActive(false);

            if (fadeHoldSeconds > 0f) yield return new WaitForSecondsRealtime(fadeHoldSeconds);
            if (fadeGroup != null)
            {
                yield return Fade(1f, 0f, fadeInSeconds);
                fadeGroup.blocksRaycasts = false;
                fadeGroup.gameObject.SetActive(false);
            }
            _sailing = false;
        }

        private IEnumerator Fade(float from, float to, float seconds)
        {
            if (seconds <= 0f) { fadeGroup.alpha = to; yield break; }
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                fadeGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / seconds));
                yield return null;
            }
            fadeGroup.alpha = to;
        }
    }
}
