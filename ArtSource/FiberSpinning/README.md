# Fiber spinning module and fabric rolls

Original 1x1 industrial attachment: a rear feed coupling, pump/filter head and spinneret, three visible drawn fiber bands, quench trough, two godets, side take-up reel and analog dial. Queue jobs at the module while the adjacent stirred reactor supplies polymer processing. This is an invented game asset with the established workshop machinery palette.

`build.py` uses `IndustrialMachines/sprite_common.py` and the existing building theme. Module export follows `IndustrialMachines/PRESENTATION_CALIBRATION.md`: four actual 64-degree orthographic views, one model unit per game tile, 256px, drawSize 1.5, 12px outside outline, matching material-mask alpha. The camera's compensated ground anchor is recorded in `validation.json`; the current XML drawOffset is (0,0,0.1897817). Preserve the ground anchor when changing geometry and update the XML from `expected_draw_offset` after a new export.

Nylon and aramid each have an original Blender roll scene and two 256px fixed-color inventory stack images. Single and triple rolls share a camera and 8px icon outline. Ivory and gold distinguish the fibers. No downloaded image or third-party model is used.

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' -b --threads 2 --python-exit-code 1 --python ArtSource/FiberSpinning/build.py
python ArtSource/FiberSpinning/verify.py
```

`preview_four_views.png` shows the themed views. `preview_game_scale.png` uses 64 display pixels per game tile (a 1.5-tile sprite canvas is 96px) and is not crop-fitted. These are render previews; gameplay host checks resolve the graphics but do not substitute for an in-game appearance screenshot.
