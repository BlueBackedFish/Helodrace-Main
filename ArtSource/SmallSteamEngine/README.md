# Period workshop steam engine

Original c.1900–1920 compact plant on a 2×2 cast skid: riveted horizontal boiler, side-loading firebox, short chimney, steam dome and safety valve, one six-spoke flywheel, front working cylinder and brass flyball governor. The front crankshaft is separate from the boiler barrel. Original sprites remain in `Reference/`; this pass follows the full period redesign brief in `../IndustrialMachines/PERIOD_DESIGN.md`.

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' -b --threads 2 --python-exit-code 1 --python ArtSource/SmallSteamEngine/build_engine.py
```

Scene: `small_steam_engine.blend`. Existing `Buildings/HD_SmallSteamEngine` texture prefix, footprint and mechanics are retained. The build exports North/East/South 512px transparent images and material masks at 64° downward orthographic elevation with one common scale, reopens the scene, checks framing and mask alpha, and writes 512px/2512px review sheets and `validation.json`. Neutral gray metal now matches neighboring workshop sprites; exports include a 12px black silhouette with an unselected mask stroke. No lighting or texture noise. Game appearance has not been tested in RimWorld.

Density correction: the skid is shortened from 1.90×1.85 to 1.90×1.66, a 10.3% top-area reduction. A driven feed-water pump and connected delivery pipe occupy its front service edge. Boiler strap, pressure dial, bordered firebox service hatch with hinges/latch and a dark guide insert break up large blank surfaces while retaining the period layout.

Tile calibration, 2026-10-26: orthographic scale and game drawSize are both (3.25,3.25); the ground target and drawOffset are zero. One model unit is one game tile, following the radio 1.5 / 2×1 machine 3 presentation rule. The saved model was reframed without changing geometry or painted materials; the hash check is recorded in validation.json.

Four-direction export (2026-10-26): North / East / South / West are actual 64-degree orthographic renders of the same saved geometry and colors; West is not mirrored East. See preview_four_views.png and validation.json.
