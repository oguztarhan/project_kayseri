using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The moment a bench earns a star, on the bench card: the new star pops, a banner names the star and what it
    /// doubles, a "+N gems" line rises off the star, and the gems themselves fly from the star into the HUD's gem
    /// counter. Presentation only — the service has paid and saved before <see cref="Play"/> is called, so nothing
    /// here can lose or double a reward.
    ///
    /// Everything lives on its own nested canvas, so the frames it animates never rebuild the card around it, and
    /// every piece is made once up front and only moved, scaled and faded afterwards: a star costs no allocation but
    /// the two strings its caller formats.
    /// </summary>
    public sealed class BenchStarFx : MonoBehaviour
    {
        private const int MaxGems = 8;
        private const float GemLead = 0.25f;      // the gems leave once the star has started to pop
        private const float GemStagger = 0.06f;
        private const float FadeIn = 0.15f, FadeOut = 0.35f;

        private RectTransform _root;
        private CanvasGroup _bannerGroup;
        private RectTransform _banner;
        private Text _bannerText, _gain;
        private readonly RectTransform[] _gems = new RectTransform[MaxGems];
        private readonly Vector2[] _gemFrom = new Vector2[MaxGems];
        private readonly Vector2[] _gemCtrl = new Vector2[MaxGems];

        private float _bannerSeconds = 1.8f, _popSeconds = 0.5f, _flightSeconds = 0.7f, _gainRise = 90f;
        private float _time;
        private int _gemCount;
        private RectTransform _star;
        private Vector2 _starAt, _target;

        /// <summary>Builds the pieces on <paramref name="parent"/>, a child of the card's canvas, hidden until played.</summary>
        public static BenchStarFx Create(RectTransform parent, Color bannerTextColor, Color gainColor, float gemSize)
        {
            var go = new GameObject("YildizAnisi", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(parent, false);
            RectTransform root = UiBuild.Anchor((RectTransform)go.transform, Vector2.zero, Vector2.one);
            var fx = go.AddComponent<BenchStarFx>();
            fx._root = root;

            // Just above the card, which spans 0.17-0.34 of the screen, clear of the bench it names.
            fx._banner = UiBuild.Box(root, "Afis", new Color(0.12f, 0.14f, 0.2f, 0.95f),
                new Vector2(0.1f, 0.355f), new Vector2(0.9f, 0.425f));
            fx._banner.GetComponent<Image>().raycastTarget = false;
            fx._bannerGroup = fx._banner.gameObject.AddComponent<CanvasGroup>();
            fx._bannerGroup.blocksRaycasts = false;
            fx._bannerGroup.interactable = false;
            fx._bannerText = UiBuild.Label(fx._banner, "Yazi", string.Empty, 34, TextAnchor.MiddleCenter);
            fx._bannerText.color = UiSkin.HasArt ? bannerTextColor : Color.white;

            fx._gain = UiBuild.Label(root, "Kazanc", string.Empty, 38, TextAnchor.MiddleCenter);
            fx._gain.color = gainColor;
            fx._gain.gameObject.AddComponent<Outline>().effectDistance = new Vector2(2f, -2f);
            Centred(fx._gain.rectTransform, new Vector2(420f, 60f));

            Sprite gem = UiSkin.Gem;
            for (int i = 0; i < MaxGems; i++)
            {
                var g = new GameObject("Elmas" + i, typeof(RectTransform), typeof(Image));
                g.transform.SetParent(root, false);
                Image img = g.GetComponent<Image>();
                img.sprite = gem != null ? gem : UiSkin.Flat;
                img.color = gem != null ? Color.white : new Color(0.45f, 0.8f, 1f);
                img.preserveAspect = true;
                img.raycastTarget = false;
                fx._gems[i] = Centred((RectTransform)g.transform, new Vector2(gemSize, gemSize));
            }

            go.SetActive(false);
            return fx;
        }

        /// <summary>How long each part of the moment lasts.</summary>
        public void Configure(float bannerSeconds, float popSeconds, float flightSeconds, float gainRise)
        {
            _bannerSeconds = Mathf.Max(0.3f, bannerSeconds);
            _popSeconds = Mathf.Max(0.05f, popSeconds);
            _flightSeconds = Mathf.Max(0.1f, flightSeconds);
            _gainRise = gainRise;
        }

        /// <summary>
        /// Plays the moment for a star just paid. <paramref name="star"/> is the card's star image, which is popped in
        /// place and put back; <paramref name="target"/> is where the gems land, usually the HUD's gem counter — with
        /// none the gems are skipped and only the banner and the gain line show. A second star before the first
        /// finishes restarts the moment rather than stacking two.
        /// </summary>
        public void Play(RectTransform star, string banner, string gain, long gems, RectTransform target)
        {
            if (_star != null && _star != star) _star.localScale = Vector3.one;
            _root.gameObject.SetActive(true);
            _star = star;
            _time = 0f;
            _bannerText.text = banner;
            _gain.text = gain;
            _starAt = star != null ? LocalPoint(star) : Vector2.zero;

            _gemCount = target != null ? (int)Mathf.Clamp(gems, 3, MaxGems) : 0;
            if (_gemCount > 0) _target = LocalPoint(target);
            for (int i = 0; i < MaxGems; i++)
            {
                _gems[i].gameObject.SetActive(false);
                if (i >= _gemCount) continue;
                // Each gem leaves from a little scatter round the star and arcs high before dropping onto the counter.
                _gemFrom[i] = _starAt + new Vector2(Random.Range(-40f, 40f), Random.Range(-20f, 30f));
                _gemCtrl[i] = (_gemFrom[i] + _target) * 0.5f + new Vector2(Random.Range(-160f, 160f), Random.Range(180f, 320f));
            }
            Apply();
        }

        private void Update()
        {
            _time += Time.unscaledDeltaTime;
            Apply();
            if (_time >= Duration) Stop();
        }

        private float Duration => Mathf.Max(_bannerSeconds, GemLead + GemStagger * _gemCount + _flightSeconds);

        private void Apply()
        {
            if (_star != null)
            {
                float p = _time / _popSeconds;
                _star.localScale = Vector3.one * (p < 1f ? 1f + 0.7f * Mathf.Sin(p * Mathf.PI) : 1f);
            }

            float alpha = _time < FadeIn ? _time / FadeIn
                        : _time > _bannerSeconds - FadeOut ? Mathf.Clamp01((_bannerSeconds - _time) / FadeOut) : 1f;
            _bannerGroup.alpha = alpha;
            _banner.localScale = Vector3.one * (_time < FadeIn ? Mathf.Lerp(0.85f, 1f, _time / FadeIn) : 1f);

            float rise = Mathf.Clamp01(_time / _bannerSeconds);
            RectTransform gain = _gain.rectTransform;
            gain.anchoredPosition = _starAt + new Vector2(0f, 60f + _gainRise * rise);
            Color c = _gain.color;
            c.a = alpha;
            _gain.color = c;

            for (int i = 0; i < _gemCount; i++)
            {
                float t = (_time - GemLead - GemStagger * i) / _flightSeconds;
                bool flying = t > 0f && t < 1f;
                if (_gems[i].gameObject.activeSelf != flying) _gems[i].gameObject.SetActive(flying);
                if (!flying) continue;
                float e = t * t * (3f - 2f * t);
                float u = 1f - e;
                _gems[i].anchoredPosition = u * u * _gemFrom[i] + 2f * u * e * _gemCtrl[i] + e * e * _target;
                _gems[i].localScale = Vector3.one * Mathf.Lerp(1.15f, 0.6f, e);
            }
        }

        private void Stop()
        {
            if (_star != null) _star.localScale = Vector3.one;
            _star = null;
            _root.gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            // The card closing mid-moment must not leave its star swollen.
            if (_star != null) _star.localScale = Vector3.one;
        }

        /// <summary>A rect's centre in this layer's space, whichever canvas and render mode it lives on.</summary>
        private Vector2 LocalPoint(RectTransform rt)
        {
            Canvas canvas = rt.GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(rt.rect.center));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, null, out Vector2 local);
            return local;
        }

        private static RectTransform Centred(RectTransform rt, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            return rt;
        }
    }
}
