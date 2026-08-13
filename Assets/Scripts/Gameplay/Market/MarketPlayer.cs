using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The body the player drives around a market yard. Movement only — carrying, depositing and
    /// selling arrive in the next step.
    ///
    /// It does not read input. A joystick is a piece of interface, and <c>Game.Gameplay</c> sits below
    /// <c>Game.UI</c> in the assembly order for exactly the reason the sale event does: the simulation
    /// is not allowed to know what a thumb is. The HUD pushes a direction in through
    /// <see cref="SetMoveInput"/> and this turns it into a walk.
    ///
    /// The input vector is in CAMERA space, not world space — up on the stick means away from the
    /// viewer whatever angle the yard is being looked at from. An isometric camera makes those two
    /// things 45 degrees apart, and a player walking diagonally when they pushed straight up is the
    /// single fastest way to make a top-down game feel broken.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class MarketPlayer : MonoBehaviour
    {
        [Header("Yürüyüş")]
        [Tooltip("Tam itilmiş kolda saniyedeki hız, dünya birimi.")]
        [SerializeField, Min(0.1f)] private float moveSpeed = 7f;

        [Tooltip("Hıza yaklaşma süresi. Büyüdükçe hareket yumuşar, küçüldükçe sertleşir.")]
        [SerializeField, Min(0.01f)] private float accelerationSeconds = 0.14f;

        [Tooltip("Durma süresi. Kalkıştan kısa olmalı — yavaş duran bir gövde kaygan hissettirir.")]
        [SerializeField, Min(0.01f)] private float brakingSeconds = 0.09f;

        [Tooltip("Kolun kendi yumuşaması. Başparmak sıçradığında gövde sıçramasın diye.")]
        [SerializeField, Min(0.01f)] private float inputSmoothingSeconds = 0.07f;

        [Tooltip("Dönüş hızı, saniyedeki derece. Yürüdüğü yöne bu hızla döner.")]
        [SerializeField, Min(1f)] private float turnDegreesPerSecond = 900f;

        [Tooltip("Yere yapıştıran sabit kuvvet. Zemin tam düz olmadığında basamaklarda zıplamasın diye.")]
        [SerializeField] private float gravity = -22f;

        [Tooltip("Kolun bu kadar altındaki itmeler yok sayılır — başparmak dinlenirken sürüklenmesin diye.")]
        [SerializeField, Range(0f, 0.9f)] private float deadZone = 0.12f;

        private CharacterController _controller;
        private Transform _cameraBasis;      // whose yaw the stick is measured against
        private Vector2 _input;              // what the thumb is asking for
        private Vector2 _smoothed;           // what the body is willing to believe
        private Vector3 _velocity;           // horizontal only; gravity is applied separately
        private float _fall;
        private PersonAnimator _anim;

        /// <summary>
        /// Exponential approach, framerate-independent. Lerping by <c>speed * dt</c> is the usual
        /// shorthand and it is wrong: the same constant settles at a different rate on a 60fps phone
        /// than on a 120fps one, so the character handles differently on different hardware. This is
        /// the same curve at any step size.
        /// </summary>
        private static float Approach(float seconds, float dt)
            => seconds <= 0f ? 1f : 1f - Mathf.Exp(-dt / seconds);

        /// <summary>Speed right now as a fraction of the maximum — what a walk animation would key off.</summary>
        public float Gait => moveSpeed > 0f ? _velocity.magnitude / moveSpeed : 0f;

        private void Awake() => _controller = GetComponent<CharacterController>();

        /// <summary>
        /// Binds the animator on whatever body was spawned. Separate from Awake because the model is
        /// instantiated by the scene's boot after this component already exists.
        /// </summary>
        public void BindBody(Transform body) => _anim = new PersonAnimator(body);

        /// <summary>The transform the stick is measured against. Normally the yard camera.</summary>
        public void SetCameraBasis(Transform basis) => _cameraBasis = basis;

        /// <summary>A stick direction, roughly unit length. Anything shorter than the dead zone is a rest.</summary>
        public void SetMoveInput(Vector2 input)
        {
            _input = input.sqrMagnitude < deadZone * deadZone ? Vector2.zero : Vector2.ClampMagnitude(input, 1f);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // Flatten the camera's own axes onto the ground and steer by those.
            Vector3 forward = Vector3.forward, right = Vector3.right;
            if (_cameraBasis != null)
            {
                forward = _cameraBasis.forward; forward.y = 0f;
                right = _cameraBasis.right; right.y = 0f;
                // Looking straight down would leave nothing to flatten; fall back to world axes.
                if (forward.sqrMagnitude < 1e-4f) { forward = Vector3.forward; right = Vector3.right; }
                forward.Normalize(); right.Normalize();
            }

            // Smooth the stick before the body ever sees it. A thumb dragging across glass is noisy at
            // the sample level, and a body that answers every sample of that noise reads as twitchy no
            // matter how gently it accelerates.
            _smoothed = Vector2.Lerp(_smoothed, _input, Approach(inputSmoothingSeconds, dt));
            if (_smoothed.sqrMagnitude < 1e-5f) _smoothed = Vector2.zero;

            Vector3 wanted = (right * _smoothed.x + forward * _smoothed.y) * moveSpeed;

            // Starting and stopping get their own curves. Equal ones feel wrong in opposite directions:
            // matched to a pleasant start, the stop drifts; matched to a crisp stop, the start snaps.
            bool slowing = wanted.sqrMagnitude < _velocity.sqrMagnitude;
            _velocity = Vector3.Lerp(_velocity, wanted,
                                     Approach(slowing ? brakingSeconds : accelerationSeconds, dt));
            if (_velocity.sqrMagnitude < 1e-4f) _velocity = Vector3.zero;

            // Re-grounding to a small negative rather than zero: CharacterController only reports
            // isGrounded after it has been pushed into the floor, so a clean zero flickers it off and on
            // and the yard reads as very slightly bouncy.
            _fall = _controller.isGrounded && _fall < 0f ? -2f : _fall + gravity * dt;

            Vector3 step = _velocity;
            step.y = _fall;
            _controller.Move(step * dt);

            if (_velocity.sqrMagnitude > 0.01f)
            {
                Quaternion face = Quaternion.LookRotation(new Vector3(_velocity.x, 0f, _velocity.z));
                transform.rotation = Quaternion.RotateTowards(transform.rotation, face,
                                                              turnDegreesPerSecond * dt);
            }

            // Walking at anything past a crawl, running when near full tilt. The threshold sits well
            // above zero so the tail of the braking curve does not flicker the clip on and off.
            if (_anim != null)
                _anim.Set(Gait > 0.85f ? PersonAnimator.Run
                        : Gait > 0.08f ? PersonAnimator.Walk
                        : PersonAnimator.Idle);
        }
    }
}
