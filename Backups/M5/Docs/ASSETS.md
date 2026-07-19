# Ore Empire *(working title)* — Asset Catalog & Blender Prompts

Companion to [GDD.md](GDD.md) and [PLAN.md](PLAN.md). Every 3D asset the game needs, with a **Blender-generation prompt** for each. Workflow: **model in Blender (via Blender MCP) → export `.glb` → import to Unity → drag into the prefab's mesh slot** (no code change — GDD §14, §16).

> **How to use this file:** every asset's **Prompt** line is the *specific* part. Prepend the **Global Style Guide** (below) to it → that's the complete prompt you give Blender. This keeps all 40+ assets visually consistent.

---

## 0. Global Style Guide  *(prepend to EVERY asset prompt)*

> *"Create a low-poly, game-ready 3D model for a mobile idle tycoon game. Style: bright, friendly, slightly cartoony — clean flat surfaces, soft rounded edges, chunky readable shapes. Solid FLAT colors only (vertex colors or simple single-color materials); NO textures, NO high-frequency detail, NO bevelled micro-edges. It must read clearly from an isometric top-down camera at ~45° from a distance. Technical: keep polycount within the budget stated; tris/quads only, no n-gons; ONE material where possible (add a second/third material slot only for parts that need a distinct color); apply all transforms (scale = 1,1,1, rotation = 0); 1 Blender unit = 1 metre = 1 Unity unit; Z-up (Unity import converts to Y-up). Model faces −Y (front toward camera-forward). Export as glTF Binary (.glb), triangulated. It must look good untextured under a single directional light."*

**Shared palette (hex):**
`grass #86D06A / #4E9440` · `dirt/tan #D8B888` · `wood #8A5A3C` · `snow #F2F7FF` · `rock #8B8FA3 / #565E6B` · `steel #7A879F` · `gold-warm #F2C14E / #C6922E` · `orange #F5923E` · `red-roof #E8663B` · `train-green #2FA96B` · `truck-blue #3E7CC4`
**Ore tiers:** `coal #2B2F36` · `copper #E08A4C` · `iron #9AA0AA` · `silver #D7DCE5` · `gold #FFCF4D` · `ruby #E5484D` · `emerald #2FBF71` · `sapphire #3B82F6` · `diamond #7FE3F0`
**Currency:** `coin #FFD34E` · `premium gem #A855F7`

**Global technical conventions**
- **Scale reference:** ground tile = 2×2 u; a truck ≈ 2 u long; train engine ≈ 2.5 u; a building ≈ 3–4 u wide, 2–4 u tall; a mountain ≈ 8–12 u wide, 6–10 u tall.
- **Pivot/origin:** buildings, mountains, props, tiles → **base centre** (sits on ground at Y=0). Vehicles → **centre-bottom**. Rail/road segments → **segment centre**. Coins/small pickups → **centre**.
- **Materials:** flat colours from the palette; separate slots ONLY for tint-swappable parts (so Unity can recolour per tier/biome).
- **Naming on export:** `SM_<Category>_<Name>` (e.g., `SM_Building_Refinery`, `SM_Ore_Chunk`).
- **Out of scope for Blender** (handled elsewhere): VFX = Unity particle systems; UI icons = rendered from these models or 2D; music/SFX = audio; manager portraits = 2D art.

---

## A. Terrain & Environment

### A1 · Ground Tile
**Look:** flat 2×2 u square ground tile, very slightly beveled top edge, grass-green. Tileable seamlessly. **Features:** <100 tris · pivot base-centre · 1 material (biome-tintable). **Prompt:** *"a simple flat square ground tile, 2×2 units, subtle rounded top edge, plain grass-green top; designed to tile seamlessly edge-to-edge; a thin darker soil band on the sides."*

### A2 · Island Edge / Cliff Slab
**Look:** the "floating island" outer edge — grass top, thick soil/rock underside tapering slightly inward. **Features:** <150 tris · modular to match tile width · 1–2 materials (grass + soil). **Prompt:** *"a chunky island-edge cliff piece: grass-green top matching the ground tile, a thick tapered soil-and-rock underside about 1.5 units deep, low-poly faceted rock face."*

