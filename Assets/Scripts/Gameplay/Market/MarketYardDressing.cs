using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The things standing around the edges of a market yard: barrels, crates, containers, a chimney.
    /// Which ones is <see cref="MarketTheme"/>'s business — this file only knows where they may stand
    /// and how to make a model from an art pack the right size for this room.
    ///
    /// Every spot is a piece of floor the yard's own machinery does not use. That constraint is the whole
    /// design of the list below: the ramp owns the north, the stock pad owns the east middle, the counter
    /// and the queue own the south-west, the upgrade rank owns the west wall, and the two doorways and the
    /// painted floor signs own the lines the player actually walks. What is left is the corners, and
    /// dressing is the right thing to put in a corner — it is the one addition to the yard that can never
    /// get between the player and something he is trying to do.
    ///
    /// Models are loaded from <c>Resources</c> rather than wired in the Inspector because a yard builds
    /// itself at runtime and there are eight of them; a missing one is skipped, so a project without the
    /// props folder still gets a market, just an empty-cornered one.
    /// </summary>
    public static class MarketYardDressing
    {
        private const string PropRoot = "Market/Props/";

        /// <summary>What the holder is called under the yard root, so the yard can switch it off.</summary>
        public const string HolderName = "Susleme";

        /// <summary>
        /// Where a prop may stand and which way it faces, in yard-local units. Facings point back into
        /// the room: these all sit against a wall, and a crate showing the player its back across the
        /// width of the yard is a crate that may as well be a box.
        ///
        /// Every one of them is at least 2.45 clear of the wall behind it, which is the half-diagonal of
        /// the largest prop <see cref="MarketTheme"/> will hand over. Props are turned by a hash, so the
        /// number that has to clear the wall is the diagonal and not the width — square to the wall they
        /// all fitted, and a crate that happened to land at forty-five degrees put a corner through it.
        ///
        /// NOTHING stands along the west wall, and that is the rank of upgrade pads' doing rather than
        /// the wall's. Six pads with 3.4-wide painted faces leave gaps of about one and a half units
        /// between them; a prop that fits in one of those does not clear the wall, and a prop that
        /// clears the wall stands on somebody's price tag. The room's spare floor is the east side and
        /// the strip south of the queue lane, so that is where the dressing lives.
        /// </summary>
        private static readonly Vector3[] Spots =
        {
            new Vector3( 20.0f, 0f,  16.6f),   // north-east corner, past the end of the ramp
            new Vector3( 20.0f, 0f,  11.8f),   // east wall, north of the stock pad
            new Vector3( 16.4f, 0f,  17.0f),   // north wall, in the gap between the ramp and the corner
            new Vector3( 20.0f, 0f, -13.8f),   // east wall, south of the doorway and clear of the floor sign
            new Vector3( 18.6f, 0f, -17.1f),   // south-east corner
            new Vector3( 14.6f, 0f, -17.1f),   // south wall, east of the customers' door frame
            new Vector3( -4.5f, 0f, -17.5f),  // south strip, behind the queue lane and clear of its walk-in
            new Vector3(-11.0f, 0f, -17.5f),   // south strip, west of it and short of the bottom pad
        };

        private static readonly float[] Facings = { 225f, 250f, 200f, 290f, 315f, 340f, 0f, 20f };

        /// <summary>
        /// Stands this island's props in the yard and hands back the holder they live under.
        ///
        /// The holder matters: dressing is the only thing in a parked yard that would still cost draw
        /// calls, since it is scenery with no components to disable. Seven shut yards' worth of crates
        /// are hidden under their roofs anyway, so the yard switches this off with them.
        /// </summary>
        public static Transform Dress(Transform root, MarketTheme.Palette theme)
        {
            var holder = new GameObject(HolderName).transform;
            holder.SetParent(root, false);
            if (theme.Props == null || theme.Props.Length == 0) return holder;

            for (int i = 0; i < Spots.Length; i++)
            {
                MarketTheme.Prop prop = theme.Props[i % theme.Props.Length];
                var prefab = Resources.Load<GameObject>(PropRoot + prop.Resource);
                if (prefab == null) continue;

                var stand = new GameObject(prop.Resource).transform;
                stand.SetParent(holder, false);
                stand.localPosition = Spots[i];
                // A few degrees off the nominal facing, taken from the same deterministic hash the ore
                // pool is jittered with. A row of props all square to their wall reads as a shop display.
                stand.localRotation = Quaternion.Euler(
                    0f, Facings[i] + (BoxMeshBuilder.Hash(77, i) - 0.5f) * 26f, 0f);

                GameObject body = Object.Instantiate(prefab, stand, false);
                body.name = "Model";
                // Scenery. The player has the only CharacterController in the yard and these stand
                // against walls he is already stopped by — a collider here is only ever something to
                // snag on.
                Collider[] hazards = body.GetComponentsInChildren<Collider>(true);
                for (int c = 0; c < hazards.Length; c++) Object.Destroy(hazards[c]);

                Fit(body.transform, prop.Size, stand.position.y);
            }
            return holder;
        }

        /// <summary>
        /// Scales a model until its longest side is <paramref name="size"/>, then drops it onto the floor.
        ///
        /// Measured off its renderers rather than trusted from the asset, because the props come from
        /// three packs whose units disagree — a Kenney barrel is a third of a unit tall and a cargo
        /// container is nearly three long — and because a model authored around its middle would
        /// otherwise stand half sunk into the concrete.
        ///
        /// The bounds are read again AFTER scaling rather than divided through, because these are world
        /// bounds of an already-turned object: the box a rotated model occupies is not the scaled box of
        /// the unrotated one, and a prop set down by the arithmetic instead of the measurement floats.
        /// </summary>
        private static void Fit(Transform body, float size, float floorY)
        {
            Bounds bounds;
            if (!TryBounds(body, out bounds)) return;
            Vector3 span = bounds.size;
            float longest = Mathf.Max(span.x, Mathf.Max(span.y, span.z));
            body.localScale = Vector3.one * (size / Mathf.Max(0.001f, longest));
            if (!TryBounds(body, out bounds)) return;
            body.position += new Vector3(0f, floorY - bounds.min.y, 0f);
        }

        private static bool TryBounds(Transform body, out Bounds bounds)
        {
            bounds = default(Bounds);
            Renderer[] parts = body.GetComponentsInChildren<Renderer>();
            bool any = false;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == null) continue;
                if (!any) { bounds = parts[i].bounds; any = true; }
                else bounds.Encapsulate(parts[i].bounds);
            }
            return any;
        }
    }
}
