using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// A few small stars twinkling forever over a card's art — what makes a pack read as treasure
    /// rather than as a picture of treasure. Each star pops in, spins a little and fades, then waits
    /// a random beat and reappears somewhere else, so the pattern never loops visibly.
    ///
    /// The stars are built once in Awake and reused; nothing allocates afterwards. While the card is
    /// outside the scroll viewport the whole set is switched off, so the twelve cells in the store
    /// only ever animate the six or so the player is actually looking at.
    /// </summary>
    public sealed class CardSparkle : MonoBehaviour
    {
        [SerializeField] private Sprite starSprite;
        [Tooltip("Kart başına aynı anda kaç yıldız yaşar.")]
        [SerializeField, Min(1)] private int count = 3;
        [SerializeField] private float minSize = 22f;
        [SerializeField] private float maxSize = 46f;
        [Tooltip("Bir yıldızın parlayıp sönme süresi.")]
        [SerializeField] private float lifeSeconds = 0.85f;
        [Tooltip("Sönen yıldızın yeniden doğmadan önceki bekleme aralığı.")]
        [SerializeField] private Vector2 gapSeconds = new Vector2(0.35f, 1.6f);
        [Tooltip("Ömrü boyunca dönme miktarı (derece).")]
        [SerializeField] private float spinDegrees = 55f;
        [SerializeField] private Color tint = new Color(1f, 0.98f, 0.88f, 1f);

        [Header("Alan (kart boyutunun oranı)")]
        [Tooltip("Yıldızların dağıldığı bölgenin genişlik/yükseklik oranı. Fiyat şeridini boş bırakmak için 1'den küçük tut.")]
        [SerializeField] private Vector2 areaSize = new Vector2(0.86f, 0.58f);
        [Tooltip("Bölgenin kart merkezinden kayması. +y yukarı.")]
        [SerializeField] private Vector2 areaOffset = new Vector2(0f, 0.16f);

        [Header("Görünürlük")]
        [Tooltip("Kart kaydırma penceresinden bu kadar px dışarıdayken yıldızlar tamamen kapanır.")]
        [SerializeField] private float cullMargin = 60f;

        private sealed class Star
        {
            public RectTransform rt;
            public Image img;
            public float t;          // negative while waiting to be reborn
            public float life;
            public float spin;
        }

        private RectTransform _rt;
        private RectTransform _viewport;
        private Star[] _stars;
        private bool _on = true;

        private void Awake()
        {
            _rt = (RectTransform)transform;
            ScrollRect scroll = GetComponentInParent<ScrollRect>();
            if (scroll != null)
                _viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;

            _stars = new Star[count];
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Yildiz", typeof(RectTransform), typeof(Image));
                go.layer = gameObject.layer;
                go.transform.SetParent(transform, false);
                var img = go.GetComponent<Image>();
                img.sprite = starSprite;
                img.color = tint;
                img.raycastTarget = false;
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                var star = new Star { rt = rt, img = img };
                _stars[i] = star;
                Respawn(star);
                // spread the first appearances out, or all three blink in unison on the first beat
                star.t = -(i * lifeSeconds * 0.6f);
            }
        }

        private void OnEnable()
        {
            // Recompiling while the editor is playing re-runs OnEnable without Awake, so the set built
            // there is gone. Skipping the reset is the whole fix: the stars resume on the next play.
            if (_stars == null) return;
            for (int i = 0; i < _stars.Length; i++)
            {
                Respawn(_stars[i]);
                _stars[i].t = -(i * lifeSeconds * 0.6f);
            }
        }

        private void Update()
        {
            if (_stars == null) return;
            bool visible = InView();
            if (visible != _on)
            {
                _on = visible;
                for (int i = 0; i < _stars.Length; i++) _stars[i].rt.gameObject.SetActive(visible);
            }
            if (!visible) return;

            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < _stars.Length; i++)
            {
                Star s = _stars[i];
                s.t += dt;
                if (s.t < 0f) { s.rt.localScale = Vector3.zero; continue; }
                if (s.t >= s.life)
                {
                    Respawn(s);
                    continue;
                }
                float p = s.t / s.life;
                // one clean swell: nothing at the ends, brightest in the middle
                float k = Mathf.Sin(p * Mathf.PI);
                float scale = k * k * (3f - 2f * k);
                s.rt.localScale = new Vector3(scale, scale, 1f);
                s.rt.localRotation = Quaternion.Euler(0f, 0f, s.spin * p);
                Color c = tint;
                c.a = tint.a * k;
                s.img.color = c;
            }
        }

        /// <summary>New spot, new size, new wait — a star never reappears where it just died.</summary>
        private void Respawn(Star s)
        {
            Rect r = _rt.rect;
            float w = r.width * areaSize.x * 0.5f;
            float h = r.height * areaSize.y * 0.5f;
            s.rt.anchoredPosition = new Vector2(
                r.width * areaOffset.x + Random.Range(-w, w),
                r.height * areaOffset.y + Random.Range(-h, h));
            float size = Random.Range(minSize, maxSize);
            s.rt.sizeDelta = new Vector2(size, size);
            s.rt.localScale = Vector3.zero;
            s.rt.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 90f));
            s.spin = Random.value < 0.5f ? spinDegrees : -spinDegrees;
            s.life = lifeSeconds;
            s.t = -Random.Range(gapSeconds.x, gapSeconds.y);
        }

        /// <summary>Cards scrolled off the viewport stop animating entirely.</summary>
        private bool InView()
        {
            if (_viewport == null) return true;
            Vector3 local = _viewport.InverseTransformPoint(_rt.position);
            Rect view = _viewport.rect;
            float half = _rt.rect.height * 0.5f;
            return local.y + half > view.yMin - cullMargin && local.y - half < view.yMax + cullMargin;
        }
    }
}
