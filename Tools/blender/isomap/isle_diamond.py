"""Diamond island - the gold map, worked for kimberlite instead.

The gold island's arid land and layout, unchanged. Gold is #4 and diamond is
#7, so the map repeats only at the very top of the ladder.

01_setup cools this island's ground off gold's sun-cured straw and onto the
blue-grey of a kimberlite pipe, which is the one thing that stops the last
island on the ladder looking like the fifth one with different heaps.
"""
# Reload the base BEFORE the star-import. layout.py reloads only the module it
# resolved - this one - so without this the base stays on whatever it was when
# it first imported, and editing isle_gold.py in a live Blender session would
# change the base island and leave this one on the old map.
import importlib as _il
import isle_gold as _base
_il.reload(_base)
from isle_gold import *                       # noqa: E402,F401,F403

NAME = "diamond"
DESIGN = "gold"

# Kimberlite: blue-gray host rock with the icy white flash of the stones in
# it. Raw diamond ore is rock, the same as gold ore is quartz - the sparkle
# is the accent, not the body.
ORE = "ore_dm"
ORE_SHINY = "ore_dm_shiny"

# The painted ground. Gold's straw ramp with the warmth taken out of it, over
# blue-grey kimberlite - see the diamond block in 01_setup. The ramp only
# cools; it does not turn green, because this is still the arid map.
GROUND_RAMP = [(0.062, 0.056, 0.042),    # deep shade, gully floors
               (0.112, 0.102, 0.076),
               (0.172, 0.160, 0.122),
               (0.238, 0.222, 0.172),
               (0.304, 0.288, 0.228),
               (0.376, 0.360, 0.292)]    # sun-cured tops
GROUND_EARTH = (0.250, 0.230, 0.205)     # pale cut ground and spoil
GROUND_ROCK = (0.352, 0.372, 0.402)      # kimberlite country rock
GROUND_SAND = (0.495, 0.478, 0.458)      # the south beaches

# The gold map's strong-room cage, asked of the island in 16_theme.py. Nothing
# is poured on a diamond island, so the cage stacks sorted parcels of the
# island's own ore instead of gold bars - same geometry, and the shiny ore is
# already the right thing to be looking at behind a steel cage.
PLATE = "ore_dm_shiny"
