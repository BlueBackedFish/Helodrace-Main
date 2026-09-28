# Industrial plate rolling mill

New four-roll machine in the same neutral-gray sprite family as the industrial
press. Two large working rolls and two smaller entry/exit guide rolls define the
machine. A single broad steel sheet, partly fed through the working rolls,
establishes the -Y to +Y feed path and leaves the exit guide exposed.

The low cast side frames, upper side rails/rear tie, heavy foundation, feed lips
and one enclosed drive housing provide industrial mass without exposed gears,
bolts, pipes, surface textures or decorative paneling. Roller ends project past
the side castings so their circular profiles remain visible from East.

- `industrial_rolling_mill.blend`: complete editable scene.
- `industrial_rolling_mill_north.png`, `industrial_rolling_mill_east.png`,
  `industrial_rolling_mill_south.png`: 512×512 transparent RGBA renders.
- `preview_north_east_south.png`: three views together, left to right N/E/S.
- All cameras: orthographic, 64° downward, scale 4.65, target (0, 0, 1).
- Camera positions: North +Y, East +X, South -Y.
- Lighting-independent emission materials with broad cylinder/face shading.

From this directory, rebuild with Blender 5.1.2:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' --background --python '.\build_rolling_mill.py'
```

The script reopens the saved scene and verifies actual camera angles, common
scale/target, PNG size, transparency and margins. Results and mesh counts are
saved in `manifest.json`. Existing press and reconstruction assets are unchanged.
