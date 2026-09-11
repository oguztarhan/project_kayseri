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

        // Both multiply the kit's slate stat card: the track tinted down to deep blue, the fill left as
        // the card's own lighter slate so it reads against the track.
        private static readonly Color Track = new Color(0.22f, 0.34f, 0.50f, 1f);
        private static readonly Color Fill = new Color(0.85f, 1f, 1f, 1f);

        private ExpeditionService _sea;
        private TMP_Text _route, _clock;
        private RectTransform _progressFill;
        private float _timer;
        private string _lastRoute, _lastClock;

        public void Build(ExpeditionService sea, System.Action onAshore)
        {
            _sea = sea;
            RectTransform canvas = UiBuild.Canvas(transform, "DenizKanvas", sortingOrder);

            // Portrait puts the notch straight through the top strip, which landscape never did.
            // SafeArea is added AFTER the stretch — its Apply runs on Awake, so adding it first
            // would just have the stretch overwrite the insets it had already worked out.
            RectTransform safe = UiBuild.Anchor(
                (RectTransform)new GameObject("GuvenliAlan", typeof(RectTransform)).transform,
                Vector2.zero, Vector2.one);
            safe.SetParent(canvas, false);
            safe.gameObject.AddComponent<SafeArea>();

            // The kit's title plate. Its anchor medallion rides the top edge and dips into the body, so
            // the two lines sit in the lower two thirds: the route under the medallion, the clock under
            // that.
            RectTransform bar = SeaKit.Plate(safe, "Levha", "plaka", new Vector2(0.175f, 0.893f),
                                             new Vector2(0.985f, 0.985f));
            _route = Line(bar, "Rota", 34f, 0.36f, 0.60f);
            _clock = Line(bar, "Saat", 26f, 0.11f, 0.37f);

            // The crossing bar sits under the caption rather than at the foot of the screen: it and the
            // words it explains are one reading, and splitting them across the whole display makes the
            // player hunt for the half they did not look at first.
            // Drawn with the kit's rounded stat card, dark for the track and sky-blue for the fill, so the
            // bar has the same soft corners as everything else on the screen.
            Image track = SeaKit.Sliced(safe, "Yol", "stat", new Vector2(0.20f, 0.878f),
                                        new Vector2(0.96f, 0.889f), true);
            track.color = Track;
            Image fill = SeaKit.Sliced(track.rectTransform, "Fill", "stat", Vector2.zero, new Vector2(0f, 1f), true);
            fill.color = Fill;
            _progressFill = fill.rectTransform;

            // The kit's back arrow, square and uncaptioned: the arrow is the universal way out, and the
            // freed width went to the plate beside it.
            Image arrow = SeaKit.Square(safe, "Karaya", "geri", new Vector2(0.025f, 0.025f),
                                        new Vector2(0.905f, 0.975f), 0f);
            arrow.raycastTarget = true;
            var ashore = arrow.gameObject.AddComponent<Button>();
            ashore.targetGraphic = arrow;
            ashore.onClick.AddListener(() => { ServiceLocator.Get<HapticService>()?.Medium(); onAshore?.Invoke(); });

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
            UiBuild.Anchor((RectTransform)go.transform, new Vector2(0.09f, bottom), new Vector2(0.91f, top));
            return text;
        }
    }
}
