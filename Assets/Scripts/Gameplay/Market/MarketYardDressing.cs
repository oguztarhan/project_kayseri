using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The things standing around the edges of a market yard, and the things bolted to its walls:
    /// barrels, crates, pallets, sacks, a hand truck, a bench, a scale — and up on the wall a vent, a
    /// clock, a run of pipes, a notice board and a fire extinguisher.
    ///
    /// Which props is <see cref="MarketTheme"/>'s business — this file only knows where they may
    /// stand, how to make a model from an art pack the right size for this room, and what colour each
    /// part of one should be.
    ///
    /// WHY THE WALLS. The floor was full long before the yard looked full. Every spot below is a piece
    /// of ground the machinery does not use, and that list was already as long as it can be: the ramp
    /// owns the north, the stock pad the east middle, the counter and the queue the south-west, the
    /// upgrade rank the west wall, and the two doorways and the painted floor signs own the lines the
    /// player walks. The departing customers' lane runs the whole length of the south strip at z = -18,
    /// which rules out most of what is left. So the room got fuller by going UP: the wall fixtures cost
    /// no floor at all, they cannot be walked into, and they hang on the only two interior faces this
    /// camera ever sees.
    ///
    /// PALLETS do the same trick a second time. Standing a crate on a pallet is two objects in the
    /// footprint of one, and it is what the corner of a depot actually looks like — nothing in a
    /// warehouse sits on bare concrete.
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

        /// <summary>What goes under a stacked prop, and how big it is made.</summary>
        private const string PalletResource = "pallet";
        private const float PalletSize = 1.55f;

        /// <summary>
        /// A prop bigger than this is not stood on a pallet — it would wear it as a hat.
        ///
        /// Generous, because a crate overhanging its pallet is what a real one does. What this is
        /// really keeping off is the chimney and the tank, which are towers.
        /// </summary>
        private const float PalletFitsUnder = 3.0f;

        /// <summary>
        /// Where a prop may stand, which way it faces, how big it is allowed to get, and whether it
        /// stands on a pallet.
        ///
        /// The size CAP is what let the list grow. Every spot used to take whatever
        /// <see cref="MarketTheme"/> handed it, so every spot had to be clear enough for the largest
        /// prop in the game — which is a four-unit chimney — and only eight places in the room were.
        /// A spot that declares its own ceiling can sit in a gap that only a small thing fits in.
        /// </summary>
        private readonly struct Spot
        {
            public readonly Vector3 At;
            public readonly float Facing;
            public readonly float MaxSize;
            public readonly bool Pallet;

            public Spot(float x, float z, float facing, float maxSize, bool pallet = false,
                        float y = 0f)
            {
                At = new Vector3(x, y, z);
                Facing = facing;
                MaxSize = maxSize;
                Pallet = pallet;
            }
        }

        /// <summary>
        /// The floor spots. Facings point back into the room: these all sit against a wall, and a crate
        /// showing the player its back across the width of the yard is a crate that may as well be a box.
        ///
        /// Every one is clear of its wall by at least the half-diagonal of the largest prop it will
        /// accept. Props are turned by a hash, so the number that has to clear the wall is the diagonal
        /// and not the width — square to the wall they all fitted, and a crate that happened to land at
        /// forty-five degrees put a corner through it.
        ///
        /// NOTHING stands along the west wall, and that is the rank of upgrade pads' doing rather than
        /// the wall's. Six pads with 3.4-wide painted faces leave gaps of about one and a half units
        /// between them; a prop that fits in one of those does not clear the wall, and a prop that
        /// clears the wall stands on somebody's price tag.
        ///
        /// The last two are the only genuinely empty floor left in the room: the corner west of the
        /// upgrade pylon, and the strip between the stock pad's slab and the east wall just north of
        /// the doorway. Both are small, and both say so.
        /// </summary>
        private static readonly Spot[] Spots =
        {
            new Spot( 20.0f,  16.6f, 225f, 4.2f),          // north-east corner, past the end of the ramp
            new Spot( 20.0f,  11.8f, 250f, 3.0f, true),    // east wall, north of the stock pad
            new Spot( 16.4f,  17.0f, 200f, 3.0f),          // north wall, between the ramp and the corner
            new Spot( 20.0f, -13.8f, 290f, 3.0f, true),    // east wall, south of the doorway
            new Spot( 18.6f, -17.1f, 315f, 3.0f, true),    // south-east corner
            new Spot( 14.6f, -17.1f, 340f, 3.0f, true),    // south wall, east of the customers' door frame
            new Spot( -4.5f, -17.5f,   0f, 3.0f, true),    // south strip, behind the queue lane
            new Spot(-11.0f, -17.5f,  20f, 3.0f, true),    // south strip, west of it and short of the pad
            new Spot(-21.0f,  17.8f, 150f, 1.6f, true),    // north-west corner, west of the upgrade pylon
            new Spot( 20.0f,   6.0f, 265f, 1.6f, true),    // east wall, between the stock pad and the gate

            // The strip along the front of the ramp, and it is the only floor in the middle of the
            // room that is genuinely dead: the ramp's face is at z = 12 and the stock pad's slab stops
            // at 9.5, so this two-and-a-half-unit band is behind everywhere the player works and in
            // front of a wall of steel he cannot climb. Goods tipped off the ramp and not yet moved.
            // Capped small and centred at 10.7 so nothing here can reach either edge.
            new Spot(-12.0f,  10.7f,   0f, 1.6f, true),
            new Spot( -5.5f,  10.7f,  14f, 1.6f, true),
            new Spot(  2.5f,  10.7f, 348f, 1.6f, true),
            new Spot( 13.5f,  10.7f,  20f, 1.6f, true),

            // And ON the ramp, which is the largest flat surface in the room and had nothing on it.
            // It is the one place goods obviously belong — the lorries tip into it — and nothing ever
            // walks there: the deck is 3.2 up and the player has no way onto it. The mouth of the
            // chute stands at x = -4.5 to 4.5, so these keep to either side of it.
            new Spot(-10.5f,  15.0f,  10f, 1.8f, true, 3.2f),
            new Spot( -6.6f,  12.9f, 340f, 1.4f, false, 3.2f),
            new Spot(  7.6f,  15.0f, 200f, 1.8f, true, 3.2f),
            new Spot( 11.6f,  13.0f, 160f, 1.4f, false, 3.2f),
        };

        /// <summary>
        /// The wall fixtures: which model, where its back plate sits, which way it looks and how wide
        /// it is made. Fixed rather than themed — a fire extinguisher is red on every island — and the
        /// same in every yard, because these are the building rather than the trade.
        ///
        /// Only the north and east walls, and only where nothing already covers them. The ramp hides
        /// the north wall from the floor to 3.2 across its whole 28-unit width, the roofline beam
        /// starts at 4.14, and the price board, the two gate plates, the banners and the upgrade pylon
        /// each own a piece of what is left. What these five sit in is the rest of it.
        /// </summary>
        private readonly struct Fixture
        {
            public readonly string Resource;
            public readonly Vector3 At;
            public readonly float Yaw;
            public readonly float Size;

            public Fixture(string resource, float x, float y, float z, float yaw, float size)
            {
                Resource = resource;
                At = new Vector3(x, y, z);
                Yaw = yaw;
                Size = size;
            }
        }

        /// <summary>Where the inner faces of the two walls the camera can see actually are.</summary>
        private const float NorthFace = MarketYardBuild.Depth * 0.5f - MarketYardBuild.WallThickness * 0.5f;
        private const float EastFace = MarketYardBuild.Width * 0.5f - MarketYardBuild.WallThickness * 0.5f;

        private static readonly Fixture[] Fixtures =
        {
            // North wall, in the panels either side of the ramp. Turned to face south, at the camera.
            new Fixture("vent",         -15.2f, 3.30f, NorthFace - 0.20f, 180f, 1.90f),
            new Fixture("clock",         16.3f, 3.35f, NorthFace - 0.20f, 180f, 1.35f),
            new Fixture("extinguisher",  18.9f, 2.05f, NorthFace - 0.20f, 180f, 1.05f),
            // East wall. The north segment above the stock pad takes the pipe run — high, so it clears
            // the price board under it — and the south segment takes the notice board.
            new Fixture("pipes",  EastFace - 0.22f, 3.68f,  16.4f, -90f, 4.20f),
            new Fixture("notice", EastFace - 0.20f, 2.45f, -10.0f, -90f, 1.90f),
        };

        /// <summary>
        /// What each named part of an authored prop is painted. The model's object names ARE the roles:
        /// a mesh called <c>M_Ahsap</c> is timber, <c>M_Metal</c> is steel, and so on — see
        /// <c>Tools/blender/market_props.py</c>, which is where they get those names.
        ///
        /// Two of the roles are the ISLAND's and the rest are not, and the split is the whole point. A
        /// pallet in a coal yard should be as sooty as the walls around it, so timber and steel come
        /// out of <see cref="MarketTheme"/>. A fire extinguisher is red in a coal yard and red in a
        /// gold one, because a fire extinguisher that changed colour per island would stop reading as
        /// a fire extinguisher.
        ///
        /// Anything NOT named <c>M_*</c> keeps whatever material it arrived with, and that is what
        /// leaves the Kenney packs alone — those props came painted and repainting them grey would be
        /// throwing away the only colour in the room.
        /// </summary>
        private static Material RoleMaterial(string partName, MarketTheme.Palette theme)
        {
            if (partName == null || !partName.StartsWith("M_")) return null;

            // Blender hands the second object of a role the name "M_Ahsap.001", so the role is read up
            // to the first dot as well as the first underscore.
            int start = 2;
            int end = partName.Length;
            for (int i = start; i < partName.Length; i++)
                if (partName[i] == '.' || partName[i] == '_') { end = i; break; }
            string role = partName.Substring(start, end - start);

            switch (role)
            {
                case "Ahsap":   return MarketSurfaces.Get(theme.Trim, MarketSurfaces.Finish.Wood);
                case "Metal":   return MarketSurfaces.Get(theme.Metal, MarketSurfaces.Finish.Metal);
                case "Tas":     return MarketSurfaces.Get(theme.Slab, MarketSurfaces.Finish.Floor);
                case "Bez":     return MarketSurfaces.Get(new Color(0.80f, 0.72f, 0.56f),
                                                          MarketSurfaces.Finish.Banner);
                case "Yesil":   return MarketSurfaces.Get(new Color(0.28f, 0.55f, 0.26f),
                                                          MarketSurfaces.Finish.Plain);
                case "Kirmizi": return MarketSurfaces.Get(new Color(0.74f, 0.15f, 0.13f),
                                                          MarketSurfaces.Finish.Plain);
                case "Turuncu": return MarketSurfaces.Get(new Color(0.93f, 0.44f, 0.10f),
                                                          MarketSurfaces.Finish.Plain);
                case "Beyaz":   return MarketSurfaces.Get(new Color(0.90f, 0.90f, 0.87f),
                                                          MarketSurfaces.Finish.Plain);
                default:        return null;
            }
        }

        /// <summary>
        /// Stands this island's props in the yard, hangs its fixtures on the walls, and hands back the
        /// holder they all live under.
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

            var palletPrefab = Resources.Load<GameObject>(PropRoot + PalletResource);

            for (int i = 0; i < Spots.Length; i++)
            {
                Spot spot = Spots[i];
                MarketTheme.Prop prop = theme.Props[i % theme.Props.Length];
                var prefab = Resources.Load<GameObject>(PropRoot + prop.Resource);
                if (prefab == null) continue;

                var stand = new GameObject(prop.Resource).transform;
                stand.SetParent(holder, false);
                stand.localPosition = spot.At;
                // A few degrees off the nominal facing, taken from the same deterministic hash the ore
                // pool is jittered with. A row of props all square to their wall reads as a shop display.
                stand.localRotation = Quaternion.Euler(
                    0f, spot.Facing + (BoxMeshBuilder.Hash(77, i) - 0.5f) * 26f, 0f);

                float size = Mathf.Min(prop.Size, spot.MaxSize);
                float floorY = stand.position.y;

                // The pallet first, so the thing on it knows how high its floor is. Never under
                // another pallet: the theme lists hand a pallet out as a prop in its own right, and a
                // pallet stood on a pallet reads as a bug rather than as a stack.
                if (spot.Pallet && palletPrefab != null && size <= PalletFitsUnder
                    && prop.Resource != PalletResource)
                {
                    Transform pallet = Body(palletPrefab, stand, "Palet", theme);
                    Fit(pallet, PalletSize, floorY);
                    Bounds under;
                    if (TryBounds(pallet, out under)) floorY = under.max.y;
                }

                Fit(Body(prefab, stand, "Model", theme), size, floorY);
            }

            Fittings(holder, theme);
            return holder;
        }

        /// <summary>
        /// The wall fixtures. Placed rather than fitted to the floor: these hang, and the whole point
        /// of them is that they are at a height nothing else in the room occupies.
        ///
        /// Scaled by their longest side like everything else, but with the scale worked out and applied
        /// WITHOUT the drop onto the ground that <see cref="Fit"/> ends with — a fire extinguisher
        /// dropped to the floor is a fire extinguisher lying on the floor.
        /// </summary>
        private static void Fittings(Transform holder, MarketTheme.Palette theme)
        {
            for (int i = 0; i < Fixtures.Length; i++)
            {
                Fixture fixture = Fixtures[i];
                var prefab = Resources.Load<GameObject>(PropRoot + fixture.Resource);
                if (prefab == null) continue;

                var mount = new GameObject(fixture.Resource).transform;
                mount.SetParent(holder, false);
                mount.localPosition = fixture.At;
                mount.localRotation = Quaternion.Euler(0f, fixture.Yaw, 0f);

                Transform body = Body(prefab, mount, "Model", theme);
                Bounds bounds;
                if (!TryBounds(body, out bounds)) continue;
                Vector3 span = bounds.size;
                float longest = Mathf.Max(span.x, Mathf.Max(span.y, span.z));
                body.localScale = Vector3.one * (fixture.Size / Mathf.Max(0.001f, longest));
            }
        }

        /// <summary>
        /// Instantiates a prop, paints its named parts and takes its colliders off.
        ///
        /// Scenery. The player has the only CharacterController in the yard and these stand against
        /// walls he is already stopped by — a collider here is only ever something to snag on.
        /// </summary>
        private static Transform Body(GameObject prefab, Transform parent, string name,
                                      MarketTheme.Palette theme)
        {
            GameObject body = Object.Instantiate(prefab, parent, false);
            body.name = name;

            var parts = body.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < parts.Length; i++)
            {
                Material paint = RoleMaterial(parts[i].gameObject.name, theme);
                if (paint != null) parts[i].sharedMaterial = paint;
            }

            Collider[] hazards = body.GetComponentsInChildren<Collider>(true);
            for (int c = 0; c < hazards.Length; c++) Object.Destroy(hazards[c]);
            return body.transform;
        }

        /// <summary>
        /// Scales a model until its longest side is <paramref name="size"/>, then drops it onto
        /// <paramref name="floorY"/> — the concrete, or the top of the pallet it is standing on.
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
