using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// Holds a portrait-authored screen at the size it was drawn against and scales it as one piece to
    /// fit whatever canvas it lands in, so the game can run landscape without every panel being
    /// re-laid-out first.
    ///
    /// The panels came out of Figma corner-anchored with absolute pixel offsets — a button 1400px down
    /// a 2340-tall sheet is 1400px down whatever it is parented to. Stretch that parent across a
    /// 1080-tall landscape canvas and the button is simply off the screen. Pinning the rect to the
    /// design size instead and scaling it uniformly keeps every one of those offsets pointing where it
    /// was drawn; the sheet just arrives smaller, centred, with the world visible either side.
    ///
    /// Goes on the content, never on the dimmer — <c>Karartma</c> stays stretched to the full screen so
    /// the darkening still covers everything behind the card.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public sealed class LetterboxRoot : MonoBehaviour
    {
        [Tooltip("The resolution this screen's children were laid out against — the CanvasScaler " +
                 "reference the Figma export used. Offsets inside stay true as long as this matches.")]
        [SerializeField] private Vector2 designSize = new Vector2(1080f, 2340f);

        private RectTransform _rect;
        private RectTransform _parent;
        private Vector2 _applied = new Vector2(-1f, -1f);

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _parent = _rect.parent as RectTransform;
            Apply();
        }

        private void OnEnable()
        {
            Apply();
        }

        private void Update()
        {
            // Size compare only — the same trick SafeArea uses, and it catches rotation and resolution
            // changes without needing a callback from the OS.
            if (_parent != null && _parent.rect.size != _applied) Apply();
        }

        private void Apply()
        {
            if (_rect == null) _rect = (RectTransform)transform;
            if (_parent == null) _parent = _rect.parent as RectTransform;
            if (_parent == null || designSize.x <= 0f || designSize.y <= 0f) return;

            Vector2 avail = _parent.rect.size;
            if (avail.x <= 0f || avail.y <= 0f) return;
            _applied = avail;

            _rect.anchorMin = Centre;
            _rect.anchorMax = Centre;
            _rect.pivot = Centre;
            _rect.anchoredPosition = Vector2.zero;
            _rect.sizeDelta = designSize;

            // Uniform, and the smaller of the two ratios — a landscape canvas is always the short way
            // up, so height wins and the card ends up full-height with bars left and right.
            float s = Mathf.Min(avail.x / designSize.x, avail.y / designSize.y);
            _rect.localScale = new Vector3(s, s, 1f);
        }

        private static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);
    }
}