### A3 · Water Tile *(optional dressing)*
**Look:** flat translucent-cyan water plane for edges/decoration. **Features:** <40 tris · 1 material. **Prompt:** *"a flat stylized water tile, 2×2 units, calm light-cyan surface with a couple of low-poly gentle wave facets."*

---

## B. Mountains  *("mountain types" — the ore sources)*

### B1 · Rocky Mountain *(Coal / Copper / Iron biome)*
**Look:** faceted grey-brown peak, a few angular ledges, flat-shaded. Slot at the base front for a mine entrance. **Features:** <1000 tris · pivot base-centre · 1–2 materials (rock + soil). **Prompt:** *"a low-poly faceted mountain peak, ~10 units wide and ~8 tall, grey-brown angular rock with a few flat ledges and a slightly flattened front base area to attach a mine entrance; chunky stylized silhouette, no snow."*

### B2 · Snowy Mountain *(Silver / Diamond biome)*
**Look:** taller, sharper grey peak with a clean white snow cap on the top third. **Features:** <1100 tris · 2 materials (rock + snow). **Prompt:** *"a low-poly snowy mountain, ~10 wide ~9 tall, sharp faceted grey rock with a crisp pure-white snow cap covering the top third; a flat base area at the front for a mine entrance."*

### B3 · Volcanic Mountain *(Ruby / gem biome)*
**Look:** dark charcoal rock, red-orange glowing crack lines, small crater rim. **Features:** <1200 tris · 2–3 materials (dark rock + lava-glow + rim). **Prompt:** *"a low-poly volcano, ~10 wide ~8 tall, dark charcoal faceted rock with bright red-orange lava veins running down the sides and a small crater rim at the top; ominous but still stylized and friendly."*

### B4 · Crystal / Emerald Mountain *(Emerald / Sapphire biome — optional)*
**Look:** grey rock studded with large angular green/blue crystal formations jutting out. **Features:** <1200 tris · 2 materials (rock + crystal, tintable). **Prompt:** *"a low-poly mountain with a grey faceted rock body and several large angular translucent green crystal shards jutting from its sides and peak; magical stylized look; flat base for a mine entrance."*

### B5 · Mine Entrance / Tunnel *(attaches to any mountain base)*
**Look:** wooden support frame around a dark arched opening, a short mine-cart rail stub, a couple of support beams. **Features:** <250 tris · pivot base-centre · 2 materials (wood + dark interior). **Prompt:** *"a small mine-tunnel entrance: a wooden A-frame support around a dark arched cave opening, two vertical timber beams and a lintel, a short mine-cart rail stub poking out; weathered brown wood, chunky low-poly."*

---

## C. Buildings / Stations

> **Upgrade tiers:** rather than remodel each building 3×, model the **base building + a small "tier add-on kit"** (extra chimney, extension wing, bigger sign, antenna) that Unity enables at higher levels. Prompt per building includes the base; add-ons listed under C6.

### C1 · Storage Yard / Depot
**Look:** a low wide warehouse with a shallow gable roof + open-air ore bays beside it (low walls forming bins). Warm walls, blue-grey roof. **Features:** <1400 tris · pivot base-centre · 2–3 materials. **Prompt:** *"a low-poly warehouse depot: a wide building with warm sand-coloured walls, a shallow blue-grey gable roof, a big sliding door on the front, and two open-topped low-walled ore bays beside it for stockpiling ore; chunky and friendly."*

### C2 · Refinery / Factory
**Look:** blocky steel-grey industrial building, 1–2 chimneys, a few pipes, an orange hazard stripe, a conveyor stub. **Features:** <1500 tris · pivot base-centre · 3 materials (steel + orange accent + pipes). **Prompt:** *"a low-poly refinery factory: a blocky steel-grey industrial building with one tall chimney, a couple of external pipes, a bright orange hazard stripe near the base, and a small conveyor-belt stub on the front; clean stylized industrial look."*

