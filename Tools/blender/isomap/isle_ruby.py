"""Ruby island - the iron map, mined for corundum instead.

The land is the iron island's: the same wide frame, the same three extra
roads and four extra pads, the same yard arms off the rail. See isle_iron.py
for why any of it sits where it does.

Iron is #2 on the ladder and ruby is #5, so they never appear back to back.
They do both mine something red, though, which is why 01_setup pushes this
island's ground the other way - off iron's rust laterite and onto the pale
marble corundum actually forms in. Red ore on red ground reads as one island;
red ore on grey-violet marble does not.
"""
# Reload the base BEFORE the star-import. layout.py reloads only the module it
# resolved - this one - so without this the base stays on whatever it was when
# it first imported, and editing isle_iron.py in a live Blender session would
# change the base island and leave this one on the old map.
import importlib as _il
import isle_iron as _base
_il.reload(_base)
from isle_iron import *                       # noqa: E402,F401,F403

NAME = "ruby"
DESIGN = "iron"

# Corundum: crimson crystal faces in a dark host, cut with the pink of the
# marble it grows in.
ORE = "ore_rb"
ORE_SHINY = "ore_rb_shiny"

# The painted ground - see the ruby block in 01_setup for why it moves this far.
# Same six-stop shade-to-sun structure as the iron ramp it replaces, hue only:
# dusty rose over marble instead of ferruginous laterite.
GROUND_RAMP = [(0.058, 0.042, 0.044),    # deep shade, hollows and gully floors
               (0.098, 0.072, 0.074),
               (0.152, 0.114, 0.116),
               (0.218, 0.168, 0.170),
               (0.288, 0.228, 0.228),
               (0.358, 0.296, 0.296)]    # sun on the exposed tops
GROUND_EARTH = (0.212, 0.170, 0.168)     # bare cut and spoil
GROUND_ROCK = (0.352, 0.332, 0.352)      # marble country rock
GROUND_SAND = (0.442, 0.396, 0.388)      # the beaches, all the way round
