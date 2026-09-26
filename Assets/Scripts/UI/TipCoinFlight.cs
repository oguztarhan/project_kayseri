using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// A tip's coins flying from the shop counter into the HUD cash counter, on an up-and-over curve, shrinking as they
    /// go. Presentation only — the wallet was paid before <see cref="Play"/> is called, and the counter's own pop when
    /// cash arrives is what answers them.
    ///
    /// Screen-space overlay throughout, like the shop coin's flight: a rect's world position is its screen position, so
    /// the counter is read where it really sits (safe area and all) every frame. The coins are made once on their own
    /// nested canvas and reused; when all are flying, a new tip takes the oldest.
    /// </summary>
    public sealed class TipCoinFlight : MonoBehaviour
    {
        private const int MaxCoins = 12;

        private sealed class Coin
        {
            public RectTransform rt;
            public Vector2 from, ctrl;
            public RectTransform target;
            public float time = -1f;
        }

        private readonly Coin[] _coins = new Coin[MaxCoins];
        private Canvas _canvas;
        private float _seconds = 0.7f, _stagger = 0.06f, _arc = 140f, _spread = 30f;

        /// <summary>Builds the coins on <paramref name="parent"/>, hidden until played.</summary>
        public static TipCoinFlight Create(RectTransform parent, float coinSize)
        {
            var go = new GameObject("BahsisSikkeleri", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(parent, false);
            var fx = go.AddComponent<TipCoinFlight>();
            fx._canvas = go.GetComponent<Canvas>();
            RectTransform root = UiBuild.Anchor((RectTransform)go.transform, Vector2.zero, Vector2.one);
            Sprite coin = UiSkin.Coin;
            for (int i = 0; i < MaxCoins; i++)
            {
                var g = new GameObject("Sikke" + i, typeof(RectTransform), typeof(Image));
                g.transform.SetParent(root, false);
                Image img = g.GetComponent<Image>();
                img.sprite = coin != null ? coin : UiSkin.Flat;
                img.color = coin != null ? Color.white : new Color(1f, 0.8f, 0.2f);
                img.preserveAspect = true;
                img.raycastTarget = false;
                var rt = (RectTransform)g.transform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(coinSize, coinSize);
                g.SetActive(false);
                fx._coins[i] = new Coin { rt = rt };
            }
            return fx;
        }

        /// <summary>How long one coin takes, the gap between coins, how high they arc and how far apart they start (px).</summary>
        public void Configure(float seconds, float stagger, float arc, float spread)
        {
            _seconds = Mathf.Max(0.1f, seconds);
            _stagger = Mathf.Max(0f, stagger);
            _arc = arc;
            _spread = spread;
        }

        /// <summary>
        /// <paramref name="count"/> coins from <paramref name="world"/> to <paramref name="target"/>, the first after
        /// <paramref name="delay"/> seconds. Nothing flies without a target or with the point behind the camera.
        /// </summary>
        public void Play(Vector3 world, Camera camera, RectTransform target, int count, float delay)
        {
            if (target == null || camera == null) return;
            Vector3 screen = camera.WorldToScreenPoint(world);
            if (screen.z <= 0f) return;
            float scale = _canvas != null && _canvas.rootCanvas.scaleFactor > 0f ? _canvas.rootCanvas.scaleFactor : 1f;
            for (int n = 0; n < count; n++)
            {
                Coin coin = Take();
                coin.target = target;
                coin.from = (Vector2)screen + new Vector2(Random.Range(-_spread, _spread), Random.Range(-_spread, _spread) * 0.5f) * scale;
                coin.ctrl = coin.from + new Vector2(Random.Range(-1f, 1f) * _arc * 0.5f, _arc) * scale;
                coin.time = -(Mathf.Max(0f, delay) + n * _stagger);
                coin.rt.position = coin.from;
                coin.rt.localScale = Vector3.one;
                coin.rt.gameObject.SetActive(false);
            }
        }

        /// <summary>An idle coin, or the one nearest its end.</summary>
        private Coin Take()
        {
            Coin pick = _coins[0];
            for (int i = 0; i < _coins.Length; i++)
            {
                Coin c = _coins[i];
                if (c.target == null) return c;
                if (c.time > pick.time) pick = c;
            }
            return pick;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < _coins.Length; i++)
            {
                Coin c = _coins[i];
                if (c.target == null) continue;
                c.time += dt;
                if (c.time < 0f) continue;
                float t = c.time / _seconds;
                if (t >= 1f)
                {
                    c.target = null;
                    c.rt.gameObject.SetActive(false);
                    continue;
                }
                if (!c.rt.gameObject.activeSelf) c.rt.gameObject.SetActive(true);
                Vector2 to = c.target.TransformPoint(c.target.rect.center);
                float e = t * t * (3f - 2f * t);
                float u = 1f - e;
                c.rt.position = u * u * c.from + 2f * u * e * c.ctrl + e * e * to;
                c.rt.localScale = Vector3.one * Mathf.Lerp(1f, 0.55f, e);
            }
        }
    }
}
