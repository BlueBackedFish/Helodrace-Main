# F-15EX Eagle II — clean top-view aircraft

Created from the supplied `F15EX_Eagle_II_Lineup.zip`, using only the first clean aircraft. The sprite has no missiles, bombs, separate drop tanks or targeting pods. Original integrated airframe fittings and empty hardpoints remain. The canopy is closed and the source landing-gear group is hidden for the flight view.

- Runtime texture: `Textures/Effects/Aircraft/F15EX/HD_F15EX.png` in the repository root.
- Final export: `output/HD_F15EX.png`, **1222×1222 transparent RGBA**, one **12px black outside outline**.
- Plain / outlined exports: `output/HD_F15EX_plain.png`, `output/HD_F15EX_outlined.png`.
- Editable model: `output/f15ex_eagle_ii.blend`; original airframe and archived landing gear retain source coordinates, topology and UVs.
- Full model view: `output/model_review.png`; alpha backgrounds: `output/preview.png`; actual 256/512px sizes: `output/scale_review.png`.
- Equal physical scale with the logo-free F-22: `output/F22_F15EX_real_scale.png`.
- Source selection and geometry/scale checks: `output/selection_manifest.json`, `output/model_validation.json`, `output/texture_validation.json`, `output/visual_review.json`.

## Source and configuration

`style.json` pins the supplied archive and reference albedo hashes. The first clean airframe object is `1_F_15EX_clean__F_15EX` (13,886 faces). Its separate `1_F_15EX_clean__open` group is the 5,762-face deployed landing gear. That group is retained in the Blender file with viewport/render visibility off; it is not part of the exported flight silhouette. Other aircraft and every separate weapon/store group in the lineup are omitted. The selection manifest lists each source group and its treatment.

Source geometry is not welded, decimated or reshaped. The common `VehiclePipeline/pipeline.py` OBJ reader preserves source face IDs, polygon order and UVs. The original +Y up and 35-degree XZ heading are transformed with a uniform parent matrix to +Z up and +Y forward. Original absolute source vertex coordinates remain in the mesh datablocks. `F15EX_ART_ORIGIN` centers and calibrates the aircraft. The image pivot is `(0.5,0.5)`.

## Flat aircraft paint

The aircraft uses fixed gray emission colors, an opaque muted blue-gray canopy, a distinct radome, intake recesses, and two exhaust metal colors. Upright fins receive the same mild fixed brightness gain used for F-22. There are no lamps, AO, reflections, normal maps, gloss or cast shadows.

The supplied albedo was inspected as a reference, but its weathering, unit codes and baked surface highlights are not rendered. `panel_edges.py` instead traces actual upper-skin boundary/crease edges into a separate thin 3D line object. Short edges below 0.18m are skipped for the line overlay; no source geometry is removed. These are static visual joints, not a new control-surface animation system. All original UVs remain available for later editing.

## Physical size

Boeing lists nominal length/span of 19.4/13.0m: https://www.boeing.com/defense/fighters-and-bombers/f-15ex-eagle . The provided game model is uniformly calibrated to the nominal length. Its inferred span is about 13.19m, roughly 1.5% wider than the rounded specification; source proportions are retained rather than stretched independently.

Using the existing C-17 19-tile calibration, the F-15EX prepared square draw size is about **7.163×7.163 tiles**, recorded exactly in the model/texture reports. The 1222px canvas keeps the established approximately 170.67 pixels/tile density and 12px outside outline. It is generated from a 2048px model render, with small transparent margins. The F-22 comparison uses equal tiles/pixels, not equal sprite sizes.

## Regenerate

From the repository root:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background -t 6 --python-exit-code 1 --python ArtSource/F15EXEagleII/build.py
python ArtSource/F15EXEagleII/package.py
```

Python needs Pillow/numpy; Blender uses its bundled numpy. Source files remain read-only. The source ZIP's OBJ is read through a temporary file inside the output directory; source textures, normal maps and other loadout files are not imported as active materials.

This delivery is model/texture preparation. No aircraft definition, gameplay flight controller or in-game test is included. An independent blueprint was not supplied.
