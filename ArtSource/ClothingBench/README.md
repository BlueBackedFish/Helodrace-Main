# Clothing / tailoring workbench

New 3×1 sprite-proxy workbench with three broad zones: sewing on the left,
cutting in the center, and fabric/supply storage on the right. The open-arm
sewing machine has a large handwheel; the cutting zone has a generic partially
cut fabric blank and large scissors. One fabric roll, two folded pieces and
three drawers communicate general clothing production without specializing in
one garment type.

## Deliverables

- `clothing_bench.blend`: complete editable scene, 16 logical meshes / 400 vertices.
- `clothing_bench_north.png`, `clothing_bench_south.png`, `clothing_bench_east.png`.
- `preview_north_south_east.png`: all three views, left to right N/S/E.
- `build_clothing_bench.py`: procedural source and validation.
- `revise_clothing_bench.py`: edits the existing scene in place; safe to rerun.
- `correct_pegboard.py`: focused rear-pegboard correction, preserving other objects.
- `manifest.json`: saved-camera and exported-image checks.

## Rendering

All views use **64° downward orthographic projection**, as explicitly requested
for this asset. They share scale 3.65, target (0, 0, 0.79), and 512×512 RGBA
output. Camera positions: North +Y, South -Y, East +X. East is naturally narrow;
it is not enlarged independently. Transparent background, no ground or lights.

Neutral gray and dark cyan emission materials use very small face-value differences. Fabric
uses only muted canvas and sage colors. No textures, needles, threads, tiny
hardware, seams, folds or realistic surface detail are modeled. The flat cloth
and scissor meshes are intentional sprite-proxy surfaces.

## Rebuild

Update the existing scene from this folder with Blender 5.1.2:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' --background --python '.\correct_pegboard.py'
```

The script reopens its saved scene and checks actual camera orientation,
orthographic scale/target consistency, PNG resolution, transparent pixels,
minimum margins, and the component/vertex budget. All three final renders
were visually inspected. Existing industrial machine assets are unchanged.

## Sprite revision

The existing geometry and layout were edited, not replaced. The sewing machine
now has an explicitly split light-gray upper housing and a uniform darker-gray
lower body; the color difference is emission material assignment, not lighting.
Its height and the fabric props' thickness were reduced. Scissor blades and
handles share one flat material. Cyan remains on the storage drawers.

The incorrectly placed front board has been removed. A thin rear pegboard now
stands at Y=+0.462, entirely above the tabletop (board Z=1.32–1.82; tabletop
Z=0.95). Two simple mounts connect it to the rear edge. Twelve square through-
holes in a 6×2 grid communicate the workshop-board identity without texture or
small hooks. The existing canvas was moved onto the board, covering four holes.
North and South show its broad faces; East shows its natural thin edge above
and behind the work surface. No camera-specific geometry or visibility is used.

The added through-hole topology accounts for the increased vertex count.
`pegboard_correction_validation.json` records unchanged geometry, materials and
transforms for all unrelated objects, including all three cameras. The cabinet,
sewing machine, tabletop props and prior simplification changes were preserved.

Two thin, clipped-corner armor inserts occupy a small area of the cutting mat;
the scissors were repositioned to avoid covering them. They remain subordinate
to the sewing and fabric zones, unchanged by the pegboard correction.

The latest camera-only update uses 64 degrees. The original scale, target and
all model geometry are retained.
`build_clothing_bench.py` also applies the revision after a full procedural build.
