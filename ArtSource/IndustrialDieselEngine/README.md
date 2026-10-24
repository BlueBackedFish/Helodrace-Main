# Period stationary diesel engine

Original c.1920–1940 three-cylinder oil-engine design on a 4×3 skid. Three separate upright water jackets and rocker covers sit on a common crankcase with large inspection lids. An end flywheel, belt pulley, overhead exhaust manifold and silencer, cooling-water tank, pump rack and three broad injection lines establish the period machine family. No radiator grille, modern acoustic enclosure or generic generator housing. Original sprites remain in `Reference/`; see `../IndustrialMachines/PERIOD_DESIGN.md` for the redesign brief and primary historical references.

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' -b --threads 2 --python-exit-code 1 --python ArtSource/IndustrialDieselEngine/build_engine.py
```

Scene: `HD_IndustrialDieselEngine.blend`. The existing texture prefix, 4×3 footprint and mechanics are retained. North/East/South color/mask pairs use 512px transparency, 64° downward orthographic cameras and one common scale. Brass/copper/insulators remain fixed-color; cast body and metalwork are selected in material masks. `validation.json` records saved-scene, framing and mask-alpha checks; 512px and 256px review sheets are generated. RimWorld testing is not included.
