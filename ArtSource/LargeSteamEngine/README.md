# Period large steam engine

Original c.1900–1920 horizontal tandem-compound design, 4×3. Two cylinders of differing bores drive a crosshead, low crankshaft and large ten-spoke flywheel. Brass governor, steam header, copper drain and compact cast bed provide the major details. The source reference sprite is preserved in `Reference/` for provenance; this is now a full redesign under the user's period brief.

The flywheel has radius 1.04 and axle z=0.70. The concrete floor is z=0.22; the wheel bottom is z=-0.34, so 0.56 of its lower portion enters the ground. Five separate shaped floor blocks leave an actual rectangular slot, with below-floor walls and a dark bottom at z=-0.49. No opaque foundation slab cuts across the opening. The complete wheel exists below the floor and is occluded naturally by the pit/foundation geometry. Pit lips and bearing plinths make the installation readable from all three views.

Density/style correction: the former rectangular floor apron is trimmed into a narrow pit surround and a bearing/cylinder tongue, reducing actual top area from 9.816 to 5.343 square scene units (45.6%). Exposed guide rails, sliding crosshead insert, connected condensate/feed pump, governor belt drive, rear power pulley, valve-chest service lids and steam-pressure dial add functional large-scale detail. Neutral gray metal, distinct dark seams and a 12px black exported silhouette replace the low-contrast green paint.

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' -b --threads 2 --python-exit-code 1 --python ArtSource/LargeSteamEngine/build_engine.py
```

Scene: `HD_LargeSteamEngine.blend`. Existing `Buildings/HD_LargeSteamEngine` texture prefix, 4×3 footprint and mechanics remain unchanged. North/East/South PNGs and masks use 64° and a common scale; copper/brass, concrete and pit interior are unselected in the material masks. See `validation.json`, review sheets and `../IndustrialMachines/PERIOD_DESIGN.md`. No in-game verification is claimed.

Tile calibration, 2026-10-26: orthographic scale and game drawSize are both (6,6); the ground target and drawOffset are zero. One model unit is one game tile, following the radio 1.5 / 2×1 machine 3 presentation rule. The saved model was reframed without changing geometry or painted materials; the hash check is recorded in validation.json.
