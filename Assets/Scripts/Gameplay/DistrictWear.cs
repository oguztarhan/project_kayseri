using System.Collections.Generic;
using UnityEngine;
using Econ = Game.Core.IslandEconomy;

namespace Game.Gameplay
{
    /// <summary>
    /// What neglect LOOKS like: grime creeping over the districts whose station has been left to wear,
    /// and lifting again as the crew puts them right.
    ///
    /// The island's art carries no textures — <c>Kayseri/IslandVertexLit</c> takes its colour from
    /// baked vertex colours — so there is no dirt map to fade in. Instead the shader carries a
    /// <c>_Grime</c> term, and this swaps a worn district onto worn VARIANTS of the very same
    /// materials. That works out cheaper than it sounds: the SRP Batcher keys on the shader rather
    /// than on the material, so a filthy mine and a clean market still draw in one batch, and the
    /// variants are built once, on demand, and shared by every district that reaches that tier.
    ///
    /// FOUR TIERS, not a continuous fade. A material swap is not something to do per frame, and the
    /// eye cannot read the difference between 61% and 64% worn anyway — what it reads is "that yard
    /// has got worse since I last looked". Quantising also bounds the variant count at three per
    /// authored material, whatever happens to the wear curve.
    ///
    /// Districts are re-resolved on a slow poll rather than cached at startup, because the art is
    /// rebuilt under this as the island advances through its three phases: a renderer list taken at
    /// Start would be pointing at a phase-1 shed nobody can see by the time the mine is worn.
    ///
    /// A plain class rather than a MonoBehaviour, for the same reason <see cref="SiteLife"/> and
    /// <see cref="StationCrew"/> are: <see cref="CoalOperation"/> already owns the per-island update
    /// order, and this has no lifecycle of its own.
    /// </summary>
    public sealed class DistrictWear
    {
        /// <summary>How often the districts are re-read. Wear moves on the scale of hours.</summary>
        private const float PollSeconds = 0.5f;

        /// <summary>How dirty each tier draws. Index 0 is never used — that is the clean material.</summary>
        private static readonly float[] TierGrime = { 0f, 0.35f, 0.70f, 1f };

        /// <summary>
        /// Damage at which each tier takes over, as <c>Maintenance.Damage</c> reports it (0 = as new,
        /// 1 = at the floor). The first band is deliberately wide: a station that is 5% worn should
        /// look after itself, or the island would never once be seen clean.
        /// </summary>
        private static readonly float[] TierFrom = { 0f, 0.18f, 0.45f, 0.75f };

        /// <summary>
        /// The districts that show neglect, and the station whose state of repair drives each.
        ///
        /// The names are the art objects <see cref="Kayseri.Island.IslandPhaseController.ActiveDistrict"/>
        /// hands back, and the stations are the ones that already advance each district's phase, so
        /// grime spreads over exactly the buildings the neglected station owns.
        ///
        /// Terrain, Foliage and Theme are deliberately absent. Grass does not rust and cliffs do not
        /// get grubby, and dirtying the ground the buildings stand on would read as a rendering fault
        /// rather than as neglect. Roads ARE included: potholes and filthy tarmac are half of what an
        /// abandoned industrial site looks like.
        /// </summary>
        private static readonly string[] Districts =
        { "Mine", "Rail", "Depot", "Refinery", "Market", "Port", "Power", "Haul", "Fleet", "Roads", "Civic", "Sites", "Props" };

        /// <summary>Driver station per district; -1 = follow the WORST station on the island.</summary>
        private static readonly int[] Drivers =
        {
            Econ.Mine, Econ.Train, Econ.Storage, Econ.Smelter, Econ.Market, Econ.CargoTrucks,
            Econ.Power, Econ.OreTrucks, Econ.CargoTrucks, Econ.OreTrucks, -1, -1, -1,
        };

        private static readonly int GrimeId = Shader.PropertyToID("_Grime");

        /// <summary>
        /// One district's renderers and the state they are currently drawn in.
        ///
        /// <see cref="clean"/> holds the material arrays the art shipped with, so putting a district
        /// right is a restore rather than a guess — and so a district that is already dirty when its
        /// phase changes cannot have a worn variant mistaken for its authored material.
        /// </summary>
        private sealed class Zone
        {
            public int driver;
            public string district;
            public Transform art;
            public Renderer[] rends;
            public Material[][] clean;
            public int tier;
        }

        private readonly Kayseri.Island.IslandPhaseController _phases;
        private readonly Zone[] _zones;

