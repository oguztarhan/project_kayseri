namespace Game.Core
{
    /// <summary>
    /// A trailing one-minute counter: something happened, how much of it is happening per minute?
    ///
    /// The island's production chain has four of these on it - ore out of the mines, ore into the
    /// furnace, bars out of the furnace, bars onto the market's pads - because the upgrade screen
    /// cannot tell a player which stage is holding the island back without measuring all four.
    /// It cannot be derived instead: how much ore a mine yields per minute depends on how long a
    /// train takes to drive a route that was drawn in Blender, and no formula in
    /// <see cref="IslandEconomy"/> knows the length of a railway.
    ///
    /// Sixty one-second buckets rolling in a ring, which is the same shape (and the same trusted
    /// window) as the cash meter in MarketService - a player who has learnt to read the $/min pill
    /// should find these behaving the way it does.
    /// </summary>
    public sealed class FlowMeter
    {
        private const int Window = 60;

        /// <summary>
        /// Below this many filled seconds the window has measured a lump, not a rate. A truck that
        /// has just tipped six ore reads as 360 an hour if you ask three seconds later.
        /// </summary>
        private const int MinTrusted = 8;

        private readonly double[] _buckets = new double[Window];
        private int _index, _filled;
        private double _trailing, _thisSecond;
        private float _accum;

        /// <summary>Records work as it happens. Called from the simulation, so it stays this cheap.</summary>
        public void Add(double amount)
        {
            if (amount > 0d) _thisSecond += amount;
        }

        /// <summary>Rolls the ring. Safe to call every frame; it only does anything once a second.</summary>
        public void Tick(float deltaTime)
        {
            _accum += deltaTime;
            if (_accum < 1f) return;
            _accum -= 1f;

            _trailing += _thisSecond - _buckets[_index];
            _buckets[_index] = _thisSecond;
            _thisSecond = 0d;
            _index = (_index + 1) % Window;
            if (_filled < Window) _filled++;
        }

        /// <summary>What the last minute managed. Reads 0 until the window has enough in it to mean something.</summary>
        public double PerMinute => _filled >= MinTrusted ? _trailing * (60d / _filled) : 0d;

        /// <summary>Whether <see cref="PerMinute"/> is worth showing yet, as opposed to honestly zero.</summary>
        public bool Ready => _filled >= MinTrusted;

        /// <summary>Forgets everything measured. Sailing to another island is not a slow minute on this one.</summary>
        public void Reset()
        {
            for (int i = 0; i < _buckets.Length; i++) _buckets[i] = 0d;
            _index = _filled = 0;
            _trailing = _thisSecond = 0d;
            _accum = 0f;
        }
    }
}
