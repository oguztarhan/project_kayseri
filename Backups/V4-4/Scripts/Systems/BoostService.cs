using UnityEngine;

namespace Game.Systems
{
    /// <summary>Temporary income boost (e.g. a rewarded-ad 2× for 30s, GDD §10). Time-limited multiplier.</summary>
    public sealed class BoostService
    {
        private double _mult = 1d;
        private double _end;

        public void SetBoost(double mult, double seconds)
        {
            _mult = mult;
            _end = Time.realtimeSinceStartupAsDouble + seconds;
        }

        public double ActiveMultiplier => Time.realtimeSinceStartupAsDouble < _end ? _mult : 1d;
        public bool IsActive => ActiveMultiplier > 1d;
        public float SecondsLeft => (float)System.Math.Max(0d, _end - Time.realtimeSinceStartupAsDouble);
    }
}
