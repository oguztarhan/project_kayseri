using UnityEngine;

using Finish = Game.Gameplay.MarketSurfaces.Finish;

namespace Game.Gameplay
{
    /// <summary>
    /// Puts up a greybox market yard in code: floor, four walls, a roof, the unloading ramp, the stock
    /// pad and the counter, in the layout the design calls for.
    ///
    /// Built rather than authored on purpose, and only for now. The island does the same thing — roads,
    /// piles and site dressing are all constructed at runtime — so a yard that assembles itself is in
    /// keeping, and while the layout is still being argued about it is far cheaper to move a number
    /// here than to re-drag a prefab. Authored art replaces the boxes once the loop plays right;
    /// <see cref="Anchor"/> is what it will hang off, so nothing downstream has to change.
    ///
    /// Every piece is a plain box with a shared material per colour, so the whole yard is a handful of
    /// draw calls even before batching. The SRP batcher keeps them together — nothing here touches a
    /// MaterialPropertyBlock.
    /// </summary>
    public static class MarketYardBuild
    {
        /// <summary>Named spots the yard's contents attach to. The greybox and the authored art share them.</summary>
        public enum Anchor { PlayerStart, Ramp, StockPad, Counter, Queue, CashFloor, QueueDoor }

        /// <summary>
        /// What the roof is called under the yard root. Named rather than returned because
        /// <see cref="MarketYardScene"/> already finds the stock pad the same way, and the roof is the
        /// one piece of the greybox anything outside this file has to switch on and off.
        /// </summary>
        public const string RoofName = "Cati";

        /// <summary>Inside dimensions of the yard, in world units. The walls stand on the edges of this.</summary>
        public const float Width = 46f, Depth = 40f;

        /// <summary>
        /// Wall height, and it is a sightline number rather than a decorative one. The people are about
        /// 3.1 units tall now, so the old 3.2 hid nothing: customers walked out through the door and
        /// their heads slid along the top of the wall in full view, which is the exact thing the porch
        /// was built to prevent. Anything here has to stay comfortably above the tallest body.
        ///
        /// Public because the signage hangs off the walls and has to know where their inner faces are.
        /// Copying 4.6 into that file instead is how a sign ends up buried in a wall the day this
        /// number moves — and it has moved once already, from 3.2.
        /// </summary>
        public const float WallHeight = 4.6f, WallThickness = 0.9f;

        /// <summary>How far above the walls the porch roof sits. Headroom, so nobody walks into it.</summary>
        private const float PorchRoofLift = 0.9f;

        /// <summary>How thick the yard's own roof slab is. Read from the side, so it wants to be a lip
        /// rather than a sheet — a paper-thin roof seen edge-on from the camera reads as a crack.</summary>
        private const float RoofThickness = 0.6f;

        /// <summary>How wide the doorway between two neighbouring yards is.</summary>
        private const float GateWidth = 10f;

        /// <summary>
        /// The floor arrow's length, width and how thick a coat of paint it is.
        ///
        /// The length is not a taste number: it is what fits between the stock pad's slab and the east
        /// wall, which is the only clear floor at the opening. See <see cref="FloorArrow"/>.
        /// </summary>
        private const float ArrowLength = 4f, ArrowWidth = 1.2f, ArrowThickness = 0.14f;

        /// <summary>The customers' door in the south wall: where it sits, and how wide it is.</summary>
        private const float CustomerDoorX = 9f, DoorWidth = 6f;

        /// <summary>How far the covered porch reaches out past that door.</summary>
        private const float PorchDepth = 7f;

