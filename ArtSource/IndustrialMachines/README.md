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

Run one script at a time with Blender background mode. All models use transparent 512px color/material-mask pairs, fixed RGB Emission, no lights/shadows/AO/specular effects and neutral gray metal with limited copper/brass. The outline is a 12px black alpha-dilated stroke beneath the render, unselected in masks. It is deterministic export processing rather than geometry or lighting in the saved scene. Color/mask alpha matches. Review-only backgrounds never enter game textures.

North is -Y, East +X and South +Y. Cameras look downward at 64°, share one ortho_scale per model and target the ground origin. XML drawSize equals ortho_scale and drawOffset is zero, making one model unit equal one game tile. Canvas starts at 1.5 times the larger footprint dimension (radio/2×1 machine-tool convention), expanding in quarter-unit steps if needed for 24px clear final margins. Expanding canvas and drawSize together preserves world size.

The five power models retain their geometry and painted materials in the scale pass. `resize_power.py -- <asset-folder>` reframes one existing saved model, hashes geometry/materials before and after, then updates only its drawSize/drawOffset. Their dense mechanical parts, shaped foundations and the large steam engine's genuine below-floor flywheel pit are retained.

Reactors keep their existing 3×3 sizes, recipes, stats and Graphic_Single North texPaths. East/South are review outputs. Their active material masks use the standard `_northm.png` ending. Obsolete `_north_m.png` files are preserved only in source Reference folders. Generator and motor continue to have independent texture prefixes.

`verify_assets.py` reopens seven scenes and checks 21 camera elevations, common scales, zero ground anchors, matching XML draw sizes, 42 PNG color/mask files, mask naming/alpha and neutral body/black outline colors. It also checks smaller power-machine foundation areas, the physical flywheel pit and coplanar motor vent faces. Results are in `integration_validation.json`; no in-game verification is claimed.

`overview.py` composes the seven North views at their native 512px canvases. Rows are small/large/diesel, generator/motor/pressure reactor, then stirred kettle. Those canvases alone are not a world-size comparison.

Run `review_tile_scale.py` with Python/Pillow for **64 pixels per game tile**, using actual XML draw sizes without bounding-box normalization. It produces `Review/tile_scale_comparison.png`, `Review/power_scale_before_after.png`, reactor North and 3-direction sheets, and `presentation_calibration.json`. Previous power PNGs for this correction are in `Review/BeforeScale/`.

`analyze_family.py` records reference/current dominant colors and dark edge widths. `family_palette_audit_before.json` retains the earlier green-paint snapshot. `review_family.py` produces normalized shape/color contact sheets; those are deliberately not size evidence. Its original density-pass before images remain in `Review/Before/`.

The timber/steel pumpjacks, rebuilt distillation tower and new rotary-drill artwork use the same primitives and presentation rules. Their independent builds, 12-view review, integration status and validation are documented in [the petroleum artwork set](../PetroleumMachines/README.md).
