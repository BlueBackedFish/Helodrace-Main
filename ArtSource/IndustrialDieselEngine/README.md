# Period stationary diesel engine

Original c.1920–1940 three-cylinder oil-engine design on a 4×3 skid. Three separate upright water jackets and rocker covers sit on a common crankcase with large inspection lids. An end flywheel, belt pulley, overhead exhaust manifold and silencer, cooling-water tank, pump rack and three broad injection lines establish the period machine family. No radiator grille, modern acoustic enclosure or generic generator housing. Original sprites remain in `Reference/`; see `../IndustrialMachines/PERIOD_DESIGN.md` for the redesign brief and primary historical references.

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' -b --threads 2 --python-exit-code 1 --python ArtSource/IndustrialDieselEngine/build_engine.py
```

Scene: `HD_IndustrialDieselEngine.blend`. The existing texture prefix, 4×3 footprint and mechanics are retained. North/East/South color/mask pairs use 512px transparency, 64° downward orthographic cameras and one common scale. Brass/copper/insulators remain fixed-color; cast body and metalwork are selected in material masks. `validation.json` records saved-scene, framing and mask-alpha checks; 512px and 2512px review sheets are generated. RimWorld testing is not included.

Density/style correction: skid top area is reduced by 25.8%, from 3.85×2.50 to 3.80×1.88. Neutral gray metal and a 12px black export silhouette match neighboring workshop machinery. Dark cylinder-neck flanges, bordered rocker-service lids, oil cups, a connected lubricator reservoir, stiffened side rails, transverse anchor ribs and a cooling-tank inspection lid break up broad surfaces with readable functional parts.

Tile calibration, 2026-10-26: orthographic scale and game drawSize are both (6,6); the ground target and drawOffset are zero. One model unit is one game tile, following the radio 1.5 / 2×1 machine 3 presentation rule. The saved model was reframed without changing geometry or painted materials; the hash check is recorded in validation.json.

Four-direction export (2026-10-26): North / East / South / West are actual 64-degree orthographic renders of the same saved geometry and colors; West is not mirrored East. See preview_four_views.png and validation.json.
