using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// A perfect sale over the shop counter: "PERFECT!" pops up with what the sale paid, and a handful of coins burst
    /// out of it and fall away. Presentation only — the wallet was paid before <see cref="Play"/> is called.
    ///
    /// The pops follow the counter's world point on screen, so they stay on it while the camera moves. A few are made
    /// up front on their own nested canvas and reused; when all are playing, the oldest is restarted. A pop costs no
    /// allocation but the cash string its caller formats.
    /// </summary>
    public sealed class PerfectSaleFx : MonoBehaviour
    {
        private const int Pops = 3;
        private const int MaxCoins = 10;
        private const float PopIn = 0.18f, FadeOut = 0.3f;

        private sealed class Pop
        {
            public RectTransform root;
            public CanvasGroup group;
            public Text title, cash;
            public RectTransform[] coins;
            public Vector2[] velocity;
            public Vector3 world;
            public float time = -1f;
        }

        private readonly Pop[] _pops = new Pop[Pops];
        private RectTransform _root;
        private Camera _camera;
        private float _seconds = 1.2f, _rise = 120f, _coinSpeed = 520f, _gravity = -1400f;
        private int _coins = 6;

        /// <summary>Builds the pops on <paramref name="parent"/>, a child of the shop's canvas, hidden until played.</summary>
        public static PerfectSaleFx Create(RectTransform parent, string title, Color titleColor, Color cashColor,
                                           float coinSize)
        {
            var go = new GameObject("MukemmelSatis", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(parent, false);
            var fx = go.AddComponent<PerfectSaleFx>();
            fx._root = UiBuild.Anchor((RectTransform)go.transform, Vector2.zero, Vector2.one);

            Sprite coin = UiSkin.Coin;
            for (int i = 0; i < Pops; i++)
            {
                var pop = new Pop { coins = new RectTransform[MaxCoins], velocity = new Vector2[MaxCoins] };
                var root = new GameObject("Pop" + i, typeof(RectTransform), typeof(CanvasGroup));
                root.transform.SetParent(fx._root, false);
                pop.root = Centred((RectTransform)root.transform, new Vector2(520f, 160f));
                pop.group = root.GetComponent<CanvasGroup>();
                pop.group.blocksRaycasts = false;
                pop.group.interactable = false;

                // Coins first, so the words draw over them.
                for (int c = 0; c < MaxCoins; c++)
                {
                    var g = new GameObject("Sikke" + c, typeof(RectTransform), typeof(Image));
                    g.transform.SetParent(pop.root, false);
                    Image img = g.GetComponent<Image>();
                    img.sprite = coin != null ? coin : UiSkin.Flat;
                    img.color = coin != null ? Color.white : new Color(1f, 0.8f, 0.2f);
                    img.preserveAspect = true;
                    img.raycastTarget = false;
                    pop.coins[c] = Centred((RectTransform)g.transform, new Vector2(coinSize, coinSize));
                }

                pop.title = UiBuild.Label(pop.root, "Baslik", title, 46, TextAnchor.MiddleCenter);
                pop.title.color = titleColor;
                pop.title.raycastTarget = false;
                pop.title.gameObject.AddComponent<Outline>().effectDistance = new Vector2(3f, -3f);
                UiBuild.Anchor(pop.title.rectTransform, new Vector2(0f, 0.45f), new Vector2(1f, 1f));
                pop.cash = UiBuild.Label(pop.root, "Tutar", string.Empty, 36, TextAnchor.MiddleCenter);
                pop.cash.color = cashColor;
                pop.cash.raycastTarget = false;
                pop.cash.gameObject.AddComponent<Outline>().effectDistance = new Vector2(2f, -2f);
                UiBuild.Anchor(pop.cash.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.5f));

                root.SetActive(false);
                fx._pops[i] = pop;
            }
            return fx;
        }

        /// <summary>How long a pop lasts, how far it rises, and how its coins fly.</summary>
        public void Configure(float seconds, float rise, int coins, float coinSpeed, float gravity)
        {
            _seconds = Mathf.Max(0.3f, seconds);
            _rise = rise;
            _coins = Mathf.Clamp(coins, 0, MaxCoins);
            _coinSpeed = coinSpeed;
            _gravity = gravity;
        }

        /// <summary>The title in the current language; the pops keep it until told again.</summary>
        public void SetTitle(string title)
        {
            for (int i = 0; i < _pops.Length; i++) _pops[i].title.text = title;
        }

        /// <summary>One perfect sale at <paramref name="world"/>, showing <paramref name="cash"/> under the title.</summary>
        public void Play(Vector3 world, string cash, Camera camera)
        {
            _camera = camera;
            if (_camera == null) return;
            // A free pop, or the one nearest its end.
            Pop pop = _pops[0];
            for (int i = 0; i < _pops.Length; i++)
            {
                if (_pops[i].time < 0f) { pop = _pops[i]; break; }
                if (_pops[i].time > pop.time) pop = _pops[i];
            }
            pop.world = world;
            pop.time = 0f;
            pop.cash.text = cash;
            for (int c = 0; c < MaxCoins; c++)
            {
                bool on = c < _coins;
                pop.coins[c].gameObject.SetActive(on);
                if (!on) continue;
                // An upward fan, a little different every time.
                float angle = Mathf.Lerp(25f, 155f, (c + Random.Range(0.1f, 0.9f)) / _coins) * Mathf.Deg2Rad;
                pop.velocity[c] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * _coinSpeed * Random.Range(0.75f, 1.15f);
            }
            pop.root.gameObject.SetActive(true);
            Apply(pop);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < _pops.Length; i++)
            {
                Pop pop = _pops[i];
                if (pop.time < 0f) continue;
                pop.time += dt;
                if (pop.time >= _seconds || _camera == null)
                {
                    pop.time = -1f;
                    pop.root.gameObject.SetActive(false);
                    continue;
                }
                Apply(pop);
            }
        }

        private void Apply(Pop pop)
        {
            Vector3 screen = _camera.WorldToScreenPoint(pop.world);
            // Behind the camera: nothing sensible to point at.
            pop.group.alpha = screen.z > 0f ? Alpha(pop.time) : 0f;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, null, out Vector2 at);
            float t = pop.time / _seconds;
            pop.root.anchoredPosition = at + new Vector2(0f, _rise * (1f - (1f - t) * (1f - t)));
            // Overshoots, then settles.
            float s = pop.time < PopIn ? Mathf.Lerp(0.4f, 1.2f, pop.time / PopIn)
                    : Mathf.Lerp(1.2f, 1f, Mathf.Clamp01((pop.time - PopIn) / PopIn));
            pop.title.rectTransform.localScale = Vector3.one * s;

            float time = pop.time;
            for (int c = 0; c < _coins; c++)
            {
                Vector2 v = pop.velocity[c];
                pop.coins[c].anchoredPosition = new Vector2(v.x * time, v.y * time + 0.5f * _gravity * time * time);
                pop.coins[c].localScale = Vector3.one * Mathf.Lerp(1f, 0.6f, t);
            }
        }

        private float Alpha(float time)
            => time > _seconds - FadeOut ? Mathf.Clamp01((_seconds - time) / FadeOut) : 1f;

        private static RectTransform Centred(RectTransform rt, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            return rt;
        }
    }
}
