# Industrial sprite pipeline

Seven independent models share fixed Emission palettes and simple mechanical forms. Each has its own build script, source references, Blender scene, validation and North/East/South review sheets. Power-machine design history is in [PERIOD_DESIGN.md](PERIOD_DESIGN.md); the two circa-1950 reactors have individual READMEs. Current size and outline rules are in [PRESENTATION_CALIBRATION.md](PRESENTATION_CALIBRATION.md).

| Building | Independent script | Texture prefix |
|---|---|---|
| Small steam | `ArtSource/SmallSteamEngine/build_engine.py` | `HD_SmallSteamEngine` |
| Large steam | `ArtSource/LargeSteamEngine/build_engine.py` | `HD_LargeSteamEngine` |
| Diesel | `ArtSource/IndustrialDieselEngine/build_engine.py` | `HD_IndustrialDieselEngine` |
| DC generator | `ArtSource/EarlyIndustrialGenerator/build_generator.py` | `HD_EarlyIndustrialGenerator` |
| Electric motor | `ArtSource/EarlyIndustrialMotor/build_motor.py` | `HD_EarlyIndustrialLargeMotor` |
| Pressure reactor | `ArtSource/HighPressureReactor/build_reactor.py` | `HD_HaberBoschHighPressureReactor` |
| Stirred kettle | `ArtSource/StirredTankReactor/build_reactor.py` | `HD_StirredTankReactor` |

Run one script at a time with Blender background mode. Models use transparent color/material-mask pairs at 256px times the longest footprint dimension, fixed RGB Emission, no lights/shadows/AO/specular effects and neutral gray metal with limited copper/brass. The outline is a 12px black alpha-dilated stroke beneath the render, unselected in masks. It is deterministic export processing rather than geometry or lighting in the saved scene. Color/mask alpha matches. Review-only backgrounds never enter game textures.

All views use 64° downward orthographic cameras. Camera scale and XML drawSize are 1.5 times the longest footprint dimension; texture resolution is 256 times that dimension. Their ratio is fixed, so the 12px outline has the same game-space thickness everywhere. Matching camera shifts and XML drawOffsets provide headroom while preserving the ground anchor. PRESENTATION_CALIBRATION.md is authoritative; normalize_density.py publishes all 22 saved building models.

The five power models retain their geometry and painted materials in the scale pass. `resize_power.py -- <asset-folder>` reframes one existing saved model, hashes geometry/materials before and after, then updates only its drawSize/drawOffset. Their dense mechanical parts, shaped foundations and the large steam engine's genuine below-floor flywheel pit are retained.

Reactors keep their existing 3×3 sizes, recipes and stats. They now rotate using Graphic_Multi with unsuffixed texPaths and actual North/East/South/West renders. Their active material masks use the standard `_northm.png` ending. Obsolete `_north_m.png` files are preserved only in source Reference folders. Generator and motor continue to have independent texture prefixes.

`verify_assets.py` reopens seven scenes and checks 28 camera elevations, common scales, registered ground anchors, matching XML draw sizes, 56 PNG color/mask files, mask naming/alpha and neutral body/black outline colors. It also checks smaller power-machine foundation areas, the physical flywheel pit and coplanar motor vent faces. Results are in `integration_validation.json`; no in-game verification is claimed.

`overview.py` composes the seven North views as review thumbnails. Rows are small/large/diesel, generator/motor/pressure reactor, then stirred kettle. Those canvases alone are not a world-size comparison.

Run `review_tile_scale.py` with Python/Pillow for **64 pixels per game tile**, using actual XML draw sizes without bounding-box normalization. It produces `Review/tile_scale_comparison.png`, `Review/power_scale_before_after.png`, reactor North and 3-direction sheets, and `presentation_calibration.json`. Previous power PNGs for this correction are in `Review/BeforeScale/`.

`analyze_family.py` records reference/current dominant colors and dark edge widths. `family_palette_audit_before.json` retains the earlier green-paint snapshot. `review_family.py` produces normalized shape/color contact sheets; those are deliberately not size evidence. Its original density-pass before images remain in `Review/Before/`.

The timber/steel pumpjacks, rebuilt distillation tower and new rotary-drill artwork use the same primitives and presentation rules. Their independent builds, 12-view review, integration status and validation are documented in [the petroleum artwork set](../PetroleumMachines/README.md).

Four-direction rotation, new furnaces/cable-tool rig and engine smoke alignment are documented in [ROTATION.md](ROTATION.md).
