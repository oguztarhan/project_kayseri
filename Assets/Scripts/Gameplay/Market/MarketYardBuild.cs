using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Puts up a greybox market yard in code: floor, four walls, the unloading ramp, the stock pad and
    /// the counter, in the layout the design calls for.
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

        /// <summary>Inside dimensions of the yard, in world units. The walls stand on the edges of this.</summary>
        public const float Width = 46f, Depth = 40f;

        /// <summary>
        /// Wall height, and it is a sightline number rather than a decorative one. The people are about
        /// 3.1 units tall now, so the old 3.2 hid nothing: customers walked out through the door and
        /// their heads slid along the top of the wall in full view, which is the exact thing the porch
        /// was built to prevent. Anything here has to stay comfortably above the tallest body.
        /// </summary>
        private const float WallHeight = 4.6f, WallThickness = 0.9f;

        /// <summary>How far above the walls the porch roof sits. Headroom, so nobody walks into it.</summary>
        private const float PorchRoofLift = 0.9f;

        /// <summary>How wide the doorway between two neighbouring yards is.</summary>
        private const float GateWidth = 10f;

        /// <summary>The customers' door in the south wall: where it sits, and how wide it is.</summary>
        private const float CustomerDoorX = 9f, DoorWidth = 6f;

        /// <summary>How far the covered porch reaches out past that door.</summary>
        private const float PorchDepth = 7f;

        /// <summary>
        /// Builds the yard under <paramref name="root"/> and reports where the named spots landed.
        /// The tint is the island's ore colour, so a copper yard reads as copper without a second layout.
        ///
        /// Yards stand shoulder to shoulder, exactly <see cref="Width"/> apart, so one yard's east wall
        /// is the next one's west wall — which is why only the first in the row builds a west wall at
        /// all. <paramref name="eastDoorway"/> splits the shared wall in two and leaves a gap, and that
        /// gap is the whole of "the market is one place": you walk from coal into copper.
        /// </summary>
        public static Vector3[] Build(Transform root, Color oreTint, bool westWall, bool eastDoorway)
        {
            Material floor = Mat(new Color(0.55f, 0.42f, 0.34f));
            Material wall = Mat(new Color(0.29f, 0.32f, 0.39f));
            Material slab = Mat(new Color(0.78f, 0.77f, 0.72f));
            Material steel = Mat(new Color(0.36f, 0.39f, 0.45f));
            Material timber = Mat(new Color(0.67f, 0.45f, 0.25f));
            Material ore = Mat(oreTint);

            Box(root, "Zemin", new Vector3(0f, -0.5f, 0f), new Vector3(Width, 1f, Depth), floor);

            float halfW = Width * 0.5f, halfD = Depth * 0.5f;
            Box(root, "Duvar_Kuzey", new Vector3(0f, WallHeight * 0.5f, halfD),
                new Vector3(Width, WallHeight, WallThickness), wall);
            // The south wall carries the customers' door. They have to come from somewhere and go
            // somewhere — appearing out of the air at the end of the queue and evaporating again is the
            // one thing that makes a shop read as a diorama instead of a place.
            float doorX = CustomerDoorX;
            float westRun = (doorX - DoorWidth * 0.5f) + halfW;
            float eastRun = halfW - (doorX + DoorWidth * 0.5f);
            Box(root, "Duvar_Guney_Bati",
                new Vector3(-halfW + westRun * 0.5f, WallHeight * 0.5f, -halfD),
                new Vector3(westRun, WallHeight, WallThickness), wall);
            Box(root, "Duvar_Guney_Dogu",
                new Vector3(halfW - eastRun * 0.5f, WallHeight * 0.5f, -halfD),
                new Vector3(eastRun, WallHeight, WallThickness), wall);

            // A frame around the opening, so it reads as a door rather than a hole somebody forgot.
            Box(root, "Kapi_Sol", new Vector3(doorX - DoorWidth * 0.5f, WallHeight * 0.6f, -halfD),
                new Vector3(0.7f, WallHeight * 1.2f, WallThickness * 1.6f), timber);
            Box(root, "Kapi_Sag", new Vector3(doorX + DoorWidth * 0.5f, WallHeight * 0.6f, -halfD),
                new Vector3(0.7f, WallHeight * 1.2f, WallThickness * 1.6f), timber);
            Box(root, "Kapi_Ust", new Vector3(doorX, WallHeight * 1.15f, -halfD),
                new Vector3(DoorWidth + 0.7f, 0.7f, WallThickness * 1.6f), timber);
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
                new Vector3(DoorWidth + 1.4f, 0.12f, PorchDepth), slab);
            Box(root, "Sundurma_Arka", new Vector3(doorX, WallHeight * 0.5f, -halfD - PorchDepth),
                new Vector3(DoorWidth + 1.4f, WallHeight, WallThickness), wall);
            Box(root, "Sundurma_Bati", new Vector3(doorX - DoorWidth * 0.5f - 0.5f, WallHeight * 0.5f, porchZ),
                new Vector3(WallThickness, WallHeight, PorchDepth), wall);
            Box(root, "Sundurma_Dogu", new Vector3(doorX + DoorWidth * 0.5f + 0.5f, WallHeight * 0.5f, porchZ),
                new Vector3(WallThickness, WallHeight, PorchDepth), wall);
            Box(root, "Sundurma_Cati", new Vector3(doorX, WallHeight + PorchRoofLift, porchZ),
                new Vector3(DoorWidth + 2.4f, 0.4f, PorchDepth + 1f), timber);

            if (westWall)
                Box(root, "Duvar_Bati", new Vector3(-halfW, WallHeight * 0.5f, 0f),
                    new Vector3(WallThickness, WallHeight, Depth), wall);

            if (eastDoorway)
            {
                float segment = halfD - GateWidth * 0.5f;
                float offset = (halfD + GateWidth * 0.5f) * 0.5f;
                Box(root, "Duvar_Dogu_Kuzey", new Vector3(halfW, WallHeight * 0.5f, offset),
                    new Vector3(WallThickness, WallHeight, segment), wall);
                Box(root, "Duvar_Dogu_Guney", new Vector3(halfW, WallHeight * 0.5f, -offset),
                    new Vector3(WallThickness, WallHeight, segment), wall);
            }
            else
            {
                Box(root, "Duvar_Dogu", new Vector3(halfW, WallHeight * 0.5f, 0f),
                    new Vector3(WallThickness, WallHeight, Depth), wall);
            }

            // The ramp: the thing the island's lorries tip into, along the far wall.
            var ramp = new Vector3(0f, 0f, halfD - 5f);
            Box(root, "Rampa", ramp + new Vector3(0f, 1.6f, 0f), new Vector3(28f, 3.2f, 6f), steel);
            Box(root, "RampaAgzi", ramp + new Vector3(0f, 4.4f, 0.5f), new Vector3(9f, 3.4f, 4f), steel);

            // The stock pad: where the bars land and pile up.
            var pad = new Vector3(10f, 0f, 2f);
            Box(root, "StokPedi", pad + new Vector3(0f, 0.08f, 0f), new Vector3(17f, 0.16f, 15f), slab);
            // A token heap so the pad is not an empty slab before PileStack moves in at the next step.
            Box(root, "StokOrnek", pad + new Vector3(0f, 0.55f, 0f), new Vector3(6f, 0.9f, 5f), ore);

            // The counter, and the lane the queue stands in behind it.
            var counter = new Vector3(-9f, 0f, -8f);
            Box(root, "TezgahAyak", counter + new Vector3(0f, 0.6f, 0f), new Vector3(11f, 1.2f, 2.6f), steel);
            Box(root, "TezgahTabla", counter + new Vector3(0f, 1.35f, 0f), new Vector3(12f, 0.35f, 3.6f), timber);

            var queue = new Vector3(-9f, 0f, -13.5f);
            Box(root, "SiraYolu", queue + new Vector3(0f, 0.06f, 0f), new Vector3(24f, 0.12f, 4f), slab);

            var cash = new Vector3(1.5f, 0f, -8f);
            Box(root, "ParaZemini", cash + new Vector3(0f, 0.06f, 0f), new Vector3(6f, 0.12f, 6f), slab);

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
        /// A lit box with a collider, which is what makes the walls actually stop the player.
        ///
        /// The returned transform is an UNSCALED holder with the scaled cube inside it, rather than the
        /// cube itself. Anything parented to a scaled transform inherits that scale, and the pieces
        /// here are scaled hard — a 17x0.16x15 pad would have squashed a heap of ore flat against the
        /// floor. The holder is what <see cref="PileStack"/> and the trigger volumes hang off.
        /// </summary>
        private static Transform Box(Transform parent, string name, Vector3 centre, Vector3 size, Material mat)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = centre;

            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Mesh";
            cube.transform.SetParent(holder.transform, false);
            cube.transform.localScale = size;
            cube.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return holder.transform;
        }

        /// <summary>
        /// A flat URP material for one greybox colour. Found by shader name rather than wired in the
        /// Inspector because this whole file is scaffolding — the moment real art arrives, the
        /// materials arrive with it and this goes.
        /// </summary>
        public static Material Mat(Color c)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(shader != null ? shader : Shader.Find("Sprites/Default"));
            mat.color = c;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.12f);
            return mat;
        }
    }
}
