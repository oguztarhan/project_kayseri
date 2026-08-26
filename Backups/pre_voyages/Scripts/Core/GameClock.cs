using System;

namespace Game.Core
{
    /// <summary>
    /// Fixed-timestep simulation clock. The whole economy/production sim advances on
    /// <see cref="OnTick"/> at a steady low frequency, decoupled from frame rate, so a
    /// fully-loaded factory costs the same per frame as an empty one (see GDD §14.5).
    /// A MonoBehaviour runner calls <see cref="Advance"/> with Time.deltaTime each frame;
    /// visuals interpolate between ticks. Pure C# so it is unit-testable.
    /// </summary>
    public sealed class GameClock
    {
        /// <summary>Max ticks processed in a single Advance call, to avoid a spiral-of-death after a stall.</summary>
        private const int MaxTicksPerAdvance = 8;

        public float TickInterval { get; }
        public bool Paused { get; set; }
        public long TickCount { get; private set; }

        /// <summary>Fired once per fixed simulation tick.</summary>
        public event Action OnTick;

        private float _accumulator;

        public GameClock(float ticksPerSecond = 8f)
        {
            if (ticksPerSecond <= 0f) ticksPerSecond = 1f;
            TickInterval = 1f / ticksPerSecond;
        }

        /// <summary>Feed real elapsed time (e.g. Time.deltaTime). Fires OnTick for each whole interval elapsed.</summary>
        public void Advance(float deltaTime)
        {
            if (Paused || deltaTime <= 0f) return;

            _accumulator += deltaTime;
            int ticks = 0;
            while (_accumulator >= TickInterval && ticks < MaxTicksPerAdvance)
            {
                _accumulator -= TickInterval;
                TickCount++;
                ticks++;
                OnTick?.Invoke();
            }

            // Backlog too large (long stall / big frame): drop it instead of catching up forever.
            if (ticks >= MaxTicksPerAdvance && _accumulator >= TickInterval)
            {
                _accumulator = 0f;
            }
        }
    }
}
