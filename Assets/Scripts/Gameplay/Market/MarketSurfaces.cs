using System.Collections.Generic;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// What the market yard's surfaces are made of: the six tileable maps under
    /// <c>Resources/Market/Textures</c>, and the materials that put them on a box.
    ///
    /// The maps are LUMINANCE — near-white with the detail in the normal — and that is the whole
    /// reason this can exist alongside <see cref="MarketTheme"/> rather than instead of it. A yard's
    /// colour comes from its island and only from there; a texture with its own colour in it would
    /// fight that, and the coal yard, whose palette is nearly black already, would go to mud. Here
    /// the map is a multiplier hovering around one, so a copper wall is still copper — it just has
    /// ribs and bolts and a panel seam in it now.
    ///
    /// TILING IS NOT ON THE MATERIAL. A yard is built out of boxes ranging from a 46-unit wall to a
    /// 0.7-unit door post, and one <c>_BaseMap_ST</c> across all of them smears the texture on
    /// everything it does not happen to fit. The repeat is baked into the mesh instead — see
    /// <see cref="MarketBoxMesh"/> — so the material stays shared and every face gets the same
    /// texel density no matter how big the box it is on. That is what <see cref="Tiles"/> is for.
    ///
    /// Materials are cached by colour and finish. Eight yards out of five palette colours is around
    /// forty of them for the whole hall, all on one shader, which the SRP batcher keeps in one pass.
    /// </summary>
    public static class MarketSurfaces
    {
        /// <summary>Which of the maps a surface wears. <see cref="Finish.Plain"/> is the old flat colour.</summary>
        public enum Finish { Plain, Wall, Floor, Roof, Wood, Metal, Hazard, Banner }

        private const string TextureRoot = "Market/Textures/T_Market_";

        /// <summary>
        /// How many times a map repeats per world unit, and how shiny the surface it belongs to is.
        ///
        /// The repeats are set against the PEOPLE, who are about 3.1 units tall, because that is the
        /// only ruler in the room the player can actually see. Wall ribs land every half unit — a
        /// hand's width at that scale; floor slabs are five units, the size a concrete pour really is;
        /// planks are 45cm wide. Get these wrong in either direction and the room silently changes
        /// size: a wall tiled twice as fast reads as a model of a wall.
        /// </summary>
        private readonly struct Recipe
        {
            public readonly string Map;
            public readonly float TilesPerUnit;
            public readonly float Smoothness;
            public readonly float BumpScale;

            public Recipe(string map, float tilesPerUnit, float smoothness, float bumpScale)
            {
                Map = map; TilesPerUnit = tilesPerUnit; Smoothness = smoothness; BumpScale = bumpScale;
            }
        }

        private static readonly Recipe[] Recipes =
        {
            new Recipe(null,     0f,     0.12f, 0f),      // Plain
            new Recipe("Wall",   0.333f, 0.16f, 1.0f),    // kaburgalar yarim birimde bir
            new Recipe("Floor",  0.200f, 0.06f, 0.7f),    // bes birimlik beton plaka
            // Oluk basina 1.1 birim. Once yarim birimdi ve o bir hataydi: cati kirk alti birimlik
            // tek bir levha ve kamera onu uzaktan goruyor, yani ekranda dalga basina birkac piksel
            // dusuyordu — sonuc oluklu sac degil titreyen bir tel kafes. Gercek sanayi sac levhalari
            // da zaten bu araliklarda.
            new Recipe("Roof",   0.115f, 0.28f, 1.0f),
            new Recipe("Wood",   0.550f, 0.14f, 0.8f),    // 45 cm'lik kalas
            new Recipe("Metal",  0.333f, 0.38f, 1.0f),    // gozyasi saci
            new Recipe("Hazard", 0.500f, 0.20f, 0.5f),    // capraz uyari seridi
            new Recipe("Banner", 0.320f, 0.10f, 0.6f),    // bez afis
        };

        /// <summary>How many times this finish's map repeats per world unit. Zero means untextured.</summary>
        public static float Tiles(Finish finish) => Recipes[(int)finish].TilesPerUnit;

        private readonly struct Key : System.IEquatable<Key>
        {
            private readonly int _rgb;
            private readonly int _finish;

            public Key(Color c, Finish finish)
            {
                // Quantised, because these come from lerps — two colours a thousandth apart are the
                // same wall and should not be two materials.
                _rgb = (Mathf.RoundToInt(c.r * 255f) << 16) |
                       (Mathf.RoundToInt(c.g * 255f) << 8) |
                       Mathf.RoundToInt(c.b * 255f);
                _finish = (int)finish;
            }

            public bool Equals(Key other) => _rgb == other._rgb && _finish == other._finish;
            public override bool Equals(object other) => other is Key k && Equals(k);
            public override int GetHashCode() => (_rgb * 397) ^ _finish;
        }

        private static readonly Dictionary<Key, Material> Cache = new Dictionary<Key, Material>(64);
        private static readonly Dictionary<string, Texture2D> Maps = new Dictionary<string, Texture2D>(16);
        private static Shader _lit;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BumpMapId = Shader.PropertyToID("_BumpMap");
        private static readonly int BumpScaleId = Shader.PropertyToID("_BumpScale");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        /// <summary>
        /// The material for one colour in one finish. Handed out shared — never write to it.
        ///
        /// A missing map falls back to the flat colour rather than throwing or drawing magenta. The
        /// yard has always been able to run half-dressed (see <see cref="MarketYardDressing"/>), and
        /// that is worth keeping: a project without the texture folder still gets a market.
        /// </summary>
        public static Material Get(Color colour, Finish finish)
        {
            var key = new Key(colour, finish);
            Material cached;
            if (Cache.TryGetValue(key, out cached) && cached != null) return cached;

            Recipe recipe = Recipes[(int)finish];
            var mat = new Material(Lit) { color = colour };
            mat.SetColor(BaseColorId, colour);
            mat.SetFloat(SmoothnessId, recipe.Smoothness);

            Texture2D map = recipe.Map != null ? Map(recipe.Map) : null;
            if (map != null)
            {
                mat.SetTexture(BaseMapId, map);
                Texture2D bump = Map(recipe.Map + "_N");
                if (bump != null)
                {
                    // URP only samples _BumpMap with the keyword on; setting the texture alone is a
                    // silent no-op and looks exactly like a normal map that came out flat.
                    mat.SetTexture(BumpMapId, bump);
                    mat.SetFloat(BumpScaleId, recipe.BumpScale);
                    mat.EnableKeyword("_NORMALMAP");
                }
            }

            Cache[key] = mat;
            return mat;
        }

        /// <summary>
        /// A material that gives off light: signs, lamp glass, the neon over a yard's door.
        ///
        /// Not cached with the rest. These are one-offs — a handful in the whole hall — and several of
        /// them are animated, so handing out a shared instance would have one sign's flicker driving
        /// every other sign in the market.
        /// </summary>
        public static Material Glow(Color colour, float strength)
        {
            var mat = new Material(Lit) { color = colour };
            mat.SetColor(BaseColorId, colour);
            mat.SetFloat(SmoothnessId, 0.4f);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetColor(EmissionColorId, colour * strength);
            return mat;
        }

        /// <summary>
        /// Re-aims an existing glow material. What a flickering lamp writes every frame, and what the
        /// neon over a yard's door writes when the shop opens or shuts.
        ///
        /// The base colour goes with the emission. A sign turned down to a quarter strength with its
        /// base still at full is not a dark sign — it is a bright sign that has stopped glowing, which
        /// is not what "closed" looks like from across the hall.
        /// </summary>
        public static void SetGlow(Material mat, Color colour, float strength)
        {
            if (mat == null) return;
            mat.color = colour;
            mat.SetColor(BaseColorId, colour);
            mat.SetColor(EmissionColorId, colour * strength);
        }

        /// <summary>
        /// An unlit, see-through material for one of the mood badges over a customer's head.
        ///
        /// Unlit because a badge is a symbol, not an object: one that dimmed when the customer walked
        /// out of a lamp's pool would be a symbol that stops working in the shadows. Transparent
        /// because the icons are round and the corners of their quad have to go away — which in URP
        /// takes the surface mode, the blend factors, the depth write AND the keyword, all four, and
        /// missing any one of them leaves an opaque square with a circle drawn on it.
        ///
        /// Cached by name: four badges serve every customer in the hall.
        /// </summary>
        public static Material Badge(string icon)
        {
            Material found;
            if (Badges.TryGetValue(icon, out found) && found != null) return found;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            var mat = new Material(shader);
            Texture2D map = Map("../T_Mood_" + icon);
            if (map != null)
            {
                mat.SetTexture(BaseMapId, map);
                mat.mainTexture = map;
            }
            mat.SetColor(BaseColorId, Color.white);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            Badges[icon] = mat;
            return mat;
        }

        private static readonly Dictionary<string, Material> Badges = new Dictionary<string, Material>(8);

        private static Shader Lit
        {
            get
            {
                if (_lit == null) _lit = Shader.Find("Universal Render Pipeline/Lit");
                if (_lit == null) _lit = Shader.Find("Sprites/Default");
                return _lit;
            }
        }

        /// <summary>
        /// One of the maps, loaded once and remembered — including the misses, so a project without the
        /// texture folder asks Resources for a file that is not there once rather than every frame.
        ///
        /// A name starting <c>../</c> steps back out of the <c>T_Market_</c> prefix, which is how the
        /// badges (<c>T_Mood_*</c>) share this cache without a second one.
        /// </summary>
        private static Texture2D Map(string name)
        {
            Texture2D found;
            if (Maps.TryGetValue(name, out found)) return found;
            found = Resources.Load<Texture2D>(name.StartsWith("../")
                ? "Market/Textures/" + name.Substring(3)
                : TextureRoot + name);
            Maps[name] = found;
            return found;
        }
    }
}
