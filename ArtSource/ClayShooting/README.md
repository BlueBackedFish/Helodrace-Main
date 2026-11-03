# Clay shooting artwork

Procedurally authored Blender scenes provide a mechanical spring trap (2x1 tiles) and a separate orange clay pigeon. The trap shows the spring drum, throwing arm, launch plate, clay magazine, release pedal and linkage. The orange target has a stepped crown/rim and dark underside.

The current building rules were checked against the chat **Create Blender gunsmith workbench** (01a087fc-73bd-7910-a544-06681a3b2d4b), its attached original request, and the later approved fixed-density/color-theme documents in `ArtSource/IndustrialMachines`. Current 64-degree North/East/South/West views supersede the early prompt's three-view 26-degree draft. `ArtSource/AGENTS.md` explicitly limits vehicle rules to vehicles; this uses the building pipeline instead.

- One model unit per game tile; 512x512 RGBA, drawSize 3x3 for the 2x1 trap; 12px outline at the established constant density.
- Four actual orthographic cameras, not mirrored sprites; identical scale/ground anchor, transparent background, flat RGB Emission, no lighting, shadows, AO or texture noise.
- Existing Steel-green material masks and fixed orange/rubber colors. The new trap is ordinary equipment, not one of the advanced-white or computer-cyan families. No global palette/catalog changes were made.
- Separate editable `clay_pigeon.blend`, 64-degree item render, 256px RGBA and 12px cutout outline; the same target image is used by the inventory item and flying target.

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' -b -t 6 --python-exit-code 1 --python ArtSource/ClayShooting/build.py
python ArtSource/ClayShooting/package.py
python ArtSource/ClayShooting/verify.py
```

`HD_ClayTrap.blend`, `clay_pigeon.blend`, the source builders, validation records and four-view previews are retained. `clay_raw.png` is the reproducible intermediate item render. Renders and actual-size texture links were visually checked; a separate in-game screenshot review is not implied.
