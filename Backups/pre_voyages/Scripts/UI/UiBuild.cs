using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The handful of uGUI builders every runtime-built screen needs: a canvas, a coloured box, a label,
    /// a button, a progress bar.
    ///
    /// <see cref="CoalHud"/>, <see cref="IslandMapUI"/> and <see cref="StationBadges"/> each grew their
    /// own private copies of these before this existed. Those are left alone deliberately — this is here
    /// so the meta-layer screens added afterwards do not make it five copies. Everything is anchored in
    /// fractions of the parent, so a screen laid out here scales to any aspect without a second pass.
    /// </summary>
    public static class UiBuild
    {
        private static Font _font;

        /// <summary>The built-in font, fetched once. Every runtime screen in the project uses it.</summary>
        public static Font Font
        {
            get
            {
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        /// <summary>
        /// A screen-space canvas at the portrait reference resolution the rest of the HUD is laid out
        /// against. <paramref name="sortingOrder"/> places it in the existing stack: badges 90, juice 95,
        /// HUD 100, world map 150.
        /// </summary>
        public static RectTransform Canvas(Transform parent, string name, int sortingOrder)
        {
            EnsureEventSystem(parent);
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var sc = go.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1080f, 1920f);
            sc.matchWidthOrHeight = 0.5f;
            return (RectTransform)go.transform;
        }

        /// <summary>Adds an EventSystem if the scene has none, so a runtime screen is clickable on its own.</summary>
        public static void EnsureEventSystem(Transform parent)
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            es.transform.SetParent(parent, false);
        }

        /// <summary>A panel filling its parent — the backing for a full-screen overlay.</summary>
        public static RectTransform Panel(Transform parent, string name, Color c)
        {
            return Box(parent, name, c, Vector2.zero, Vector2.one);
        }

        /// <summary>A coloured box anchored to a fraction of its parent.</summary>
        public static RectTransform Box(Transform parent, string name, Color c, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            // The kit art is pre-coloured, so a skinned panel takes the sprite as-is and only the
            // unskinned fallback gets tinted — tinting pre-coloured art just muddies it.
            img.sprite = UiSkin.Panel;
            img.type = Image.Type.Sliced;
            img.color = UiSkin.HasArt ? Color.white : c;
            return Anchor((RectTransform)go.transform, aMin, aMax);
        }

        /// <summary>A box that keeps its own colour even when a skin is wired — dimmers, bars, swatches.</summary>
        public static RectTransform Flat(Transform parent, string name, Color c, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = UiSkin.Flat;
            img.type = Image.Type.Sliced;
            img.color = c;
            return Anchor((RectTransform)go.transform, aMin, aMax);
        }

        public static Text Label(Transform parent, string name, string text, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = Font; t.text = text; t.fontSize = size; t.alignment = anchor;
            t.color = Color.white; t.fontStyle = FontStyle.Bold;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;                       // labels must never eat a tap meant for the button under them
            Anchor((RectTransform)go.transform, Vector2.zero, Vector2.one);
            return t;
        }

        public static Button Btn(Transform parent, string name, string text, Sprite sprite, Color tint,
                                 int size, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite != null ? sprite : UiSkin.Flat;
            img.type = Image.Type.Sliced;
            img.color = UiSkin.HasArt ? Color.white : tint;
            Label(go.transform, "Text", text, size, TextAnchor.MiddleCenter);
            var b = go.GetComponent<Button>();
            b.targetGraphic = img;
            if (onClick != null) b.onClick.AddListener(onClick);
            return b;
        }

        /// <summary>
        /// A left-to-right progress bar. Returns the track; <paramref name="fill"/> is the part to
        /// resize — drive it by setting <c>fill.anchorMax = new Vector2(t, 1f)</c>.
        /// </summary>
        public static RectTransform Bar(Transform parent, string name, Color track, Color fillColor,
                                        Vector2 aMin, Vector2 aMax, out RectTransform fill)
        {
            RectTransform bg = Flat(parent, name, track, aMin, aMax);
            fill = Flat(bg, "Fill", fillColor, Vector2.zero, new Vector2(0f, 1f));
            return bg;
        }

        /// <summary>Stretches a rect between two parent-space fractions with no pixel offsets.</summary>
        public static RectTransform Anchor(RectTransform rt, Vector2 aMin, Vector2 aMax)
        {
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>mm:ss for the countdowns on contracts and boosts.</summary>
        public static string Clock(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int total = Mathf.CeilToInt(seconds);
            return (total / 60) + ":" + (total % 60).ToString("00");
        }
    }
}