### C3 · Market / Trading Post
**Look:** warm inviting shop with a striped awning, a sale counter/window, a hanging sign, small crates of goods out front. Gold walls, red roof. **Features:** <1400 tris · pivot base-centre · 3 materials. **Prompt:** *"a low-poly market trading post: a warm golden-walled shop with a red gabled roof, a striped awning over a front counter, a hanging shop sign on a post, and a couple of goods crates out front; cheerful marketplace feel."*

### C4 · Train Station / Main Stop Platform
**Look:** a raised platform beside the rail with a small roofed shelter, a name-board, and an ore-unloading chute. **Features:** <900 tris · pivot base-centre · 2 materials. **Prompt:** *"a low-poly train station platform: a raised stone platform running alongside a rail track, a small open shelter with a pitched roof on posts, a station name-board, and a tilted ore-unloading chute; tidy and stylized."*

### C5 · Truck Loading Dock *(optional)*
**Look:** a small concrete loading bay with a ramp, bollards, and a hanging ore/cargo hopper. **Features:** <500 tris · pivot base-centre · 2 materials. **Prompt:** *"a small low-poly loading dock: a concrete bay with a short ramp, two yellow bollards, and an overhead hopper chute for filling trucks; simple industrial."*

### C6 · Building Tier Add-on Kit
**Look:** a small set of bolt-on upgrade props — extra chimney, side extension wing, larger sign, rooftop tank, string lights. **Features:** each <150 tris · pivots to sit on the base buildings · matching materials. **Prompt:** *"a small kit of low-poly building upgrade add-ons matching a warm cartoony factory/warehouse style: (1) an extra chimney, (2) a boxy side-extension wing, (3) a bigger billboard sign on posts, (4) a rooftop cylindrical tank, (5) a string of festoon lights — each as a separate object with its origin where it bolts onto a building."*

---

## D. Vehicles

### D1 · Train Engine (Locomotive)
**Look:** friendly chunky steam/diesel loco — rounded cab, boiler barrel, a stubby chimney, big + small wheels. Recolorable body. **Features:** 500–800 tris · pivot centre-bottom · wheels as separate objects (optional spin) · 2 materials (body-tint + trim). **Prompt:** *"a cute low-poly train locomotive, ~2.5 units long: rounded driver cab at the back, a cylindrical boiler at the front, a short chimney on top, a cow-catcher at the front, chunky wheels; a bright green body with dark trim; wheels modelled as separate objects; friendly cartoon proportions."*

### D2 · Ore Wagon (Train Car)
**Look:** open-top hopper car that holds a mound of ore; couplers front and back. **Features:** <500 tris · pivot centre-bottom · 2 materials (body + empty interior) · leave the top open so an ore-mound mesh can sit inside. **Prompt:** *"a low-poly open-top mining hopper wagon, ~1.8 units long, with sloped inner walls, simple couplers on both ends, and chunky wheels; brown/steel body; the top is open so a separate ore pile can be placed inside."*

### D3 · Ore Truck (raw-ore hauler)
**Look:** small dump-truck: cab + open tipper bed for raw ore; blue cab. **Features:** 400–700 tris · pivot centre-bottom · wheels separate · 2 materials (cab-tint + bed). **Prompt:** *"a low-poly dump truck, ~2 units long: a rounded cab and an open tipper bed for carrying loose ore; blue cab, grey bed, four chunky wheels as separate objects; cheerful stylized proportions; the bed is open so an ore load can sit inside."*

### D4 · Cargo Truck (product hauler)
**Look:** flatbed/box truck carrying crates or products; orange cab. **Features:** 400–700 tris · pivot centre-bottom · wheels separate · 2 materials. **Prompt:** *"a low-poly cargo truck, ~2.2 units long: a rounded cab and a flatbed rear with low side rails for carrying crates; orange cab, wooden flatbed, chunky separate wheels; friendly stylized look; the bed is open so crates can be placed on it."*

---

## E. Rail & Road Kit  *(modular, tileable, 2 u segment length)*

