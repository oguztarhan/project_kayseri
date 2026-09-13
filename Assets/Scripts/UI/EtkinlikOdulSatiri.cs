using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// One reward, drawn the way the events screens draw rewards: a word in front (a token grant), then
    /// gems, master cards and charts each as the kit's icon beside its amount, then a word behind (a
    /// boost, cash minutes). Only the parts the reward actually carries are shown.
    ///
    /// LAID OUT BY A <see cref="HorizontalLayoutGroup"/>, not by fixed slots: "+30 Harbor Token" and
    /// "25" are not the same width, and evenly spaced slots would leave a gap after every short one.
    /// The group only rebuilds when a part turns on or off or a number actually changes — the setters
    /// here write nothing that is already there.
    /// </summary>
    public sealed class EtkinlikOdulSatiri
    {
        private readonly RectTransform _root;
        private readonly Text _lead, _tail;
        private readonly GameObject _gemPart, _cardPart, _chartPart;
        private readonly Text _gemAmount, _cardAmount, _chartAmount;

        private EtkinlikOdulSatiri(RectTransform root, Text lead, GameObject gemPart, Text gemAmount,
                                   GameObject cardPart, Text cardAmount, GameObject chartPart, Text chartAmount,
                                   Text tail)
        {
            _root = root;
            _lead = lead;
            _gemPart = gemPart;
            _gemAmount = gemAmount;
            _cardPart = cardPart;
            _cardAmount = cardAmount;
            _chartPart = chartPart;
            _chartAmount = chartAmount;
            _tail = tail;
        }

        public RectTransform Root => _root;

        public static EtkinlikOdulSatiri Create(RectTransform parent, string name, Vector2 min, Vector2 max,
                                                int size, Color ink, TextAnchor align)
        {
            RectTransform root = EtkinlikKit.Slot(parent, name, min, max);
            Arrange(root.gameObject.AddComponent<HorizontalLayoutGroup>(), align, Mathf.RoundToInt(size * 0.6f));

            Text lead = Word(root, "Bas", size, ink);
            GameObject gem = Part(root, "Elmas", EtkinlikKit.Gem, size, ink, out Text gemAmount);
            GameObject card = Part(root, "Kart", EtkinlikKit.MasterCard, size, ink, out Text cardAmount);
            GameObject chart = Part(root, "Harita", EtkinlikKit.Chart, size, ink, out Text chartAmount);
            Text tail = Word(root, "Son", size, ink);

            var row = new EtkinlikOdulSatiri(root, lead, gem, gemAmount, card, cardAmount, chart, chartAmount, tail);
            row.Set(null, 0L, 0, 0L, null);
            return row;
        }

        /// <summary>Any part left null or zero is hidden, and the rest close up around the gap.</summary>
        public void Set(string lead, long gems, int cards, long charts, string tail)
        {
            Show(_lead, lead);
            Show(_gemPart, _gemAmount, gems);
            Show(_cardPart, _cardAmount, cards);
            Show(_chartPart, _chartAmount, charts);
            Show(_tail, tail);
        }

        private static void Show(Text word, string value)
        {
            bool on = !string.IsNullOrEmpty(value);
            if (word.gameObject.activeSelf != on) word.gameObject.SetActive(on);
            if (on && word.text != value) word.text = value;
        }

        private static void Show(GameObject part, Text amount, long value)
        {
            bool on = value > 0L;
            if (part.activeSelf != on) part.SetActive(on);
            if (!on) return;
            string text = value.ToString();
            if (amount.text != text) amount.text = text;
        }

        private static void Arrange(HorizontalLayoutGroup group, TextAnchor align, int spacing)
        {
            group.childAlignment = align;
            group.spacing = spacing;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = true;
        }

        /// <summary>A word sized by its own text — <see cref="Text"/> reports its preferred width to the group.</summary>
        private static Text Word(Transform parent, string name, int size, Color ink)
        {
            Text word = UiBuild.Label(parent, name, string.Empty, size, TextAnchor.MiddleLeft);
            word.color = ink;
            return word;
        }

        private static GameObject Part(Transform parent, string name, Sprite icon, int size, Color ink, out Text amount)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Arrange(go.AddComponent<HorizontalLayoutGroup>(), TextAnchor.MiddleLeft, 4);

            var iconGo = new GameObject("Simge", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            iconGo.transform.SetParent(go.transform, false);
            var image = iconGo.GetComponent<Image>();
            image.sprite = icon;
            image.enabled = icon != null;
            image.preserveAspect = true;
            image.raycastTarget = false;
            var element = iconGo.GetComponent<LayoutElement>();
            element.minWidth = size * 1.35f;
            element.preferredWidth = size * 1.35f;
            element.flexibleWidth = 0f;

            amount = Word(go.transform, "Sayi", size, ink);
            return go;
        }
    }
}
