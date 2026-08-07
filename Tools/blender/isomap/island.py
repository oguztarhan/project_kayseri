"""Which island the generator is currently building.

This lives in its own module on purpose. Every build step starts with
``importlib.reload(layout)``, so a selection stored in layout itself would be
wiped on the first reload of the first step. Nothing ever reloads THIS module,
so the choice survives the whole build.

    import island
    island.use("copper")
    build(2)
"""

ISLANDS = ("coal", "copper", "iron", "gold")

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
