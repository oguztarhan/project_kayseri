namespace Game.Systems
{
    /// <summary>
    /// A one-frame mailbox for "something just happened, jolt the camera".
    ///
    /// It lives here rather than on the camera because of the assembly graph: the things worth
    /// shaking for are in Game.Gameplay (a district finishing its rebuild, an unlock landing) and
    /// the camera is in Game.UI, which already references Game.Gameplay. Calling the camera
    /// directly would need the reference the other way round, and Unity rejects the cycle.
    /// Game.Systems is the one assembly both sides can see — the same reason
    /// <see cref="QualityService"/>'s night-lighting flags live here and are read from IslandGlow.
    ///
    /// Requests do not queue. The strongest one asked for since the camera last looked wins, so a
    /// small event landing in the same frame as a big one cannot flatten it, and a burst of them
    /// cannot stack into something violent.
    /// </summary>
    public static class CameraShake
    {
        /// <summary>Set once from JuiceConfig at boot. Off means every request is dropped here,
        /// so no caller needs to ask permission first.</summary>
        public static bool Enabled = true;

        private static float _amplitude;
        private static float _seconds;

        /// <summary>Ask for a jolt. Amplitude is in world units at the camera's own scale.</summary>
        public static void Request(float amplitude, float seconds)
        {
            if (!Enabled || amplitude <= 0f || seconds <= 0f) return;
            if (amplitude <= _amplitude) return;
            _amplitude = amplitude;
            _seconds = seconds;
        }

        /// <summary>Taken by the camera, once per frame. True when there was something waiting.</summary>
        public static bool Consume(out float amplitude, out float seconds)
        {
            amplitude = _amplitude;
            seconds = _seconds;
            if (_amplitude <= 0f) return false;
            _amplitude = 0f;
            _seconds = 0f;
            return true;
        }

        /// <summary>Dropped on a scene change so a request made just before a load does not fire
        /// into the next scene's camera.</summary>
        public static void Clear()
        {
            _amplitude = 0f;
            _seconds = 0f;
        }
    }
}
