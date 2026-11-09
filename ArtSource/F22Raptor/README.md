# F-22 Raptor — flat top-view texture

Created from the FBX supplied in `.texture/ref/lockheed-martin-f-22-raptor`. The aircraft points up in a true 90-degree orthographic view. It now has physical control joints, separate rudders and independent left/right thrust-vectoring nozzle controls. Internal boundaries come from the model, with one black outline around the complete aircraft.

- Game texture: `Textures/Effects/Aircraft/F22/HD_F22.png` in the repository root. **1024×1024 transparent RGBA, 12px outside outline**. No 2D control-seam mask is painted over the render.
- Matching output: `output/HD_F22.png`.
- Editable model: `output/f22_raptor.blend`, with its four-color UV paint image packed inside.
- Plain/outlined exports: `output/HD_F22_plain.png`, `output/HD_F22_outlined.png`.
- UV paint map: `output/fuselage_flat_uv.png`.
- 2048px model render: `output/top_plain_2048.png`.
- Preview: `output/preview.png`; small-size review: `output/scale_review.png`.
- Source, pose, preservation and texture checks: `output/model_validation.json`, `output/texture_validation.json`.
- Reviewed source pose: `analysis/pose_selection.png`; style-reference hashes: `output/references.json`; visual review: `output/visual_review.json`.
- Hinge positions, axes, mesh ownership and independent-movement checks: `output/articulation.json`.
- Model motion preview: `output/articulation_preview.gif`; nozzle close-ups: `output/tvc_review.png`.

The source's broad gray camouflage and light RAM-edge layout are retained through the source UV paint. Fine baked scuffs are removed before assigning four fixed paint colors. The upright fins and rudders retain that pattern with a mild 1.12 linear color gain (roughly 7–9 extra sRGB levels). The canopy is opaque muted gold; exhaust nozzles use separate fixed metal colors. No light, normal-map shading, AO, reflections or cast shadows are used. Main P-47/A-10 and Arsenal P-47/DH-4 assets were inspected for the flat silhouette treatment; their pixels are not copied into the new texture.

Source frame **650** supplies the closed canopy, retracted gear and closed bays. Those subsystems are frozen in that pose; the source rig remains and its original actions are archived as fake-user actions. Small source flight-control deflections are neutralized. The current model saves at **frame 1**, the neutral frame of the new control demonstration.

The user-requested geometry revision edits 19 source meshes: eight control surfaces, the two fins split into fixed/moving portions, eight nozzle flap/lip meshes, and the fixed fuselage. Wing controls have real inset/beveled rims. Six watertight pocket cutters remove the fixed wing skin beneath the leading-edge flaps, flaps and ailerons so a stationary duplicate is not exposed when they move. Rudder hinge gaps are capped model cuts. The remaining **246 source meshes** retain their coordinates, topology and UVs; two new rudder meshes make **267 meshes** overall. The input FBX remains unchanged. A parent transform still rotates the native +X nose to +Y and centers the aircraft without nonuniform scale. The image pivot remains `(0.5,0.5)`.

## Model controls

There are twelve named `CTRL_*` objects: left/right leading-edge flap, flap, aileron, stabilator, rudder and TVC nozzle. Each has a separate `HINGE_*` frame storing the real hinge position and axis. **Rotate `CTRL_*.rotation_euler[1]` in radians**; zero is neutral. Do not rotate around the texture center. `output/articulation.json` records the native pivots/axes and owned meshes; `output/model_validation.json` records the native-to-centered-model transform.

The upright fin's front portion stays fixed while its separated rudder turns on the swept/canted hinge. Each TVC control carries the corresponding upper/lower nozzle flaps and lips, with independent engine controls. The fixed interior throat remains on the airframe. Demonstration actions `DEMO_F22_*` use frames 1/20/40/60 for neutral/negative/positive/neutral. Their 12-degree excursions are inspection poses, not specified aerodynamic limits. They are model preparation for future gameplay animation.

The texture resolution matches the existing 1024px aircraft assets. A gameplay draw size has **not** been assigned. There is no new aircraft definition or flight/combat implementation in this artwork change.

## Regenerate

Run from the repository root with Python (Pillow/numpy) and Blender 5.1:

```powershell
python ArtSource/F22Raptor/prepare_paint.py
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background -t 6 --python-exit-code 1 --python ArtSource/F22Raptor/build.py
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background -t 6 --python-exit-code 1 --python ArtSource/F22Raptor/render_articulation.py
python ArtSource/F22Raptor/package.py
```

The aircraft-specific FBX import retains the source rig. `articulation.py` builds the physical joints and named controls from `articulation.json`. Shared `VehiclePipeline/images.py` handles premultiplied-alpha resizing, and `PropAssets/image_export.py` supplies the round outside outline. The obsolete control-mask render was removed. No ground-vehicle camera or TAN paint preset is applied. The supplied reference files are read-only inputs.
