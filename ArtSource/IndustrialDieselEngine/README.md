# Industrial diesel engine

Independent 4×3 rework of `HD_IndustrialDieselEngine`. The original texture/mask are retained in `Reference/` and packed into the scene. The long horizontal engine body, prominent left-front flywheel, rear exhaust stack with connected overhead manifold, top governor, inspection plate and right-hand auxiliary pump follow the existing reference family. Large structural details and rivets replace the original fine surface clutter.

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' -b --threads 2 --python-exit-code 1 --python ArtSource/IndustrialDieselEngine/build_engine.py
```

Scene: `HD_IndustrialDieselEngine.blend`. Three transparent 512px directions and corresponding material masks are written to the existing texture prefix. North / East / South use the same framing at 64°. Neutral iron remains stuff-tintable; brass stays unselected. See `validation.json` and the 512px/256px review sheets. The building definition, footprint and mechanics are unchanged. Render checks are complete when the script succeeds; in-game testing is not performed.
