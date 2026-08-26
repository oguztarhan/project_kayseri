using Game.Core;
using Game.Systems;
using TMPro;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// The welcome-back screen's whole performance: the card and everything on it
    /// arriving one piece at a time, the medallion swaying under a turning glow, and the amount counting
    /// up from nothing while coins burst out of the pile. The twinkle over the medallion and the 2×
    /// button is <see cref="CardSparkle"/>, which builds its own stars and so shares nothing with this.
    ///
    /// This is the first thing a returning player sees and the one moment the game hands them something
    /// for having been away. It used to be a finished number on a still card — the reward arrived
    /// already over. A number that climbs is the same reward watched instead of read.
    ///
    /// Runs on <c>OnEnable</c> and reads <see cref="OfflineReport"/> itself, so
    /// <see cref="WelcomeBackUI"/> needs to know nothing about it: the controller still decides what the
    /// screen says and when it opens.
    ///
    /// One rule holds the choreography together — <b>nothing here may share a transform with another
    /// component</b>. Entrances write the step's own scale, the count writes the label's, the sway writes
    /// the medallion's rotation. <see cref="TapBounce"/> sits on the buttons and only moves while a
    /// finger is down, which cannot overlap an entrance nobody has had time to touch yet.
    /// </summary>
    public sealed class WelcomeBackFx : MonoBehaviour
    {
        [Header("Giriş sırası")]
        [Tooltip("Sırayla belirecek parçalar: kart, şerit, madalyon, satırlar, butonlar. Sıra bu dizinin sırası.")]
        [SerializeField] private RectTransform[] steps;
        [Tooltip("Bir parça ile sonraki arasındaki gecikme.")]
        [SerializeField] private float stepDelay = 0.07f;
        [SerializeField] private float stepSeconds = 0.36f;
        [SerializeField] private float fromScale = 0.5f;
        [Tooltip("Yerine otururken ne kadar taşsın. 0 = taşma yok, 1.7 = klasik zıplama.")]
        [SerializeField] private float overshoot = 1.7f;

        [Header("Madalyon")]
        [SerializeField] private RectTransform medallion;
        [Tooltip("Madalyonun arkasındaki ışık; yavaşça döner ve nefes alır.")]
        [SerializeField] private RectTransform glow;
        [Tooltip("Işığın dönme hızı, derece/sn. Yavaş olmalı: hızlısı fırıldağa benziyor.")]
        [SerializeField] private float glowSpin = 9f;
        [SerializeField] private float glowPulse = 0.05f;
        [Tooltip("Madalyonun sağa sola yalpalaması, derece.")]
        [SerializeField] private float swayDegrees = 2.5f;
        [SerializeField] private float swaySeconds = 2.6f;

        [Header("Sayım")]
        [SerializeField] private TMP_Text amountLabel;
        [Tooltip("Kart yerine oturmadan sayım başlamasın.")]
        [SerializeField] private float countDelay = 0.5f;
        [SerializeField] private float countSeconds = 1.15f;
        [Tooltip("Sayı hedefe varınca son bir büyüme.")]
        [SerializeField] private float countPunch = 0.2f;
        [SerializeField] private float countPunchSeconds = 0.32f;

        [Header("Kutlama")]
        [Tooltip("Sayım başlarken patlayan sikkeler. Boşsa sessizce atlanır.")]
        [SerializeField] private ConfettiBurst coins;

        private OfflineReport _report;
        private Vector3 _glowRest = Vector3.one;
        private double _target;
        private float _t;
        private float _entranceEnd;
        private float _punchLeft;
        private bool _entering;
        private bool _counting;
        private bool _coinsFired;

        private void Awake()
        {
            if (glow != null) _glowRest = glow.localScale;
            _entranceEnd = steps != null && steps.Length > 0
                ? (steps.Length - 1) * stepDelay + stepSeconds
                : 0f;
        }

        private void OnEnable()
        {
            if (_report == null) _report = ServiceLocator.Get<OfflineReport>();
            _target = _report != null ? _report.Amount.ToDouble() : 0d;

            _t = 0f;
            _punchLeft = 0f;
            _entering = true;
            _counting = true;
            _coinsFired = false;

            if (steps != null)
                for (int i = 0; i < steps.Length; i++)
                    if (steps[i] != null) steps[i].localScale = Vector3.zero;
            if (amountLabel != null)
            {
                amountLabel.transform.localScale = Vector3.one;
                amountLabel.text = "$0";
            }
        }

        private void OnDisable()
        {
            // Leave the screen the way it was authored: the next open replays from a known state, and a
            // half-finished entrance must never be what the player finds when they reopen it.
            if (steps != null)
                for (int i = 0; i < steps.Length; i++)
                    if (steps[i] != null) steps[i].localScale = Vector3.one;
            if (glow != null) glow.localScale = _glowRest;
            if (medallion != null) medallion.localRotation = Quaternion.identity;
            if (amountLabel != null) amountLabel.transform.localScale = Vector3.one;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;   // the game is often paused behind this screen
            _t += dt;

            if (_entering) Entrance();
            if (_counting) Count();
            if (_punchLeft > 0f) Punch(dt);
            Idle();
        }

        /// <summary>Each piece springs up from nothing, one after the next.</summary>
        private void Entrance()
        {
            if (steps == null) { _entering = false; return; }
            for (int i = 0; i < steps.Length; i++)
            {
                RectTransform step = steps[i];
                if (step == null) continue;
                float p = stepSeconds > 0f ? (_t - i * stepDelay) / stepSeconds : 1f;
                if (p <= 0f) { step.localScale = Vector3.zero; continue; }
                float s = p >= 1f ? 1f : Mathf.LerpUnclamped(fromScale, 1f, Back(p));
                step.localScale = new Vector3(s, s, 1f);
            }
            if (_t < _entranceEnd) return;
            for (int i = 0; i < steps.Length; i++)
                if (steps[i] != null) steps[i].localScale = Vector3.one;
            _entering = false;
        }

        /// <summary>Overshoot easing — the piece passes its size and settles back into it.</summary>
        private float Back(float p)
        {
            float u = p - 1f;
            return 1f + (overshoot + 1f) * u * u * u + overshoot * u * u;
        }

        private void Count()
        {
            float p = countSeconds > 0f ? Mathf.Clamp01((_t - countDelay) / countSeconds) : 1f;
            if (_t < countDelay) return;

            if (!_coinsFired)
            {
                _coinsFired = true;
                if (coins != null) coins.Play();
            }

            // Fast at the start and slowing into the total: the last few digits are the ones the player
            // is waiting for, and a linear count spends its whole time on numbers nobody reads.
            float e = 1f - (1f - p) * (1f - p) * (1f - p);
            if (amountLabel != null)
                amountLabel.text = "$" + NumberFormatter.Format(new BigDouble(_target * e));

            if (p < 1f) return;
            _counting = false;
            _punchLeft = countPunchSeconds;
        }

        private void Punch(float dt)
        {
            _punchLeft -= dt;
            if (amountLabel == null) return;
            float s = _punchLeft > 0f
                ? 1f + countPunch * Mathf.Sin(Mathf.Clamp01(1f - _punchLeft / countPunchSeconds) * Mathf.PI)
                : 1f;
            amountLabel.transform.localScale = new Vector3(s, s, 1f);
            if (_punchLeft <= 0f) _punchLeft = 0f;
        }

        /// <summary>
        /// What the screen does while the player just looks at it. Small and slow on purpose: the card
        /// has to stay readable, and anything faster turns a reward into a warning.
        /// </summary>
        private void Idle()
        {
            if (glow != null)
            {
                glow.Rotate(0f, 0f, glowSpin * Time.unscaledDeltaTime);
                float g = 1f + glowPulse * Mathf.Sin(_t * 1.7f);
                glow.localScale = new Vector3(_glowRest.x * g, _glowRest.y * g, 1f);
            }
            if (medallion != null && swaySeconds > 0f)
                medallion.localRotation = Quaternion.Euler(0f, 0f,
                    swayDegrees * Mathf.Sin(_t * (2f * Mathf.PI / swaySeconds)));
        }
    }
}
