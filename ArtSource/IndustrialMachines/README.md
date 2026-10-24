# Industrial building sprite tools

Each building has an independent source folder and build command. `sprite_common.py` supplies fixed Emission palettes, simple solid primitives, open spoked wheels, governors and per-asset export. It does not build multiple buildings in one invocation.

North views from -Y, East from +X, South from +Y, at 64° downward orthographic elevation. One common scale per model is calculated from all three views; transparent 512×512 PNGs and corresponding red-channel material masks are exported to `Textures/Buildings`. Brass, copper and dark electrical insulators are unselected. No lights, shadows, AO or specular highlights are used. Saved scenes are reopened and camera elevation, framing, dimensions and mask-alpha alignment are checked. Review sheets are ordered North / East / South at 512px and 256px per direction. In-game testing is not included.

Buildings share a neutral metal family with the existing small steam engine. Broad surfaces use modest face separation; small cylinder barrels use a single color with separate end caps. Rivets and mechanical features are deliberately enlarged for sprite readability. The electrical generator and motor are independent designs with independent texture prefixes and scenes.
