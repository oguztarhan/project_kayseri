using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Drives a person prefab's <see cref="Animator"/> from whatever the yard has them doing.
    ///
    /// The people packs animate off TRIGGERS, not a blend parameter — one each for idle, walk, run and
    /// wave — which means firing them every frame would restart the clip every frame and freeze the
    /// character on its first pose. So this remembers the last state it asked for and only speaks when
    /// the answer changes. Everything that moves a body in the market goes through it: the player, the
    /// customers and the staff.
    ///
    /// Silently does nothing when the body has no Animator, which is what keeps the greybox capsules
    /// working while the art is being swapped in.
    /// </summary>
    public sealed class PersonAnimator
    {
        /// <summary>The trigger names in the packs' controllers. Spelled once, here.</summary>
        public const string Idle = "idle", Walk = "walk", Run = "run", Wave = "wave";

        private readonly Animator _animator;
        private string _state;

        public PersonAnimator(Transform body)
        {
            _animator = body != null ? body.GetComponentInChildren<Animator>() : null;
            if (_animator == null) return;

            // The people packs ship their prefabs on AlwaysAnimate, which evaluates the rig every frame
            // whether or not the body is on screen. That is the whole cost of a crowd on a phone: a
            // couple of dozen skinned characters, most of them behind the camera, all being posed for
            // nobody. Culled completely they cost nothing until they are actually visible.
            //
            // Safe for everyone who goes through here because none of them use root motion — bodies are
            // moved by hand — so a skipped update loses nothing but the pose, which is recomputed the
            // frame they come back into view.
            _animator.cullingMode = AnimatorCullingMode.CullCompletely;
        }

        /// <summary>
        /// Whether the body actually has a rig. Callers that fake motion on unrigged art — the island
        /// crew's vertical bob — ask this so they don't stack their fake on top of a real walk cycle.
        /// </summary>
        public bool HasAnimator => _animator != null;

        /// <summary>
        /// Plays the clips at the caller's clock rather than the game's. A body moved by hand along a
        /// sped-up simulation covers twice the ground per stride, and a walk cycle that does not speed up
        /// with it reads as skating; this is the only thing that keeps the feet under the body.
        /// </summary>
        public void SetSpeed(float speed)
        {
            if (_animator != null) _animator.speed = speed;
        }

        /// <summary>Asks for a state. Repeats are free — only a change reaches the Animator.</summary>
        public void Set(string state)
        {
            if (_animator == null || state == _state) return;
            _state = state;
            _animator.SetTrigger(state);
        }

        /// <summary>
        /// Walk or idle, from how fast the body is actually going. The threshold is deliberately low:
        /// a character shuffling the last few centimetres into a queue slot should still be walking,
        /// and only a body that has genuinely stopped should stand still.
        /// </summary>
        public void SetMoving(bool moving) => Set(moving ? Walk : Idle);
    }
}
