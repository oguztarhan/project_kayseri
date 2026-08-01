using UnityEngine;

namespace Kayseri.Island
{
    /// <summary>
    /// Swaps the island between its three upgrade phases.
    ///
    /// Phase 1 = levels 0-15, phase 2 = 15-30, phase 3 = maxed. Each phase is a
    /// separate prefab root; only one is active at a time. Wire the roots in the
    /// Inspector (the island build tool fills them in automatically).
    /// </summary>
    public sealed class IslandPhaseController : MonoBehaviour
    {
        [SerializeField] private GameObject[] _phaseRoots;
        [SerializeField] private int _startPhase = 1;

        private int _currentPhase = -1;

        /// <summary>Currently active phase, 1-based. 0 when nothing is active.</summary>
        public int CurrentPhase => _currentPhase;

        /// <summary>Number of phases wired up.</summary>
        public int PhaseCount => _phaseRoots != null ? _phaseRoots.Length : 0;

        private void Awake()
        {
            SetPhase(_startPhase);
        }

        /// <summary>
        /// Activates the given phase (1-based) and deactivates the others.
        /// Cheap enough to call from UI; does nothing if already on that phase.
        /// </summary>
        public void SetPhase(int phase)
        {
            if (_phaseRoots == null || _phaseRoots.Length == 0)
            {
                Debug.LogWarning("[Island] No phase roots assigned.", this);
                return;
            }

            phase = Mathf.Clamp(phase, 1, _phaseRoots.Length);
            if (phase == _currentPhase) return;

            for (int i = 0; i < _phaseRoots.Length; i++)
            {
                var root = _phaseRoots[i];
                if (root == null) continue;

                bool shouldBeActive = (i == phase - 1);
                if (root.activeSelf != shouldBeActive)
                    root.SetActive(shouldBeActive);
            }

            _currentPhase = phase;
        }

        /// <summary>Maps a building level onto a phase and applies it.</summary>
        public void SetPhaseForLevel(int level)
        {
            if (level < 15) SetPhase(1);
            else if (level < 30) SetPhase(2);
            else SetPhase(3);
        }

        /// <summary>Steps to the next phase, stopping at the last one.</summary>
        public void AdvancePhase()
        {
            SetPhase(_currentPhase + 1);
        }
    }
}