        /// <summary>
        /// Builds the yard under <paramref name="root"/> and reports where the named spots landed.
        /// The tint is the island's ore colour and <paramref name="theme"/> is that island's palette, so
        /// a copper yard reads as copper — inside and out — without a second layout.
        ///
        /// Yards stand shoulder to shoulder, exactly <see cref="Width"/> apart, so one yard's east wall
        /// is the next one's west wall — which is why only the first in the row builds a west wall at
        /// all. <paramref name="eastDoorway"/> splits the shared wall in two and leaves a gap, and that
        /// gap is the whole of "the market is one place": you walk from coal into copper.
        ///
        /// The gap is left as a gap on purpose. It was framed for a while — posts up past the roofline, a
        /// beam across the top — and the frame fought the roof it was meant to be seen against: the posts
        /// stood exactly on the seam between two roof slabs and pushed through both of them. What marks
        /// the way now is paint on the floor, which cannot argue with anything at head height. See
        /// <see cref="FloorArrow"/>.
        /// </summary>
        public static Vector3[] Build(Transform root, MarketTheme.Palette theme, Color oreTint,
                                      bool westWall, bool eastDoorway)
        {
            // Five colours, and every one of them comes from the island rather than from here. The five
            // greys that used to be hard-coded on these lines built the same room eight times: the ore
            // tint reached the roof and nothing else, so the moment the player was under that roof the
            // hall stopped telling him which market he was in. See <see cref="MarketTheme"/>.
            //
            // COLOURS now, not materials, and the pairing with a finish is the point. The old five
            // locals handed one flat material to a wall, a plank and a sheet of tread plate alike —
            // which is the other half of why the yard read as a greybox even after the palette landed.
            // The palette was doing its job; there was simply nothing in the room for light to catch on.
            // A surface is a colour AND a finish from here on, and <see cref="MarketSurfaces"/> caches
            // the pair.
            Color floor = theme.Floor, wall = theme.Wall, slab = theme.Slab;
            Color steel = theme.Metal, timber = theme.Trim;

            Box(root, "Zemin", new Vector3(0f, -0.5f, 0f), new Vector3(Width, 1f, Depth), floor, Finish.Floor);

            float halfW = Width * 0.5f, halfD = Depth * 0.5f;
            Box(root, "Duvar_Kuzey", new Vector3(0f, WallHeight * 0.5f, halfD),
                new Vector3(Width, WallHeight, WallThickness), wall, Finish.Wall);
            // The south wall carries the customers' door. They have to come from somewhere and go
            // somewhere — appearing out of the air at the end of the queue and evaporating again is the
            // one thing that makes a shop read as a diorama instead of a place.
            float doorX = CustomerDoorX;
            float westRun = (doorX - DoorWidth * 0.5f) + halfW;
            float eastRun = halfW - (doorX + DoorWidth * 0.5f);
            Box(root, "Duvar_Guney_Bati",
                new Vector3(-halfW + westRun * 0.5f, WallHeight * 0.5f, -halfD),
                new Vector3(westRun, WallHeight, WallThickness), wall, Finish.Wall);
            Box(root, "Duvar_Guney_Dogu",
                new Vector3(halfW - eastRun * 0.5f, WallHeight * 0.5f, -halfD),
                new Vector3(eastRun, WallHeight, WallThickness), wall, Finish.Wall);

            // A frame around the opening, so it reads as a door rather than a hole somebody forgot.
            Box(root, "Kapi_Sol", new Vector3(doorX - DoorWidth * 0.5f, WallHeight * 0.6f, -halfD),
                new Vector3(0.7f, WallHeight * 1.2f, WallThickness * 1.6f), timber, Finish.Wood);
            Box(root, "Kapi_Sag", new Vector3(doorX + DoorWidth * 0.5f, WallHeight * 0.6f, -halfD),
                new Vector3(0.7f, WallHeight * 1.2f, WallThickness * 1.6f), timber, Finish.Wood);
            Box(root, "Kapi_Ust", new Vector3(doorX, WallHeight * 1.15f, -halfD),
                new Vector3(DoorWidth + 0.7f, 0.7f, WallThickness * 1.6f), timber, Finish.Wood);
            // An invisible pane across the opening itself.
            //
            // Customers pass straight through it — they are moved by transform and the spawner strips
            // their colliders — and the player does not, because he is the only body in the yard with a
            // CharacterController. That asymmetry is the whole trick: one doorway, open to the people
            // who live in it and shut to the one who owns it.
            //
            // It is at the wall line rather than at the back of the porch so the player never gets far
            // enough out to see into it. The ground plate ends here too, so without this he walked
            // through his own front door and off the end of the world.
            var blocker = new GameObject("Kapi_Engel");
            blocker.transform.SetParent(root, false);
            blocker.transform.localPosition = new Vector3(doorX, WallHeight * 0.5f, -halfD);
            blocker.AddComponent<BoxCollider>().size =
                new Vector3(DoorWidth, WallHeight * 2f, WallThickness);

            // A roofed porch beyond the opening. Two jobs, and both matter.
            //
            // It HIDES the outside: customers are switched on and off out there, and watching bodies
            // blink into existence on an empty plain is worse than never having built a door. Roofed
            // and walled on three sides, the isometric camera sees a porch and nothing inside it.
            //
            // It also stops the PLAYER walking out. The ground plate ends at the wall, so the doorway
            // used to be a hole you could fall through — the customers pass because they are moved by
            // transform and carry no colliders, and the player does not because he is the only body in
            // the yard with a CharacterController. The back wall is what he actually stops against.
            float porchZ = -halfD - PorchDepth * 0.5f;
            Box(root, "Sundurma_Zemin", new Vector3(doorX, 0.06f, porchZ),
                new Vector3(DoorWidth + 1.4f, 0.12f, PorchDepth), slab, Finish.Floor);
            Box(root, "Sundurma_Arka", new Vector3(doorX, WallHeight * 0.5f, -halfD - PorchDepth),
                new Vector3(DoorWidth + 1.4f, WallHeight, WallThickness), wall, Finish.Wall);
            Box(root, "Sundurma_Bati", new Vector3(doorX - DoorWidth * 0.5f - 0.5f, WallHeight * 0.5f, porchZ),
                new Vector3(WallThickness, WallHeight, PorchDepth), wall, Finish.Wall);
            Box(root, "Sundurma_Dogu", new Vector3(doorX + DoorWidth * 0.5f + 0.5f, WallHeight * 0.5f, porchZ),
                new Vector3(WallThickness, WallHeight, PorchDepth), wall, Finish.Wall);
            Box(root, "Sundurma_Cati", new Vector3(doorX, WallHeight + PorchRoofLift, porchZ),
                new Vector3(DoorWidth + 2.4f, 0.4f, PorchDepth + 1f), timber, Finish.Wood);

            if (westWall)
                Box(root, "Duvar_Bati", new Vector3(-halfW, WallHeight * 0.5f, 0f),
                    new Vector3(WallThickness, WallHeight, Depth), wall, Finish.Wall);

            float gateSegment = halfD - GateWidth * 0.5f;
            float gateOffset = (halfD + GateWidth * 0.5f) * 0.5f;
            if (eastDoorway)
            {
                Box(root, "Duvar_Dogu_Kuzey", new Vector3(halfW, WallHeight * 0.5f, gateOffset),
                    new Vector3(WallThickness, WallHeight, gateSegment), wall, Finish.Wall);
                Box(root, "Duvar_Dogu_Guney", new Vector3(halfW, WallHeight * 0.5f, -gateOffset),
                    new Vector3(WallThickness, WallHeight, gateSegment), wall, Finish.Wall);
            }
            else
            {
                Box(root, "Duvar_Dogu", new Vector3(halfW, WallHeight * 0.5f, 0f),
                    new Vector3(WallThickness, WallHeight, Depth), wall, Finish.Wall);
            }

            // The three bands that finish a wall: skirting, bumper rail, roofline beam. See
            // <see cref="Trim"/> for why they run over every wall including the shared one.
            Trim(root, "Kuzey", new Vector3(0f, 0f, halfD), Width, true, theme);
            Trim(root, "Guney_Bati", new Vector3(-halfW + westRun * 0.5f, 0f, -halfD), westRun, true, theme);
            Trim(root, "Guney_Dogu", new Vector3(halfW - eastRun * 0.5f, 0f, -halfD), eastRun, true, theme);
            if (westWall) Trim(root, "Bati", new Vector3(-halfW, 0f, 0f), Depth, false, theme);
            if (eastDoorway)
            {
                Trim(root, "Dogu_Kuzey", new Vector3(halfW, 0f, gateOffset), gateSegment, false, theme);
                Trim(root, "Dogu_Guney", new Vector3(halfW, 0f, -gateOffset), gateSegment, false, theme);
            }
            else
            {
                Trim(root, "Dogu", new Vector3(halfW, 0f, 0f), Depth, false, theme);
            }

            // The roof, and it is structure rather than decoration: it is what makes eight yards in one
            // hall readable.
            //
            // A yard nobody is standing in is FROZEN — its customers stop mid-stride, its heap stops
            // moving — while its ledger row goes on selling for it. Frozen in full view reads as broken,
            // so every yard is roofed and the one the player walks into has its roof taken off. Closed,
            // the camera sees a shut building; open, it sees the whole floor. What is really happening in
            // there was never the bodies anyway: it is a number, and the number does not stop.
            //
            // Sitting flat on the wall tops rather than lifted clear like the porch: a lifted roof leaves
            // a slot the isometric camera can see the stopped queue through, which is the one thing this
            // is here to prevent. No overhang either, or it would lie over the neighbour's roof and
            // z-fight along the seam.
            //
            // Tinted with the island's ore, pulled most of the way to slate. A hall of identical grey
            // roofs is a corridor with no signposts — with the interiors shut away, the roof is the ONLY
            // thing left that says which shop is which, so the player can look down the row and know
            // where copper is before walking there. Kept dark enough that it still reads as a roof: the
            // ore colour raw makes a forty-metre gold sheet.
            //
            // One flush slab covering the whole floor plan, and no notch cut in it at the doorway. A notch
            // was tried, to stop the slab reaching over the shared wall — and it is a hole in a roof, which
            // is exactly what it looks like.
            Color roofing = Color.Lerp(oreTint, new Color(0.24f, 0.26f, 0.31f), 0.55f);
            Transform roof = Box(root, RoofName, new Vector3(0f, WallHeight + RoofThickness * 0.5f, 0f),
                                 new Vector3(Width, RoofThickness, Depth), roofing, Finish.Roof);
            // And no collider on it. Nothing walks on top, and the only body that can ever reach its
            // underside is the player stepping through a doorway in the fraction of a second before the
            // neighbour's roof comes off — a ceiling for his controller to catch on there is a stumble
            // in the one place the hall is trying to feel like a single room.
            Collider roofBox = roof.GetComponentInChildren<Collider>();
            if (roofBox != null) Object.Destroy(roofBox);

            // The ramp: the thing the island's lorries tip into, along the far wall.
            var ramp = new Vector3(0f, 0f, halfD - 5f);
            Box(root, "Rampa", ramp + new Vector3(0f, 1.6f, 0f), new Vector3(28f, 3.2f, 6f), steel, Finish.Metal);
            Box(root, "RampaAgzi", ramp + new Vector3(0f, 4.4f, 0.5f), new Vector3(9f, 3.4f, 4f), steel, Finish.Metal);
            // A painted warning band along the lip the lorries tip over. It is the only diagonal in a
            // room built entirely out of right angles, which is most of why it reads from across the
            // floor — and it says the ramp is machinery rather than another grey ledge to walk on.
            // Its colour is fixed rather than themed: a hazard stripe that changes colour per island is
            // not a hazard stripe.
            Box(root, "Rampa_Serit", ramp + new Vector3(0f, 2.75f, -3.12f), new Vector3(28f, 0.8f, 0.24f),
                new Color(0.95f, 0.78f, 0.18f), Finish.Hazard, collide: false);

            // The stock pad: where the bars land and pile up.
            var pad = new Vector3(10f, 0f, 2f);
            Box(root, "StokPedi", pad + new Vector3(0f, 0.08f, 0f), new Vector3(17f, 0.16f, 15f), slab, Finish.Floor);
            // No token heap on it any more. There was a plain ore-coloured box here to stop the slab
            // looking empty before PileStack existed; PileStack now draws a shallow pool barely half a
            // unit deep, and a 0.9-tall box stood in the middle of it like a plinth in a puddle.

            // The counter, and the lane the queue stands in behind it.
            var counter = new Vector3(-9f, 0f, -8f);
            Box(root, "TezgahAyak", counter + new Vector3(0f, 0.6f, 0f), new Vector3(11f, 1.2f, 2.6f), steel, Finish.Metal);
            Box(root, "TezgahTabla", counter + new Vector3(0f, 1.35f, 0f), new Vector3(12f, 0.35f, 3.6f), timber, Finish.Wood);

            var queue = new Vector3(-9f, 0f, -13.5f);
            Box(root, "SiraYolu", queue + new Vector3(0f, 0.06f, 0f), new Vector3(24f, 0.12f, 4f), slab, Finish.Floor);

            var cash = new Vector3(1.5f, 0f, -8f);
            Box(root, "ParaZemini", cash + new Vector3(0f, 0.06f, 0f), new Vector3(6f, 0.12f, 6f), slab, Finish.Floor);

            // Sized from the enum, not a literal. It was a literal 6, and adding a seventh anchor threw
            // an IndexOutOfRange halfway through building the yard — which looks nothing like an array
            // bug from the outside: the walls were up, the queue and the camera simply never existed.
            var spots = new Vector3[System.Enum.GetValues(typeof(Anchor)).Length];
            spots[(int)Anchor.PlayerStart] = new Vector3(0f, 0.2f, -2f);
            spots[(int)Anchor.Ramp] = ramp;
            spots[(int)Anchor.StockPad] = pad;
            spots[(int)Anchor.Counter] = counter;
            spots[(int)Anchor.Queue] = queue;
            spots[(int)Anchor.CashFloor] = cash;
            // The middle of the opening, on the wall line. The queue walks its customers to here and
            // then out past it, where the wall hides them.
            spots[(int)Anchor.QueueDoor] = new Vector3(CustomerDoorX, 0f, -halfD);
            return spots;
        }

