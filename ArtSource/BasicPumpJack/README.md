# Timber basic pumpjack

Reworked 6×3 basic pumping unit: heavy timber sills, braced Sampson trestles, a thick wooden walking beam and pitman, wooden wheel spokes with an iron tire, polished rod/packing gland, belt-input pulley and small wooden receiving barrel. Broad joinery and iron straps establish wood construction without procedural grain or clutter. The existing line-shaft/oil-production behavior, Woody material category, cost and footprint are retained.

Vanilla timber colors are baked (top 133/97/67, side 113/83/57) using the radio desk family. Timber and iron are unselected in the material mask, keeping the intended brown wood and gray hardware instead of applying wood tint to ironwork. This fixes the visible wood palette; wood-species recoloring is not provided by this artwork.

Historical guidance: [Petroleum History Institute's central-power pumpjack](https://www.petroleumhistory.org/OilHistory/pages/Central%20Power/pump.html) and [Kansas Geological Survey's early oil-production account](https://www.kgs.ku.edu/Publications/Bulletins/6_1/02_origin.html). This is a new simplified design, not a trace or reconstruction of a specific example. Original texture/mask are preserved in `Reference/` and packed in the Blender scene.

Build: run Blender background mode with `--python ArtSource/BasicPumpJack/build_pumpjack.py`. Scene `HD_BasicPumpJack.blend`; texture prefix `Buildings/HD_BasicPumpJack`. North/East/South share 64° downward orthographic cameras, scale/drawSize 9 and zero target/drawOffset. Final 512px transparent PNGs have 12px black outlines and aligned masks. Review sheets and `validation.json` are generated. No in-game verification is claimed.

Four-direction export (2026-10-26): North / East / South / West are actual 64-degree orthographic renders of the same saved geometry and colors; West is not mirrored East. See preview_four_views.png and validation.json.
