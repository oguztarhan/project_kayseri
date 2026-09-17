using Game.Core;
using Game.Data;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// A shared, code-built detail sheet for masters and captains.
    ///
    /// It carries two things the small grid cards cannot. A SKILLS BLOCK, because a master has three
    /// of them and fifteen cards five across have room for one line apiece — the headline goes on the
    /// card, the full three go here. And a SECOND BUTTON, because posting a master to his station is a
    /// different decision from spending cards on a star and putting both on one control would make the
    /// commoner action steal the rarer one. Captains use neither and pass nothing.
    /// </summary>
    public sealed class RosterInspectPanel
    {
        private readonly RectTransform _overlay;
        private readonly Text _title;
        private readonly Text _identity;
        private readonly Text _current;
        private readonly Text _next;
        private readonly Text _progress;
        private readonly Text _status;
        private readonly Text _skills;
        private readonly Button _action;
        private readonly Text _actionText;
        private readonly Button _second;
        private readonly Text _secondText;

        public RosterInspectPanel(RectTransform parent)
        {
            _overlay = UiBuild.Flat(parent, "KadroDetayKarartma", new Color(0.02f, 0.03f, 0.06f, 0.88f),
                                    Vector2.zero, Vector2.one);
            var dismiss = _overlay.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(Hide);

            // Use the same centred, portrait-safe panel treatment as Settings and More. Fixed
            // reference units keep the sheet balanced on both phone aspect ratios.
            RectTransform sheet = UiBuild.Box(_overlay, "KadroDetay", Color.white,
                                               new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            sheet.pivot = new Vector2(0.5f, 0.5f);
            sheet.anchoredPosition = Vector2.zero;
            sheet.sizeDelta = new Vector2(900f, 1272f);
            Image sheetImage = sheet.GetComponent<Image>();
            Sprite panelArt = PortraitUiArt.Get("settings-settings-panel");
            if (panelArt != null) PortraitUiArt.Apply(sheetImage, panelArt);
            else { sheetImage.sprite = UiSkin.Panel; sheetImage.type = Image.Type.Sliced; }
            sheetImage.raycastTarget = true;
            // Stops a tap inside the sheet from reaching the dismiss layer.
            var blocker = sheet.gameObject.AddComponent<Button>();
            blocker.transition = Selectable.Transition.None;

            // Keep the stable child name used by CardCollectionUI when it reuses this shared
            // inspect sheet for collection previews.
            var titleSlot = new GameObject("Baslik", typeof(RectTransform), typeof(Image));
            RectTransform titleRect = (RectTransform)titleSlot.transform;
            titleRect.SetParent(sheet, false);
            titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.sizeDelta = new Vector2(650f, 162f);
            titleRect.anchoredPosition = new Vector2(0f, -96f);
            Sprite titleArt = PortraitUiArt.Get("general-title-plate");
            if (titleArt != null) PortraitUiArt.Apply(titleSlot.GetComponent<Image>(), titleArt);
            else titleSlot.GetComponent<Image>().enabled = false;
            _title = UiBuild.Label(titleRect, "Text", string.Empty, 38, TextAnchor.MiddleCenter);
            _title.color = new Color(0.09f, 0.14f, 0.24f, 1f);
            Fit(_title, 20, 38);

            _identity = Label(sheet, "Kimlik", 25, new Vector2(0.08f, 0.71f), new Vector2(0.92f, 0.79f));
            _identity.color = new Color(0.09f, 0.14f, 0.24f, 1f);

            // A deep-blue information card gives the dense skills/effects copy a clear reading
            // surface while retaining the rounded cyan frame used throughout the current UI.
            var infoCard = new GameObject("BilgiKart", typeof(RectTransform), typeof(Image));
            RectTransform infoRect = (RectTransform)infoCard.transform;
            infoRect.SetParent(sheet, false);
            UiBuild.Anchor(infoRect, new Vector2(0.075f, 0.335f), new Vector2(0.925f, 0.68f));
            Sprite infoArt = PortraitUiArt.Get("general-small-card-panel");
            if (infoArt != null) PortraitUiArt.Apply(infoCard.GetComponent<Image>(), infoArt);
            else infoCard.GetComponent<Image>().color = new Color(0.05f, 0.20f, 0.42f, 1f);
            infoCard.GetComponent<Image>().raycastTarget = false;

            // Current and next are intentionally multiline: captain details show the sea effect and
            // the separate idle-income bonus together, while master details continue to use the skills block.
            _current = Label(sheet, "Mevcut", 25, new Vector2(0.10f, 0.50f), new Vector2(0.90f, 0.65f));
            _next = Label(sheet, "Sonraki", 22, new Vector2(0.10f, 0.36f), new Vector2(0.90f, 0.50f));

            // The three skills take the same band the current/next pair does, because only one of the
            // two ever shows: a master has three numbers to read and a captain has a before and after.
            _skills = Label(sheet, "Beceriler", 24, new Vector2(0.10f, 0.385f), new Vector2(0.90f, 0.64f));
            _skills.alignment = TextAnchor.MiddleLeft;
            _skills.color = Color.white;
            _current.color = Color.white;
            _next.color = Color.white;

            _progress = Label(sheet, "Ilerleme", 23, new Vector2(0.08f, 0.235f), new Vector2(0.92f, 0.325f));
            _status = Label(sheet, "Durum", 23, new Vector2(0.08f, 0.155f), new Vector2(0.92f, 0.235f));

            _action = UiBuild.Btn(sheet, "Aksiyon", string.Empty, UiSkin.ButtonGreen,
                                  new Color(0.24f, 0.68f, 0.36f, 1f), 27, null);
            ApplyActionArt(_action.GetComponent<Image>());
            _actionText = _action.GetComponentInChildren<Text>();
            Fit(_actionText, 15, 27);
            _actionText.color = new Color(0.10f, 0.14f, 0.22f, 1f);

            _second = UiBuild.Btn(sheet, "Ikincil", string.Empty, UiSkin.ButtonGrey,
                                  new Color(0.28f, 0.40f, 0.62f, 1f), 27, null);
            ApplyActionArt(_second.GetComponent<Image>());
            _secondText = _second.GetComponentInChildren<Text>();
            Fit(_secondText, 15, 27);
            _secondText.color = new Color(0.10f, 0.14f, 0.22f, 1f);
            _second.gameObject.SetActive(false);

            var closeGo = new GameObject("BtnKapat", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform closeRect = (RectTransform)closeGo.transform;
            closeRect.SetParent(sheet, false);
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(0.5f, 1f);
            closeRect.pivot = new Vector2(0.5f, 0.5f);
            closeRect.sizeDelta = new Vector2(92f, 92f);
            closeRect.anchoredPosition = new Vector2(395f, -64f);
            Image closeImage = closeGo.GetComponent<Image>();
            Sprite closeArt = PortraitUiArt.Get("general-close-button");
            if (closeArt != null) PortraitUiArt.Apply(closeImage, closeArt);
            else { closeImage.sprite = UiSkin.ButtonGrey; closeImage.type = Image.Type.Sliced; }
            Button close = closeGo.GetComponent<Button>();
            close.targetGraphic = closeImage;
            close.transition = Selectable.Transition.None;
            close.onClick.AddListener(Hide);

            _overlay.gameObject.SetActive(false);
        }

        public bool Visible => _overlay != null && _overlay.gameObject.activeSelf;

        public void Show(string title, string identity, string current, string next, string progress,
                         string status, string action, bool canAct, UnityAction onAction)
            => Show(title, identity, current, next, null, progress, status,
                    action, canAct, onAction, null, false, null);

        /// <summary>
        /// The full sheet. <paramref name="skills"/> non-empty swaps the current/next pair for a
        /// three-line block; <paramref name="second"/> non-empty adds the secondary button beside the
        /// action and narrows both to half the row.
        /// </summary>
        public void Show(string title, string identity, string current, string next, string skills,
                         string progress, string status, string action, bool canAct,
                         UnityAction onAction, string second, bool canSecond, UnityAction onSecond)
        {
            _title.text = title;
            _identity.text = identity;

            bool listing = !string.IsNullOrEmpty(skills);
            _skills.gameObject.SetActive(listing);
            _current.gameObject.SetActive(!listing);
            _next.gameObject.SetActive(!listing);
            if (listing) _skills.text = skills;
            else { _current.text = current; _next.text = next; }

            _progress.text = progress;
            _status.text = status;

            _actionText.text = action;
            _action.interactable = canAct;
            _action.onClick.RemoveAllListeners();
            if (canAct && onAction != null) _action.onClick.AddListener(onAction);

            bool paired = !string.IsNullOrEmpty(second);
            _second.gameObject.SetActive(paired);
            UiBuild.Anchor((RectTransform)_action.transform,
                           new Vector2(paired ? 0.07f : 0.20f, 0.055f),
                           new Vector2(paired ? 0.49f : 0.80f, 0.165f));
            if (paired)
            {
                UiBuild.Anchor((RectTransform)_second.transform,
                               new Vector2(0.51f, 0.055f), new Vector2(0.93f, 0.165f));
                _secondText.text = second;
                _second.interactable = canSecond;
                _second.onClick.RemoveAllListeners();
                if (canSecond && onSecond != null) _second.onClick.AddListener(onSecond);
            }

            _overlay.gameObject.SetActive(true);
            _overlay.SetAsLastSibling();
        }

        private static void ApplyActionArt(Image image)
        {
            Sprite art = PortraitUiArt.Get("general-primary-action-button");
            if (art != null) PortraitUiArt.Apply(image, art);
            else
            {
                image.sprite = UiSkin.ButtonGreen;
                image.type = Image.Type.Sliced;
            }
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
