using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The one line the store says out loud when a sale does not happen.
    ///
    /// Before this, a refused purchase was completely silent: the card's callback returned early on
    /// failure and the panel simply sat there. Nothing on screen tells a player — or an App Review
    /// specialist — the difference between a store that cannot sell right now and a button that does
    /// not work at all, and the second reading is the one that comes back as a 2.1(b) rejection.
    ///
    /// Built in code rather than authored because it belongs to two panels, the store and the offer
    /// pop-up, and two authored copies drift apart the first time one of them is nudged.
    /// </summary>
    public sealed class StoreNotice : MonoBehaviour
    {
        private const float HoldSeconds = 2.6f;
        private const float FadeSeconds = 0.35f;

        private CanvasGroup _group;
        private TMP_Text _label;
        private float _left;

        /// <summary>
        /// Says <paramref name="message"/> at the bottom of <paramref name="panel"/>, replacing whatever
        /// that panel was saying before. The panel keeps one notice and reuses it.
        /// </summary>
        public static void Show(RectTransform panel, string message)
        {
            if (panel == null || string.IsNullOrEmpty(message)) return;
            StoreNotice notice = panel.GetComponentInChildren<StoreNotice>(true);
            if (notice == null) notice = Build(panel);
            notice.Say(message);
        }

        private void Awake() => _group = GetComponent<CanvasGroup>();

        private static StoreNotice Build(RectTransform panel)
        {
            var go = new GameObject("MagazaUyari", typeof(RectTransform), typeof(CanvasGroup),
                                    typeof(Image), typeof(LayoutElement), typeof(StoreNotice));
            var rt = (RectTransform)go.transform;
            rt.SetParent(panel, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(820f, 104f);
            rt.anchoredPosition = new Vector2(0f, 46f);
            // Over the cards whatever order the panel was authored in — a message drawn behind the
            // thing it is about explains nothing.
            rt.SetAsLastSibling();

            // Both panels this lands on are authored, and either may carry a layout group. Without
            // this the notice is dealt into the card grid as one more cell instead of sitting over it.
            go.GetComponent<LayoutElement>().ignoreLayout = true;

            var back = go.GetComponent<Image>();
            back.sprite = UiSkin.Panel;
            back.type = Image.Type.Sliced;
            // A bar across the panel that ate the next tap would be worse than saying nothing at all.
            back.raycastTarget = false;

            var textGo = new GameObject("Yazi", typeof(RectTransform), typeof(TextMeshProUGUI));
            var trt = (RectTransform)textGo.transform;
            trt.SetParent(rt, false);
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(28f, 14f);
            trt.offsetMax = new Vector2(-28f, -14f);

            var tmp = textGo.GetComponent<TextMeshProUGUI>();
            // The panel's own font, not TMP's default: eleven languages ship here and the default asset
            // has no Cyrillic or Vietnamese glyphs, so borrowing is the difference between a sentence
            // and a row of boxes.
            TMP_Text authored = panel.GetComponentInChildren<TMP_Text>(true);
            if (authored != null && authored.font != null) tmp.font = authored.font;
            tmp.fontSize = 34f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color32(0x2A, 0x3A, 0x5C, 0xFF);
            tmp.raycastTarget = false;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 20f;
            tmp.fontSizeMax = 34f;

            var notice = go.GetComponent<StoreNotice>();
            notice._label = tmp;
            go.SetActive(false);
            return notice;
        }

        private void Say(string message)
        {
            if (_label != null) _label.text = message;
            if (_group != null) _group.alpha = 1f;
            _left = HoldSeconds + FadeSeconds;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
        }

        private void Update()
        {
            if (_left <= 0f) return;
            _left -= Time.unscaledDeltaTime;
            if (_left <= 0f) { gameObject.SetActive(false); return; }
            if (_left < FadeSeconds && _group != null) _group.alpha = _left / FadeSeconds;
        }
    }
}
