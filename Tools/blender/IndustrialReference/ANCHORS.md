# Anchor manifest — IndustrialReference map

**For Codex.** Everything gameplay needs to bind to, measured on the map as it
already exists.

- Data: `anchors.json` (this folder)
- Generator: `build_anchors.py` — re-run with `python3 build_anchors.py`
- Space: **Unity world metres**, identical to `map_geometry.json`, so a value
  drops straight into a Transform with no conversion.
- Target: `Assets/Prefabs/Island/IndustrialReference/IndustrialReference_Map.prefab`

**Nothing in the map was changed.** No mesh moved, no prefab edited, no scene
touched, no re-export.

45 anchors, 17 routes, 5 station pads. `build_anchors.py` asserts on every run
and refuses to write a manifest that fails.

## What changed since the first handoff

Codex was right on all four counts. The fixes:

**Ground is measured, not assumed.** v1 used a building's lowest vertex as the
ground height for every anchor on that plot. On a terraced island that is
wrong — an anchor 0.9m off the +z wall stands on the *next terrace up*. Every
anchor is now raycast down onto the real walkable surface at its own x/z
(terrace meadows and cliffs, roads, the quay, the jetty, the bridges, the
customer islets) and the run fails if the ground is missing or off-level. The
check is `abs(ground - anchor.y) <= 0.06` for every ground anchor.

**Faces moved.** The terraces are ~5m deep and his buildings sit hard against
the uphill retaining wall, so **there is no standable ground on any plot's +z
face** — that is what buried the four entrances. Material now enters on the
+x flank and leaves on the −z face:

| suffix | where |
|---|---|
| `_Input` | +x flank, uphill half |
| `_Output` | −z face, downhill |
| `_Work` | plot centre |
| `_Worker` | +x flank, downhill half |
| `_Upgrade` | above the roof — a badge, **not** a ground anchor |

Offsets are not fixed. Each face is scanned outward from 1.0m to 0.4m and the
largest offset still on level, unoccupied ground wins.

**Nothing that toggles shares a building.** v1 double-booked two plots, which
is exactly why unlocking was unresolvable. Only the five equipment stations
ever toggle; Refinery and Storage are logistics points with two anchors each,
no building and no locked state. So the five buildings go to the five
stations and the two process zones get their own always-on props:

| zone | home | locked state |
|---|---|---|
| `Station_Cannon` | `05_Smelter/Smelting_Plant` | hide the group, show the pad |
| `Station_Hull` | `06_Factory/Blue_Factory` | hide the group, show the pad |
| `Station_Rigging` | `07_Refinery/Refinery` | hide the group, show the pad |
| `Station_Navigation` | `08_Harbor/Warehouse` | hide the group, show the pad |
| `Station_Figurehead` | **no art** | pad only |
| `Refinery` | `07_Refinery/Horizontal_Tank` | never hidden |
| `Storage` | the two stacked containers | never hidden |
| Mine, Port, Crane, Ship | his art | never hidden |

Storage keeps working when Navigation is locked, and the player can still sail
when Figurehead is locked, because neither zone lives on a station's building
any more.

**Figurehead has no building.** The island is full — six 4-6m plots on 5m-deep
terraces with trees and props in every gap — so there is no room to invent a
sixth. It gets the one clear patch of level waterfront left, between the quay
and the jetty: **1.2 × 0.7m at (−0.56, 1.07, −11.66)**. That is the largest
clear, flat, unoccupied rectangle within reach of the harbour; the search tries
3.0×2.4 first and works down. Locked state is the bare pad. Built state needs
art that does not exist yet. Flagged `needs_art: true` in `anchors.json`.

## Construction pads

Every station carries a `pad` in `zones[]`:

```json
{"id": "Station_Hull", "art_group": "06_Factory/Blue_Factory",
 "hide_when_locked": ["06_Factory/Blue_Factory"], "needs_art": false,
 "pad": {"centre": [0.194, 3.715, 0.494], "size": [6.222, 4.559]}}
```

For the four stations with art the pad is that building's own footprint —
`SetActive(false)` on the group and drop a pad quad in its place. For
Figurehead the pad is all there is.

## Routes

`routes[]`, each `{from, to, kind, points}` — a polyline of world-space
waypoints, y already on the ground (+0.05 clearance).

| kind | routes | what it is |
|---|---|---|
| `delivery` | 8 | the production chain, mine → cannon → hull → refinery → rigging → storage → navigation → figurehead → sail |
| `worker` | 5 | one loop per station: idle spot → input → output → back |
| `rail` | 1 | the existing rail, `Train_Load` → `Train_Unload` |
| `sea` | 3 | `Set_Sail` → each customer berth |

Routes come from A* on a 0.25m walkable grid, not straight lines. A cell is
walkable when there is ground and nothing of his stands on it; a step is
allowed when the rise is under 0.55m, or under 1.1m if both cells are on his
road. That single rule is what makes them usable — terrace faces jump 1.3m and
get rejected, so every path between two terraces is forced onto his own
switchback road, which is the only graded connection on the map.

Three deliberate exceptions, all in `build_anchors.py`:

- his road is never blocked — bounding boxes overlap it constantly (the
  refinery slab's box swallows the terrace 3 ramp) and honouring them severs
  the island into four unreachable terraces;
- pine needle cones and scattered pebbles do not block — his ramps run straight
  through both;
- geometry entirely below your feet or 2m over your head does not block, so the
  quay pilings and the crane boom are not walls.

Customers are sea lanes rather than ground paths: his bridges from the jetty to
the islands exist, but the moored ship lies across the western one, and a
delivery boat is the fiction anyway.

## Camera

`Camera_Stop_01..07` sit on plot centres in travel order, on the ground.
`Camera_Bounds` is the island only — the sea planes are excluded deliberately;
they span 236 × 200m and would let the camera drift out over open water.

## Checks

`build_anchors.py` asserts on every run:

1. all 45 contract names present;
2. every ground anchor within 0.06m of the measured ground under it, and none
   over water;
3. no anchor inside geometry (camera stops and the three rail anchors are
   exempt — they are meant to be on the plot centre and on the rail);
4. no two anchors sharing one coordinate;
5. every point of every land route on the ground.

## What is still missing

- **Figurehead Atelier art.** A 1.2 × 0.7m building. Nothing else is blocked
  on it — the pad, anchors and routes are all there.
- Collision proxies and LODs. Milestone 6, needs the scene running.
