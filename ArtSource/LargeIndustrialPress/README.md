# Large industrial press — new variant

A new single-bay industrial press inspired by the gray, heavy-frame visual
language of `ArtSource/Reference/LineShaftPress`. No reconstruction geometry was
reused and no pixel layout was traced.

The new design uses a broad foundation, two rear-set side columns, an upper
crosshead, a bright central ram, moving head and lower die/platen. The working
assembly projects forward of the columns so its profile remains visible from
East. There are no bolts, pipes, cables, outline strips or texture details.

- Scene: `large_industrial_press.blend`
- Renders: `large_industrial_press_north.png`, `large_industrial_press_east.png`,
  `large_industrial_press_south.png`
- All three: 512×512 transparent RGBA, orthographic, 64° downward, scale 4.9,
  shared target (0, 0, 1.78).
- Camera positions: North +Y, East +X, South -Y.
- Flat gray emission materials with broad face shading; no lights or textures.
- Base footprint: 2.85 × 1.95 Blender units; overall height 3.64 units.

Rebuild and validate using Blender 5.1.2 from this directory:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' --background --python '.\build_press.py'
```

`manifest.json` records mesh counts, actual saved-camera angles and PNG margin/
transparency checks. Existing reference and reconstruction assets are unchanged.
