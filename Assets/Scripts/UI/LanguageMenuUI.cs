using Game.Core;
using Game.Systems;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The language picker behind the settings screen's DİL row: one tappable row per language, each
    /// written in that language ("Deutsch", "Русский", "Tiếng Việt") — a player who has the game in a
    /// language they cannot read still has to be able to find their way out.
    ///
    /// Built in code rather than authored, because the row list comes from the table: add a column to
    /// <c>metinler.txt</c> and a row appears here with no prefab to touch. That is also why the art
    /// arrives as a <see cref="Skin"/> handed over by <see cref="SettingsUI"/> instead of living in
    /// this component's own Inspector — nothing ever wires a component that is added at runtime. The
    /// pieces are the settings screen's own (panel_ayarlar, serit_baslik, satir_ayar, madalyon), so the
    /// picker reads as the next page of the screen it opened from rather than a second design.
    ///
    /// Text is TMP rather than <see cref="UiBuild"/>'s legacy labels so the Cyrillic fallback applies —
    /// the built-in runtime font has no say in whether "Русский" draws.
    /// </summary>
    public sealed class LanguageMenuUI : MonoBehaviour
    {
        /// <summary>The settings screen's art, handed over because a runtime component has no Inspector.</summary>
        [System.Serializable]
        public struct Skin
        {
            public Sprite Panel;        // panel_ayarlar
            public Sprite Ribbon;       // serit_baslik
            public Sprite Close;        // btn_kapat
            public Sprite Row;          // satir_ayar
            public Sprite Medallion;    // madalyon
            public Sprite PipOn;        // rozet_tamam — yaşayan dilin işareti
            public Sprite PipOff;       // pip_bos
            public TMP_FontAsset Font;  // boşken sahnedeki ilk TMP yazı tipi ödünç alınır
        }

        // Ölçüler UI_Ayarlar'dan birebir alındı: panelin genişliği, üst kenarı, satır yüksekliği,
        // madalyon boyu, satırın soldan boşluğu. Bu ekran o pencerenin bir sonraki sayfası; iki parmak
        // farklı bir satır yüksekliği, ekranın başka bir yerden gelmiş gibi durmasına yetiyordu.
        private const float PanelWidth = 976f;
        // Ayarlar penceresiyle birebir aynı yükseklik. Kısa bir panel denendi ve altından ayarların
        // son satırı görünüyordu — karartma bir pencereyi silmeye yetmiyor, üstünü örtmek gerekiyor.
        private const float PanelHeight = 1520f;
        private const float PanelTop = -430f;       // ayarlar penceresinin üst kenarıyla aynı hizada
        private const float RibbonWidth = 980f;
        private const float RibbonHeight = 230f;
        private const float RibbonDrop = -33f;      // şerit merkezi panelin üst kenarının bu kadar altında
        private const float CloseSize = 140f;
        private const float CloseRight = -94f;      // ayarların kapatma tuşuyla aynı nokta
        private const float CloseTop = -276f;
        private const float ViewportSide = 60f;
        private const float ViewportTop = 162f;     // ayarlardaki ilk satırın başladığı yer
        private const float ViewportBottom = 60f;
        // İki sütun × altı sıra = on iki yer, on bir dil; hepsi tek ekranda, kaydırma yok. Dolgu ızgarayı
        // görüş alanının ortasına oturtuyor: 159 + 6×150 + 5×16 + 159 = 1298, görüş alanının tamamı.
        private const float ListTopPad = 159f;
        private const int Columns = 2;
        // 2×420 + 16 = 856, yani görüş alanının tam genişliği
        private const float CellWidth = 420f;
        private const float CellHeight = 150f;
        private const float CellSpacing = 16f;
        private const float MedallionSize = 104f;
        private const float PipSize = 44f;
        private const float CloseDelay = 0.22f;     // işaret yer değiştirsin, sonra kapansın

        private static readonly Color Ink = new Color32(0x2A, 0x3A, 0x5C, 0xFF);
        private static readonly Color Accent = new Color32(0xB9, 0x5E, 0x06, 0xFF);
        private static readonly Color LiveCard = new Color32(0xFF, 0xF3, 0xD6, 0xFF);
        // 0.72 dünyayı karartmaya yetiyordu ama arkadaki ayarlar penceresi kocaman ve beyaz: %28'i bile
        // panelin altından ikinci bir pencere gibi görünüyordu. Bu ekran bir üst sayfanın yerine geçiyor,
        // arkasında bir şey durmamalı.
        private static readonly Color Dim = new Color(0f, 0f, 0f, 0.88f);

        private LocalizationService _loc;
        private Skin _skin;
        private bool _art;
        private RectTransform _root;
        private RectTransform _list;
        private ScrollRect _scroll;
        private TMP_Text _title;
        private Image[] _rowArt;
        private Image[] _rowPip;
        private TMP_Text[] _rowCodeText;
        private TMP_Text[] _rowNameText;
        private string[] _rowCode;

        /// <summary>Opens the picker, building it the first time.</summary>
        public void Show(Skin skin)
        {
            if (_loc == null) _loc = ServiceLocator.Get<LocalizationService>();
            if (_loc == null) return;
            if (_root == null) { _skin = skin; Build(); }
            if (_root == null) return;

            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            Paint();
            Reveal();
        }

        public void Hide()
        {
            if (_root != null) _root.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ building

        private void Build()
        {
            _art = _skin.Row != null;
            if (_skin.Font == null) _skin.Font = FindFont();

            // Ayrı bir Canvas değil: bu ekran ayarlar Canvas'ının içinde yaşıyor, son kardeş olduğu için
            // ayarlar penceresinin üstüne çiziliyor. İç içe Canvas olsaydı sortingOrder'ın iş görmesi için
            // overrideSorting gerekirdi ve bir batch daha bölünürdü.
            //
            // KÖK TAM KANAMA, İÇERİK GÜVENLİ ALANDA — ikisi ayrı olmak zorunda.
            //
            // Kökün tamamı güvenli alana kurulduğunda karartma da onunla birlikte içeri kayıyordu, yani
            // çentiğin ve alt çubuğun bulunduğu şeritler karartılmadan kalıyor ve ekran tam ekran bir
            // sayfa gibi değil, bir delikten dışarı bakıyormuş gibi duruyordu. Karartmanın işi arkada
            // ne varsa hepsini götürmek; o yüzden karartma kökte.
            //
            // Panel ve kapat düğmesi yine güvenli alanda: ayarlar penceresi orada duruyor ve çentikli
            // bir telefonda ikisi aynı miktarda içeri kaymazsa panel bir kenara doğru kayar.
            _root = Full(transform, "UI_DilSecimi");

            RectTransform dim = Full(_root, "Karartma");
            var dimArt = dim.gameObject.AddComponent<Image>();
            dimArt.color = Dim;
            var dimBtn = dim.gameObject.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(Hide);

            RectTransform icerik = Full(_root, "GuvenliAlan");
            icerik.gameObject.AddComponent<SafeArea>();

            var panelGO = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(PanelOpenFx));
            var panel = (RectTransform)panelGO.transform;
            panel.SetParent(icerik, false);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 1f);
            panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            panel.anchoredPosition = new Vector2(0f, PanelTop);
            Art(panelGO.GetComponent<Image>(), _art ? _skin.Panel : UiSkin.Panel, _art ? Color.white : new Color(0.10f, 0.13f, 0.20f, 1f));
            // panele basmak paneli kapatmasın — karartmaya basmak kapatır
            panelGO.AddComponent<Button>().transition = Selectable.Transition.None;

            BuildList(panel);
            BuildRibbon(panel);
            BuildClose(icerik);

            // Panel sesi kapalıyken takılır, yoksa kuruluşta bir açılış sesi çalar (UiPanelSound'un kuralı).
            _root.gameObject.SetActive(false);
            UiPanelSound.Attach(_root.gameObject);
        }

        private void BuildList(RectTransform panel)
        {
            var viewGO = new GameObject("Kaydirma", typeof(RectTransform), typeof(ScrollRect), typeof(RectMask2D));
            var view = (RectTransform)viewGO.transform;
            view.SetParent(panel, false);
            view.anchorMin = Vector2.zero;
            view.anchorMax = Vector2.one;
            view.offsetMin = new Vector2(ViewportSide, ViewportBottom);
            view.offsetMax = new Vector2(-ViewportSide, -ViewportTop);

            _list = (RectTransform)new GameObject("Icerik", typeof(RectTransform)).transform;
            _list.SetParent(view, false);
            _list.anchorMin = new Vector2(0f, 1f);
            _list.anchorMax = new Vector2(1f, 1f);
            _list.pivot = new Vector2(0.5f, 1f);
            // yeni bir RectTransform 100×100 gelir; yatayda gerili olduğu için o 100 genişliğe eklenir
            // ve satırlar görüş alanından taşar. Yüksekliği ContentSizeFitter sürüyor.
            _list.sizeDelta = Vector2.zero;
            // Izgara: diller yan yana, sonrakiler altına. Tek sütunlu tam genişlikte satırlarken on bir
            // dil ekrana sığmıyordu; iki sütunda hepsi tek bakışta duruyor ve hiç kaydırmak gerekmiyor.
            var grid = _list.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(CellWidth, CellHeight);
            grid.spacing = new Vector2(CellSpacing, CellSpacing);
            grid.padding = new RectOffset(0, 0, (int)ListTopPad, (int)ListTopPad);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Columns;
            grid.childAlignment = TextAnchor.UpperCenter;
            var fit = _list.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scroll = viewGO.GetComponent<ScrollRect>();
            _scroll.content = _list;
            _scroll.viewport = view;
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Elastic;
            _scroll.elasticity = 0.08f;
            _scroll.scrollSensitivity = 34f;
            _scroll.decelerationRate = 0.12f;

            var langs = _loc.Languages;
            _rowArt = new Image[langs.Count];
            _rowPip = new Image[langs.Count];
            _rowCodeText = new TMP_Text[langs.Count];
            _rowNameText = new TMP_Text[langs.Count];
            _rowCode = new string[langs.Count];
            for (int i = 0; i < langs.Count; i++)
            {
                string code = langs[i].Code;
                _rowCode[i] = code;
                Row(i, code, langs[i].Name);
            }
        }

        private void Row(int i, string code, string caption)
        {
            var go = new GameObject("Satir_" + code, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_list, false);
            _rowArt[i] = go.GetComponent<Image>();
            Art(_rowArt[i], _art ? _skin.Row : UiSkin.ButtonGrey, Color.white);
            // satir_ayar 560×400 ve dilimleri 60'ar; 150 yüksekliğe basılınca üst ve alt dilim üst üste
            // biner, çarpan onları küçültüp kenarın yuvarlağını koruyor.
            _rowArt[i].pixelsPerUnitMultiplier = 1.9f;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = _rowArt[i];

            var medal = new GameObject("Madalyon", typeof(RectTransform), typeof(Image));
            var mrt = (RectTransform)medal.transform;
            mrt.SetParent(rt, false);
            mrt.anchorMin = mrt.anchorMax = new Vector2(0f, 0.5f);
            mrt.sizeDelta = new Vector2(MedallionSize, MedallionSize);
            mrt.anchoredPosition = new Vector2(14f + MedallionSize * 0.5f, 0f);
            Art(medal.GetComponent<Image>(), _art ? _skin.Medallion : UiSkin.Flat, _art ? Color.white : new Color(0.20f, 0.25f, 0.36f, 1f));
            medal.GetComponent<Image>().raycastTarget = false;
            _rowCodeText[i] = Text(mrt, "Kod", code.ToUpperInvariant(), 36f, TextAlignmentOptions.Center, Vector2.zero, Vector2.one);

            _rowNameText[i] = Text(rt, "Ad", caption, 40f, TextAlignmentOptions.Left,
                                   new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            var nrt = (RectTransform)_rowNameText[i].transform;
            nrt.sizeDelta = new Vector2(CellWidth - 128f - 60f, 74f);
            nrt.pivot = new Vector2(0f, 0.5f);
            nrt.anchoredPosition = new Vector2(128f, 0f);
            // "Português" ile "Polski" arasında iki kat fark var, kutu ise sabit
            _rowNameText[i].enableAutoSizing = true;
            _rowNameText[i].fontSizeMin = 26f;
            _rowNameText[i].fontSizeMax = 40f;

            var pip = new GameObject("Isaret", typeof(RectTransform), typeof(Image));
            var prt = (RectTransform)pip.transform;
            prt.SetParent(rt, false);
            prt.anchorMin = prt.anchorMax = new Vector2(1f, 0.5f);
            prt.sizeDelta = new Vector2(PipSize, PipSize);
            prt.anchoredPosition = new Vector2(-34f, 0f);
            _rowPip[i] = pip.GetComponent<Image>();
            _rowPip[i].raycastTarget = false;

            string chosen = code;
            btn.onClick.AddListener(delegate { Choose(chosen); });
        }

        private void BuildRibbon(RectTransform panel)
        {
            var go = new GameObject("Serit", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(panel, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(RibbonWidth, RibbonHeight);
            rt.anchoredPosition = new Vector2(0f, RibbonDrop);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            Art(img, _art ? _skin.Ribbon : UiSkin.ButtonYellow, Color.white);
            _title = Text(rt, "Baslik", Loc.T("ayarlar.dil"), 62f, TextAlignmentOptions.Center, Vector2.zero, Vector2.one);
            _title.color = Color.white;
            // şeridin sağ/sol kanatları yazıya girmesin
            var trt = (RectTransform)_title.transform;
            trt.offsetMin = new Vector2(160f, 30f);
            trt.offsetMax = new Vector2(-160f, -10f);
        }

        /// <summary>
        /// The close button, put exactly where the settings screen's own is rather than on the panel:
        /// this game parks every X in the same corner of the screen, and landing on top of the one
        /// underneath means two taps close two screens without the finger moving.
        /// </summary>
        /// <summary>Sağ üste oturuyor, o yüzden karartmanın değil güvenli alanın çocuğu — çentikli bir
        /// telefonda kökün köşesi kesiğin altında kalıyor.</summary>
        private void BuildClose(RectTransform icerik)
        {
            var go = new GameObject("BtnKapat", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(icerik, false);
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(CloseSize, CloseSize);
            rt.anchoredPosition = new Vector2(CloseRight, CloseTop);
            var img = go.GetComponent<Image>();
            Art(img, _art ? _skin.Close : UiSkin.ButtonGrey, Color.white);
            var b = go.GetComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(Hide);
            if (!_art) Text(rt, "Yazi", "X", 54f, TextAlignmentOptions.Center, Vector2.zero, Vector2.one);
        }

        // ------------------------------------------------------------------ choosing

        private void Choose(string code)
        {
            // Tık sesi ve titreşim UiPanelSound'dan geliyor — burada tekrarlamak ikisini de çiftler.
            if (_loc == null) return;
            _loc.SetLanguage(code);
            Paint();

            // Seçim anında kapanırsa oyuncu işaretin yer değiştirdiğini göremez; bir an dursun.
            StopAllCoroutines();
            StartCoroutine(CloseSoon());
        }

        private IEnumerator CloseSoon()
        {
            yield return new WaitForSecondsRealtime(CloseDelay);
            Hide();
        }

        /// <summary>Marks the live language and re-reads this screen's own title in the new language.</summary>
        private void Paint()
        {
            if (_rowArt == null || _loc == null) return;
            if (_title != null) _title.text = Loc.T("ayarlar.dil");
            for (int i = 0; i < _rowArt.Length; i++)
            {
                bool live = _rowCode[i] == _loc.Code;
                if (_rowArt[i] != null) _rowArt[i].color = _art
                    ? (live ? LiveCard : Color.white)
                    : (live ? new Color(0.24f, 0.55f, 0.31f, 1f) : new Color(0.18f, 0.22f, 0.30f, 1f));
                if (_rowPip[i] != null) Art(_rowPip[i], _art ? (live ? _skin.PipOn : _skin.PipOff) : UiSkin.Flat,
                                              _art ? Color.white : (live ? Accent : new Color(1f, 1f, 1f, 0.25f)));
                Color ink = !_art ? Color.white : (live ? Accent : Ink);
                if (_rowCodeText[i] != null) _rowCodeText[i].color = ink;
                if (_rowNameText[i] != null) _rowNameText[i].color = ink;
            }
        }

        /// <summary>
        /// Scrolls the live language into view. Eleven languages in two columns fit on one screen, so
        /// this normally just parks at the top — it earns its keep the day a twelfth column is added to
        /// the table and the grid grows past the panel.
        /// </summary>
        private void Reveal()
        {
            if (_scroll == null || _list == null || _rowCode == null) return;
            int live = -1;
            for (int i = 0; i < _rowCode.Length; i++) if (_rowCode[i] == _loc.Code) { live = i; break; }
            if (live < 0) return;

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_list);
            float viewH = _scroll.viewport != null ? _scroll.viewport.rect.height : 0f;
            float span = _list.rect.height - viewH;
            if (span <= 1f) { _scroll.verticalNormalizedPosition = 1f; return; }

            float top = ListTopPad + (live / Columns) * (CellHeight + CellSpacing) - (viewH - CellHeight) * 0.5f;
            _scroll.verticalNormalizedPosition = 1f - Mathf.Clamp01(top / span);
        }

        // ------------------------------------------------------------------ small builders

        private static RectTransform Full(Transform parent, string name)
        {
            var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static void Art(Image img, Sprite sprite, Color tint)
        {
            img.sprite = sprite != null ? sprite : UiSkin.Flat;
            img.type = Image.Type.Sliced;
            img.color = tint;
        }

        private TMP_Text Text(RectTransform parent, string name, string caption, float size,
                              TextAlignmentOptions align, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var t = go.AddComponent<TextMeshProUGUI>();
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            if (_skin.Font != null) t.font = _skin.Font;
            t.text = caption;
            t.fontSize = size;
            t.alignment = align;
            t.color = Ink;
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        /// <summary>
        /// Borrows the font the rest of the UI already uses when the skin left the slot empty. Taking it
        /// off a live label means the picker follows a font swap without a second place to remember.
        /// </summary>
        private static TMP_FontAsset FindFont()
        {
            var any = FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < any.Length; i++)
                if (any[i].font != null) return any[i].font;
            return null;
        }
    }
}
