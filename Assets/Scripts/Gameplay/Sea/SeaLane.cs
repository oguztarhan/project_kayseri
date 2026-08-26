using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The route, in world space: the long axis between the two ports and the bend across it.
    ///
    /// A thin wrapper on <see cref="Expedition"/>'s lane maths, and thin on purpose — the shape of the
    /// route is a fact about the voyage, not about the scene, so it lives in Core where a test can
    /// reach it. What this adds is the world it is drawn in: metres, a Y for the waterline, and the
    /// conversion from the lane's own 0..1 to a <see cref="Vector3"/>.
    ///
    /// S2 wants <see cref="Beside"/>. An encounter sits off the route rather than on it, and "off the
    /// route" has to keep meaning the same thing wherever the lane happens to be bending.
    /// </summary>
    public sealed class SeaLane : MonoBehaviour
    {
        [Tooltip("Iki liman arasindaki mesafe, dunya birimi. Rota bu eksende uzuyor.")]
        [SerializeField, Min(50f)] private float length = 900f;

        [Tooltip("Rotanin yanal savrulmasi. Sifir duz bir cizgi demek — ve duz bir cizgide giden " +
                 "gemi hareket ediyormus gibi durmuyor, cunku govde hic donmuyor.")]
        [SerializeField, Min(0f)] private float sway = 110f;

        [Tooltip("Su seviyesi. Govde bunun uzerinde yuzuyor.")]
        [SerializeField] private float waterY = 0f;

        public float Length => length;
        public float Sway => sway;
        public float WaterY => waterY;

        /// <summary>Where <paramref name="u"/> (0 = home, 1 = the far port) is in the world.</summary>
        public Vector3 Point(float u)
        {
            Expedition.PointOnLane(u, length, sway, out double x, out double z);
            return new Vector3((float)x, waterY, (float)z);
        }

        /// <summary>Which way a hull at <paramref name="u"/> is pointing.</summary>
        public Vector3 Heading(float u, bool outbound)
        {
            Expedition.HeadingOnLane(u, length, sway, outbound, out double dx, out double dz);
            return new Vector3((float)dx, 0f, (float)dz);
        }

        /// <summary>
        /// A point <paramref name="distance"/> to the port side of the lane at <paramref name="u"/> —
        /// negative for starboard. What S2 hangs encounters off.
        /// </summary>
        public Vector3 Beside(float u, float distance)
        {
            Expedition.OffsetFromLane(u, length, sway, distance, out double x, out double z);
            return new Vector3((float)x, waterY, (float)z);
        }

        public Vector3 Home => Point(0f);
        public Vector3 Away => Point(1f);
    }
}
