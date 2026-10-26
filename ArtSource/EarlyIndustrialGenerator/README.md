# Period open-field DC generator

Original c.1900–1925 belt-driven dynamo on a 3×3 cast skid. Two arched magnetic yokes join upright iron poles wrapped in broad copper field coils. The open central armature, copper commutator, two carbon brushes, insulated terminal bridge and separate bearing pedestals distinguish it from the enclosed electric motor. Shapes are enlarged and simplified for sprites; this is not a precise electrical engineering reconstruction. Historical guidance and the five-machine brief are in `../IndustrialMachines/PERIOD_DESIGN.md`. Original sprite references remain packed and in `Reference/`.

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' -b --threads 2 --python-exit-code 1 --python ArtSource/EarlyIndustrialGenerator/build_generator.py
```

Scene: `HD_EarlyIndustrialGenerator.blend`; independent `Buildings/HD_EarlyIndustrialGenerator` texture prefix. Footprint and behavior are retained. North/East/South 512px color/mask pairs use a common orthographic scale and 64° downward elevation. Copper, brass and insulation are fixed-color. The build reopens the saved scene and checks camera elevation, safe margins and mask alpha before writing review sheets and `validation.json`. No in-game test is claimed.

Density/style correction: the broad rectangular slab is replaced by a 1.32×1.84 central casting and two 0.77×0.88 bearing wings, reducing top area by 39.3%. Neutral gray iron and the exported 12px black silhouette match the workshop family. Three broad armature conductors, coil binding strips, pole-foot gussets, a brush-carrier crossbar/spring blocks and split-bearing caps give the open machine a visible mechanical structure at sprite scale.

Tile calibration, 2026-10-26: orthographic scale and game drawSize are both (4.5,4.5); the ground target and drawOffset are zero. One model unit is one game tile, following the radio 1.5 / 2×1 machine 3 presentation rule. The saved model was reframed without changing geometry or painted materials; the hash check is recorded in validation.json.

Four-direction export (2026-10-26): North / East / South / West are actual 64-degree orthographic renders of the same saved geometry and colors; West is not mirrored East. See preview_four_views.png and validation.json.
