# UI sprite pipeline

The Figma set that ships the game's UI is text-free by design, so anything it is missing has to
be drawn in the same language rather than typed into a layout. These scripts are that: SVG
authored in Python, rasterised to transparent PNG by headless Chrome, dropped into `Assets/Art/UI/`.

No rasteriser library is installed on the dev machine, but Chrome is — hence the `--headless=new
--screenshot` trick. A standalone `.svg` document would pick up Chrome's centring and bake stray
padding into the sprite, so every piece is inlined into a zero-margin HTML page sized exactly to
its artboard.

## Files

| | |
|---|---|
| `harita_parcalari.py` | The world-map showcase set: 8 ore emblems, medallion disc + gold frame, aura, ray wheel, sparkle, dark glass plate, blue CTA pill, grey CTA pill. |
| `rasterize_harita.py` | Renders the above straight into `Assets/Art/UI/Harita/`. |
| `yeni_parcalar.py` | The earlier batch: rope link, page pips, "you are here" pin, gem price pill, contract icon. |
| `rasterize_yeni.py` | Renders that batch into a local `png/` folder; the pieces were filed into `Gostergeler/`, `Ikonlar/` and `Butonlar/` by hand. |

## Running

```
python Tools/ui/rasterize_harita.py
```

Run it from anywhere — the destination resolves relative to the script. Unity re-imports on focus.

## House style

Every shape carries the set's anatomy, sampled out of the shipped PNGs:

- a **navy `#182840` outline** on every solid body
- a **vertical body gradient**, light at the top
- a **white gloss band** across the upper third
- **ASCII-only SVG, no `<text>` nodes** — localisation must never be able to reflow a sprite

Two pieces are deliberately colourless. `harita_disk` and `harita_aura` are drawn white so the
code can tint them with each island's ore colour, which is why one artboard serves all eight
islands instead of eight near-identical files.

## Adding a piece

Add an entry to `PIECES`, re-run the rasteriser, then set its import settings once in Unity —
sprite mode and, if the piece will be stretched, its 9-slice border. `harita_tabela` uses
`(90, 90, 90, 90)`; `btn_git` and `btn_pasif` slice horizontally only, `(110, 0, 110, 0)`, because
their drop shadow sits below the body and a vertical slice would smear it.
