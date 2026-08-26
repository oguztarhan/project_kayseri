using Game.Core;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// What the player needs on deck: which route this is, how far through it she is, when she is
    /// home, and the way back ashore.
    ///
    /// Four things and no more. The sea is a place to WATCH in S1 — every control that could exist
    /// here would be a control over a voyage the player is not allowed to change
    /// (Docs/FIVE_LAYERS.md §4), so the only button is the one that leaves. S2's abilities are the
    /// first thing that earns a place beside it.
    ///
    /// TMP rather than <see cref="UiBuild.Label"/>, for the reason VoyageUI gives: the market-side
    /// screens are set in Baloo2 and one Arial glyph among them is exactly the one that shows.
    /// </summary>
    public sealed class SeaHudUI : MonoBehaviour
    {
        [SerializeField] private int sortingOrder = 100;
        [SerializeField] private float refreshInterval = 0.25f;

        private static readonly Color Chrome = new Color(0.06f, 0.10f, 0.16f, 0.88f);
        private static readonly Color Fill = new Color(0.36f, 0.74f, 0.99f, 0.95f);

        private ExpeditionService _sea;
        private TMP_Text _route, _clock;
        private RectTransform _progressFill;
        private float _timer;
        private string _lastRoute, _lastClock;

        public void Build(ExpeditionService sea, System.Action onAshore)
        {
            _sea = sea;
            RectTransform canvas = UiBuild.Canvas(transform, "DenizKanvas", sortingOrder);

            RectTransform bar = Plate(canvas, new Vector2(0.205f, 0.885f), new Vector2(0.635f, 0.985f));
            _route = Line(bar, "Rota", 34f, 0.52f, 1f);
            _clock = Line(bar, "Saat", 26f, 0.04f, 0.50f);

            // The crossing bar sits under the caption rather than at the foot of the screen: it and the
            // words it explains are one reading, and splitting them across the whole display makes the
            // player hunt for the half they did not look at first.
            RectTransform track = UiBuild.Bar(canvas, "Yol", new Color(0f, 0f, 0f, 0.45f), Fill,
                                              new Vector2(0.225f, 0.862f), new Vector2(0.615f, 0.882f),
                                              out _progressFill);
            track.GetComponent<Image>().raycastTarget = false;
            _progressFill.GetComponent<Image>().raycastTarget = false;

            Button ashore = UiBuild.Btn(canvas, "Karaya", Loc.T("deniz.karaya"),
                                        UiSkin.ButtonGrey, Chrome, 28,
                                        () => { ServiceLocator.Get<HapticService>()?.Medium(); onAshore?.Invoke(); });
            UiBuild.Anchor((RectTransform)ashore.transform, new Vector2(0.030f, 0.885f),
                           new Vector2(0.185f, 0.975f));
            PillFit.Wrap(ashore.GetComponent<Image>());

            Refresh();
        }

        private void Update()
        {
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = refreshInterval;
            Refresh();
        }

        private void Refresh()
        {
            if (_sea == null || _route == null) return;

            if (!_sea.Active)
            {
                Push(_route, Loc.T("deniz.gemiYok"), ref _lastRoute);
                Push(_clock, string.Empty, ref _lastClock);
                _progressFill.anchorMax = new Vector2(0f, 1f);
                return;
            }

            string leg = Loc.T(_sea.Outbound ? "deniz.gidis" : "deniz.donus");
            Push(_route, Loc.T("sefer.rota" + _sea.Tier) + "   ·   " + leg, ref _lastRoute);
            Push(_clock, string.Format(Loc.T("deniz.varis"), Clock(_sea.SecondsLeft)), ref _lastClock);
            _progressFill.anchorMax = new Vector2(Mathf.Clamp01((float)_sea.Progress), 1f);
        }

        /// <summary>
        /// Hours and minutes, or minutes and seconds under the hour. A far reach is measured in hours
        /// and <see cref="UiBuild.Clock"/>'s mm:ss would read as a four-figure minute count.
        /// </summary>
        private static string Clock(double seconds)
        {
            if (seconds < 0d) seconds = 0d;
            int total = Mathf.CeilToInt((float)seconds);
            if (total >= 3600) return string.Format(Loc.T("deniz.saatDk"), total / 3600, (total % 3600) / 60);
            return (total / 60) + ":" + (total % 60).ToString("00");
        }

        private static void Push(TMP_Text label, string value, ref string last)
        {
            if (label == null || value == last) return;
            label.text = value;
            last = value;
        }

        private static RectTransform Plate(RectTransform parent, Vector2 aMin, Vector2 aMax)
        {
            RectTransform rt = UiBuild.Flat(parent, "Levha", Chrome, aMin, aMax);
            var img = rt.GetComponent<Image>();
            img.sprite = UiSkin.Panel != null ? UiSkin.Panel : UiSkin.Flat;
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            return rt;
        }

        private static TMP_Text Line(Transform parent, string name, float size, float bottom, float top)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.enableAutoSizing = true;
            text.fontSizeMin = size * 0.5f;
            text.fontSizeMax = size;
            text.raycastTarget = false;
            UiBuild.Anchor((RectTransform)go.transform, new Vector2(0.04f, bottom), new Vector2(0.96f, top));
            return text;
        }
    }
}
