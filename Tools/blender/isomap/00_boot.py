"""Bootstrap: put the isomap package on sys.path and (re)load lib.

run(step, phase) injects the upgrade phase into the step's globals:
    PHASE = 1  -> level 0-15   (starter camp)
    PHASE = 2  -> level 15-30  (industrialised)
    PHASE = 3  -> maxed        (fully automated)
Steps read PHASE directly, or use PK(a, b, c) to pick a per-phase value.
"""
import sys, importlib

ISOMAP = "/Users/macbookair/Documents/GitHub/project_kayseri/Tools/blender/isomap"
if ISOMAP not in sys.path:
    sys.path.insert(0, ISOMAP)

import lib
importlib.reload(lib)

PHASE = 1


def set_phase(p):
    global PHASE
    PHASE = max(1, min(3, int(p)))
    return PHASE


def run(step, phase=None):
    """Execute a step file fresh, with lib's namespace already in globals."""
    ph = PHASE if phase is None else max(1, min(3, int(phase)))
    path = "%s/%s.py" % (ISOMAP, step)
    g = {"__name__": "__main__", "__file__": path}
    g.update({k: getattr(lib, k) for k in dir(lib) if not k.startswith("__")})
    g["lib"] = lib
    g["PHASE"] = ph
    g["PK"] = lambda a, b, c: (a, b, c)[ph - 1]
    exec(compile(open(path).read(), step + ".py", "exec"), g)
    return "%s done (phase %d)" % (step, ph)


STEPS = ("01_setup", "02_terrain", "03_roads", "04_rail", "05_mine",
         "06_depot", "07_refinery", "08_market", "09_port", "10_traffic",
         "11_dressing", "12_sites")


def build(phase=1, verbose=False):
    set_phase(phase)
    for s in STEPS:
        r = run(s, phase)
        if verbose:
            print(r)
    return "island built at phase %d: %s" % (phase, lib.stats())