        /// <summary>
        /// An arrow painted flat on the floor, pointing east or west along the row of yards — which is
        /// what says "the next market is that way" now that the doorway is not dressed.
        ///
        /// On the FLOOR, and that is the whole trick. The camera is a fixed angle and never rotates, so
        /// anything standing up in a doorway shows one of the two rooms its back; the ground faces the
        /// camera from both sides. It is the same reason the pad prices are painted down there.
        ///
        /// Built from three boxes — a shaft and two barbs turned off it — rather than a sprite, because
        /// every other thing in this yard is a box too and they all batch together. Colliders come off:
        /// this is paint, and paint the player's capsule can catch on is a trip hazard at the one place
        /// in the room he is trying to walk through.
        /// </summary>
        public static Transform FloorArrow(Transform parent, string name, Vector3 at, bool eastward,
                                           Material paint)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = at;

            float dir = eastward ? 1f : -1f;
            float tip = ArrowLength * 0.5f * dir;
            float lift = ArrowThickness * 0.5f;

            Box(holder.transform, "Govde", new Vector3(-0.6f * dir, lift, 0f),
                new Vector3(ArrowLength - 1.2f, ArrowThickness, ArrowWidth), paint);
            // The barbs meet the shaft at the tip and run back at forty-five degrees. Turning them is why
            // Box hands back an unscaled holder: rotating the holder turns the box inside it without
            // shearing anything, which a scaled transform would.
            Barb(holder.transform, "Uc_Kuzey", new Vector3(tip - 0.85f * dir, lift, 0.85f),
                 eastward ? 45f : 135f, paint);
            Barb(holder.transform, "Uc_Guney", new Vector3(tip - 0.85f * dir, lift, -0.85f),
                 eastward ? -45f : -135f, paint);

