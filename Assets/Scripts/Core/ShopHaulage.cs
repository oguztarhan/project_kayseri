using System;

namespace Game.Core
{
    /// <summary>Bounded visual dispatches, not inventory. Reads cumulative completed shop deliveries only.</summary>
    public sealed class ShopHaulage
    {
        public enum Phase { Idle, Loading, Carrying, Unloading, Returning }
        public const int Capacity = 3;
        private readonly double _firstTravel, _secondTravel, _handling;
        private double _firstLeft, _secondLeft;
        private long _seen = -1;
        public Phase First { get; private set; }
        public Phase Second { get; private set; }
        public int Waiting { get; private set; }
        public int AtDepot { get; private set; }
        public long Delivered { get; private set; }

        public ShopHaulage(double firstTravel, double secondTravel, double handling)
        {
            if (!Positive(firstTravel) || !Positive(secondTravel) || !Positive(handling))
                throw new ArgumentOutOfRangeException(nameof(firstTravel));
            _firstTravel = firstTravel; _secondTravel = secondTravel; _handling = handling;
        }

        private static bool Positive(double v) => v > 0 && !double.IsNaN(v) && !double.IsInfinity(v);
        public void Baseline(long delivered) => _seen = Math.Max(0, delivered);

        public void Observe(long delivered)
        {
            delivered = Math.Max(0, delivered);
            if (_seen >= 0 && delivered > _seen)
                Waiting += (int)Math.Min(Capacity - Waiting, delivered - _seen);
            _seen = delivered;
        }

        public double Progress(bool first)
        {
            Phase phase = first ? First : Second;
            if (phase == Phase.Idle) return 0;
            double duration = phase == Phase.Carrying || phase == Phase.Returning
                ? first ? _firstTravel : _secondTravel : _handling;
            return Math.Max(0, Math.Min(1, 1 - (first ? _firstLeft : _secondLeft) / duration));
        }

        public void Tick(double seconds)
        {
            if (!Positive(seconds)) return;
            // Nearest transition first: the second carrier cannot spend time before the parcel arrives.
            for (int events = 0; events < 128; events++)
            {
                if (First == Phase.Idle && Waiting > 0 && AtDepot < Capacity)
                { Waiting--; First = Phase.Loading; _firstLeft = _handling; }
                if (Second == Phase.Idle && AtDepot > 0)
                { AtDepot--; Second = Phase.Loading; _secondLeft = _handling; }
                if (seconds <= 0 || First == Phase.Idle && Second == Phase.Idle) return;
                double step = Math.Min(seconds, Math.Min(First == Phase.Idle ? double.PositiveInfinity : _firstLeft,
                    Second == Phase.Idle ? double.PositiveInfinity : _secondLeft));
                seconds -= step;
                if (First != Phase.Idle) _firstLeft -= step;
                if (Second != Phase.Idle) _secondLeft -= step;
                if (First != Phase.Idle && _firstLeft <= 0)
                {
                    switch (First)
                    {
                        case Phase.Loading: First = Phase.Carrying; _firstLeft = _firstTravel; break;
                        case Phase.Carrying: First = Phase.Unloading; _firstLeft = _handling; break;
                        case Phase.Unloading: AtDepot++; First = Phase.Returning; _firstLeft = _firstTravel; break;
                        case Phase.Returning: First = Phase.Idle; break;
                    }
                }
                if (Second != Phase.Idle && _secondLeft <= 0)
                {
                    switch (Second)
                    {
                        case Phase.Loading: Second = Phase.Carrying; _secondLeft = _secondTravel; break;
                        case Phase.Carrying: Second = Phase.Unloading; _secondLeft = _handling; break;
                        case Phase.Unloading: Delivered++; Second = Phase.Returning; _secondLeft = _secondTravel; break;
                        case Phase.Returning: Second = Phase.Idle; break;
                    }
                }
            }
        }
    }
}
