using System.Collections;
using Game.Core;
using Game.Gameplay;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The world map (GDD §2 meta) as a full-screen showcase: one island at a time, swiped
    /// left and right. The archipelago is not a geography — every island root sits at the same
    /// place in the scene and <see cref="WorldIslands.Travel"/> just swaps which one is live —
    /// so a list of eight rows only ever read as a form. A single island filling the screen,
    /// wearing its own ore colour and emblem, is what actually sells the next purchase.
    ///
    /// Editor-authored like every other screen: the hierarchy lives in the UI_Harita prefab and
    /// every reference below is wired in the Inspector. Motion is all code — the backdrop turns,
    /// the aura breathes, sparkles orbit an owned island — but nothing here draws layout.
    /// Buying costs billions, so it goes through a confirm popup rather than one tap.
    /// </summary>
    [DefaultExecutionOrder(-110)]
    public sealed class IslandMapUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Header("Panel (UI_Harita prefabında bağlı)")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Button closeButton;
        [Tooltip("Tam ekran zemin; gösterilen adanın cevher rengine boyanır.")]
        [SerializeField] private Image backdrop;
        [Tooltip("Madalyonun arkasında yavaşça dönen ışın çarkı.")]
        [SerializeField] private Image rays;

        [Header("Yatay yerleşim")]
        [SerializeField] private RectTransform titleRibbon;
        [SerializeField] private RectTransform infoCard;

        [Header("Vitrin")]
        [Tooltip("Madalyon, tabela ve butonu taşıyan kök. Kaydırma animasyonu bunu oynatır.")]
        [SerializeField] private RectTransform stage;
        [SerializeField] private CanvasGroup stageGroup;
        [SerializeField] private Image aura;
        [SerializeField] private Image disc;
        [Tooltip("Madalyonun altın dış çerçevesi; diskten ayrı bir prefab düğümüdür.")]
        [SerializeField] private RectTransform medalFrame;
        [SerializeField] private Image emblem;
        [SerializeField] private GameObject lockIcon;
        [SerializeField] private RectTransform sparkleRing;
        [SerializeField] private RectTransform[] sparkles;

        [Header("Tabela")]
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text statusText;
        [Tooltip("Yatak/DolguAlani — genişliği doluluk oranıyla kısılır.")]
        [SerializeField] private RectTransform barFillArea;

        [Header("Sayfa noktaları")]
        [SerializeField] private RectTransform pipRoot;
        [SerializeField] private GameObject pipTemplate;
        [SerializeField] private Sprite pipOn;
        [SerializeField] private Sprite pipOff;

        [Header("Yan geçiş")]
        [SerializeField] private Button prevButton;
        [SerializeField] private Button nextButton;
        [SerializeField] private TMP_Text prevLabel;
        [SerializeField] private TMP_Text nextLabel;

        [Header("Ana buton")]
        [SerializeField] private Button ctaButton;
        [SerializeField] private Image ctaImage;
        [SerializeField] private TMP_Text ctaLabel;
        [SerializeField] private TMP_Text ctaSubLabel;
        [SerializeField] private Sprite ctaGo;        // btn_git — sahip olunan başka ada
        [SerializeField] private Sprite ctaBuy;       // btn_satinal — parası yetiyor
        [SerializeField] private Sprite ctaIdle;      // btn_bekleme — kilitli ya da parası yetmiyor
        [Tooltip("Üzerinde durduğun ada — btn_mavi. Kilitli hâlle aynı sprite'ı paylaşmıyor: " +
                 "\"buradasın\" bir engel değil, ulaşılmış bir yer.")]
        [SerializeField] private Sprite ctaHere;

        [Header("Cevher görselleri")]
        [Tooltip("Ada sırasıyla: kömür, bakır, demir, gümüş, altın, yakut, zümrüt, elmas.")]
        [SerializeField] private Sprite[] oreEmblems;
        [Tooltip("Kilitli adanın madalyon rengi. Sahip olunanlar cevher rengiyle boyanır.")]
        [SerializeField] private Color lockedTint = new Color(0.42f, 0.47f, 0.58f, 1f);

        [Header("Satın alma onayı")]
        [SerializeField] private GameObject confirmRoot;
        [SerializeField] private TMP_Text confirmTitle;
        [SerializeField] private TMP_Text confirmNote;
        [SerializeField] private TMP_Text confirmPrice;
        [SerializeField] private Button confirmBuyButton;
        [SerializeField] private Image confirmBuyImage;
        [SerializeField] private Button confirmCancelButton;

        [Header("Geçiş (yükleme perdesi)")]
        [Tooltip("Tam ekran katman; ada değişimi perdenin arkasında olur, böylece kök takası görünmez.")]
        [SerializeField] private CanvasGroup fadeGroup;
        [SerializeField] private float fadeOutSeconds = 0.25f;
        [Tooltip("Perde düz siyahken bekleme — gösterecek bir şey yok, kısa olmalı.")]
        [SerializeField] private float fadeHoldSeconds = 0.1f;
        [Tooltip("Perde ada görselini taşırken bekleme. Buna açılma süresi ekleniyor: 1,7 + 0,3 = " +
                 "oyuncunun gittiği adayı gördüğü iki saniye.")]
        [SerializeField] private float sailHoldSeconds = 1.7f;

        [Header("Takımadaları şeridi")]
        // The chain along the bottom. Built in code (see MapArchipelago) so the UI_Harita prefab needs
        // nothing wired; these are the band it occupies, as fractions of the panel.
        [SerializeField] private bool showArchipelago = true;
        [SerializeField] private Vector2 archipelagoMin = new Vector2(0.02f, 0.02f);
        [SerializeField] private Vector2 archipelagoMax = new Vector2(0.98f, 0.17f);
        [SerializeField] private float archipelagoNodeSize = 34f;
        [Tooltip("Alt şeritteki cevher rozetleri, ada sırasıyla. Boş bırakılınca zincir düz renkli " +
                 "disklere düşer.")]
        [SerializeField] private Sprite[] archipelagoIcons;
        [Tooltip("Bakılan adanın arkasında nabız gibi yanan hale; o adanın cevher rengiyle boyanır.")]
        [SerializeField] private Sprite archipelagoGlow;
        [SerializeField] private Color archipelagoRoute = new Color(0.62f, 0.78f, 0.92f, 1f);
        [SerializeField] private float fadeInSeconds = 0.3f;

        [Header("Animasyon")]
        [SerializeField] private float openSeconds = 0.32f;
        [SerializeField] private float slideOutSeconds = 0.13f;
        [SerializeField] private float slideInSeconds = 0.2f;
        [Tooltip("Kaydırırken sahnenin kaçtığı mesafe.")]
        [SerializeField] private float slideDistance = 560f;
        [Tooltip("Ekran genişliğinin bu kadarını süpürünce ada değişir.")]
        [SerializeField] private float swipeFraction = 0.16f;
        [SerializeField] private float backdropSpin = 1.4f;
        [SerializeField] private float auraPulseSeconds = 2.6f;
        [SerializeField] private float sparkleSpin = 14f;
        [SerializeField] private float refreshInterval = 0.5f;

        private WorldIslands _world;
        private WalletService _wallet;
        private Canvas _canvas;
        private int _canvasBaseSortingOrder;

        private int _shown;             // island the showcase is displaying, not necessarily the live one
        private int _pending = -1;      // island waiting on the confirm popup
        private bool _sailing;
        private MapArchipelago _chain;
        private bool _busy;             // a slide is playing; input is ignored
        private bool _dragging;
        private float _dragStart;
        private float _timer;
        private float _clock;
        private Color _backTint = Color.white;
        private Color _backWant = Color.white;
        private Image[] _pips;
        private Vector3 _auraBase = Vector3.one;

        // the backdrop is a flat fill, not art: deep enough that the lit medallion and the plate
        // carry the screen, but still carrying a trace of the island's ore colour
        private static readonly Color BackdropFloor = new Color(0.05f, 0.09f, 0.16f, 1f);

        // Resolved on the first travel, not in Start: these screens build themselves at their own
        // pace and a map that loads first would cache nulls forever.
        private OperationCameraBoot _camBoot;
        private StationScreenUI _upgrades;
        private HudJuice _juice;

        private Image _curtain;
        private TMP_Text _travelName;
        private TMP_Text _travelPercent;
        private RectTransform _travelFill;
        private Image _travelFillImage;

        private const string TravelBackdropResource = "UI/Transitions/island_transition";

        private void Awake()
        {
            ApplyLandscapeLayout();
        }

        private void Start()
        {
            _world = FindAnyObjectByType<WorldIslands>();
            _wallet = ServiceLocator.Get<WalletService>();
            _canvas = GetComponentInParent<Canvas>();
            if (_canvas != null) _canvasBaseSortingOrder = _canvas.sortingOrder;

            var panelRect = panelRoot != null ? panelRoot.transform as RectTransform : null;
            if (showArchipelago && _world != null && panelRect != null)
            {
                _chain = new MapArchipelago(_world);
                // Behind the medallion stage but in front of the backdrop and its ray wheel, so the
                // chain reads as something on the water rather than something over the UI. Inserting
                // AT the stage's index pushes the stage one later, which is what puts us behind it.
                int behindStage = stage != null && stage.parent == panelRect ? stage.GetSiblingIndex() : 1;
                _chain.Build(panelRect, behindStage, archipelagoMin, archipelagoMax,
                             archipelagoRoute, lockedTint, archipelagoNodeSize,
                             archipelagoIcons, archipelagoGlow);
            }

            if (closeButton != null) closeButton.onClick.AddListener(Hide);
            if (confirmCancelButton != null) confirmCancelButton.onClick.AddListener(CloseConfirm);
            if (confirmBuyButton != null) confirmBuyButton.onClick.AddListener(OnConfirmBuy);
            if (prevButton != null) prevButton.onClick.AddListener(StepBack);
            if (nextButton != null) nextButton.onClick.AddListener(StepOn);
            if (ctaButton != null) ctaButton.onClick.AddListener(OnCta);

            NormalizeSideButton(prevButton, prevLabel, true);
            NormalizeSideButton(nextButton, nextLabel, false);

            if (aura != null) _auraBase = aura.rectTransform.localScale;
            // Perde zaten tam ekran bir Image taşıyor (siyah, sprite'sız). Ada görselini onun üstüne
            // basıyoruz — ikinci bir katman kurmak, ikinci bir referans bağlamak demekti.
            if (fadeGroup != null) _curtain = fadeGroup.GetComponent<Image>();
            BuildTravelOverlay();
            BuildPips();
            CloseConfirm();
            if (fadeGroup != null) fadeGroup.gameObject.SetActive(false);
            if (panelRoot != null) panelRoot.SetActive(false);
            UiPanelSound.Attach(panelRoot);   // panel kapatıldıktan SONRA — açılış sesi boot'ta çalmasın
        }

        /// <summary>
        /// Only the arrow art is mirrored on the previous-island button. Mirroring the whole Button
        /// transform also mirrors every child TMP label and produced the backwards island name seen on
        /// the map. A dedicated graphic child keeps both labels in a normal coordinate system.
        /// </summary>
        private static void NormalizeSideButton(Button button, TMP_Text label, bool mirrorArrow)
        {
            if (button == null) return;

            RectTransform root = button.transform as RectTransform;
            if (root == null) return;
            root.localScale = Vector3.one;
            root.localRotation = Quaternion.identity;
            if (label != null)
            {
                label.rectTransform.localScale = Vector3.one;
                label.rectTransform.localRotation = Quaternion.identity;
            }

            Image source = button.GetComponent<Image>();
            if (source == null) return;
            Transform found = root.Find("OkGorseli");
            Image arrow;
            if (found == null)
            {
                var go = new GameObject("OkGorseli", typeof(RectTransform), typeof(Image));
                var rt = (RectTransform)go.transform;
                rt.SetParent(root, false);
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                rt.SetAsFirstSibling();
                arrow = go.GetComponent<Image>();
            }
            else arrow = found.GetComponent<Image>();

            if (arrow == null) return;
            arrow.sprite = source.sprite;
            arrow.type = source.type;
            arrow.preserveAspect = source.preserveAspect;
            arrow.color = source.color;
            arrow.raycastTarget = true;
            arrow.rectTransform.localScale = new Vector3(mirrorArrow ? -1f : 1f, 1f, 1f);
            source.enabled = false;
            button.targetGraphic = arrow;
        }

        private void ApplyLandscapeLayout()
        {
            if (Screen.width <= Screen.height || stage == null) return;

            SetRect(stage, Vector2.zero, new Vector2(2100f, 850f));
            // Başlık şeridi ekranın tepesinde ortada; kapat sağ üstte. Şerit 640x134'ten büyük
            // olamıyor: bilgi kartının üst kenarı 310'da, şeridin kuyrukları 348'de bitiyor.
            SetRect(titleRibbon, new Vector2(0f, 415f), new Vector2(640f, 134f));
            SetRect(closeButton != null ? closeButton.transform as RectTransform : null,
                    new Vector2(1000f, 350f), new Vector2(84f, 84f));

            Vector2 medal = new Vector2(-560f, 10f);
            SetPosition(rays != null ? rays.rectTransform : null, medal);
            SetPosition(aura != null ? aura.rectTransform : null, medal);
            SetPosition(disc != null ? disc.rectTransform : null, medal);
            SetPosition(medalFrame, medal);
            SetPosition(emblem != null ? emblem.rectTransform : null, medal);
            SetPosition(lockIcon != null ? lockIcon.transform as RectTransform : null, medal);
            SetPosition(sparkleRing, medal);

            // Sağ sütun: bilgi kartı, sayfa noktaları, ana buton. Kartın kendi mavi başlık şeridi
            // ada adını taşıyor, durum ve doluluk çubuğu beyaz gövdede.
            SetRect(infoCard, new Vector2(480f, 145f), new Vector2(880f, 330f));
            if (nameText != null) SetRect(nameText.rectTransform, new Vector2(0f, 115f), new Vector2(780f, 92f));
            if (statusText != null) SetRect(statusText.rectTransform, new Vector2(0f, 0f), new Vector2(780f, 58f));
            RectTransform barBed = barFillArea != null ? barFillArea.parent as RectTransform : null;
            SetRect(barBed, new Vector2(0f, -85f), new Vector2(760f, 52f));

            SetRect(pipRoot, new Vector2(480f, -80f), new Vector2(720f, 56f));
            SetRect(ctaButton != null ? ctaButton.transform as RectTransform : null,
                    new Vector2(480f, -230f), new Vector2(720f, 180f));

            // Oklar adanın iki yanında: sol kenarda ve ada ile bilgi kartının arasında.
            SetRect(prevButton != null ? prevButton.transform as RectTransform : null,
                    new Vector2(-1000f, 10f), new Vector2(124f, 124f));
            SetRect(nextButton != null ? nextButton.transform as RectTransform : null,
                    new Vector2(-120f, 10f), new Vector2(124f, 124f));
        }

        private static void SetPosition(RectTransform rect, Vector2 position)
        {
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
        }

        private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        /// <summary>Open/close the world map — called by the HUD's map button.</summary>
        public void ToggleMap()
        {
            if (_sailing || panelRoot == null) return;
            if (panelRoot.activeSelf) { Hide(); return; }
            if (_wallet == null) _wallet = ServiceLocator.Get<WalletService>();
            _shown = _world != null ? _world.ActiveIndex : 0;
            Refresh();
            _backTint = _backWant;                       // open already wearing the right colour
            panelRoot.SetActive(true);
            StopAllCoroutines();
            StartCoroutine(OpenFx());
        }

        public void Hide()
        {
            if (_sailing) return;
            CloseConfirm();
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        // ---------- construction ----------
        private void BuildPips()
        {
            if (_world == null || pipRoot == null || pipTemplate == null) return;
            pipTemplate.SetActive(false);
            int n = _world.Count;
            _pips = new Image[n];
            for (int i = 0; i < n; i++)
            {
                GameObject go = Instantiate(pipTemplate, pipRoot);
                go.name = "Nokta_" + i;
                go.SetActive(true);
                _pips[i] = go.GetComponent<Image>();
            }
        }

        // ---------- per-frame motion ----------
        private void Update()
        {
            if (panelRoot == null || !panelRoot.activeSelf) return;
            float dt = Time.unscaledDeltaTime;
            _clock += dt;

            if (_chain != null) _chain.Tick(dt);

            if (rays != null) rays.rectTransform.Rotate(0f, 0f, backdropSpin * dt);
            if (backdrop != null)
            {
                _backTint = Color.Lerp(_backTint, _backWant, dt * 5f);
                backdrop.color = _backTint;
            }

            if (aura != null && aura.gameObject.activeSelf)
            {
                float p = 0.5f + 0.5f * Mathf.Sin(_clock * Mathf.PI * 2f / Mathf.Max(0.1f, auraPulseSeconds));
                aura.rectTransform.localScale = _auraBase * (0.94f + p * 0.12f);
                Color c = aura.color;
                c.a = 0.42f + p * 0.30f;
                aura.color = c;
            }

            if (sparkleRing != null && sparkleRing.gameObject.activeSelf)
            {
                sparkleRing.Rotate(0f, 0f, sparkleSpin * dt);
                if (sparkles != null)
                    for (int i = 0; i < sparkles.Length; i++)
                    {
                        if (sparkles[i] == null) continue;
                        float s = 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(_clock * 1.7f + i * 1.1f));
                        sparkles[i].localScale = new Vector3(s, s, 1f);
                    }
            }

            if (_wallet == null) _wallet = ServiceLocator.Get<WalletService>();
            _timer -= dt;
            if (_timer > 0f) return;
            _timer = refreshInterval;
            Refresh();
        }

        // ---------- refresh ----------
        private void Refresh()
        {
            if (_world == null || _shown < 0 || _shown >= _world.Count) return;
            int i = _shown;
            bool owned = _world.IsOwned(i);
            bool here = owned && i == _world.ActiveIndex;

            int next = -1;                                   // first island the player does not own yet
            for (int k = 0; k < _world.Count; k++)
                if (!_world.IsOwned(k)) { next = k; break; }
            bool nextTarget = !owned && i == next;
            bool buyable = nextTarget && _world.CanBuy(i);

            // The BRAND, not the ore — see WorldIslands.BrandColor. Half the ore palette is grey by
            // design, and this screen was showing that grey.
            Color ore = _world.BrandColor(i);
            // ore colours run dark — coal is nearly black — so the disc is lifted toward the set's
            // cream, or the medallion reads as a hole punched in the screen
            Color face = owned ? Color.Lerp(ore, new Color(1f, 0.96f, 0.88f), 0.5f) : lockedTint;
            if (disc != null) disc.color = face;
            if (aura != null)
            {
                aura.gameObject.SetActive(owned || buyable);
                Color a = owned ? ore : new Color(0.98f, 0.82f, 0.32f);
                aura.color = new Color(a.r, a.g, a.b, aura.color.a);
            }
            if (emblem != null)
            {
                emblem.sprite = oreEmblems != null && i < oreEmblems.Length ? oreEmblems[i] : null;
                // Kilitli ada da kendi silüetini gösteriyor, sadece karartılmış. Madalyon diski
                // kaldırıldığından "sahip değilsen hiçbir şey yok" hâli ekranı boşaltıyordu.
                emblem.enabled = emblem.sprite != null;
                emblem.color = owned ? Color.white : new Color(0.40f, 0.42f, 0.48f, 1f);
            }
            SetOn(lockIcon, !owned);
            if (sparkleRing != null) sparkleRing.gameObject.SetActive(owned);
            // The rays are the only thing carrying the colour at full strength, and at 0.42/0.18 they
            // were barely there. This is the screen's main source of hue now.
            if (rays != null) rays.color = new Color(ore.r, ore.g, ore.b, owned ? 0.70f : 0.34f);

            // 0.72 kept barely a quarter of the island's colour and mixed in three quarters of a
            // near-black navy, so every backdrop landed in the same dark band whatever island you were
            // looking at. 0.45 keeps over half of it and still reads as deep water.
            //
            // A locked island keeps its own colour too, dimmed rather than replaced by a shared grey:
            // the map's job is to make you want the next island, and it cannot do that while every
            // island you do not own looks identical.
            _backWant = owned
                ? Color.Lerp(ore, BackdropFloor, 0.45f)
                : Color.Lerp(ore, BackdropFloor, 0.72f);

            if (_chain != null) _chain.Refresh(i, lockedTint, archipelagoRoute);

            if (nameText != null) nameText.text = IslandName(i);
            double cap = _world.CapPerMin(i);
            if (statusText != null)
            {
                if (owned)
                {
                    double rate = _world.RatePerMin(i);
                    string money = string.Format(Loc.T("ortak.dakika_basina"),
                                                 "$" + NumberFormatter.Format(new BigDouble(rate)));
                    statusText.text = _world.IsMaxed(i) ? string.Format(Loc.T("harita.tamamlandi"), money)
                                                        : string.Format(Loc.T("harita.oran"), money, Percent(rate, cap));
                }
                else
                {
                    statusText.text = string.Format(Loc.T("harita.tavana_kadar"),
                                                    "$" + NumberFormatter.Format(new BigDouble(cap)));
                }
            }
            if (barFillArea != null)
            {
                float full = ((RectTransform)barFillArea.parent).rect.width;
                float p = owned && cap > 0d ? Mathf.Clamp01((float)(_world.RatePerMin(i) / cap)) : 0f;
                barFillArea.sizeDelta = new Vector2(full * p, barFillArea.sizeDelta.y);
            }

            RefreshCta(owned, here, buyable, nextTarget);
            RefreshSides(i);
            RefreshPips(i);
        }

        /// <summary>
        /// The island's name in the player's language. <see cref="WorldIslands"/> keeps a Turkish
        /// display name next to the key, and that key is what the save file and the scene roots are
        /// built on — so the name is translated here, at the point it is drawn, and the id stays put.
        /// </summary>
        private string IslandName(int i)
        {
            if (_world == null || i < 0 || i >= _world.Count) return string.Empty;
            return Loc.Id("ada", _world.IslandKey(i));
        }

        private void RefreshCta(bool owned, bool here, bool buyable, bool nextTarget)
        {
            if (ctaButton == null) return;
            bool afford = false;
            string label, sub;
            Sprite art;

            // Every island wears the same button now — the one the coal island has. Three different
            // pieces of art for four states made the ladder look like four different screens; the
            // state is carried by the label and by the tint below instead of by the silhouette.
            if (here)
            {
                label = Loc.T("harita.buradasin"); sub = ""; art = ctaHere != null ? ctaHere : ctaIdle;
            }
            else if (owned)
            {
                label = Loc.T("ortak.git"); sub = ""; art = ctaHere != null ? ctaHere : ctaGo; afford = true;
            }
            else if (buyable)
            {
                var cost = new BigDouble(_world.UnlockCost(_shown));
                afford = _wallet != null && _wallet.CanAfford(cost);
                label = Loc.T("ortak.satin_al");
                sub = "$" + NumberFormatter.Format(cost);
                art = ctaHere != null ? ctaHere : (afford ? ctaBuy : ctaIdle);
            }
            else if (nextTarget)
            {
                label = Loc.T("ortak.kilitli");
                sub = string.Format(Loc.T("harita.hedefleri_tamamla"), IslandName(Mathf.Max(0, _shown - 1)));
                art = ctaHere != null ? ctaHere : ctaIdle;
            }
            else
            {
                // The island right before this one, not the first gap in the ladder. Naming the far-off
                // gap made every locked island in the tail say the same thing — four cards all reading
                // "first GOLD ISLAND" tell you nothing about how far away you are. One step at a time
                // reads as a chain, and following it forward lands on the one you can actually buy.
                label = Loc.T("ortak.kilitli");
                sub = string.Format(Loc.T("harita.once"), IslandName(Mathf.Max(0, _shown - 1)));
                art = ctaHere != null ? ctaHere : ctaIdle;
            }

            if (ctaLabel != null) ctaLabel.text = label;
            if (ctaSubLabel != null)
            {
                ctaSubLabel.text = sub;
                SetOn(ctaSubLabel.gameObject, sub.Length > 0);
            }
            // One sprite for four states means the unavailable ones have to say so some other way,
            // or a locked island offers a button that looks live and does nothing. "Here" is not
            // unavailable — it is arrived at — so it keeps full colour with the rest.
            //
            // Written into the button's own disabled colour rather than stamped on the image: the
            // Selectable cross-fades to that colour over several frames whenever interactable moves,
            // so any tint painted here by hand is overwritten a frame later.
            Color rest = afford || here ? Color.white : new Color(0.55f, 0.60f, 0.68f, 1f);
            ColorBlock scheme = ctaButton.colors;
            if (scheme.disabledColor != rest) { scheme.disabledColor = rest; ctaButton.colors = scheme; }
            ctaButton.interactable = afford;
            if (ctaImage != null && art != null)
            {
                ctaImage.sprite = art;
                ctaImage.CrossFadeColor(rest, 0f, true, true);
            }
        }

        private void RefreshSides(int i)
        {
            bool hasPrev = i > 0, hasNext = i < _world.Count - 1;
            if (prevButton != null) SetOn(prevButton.gameObject, hasPrev);
            if (nextButton != null) SetOn(nextButton.gameObject, hasNext);
            if (prevLabel != null && hasPrev) prevLabel.text = IslandName(i - 1);
            if (nextLabel != null && hasNext) nextLabel.text = IslandName(i + 1);
        }

        private void RefreshPips(int i)
        {
            if (_pips == null) return;
            for (int k = 0; k < _pips.Length; k++)
            {
                if (_pips[k] == null) continue;
                _pips[k].sprite = k == i ? pipOn : pipOff;
                _pips[k].color = _world.IsOwned(k) ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            }
        }

        private static void SetOn(GameObject go, bool on)
        {
            if (go != null && go.activeSelf != on) go.SetActive(on);
        }

        /// <summary>Whole percent, so the card never jitters between "%77.4" and "%77.6".</summary>
        private static int Percent(double value, double of)
            => of > 0d ? Mathf.Clamp(Mathf.RoundToInt((float)(value / of * 100d)), 0, 100) : 100;

        // ---------- paging ----------
        private void StepBack() { Step(-1); }
        private void StepOn() { Step(1); }

        private void Step(int dir)
        {
            if (_busy || _sailing || _world == null) return;
            int target = _shown + dir;
            if (target < 0 || target >= _world.Count) { StartCoroutine(Settle()); return; }
            StartCoroutine(Swap(target, dir));
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (_busy || _sailing) return;
            _dragging = true;
            _dragStart = e.position.x;
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_dragging || stage == null) return;
            float scale = _canvas != null && _canvas.scaleFactor > 0.01f ? _canvas.scaleFactor : 1f;
            float dx = (e.position.x - _dragStart) / scale;
            stage.anchoredPosition = new Vector2(Mathf.Clamp(dx * 0.7f, -260f, 260f), 0f);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (!_dragging) return;
            _dragging = false;
            float dx = e.position.x - _dragStart;
            if (Mathf.Abs(dx) > Screen.width * swipeFraction) Step(dx < 0f ? 1 : -1);
            else StartCoroutine(Settle());
        }

        /// <summary>Slide the stage out one way, redraw it for the new island, bring it in the other.</summary>
        private IEnumerator Swap(int target, int dir)
        {
            _busy = true;
            float from = stage != null ? stage.anchoredPosition.x : 0f;
            yield return Glide(from, -dir * slideDistance, 1f, 0f, slideOutSeconds);
            _shown = target;
            Refresh();
            yield return Glide(dir * slideDistance, 0f, 0f, 1f, slideInSeconds);
            _busy = false;
        }

        /// <summary>Nothing changed — drop the half-swiped stage back into place.</summary>
        private IEnumerator Settle()
        {
            if (stage == null) yield break;
            _busy = true;
            yield return Glide(stage.anchoredPosition.x, 0f, 1f, 1f, slideInSeconds);
            _busy = false;
        }

        private IEnumerator Glide(float fromX, float toX, float fromA, float toA, float seconds)
        {
            if (stage == null) yield break;
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / seconds);
                k = 1f - (1f - k) * (1f - k);                       // ease out
                stage.anchoredPosition = new Vector2(Mathf.Lerp(fromX, toX, k), 0f);
                if (stageGroup != null) stageGroup.alpha = Mathf.Lerp(fromA, toA, k);
                yield return null;
            }
            stage.anchoredPosition = new Vector2(toX, 0f);
            if (stageGroup != null) stageGroup.alpha = toA;
        }

        /// <summary>Opening pop: the stage overshoots past full size and settles back.</summary>
        private IEnumerator OpenFx()
        {
            if (stage == null) yield break;
            stage.anchoredPosition = Vector2.zero;
            float t = 0f;
            while (t < openSeconds)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / openSeconds);
                float s = 0.86f + 0.14f * (1f + 2.2f * Mathf.Sin(k * Mathf.PI) * (1f - k)) * k;
                stage.localScale = new Vector3(s, s, 1f);
                if (stageGroup != null) stageGroup.alpha = Mathf.Clamp01(k * 2f);
                yield return null;
            }
            stage.localScale = Vector3.one;
            if (stageGroup != null) stageGroup.alpha = 1f;
        }

        // ---------- actions ----------
        private void OnCta()
        {
            if (_busy || _sailing || _world == null) return;
            if (_world.IsOwned(_shown))
            {
                if (_shown != _world.ActiveIndex) StartCoroutine(Travel(_shown));
                return;
            }
            AskBuy(_shown);
        }

        private void AskBuy(int i)
        {
            if (confirmRoot == null) return;
            _pending = i;
            if (confirmTitle != null) confirmTitle.text = IslandName(i);
            if (confirmNote != null)
                confirmNote.text = string.Format(Loc.T("harita.tavana_kadar"),
                                                 "$" + NumberFormatter.Format(new BigDouble(_world.CapPerMin(i))));

            var cost = new BigDouble(_world.UnlockCost(i));
            bool afford = _wallet != null && _wallet.CanAfford(cost);
            if (confirmPrice != null) confirmPrice.text = "$" + NumberFormatter.Format(cost);
            if (confirmBuyImage != null)
            {
                confirmBuyImage.sprite = afford ? ctaBuy : ctaIdle;
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
        /// Builds the destination name and progress readout once. They live as children of the authored
        /// full-screen curtain, so they are always above the painting and inherit the same fade/input
        /// block without adding another canvas or another safe-area edge case.
        /// </summary>
        private void BuildTravelOverlay()
        {
            if (fadeGroup == null) return;
            Transform root = fadeGroup.transform;

            // Travel is a global transition, so it must render above every regular modal canvas.
            RaiseTravelOverlay();

            _travelName = TravelLabel(root, "HedefAda", 60f,
                                      new Vector2(0.13f, 0.755f), new Vector2(0.87f, 0.865f),
                                      new Color(1f, 0.86f, 0.40f, 1f));

            // Two nested beds give the bar a crisp readable edge on both the bright sea and the dark
            // tunnel foreground. The fill itself is coloured for the destination ore in Curtain().
            UiBuild.Flat(root, "YuklemeGolgesi", new Color(0.005f, 0.018f, 0.045f, 0.88f),
                         new Vector2(0.205f, 0.068f), new Vector2(0.795f, 0.118f));
            RectTransform track = UiBuild.Flat(root, "YuklemeYatagi", new Color(0.035f, 0.10f, 0.20f, 0.94f),
                                               new Vector2(0.215f, 0.078f), new Vector2(0.785f, 0.108f));
            _travelFill = UiBuild.Flat(track, "Dolgu", Color.white, Vector2.zero, new Vector2(0f, 1f));
            _travelFillImage = _travelFill.GetComponent<Image>();

            _travelPercent = TravelLabel(root, "Yuzde", 25f,
                                          new Vector2(0.42f, 0.118f), new Vector2(0.58f, 0.16f),
                                          new Color(1f, 1f, 1f, 0.92f));
            TravelProgress(0f);
        }

        /// <summary>Notification deep-link entry: sail directly to the island carried by the alert.</summary>
        public bool TravelToIsland(string islandKey)
        {
            if (_world == null) _world = FindAnyObjectByType<WorldIslands>();
            if (_world == null || string.IsNullOrEmpty(islandKey) || _busy || _sailing) return false;
            for (int i = 0; i < _world.Count; i++)
            {
                if (_world.IslandKey(i) != islandKey || !_world.IsOwned(i)) continue;
                if (i != _world.ActiveIndex) StartCoroutine(Travel(i));
                return true;
            }
            return false;
        }

        private void RaiseTravelOverlay()
        {
            if (fadeGroup == null) return;
            if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
            if (_canvas != null) _canvas.sortingOrder = 1000;
            fadeGroup.transform.SetAsLastSibling();
        }

        private TMP_Text TravelLabel(Transform parent, string name, float size,
                                     Vector2 aMin, Vector2 aMax, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset font = nameText != null ? nameText.font : TMP_Settings.defaultFontAsset;
            if (font != null) text.font = font;
            text.fontSize = size;
            text.enableAutoSizing = true;
            text.fontSizeMin = Mathf.Max(22f, size * 0.58f);
            text.fontSizeMax = size;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.color = color;
            if (text.font != null)
            {
                text.outlineColor = new Color32(4, 14, 31, 240);
                text.outlineWidth = 0.22f;
            }
            text.raycastTarget = false;
            UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
            return text;
        }

        private void TravelProgress(float value)
        {
            float t = Mathf.Clamp01(value);
            if (_travelFill != null) _travelFill.anchorMax = new Vector2(t, 1f);
            if (_travelPercent != null) _travelPercent.text = Mathf.RoundToInt(t * 100f) + "%";
        }

        /// <summary>
        /// Sail to an island behind the destination's loading screen — the same splash the game boots
        /// on, so arriving somewhere new is announced by the art of the place rather than by a black
        /// gap. The swap itself — island roots, operation, camera framing, and the three HUD screens
        /// that hold a per-island reference — happens behind it, which is the whole point of the
        /// curtain: none of it is watchable mid-frame.
        /// </summary>
        private IEnumerator Travel(int i)
        {
            _sailing = true;
            bool art = Curtain(i);
            TravelProgress(0f);

            if (fadeGroup != null)
            {
                fadeGroup.alpha = 0f;
                fadeGroup.blocksRaycasts = true;
                fadeGroup.gameObject.SetActive(true);
                // Unity can rebuild a nested Canvas when an inactive prefab node is enabled. Apply
                // the global sorting order after activation as well so no modal can cover travel.
                RaiseTravelOverlay();
                yield return Fade(0f, 1f, fadeOutSeconds, 0f, 0.18f);
            }

            int previousIsland = _world.ActiveIndex;
            CoalOperation op = _world.Travel(i);
            bool arrived = previousIsland != i && _world.ActiveIndex == i;
            if (arrived)
            {
                if (_camBoot == null) _camBoot = FindAnyObjectByType<OperationCameraBoot>();
                if (_upgrades == null) _upgrades = FindAnyObjectByType<StationScreenUI>(FindObjectsInactive.Include);
                if (_juice == null) _juice = FindAnyObjectByType<HudJuice>();

                // Camera framing belongs to the island switch, not to the optional operation binding.
                // If an operation is late/missing on a device, the destination is still live and must
                // never inherit the previous island's camera position.
                if (_camBoot != null) _camBoot.FrameOn(_world.RootName(i));
                if (op != null)
                {
                    if (_upgrades != null) _upgrades.SetOperation(op);
                    if (_juice != null) _juice.SetOperation(op);
                }
            }
            TravelProgress(0.58f);

            CloseConfirm();
            if (panelRoot != null) panelRoot.SetActive(false);

            // Siyah perdenin beklemesi için sebep yok; ada görselini taşıyorsa oyuncunun onu görmesi
            // için var.
            float hold = art ? sailHoldSeconds : fadeHoldSeconds;
            if (hold > 0f)
            {
                float elapsed = 0f;
                while (elapsed < hold)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / hold);
                    // A gentle ease keeps the bar moving throughout the announcement instead of
                    // reaching 100% immediately and pretending to be stuck there.
                    t = 1f - (1f - t) * (1f - t);
                    TravelProgress(Mathf.Lerp(0.58f, 1f, t));
                    yield return null;
                }
            }
            TravelProgress(1f);
            if (fadeGroup != null)
            {
                yield return Fade(1f, 0f, fadeInSeconds);
                fadeGroup.blocksRaycasts = false;
                fadeGroup.gameObject.SetActive(false);
                if (_canvas != null) _canvas.sortingOrder = _canvasBaseSortingOrder;
            }
            _sailing = false;
        }

        /// <summary>
        /// Paints the curtain with the island being sailed to and says whether it got any art. An island
        /// with no splash wired falls back to the plain black curtain — the behaviour this screen had
        /// before the art existed — rather than showing the previous destination's.
        /// </summary>
        private bool Curtain(int i)
        {
            if (_curtain == null) return false;
            Sprite background = Resources.Load<Sprite>(TravelBackdropResource);
            bool has = background != null;
            _curtain.sprite = background;
            _curtain.color = has ? Color.white : Color.black;
            _curtain.type = Image.Type.Simple;
            _curtain.preserveAspect = false;

            if (_travelName != null)
            {
                // i is the destination, never the island being left. The sentence therefore says
                // "BAKIR ADASI YÜKLENİYOR" for coal -> copper and "KÖMÜR ADASI YÜKLENİYOR" on the way back.
                _travelName.text = IslandName(i).ToUpperInvariant() + " " +
                                   Loc.T("ortak.yukleniyor").ToUpperInvariant();
            }
            if (_travelFillImage != null && _world != null)
            {
                // The BRAND, not the ore — see WorldIslands.BrandColor. Half the ore palette is grey by
            // design, and this screen was showing that grey.
            Color ore = _world.BrandColor(i);
                _travelFillImage.color = Color.Lerp(ore, new Color(1f, 0.80f, 0.28f, 1f), 0.42f);
            }
            return has;
        }

        private IEnumerator Fade(float from, float to, float seconds,
                                 float progressFrom = -1f, float progressTo = -1f)
        {
            if (seconds <= 0f) { fadeGroup.alpha = to; yield break; }
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / seconds);
                fadeGroup.alpha = Mathf.Lerp(from, to, k);
                if (progressFrom >= 0f && progressTo >= 0f)
                    TravelProgress(Mathf.Lerp(progressFrom, progressTo, k));
                yield return null;
            }
            fadeGroup.alpha = to;
        }
    }
}
