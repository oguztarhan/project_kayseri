# Island generator (Blender)

Fully scripted generator for the Kayseri island map. Rebuilds the whole island
from code at any of three upgrade phases and exports it to
`Assets/Art/KayseriIsland/Models/`.

Needs Blender 5.x with the Blender MCP addon (the `blender` MCP server is
user-scoped, so it's available from any Claude Code session).

## Rebuild + re-export everything

```python
import sys, importlib as il, math
P = "/Users/macbookair/Documents/GitHub/project_kayseri/Tools/blender/isomap"
if P not in sys.path: sys.path.insert(0, P)
import lib; il.reload(lib)
if not hasattr(lib, "floor"): lib.floor = math.floor
g = {"__name__": "__boot__"}
exec(compile(open(P + "/00_boot.py").read(), "00_boot.py", "exec"), g)

for ph in (1, 2, 3):
    g["build"](ph)          # build the island at that phase
    g["run"]("13_export", ph)   # strip vehicles, bake vertex colours, export FBX
```

Then in Unity: **Kayseri → Island → Build All**.

## Files

| | |
|---|---|
| `00_boot.py` | runner. `build(phase)` runs every step; `run(step, phase)` runs one |
| `layout.py` | **all shared coordinates** — districts, roads, rail, river, shore, port, sites, mountains |
| `lib.py` | mesh builder (`B`), instancing, path/strip helpers, map-frame conversion |
| `tex.py` | procedural material factory (noise / voronoi / wave + ColorRamp) |
| `parts.py` | reusable assets — trucks, ships, cranes, silos, tanks, trees, buildings |
| `01..12_*.py` | build steps, in order |
| `13_export.py` | vehicle strip + vertex-colour bake + FBX export |
| `shot.py` | `shot()` renders the iso view, `zoom()` renders a close-up |

## Phases

`PHASE` is injected into every step; `PK(a, b, c)` picks the per-phase value.

```
1  levels 0-15   dirt roads, timber camps, 1 pier, sites locked
2  levels 15-30  paved roads, industrial plant, quarry + storage unlocked
3  maxed         double track + catenary, container terminal, all sites
```

## Layout constraints worth knowing before moving anything

These bit me repeatedly — they're geometric, not stylistic:

- **Screen top-left = world far-west along the X road.** The camera is iso at yaw
  45, so world axes read as screen diagonals. Anything you place "above the mine
  on screen" is actually west along the road corridor, and the road's flatten
  mask will erase it. That's why `ROAD_X` dead-ends at the mine.
- **Screen top-centre is the railway arc.** The quarry site had to move to the
  coastal strip; a site there fights the track.
- **Tall terrain projects upward on screen.** A 145-unit peak at high `v` lands
  entirely above the frame. Peaks combine with `max()`, not sum, for this reason.
- **District pads are big diamonds on screen.** Secondary sites need radius > 100
  or they collide with the loop road.
- **Port vs market clearance** is measured, not eyeballed — the port's landward
  edge must stay clear of the market pad at `x >= -36`.

## Vertex colours

Blender's procedural materials cannot cross FBX, so `13_export.py` evaluates each
material per mesh corner and bakes the result into a `CORNER` byte-colour
attribute. Unity's `Kayseri/IslandVertexLit` blends between `_BaseColor` and that
vertex colour via `_VertexColorAmount`.

Detail survives in proportion to mesh density: terrain and coal keep their grain,
an 8-vertex box roof does not.
