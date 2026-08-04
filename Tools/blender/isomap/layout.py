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
