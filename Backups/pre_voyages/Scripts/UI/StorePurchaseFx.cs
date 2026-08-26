using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Purchase celebration: a burst of coin or gem icons scatters off the bought card, arcs up to a
    /// gather point under the awning and fades — the store's answer to HudJuice's coin flight. The
    /// real HUD counters are hidden behind the store's opaque background, so the icons fly to a
    /// marker inside this screen instead. Pool-backed: a purchase allocates nothing after the first.
    /// PremiumStoreUI calls in from its purchase-success callbacks.
    /// </summary>
    public sealed class StorePurchaseFx : MonoBehaviour
    {
        [Header("Sahne")]
        [Tooltip("İkonların uçtuğu katman — panelin son çocuğu, kartların üstünde.")]
        [SerializeField] private RectTransform layer;
        [Tooltip("İkonların toplandığı nokta (tentenin altı).")]
        [SerializeField] private RectTransform gatherPoint;

        [Header("Uçuş")]
        [SerializeField] private Sprite coinSprite;
        [SerializeField] private Sprite gemSprite;
        [SerializeField, Min(1)] private int iconCount = 9;
        [SerializeField] private float iconSize = 66f;
        [SerializeField] private float flightSeconds = 0.85f;
        [Tooltip("Karttan ilk saçılmanın yarıçapı.")]
        [SerializeField] private float spread = 130f;

        private sealed class Fly
        {
            public RectTransform rt;
            public Image img;
            public float t;
            public Vector2 from, ctrl, to;
            public float spin;
        }

        private Pool<Fly> _pool;
        private readonly List<Fly> _live = new List<Fly>();

        private void Awake()
        {
            _pool = new Pool<Fly>(NewFly,
                f => f.rt.gameObject.SetActive(true),
                f => f.rt.gameObject.SetActive(false),
                iconCount);
        }

        public void PlayCash(RectTransform card) => Play(card, coinSprite);
        public void PlayGems(RectTransform card) => Play(card, gemSprite);

        private void Play(RectTransform card, Sprite sprite)
        {
            if (card == null || layer == null || gatherPoint == null || sprite == null) return;

            StoreCardFx cardFx = card.GetComponent<StoreCardFx>();
            if (cardFx != null) cardFx.Punch();

            Vector2 from = InLayer(card);
            Vector2 to = InLayer(gatherPoint);
            for (int i = 0; i < iconCount; i++)
            {
                Fly f = _pool.Get();
                f.img.sprite = sprite;
                f.img.color = Color.white;
                Vector2 at = from + new Vector2(Random.Range(-spread, spread), Random.Range(-spread * 0.5f, spread * 0.5f));
                f.from = at;
                f.to = to;
                // arc sideways-and-up so the icons read as tossed, not slid on rails
                f.ctrl = Vector2.Lerp(at, to, 0.45f) + new Vector2(Random.Range(-220f, 220f), Random.Range(60f, 240f));
                f.spin = Random.Range(-240f, 240f);
                f.t = -i * 0.03f;                        // slight stagger, so the burst has a body
                f.rt.anchoredPosition = at;
                f.rt.localRotation = Quaternion.identity;
                f.rt.localScale = new Vector3(0.4f, 0.4f, 1f);
                f.rt.SetAsLastSibling();
                _live.Add(f);
            }
        }

        /// <summary>A rect's centre in the flight layer's local space, whatever canvas it lives on.</summary>
        private Vector2 InLayer(RectTransform rect)
        {
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, screen, null, out Vector2 local);
            return local;
        }

        private void Update()
        {
            if (_live.Count == 0) return;
            float dt = Time.unscaledDeltaTime;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                Fly f = _live[i];
                f.t += dt / Mathf.Max(0.1f, flightSeconds);
                if (f.t < 0f) continue;
                if (f.t >= 1f)
                {
                    _live.RemoveAt(i);
                    _pool.Return(f);
                    continue;
                }
                // quadratic bezier from the scatter point, through the arc, into the gather point
                float u = 1f - f.t;
                f.rt.anchoredPosition = u * u * f.from + 2f * u * f.t * f.ctrl + f.t * f.t * f.to;
                f.rt.localRotation = Quaternion.Euler(0f, 0f, f.spin * f.t);
                float s = f.t < 0.15f ? Mathf.Lerp(0.4f, 1.15f, f.t / 0.15f) : Mathf.Lerp(1.15f, 0.55f, (f.t - 0.15f) / 0.85f);
                f.rt.localScale = new Vector3(s, s, 1f);
                Color c = f.img.color;
                c.a = f.t > 0.85f ? 1f - (f.t - 0.85f) / 0.15f : 1f;
                f.img.color = c;
            }
        }

        private Fly NewFly()
        {
            var go = new GameObject("Ucus", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(layer, false);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(iconSize, iconSize);
            go.SetActive(false);
            return new Fly { rt = rt, img = img };
        }
    }
}