### E1 · Rail — Straight   ### E2 · Rail — Curve (90°)   ### E3 · Rail — Junction/Switch   ### E4 · Rail — Buffer/End
**Look:** two steel rails on brown wooden ties over a gravel bed. Consistent 2 u length so pieces snap together. **Features:** each <120 tris · pivot segment-centre · 2 materials (rails + ties/gravel). **Prompt (make all four as a set):** *"a modular low-poly railway track kit, 2-unit segment length so pieces tile seamlessly: (1) straight, (2) 90° curve, (3) T-junction switch, (4) buffer/end-stop — each with two steel rails on evenly spaced brown wooden ties over a low grey gravel bed; separate objects, each origin at its segment centre."*

### E5 · Road — Straight   ### E6 · Road — Curve
**Look:** packed dirt/tan path with faint wheel ruts, subtle raised edges. 2 u segments. **Features:** each <80 tris · pivot segment-centre · 1 material. **Prompt:** *"a modular low-poly dirt road kit, 2-unit segments (straight + 90° curve): flat packed tan-dirt surface with faint darker wheel ruts and slightly raised soft edges; tiles seamlessly; separate objects, origin at segment centre."*

---

## F. Raw Ore  *(one base mesh, recolored per tier)*

### F1 · Raw Ore Chunk *(coal · copper · iron · silver · gold)*
**Look:** a fist-sized angular rock with a few embedded mineral facets. **One mesh, recolored** in Unity per tier. **Features:** <150 tris · pivot centre · 2 materials (rock-base + mineral-facet, both tintable). **Prompt:** *"a small low-poly ore chunk: an angular faceted rock with 3–4 flat gem/mineral facets embedded on top; neutral grey rock body with a separate material on the mineral facets so it can be recoloured per ore type; roughly 0.4 units across."*

### F2 · Gem Crystal Cluster *(ruby · emerald · sapphire · diamond)*
**Look:** a small cluster of angular pointed crystals on a rocky base. Recolored per gem. **Features:** <200 tris · pivot centre · 2 materials (rock + crystal, crystal tintable). **Prompt:** *"a small low-poly crystal cluster: 3–5 sharp angular faceted crystal shards of varying heights growing from a little grey rock base; the crystals on their own material so they can be recoloured (red, green, blue, white); clean faceted gem look."*

### F3 · Ore Pile / Stockpile
**Look:** a low heap of ore chunks for filling wagons, bins, and storage bays. **Features:** <200 tris · pivot base-centre · tintable · comes in 2–3 sizes (small/med/large) OR scalable. **Prompt:** *"a low-poly pile of loose ore: a rounded heap of small angular rock chunks, faceted flat-shaded, sized to sit inside a wagon or storage bin; provide it as a single tintable mesh; make small, medium, and large versions."*

---

## G. Refined Products

### G1 · Coke / Fuel Briquettes
**Prompt:** *"a small low-poly stack of dark coke fuel briquettes — a neat pyramid of chunky black-grey blocks."* (<120 tris, pivot base-centre.)

### G2 · Metal Bar *(copper · silver · gold — one mesh recolored)*
**Prompt:** *"a single low-poly metal ingot/bar with slightly trapezoidal sides and a flat top, clean and shiny-looking through flat colour alone; one material so it can be tinted copper, silver, or gold."* (<120 tris, pivot base-centre.)

### G3 · Steel Beam (I-beam)
**Prompt:** *"a low-poly steel I-beam girder, medium length, classic H cross-section, flat steel-grey."* (<150 tris, pivot centre.)

### G4 · Cut Gem *(ruby · emerald — one mesh recolored)*
**Prompt:** *"a single faceted cut gemstone, classic brilliant/emerald cut with clean flat facets, on one tintable material; looks premium through flat shading."* (<200 tris, pivot base-centre.)

### G5 · Polished Diamond
**Prompt:** *"a low-poly polished diamond, brilliant cut with a pointed base and flat top table, bright icy-cyan/white, crisp facets."* (<220 tris, pivot base-centre.)

### G6 · Ruby Ring *(jewelry)*
**Prompt:** *"a low-poly gold ring holding a large faceted red ruby in a small pronged setting; two materials (gold band + red gem)."* (<220 tris, pivot base-centre.)

### G7 · Diamond Crown *(endgame jewelry)*
**Prompt:** *"a low-poly royal gold crown with pointed peaks, studded with several small gems and one large cyan diamond at the front; two–three materials (gold + gems)."* (<300 tris, pivot base-centre.)

