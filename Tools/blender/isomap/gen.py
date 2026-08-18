"""Headless island generator.

    /Applications/Blender.app/Contents/MacOS/Blender --background \
        --python Tools/blender/isomap/gen.py -- copper 1 2 3

Builds one island at each named phase and writes it into the Unity project.
With no phases it does all three. `all` in place of an island name walks
island.ISLANDS.

Order per phase is build -> 14_routes -> 13_export, and it is not negotiable:
the export strips the vehicles and the hidden source objects out of the scene,
and the routes step reads what was actually laid.

Each phase starts from a clean slate (01_setup calls clear_scene), so one
process can do all three - and all eight islands - without the maps piling up
on top of each other.
"""
import sys
import math
import importlib as il
import traceback

HERE = "/Users/macbookair/Documents/GitHub/project_kayseri/Tools/blender/isomap"
if HERE not in sys.path:
    sys.path.insert(0, HERE)

# Everything the build touches, dropped so a rerun in the same process starts
# from source rather than from whatever the last island left behind. The isle_
# modules go too: a derived island star-imports its base, so a stale base is a
# silver island drawn on last run's copper.
for _m in ("island", "geom", "layout", "grade", "lib", "tex", "parts", "bake",
           "detail", "roadmask", "settle", "shot", "yard", "survey",
           "isle_coal", "isle_copper", "isle_iron", "isle_gold",
           "isle_silver", "isle_ruby", "isle_emerald", "isle_diamond"):
    sys.modules.pop(_m, None)

import lib
il.reload(lib)
if not hasattr(lib, "floor"):          # 11_dressing reaches for it through lib
    lib.floor = math.floor

import island

BOOT = {"__name__": "__boot__"}
exec(compile(open(HERE + "/00_boot.py").read(), "00_boot.py", "exec"), BOOT)


def generate(isle, phases):
    for ph in phases:
        print("\n=== %s phase %d ===" % (isle, ph), flush=True)
        print(BOOT["build"](ph, isle=isle), flush=True)
        print(BOOT["run"]("14_routes", ph), flush=True)
        print(BOOT["run"]("13_export", ph), flush=True)


def main(argv):
    names = [a for a in argv if not a.isdigit()]
    phases = [int(a) for a in argv if a.isdigit()] or [1, 2, 3]
    if not names or names == ["all"]:
        names = list(island.ISLANDS)
    for n in names:
        if n not in island.ISLANDS:
            raise SystemExit("unknown island %r - expected one of %s"
                             % (n, ", ".join(island.ISLANDS)))

    failed = []
    for n in names:
        try:
            generate(n, phases)
        except Exception:
            traceback.print_exc()
            failed.append(n)
    print("\n=== done: %d island(s), phases %s ==="
          % (len(names), phases), flush=True)
    if failed:
        print("FAILED: " + ", ".join(failed), flush=True)
        raise SystemExit(1)


main(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:])
