# F-22 Raptor — flat top-view texture

Created from the FBX supplied in `.texture/ref/lockheed-martin-f-22-raptor`. The aircraft points up in a true 90-degree orthographic view. This follows the flat aircraft presentation used by Main air support and Arsenal aircraft, with one black outside silhouette and muted internal control-surface seams.

- Game texture: `Textures/Effects/Aircraft/F22/HD_F22.png` in the repository root. **1024×1024 transparent RGBA, 12px outside outline, 2px internal seams**.
- Matching output: `output/HD_F22.png`.
- Editable model: `output/f22_raptor.blend`, with its four-color UV paint image packed inside.
- Plain/outlined exports: `output/HD_F22_plain.png`, `output/HD_F22_outlined.png`.
- UV paint map: `output/fuselage_flat_uv.png`.
- Original 2048px render and visible control-surface mask: `output/top_plain_2048.png`, `output/control_mask_2048.png`.
- Preview: `output/preview.png`; small-size review: `output/scale_review.png`.
- Source, pose, preservation and texture checks: `output/model_validation.json`, `output/texture_validation.json`.
- Reviewed source pose: `analysis/pose_selection.png`; style-reference hashes: `output/references.json`; visual review: `output/visual_review.json`.

The source's broad gray camouflage and light RAM-edge layout are retained through the original UVs. Fine baked scuffs are removed before assigning four fixed paint colors. The canopy is opaque muted gold; exhaust nozzles use separate fixed metal colors. No light, normal-map shading, AO, reflections or cast shadows are used. Main P-47/A-10 and Arsenal P-47/DH-4 assets were inspected for the flat silhouette treatment; their pixels are not copied into the new texture.

Source animation frame **650** provides the closed canopy, retracted landing gear and closed weapon-bay pose. All 265 source meshes, 39,040 faces, UVs and the original rig/animation remain in the editable model. A parent transform rotates the native +X nose to +Y and centers the aircraft without altering mesh coordinates or stretching its proportions. The image pivot is `(0.5,0.5)`.

The texture resolution matches the existing 1024px aircraft assets. A gameplay draw size has **not** been assigned. There is no new aircraft definition or flight/combat implementation in this artwork change.

## Regenerate

Run from the repository root with Python (Pillow/numpy) and Blender 5.1:

```powershell
python ArtSource/F22Raptor/prepare_paint.py
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background -t 6 --python-exit-code 1 --python ArtSource/F22Raptor/build.py
python ArtSource/F22Raptor/package.py
```

The aircraft-specific FBX import retains the source rig; shared `VehiclePipeline/images.py` handles linear-light compositing and premultiplied-alpha resizing, and `PropAssets/image_export.py` supplies the round silhouette outline. No ground-vehicle camera or TAN paint preset is applied. The supplied reference files are read-only inputs.
