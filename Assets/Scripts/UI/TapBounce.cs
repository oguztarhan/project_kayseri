using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The dip and spring-back under a finger, for every ordinary button in the game.
    ///
    /// <see cref="StoreCardFx"/> already gives the store's cards this, and the difference between a
    /// store card and a HUD button was the whole argument for writing it: one answers the touch and the
    /// other is a picture that happens to be tappable. Nothing else about the two is different, so the
    /// press half is the same spring with the same constants — a small control just dips a little
    /// further, because there is less of it to see move.
    ///
    /// Added at runtime by <see cref="UiPanelSound"/> rather than authored on twelve prefabs: that hook
    /// already walks every button a panel contains, at the one moment when rows built in code are
    /// finally there to find. Cards under a <see cref="StoreCardFx"/> are skipped — two components
    /// writing one localScale would fight.
    ///
    /// Idle buttons cost a return: the spring only runs between the touch and the moment it settles,
    /// and the transform is written only while it does.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TapBounce : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        [Tooltip("Parmak üstündeyken ölçek. Mağaza kartından biraz daha derin: küçük bir butonda daha az hareket görünüyor.")]
        [SerializeField] private float pressedScale = 0.94f;
        [Tooltip("Bırakınca geri yaylanmanın sertliği (yay sabiti).")]
        [SerializeField] private float springStiffness = 340f;
        [Tooltip("Bırakıştaki sönümleme; düşük değer daha çok seker.")]
        [SerializeField] private float releaseDamping = 13f;

        private Selectable _selectable;   // optional — a disabled button must not dip when touched
        private Vector3 _rest;            // the authored scale; the bounce multiplies into it
        private float _press = 1f;
        private float _velocity;
        private bool _pressed;
        private bool _running;

        /// <summary>Adds the bounce to one button, unless something else already owns its scale.</summary>
        public static void Attach(Button button)
        {
            if (button == null) return;
            GameObject go = button.gameObject;
            if (go.GetComponent<TapBounce>() != null) return;
            if (go.GetComponentInParent<StoreCardFx>(true) != null) return;
            // A button that already breathes owns its scale every frame; a press written into the same
            // field would simply be overwritten on the next one, so the button would read as dead.
            if (go.GetComponent<StoreHeroFx>() != null) return;
            if (Backdrop(button)) return;
            go.AddComponent<TapBounce>();
        }

        /// <summary>
        /// A control stretched to fill its parent on both axes is a dimmer, not a button: it is there to
        /// eat the tap that closes the screen. Shrinking one lets the scene show around its edge.
        /// </summary>
        private static bool Backdrop(Button button)
        {
            var rt = button.transform as RectTransform;
            if (rt == null) return false;
            return rt.anchorMin.x < 0.01f && rt.anchorMin.y < 0.01f
                && rt.anchorMax.x > 0.99f && rt.anchorMax.y > 0.99f;
        }

        /// <summary>Every button under a root, for surfaces that do not go through <see cref="UiPanelSound"/>.</summary>
        public static void AttachAll(GameObject root)
        {
            if (root == null) return;
            var found = root.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < found.Length; i++) Attach(found[i]);
        }

        private void Awake()
        {
            _selectable = GetComponent<Selectable>();
            _rest = transform.localScale;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_selectable != null && !_selectable.IsInteractable()) return;
            _pressed = true;
            _running = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _pressed = false;
            _running = true;
        }

        private void OnDisable()
        {
            // A panel closed mid-press must not reopen squashed.
            _pressed = false;
            _running = false;
            _press = 1f;
            _velocity = 0f;
            transform.localScale = _rest;
        }

        private void Update()
        {
            if (!_running) return;
            float dt = Time.unscaledDeltaTime;   // panels are often open while the game is paused

            if (_pressed)
            {
                // Straight to the pressed depth: under a finger the control should already be there.
                _press = Mathf.Lerp(_press, pressedScale, 1f - Mathf.Exp(-30f * dt));
                _velocity = 0f;
            }
            else if (Mathf.Abs(_press - 1f) > 0.0004f || Mathf.Abs(_velocity) > 0.004f)
            {
                _velocity += (1f - _press) * springStiffness * dt;
                _velocity *= Mathf.Exp(-releaseDamping * dt);
                _press += _velocity * dt;
            }
            else
            {
                _press = 1f;
                _velocity = 0f;
                _running = false;
            }

            transform.localScale = _rest * _press;
        }
    }
}
