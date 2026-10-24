# Period industrial building sprite tools

Each building has an independent source folder and build command. `sprite_common.py` supplies fixed Emission palettes, simple solid primitives, open spoked wheels, arched field yokes, governors and per-asset export. It does not build multiple buildings in one invocation. The 1900–1950 full redesign brief and primary historical references are in [PERIOD_DESIGN.md](PERIOD_DESIGN.md).

North views from -Y, East from +X, South from +Y, at 64° downward orthographic elevation. One common scale per model is calculated from all three views; transparent 512×512 PNGs and corresponding red-channel material masks are exported to `Textures/Buildings`. Brass, copper and dark electrical insulators are unselected. No lights, shadows, AO or specular highlights are used. Saved scenes are reopened and camera elevation, framing, dimensions and mask-alpha alignment are checked. Review sheets are ordered North / East / South at 512px and 256px per direction. In-game testing is not included.

All five buildings share muted foundry paint, cast beds, machined iron and fixed copper/brass accents. Broad surfaces use modest face separation; small cylinder barrels use a single color with separate end caps. Rivets and mechanical features are deliberately enlarged for sprite readability. The electrical generator and motor are independent designs with independent texture prefixes and scenes. Concrete and deep pit interiors are also unselected in material masks.

| Building | Independent build script | Texture prefix |
|---|---|---|
| Small workshop steam plant | `ArtSource/SmallSteamEngine/build_engine.py` | `HD_SmallSteamEngine` |
| Large steam engine | `ArtSource/LargeSteamEngine/build_engine.py` | `HD_LargeSteamEngine` |
| Industrial diesel engine | `ArtSource/IndustrialDieselEngine/build_engine.py` | `HD_IndustrialDieselEngine` |
| Open-field generator | `ArtSource/EarlyIndustrialGenerator/build_generator.py` | `HD_EarlyIndustrialGenerator` |
| Enclosed electric motor | `ArtSource/EarlyIndustrialMotor/build_motor.py` | `HD_EarlyIndustrialLargeMotor` |

Run each script independently through Blender background mode, then `overview.py` to compose `overview_north.png`: small steam / large steam / diesel across the top and generator / motor below. Review tiles each have a 512px canvas; they are not a common world-unit scale across differently sized buildings. Each model's three directions do share one scale. The five existing game footprints and behaviors are retained. The generator continues to use its independent texture prefix. Original reference textures and masks are preserved in the respective source folders.

The large steam engine uses a genuine open floor slot: the complete flywheel extends below the concrete floor, with a dark pit bottom and walls. There is no solid foundation slab across the opening. See its README for dimensions.

Run `verify_assets.py` through Blender background mode to check five definition links, unchanged footprints, all 30 color/mask files and the per-asset render checks. It reopens all five scenes to verify orthographic cameras, 64° elevation and equal scale across directions, checks the flywheel geometry below the floor and clearance inside its slot, and verifies that the motor vent faces lie in the end-shield plane. Results are saved in `integration_validation.json`; this does not replace in-game visual testing.
