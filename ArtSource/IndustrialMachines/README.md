# Period industrial building sprite tools

Each building has an independent source folder and build command. `sprite_common.py` supplies fixed Emission palettes, simple solid primitives, open spoked wheels, arched field yokes, governors and per-asset export. It does not build multiple buildings in one invocation. The 1900–1950 full redesign brief and primary historical references are in [PERIOD_DESIGN.md](PERIOD_DESIGN.md).

North views from -Y, East from +X, South from +Y, at 64° downward orthographic elevation. One common scale per model is calculated from all three views; transparent 512×512 PNGs and corresponding red-channel material masks are exported to `Textures/Buildings`. Brass, copper and dark electrical insulators are unselected. No lights, shadows, AO or specular highlights are used. Saved scenes are reopened and camera elevation, framing, dimensions and mask-alpha alignment are checked. Review sheets are ordered North / East / South at 512px and 256px per direction. In-game testing is not included.

All five buildings share neutral gray cast iron, darker cast beds, bright machined iron and fixed copper/brass accents. The workshop-family comparison replaces the previous green low-contrast scheme. Broad surfaces use modest face separation; small cylinder barrels use a single color with separate end caps. Connected pumps, drives, broad conductors and bordered service lids provide functional detail. Foundations are cut down to follow the equipment, with bearing wings on the electrical machines. The electrical generator and motor have independent texture prefixes and scenes. Concrete and deep pit interiors are unselected in material masks.

Export adds a 6px black alpha-dilated silhouette beneath each 512px render. Opaque source colors are preserved; the stroke is black/unselected in the material mask and shares the color image's alpha. This is deterministic export processing, not geometry or lighting in the saved Blender scene. The original raw render can be recreated from that scene; run the build script to produce the final outlined game PNGs.

| Building | Independent build script | Texture prefix |
|---|---|---|
| Small workshop steam plant | `ArtSource/SmallSteamEngine/build_engine.py` | `HD_SmallSteamEngine` |
| Large steam engine | `ArtSource/LargeSteamEngine/build_engine.py` | `HD_LargeSteamEngine` |
| Industrial diesel engine | `ArtSource/IndustrialDieselEngine/build_engine.py` | `HD_IndustrialDieselEngine` |
| Open-field generator | `ArtSource/EarlyIndustrialGenerator/build_generator.py` | `HD_EarlyIndustrialGenerator` |
| Enclosed electric motor | `ArtSource/EarlyIndustrialMotor/build_motor.py` | `HD_EarlyIndustrialLargeMotor` |

Run each script independently through Blender background mode, then `overview.py` to compose `overview_north.png`: small steam / large steam / diesel across the top and generator / motor below. Review tiles each have a 512px canvas; they are not a common world-unit scale across differently sized buildings. Each model's three directions do share one scale. The five existing game footprints and behaviors are retained. The generator continues to use its independent texture prefix. Original reference textures and masks are preserved in the respective source folders.

The large steam engine uses a genuine open floor slot: the complete flywheel extends below the concrete floor, with a dark pit bottom and walls. There is no solid foundation slab across the opening. See its README for dimensions.

Run `verify_assets.py` through Blender background mode to check five definition links, unchanged footprints, all 30 color/mask files and the per-asset render checks. It reopens all five scenes to verify orthographic cameras, 64° elevation and equal scale across directions, measures smaller foundation top areas, checks neutral body materials and black unselected outline pixels against the actual exported color/mask pairs, checks the flywheel geometry below the floor and clearance inside its slot, and verifies that the motor vent faces lie in the end-shield plane. Results are saved in `integration_validation.json`; this does not replace in-game visual testing.

`analyze_family.py` records dominant opaque RGB values and dark edge widths from neighboring sprites and current models. `family_palette_audit_before.json` is the retained pre-correction snapshot. Run `review_family.py` with Python/Pillow to generate labeled `Review/family_context.png` and `Review/before_after_north.png`; the source before images are preserved in `Review/Before/`. Those contact sheets use a gray backdrop only for inspection; game texture backgrounds stay transparent.