            Collider[] hazards = holder.GetComponentsInChildren<Collider>();
            for (int i = 0; i < hazards.Length; i++) Object.Destroy(hazards[i]);
            return holder.transform;
        }

        private static void Barb(Transform parent, string name, Vector3 at, float yaw, Material paint)
        {
            Transform barb = Box(parent, name, at, new Vector3(2.4f, ArrowThickness, ArrowWidth), paint);
            barb.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>
        /// The three bands that turn a coloured slab into a wall: a skirting at the floor, a bumper
        /// rail at chest height and a beam under the roofline.
        ///
        /// Trim rather than modelled wall sections, and that is a deliberate limit. Real openings and
        /// girders would mean re-fitting every layout constant in this file against new geometry — the
        /// pad rank, the queue lane and the camera framing are all measured off flat walls. Three
        /// horizontal bands cost twelve boxes a yard and buy the one thing a flat wall cannot have:
        /// a silhouette. At this camera angle they are what catch the sun and throw the shadow that
        /// says the wall has a thickness.
        ///
        /// Each band WRAPS its wall — it is thicker than the wall itself and pokes out on both faces —
        /// so the shared wall between two yards gets trimmed once and is dressed on both sides. Doing
        /// it per-face would trim the same wall twice and z-fight down the middle of it.
        ///
        /// No colliders. The wall behind is what stops the player, and it is at most a quarter of a
        /// unit further in; a collider here only ever adds something to snag on at the skirting.
        /// </summary>
        private static void Trim(Transform root, string name, Vector3 at, float length, bool alongX,
                                 MarketTheme.Palette theme)
        {
            for (int i = 0; i < BandAt.Length; i++)
            {
                float thickness = WallThickness + BandStand[i] * 2f;
                Vector3 size = alongX
                    ? new Vector3(length, BandHeight[i], thickness)
                    : new Vector3(thickness, BandHeight[i], length);
                // The middle band is the timber bumper rail; the other two are steel. Two colours from
                // the island's own palette, so a copper yard's trim is copper and a coal yard's is not.
                bool rail = i == 1;
                Box(root, "Kusak_" + name + "_" + i, at + new Vector3(0f, BandAt[i], 0f), size,
                    rail ? theme.Trim : theme.Metal, rail ? Finish.Wood : Finish.Metal, collide: false);
            }
        }

        /// <summary>
        /// Where the three bands sit, how tall each is and how far it stands off the wall.
        ///
        /// The rail is at 2.55 because the people are about 3.1 units tall — it has to land on a body
        /// rather than over its head, or it reads as a second roofline. The beam is tucked under the
        /// wall top rather than on it, so the roof still sits flush and nothing pokes through the
        /// neighbour's slab.
        /// </summary>
        private static readonly float[] BandAt = { 0.30f, 2.55f, WallHeight - 0.26f };
        private static readonly float[] BandHeight = { 0.60f, 0.34f, 0.40f };
        private static readonly float[] BandStand = { 0.16f, 0.12f, 0.26f };

        /// <summary>
        /// A box at its real size wearing one of the yard's finishes, with a collider — which is what
        /// makes the walls actually stop the player.
        ///
        /// NOT a scaled primitive any more, and the difference is the whole texture pass. A unit cube
        /// stretched to 46x4.6x0.9 has UVs that run 0..1 across every face however big it is, so one
        /// map would be smeared fifty times along a wall and squashed into a line across its edge.
        /// <see cref="MarketBoxMesh"/> builds the box at size with the repeat measured in world units
        /// instead, and hands back the same mesh to every box in the hall that happens to be that size.
        ///
        /// The returned transform is an UNSCALED holder with the mesh inside it, rather than the mesh
        /// itself. Anything parented to a scaled transform inherits that scale, and the pieces here were
        /// scaled hard — a 17x0.16x15 pad would have squashed a heap of ore flat against the floor. The
        /// holder is what <see cref="PileStack"/> and the trigger volumes hang off. It stays a holder
        /// even now that the child is unscaled, because that is what everything downstream expects to
        /// find by name.
        /// </summary>
        private static Transform Box(Transform parent, string name, Vector3 centre, Vector3 size,
                                     Color colour, Finish finish, bool collide = true)
            => Box(parent, name, centre, size, MarketSurfaces.Get(colour, finish), finish, collide);

        /// <summary>The same, for a caller that already has the material — the floor paint does.</summary>
        private static Transform Box(Transform parent, string name, Vector3 centre, Vector3 size,
                                     Material mat, Finish finish = Finish.Plain, bool collide = true)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = centre;

            var piece = new GameObject("Mesh");
            piece.transform.SetParent(holder.transform, false);
            piece.AddComponent<MeshFilter>().sharedMesh =
                MarketBoxMesh.Get(size, MarketSurfaces.Tiles(finish));
            piece.AddComponent<MeshRenderer>().sharedMaterial = mat;
            // The collider has to be given the size explicitly now. A BoxCollider defaults to a unit
            // cube and used to inherit the mesh's scale for free; with the size baked into the mesh
            // instead, an unset collider is a one-metre box in the middle of a forty-metre wall.
            if (collide) piece.AddComponent<BoxCollider>().size = size;
            return holder.transform;
        }

        /// <summary>
        /// A flat URP material for one greybox colour, with no map on it.
        ///
        /// Still here, and still the right thing for the handful of surfaces that genuinely are flat
        /// colour: the floor paint, the player's kit, the worker tints, the pad faces. Everything the
        /// room is BUILT from goes through <see cref="MarketSurfaces"/> instead — this now just asks it
        /// for the plain finish, which means these are cached and shared like the rest rather than a
        /// fresh material per call.
        /// </summary>
        public static Material Mat(Color c) => MarketSurfaces.Get(c, Finish.Plain);
    }
}
