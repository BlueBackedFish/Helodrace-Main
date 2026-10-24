# Period large steam engine

Original c.1900–1920 horizontal tandem-compound design, 4×3. Two cylinders of differing bores drive a crosshead, low crankshaft and large ten-spoke flywheel. Brass governor, steam header, copper drain and compact cast bed provide the major details. The source reference sprite is preserved in `Reference/` for provenance; this is now a full redesign under the user's period brief.

The flywheel has radius 1.04 and axle z=0.70. The concrete floor is z=0.22; the wheel bottom is z=-0.34, so 0.56 of its lower portion enters the ground. Four separate floor blocks leave an actual rectangular slot, with below-floor walls and a dark bottom at z=-0.49. No opaque foundation slab cuts across the opening. The complete wheel exists below the floor and is occluded naturally by the pit/foundation geometry. Pit lips and bearing plinths make the installation readable from all three views.

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' -b --threads 2 --python-exit-code 1 --python ArtSource/LargeSteamEngine/build_engine.py
```

Scene: `HD_LargeSteamEngine.blend`. Existing `Buildings/HD_LargeSteamEngine` texture prefix, 4×3 footprint and mechanics remain unchanged. North/East/South PNGs and masks use 64° and a common scale; copper/brass, concrete and pit interior are unselected in the material masks. See `validation.json`, review sheets and `../IndustrialMachines/PERIOD_DESIGN.md`. No in-game verification is claimed.
