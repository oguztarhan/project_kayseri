using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The world the lane runs through: water, a port at each end, and enough scattered along the way
    /// to see the ship moving past.
    ///
    /// Primitives, deliberately, and this is the precedent rather than a shortcut — Docs/VOYAGES.md
    /// §20 shipped the dock as three cubes and §23 replaced them with models once the feature had
    /// been played. The shape of the scene is the thing to get right first; the models are an
    /// afternoon in Blender afterwards and cannot tell you whether the crossing reads.
    ///
    /// THE BUOYS ARE NOT DRESSING. A hull on an empty plane has no parallax: it can be moving at any
    /// speed or none, and the eye cannot tell. Something fixed passing the camera is the whole of what
    /// makes a crossing feel like one, and it is the cheapest thing in the scene.
    /// </summary>
    public sealed class SeaScene : MonoBehaviour
    {
        private static readonly Color Water = new Color(0.09f, 0.30f, 0.46f, 1f);
        private static readonly Color Deep = new Color(0.05f, 0.19f, 0.32f, 1f);
        private static readonly Color Stone = new Color(0.45f, 0.47f, 0.52f, 1f);
        private static readonly Color Timber = new Color(0.42f, 0.30f, 0.19f, 1f);
        private static readonly Color Foam = new Color(0.78f, 0.88f, 0.95f, 1f);

        /// <summary>How far past each port the water keeps going, so the horizon is never an edge.</summary>
        private const float Margin = 700f;

        /// <summary>Buoys along the route. Odd count so none of them lands exactly on the turn.</summary>
        private const int BuoyCount = 23;

        public void Build(SeaLane lane, Color homeTint)
        {
            if (lane == null) return;
            BuildWater(lane);
            BuildPort(lane, 0f, homeTint, "EvLimani");
            BuildPort(lane, 1f, Stone, "UzakLiman");
            BuildBuoys(lane);
        }

        private void BuildWater(SeaLane lane)
        {
            // One slab, wide enough that the camera never finds its edge. A plane primitive is 10
            // units across, so the scale is the span over ten.
            float span = lane.Length + Margin * 2f;
            var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            water.name = "Su";
            water.transform.SetParent(transform, false);
            water.transform.position = new Vector3(lane.Length * 0.5f, lane.WaterY, 0f);
            water.transform.localScale = new Vector3(span / 10f, 1f, span / 10f);
            Paint(water, Water);
            // Nothing collides out here; the ship is placed by the clock, not by physics.
            Object.Destroy(water.GetComponent<Collider>());

            // A darker slab under it, offset, so the water has a floor to read against where the
            // buoys break the surface. Cheaper than a shader and it survives the SRP batcher.
            var deep = GameObject.CreatePrimitive(PrimitiveType.Plane);
            deep.name = "Derinlik";
            deep.transform.SetParent(transform, false);
            deep.transform.position = new Vector3(lane.Length * 0.5f, lane.WaterY - 3f, 0f);
            deep.transform.localScale = water.transform.localScale;
            Paint(deep, Deep);
            Object.Destroy(deep.GetComponent<Collider>());
        }

        /// <summary>A jetty and a light at one end of the lane — where she left, and where she is going.</summary>
        private void BuildPort(SeaLane lane, float u, Color tint, string name)
        {
            var root = new GameObject(name);
            root.transform.SetParent(transform, false);
            Vector3 at = lane.Point(u);
            // Set back from the lane's own end so the ship arrives BESIDE the port rather than inside it.
            Vector3 back = lane.Heading(u, true) * (u <= 0.5f ? -46f : 46f);
            root.transform.position = at + back;

            var rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rock.name = "Kaya";
            rock.transform.SetParent(root.transform, false);
            rock.transform.localScale = new Vector3(78f, 16f, 78f);
            rock.transform.localPosition = new Vector3(0f, 3f, 0f);
            rock.transform.localRotation = Quaternion.Euler(0f, 24f, 0f);
            Paint(rock, tint);
            Object.Destroy(rock.GetComponent<Collider>());

            var jetty = GameObject.CreatePrimitive(PrimitiveType.Cube);
            jetty.name = "Iskele";
            jetty.transform.SetParent(root.transform, false);
            jetty.transform.localScale = new Vector3(46f, 3f, 12f);
            jetty.transform.localPosition = new Vector3(u <= 0.5f ? 38f : -38f, 10f, 0f);
            Paint(jetty, Timber);
            Object.Destroy(jetty.GetComponent<Collider>());

            var lamp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lamp.name = "Fener";
            lamp.transform.SetParent(root.transform, false);
            lamp.transform.localScale = new Vector3(7f, 17f, 7f);
            lamp.transform.localPosition = new Vector3(0f, 26f, 0f);
            Paint(lamp, Foam);
            Object.Destroy(lamp.GetComponent<Collider>());
        }

        /// <summary>
        /// Markers down both sides of the route. Spread along it rather than dotted at random, because
        /// what they are for is telling the eye how fast the hull is going past — and something that
        /// arrives at a steady rate says that far better than a scatter does.
        /// </summary>
        private void BuildBuoys(SeaLane lane)
        {
            var root = new GameObject("Samandiralar");
            root.transform.SetParent(transform, false);

            for (int i = 0; i < BuoyCount; i++)
            {
                float u = (i + 0.5f) / BuoyCount;
                // Alternating sides, at a distance that wobbles so the pair never reads as a gate.
                float side = (i % 2 == 0 ? 1f : -1f) * (74f + 34f * Mathf.Abs(Mathf.Sin(i * 2.399f)));
                Vector3 at = lane.Beside(u, side);

                var buoy = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                buoy.name = "Samandira_" + i;
                buoy.transform.SetParent(root.transform, false);
                buoy.transform.position = at + new Vector3(0f, 2.4f, 0f);
                buoy.transform.localScale = new Vector3(4.6f, 5.2f, 4.6f);
                buoy.transform.localRotation = Quaternion.Euler(Mathf.Sin(i * 1.7f) * 9f, 0f,
                                                                Mathf.Cos(i * 1.3f) * 9f);
                Paint(buoy, i % 4 == 0 ? Foam : Stone);
                Object.Destroy(buoy.GetComponent<Collider>());
            }
        }

        private static void Paint(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = MarketYardBuild.Mat(c);
        }
    }
}
