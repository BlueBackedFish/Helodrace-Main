# Petroleum machinery artwork

Four individually authored Blender assets use the established neutral-metal / vanilla-timber palette, fixed Emission colors, transparent 512px North/East/South color-and-mask pairs, 64° downward orthographic projection and 12px black outlines. There are no lighting, AO, PBR or texture-noise effects. One model unit is one tile when drawSize matches the saved ortho_scale and drawOffset is zero. All source models are independent; no image planes or camera-specific geometry swaps are used.

| Asset | Source folder | Footprint | drawSize | Integration |
|---|---|---|---|---|
| Timber basic pumpjack | `ArtSource/BasicPumpJack` | 6×3 | 9 | Existing texture/graphics updated |
| Steel pumpjack | `ArtSource/MetalPumpJack` | proposed 6×3 | 9 | New artwork only |
| Fractionation tower | `ArtSource/DistillationTower` | 3×3 | 6 | Existing texture/graphics updated |
| Rotary drilling rig | `ArtSource/RotaryDrill` | proposed 4×4 | 6.75 | New artwork only |

Existing building size, recipes, research, production, power/fuel use and costs are retained. The basic pumpjack retains its Woody cost category; baked vanilla wood and untinted iron intentionally fix visible colors rather than recoloring iron with wood stuff. New metal-pumpjack and rotary-drill gameplay definitions do not exist, so those files supply proposed placement/presentation metadata in `asset_catalog.json` without invented balancing values. The existing cable-tool rig is not replaced.

Build each asset separately using the script in its source folder, then run `verify_petroleum.py` with Blender. It checks four saved scenes, 12 cameras, 24 PNG files, mask alpha/black strokes, fixed timber materials, existing definition links and the rotary-table floor opening. `validation.json` records the results. Source READMEs identify historical references and distinguish period guidance from exact reconstruction. Original basic-pumpjack/tower sprites and masks are preserved and packed in their scenes.

Run `review_petroleum.py` with Python/Pillow. `overview_north.png` fits each silhouette for shape/color review; it is not scale evidence. `tile_scale_comparison.png` uses **64px per game tile** with no bounding-box normalization. `north_east_south.png` shows all 12 views. Gray backgrounds and grids exist only in review sheets. No in-game verification is claimed.
