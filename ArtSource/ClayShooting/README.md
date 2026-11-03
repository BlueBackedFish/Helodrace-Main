# Clay shooting artwork

Procedurally authored Blender scenes provide a mechanical spring trap (1x1 tile) and a separate orange clay pigeon. The trap shows the spring drum, throwing arm, launch plate, clay magazine, release pedal and linkage. The orange target has a stepped crown/rim and dark underside.

The current building rules were checked against the chat **Create Blender gunsmith workbench** (01a087fc-73bd-7910-a544-06681a3b2d4b), its attached original request, and the later approved fixed-density/color-theme documents in `ArtSource/IndustrialMachines`. Current 64-degree North/East/South/West views supersede the early prompt's three-view 26-degree draft. `ArtSource/AGENTS.md` explicitly limits vehicle rules to vehicles; this uses the building pipeline instead.

- One model unit per game tile; 256x256 RGBA, drawSize 1.5x1.5 for the 1x1 trap; 12px outline at the established constant density.
- Four actual orthographic cameras, not mirrored sprites; identical scale/ground anchor, transparent background, flat RGB Emission, no lighting, shadows, AO or texture noise.
- Existing Steel-green material masks and fixed orange/rubber colors. The new trap is ordinary equipment, not one of the advanced-white or computer-cyan families. No global palette/catalog changes were made.
- Separate editable `clay_pigeon.blend`, 64-degree item render, 256px RGBA and 12px cutout outline; the same target image is used by the flying target.

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' -b -t 6 --python-exit-code 1 --python ArtSource/ClayShooting/build.py
python ArtSource/ClayShooting/package.py
python ArtSource/ClayShooting/verify.py
```

`HD_ClayTrap.blend`, `clay_pigeon.blend`, the source builders, validation records and four-view previews are retained. `clay_raw.png` is the reproducible intermediate item render. Renders and actual-size texture links were visually checked; a separate in-game screenshot review is not implied.

2026-11-04: the existing trap mesh locations and scales are uniformly halved; shape, parts and material assignments are preserved. The calibrated exporter re-renders at 256px and reapplies the original 12px exterior outline rather than shrinking an outlined raster. The game footprint is 1x1 and the launcher supplies its own targets; the obsolete consumable item/recipe/refueling definitions are removed. The separate clay-disc scene and flying-target texture remain.
