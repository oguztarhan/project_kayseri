# Island generator (Blender)

Fully scripted generator for the Kayseri island maps. Rebuilds a whole island
from code at any of three upgrade phases and exports it to
`Assets/Art/Kayseri*/Models/`.

Needs Blender 5.x with the Blender MCP addon (the `blender` MCP server is
user-scoped, so it's available from any Claude Code session).

## Two islands

There are two maps. They share every building, vehicle, prop and material - only
the land, the water, the road/rail routing and the ore differ.

| | coal | copper |
|---|---|---|
| ocean | screen **left** (small x+y) | screen **right** (large x+y) |
| mountains | one massif behind the mine, ridges framing the top and bottom | a range down the whole west side |
| river | hugs the far western edge, crosses nothing | runs the **full width of the frame** through the middle of town |
| bridges | none | 6 - both arterials, the ring road twice, the quarry spur |
| districts | mine W, depot N, refinery E, market S | mine W, **depot S**, refinery E, **market N** |
| ore | black coal | malachite-green copper |
| preview ortho | 380 | 440 |

The depot and market swap ends of the north-south arterial on the copper island
because the market has to be the coastal district - it is the one that feeds the
port. Both are axis-aligned around their own centre and the arterial runs
through them either way, so their models are untouched.

## Rebuild + re-export everything

```python
import sys, importlib as il, math
P = "/Users/macbookair/Documents/GitHub/project_kayseri/Tools/blender/isomap"
if P not in sys.path: sys.path.insert(0, P)
for m in ("island","geom","isle_coal","isle_copper","layout","grade",
          "lib","tex","parts","bake","shot"):
    sys.modules.pop(m, None)
import lib; il.reload(lib)
if not hasattr(lib, "floor"): lib.floor = math.floor
g = {"__name__": "__boot__"}
exec(compile(open(P + "/00_boot.py").read(), "00_boot.py", "exec"), g)

for ph in (1, 2, 3):
    g["build"](ph, isle="copper")      # or isle="coal"
    g["run"]("14_routes", ph)          # gameplay centrelines + anchors -> JSON
    g["run"]("13_export", ph)          # strip vehicles, bake colours, export FBX
```

Then in Unity: **Kayseri → Island → Build All (Copper)**.

Blender does not have to be open. The same thing runs headless, which is faster
and scriptable — about 30 seconds per phase:

```bash
/Applications/Blender.app/Contents/MacOS/Blender --background --python gen.py -- copper 1 2 3
```

`14_routes` before `13_export`: the export strips the vehicles and hidden source
objects out of the scene, and the routes step reads what was actually laid.

## Where it lands

| | |
|---|---|
| FBX | `Assets/Art/KayseriIsland/Models/<Island>/Phase<n>/<Group>_P<n>.fbx` |
| routes | `Assets/Art/KayseriIsland/Routes/<island>_routes_P<n>.json` |
| palette | `Assets/Art/KayseriIsland/palette.json` — **merged**, never rewritten |

The palette merge is what lets an island bring its own materials: copper adds
`ore_cu` and `ore_cu_shiny`, and without an entry there Unity builds no material
for them, the FBX remap finds nothing, and the ore piles import onto the default
grey Lit material — which does not read vertex colour at all.

## Files

| | |
|---|---|
| `island.py` | which island is selected. Never reloaded, so the choice survives the `reload(layout)` every step does |
| `layout.py` | thin front for the selected island - re-exports `isle_<name>` |
| `isle_coal.py` / `isle_copper.py` | **all the coordinates for one map** |
| `geom.py` | island-independent maths: ring, straight, dist_to_path, smoothstep, shore |
| `00_boot.py` | runner. `build(phase, isle=)` runs every step; `run(step, phase)` runs one |
| `grade.py` | the graded height field - derives arterial profiles from whichever district sits at each road end |
| `lib.py` | mesh builder (`B`), instancing, path/strip helpers, map-frame conversion |
| `tex.py` | procedural material factory (noise / voronoi / wave + ColorRamp) |
| `parts.py` | reusable assets - trucks, ships, cranes, silos, tanks, trees, buildings |
| `01..12_*.py`, `15_town.py` | build steps, in order |
| `13_export.py` | vehicle strip + vertex-colour bake + FBX export |
| `shot.py` | `shot()` renders the iso view, `zoom()` renders a close-up |

## Adding a third island

Write `isle_<name>.py` exporting the same names as `isle_coal.py`, and add the
name to `island.ISLANDS`. Nothing else needs to change - no step imports an
`isle_` module directly.

## Phases

`PHASE` is injected into every step; `PK(a, b, c)` picks the per-phase value.

```
1  levels 0-15   dirt roads, timber camps, 1 pier, sites locked
2  levels 15-30  paved roads, industrial plant, quarry + storage unlocked
3  maxed         double track + catenary, container terminal, all sites
```

## Layout constraints worth knowing before moving anything

These are geometric, not stylistic. Most of them cost real time to find.

- **World axes read as screen diagonals.** `screen_x = 0.7071*(x+y)`,
  `screen_y = 0.5254*(y-x) + 0.669*z`. Districts on the world cardinal axes land
  in the four screen quadrants.
- **Screen height is the compressed axis** - 0.5254 per unit against 0.7071, over
  a frame half-height of 127 against 190. Anything much past `y - x = 200` leaves
  the top of the frame, and z pushes it further. This is why the copper island
  has no site in the world north-west, and why its preview needs a wider ortho.
- **Peaks combine with `max()`, not sum.** Summing put a 145-unit peak entirely
  above the frame.
- **A river through the middle cannot have a constant width.** At a constant 24
  it eats the central crossroads at one end and a district pad at the other -
  there is no straight line across the map that clears both. `river_carve(t)`
  pinches it to a rock notch through the built middle and opens it out at either
  end.
- **A town yard's grade FEATHER is what fouls the ring road, not its slab.**
  Keep `r + TOWN_PAD + 14` under about 71.
- **Port clearances are measured, not eyeballed** - both the gap to the market
  pad and the quay's alignment to the shore. The copper harbour has three
  exactly collinear shore points so the quay has a straight face to sit on.
- **Which side the sea is on is data** (`SEA_AXIS`), not a sign convention. Two
  places used to assume the coal island's half-plane: the surf line in
  `02_terrain.py` and - worse - the quay normal in `09_port.py`, which built the
  whole port mirrored about its own quay.

## Vertex colours

Blender's procedural materials cannot cross FBX, so `13_export.py` evaluates each
material per mesh corner and bakes the result into a `CORNER` byte-colour
attribute. Unity's `Kayseri/IslandVertexLit` blends between `_BaseColor` and that
vertex colour via `_VertexColorAmount`.

Detail survives in proportion to mesh density: terrain and coal keep their grain,
an 8-vertex box roof does not.
