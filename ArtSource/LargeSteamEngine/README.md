# Large steam engine

4×3 reference-led reconstruction of `HD_LargeSteamEngine`. Original north image and material mask are preserved in `Reference/` and packed into the Blender scene. Main retained traits: one large rear spoked flywheel, left/right working cylinders, exposed central crossheads/crank links, brass governor, right-hand overhead steam arch and copper drains. The cylinders, shaft and flywheel are actual 3D geometry, not image planes. This is a simplified game asset rather than an engineering model.

Build individually from the repository root:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' -b --threads 2 --python-exit-code 1 --python ArtSource/LargeSteamEngine/build_engine.py
```

Scene: `HD_LargeSteamEngine.blend`. Outputs: `Textures/Buildings/HD_LargeSteamEngine_{north,east,south}.png` and corresponding `{direction}m.png`. The existing building definition and 4×3 footprint remain unchanged. North is active because the building is non-rotatable; other views are supplied for review. See `validation.json` and the two preview sheets. In-game verification has not been performed.
