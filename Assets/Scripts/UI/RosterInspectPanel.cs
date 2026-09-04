using Game.Core;
using Game.Data;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>A shared, code-built detail sheet for masters and captains.</summary>
    public sealed class RosterInspectPanel
    {
        private readonly RectTransform _overlay;
        private readonly Text _title;
        private readonly Text _identity;
        private readonly Text _current;
        private readonly Text _next;
        private readonly Text _progress;
        private readonly Text _status;
        private readonly Button _action;
        private readonly Text _actionText;

        public RosterInspectPanel(RectTransform parent)
        {
            _overlay = UiBuild.Flat(parent, "KadroDetayKarartma", new Color(0.02f, 0.03f, 0.06f, 0.88f),
                                    Vector2.zero, Vector2.one);
            var dismiss = _overlay.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(Hide);

            RectTransform sheet = UiBuild.Box(_overlay, "KadroDetay", new Color(0.15f, 0.18f, 0.26f, 1f),
                                               new Vector2(0.22f, 0.17f), new Vector2(0.78f, 0.83f));
            // Stops a tap inside the sheet from reaching the dismiss layer.
            var blocker = sheet.gameObject.AddComponent<Button>();
            blocker.transition = Selectable.Transition.None;

            _title = Label(sheet, "Baslik", 38, new Vector2(0.08f, 0.83f), new Vector2(0.92f, 0.96f));
            _identity = Label(sheet, "Kimlik", 25, new Vector2(0.08f, 0.69f), new Vector2(0.92f, 0.82f));
            _current = Label(sheet, "Mevcut", 28, new Vector2(0.08f, 0.53f), new Vector2(0.92f, 0.68f));
            _next = Label(sheet, "Sonraki", 24, new Vector2(0.08f, 0.39f), new Vector2(0.92f, 0.52f));
            _progress = Label(sheet, "Ilerleme", 23, new Vector2(0.08f, 0.28f), new Vector2(0.92f, 0.38f));
            _status = Label(sheet, "Durum", 23, new Vector2(0.08f, 0.19f), new Vector2(0.92f, 0.28f));

            _action = UiBuild.Btn(sheet, "Aksiyon", string.Empty, UiSkin.ButtonGreen,
                                  new Color(0.24f, 0.68f, 0.36f, 1f), 27, null);
            UiBuild.Anchor((RectTransform)_action.transform,
                           new Vector2(0.20f, 0.055f), new Vector2(0.80f, 0.165f));
            PillFit.Wrap(_action.GetComponent<Image>());
            _actionText = _action.GetComponentInChildren<Text>();
            Fit(_actionText, 15, 27);

            _overlay.gameObject.SetActive(false);
        }

        public bool Visible => _overlay != null && _overlay.gameObject.activeSelf;

        public void Show(string title, string identity, string current, string next, string progress,
                         string status, string action, bool canAct, UnityAction onAction)
        {
            _title.text = title;
            _identity.text = identity;
            _current.text = current;
            _next.text = next;
            _progress.text = progress;
            _status.text = status;
            _actionText.text = action;
            _action.interactable = canAct;
            _action.onClick.RemoveAllListeners();
            if (canAct && onAction != null) _action.onClick.AddListener(onAction);
            _overlay.gameObject.SetActive(true);
            _overlay.SetAsLastSibling();
        }

        public void Hide()
        {
            if (_overlay != null) _overlay.gameObject.SetActive(false);
        }

        private static Text Label(RectTransform parent, string name, int size, Vector2 min, Vector2 max)
        {
            Text label = UiBuild.Label(Slot(parent, name, min, max), "Text", string.Empty,
                                       size, TextAnchor.MiddleCenter);
            label.color = new Color(0.09f, 0.14f, 0.24f, 1f);
            Fit(label, Mathf.Max(12, size / 2), size);
            return label;
        }

        private static void Fit(Text label, int min, int max)
        {
            AccessibilityConfig accessibility = ServiceLocator.Get<AccessibilityConfig>();
            float scale = accessibility != null ? accessibility.TextScale : 1f;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = Mathf.Max(1, Mathf.RoundToInt(min * scale));
            label.resizeTextMaxSize = Mathf.Max(label.resizeTextMinSize, Mathf.RoundToInt(max * scale));
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private static RectTransform Slot(RectTransform parent, string name, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return UiBuild.Anchor((RectTransform)go.transform, min, max);
        }
    }
}
