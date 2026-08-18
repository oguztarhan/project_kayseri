"""Which island the generator is currently building.

This lives in its own module on purpose. Every build step starts with
``importlib.reload(layout)``, so a selection stored in layout itself would be
wiped on the first reload of the first step. Nothing ever reloads THIS module,
so the choice survives the whole build.

    import island
    island.use("copper")
    build(2)
"""

# The ore ladder, in unlock order. The last four are DERIVED islands: each
# re-exports one of the first four unchanged and swaps the ore. See
# isle_silver.py. The ladder deliberately never runs the same map twice in
# a row - copper #1 / silver #3, iron #2 / ruby #5, coal #0 / emerald #6,
# gold #4 / diamond #7.
ISLANDS = ("coal", "copper", "iron", "silver", "gold", "ruby", "emerald",
           "diamond")

NAME = "coal"


def use(name):
    """Select the island every later `import layout` will resolve to."""
    global NAME
    n = str(name).strip().lower()
    if n not in ISLANDS:
        raise ValueError("unknown island %r - expected one of %s"
                         % (name, ", ".join(ISLANDS)))
    NAME = n
    return NAME
