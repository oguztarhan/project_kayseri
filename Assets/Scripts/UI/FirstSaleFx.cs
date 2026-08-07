using Game.Core;
using Game.Data;
using Game.Gameplay;
using Game.Systems;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The one time the game stops to say the chain worked. The first truck to reach the market is the
    /// moment everything the tutorial described turns into money, and until now it went by as a "+$12"
    /// the size of every other one — <see cref="SaleFx"/> treats the first sale exactly like the ten
    /// thousandth. This marks it once, with a banner and a burst of coins, and then never again.
    ///
    /// Once fired — or once a save says it already has been — the component switches itself off, so a
    /// celebration nobody will ever see again costs nothing to carry: no event subscription, no Update.
    ///
    /// The banner is built here rather than authored, because there is exactly one of it and it only
    /// exists for two seconds. The coins come from a <see cref="ConfettiBurst"/> wired in the Inspector.
    /// </summary>
    public sealed class FirstSaleFx : MonoBehaviour
    {
        [Tooltip("Kutlamanın sikkeleri. Boşsa yalnız afiş çıkar.")]
        [SerializeField] private ConfettiBurst confetti;
        [Tooltip("Afişin arkasındaki şerit. Boşsa yazı tek başına durur.")]
        [SerializeField] private Sprite ribbon;
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private Vector2 bannerSize = new Vector2(860f, 200f);
        [Tooltip("Afişin ekranın ortasından ne kadar yukarıda duracağı.")]
        [SerializeField] private float bannerY = 250f;
        [SerializeField] private int fontSize = 66;
        [Tooltip("Afiş yerine oturduktan sonra ekranda kalma süresi.")]
        [SerializeField] private float holdSeconds = 1.9f;
        [SerializeField] private float riseSeconds = 0.4f;
        [SerializeField] private float fadeSeconds = 0.35f;

        private SaveData _data;
        private SaveService _save;
        private AudioService _audio;
        private HapticService _haptic;
        private CoalOperation _op;
        private RectTransform _banner;
        private CanvasGroup _fade;
        private float _rebind;
        private bool _played;

        private void Awake()
        {
            var go = new GameObject("Afis", typeof(RectTransform), typeof(CanvasGroup));
            _banner = (RectTransform)go.transform;
            _banner.SetParent(transform, false);
            _banner.anchorMin = _banner.anchorMax = new Vector2(0.5f, 0.5f);
            _banner.pivot = new Vector2(0.5f, 0.5f);
            _banner.sizeDelta = bannerSize;
            _banner.anchoredPosition = new Vector2(0f, bannerY);
            _fade = go.GetComponent<CanvasGroup>();
            _fade.blocksRaycasts = false;
            _fade.interactable = false;

            if (ribbon != null)
            {
                var back = new GameObject("Serit", typeof(RectTransform), typeof(Image));
                var brt = (RectTransform)back.transform;
                brt.SetParent(_banner, false);
                brt.anchorMin = Vector2.zero;
                brt.anchorMax = Vector2.one;
                brt.offsetMin = Vector2.zero;
                brt.offsetMax = Vector2.zero;
                var im = back.GetComponent<Image>();
                im.sprite = ribbon;
                im.type = Image.Type.Sliced;
                im.raycastTarget = false;
            }

            var text = new GameObject("Yazi", typeof(RectTransform), typeof(TextMeshProUGUI));
            var trt = (RectTransform)text.transform;
            trt.SetParent(_banner, false);
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(24f, 12f);
            trt.offsetMax = new Vector2(-24f, -12f);
            var tmp = text.GetComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.raycastTarget = false;
            tmp.color = Color.white;
            tmp.text = Loc.T("kutlama.ilk_satis");

            go.SetActive(false);
        }

        /// <summary>
        /// Finds whichever island is live and hangs on its sale event. Polled on a slow timer, the same
        /// way <see cref="SaleFx"/> does it: travelling swaps the enabled operation for a different object.
        /// </summary>
        private void Update()
        {
            // Kutlama oynarken buradan geçilmez: bayrak çoktan yazıldı ve aşağıdaki kapatma,
            // bileşeni söndürerek kendi coroutine'ini de öldürüyor.
            if (_played) return;

            _rebind -= Time.unscaledDeltaTime;
            if (_rebind > 0f) return;
            _rebind = 1f;

            if (_data == null)
            {
                _data = ServiceLocator.Get<SaveData>();
                if (_data == null) return;
            }
            if (_data.firstSaleSeen) { enabled = false; return; }

            if (_op != null && _op.enabled) return;
            var ops = FindObjectsByType<CoalOperation>(FindObjectsSortMode.None);
            for (int i = 0; i < ops.Length; i++)
            {
                if (!ops[i].enabled) continue;
                if (_op != null) _op.Sold -= OnSold;
                _op = ops[i];
                _op.Sold += OnSold;
                return;
            }
        }

        private void OnDisable()
        {
            if (_op != null) _op.Sold -= OnSold;
            _op = null;
        }

        private void OnSold(Vector3 where, double amount)
        {
            if (_played || _data == null || _data.firstSaleSeen) return;
            _played = true;
            _data.firstSaleSeen = true;
            if (_save == null) _save = ServiceLocator.Get<SaveService>();
            if (_save != null) _save.Save(_data);
            if (_op != null) { _op.Sold -= OnSold; _op = null; }
            StartCoroutine(Celebrate());
        }

        private IEnumerator Celebrate()
        {
            _banner.gameObject.SetActive(true);
            if (confetti != null) confetti.Play();
            if (_audio == null) _audio = ServiceLocator.Get<AudioService>();
            if (_audio != null) _audio.Play(SoundId.Reward);
            if (_haptic == null) _haptic = ServiceLocator.Get<HapticService>();
            if (_haptic != null) _haptic.Medium();

            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / Mathf.Max(0.05f, riseSeconds);
                float e = t >= 1f ? 1f : Back(t);
                float s = Mathf.LerpUnclamped(0.4f, 1f, e);
                _banner.localScale = new Vector3(s, s, 1f);
                _fade.alpha = Mathf.Clamp01(t * 2f);
                yield return null;
            }
            _banner.localScale = Vector3.one;
            _fade.alpha = 1f;

            yield return new WaitForSecondsRealtime(holdSeconds);

            t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / Mathf.Max(0.05f, fadeSeconds);
                _fade.alpha = 1f - Mathf.Clamp01(t);
                _banner.anchoredPosition = new Vector2(0f, bannerY + Mathf.Clamp01(t) * 60f);
                yield return null;
            }

            _banner.gameObject.SetActive(false);
            _banner.anchoredPosition = new Vector2(0f, bannerY);
            enabled = false;      // bir daha çalışmayacak: ne olay aboneliği kalsın ne Update
        }

        /// <summary>Overshoot easing — the banner passes its size and settles back into it.</summary>
        private static float Back(float p)
        {
            float u = p - 1f;
            return 1f + 2.7f * u * u * u + 1.7f * u * u;
        }
    }
}
