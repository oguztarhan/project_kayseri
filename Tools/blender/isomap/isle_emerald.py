"""Emerald island - the coal map, mined for beryl instead.

The original island's land, roads and districts, down to the coke ovens by
the works. Coal is #0 on the ladder and emerald is #6, which is as far apart
as two islands sharing a map can be.

The coke ovens staying is deliberate: 16_theme's props are the island's
signature, and the stock they burn through comes off parts.coal_pile, which
asks the island for its ore - so the piles round the battery come out green
here without a line of theme code changing.
"""
# Reload the base BEFORE the star-import. layout.py reloads only the module it
# resolved - this one - so without this the base stays on whatever it was when
# it first imported, and editing isle_coal.py in a live Blender session would
# change the base island and leave this one on the old map.
import importlib as _il
import isle_coal as _base
_il.reload(_base)
from isle_coal import *                       # noqa: E402,F401,F403

NAME = "emerald"
DESIGN = "coal"

# Beryl in schist: deep green crystal in a dark, near-black host rock. The
# host is what keeps this from reading as the copper island's malachite -
# that ore is green THROUGH, this one is green IN something.
ORE = "ore_em"
ORE_SHINY = "ore_em_shiny"

# The painted ground. The coal map ships no palette at all - it is 02_terrain's
# default green coast over grey rock - so this is the first time these are set
# for it: the same wet coast, deepened, over green-black schist.
GROUND_RAMP = [(0.014, 0.052, 0.024),    # deep shade, hollows and stream beds
               (0.028, 0.092, 0.034),
               (0.048, 0.142, 0.046),
               (0.078, 0.196, 0.060),
               (0.118, 0.244, 0.076),
               (0.172, 0.292, 0.102)]    # sun on the exposed tops
GROUND_EARTH = (0.118, 0.108, 0.062)     # bare cut and spoil
GROUND_ROCK = (0.216, 0.256, 0.222)      # schist country rock
GROUND_SAND = (0.352, 0.348, 0.286)      # the bay beaches
