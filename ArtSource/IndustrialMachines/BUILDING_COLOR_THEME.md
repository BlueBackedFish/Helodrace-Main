# Building color theme

The 29 industrial models share one material-role theme. Geometry, footprint, 64° cameras, tile density, 12px outline and gameplay values stay unchanged. Radio and tailoring/gun furniture retain their existing wood/fabric palettes and mask rules.

| Family | Main body | Members |
|---|---|---|
| Advanced | white paint | vacuum induction furnace, semiconductor fab, 2000s mill-turn machine |
| Industrial | vanilla Steel gray | all other industrial models in `normalize_density.ASSETS` |
| Computer controls | small dark cyan areas | NC lathe/mill, 1970s machining center, 2000s mill-turn machine, fab and 1970s rolling mill |

The secondary tint is **(105,105,105)**, read from the installed base game's Steel `stuffProps/color`. Large gray metal areas use the **green** CutoutComplex mask channel and `graphicData.colorTwo`. Construction costs and stuff categories are unchanged; using the secondary channel keeps wood/stone construction material from tinting metal brown or beige. Advanced white bodies use the **red** primary channel with `graphicData.color=(255,255,255)`. These three advanced buildings do not have construction-stuff recoloring.

Black mask areas retain fixed colors: timber, concrete/stone, rubber/recesses, working silver, glass proxies, small copper/brass fittings and cyan accents. The outline is black and unselected. In particular, timber pumpjack/cable-rig wood stays brown while its ironwork uses Steel. The batch-still kettle/head are now metal gray; its small copper piping remains distinct. Blast-furnace and converter upper shells, charging parts and mouths use dark metal instead of white highlights.

| Material | Display sRGB, top / front / side |
|---|---|
| Cast body after Steel tint | 105 / 96 / 90 neutral gray |
| Underframe after Steel tint | 86 / 78 / 73 neutral gray |
| Hot metal after Steel tint | 102 / 93 / 86 neutral gray |
| White body | (239,239,237) / (228,228,225) / (216,216,213) |
| Cyan accent | (57,102,110) / (48,89,97) / (42,79,87) |
| Working silver | (193,193,190) / (180,180,177) / (166,166,163) |

`building_theme.py` assigns per-object roles, preserving each source material name/RGB in custom metadata. Saved Blender materials show the applied colors. **Native game PNGs contain untinted albedo**, often much lighter than the final metal; the game applies the mask once. Do not darken those PNGs and then apply Steel a second time. Mask antialias overlaps are normalized so red + green never exceeds one, within 8-bit rounding.

`theme_preview.game_image()` and individual `preview_*.png` files show the white/Steel tint applied. `review_theme.py` produces the overview, all-29 contact sheet and furnace before/after comparison. Historical reference PNGs remain untouched. Preview tinting is a material-mask approximation, not an in-game screenshot.

Publish saved scenes in Blender:

```powershell
blender -b --python-exit-code 1 --python ArtSource/IndustrialMachines/normalize_density.py
blender -b --python-exit-code 1 --python ArtSource/IndustrialMachines/verify_theme.py
blender -b --python-exit-code 1 --python ArtSource/IndustrialMachines/verify_rotation.py
```

`verify_theme.py` checks all 29 saved scenes and 116 color/mask pairs, original pre-theme palette hashes, unchanged shape/cameras/scale/gameplay, mask roles, visible cyan and subdued furnace uppers. `theme_validation.json` records the results. No in-game visual test has been performed.
