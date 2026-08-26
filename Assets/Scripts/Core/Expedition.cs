using System;

namespace Game.Core
{
    /// <summary>
    /// Where a voyage actually IS, as pure maths: how far along her clock she has run, where that
    /// puts her on the lane between the two ports, and which way she is pointing.
    ///
    /// WHY THIS EXISTS SEPARATELY FROM <see cref="Voyages"/>. That file owns what a voyage COSTS and
    /// what it PAYS, and it is deliberately blind to anything on screen — it has no idea a sea scene
    /// exists. This is the other half: a voyage is already a wall-clock deadline, and turning that
    /// deadline into a position is the only new arithmetic the sea needs. Keeping it here rather than
    /// in the scene means the ship's position is a function of the save, testable without a camera.
    ///
    /// THE SHIP IS NEVER DRIVEN. Docs/FIVE_LAYERS.md §4 states the rule the whole layer rests on:
    /// active sailing may only improve a voyage's outcome, never worsen it. The cleanest way to hold
    /// that is for the scene to be a WINDOW rather than a controller — the voyage runs on the same
    /// wall clock whether or not anybody is watching, and this file only ever reads it. Nothing here
    /// can move a ship, shorten a route or change what she brings home, and S2's encounters will add
    /// reward on top rather than taking the wheel.
    ///
    /// OUT AND BACK ON ONE LANE. A voyage is a round trip, so progress 0..0.5 runs the lane forwards
    /// and 0.5..1 runs it back. One lane rather than two means the far port is a real place the
    /// player watched themselves reach, which is most of what makes the wait legible.
    /// </summary>
    public static class Expedition
    {
        /// <summary>
        /// How far through her voyage a ship is, 0..1. Zero before she sails, one once she is home.
        ///
        /// A voyage with no duration reads as finished rather than as a division by nothing — the same
        /// answer <see cref="Goals.Progress"/> gives a zero target, and for the same reason: a bar that
        /// cannot fill must not sit at zero forever.
        /// </summary>
        public static double Progress(long sailedUnix, long returnsUnix, long nowUnix)
        {
            if (sailedUnix <= 0L) return 0d;
            long span = returnsUnix - sailedUnix;
            if (span <= 0L) return 1d;
            double t = (nowUnix - sailedUnix) / (double)span;
            return t < 0d ? 0d : (t > 1d ? 1d : t);
        }

        /// <summary>Seconds until she is home. Never negative.</summary>
        public static double SecondsLeft(long returnsUnix, long nowUnix)
        {
            double left = returnsUnix - nowUnix;
            return left < 0d ? 0d : left;
        }

        /// <summary>True while she is still heading away from home.</summary>
        public static bool Outbound(double progress) => progress < 0.5d;

        /// <summary>
        /// Where along the LANE she is, 0 at home and 1 at the far port — the round trip folded onto
        /// one path. Peaks at exactly 1 halfway through the voyage, which is the moment she turns.
        /// </summary>
        public static double LanePosition(double progress)
        {
            double t = progress < 0d ? 0d : (progress > 1d ? 1d : progress);
            return t <= 0.5d ? t * 2d : (1d - t) * 2d;
        }

        // ------------------------------------------------------------------- lane
        /// <summary>
        /// The lane's shape: a long axis with a gentle double bend across it.
        ///
        /// Not a straight line, and not for decoration. A ship crossing a featureless plane in a
        /// straight line reads as a sprite sliding across a background — there is no parallax, the
        /// hull never turns, and the eye cannot tell motion from a still image. One slow S gives the
        /// hull a heading that visibly changes, which is the cheapest possible way to make a boat look
        /// like it is sailing rather than being dragged.
        /// </summary>
        public static void PointOnLane(double u, double length, double sway, out double x, out double z)
        {
            double t = u < 0d ? 0d : (u > 1d ? 1d : u);
            x = length * t;
            z = sway * Math.Sin(t * Math.PI * 2d);
        }

        /// <summary>
        /// The way she is pointing at <paramref name="u"/>, as a unit vector. The lane's own tangent
        /// while outbound, and its reverse on the way home — she turns at the far port rather than
        /// sailing home backwards.
        /// </summary>
        public static void HeadingOnLane(double u, double length, double sway, bool outbound,
                                         out double dx, out double dz)
        {
            double t = u < 0d ? 0d : (u > 1d ? 1d : u);
            dx = length;
            dz = sway * Math.PI * 2d * Math.Cos(t * Math.PI * 2d);

            double len = Math.Sqrt(dx * dx + dz * dz);
            if (len <= 0d) { dx = 1d; dz = 0d; return; }
            dx /= len;
            dz /= len;
            if (!outbound) { dx = -dx; dz = -dz; }
        }

        /// <summary>
        /// A point across the lane from <paramref name="u"/> — the lane's normal, scaled. This is what
        /// S2 will hang encounters off: a threat wants to sit beside the route rather than on it, and
        /// "beside" has to mean the same thing wherever the lane happens to be bending.
        /// </summary>
        public static void OffsetFromLane(double u, double length, double sway, double distance,
                                          out double x, out double z)
        {
            PointOnLane(u, length, sway, out x, out z);
            HeadingOnLane(u, length, sway, true, out double dx, out double dz);
            // Left normal of the heading.
            x += -dz * distance;
            z += dx * distance;
        }
    }
}
