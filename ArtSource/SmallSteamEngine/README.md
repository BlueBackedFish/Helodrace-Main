# Small steam engine — first engine rework

Reference: the existing `Textures/Buildings/HD_SmallSteamEngine_north.png` and its `northm.png` material mask, preserved in `Reference/` and packed into the scene as inspection data. The reference identifies two large spoked flywheels on a common shaft at the left, a tall flared stack behind them, the engine casing/steam chest at the right, a brass paired-ball governor and an exposed connecting link. These large features are retained in newly authored 3D geometry. Rivets and thin auxiliary plumbing are omitted. The source image is not mapped onto a billboard.

The definition remains `HD_SmallSteamEngine`, 2×2 footprint, existing 2.55 draw size, non-rotatable `Graphic_Multi` with `CutoutComplex`. No gameplay definition is changed. North is the active in-game sprite; East and South are supplied for review and future use. North views from -Y, East from +X, South from +Y, following the existing reconstruction pipeline. All cameras use 64° downward orthographic projection and the same scale of 2.75.

Outputs are transparent 512×512 RGBA PNGs at `Textures/Buildings/HD_SmallSteamEngine_{north,east,south}.png`, with matching `{direction}m.png` masks. Neutral iron surfaces are red-selected in the masks; brass governor/valve and brown crank parts remain unselected black. Color and mask alpha are verified to match. Fixed sRGB colors are converted to linear Emission values; there are no lights, AO, cast shadows or specular highlights. Large iron faces use modest tonal separation and the small cylinder barrels use one continuous color with separately colored end faces.

Editable scene: `small_steam_engine.blend`. Build/render/verify:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' -b --threads 2 --python-exit-code 1 --python ArtSource/SmallSteamEngine/build_engine.py
```

The script checks 64° camera elevations, frame margins and mask alpha alignment. `preview_north_east_south.png` and `preview_256px.png` are visual review sheets, ordered North / East / South. Results are in `validation.json`. In-game testing has not been performed.

This first pass covers the small steam engine only. Large steam engine, industrial diesel engine and large motor remain separate subsequent reworks; their textures are unchanged.