        /// <summary>
        /// Worn variants, keyed by the authored material they came from. Built on demand and never
        /// released: there are 78 island materials and three tiers, and a district that has been dirty
        /// once will be dirty again — throwing the variants away would mean rebuilding them on every
        /// tier change for the rest of the session.
        /// </summary>
        private readonly Dictionary<Material, Material[]> _variants = new Dictionary<Material, Material[]>();

        private float _poll;

        /// <summary>
        /// How worn a station is, 0 (as new) to 1 (at the floor). Answered by
        /// <see cref="CoalOperation"/>, which is the only thing holding the maintenance service.
        /// </summary>
        public System.Func<int, float> Damage;

        /// <summary>
        /// Test override: -1 follows the actual state of repair, 0..3 forces every district onto that
        /// tier. This is how the whole range of the look can be inspected without waiting three days
        /// for an island to get there on its own.
        /// </summary>
        public int ForcedTier = -1;

        public DistrictWear(Kayseri.Island.IslandPhaseController phases)
        {
            _phases = phases;
            _zones = new Zone[Districts.Length];
            for (int d = 0; d < Districts.Length; d++)
                _zones[d] = new Zone { district = Districts[d], driver = Drivers[d] };
        }

        public void Tick(float dt)
        {
            if (_phases == null) return;

            _poll += dt;
            if (_poll < PollSeconds) return;
            _poll = 0f;
            Refresh();
        }

        /// <summary>Re-reads every district: where its art is now, and how worn it should look.</summary>
        public void Refresh()
        {
            if (_phases == null) return;

            for (int z = 0; z < _zones.Length; z++)
            {
                Zone zone = _zones[z];

                Transform art = _phases.ActiveDistrict(zone.district);
                if (art == null) continue;                  // no phase builds this district on this island
                if (art != zone.art) Bind(zone, art);       // the island rebuilt underneath us

                int want = ForcedTier >= 0 ? Mathf.Min(ForcedTier, TierGrime.Length - 1) : TierFor(zone.driver);
                if (want == zone.tier) continue;
                Apply(zone, want);
            }
        }

        /// <summary>Puts every district back to its authored materials. For a teardown or a wipe.</summary>
        public void Clear()
        {
            for (int z = 0; z < _zones.Length; z++)
                if (_zones[z].tier != 0) Apply(_zones[z], 0);
        }

        private int TierFor(int station)
        {
            if (Damage == null) return 0;
            float damage = Damage(station);
            int tier = 0;
            for (int t = TierFrom.Length - 1; t >= 1; t--)
                if (damage >= TierFrom[t]) { tier = t; break; }
            return tier;
        }

        /// <summary>Caches a district's renderers and the materials it was authored with.</summary>
        private void Bind(Zone zone, Transform art)
        {
            zone.art = art;
            zone.rends = art.GetComponentsInChildren<Renderer>(true);
            zone.clean = new Material[zone.rends.Length][];
            for (int r = 0; r < zone.rends.Length; r++)
                zone.clean[r] = zone.rends[r] != null ? zone.rends[r].sharedMaterials : null;
            // The freshly-bound art is drawn clean; whatever the old art was showing does not carry over.
            zone.tier = 0;
        }

        private void Apply(Zone zone, int tier)
        {
            zone.tier = tier;
            if (zone.rends == null) return;

            for (int r = 0; r < zone.rends.Length; r++)
            {
                Renderer rend = zone.rends[r];
                Material[] clean = zone.clean != null ? zone.clean[r] : null;
                if (rend == null || clean == null) continue;

                if (tier == 0) { rend.sharedMaterials = clean; continue; }

                // A fresh array per swap. Unity's sharedMaterials setter copies whatever it is handed,
                // so there is nothing to be gained by keeping one around, and a tier change is a few
                // times an hour rather than a few times a frame.
                var worn = new Material[clean.Length];
                for (int m = 0; m < clean.Length; m++) worn[m] = Variant(clean[m], tier);
                rend.sharedMaterials = worn;
            }
        }

        /// <summary>
        /// The worn version of one authored material, made once and shared. A material the shader does
        /// not give a <c>_Grime</c> to — anything on the island that is not on the island shader — is
        /// handed back untouched rather than cloned into a variant that could never look any different.
        /// </summary>
        private Material Variant(Material clean, int tier)
        {
            if (clean == null || !clean.HasProperty(GrimeId)) return clean;

            Material[] tiers;
            if (!_variants.TryGetValue(clean, out tiers))
            {
                tiers = new Material[TierGrime.Length];
                _variants[clean] = tiers;
            }

            if (tiers[tier] == null)
            {
                var worn = new Material(clean) { name = clean.name + "_worn" + tier };
                worn.SetFloat(GrimeId, TierGrime[tier]);
                tiers[tier] = worn;
            }
            return tiers[tier];
        }
    }
}