### G8 · Product Crate  *(what cargo trucks carry)*
**Look:** a wooden shipping crate with metal corners and a stamped symbol; tintable band shows contents. **Prompt:** *"a low-poly wooden shipping crate with metal corner brackets and a coloured label band on the front; the label band on its own material so it can be tinted to indicate contents."* (<150 tris, pivot base-centre.)

---

## H. Currency & Reward Props

### H1 · Coin (cash)
**Prompt:** *"a low-poly gold coin, thick chunky disc with a simple embossed star/ore symbol on the face; bright gold."* (<100 tris, pivot centre — used for pickups, piles, UI renders.)

### H2 · Premium Gem
**Prompt:** *"a low-poly faceted premium gemstone, hexagonal brilliant cut, bright purple, clearly distinct from the ore gems; slight glossy flat-shaded look."* (<120 tris, pivot centre.)

### H3 · Cash Pile / Money Bag *(optional)*
**Prompt:** *"a low-poly money bag with a tied neck and a coin symbol, plus a few loose gold coins spilling at its base."* (<200 tris, pivot base-centre.)

---

## I. Set Dressing *(polish — biome flavour, all optional)*

### I1 · Pine Tree
**Prompt:** *"a low-poly stylized pine tree: a stacked-cone green canopy of 2–3 tiers on a short brown trunk; chunky and cartoony."* (<150 tris.)

### I2 · Boulder / Rock Scatter
**Prompt:** *"a set of 3 low-poly faceted boulders in small/medium/large, plain grey rock, for scattering on terrain."* (<120 tris each.)

### I3 · Bush / Shrub
**Prompt:** *"a small low-poly rounded bush, a cluster of faceted green blobs; a couple of variations."* (<100 tris.)

### I4 · Stylized Cloud
**Prompt:** *"a low-poly fluffy cloud built from overlapping rounded white blobs, flat-shaded, for a floating parallax sky."* (<150 tris.)

### I5 · Props Kit (fence, signpost, lamp, barrel)
**Prompt:** *"a small kit of low-poly set-dressing props matching a warm cartoony mining town: a wooden fence segment, a signpost with an arrow board, a simple street lamp, and a wooden barrel — each a separate object with base-centre origin."* (each <150 tris.)

---

## J. Characters *(optional — juice / managers)*

### J1 · Worker / Miner Figure *(optional)*
**Prompt:** *"a simple low-poly cartoon miner: chunky blocky proportions, hard hat, dungarees, no facial detail beyond flat shapes; A-pose; kept minimal for tiny on-screen size; rig-friendly clean topology."* (<800 tris, pivot base-centre.) *Manager portraits are 2D art, not modelled here.*

---

## K. Import checklist (Blender → Unity)

1. **Apply all transforms** in Blender (Ctrl+A → All Transforms); scale must read 1,1,1.
2. Origin set per the pivot rule above (base-centre for most).
3. Assign flat-colour materials from the palette; keep tint-swappable parts on their own material slot.
4. Export **glTF Binary (.glb)**, +Y up (glTF default), include materials, triangulate.
5. Name it `SM_<Category>_<Name>`.
6. In Unity: import to `Assets/Art/Models/…`, check scale (1 unit = 1 m), then **drag the mesh into the station/vehicle prefab's `[SerializeField]` mesh slot** — greybox is replaced, zero code change.

---

## L. Production priority (matches PLAN.md milestones)

1. **First playable art (after M2):** Rocky Mountain (B1), Mine Entrance (B5), Storage (C1), Refinery (C2), Market (C3), Train (D1) + Wagon (D2), Ore Truck (D3) + Cargo Truck (D4), Rail kit (E1–E4), Ore Chunk (F1), Ground Tile (A1).
2. **Chain completeness:** Products (G1–G8), Gem Crystal (F2), Road kit (E5–E6), Station platform (C4), Coins/Gems (H1–H2).
3. **Biome expansion (M4):** Snowy (B2), Volcanic (B3), Crystal (B4) mountains; ore-tier recolours.
4. **Polish (M6):** tier add-ons (C6), set dressing (I1–I5), clouds, optional worker (J1).
