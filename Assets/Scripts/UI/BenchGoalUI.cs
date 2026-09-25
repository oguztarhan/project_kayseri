using Game.Core;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The small goal card at the top of the shop screen: the next bench star worth chasing, how far that bench has
    /// got towards it, and the gems it pays. Tapping it opens that bench's card. The bench is
    /// <see cref="MiningShopBusinessService.NextStarBench"/>'s pick; the card hides during the tutorial and once every
    /// built bench has all its stars.
    ///
    /// The HUD runs compact, which never builds the objective banner, so this is its own small card rather than a
    /// state of that banner. It polls once a second and rewrites its labels only when the bench, level or language
    /// has moved, so a settled card allocates nothing.
    /// </summary>
    public sealed class BenchGoalUI : MonoBehaviour
    {
        private MiningShopBusinessService _shop;
        private MiningShopUpgradeUI _card;
        private GameObject _root;
        private Text _title, _task, _count;
        private RectTransform _fill;
        private float _refreshSeconds = 1f, _timer;
        private int _bench = -2, _level = -1;

        /// <summary>Builds the card on <paramref name="parent"/>, a child of the shop's canvas, hidden until a goal exists.</summary>
        public static BenchGoalUI Create(RectTransform parent, MiningShopBusinessService shop, MiningShopUpgradeUI card,
                                         Vector2 min, Vector2 max, Color textColor, Color barTrack, Color barFill)
        {
            var go = new GameObject("TezgahHedefi", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            go.transform.SetParent(parent, false);
            UiBuild.Anchor((RectTransform)go.transform, Vector2.zero, Vector2.one);
            var goal = go.AddComponent<BenchGoalUI>();
            goal._shop = shop;
            goal._card = card;

            Button button = UiBuild.Btn(go.transform, "Kart", string.Empty, UiSkin.Panel, new Color(0.16f, 0.19f, 0.27f, 0.94f),
                20, goal.OnTap);
            var rt = (RectTransform)button.transform;
            UiBuild.Anchor(rt, min, max);
            Image img = button.GetComponent<Image>();
            img.type = Image.Type.Sliced;
            Text unused = button.GetComponentInChildren<Text>();
            if (unused != null) unused.gameObject.SetActive(false);
            goal._root = button.gameObject;
            Color text = UiSkin.HasArt ? textColor : Color.white;

            var star = new GameObject("Yildiz", typeof(RectTransform), typeof(Image));
            star.transform.SetParent(rt, false);
            Image starImg = star.GetComponent<Image>();
            Sprite starSprite = SeaKit.Get("yildiz");
            starImg.sprite = starSprite != null ? starSprite : UiSkin.Flat;
            starImg.color = starSprite != null ? Color.white : new Color(1f, 0.8f, 0.2f);
            starImg.preserveAspect = true;
            starImg.raycastTarget = false;
            UiBuild.Anchor((RectTransform)star.transform, new Vector2(0.03f, 0.18f), new Vector2(0.17f, 0.82f));

            goal._title = Row(rt, "Baslik", 26, TextAnchor.LowerLeft, text, new Vector2(0.2f, 0.64f), new Vector2(0.97f, 0.94f));
            goal._task = Row(rt, "Gorev", 22, TextAnchor.MiddleLeft, text, new Vector2(0.2f, 0.36f), new Vector2(0.97f, 0.64f));
            RectTransform track = UiBuild.Bar(rt, "Yatak", barTrack, barFill, new Vector2(0.2f, 0.1f), new Vector2(0.97f, 0.33f),
                out goal._fill);
            // Inside the card's own button: neither half of the bar may take the tap.
            track.GetComponent<Image>().raycastTarget = false;
            goal._fill.GetComponent<Image>().raycastTarget = false;
            goal._count = Row(track, "Sayi", 20, TextAnchor.MiddleCenter, Color.white, Vector2.zero, Vector2.one);
            goal._count.gameObject.AddComponent<Outline>().effectDistance = new Vector2(1.5f, -1.5f);

            goal._root.SetActive(false);
            return goal;
        }

        /// <summary>How often the card looks for a new goal (seconds).</summary>
        public void Configure(float refreshSeconds) => _refreshSeconds = Mathf.Max(0.25f, refreshSeconds);

        /// <summary>The language changed: redraw on the next look.</summary>
        public void MarkDirty()
        {
            _bench = -2;
            _timer = 0f;
        }

        private static Text Row(Transform parent, string name, int size, TextAnchor anchor, Color colour, Vector2 min, Vector2 max)
        {
            Text label = UiBuild.Label(parent, name, string.Empty, size, anchor);
            UiBuild.Anchor(label.rectTransform, min, max);
            label.color = colour;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 10;
            label.resizeTextMaxSize = size;
            return label;
        }

        private void Update()
        {
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = _refreshSeconds;

            int bench = TutorialUI.Blocking ? -1 : _shop.NextStarBench();
            int level = bench >= 0 ? _shop.View.ProductAt(bench).Level : -1;
            if (bench == _bench && level == _level) return;
            _bench = bench;
            _level = level;
            _root.SetActive(bench >= 0);
            if (bench < 0) return;

            int stars = BenchMastery.StarsAt(level);
            int next = BenchMastery.NextStarLevel(level);
            int from = stars > 0 ? BenchMastery.StarLevels[stars - 1] : BenchMastery.MinLevel;
            _title.text = Loc.T(MiningShopUpgradeUI.BenchTitleKey(bench));
            _task.text = string.Format(Loc.T("maden_dukkani.sonraki_yildiz"), stars + 1, next, _card.StarEffectText(stars)) +
                         "  ·  " + string.Format(Loc.T("reklam.elmas"), _shop.StarGems(stars));
            _count.text = level + " / " + next;
            _fill.anchorMax = new Vector2(Mathf.Clamp01((float)(level - from) / (next - from)), 1f);
        }

        private void OnTap()
        {
            if (_bench >= 0) _card.OpenBench(_bench);
        }
    }
}
