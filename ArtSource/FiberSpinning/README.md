# Fiber spinning module and cloth tint mask

Original 1x1 industrial attachment: a rear feed coupling, pump/filter head and spinneret, three visible drawn fiber bands, quench trough, two godets, side take-up reel and analog dial. Queue jobs at the module while the adjacent stirred reactor supplies polymer processing. This is an invented game asset with the established workshop machinery palette.

`build.py` uses `IndustrialMachines/sprite_common.py` and the existing building theme. Module export follows `IndustrialMachines/PRESENTATION_CALIBRATION.md`: four actual 64-degree orthographic views, one model unit per game tile, 256px, drawSize 1.5, 12px outside outline, matching material-mask alpha. The camera's compensated ground anchor is recorded in `validation.json`; the current XML drawOffset is (0,0,0.1897817). Preserve the ground anchor when changing geometry and update the XML from `expected_draw_offset` after a new export.

Nylon and aramid reuse the game's packed `Things/Item/Resource/Cloth` stack textures. An explicit `_MaskTex` shader parameter applies `Items/SyntheticFibers/ClothTintMask`, a uniform red primary-color mask, through CutoutComplex. Ivory/gray and gold are set in each resource's graphicData color and matching stuffProps color. The original cloth pixels, folds and outline come directly from Core. No base-game texture is copied or overridden globally. The superseded generated roll PNGs/scenes and their build branch have been removed.

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' -b --threads 2 --python-exit-code 1 --python ArtSource/FiberSpinning/build.py
python ArtSource/FiberSpinning/make_cloth_mask.py
python ArtSource/FiberSpinning/verify.py
```

`preview_four_views.png` shows the themed views. `preview_game_scale.png` uses 64 display pixels per game tile (a 1.5-tile sprite canvas is 96px) and is not crop-fitted. These are render previews; gameplay host checks resolve the graphics but do not substitute for an in-game appearance screenshot.
