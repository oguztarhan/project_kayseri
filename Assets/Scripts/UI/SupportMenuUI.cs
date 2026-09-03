using Game.Core;
using Game.Systems;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The settings screen's second page: how to reach us, where the community is, and the number that
    /// identifies this save.
    ///
    /// It is a page rather than four more rows because there is no room for four more rows. The authored
    /// settings card is 1782 units tall in a 2340-unit design space and its last row already ends 98
    /// units from the bottom edge; the card cannot grow without leaving the screen. So this follows the
    /// path <see cref="LanguageMenuUI"/> took — built in code, wearing the settings screen's own art,
    /// handed over as a <see cref="LanguageMenuUI.Skin"/> because a component added at runtime has no
    /// Inspector for anyone to wire.
    ///
    /// Reusing that struct rather than declaring a second one is deliberate: <see cref="SettingsUI"/>
    /// already carries a filled-in skin, so this screen inherited the art with no new prefab work at
    /// all, and the two pages cannot drift apart the day a sprite is swapped.
    ///
    /// EVERY ROW IS OPTIONAL EXCEPT THE ID. An unconfigured support address or an empty link list hides
    /// its row instead of showing a button that does nothing — which is also how the ad-preferences row
    /// on the page behind this one behaves.
    /// </summary>
    public sealed class SupportMenuUI : MonoBehaviour
    {
        /// <summary>One community destination, authored on <see cref="SettingsUI"/>.</summary>
        [System.Serializable]
        public struct Link
        {
            [Tooltip("Satırın adı — çevrilmez, çünkü Discord her dilde Discord.")]
            public string Label;
            [Tooltip("Tam adres. Boşken satır hiç kurulmaz.")]
            public string Url;
        }

        // Ölçüler UI_Ayarlar'dan ve dil ekranından birebir alındı: aynı pencerenin bir sonraki sayfası.
        private const float PanelWidth = 976f;
        // Arkadaki ayarlar penceresiyle AYNI yükseklik. Kısa bir panel denendiğinde altından ayarların
        // son satırları görünüyordu; karartma bir pencereyi silmeye yetmiyor, üstünü örtmek gerekiyor.
        private const float PanelHeight = 1782f;
        private const float PanelTop = -430f;
        private const float LandscapePanelWidth = 1900f;
        private const float LandscapePanelHeight = 900f;

        private const float RibbonWidth = 980f;
        private const float RibbonHeight = 230f;
        private const float RibbonDrop = -33f;
        private const float HeaderHeight = 100f;
        private const float HeaderInset = 150f;
        private const float CloseSize = 140f;
        private const float CloseRight = -94f;
        private const float CloseTop = -276f;

        private const float SidePad = 60f;
        private const float FirstRowTop = -162f;    // ayarlardaki ilk satırın başladığı yer
        private const float RowHeight = 170f;
        private const float RowGap = 12f;
        private const float PillWidth = 300f;
        private const float PillHeight = 108f;
        private const float PillRight = -22f;
        private const float LabelLeft = 34f;
        private const float CopiedSeconds = 2.4f;

        private static readonly Color Ink = new Color32(0x2A, 0x3A, 0x5C, 0xFF);
        private static readonly Color Dim = new Color(0f, 0f, 0f, 0.88f);
        private static readonly Color Faint = new Color32(0x6B, 0x76, 0x8C, 0xFF);

        private LanguageMenuUI.Skin _skin;
        private bool _art;
        private string _email;
        private Link[] _links;

        private RectTransform _root;
        private TMP_Text _title;
        private TMP_Text _idText;
        private TMP_Text _copyLabel;
        private string _playerId = "";
        private Coroutine _copied;

        /// <summary>Opens the page, building it the first time. <paramref name="email"/> and
        /// <paramref name="links"/> come straight off the settings screen's Inspector.</summary>
        public void Show(LanguageMenuUI.Skin skin, string email, Link[] links)
        {
            if (_root == null)
            {
                _skin = skin;
                _email = email;
                _links = links;
                Build();
            }
            if (_root == null) return;

            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            Paint();
        }

        public void Hide()
        {
            if (_root != null) _root.gameObject.SetActive(false);
        }

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;

        // ------------------------------------------------------------------ building

        private void Build()
        {
            _art = _skin.Row != null;
            if (_skin.Font == null) _skin.Font = FindFont();

            // Kök tam kanama, içerik güvenli alanda — dil ekranıyla aynı gerekçe: karartmanın işi
            // arkada ne varsa hepsini götürmek, panelin işi çentiğin altına girmemek.
            _root = Full(transform, "UI_Destek");

            RectTransform dim = Full(_root, "Karartma");
            var dimArt = dim.gameObject.AddComponent<Image>();
            dimArt.color = Dim;
            var dimBtn = dim.gameObject.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(Hide);

            RectTransform icerik = Full(_root, "GuvenliAlan");
            icerik.gameObject.AddComponent<SafeArea>();

            bool landscape = Screen.width > Screen.height;
            var panelGO = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(PanelOpenFx));
            var panel = (RectTransform)panelGO.transform;
            panel.SetParent(icerik, false);
            panel.anchorMin = panel.anchorMax = panel.pivot = landscape
                ? new Vector2(0.5f, 0.5f)
                : new Vector2(0.5f, 1f);
            panel.sizeDelta = landscape
                ? new Vector2(LandscapePanelWidth, LandscapePanelHeight)
                : new Vector2(PanelWidth, PanelHeight);
            panel.anchoredPosition = landscape ? Vector2.zero : new Vector2(0f, PanelTop);
            Art(panelGO.GetComponent<Image>(), _art ? _skin.Panel : UiSkin.Panel,
                _art ? Color.white : new Color(0.10f, 0.13f, 0.20f, 1f));
            panelGO.AddComponent<Button>().transition = Selectable.Transition.None;

            BuildRows(panel, landscape);
            BuildRibbon(panel, landscape);
            BuildClose(icerik, panel, landscape);

            _root.gameObject.SetActive(false);
            UiPanelSound.Attach(_root.gameObject);
        }

        private void BuildRows(RectTransform panel, bool landscape)
        {
            float width = (landscape ? LandscapePanelWidth : PanelWidth) - SidePad * 2f;
            float y = landscape ? -110f : FirstRowTop;

            if (SupportTicket.IsPlainAddress(_email))
            {
                RectTransform row = Row(panel, "SatirIletisim", width, ref y);
                Localize(Label(row, Loc.T("ayarlar.bize_ulasin"), width), "ayarlar.bize_ulasin");
                Localize(Pill(row, Loc.T("ayarlar.git"), OnContact), "ayarlar.git");
            }

            if (_links != null)
                for (int i = 0; i < _links.Length; i++)
                {
                    if (string.IsNullOrEmpty(_links[i].Url) || string.IsNullOrEmpty(_links[i].Label)) continue;
                    RectTransform row = Row(panel, "SatirBaglanti" + i, width, ref y);
                    Label(row, _links[i].Label, width);
                    string url = _links[i].Url;          // döngü değişkeni değil, kopyası kapansın
                    string label = _links[i].Label;
                    Localize(Pill(row, Loc.T("ayarlar.katil"), delegate { OnLink(label, url); }), "ayarlar.katil");
                }

            // Numara satırı her zaman kurulur: adres ve bağlantılar boş bırakılmış bir yapıda bile
            // oyuncunun destek yazarken verecek bir şeyi olsun.
            RectTransform idRow = Row(panel, "SatirNumara", width, ref y);

            // The only row with two lines in it, so the name is lifted out of the middle and the number
            // put under it. Left centred, the two boxes overlapped by a third of their height and the
            // number drew inside the word — which is not something a screenshot of a working build
            // makes obvious, because TMP had already dropped the line by then (see below).
            TMP_Text idLabel = Label(idRow, Loc.T("ayarlar.oyuncu_no"), width);
            var lrt = (RectTransform)idLabel.transform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 1f);
            lrt.pivot = new Vector2(0f, 1f);
            lrt.sizeDelta = new Vector2(width - PillWidth - LabelLeft - 40f, 62f);
            lrt.anchoredPosition = new Vector2(LabelLeft, -24f);
            Localize(idLabel, "ayarlar.oyuncu_no");

            // 64 TALL, NOT 56. TMP with Ellipsis overflow does not clip a line that is one unit too
            // tall for its box — it drops the line entirely and draws nothing. At 36pt this font's line
            // box is a shade under 56, which is how the id came out invisible with every property
            // otherwise correct: right text, right colour, enabled, on screen, and not drawn.
            _idText = Text(idRow, "Numara", "", 36f, TextAlignmentOptions.Left,
                           new Vector2(0f, 0f), new Vector2(0f, 0f));
            var irt = (RectTransform)_idText.transform;
            irt.pivot = new Vector2(0f, 0f);
            irt.sizeDelta = new Vector2(width - PillWidth - LabelLeft - 40f, 64f);
            irt.anchoredPosition = new Vector2(LabelLeft, 20f);
            _idText.color = Faint;
            _copyLabel = Pill(idRow, Loc.T("ayarlar.kopyala"), OnCopy);

            // Sürüm satırı panelin altında, satırların değil: bir düğme değil, bir künye.
            var version = Text(panel, "Surum", PlayerIdentity.VersionLine(), 30f, TextAlignmentOptions.Left,
                               new Vector2(0f, 0f), new Vector2(1f, 0f));
            var vrt = (RectTransform)version.transform;
            vrt.pivot = new Vector2(0.5f, 0f);
            vrt.offsetMin = new Vector2(SidePad + 6f, 30f);
            vrt.offsetMax = new Vector2(-SidePad - 6f, 86f);
            version.color = Faint;
        }

        /// <summary>One full-width row, placed under the last one. <paramref name="y"/> walks down.</summary>
        private RectTransform Row(RectTransform panel, string name, float width, ref float y)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(panel, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(width, RowHeight);
            rt.anchoredPosition = new Vector2(0f, y);
            y -= RowHeight + RowGap;

            var img = go.GetComponent<Image>();
            Art(img, _art ? _skin.Row : UiSkin.ButtonGrey, Color.white);
            img.raycastTarget = false;
            // Satır sanatının kenar payı satır yüksekliğinin beşte birine indiriliyor — dil ekranındaki
            // aynı hesap. Sabit bir çarpan, sanat değiştiği gün sessizce yanlış olurdu.
            img.pixelsPerUnitMultiplier = Multiplier(img.sprite, RowHeight);
            return rt;
        }

        /// <summary>The row's own name, on the left. Returned so a caller can hang a
        /// <see cref="LocalizedText"/> on it — a community link's name is authored, not a key.</summary>
        private TMP_Text Label(RectTransform row, string caption, float width)
        {
            var t = Text(row, "Ad", caption, 42f, TextAlignmentOptions.Left,
                         new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            var rt = (RectTransform)t.transform;
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(width - PillWidth - LabelLeft - 40f, 74f);
            rt.anchoredPosition = new Vector2(LabelLeft, 0f);
            // "BİZE ULAŞIN" ile "KONTAKTIERE UNS" arasında iki kat fark var, kutu ise sabit.
            t.enableAutoSizing = true;
            t.fontSizeMin = 26f;
            t.fontSizeMax = 42f;
            return t;
        }

        /// <summary>The row's action button. Returns its label so a caller can flip the caption.</summary>
        private TMP_Text Pill(RectTransform row, string caption, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Dugme", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(row, false);
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(PillWidth, PillHeight);
            rt.anchoredPosition = new Vector2(PillRight, 0f);

            var img = go.GetComponent<Image>();
            Art(img, UiSkin.ButtonGreen, Color.white);
            img.pixelsPerUnitMultiplier = Multiplier(img.sprite, PillHeight);
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            var label = Text(rt, "Yazi", caption, 38f, TextAlignmentOptions.Center, Vector2.zero, Vector2.one);
            label.color = Color.white;
            label.enableAutoSizing = true;
            label.fontSizeMin = 24f;
            label.fontSizeMax = 38f;
            return label;
        }

        private void BuildRibbon(RectTransform panel, bool landscape)
        {
            if (_skin.HeaderBar)
            {
                var band = (RectTransform)new GameObject("Baslik", typeof(RectTransform)).transform;
                band.SetParent(panel, false);
                band.anchorMin = new Vector2(0f, 1f);
                band.anchorMax = new Vector2(1f, 1f);
                band.pivot = new Vector2(0.5f, 1f);
                band.offsetMin = new Vector2(HeaderInset, -HeaderHeight);
                band.offsetMax = new Vector2(-HeaderInset, 0f);
                _title = Text(band, "Yazi", Loc.T("ayarlar.destek"), 58f, TextAlignmentOptions.Center,
                              Vector2.zero, Vector2.one);
                _title.color = Color.white;
                return;
            }

            var go = new GameObject("Serit", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(panel, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(RibbonWidth, RibbonHeight);
            rt.anchoredPosition = new Vector2(0f, landscape ? -18f : RibbonDrop);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            Art(img, _art ? _skin.Ribbon : UiSkin.ButtonYellow, Color.white);
            _title = Text(rt, "Baslik", Loc.T("ayarlar.destek"), 62f, TextAlignmentOptions.Center, Vector2.zero, Vector2.one);
            _title.color = Color.white;
            var trt = (RectTransform)_title.transform;
            trt.offsetMin = new Vector2(160f, 30f);
            trt.offsetMax = new Vector2(-160f, -10f);
        }

        /// <summary>Where every other X on this screen sits, for the same reason the language page gives:
        /// two taps close two pages without the finger moving.</summary>
        private void BuildClose(RectTransform icerik, RectTransform panel, bool landscape)
        {
            var go = new GameObject("BtnKapat", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_skin.HeaderBar ? panel : icerik, false);
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = _skin.HeaderBar
                ? new Vector2(HeaderHeight * 1.04f, HeaderHeight * 1.04f)
                : new Vector2(CloseSize, CloseSize);
            rt.anchoredPosition = _skin.HeaderBar ? new Vector2(-64f, -HeaderHeight * 0.5f)
                                 : landscape ? new Vector2(-82f, -82f)
                                 : new Vector2(CloseRight, CloseTop);
            var img = go.GetComponent<Image>();
            Art(img, _art ? _skin.Close : UiSkin.ButtonGrey, Color.white);
            var b = go.GetComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(Hide);
            if (!_art) Text(rt, "Yazi", "X", 54f, TextAlignmentOptions.Center, Vector2.zero, Vector2.one);
        }

        // ------------------------------------------------------------------ rows doing something

        /// <summary>
        /// The id, minted and written to disk the first time this page is opened — never here, in the
        /// screen. <see cref="PlayerIdentity.Ensure"/> owns that order.
        /// </summary>
        private void Paint()
        {
            if (_title != null) _title.text = Loc.T("ayarlar.destek");

            _playerId = PlayerIdentity.Ensure(ServiceLocator.Get<SaveData>(), ServiceLocator.Get<SaveService>());
            if (_idText != null) _idText.text = _playerId;
            if (_copyLabel != null && _copied == null) _copyLabel.text = Loc.T("ayarlar.kopyala");
        }

        private void OnContact()
        {
            string url = PlayerIdentity.Mailto(_email, _playerId);
            if (string.IsNullOrEmpty(url)) return;
            ServiceLocator.Get<IAnalytics>()?.Log("settings_contact_opened");
            Application.OpenURL(url);
        }

        private void OnLink(string label, string url)
        {
            ServiceLocator.Get<IAnalytics>()?.Log("settings_community_opened", "link", label);
            Application.OpenURL(url);
        }

        /// <summary>
        /// Copies the id and says so. The clipboard reports nothing back on Android, so the label is the
        /// only feedback there is — and it goes back to KOPYALA on its own, because a button stuck on
        /// "copied" reads as one that cannot be pressed again.
        /// </summary>
        private void OnCopy()
        {
            if (string.IsNullOrEmpty(_playerId)) return;
            PlayerIdentity.Copy(_playerId);
            ServiceLocator.Get<IAnalytics>()?.Log("settings_id_copied");
            ServiceLocator.Get<HapticService>()?.Light();

            if (_copyLabel == null) return;
            _copyLabel.text = Loc.T("ayarlar.kopyalandi");
            if (_copied != null) StopCoroutine(_copied);
            _copied = StartCoroutine(ResetCopyLabel());
        }

        private IEnumerator ResetCopyLabel()
        {
            yield return new WaitForSecondsRealtime(CopiedSeconds);
            if (_copyLabel != null) _copyLabel.text = Loc.T("ayarlar.kopyala");
            _copied = null;
        }

        // ------------------------------------------------------------------ small builders

        /// <summary>
        /// Makes a label follow a language change on its own. Every caption on this page carries one
        /// EXCEPT the copy button's: that one is written by code as it flips to KOPYALANDI and back, and
        /// two writers on one label would race — which is the rule <see cref="LocalizedText"/> states.
        /// </summary>
        private static void Localize(TMP_Text label, string key)
        {
            if (label != null) label.gameObject.AddComponent<LocalizedText>().SetKey(key);
        }

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

        /// <summary>Shrinks a sliced sprite's border to about a fifth of the box it is drawn in, so the
        /// top and bottom slices of a 60-unit-bordered row sprite stop overlapping.</summary>
        private static float Multiplier(Sprite sprite, float height)
        {
            if (sprite == null) return 1f;
            float border = Mathf.Max(sprite.border.y, sprite.border.w);
            if (border <= 0f) return 1f;
            return Mathf.Max(1f, border / (height * 0.21f));
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
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        private static TMP_FontAsset FindFont()
        {
            var any = FindObjectsByType<TMP_Text>(FindObjectsInactive.Include);
            for (int i = 0; i < any.Length; i++)
                if (any[i].font != null) return any[i].font;
            return null;
        }
    }
}
