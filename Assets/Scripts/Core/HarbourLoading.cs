using System;

namespace Game.Core
{
    /// <summary>Bounded visual parcels. A departing ship never takes a parcel still on the crane.</summary>
    public sealed class HarbourLoading
    {
        public const int QueueCapacity = 4, DeckCapacity = 3;
        private readonly double _seconds;
        private double _elapsed;
        public int Waiting { get; private set; }
        public int Loaded { get; private set; }
        public bool Active { get; private set; }
        public double Progress => Active ? _elapsed / _seconds : 0d;

        public HarbourLoading(double seconds) { _seconds = Math.Max(1d, seconds); }
        public bool Enqueue()
        {
            if (Waiting + (Active ? 1 : 0) >= QueueCapacity) return false;
            Waiting++;
            return true;
        }
        public void Tick(double dt, bool moored)
        {
            if (!moored)
            {
                if (Active) { Waiting++; Active = false; _elapsed = 0; }
                return;
            }
            if (dt <= 0 || double.IsNaN(dt) || double.IsInfinity(dt)) return;
            while (dt > 0 && Loaded < DeckCapacity)
            {
                if (!Active)
                {
                    if (Waiting == 0) return;
                    Waiting--; Active = true;
                }
                double step = Math.Min(dt, _seconds - _elapsed);
                dt -= step; _elapsed += step;
                if (_elapsed < _seconds) return;
                Loaded++; Active = false; _elapsed = 0;
            }
        }
        public void NewVisit()
        {
            Tick(0, false);
            Loaded = 0;
        }
    }
}
