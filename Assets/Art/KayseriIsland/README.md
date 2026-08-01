# Kayseri Island — imported map

The island map authored in Blender, exported as FBX per district and per upgrade
phase. Trucks, vans and all rolling stock were stripped on export — you're adding
those by hand. Ships, cranes, loaders, excavators and forklifts stayed, since they
read as scenery.

## First-time setup

1. Let Unity import the FBX and compile the two shaders.
2. Menu: **Kayseri → Island → Build All**

That runs four steps in order, and they can also be run individually:

| Step | What it does |
|---|---|
| 1. Configure Model Imports | Sets scale, normals, no animation/cameras/lights, and remaps FBX materials onto the URP materials |
| 2. Create Materials | Generates one URP material per Blender material from `palette.json` |
| 3. Build Phase Prefabs | Assembles `Island_Phase1/2/3.prefab` from the FBX groups |
| 4. Build Scene | Writes `Assets/Scenes/KayseriIsland.unity` with an isometric camera + sun |

Run **2 before 1** if you're re-running by hand — the import step needs the
materials to already exist so it can remap onto them.

## Why the map lands in the right place

Every FBX has world transforms baked in and is exported with Blender's Z-up →
Y-up conversion. Each district group is dropped at the Unity origin with identity
transform and reassembles the map exactly. Nothing is positioned by hand, so
there's no drift and no re-authoring if a district is re-exported.

## Phases

```
Models/Phase1/   levels 0-15    dirt roads, timber camps, 1 pier, sites locked
Models/Phase2/   levels 15-30   paved roads, industrial plant, quarry + storage unlocked
Models/Phase3/   maxed          double track + catenary, full container terminal, all sites
```

`IslandPhaseController` (on the `Island` root) activates one phase root at a time:

```csharp
_islandPhaseController.SetPhase(2);          // direct
_islandPhaseController.SetPhaseForLevel(21); // maps level -> phase
```

Terrain is per-phase, not shared: the quarry pit is carved into the heightfield
from phase 2 onward.

## Materials and vertex colours

The Blender materials were **procedural** (noise / voronoi / wave), and procedural
shading does not survive FBX. Instead the colour each procedural material produces
was evaluated per mesh corner and baked into a **vertex colour** attribute. That's
what carries the surface detail — terrain variation, coal grain, corrugated roofs —
at zero texture memory, which suits the mobile target.

`Kayseri/IslandVertexLit` multiplies `_BaseColor` by the vertex colour. Both
shaders are hand-written HLSL (not Shader Graph) and are SRP Batcher compatible —
every pass shares one `UnityPerMaterial` CBUFFER.

- `_VertexColorAmount = 1` → full baked detail
- `_VertexColorAmount = 0` → flat material colour

Transparent materials (water, sea, glass, smoke, ghost previews) use
`Kayseri/IslandVertexLitTransparent` automatically, picked by alpha < 1 in the palette.

## Mesh budget

Exported as-is, hierarchy intact, so you can see and code against individual
buildings. Not yet optimised:

| Phase | Objects | Tris |
|---|---|---|
| 1 | ~1,630 | ~180k |
| 2 | ~1,650 | ~205k |
| 3 | ~1,640 | ~230k |

Foliage and rocks are linked duplicates in Blender, so they import as many
renderers sharing a handful of meshes — good candidates for GPU instancing.
When you're ready to optimise: enable instancing on the foliage materials first,
then static-batch or merge per district. Merging costs you per-building objects,
so do it after the upgrade system is wired.

## Regenerating

The Blender build is fully scripted. Re-exporting overwrites the FBX in place and
Unity re-imports; the prefabs keep their references because the group names and
file paths are stable. Re-run **Build All** after a re-export.

## Camera

The scene camera reproduces the Blender framing: orthographic, `size 126.7`,
rotation `(48, -45, 0)`, position `(331, 520, -331)`. That size assumes a 3:2
viewport — on a phone aspect you'll want to retune `orthographicSize` for the
framing you want.
