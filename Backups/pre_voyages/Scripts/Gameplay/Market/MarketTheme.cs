using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// What one island's market yard is made of and what is lying around in it: the colour of its floor,
    /// its walls and its fittings, and the props that say which trade it is without anyone reading a sign.
    ///
    /// It exists because eight yards built from one layout were eight identical rooms. The roof already
    /// carries the ore colour, but the roof is what you see from OUTSIDE — walk in and every market was
    /// the same brown floor and the same slate walls, so the one thing the hall could not tell you was
    /// where you were standing. A coal yard is now sooty and cold, a copper yard is brick and rust, a
    /// gold yard is sand and cream, and the barrels and crates in the corners belong to that trade.
    ///
    /// Hand-picked rather than derived from <see cref="WorldIslands.OreColorFor"/>, and that is the whole
    /// reason this table exists instead of a one-line tint. Coal's ore colour is very nearly black: run
    /// the walls and floor through it and the room is unlit and unreadable, which is exactly the room
    /// the player spends the most time in. Diamond's is nearly white and goes the other way. A palette
    /// keyed to the ore has to be chosen, not computed.
    /// </summary>
    public static class MarketTheme
    {
        /// <summary>
        /// One prop the dressing may stand in a yard: which prefab under
        /// <c>Resources/Market/Props</c> to load, and how big its LONGEST side should end up in world
        /// units.
        ///
        /// A size rather than a scale factor, because the source models come from three Kenney packs
        /// with three different ideas of how big a unit is — a barrel is a third of a unit tall and a
        /// cargo container is nearly three long. Sizing them against the yard's own people (about 3.1
        /// units) is a number anyone can check by eye; a per-model scale factor is one nobody can.
        ///
        /// The longest side rather than the height, and that is the corner these props have to fit into.
        /// Fitting by height scales a flat thing by whatever it takes to make it tall, and a stack of
        /// planks nine centimetres thick came out five and a half metres long — through the wall it was
        /// leaning against. The longest side is the only measurement that bounds every axis at once.
        /// </summary>
        public readonly struct Prop
        {
            public readonly string Resource;
            public readonly float Size;
            public Prop(string resource, float size) { Resource = resource; Size = size; }
        }

        /// <summary>The five colours a yard is built out of. See <see cref="MarketYardBuild.Build"/>.</summary>
        public readonly struct Palette
        {
            public readonly Color Floor, Wall, Slab, Metal, Trim;
            public readonly Prop[] Props;

            public Palette(Color floor, Color wall, Color slab, Color metal, Color trim, Prop[] props)
            {
                Floor = floor; Wall = wall; Slab = slab; Metal = metal; Trim = trim; Props = props;
            }
        }

        // The props themselves, named once. Referenced by the palettes below rather than looked up by
        // string in a second table, so a prop cannot end up with a height that belongs to another one.
        // Nothing here goes over 3.0 but the chimney, which is a tower and only coal has one, in the
        // corner spot — and it stops at 4.2, below the 4.6 the walls stand at, so it cannot reach the
        // roofline of the yard it is in. That ceiling is what keeps a prop inside the clearance every spot in
        // <see cref="MarketYardDressing"/> is chosen to have — and the two spots in the strip south of
        // the queue lane are tighter still, so the props that land on them are the 2.6s. Widen anything
        // here and re-run the fit: the lane is four units from the wall and nothing else in the room is.
        private static readonly Prop Barrel = new Prop("barrel", 1.7f);
        private static readonly Prop BarrelOpen = new Prop("barrel_open", 1.7f);
        private static readonly Prop Crate = new Prop("crate", 1.5f);
        private static readonly Prop CrateLarge = new Prop("crate_large", 2.2f);
        private static readonly Prop CrateOpen = new Prop("crate_open", 1.5f);
        private static readonly Prop Chest = new Prop("chest", 1.9f);
        private static readonly Prop Planks = new Prop("planks", 1.8f);
        private static readonly Prop Stone = new Prop("stone", 1.6f);
        private static readonly Prop Anvil = new Prop("anvil", 1.8f);
        private static readonly Prop Panel = new Prop("panel", 2.6f);
        private static readonly Prop ContainerA = new Prop("container_a", 2.6f);
        private static readonly Prop ContainerB = new Prop("container_b", 2.6f);
        private static readonly Prop CargoPile = new Prop("cargo_pile", 3.0f);
        private static readonly Prop Tank = new Prop("tank", 2.8f);
        private static readonly Prop Chimney = new Prop("chimney", 4.2f);

        // The yard's own furniture, modelled for this room rather than borrowed from a pack — see
        // Tools/blender/market_props.py. Their parts are named for a material role, so unlike the
        // Kenney props above they take the ISLAND's timber and steel rather than arriving painted.
        //
        // They are here to say what the room is FOR. A yard dressed only in crates and barrels is a
        // storeroom; a scale, a hand truck, a bench and a pallet under everything are what make it a
        // place where goods are weighed, moved and sold to somebody who had to wait for them.
        private static readonly Prop Pallet = new Prop("pallet", 1.5f);
        private static readonly Prop Sacks = new Prop("sacks", 1.7f);
        private static readonly Prop HandTruck = new Prop("hand_truck", 1.7f);
        private static readonly Prop Scale = new Prop("scale", 1.6f);
        private static readonly Prop Bench = new Prop("bench", 2.2f);
        private static readonly Prop Plant = new Prop("plant", 1.4f);
        private static readonly Prop Cone = new Prop("cone", 0.9f);
        private static readonly Prop ToolChest = new Prop("toolchest", 1.4f);

        /// <summary>
        /// The palette for an island, by the same key <see cref="WorldIslands"/> uses. An unknown key
        /// gets the neutral fallback rather than throwing: the hall builds one yard per island the
        /// player owns, and a new ore added to the ladder should show up as a plain room, not a hole.
        /// </summary>
        public static Palette For(string islandKey)
        {
            switch (islandKey)
            {
                // Soot on the ground, cold slate walls, and the plant that made the soot in the corner.
                case "coal": return new Palette(
                    new Color(0.22f, 0.21f, 0.20f), new Color(0.31f, 0.33f, 0.38f),
                    new Color(0.55f, 0.54f, 0.52f), new Color(0.34f, 0.36f, 0.40f),
                    new Color(0.52f, 0.40f, 0.26f),
                    new[] { Chimney, Tank, Barrel, BarrelOpen, CargoPile, Barrel, Crate, Panel,
                            Sacks, ToolChest });

                case "copper": return new Palette(
                    new Color(0.31f, 0.23f, 0.18f), new Color(0.56f, 0.38f, 0.27f),
                    new Color(0.78f, 0.66f, 0.54f), new Color(0.45f, 0.33f, 0.26f),
                    new Color(0.85f, 0.55f, 0.25f),
                    new[] { Tank, ContainerA, Barrel, Crate, Planks, CrateLarge, Barrel, ContainerB,
                            HandTruck, Pallet });

                case "iron": return new Palette(
                    new Color(0.33f, 0.34f, 0.36f), new Color(0.44f, 0.47f, 0.52f),
                    new Color(0.70f, 0.72f, 0.75f), new Color(0.30f, 0.33f, 0.38f),
                    new Color(0.62f, 0.66f, 0.72f),
                    new[] { ContainerB, Panel, Anvil, CrateLarge, Stone, Anvil, Panel, ContainerA,
                            ToolChest, Cone });

                case "silver": return new Palette(
                    new Color(0.42f, 0.44f, 0.48f), new Color(0.62f, 0.66f, 0.72f),
                    new Color(0.82f, 0.85f, 0.90f), new Color(0.50f, 0.54f, 0.60f),
                    new Color(0.86f, 0.89f, 0.94f),
                    new[] { ContainerA, Crate, CrateLarge, Chest, Panel, Planks, CrateOpen, Tank,
                            Scale, Bench });

                case "gold": return new Palette(
                    new Color(0.46f, 0.39f, 0.26f), new Color(0.68f, 0.57f, 0.34f),
                    new Color(0.88f, 0.80f, 0.60f), new Color(0.52f, 0.45f, 0.30f),
                    new Color(0.95f, 0.78f, 0.22f),
                    new[] { ContainerB, Chest, Chest, CrateLarge, Planks, Barrel, Chest, Crate,
                            Scale, Plant });

                case "ruby": return new Palette(
                    new Color(0.30f, 0.20f, 0.22f), new Color(0.50f, 0.24f, 0.28f),
                    new Color(0.76f, 0.62f, 0.62f), new Color(0.38f, 0.26f, 0.30f),
                    new Color(0.85f, 0.20f, 0.30f),
                    new[] { CargoPile, Chest, Stone, CrateOpen, Planks, Chest, Crate, CrateLarge,
                            Bench, Plant });

                case "emerald": return new Palette(
                    new Color(0.22f, 0.30f, 0.24f), new Color(0.28f, 0.48f, 0.36f),
                    new Color(0.68f, 0.78f, 0.68f), new Color(0.30f, 0.40f, 0.34f),
                    new Color(0.20f, 0.78f, 0.42f),
                    new[] { CargoPile, Stone, CrateOpen, Planks, Chest, Stone, Crate, CrateLarge,
                            Plant, Sacks });

                case "diamond": return new Palette(
                    new Color(0.34f, 0.42f, 0.48f), new Color(0.44f, 0.60f, 0.70f),
                    new Color(0.80f, 0.88f, 0.92f), new Color(0.40f, 0.50f, 0.58f),
                    new Color(0.72f, 0.94f, 1.00f),
                    new[] { ContainerA, Chest, CrateLarge, Panel, Stone, Chest, Crate, ContainerB,
                            Scale, Bench });

                default: return new Palette(
                    new Color(0.36f, 0.35f, 0.34f), new Color(0.42f, 0.44f, 0.49f),
                    new Color(0.72f, 0.71f, 0.68f), new Color(0.36f, 0.39f, 0.45f),
                    new Color(0.67f, 0.45f, 0.25f),
                    new[] { Crate, Barrel, CrateLarge, Planks, Barrel, Crate, Panel, Stone,
                            Pallet, Cone });
            }
        }
    }
}
