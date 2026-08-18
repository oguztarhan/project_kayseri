"""The island currently being built - a thin front for isle_<name>.py.

Every build step does `import layout` + `importlib.reload(layout)`, so this
module re-resolves on each reload and the whole generator follows whichever
island `island.use(...)` last selected. Steps never import isle_coal or
isle_copper directly; they only ever see `layout`.

To add a third island: write isle_<name>.py exporting the same names as
isle_coal.py, and add it to island.ISLANDS.
"""
import importlib

import island
import geom

importlib.reload(geom)

_mod = importlib.import_module("isle_" + island.NAME)
importlib.reload(_mod)

# Re-export the island's whole public surface. Doing it this way rather than
# with `from ... import *` means a name added to an isle_ module is picked up
# without touching this file.
globals().update({k: v for k, v in vars(_mod).items() if not k.startswith("_")})

ISLAND = island.NAME

# Which of the four AUTHORED maps this island is drawn from. A derived island
# (isle_silver and friends) sets DESIGN to the base it re-exports, so the
# build steps that branch on a map's land - country rock in 01_setup, the
# painted ground in 02_terrain, the trees in 11_dressing - follow the map
# rather than the ore. Branching those on ISLAND would give the silver island
# the copper map with the coal island's grey granite on it.
DESIGN = getattr(_mod, "DESIGN", island.NAME)
