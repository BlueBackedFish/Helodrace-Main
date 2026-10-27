# Petroleum machinery

Four individually authored Blender machines use neutral metal or vanilla timber, fixed Emission colors, transparent North/East/South/West color/mask pairs, 64-degree downward orthographic projection and 12px exterior outlines. No lighting, AO, PBR, texture noise or camera-specific mesh swaps are used. One model unit is one game tile. Native canvas size is 256 x longest footprint side; drawSize is 1.5 x that side, maintaining the same pixel density and game-space stroke width.

| Asset | Footprint | Native canvas | drawSize | Integration |
|---|---|---|---|---|
| Timber basic pumpjack | 6x3 | 1536px | 9 | Existing mechanical pump |
| Steel pumpjack | 6x3 | 1536px | 9 | New 1800W electric pump |
| Fractionation tower | 3x3 | 768px | 4.5 | Existing refining worktable |
| Rotary drilling rig | 4x4 | 1024px | 6 | New diesel drilling rig |

The basic pump/tower retain existing gameplay. The steel pumpjack and rotary rig are active buildings defined in `AdvancedOilExtraction.xml`, with oil-system support and Korean localization. Their source READMEs document operating values and period guidance. The older cable-tool rig remains a separate machine in `ArtSource/CableToolRig`.

Build each machine with its own script, then publish with `ArtSource/IndustrialMachines/normalize_density.py`. `verify_petroleum.py` checks four saved scenes, 16 cameras, 32 PNGs, masks, four active definition links and the rotary-table floor opening. `validation.json` and `asset_catalog.json` record results and compensated draw offsets. Basic-pumpjack/tower original sprites are preserved and packed in their scenes.

`review_petroleum.py` generates shape and common-game-scale comparison sheets. Review backgrounds/grids are absent from game textures. Oil-system regression tests are in `Source/Tests/OilMachinery`; these do not replace an in-game visual/play test, which has not been performed.

Current color roles and mask channels follow [the shared building theme](../IndustrialMachines/BUILDING_COLOR_THEME.md). Review sheets apply the white/Steel mask; native PNGs remain untinted albedo.
