# Fixed tile-density building textures

The square canvas grows by **256 × max(width, depth)** pixels: 1×1 → 256px; 2×1 / 1×2 / 2×2 → 512px; 3-tile long side → 768px; 4 → 1024px; 6 → 1536px. Every resolution uses the same **12px external outline**; it is never multiplied by resolution or building size.

The radio's frame convention is retained: drawSize and camera ortho_scale are **1.5 × max(width, depth)**. Resolution / drawSize is constant at 256 / 1.5 = 170.6667 texture pixels per game unit. The factor 1.5 includes transparent framing. One model unit remains one game unit. The outline occupies exactly **0.0703125 game units** on every building.

Tall equipment can shift the camera target upward. A matching projected drawOffset is written to XML, cancelling the shift at the model ground origin. Scale and pixel density never expand for headroom. Every view has at least 12px of clear final margin. North/East/South/West share one scale and a 64° downward orthographic angle. Mesh transforms, vertices, faces, assignments and painted colors are hash-checked before and after export.

Run Blender with `--python ArtSource/IndustrialMachines/normalize_density.py` to publish all 28 saved models, or append `-- <texture-prefix-or-source-folder>` for one. This is the current publishing step after any historical builder or model edit. It includes the early NC lathe/mill and 3x3 semiconductor fab, converter, blast furnace, batch still, lathes/milling, press, rolling mill, tailoring/gun benches and radio. Radio has no material mask; other color/mask pairs have matching alpha and an unselected black outline. Older neutral-metal mask selection and baked colored parts use an explicit saved-scene policy.

`density_catalog.json` records footprints, native resolutions, draw sizes/offsets, pixel densities and outline widths. All 28 assets have active game definitions. Existing buildings retain gameplay values during art export. The new steel pumpjack and rotary drill have separate gameplay definitions in `AdvancedOilExtraction.xml`, including electricity/diesel supplies and oil-system support.

`verify_rotation.py` validates 28 scenes, 112 cameras and 220 PNGs, including actual resolution/density, game-space outline width, masks, margins, geometry/color hashes, definition links, radio direction and smoke outlets. The seven-machine and four-petroleum validators retain their structural checks. `review_density.py` generates `density_before_after.png` at exactly 64 display pixels per game tile, without crop-fitting. Review backgrounds and grids are not in the transparent game textures. No in-game visual test has been performed.

Early NC machines and the semiconductor fab are active electric worktables. Their builders export the fixed tile density directly. `ArtSource/EarlyAutomation/verify_definitions.py` checks recipe coverage, bill workers, facility links, research and resource references; its review sheets show all three machines at a common game scale.
