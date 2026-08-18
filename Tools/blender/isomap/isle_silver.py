"""Silver island - the copper map, mined for argentite instead.

Every coordinate, road, bridge and district on this island IS the copper
island's: same land, same river through the middle of town, same six bridges,
same leach ponds up by the mine. What changes is the ore in the heaps and the
tint of the country rock it sits in.

That is the whole point of a derived island. Four authored maps carry eight
ore islands, and the ladder never puts the same map twice in a row - copper is
#1 and silver is #3, with iron between them.

DESIGN is what the build steps branch on (01_setup's country-rock overrides,
02_terrain's painted ground, 11_dressing's trees). NAME is what the export
paths and the routes JSON are named for. Everything else comes in from the
base island untouched.
"""
# Reload the base BEFORE the star-import. layout.py reloads only the module it
# resolved - this one - so without this the base stays on whatever it was when
# it first imported, and editing isle_copper.py in a live Blender session would
# change the base island and leave this one on the old map.
import importlib as _il
import isle_copper as _base
_il.reload(_base)
from isle_copper import *                       # noqa: E402,F401,F403

NAME = "silver"
DESIGN = "copper"

# Argentite and galena: lead-grey metal in a pale gangue, with the bright
# metallic flash silver is actually recognised by. Same heap geometry as every
# other island - parts.coal_pile asks the island for the material name.
ORE = "ore_ag"
ORE_SHINY = "ore_ag_shiny"

# The painted ground - 02_terrain reads these, and they have to agree with the
# silver block in 01_setup or the grey outcrops would sit on copper's rust
# hillsides. The GRASS ramp is NOT overridden: this is still the copper
# island's temperate coast, and the country rock is the thing that moved.
GROUND_EARTH = (0.150, 0.148, 0.152)     # cool grey cut ground and spoil
GROUND_ROCK = (0.300, 0.312, 0.338)      # grey granite with a blue cast
GROUND_SAND = (0.408, 0.412, 0.428)      # grey beaches under a grey range

# The copper map's theme props, asked of the island in 16_theme.py. A silver
# works leaches with cyanide rather than acid, casts silver cathode rather than
# copper, and has no verdigris on its roof - that is copper's corrosion
# product. The pond, the plate stack and the pump house are the landmark that
# names the island, so they are the last place to leave copper's colours.
LIQUOR = "leach_ag"
PLATE = "plate_ag"
PATINA = "steel_lt"      # plain galvanised roof in place of the verdigris one
