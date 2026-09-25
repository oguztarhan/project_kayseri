using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Press-and-hold on a button: one step on the press, then after <see cref="_delay"/> steps that start at
    /// <see cref="_startRate"/> a second and speed up to <see cref="_maxRate"/>. A step that answers false ends the
    /// hold's repeats (a star was reached, or the money ran out); the player has to lift and press again.
    /// Ignores the press while its button is not interactable. Uses the button's own press, not its click, so a
    /// hold never also fires a click on release.
    /// </summary>
    public sealed class HoldRepeat : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        private Button _button;
        private Func<bool> _step;
        private Action _released;
        private float _delay, _startRate, _maxRate, _rampSeconds;
        private bool _held, _repeating;
        private float _heldFor, _nextStepAt;

        public void Configure(Func<bool> step, Action released, float delay, float startRate, float maxRate, float rampSeconds)
        {
            _button = GetComponent<Button>();
            _step = step;
            _released = released;
            _delay = delay;
            _startRate = Mathf.Max(0.1f, startRate);
            _maxRate = Mathf.Max(_startRate, maxRate);
            _rampSeconds = Mathf.Max(0.01f, rampSeconds);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_step == null || (_button != null && !_button.interactable)) return;
            _held = true;
            _heldFor = 0f;
            _nextStepAt = _delay;
            _repeating = _step();
        }

        public void OnPointerUp(PointerEventData eventData) => Release();

        // A finger that slides off the button has let go of it.
        public void OnPointerExit(PointerEventData eventData) => Release();

        private void OnDisable() => Release();

        private void Release()
        {
            if (!_held) return;
            _held = false;
            _repeating = false;
            _released?.Invoke();
        }

        private void Update()
        {
            if (!_held || !_repeating) return;
            _heldFor += Time.unscaledDeltaTime;
            // Several steps can fall due in one long frame. A few are taken; the rest are dropped rather than
            // owed, so a hitch never turns into a burst of purchases afterwards.
            for (int i = 0; i < 4 && _heldFor >= _nextStepAt; i++)
            {
                float ramp = Mathf.Clamp01((_heldFor - _delay) / _rampSeconds);
                _nextStepAt += 1f / Mathf.Lerp(_startRate, _maxRate, ramp);
                if (!_step()) { _repeating = false; return; }
            }
            if (_nextStepAt < _heldFor) _nextStepAt = _heldFor;
        }
    }
}
