using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// Opening pop for a whole panel: scale eases in from just under 1 while a CanvasGroup fades up.
    /// Runs on OnEnable, so any screen that opens with SetActive(true) gets it just by carrying the
    /// component — no controller has to know it exists.
    /// </summary>
    public sealed class PanelOpenFx : MonoBehaviour
    {
        [SerializeField] private float fromScale = 0.95f;
        [SerializeField] private float seconds = 0.18f;

        private RectTransform _rt;
        private CanvasGroup _group;   // optional — without it the pop is scale-only
        private float _t;

        private void Awake()
        {
            _rt = (RectTransform)transform;
            _group = GetComponent<CanvasGroup>();
        }

        private void OnEnable()
        {
            _t = 0f;
            _rt.localScale = new Vector3(fromScale, fromScale, 1f);
            if (_group != null) _group.alpha = 0f;
        }

        private void OnDisable()
        {
            _rt.localScale = Vector3.one;
            if (_group != null) _group.alpha = 1f;
        }

        private void Update()
        {
            if (_t >= seconds) return;
            _t += Time.unscaledDeltaTime;
            float p = seconds > 0f ? Mathf.Clamp01(_t / seconds) : 1f;
            float e = 1f - (1f - p) * (1f - p) * (1f - p);
            float s = Mathf.Lerp(fromScale, 1f, e);
            _rt.localScale = new Vector3(s, s, 1f);
            if (_group != null) _group.alpha = p;
        }
    }
}
