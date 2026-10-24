# Small steam engine — first engine rework

Reference: the existing `Textures/Buildings/HD_SmallSteamEngine_north.png` and its `northm.png` material mask, preserved in `Reference/` and packed into the scene as inspection data. Together with the user's structural clarification, the reference identifies two large spoked flywheels on a common shaft at the left, a flared stack, a boiler running left/right beneath the axle, a rectangular combustion chamber at the right, a brass paired-ball governor and an exposed connecting link. These large features are retained in newly authored 3D geometry. The source image is not mapped onto a billboard.

Corrected structural interpretation: the right-hand block is the combustion chamber (width 0.64, depth 1.20), not a front-loading engine cylinder. Its North (-Y) wall is flat and has no door or piston gland. The fuel door, six rivets, hinges and handle are attached to the right (+X / East) wall. The boiler is a continuous X-axis cylinder (diameter 0.42, length 1.00), extending left from the chamber to the chimney, with a flange at the chamber and an end cap at the chimney end. Its shell sits 0.10 above the bed (bands have 0.085 clearance); a dedicated saddle and two short legs support its free end. The rear shaft bearing support is offset clear of the boiler. Four plinth and six end-cap rivets are retained. Fine auxiliary plumbing and dense micro-fasteners remain omitted.

The definition remains `HD_SmallSteamEngine`, 2×2 footprint, existing 2.55 draw size, non-rotatable `Graphic_Multi` with `CutoutComplex`. No gameplay definition is changed. North is the active in-game sprite; East and South are supplied for review and future use. North views from -Y, East from +X, South from +Y, following the existing reconstruction pipeline. All cameras use 64° downward orthographic projection and the same scale of 2.75.

Outputs are transparent 512×512 RGBA PNGs at `Textures/Buildings/HD_SmallSteamEngine_{north,east,south}.png`, with matching `{direction}m.png` masks. Neutral iron surfaces are red-selected in the masks; brass governor/valve and brown crank parts remain unselected black. Color and mask alpha are verified to match. Fixed sRGB colors are converted to linear Emission values; there are no lights, AO, cast shadows or specular highlights. Large iron faces use modest tonal separation and the small cylinder barrels use one continuous color with separately colored end faces.

Editable scene: `small_steam_engine.blend`. Build/render/verify:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' -b --threads 2 --python-exit-code 1 --python ArtSource/SmallSteamEngine/build_engine.py
```

The script checks 64° camera elevations, frame margins and mask alpha alignment. `preview_north_east_south.png` and `preview_256px.png` are visual review sheets, ordered North / East / South. Results are in `validation.json`. In-game testing has not been performed.

This first pass covers the small steam engine only. Large steam engine, industrial diesel engine and large motor remain separate subsequent reworks; their textures are unchanged.
