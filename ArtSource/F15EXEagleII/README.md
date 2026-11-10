# F-15EX Eagle II — clean articulated artwork

The supplied first clean aircraft is exported without missiles, bombs, separate drop tanks or targeting pods. Integrated fittings and empty hardpoints remain. The two-seat canopy is closed; original deployed gear is archived hidden in the Blender file.

- Runtime texture: `Textures/Effects/Aircraft/F15EX/HD_F15EX.png` in the repository root.
- Export: `output/HD_F15EX.png`, **1222×1222 RGBA**, one **12px outside outline**, approximately **7.163×7.163 tiles** at the shared C-17/F-22 scale.
- Editable model: `output/f15ex_eagle_ii.blend`, neutral at frame 1.
- Engine paint comparison: `output/engine_paint_review.png` (baseline `2e5cec6f`).
- Straight radome paint comparison: `output/radome_paint_review.png`.
- Exhaust apertures: `output/nozzle_review.png`; separated surfaces: `output/articulation_review.png`; motion: `output/articulation.gif`.
- Overall/alpha/size reviews: `output/model_review.png`, `output/preview.png`, `output/scale_review.png`.
- Equal physical scale with F-22: `output/F22_F15EX_real_scale.png`.

## Paint

Fixed emission materials retain the inspected wing, stabilator, inlet, carriage and mounting-interface tones in `panel_colors.json`. Engine fairing crowns now use body gray rather than a bright fill; inlet aft covers return to RGB 134/146/156. Eight inspected source edges mark the actual fold from each engine roof into the lower outboard shoulder towards the stabilators. Those edges receive a 0.040m dark line, owned by the fixed airframe. `paint_boundaries.json` pins the edge vertices and adjacent faces to the archive hash; the builder verifies the endpoints against both faces.

The radome uses its existing fixed gray with a straight rear paint boundary at physical Y=7.09m. A binary coordinate-plane mask cuts across long source triangles so their individual tips cannot create a pointed paint seam. This changes paint only, with no gradient, lighting or source-shape changes. Source weathering, markings and baked highlights remain unused.

`panel_edges.py` traces actual upper-skin boundaries/creases. Every line belongs to its own mesh, including moving controls and nozzle leaves. Generic line diameter is 0.020m on the airframe and 0.010m on exhaust petals; the eight emphasized engine folds replace their generic overlay segments. These internal joints are separate from the final 12px outside sprite outline.

## Control surfaces

`articulation.json` pins inspected source face selections to the archive hash. Eight surfaces have independent physical hinges: left/right flaps, ailerons, all-moving stabilators and rudders. The source already has separate trailing-edge wing panels and stabilators. Only the aft upper section of each vertical fin becomes a rudder; the remaining fin stays fixed.

Open wing/rudder skins are rebuilt as closed convex volumes from their original vertices, with skin UV/color transfer, dark closing faces and a 0.020m hinge gap. Fixed fin hinge closures remain on the airframe. There is no duplicate stationary wing skin underneath the moving panels. Unedited source faces retain their geometry and UVs through physical-coordinate baking and partitioning.

Each `CTRL_<surface>` rotates about **local Y**; `HINGE_<surface>` stores its physical pivot/axis. Pivots are inferred from provided model joints, not certified engineering dimensions. Frames 1/20/40/60 hold independent `DEMO_*` actions. The ±12° poses are visual demonstrations, not operating limits. Detach or mute the demo action before manual animation.

## Variable exhaust

The provided left nozzle was more open than the right. The right barrel, twelve outer petals and twelve interleaved flaps now serve as the mirrored left template, including material/UV assignments. The rear core and inner barrel are dark recesses rather than body-colored plugs.

Each engine has **24 independently hinged leaves**, with tangential hinges at their front roots. `CTRL_nozzle_left` and `CTRL_nozzle_right` expose **`aperture` 0..1**: contracted, neutral at 0.5, open. Saved Blender drivers operate the leaves without Python handlers. A continuous dark inner seal changes shape with aperture behind the detailed leaves. The engine barrel remains fixed. This is variable exit area, not whole-engine rotation. GE's F110 variable-exhaust reference: https://www.geaerospace.com/news/articles/product/ges-f110-earns-its-way-us-air-force-f-15s .

The ±10° leaf travel is an art preview range. Saved actions animate neutral/contracted/open/neutral at frames 1/20/40/60. Opening one engine does not move the other engine or any flight-control surface.

## Source, calibration and checks

`style.json` pins read-only source/archive hashes. Only `1_F_15EX_clean__F_15EX` (13,886 original faces) and hidden `1_F_15EX_clean__open` gear (5,762 faces) are ingested. Other lineup aircraft and loadouts are omitted. Coordinates, topology and UVs are checked before requested geometry edits. `output/model_validation.json` distinguishes ingested inventory from final meshes, recording face partition, edits, paint, hinges and independent movement. `output/articulation_validation.json` records a reopened-file check of saved drivers, symmetry and moving joint lines.

The original +Y-up, 35° XZ heading is uniformly calibrated to +Z up, +Y forward and **19.4m length**. The visible airframe is baked into physical metres; hidden gear keeps its original calibration parent. The inferred 13.19m span is about 1.5% wider than Boeing's rounded 13.0m specification, without independent-axis stretching: https://www.boeing.com/defense/fighters-and-bombers/f-15ex-eagle . C-17 remains 19 tiles.

`output/texture_validation.json` checks dimensions, alpha, margins and runtime/export equality. `output/visual_review.json` records inspected renders. No aircraft Def, gameplay flight controller or in-game test is included.

## Regenerate

From the repository root, with Pillow/numpy available to Python:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background -t 6 --python-exit-code 1 --python ArtSource/F15EXEagleII/build.py
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background -t 6 --python-exit-code 1 --python ArtSource/F15EXEagleII/review_articulation.py
python ArtSource/F15EXEagleII/package.py
```

The source ZIP and textures remain read-only. Packaging assembles and removes temporary motion frames. No supplied PBR texture is active in rendering.
