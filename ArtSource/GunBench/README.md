# Gun crafting bench sprite source

`gun_bench.blend` contains the complete 3 x 1 bench and three orthographic cameras.
The PNGs are 512 x 512 RGBA with transparent backgrounds. All cameras share a
3.65 orthographic scale, target (0, 0, 0.85), and 64-degree downward elevation.
North denotes a camera at +Y, South at -Y, and East at +X; the rear shelf is +Y.
The East image is naturally narrower because it shows the end of the bench;
it is not independently enlarged or cropped.

The model has 11 logical mesh components and 208 vertices. Face-assigned gray
and muted dark teal emission materials provide fixed broad shading without lights,
textures, bevels, a floor, or surface detail.

Revise the existing scene and export all deliverables with Blender 5.1.2:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' --background --python '.\revise_gun_bench.py'
```

Outputs are written alongside the script. Dimensions use one Blender unit per
tile. This is stylized game prop geometry, with no functional weapon internals.

The reference-alignment pass edits the existing meshes without adding topology.
It lowers the shelf by 0.38 units, shortens its original uprights, rotates the
existing rifle 18 degrees across the vise, and adjusts the emission palette.
The original hammer is turned and moved toward the front to keep its head
visible past the shelf in the North view.
The six images in `../Reference/GunBench` informed the muted gray body, warm
gray tool/stock color, dark separation, and restrained teal accents. The script
converts display sRGB colors to linear emission values to avoid washed-out grays.
It is safe to rerun: geometry adjustments are guarded by a scene revision flag.

`build_gun_bench.py` remains the original procedural construction source and now
applies the revision script automatically at the end for reproducibility.
