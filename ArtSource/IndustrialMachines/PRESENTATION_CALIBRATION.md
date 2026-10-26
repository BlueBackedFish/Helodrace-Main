# Tile scale and 12px outline, 2026-10-26

The user requested two new circa-1950 reactors and a size correction for the five power buildings, using the radio and 2×1 machine tools as references. A 12px external black stroke is applied to the updated 512px exports. This is distinct from the earlier 6px density pass.

## Reference evidence

The active `HD_Radio` is a 256px sprite, footprint 1×1, drawSize 1.5 and zero drawOffset. Its North opaque width is 185px, or 1.084 game tiles. The 2×1 milling machine uses a 512px sprite and drawSize 3: 350 opaque pixels correspond to 2.051 tiles. The turret lathe uses the same drawSize and has 381 opaque pixels (2.232 tiles including projecting tooling). References are inspected, not modified.

The radio desktop and machine bed suggest about 0.95 tile of solid base width per occupied tile. The power models already have comparable physical base dimensions: small 1.90 units for two tiles, large 3.79 and diesel 3.80 for four, generator 2.86 and motor 2.80 for three. Their former automatic camera fit and independent drawSize values made the game presentation inconsistent. Mesh resizing would have changed already-suitable proportions.

## Corrected rule

One model unit equals one game tile. Camera ortho_scale and XML drawSize are equal. The default canvas is 1.5 times the larger footprint dimension, following the radio and 2×1 tool references. If tall geometry needs more room, canvas and drawSize expand together in quarter-unit steps, keeping a 24px clear final margin plus the 12px stroke. This changes padding without changing world size. The small engine needs scale 3.25 to accommodate the chimney in East.

Camera target and game drawOffset are both (0,0,0). The foundation origin stays registered to the footprint; equipment height projects upward in the image rather than being compensated by an arbitrary sprite offset. All directions use 64° downward orthographic elevation and one common scale.

| Power asset | Previous drawSize | Corrected drawSize | Mesh / paint |
|---|---|---|---|
| Small steam | 2.55 | 3.25 | unchanged, hash verified |
| Large steam | 4.3 | 6 | unchanged, hash verified |
| Diesel | 4.3 | 6 | unchanged, hash verified |
| Generator | 3.3 | 4.5 | unchanged, hash verified |
| Motor | 3.3 | 4.5 | unchanged, hash verified |

Both new reactors use 3×3 footprints, scale/drawSize 4.5 and zero offset. They retain Graphic_Single and their existing North texPaths. Standard active material-mask names end in `_northm.png`; obsolete `_north_m.png` files are retained only as source references. Recipes, statistics, research and electrical behavior are unchanged.

## Review and verification

`resize_power.py -- <asset-folder>` reframes one existing saved model, exports its three views, checks geometry/material hashes and changes only that definition's drawSize/drawOffset. Ordinary per-asset build scripts reproduce the same calibrated presentation.

`review_tile_scale.py` reads actual XML sizes and composes all references and seven updated models at **64 pixels per game tile**. It does not crop-fit or normalize object widths. `Review/tile_scale_comparison.png` shows current sizes; `Review/power_scale_before_after.png` compares the previous and corrected draw settings. Before PNGs are retained in `Review/BeforeScale/`. `presentation_calibration.json` records measured opaque widths in game tiles. Gray backgrounds and grids are review-only.

`verify_assets.py` reopens seven Blender scenes and checks 21 camera elevations, shared scale, zero ground targets, matching XML draw sizes, 42 color/mask files and active mask naming. It retains the physical recessed-flywheel and flat motor-vent checks. Color masks and black outline alpha stay aligned. No in-game visual test has been performed.
