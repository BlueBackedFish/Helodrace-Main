# F-22 Raptor — flat top-view artwork

The supplied FBX is the geometry reference. The aircraft points up in a true 90-degree orthographic view, with fixed emission paint and one outside silhouette. The current revision uses photo-referenced two-tone gray camouflage, a refined radome, and real recessed airframe details.

- Runtime texture: `Textures/Effects/Aircraft/F22/HD_F22.png` in the repository root; **1024×1024 transparent RGBA, 12px outside outline**.
- Editable model: `output/f22_raptor.blend`; all three native-space paint maps are packed inside.
- Paint plan: `paint.json`; maps: `output/fuselage_main_paint.png`, `output/fuselage_tail_paint.png`, `output/fuselage_fin_paint.png`.
- Plain and outlined exports: `output/HD_F22_plain.png`, `output/HD_F22_outlined.png`; 2048px master: `output/top_plain_2048.png`.
- Whole-aircraft comparison: `output/paint_review.png`; close-ups: `output/airframe_review.png`; actual-size review: `output/scale_review.png`.
- Model, texture, silhouette and visual checks: `output/model_validation.json`, `output/texture_validation.json`, `output/silhouette_validation.json`, `output/visual_review.json`.

## Paint and references

The broad gray lobes and narrow light edge bands were redrawn from USAF aerial photographs, checked against Mark Kubes' exterior walkaround. This replaces the initial thresholded source-albedo pattern. Main wings and stabilators use separate paint maps so a wing edge stripe cannot cross onto a tail surface. Named point attributes store the paint in the neutral native model coordinates; it stays attached when controls move. The original UV layers remain available.

The pattern is an artwork interpretation of operational F-22 camouflage, not a surveyed stencil for one serial number. RGB values are chosen for the established flat aircraft style, not claimed as measured Federal Standard paint values. Metallic reflections and weathering in the photographs are not reproduced as shading. The opaque gold canopy and the mild 1.12 linear gain on upright fins/rudders remain. No lights, AO, normal maps, reflections or cast shadows are used.

Reference links, photographers, uses and cached USAF-photo hashes are in `analysis/photo_references.json`. The local Main P-47/A-10 and Arsenal P-47/DH-4 style-reference records remain in `output/references.json`.

## Geometry

`airframe.py` separates the existing nose at its rear cross-section and refines it with four subdivision levels. The original rear fitting and pointed nose tip are fixed; the radome now has 9,921 vertices and 9,728 faces. Its original overall length is retained. The exact measured seam error and tip error are recorded in the model report.

Sixteen additional details are cut into the fixed upper skin: a starboard cannon muzzle recess, paired canopy purge screens and intake-shoulder vents, eight engine bypass-door recesses, APU intake/exhaust, and a refueling-receptacle panel. Each has actual sidewalls and an inset floor. Grilles have separate geometry below the skin. Closed panels have separate covers. Every cut is checked by ray-casting the fixed airframe to verify that the original surface was removed. Locations and depths are photo-informed native model units, not engineering measurements.

The cannon shutter is **closed by default**. Hide `DETAIL cannon muzzle cover` to inspect the modeled opening. `output/airframe_review.png` shows both states. This is model preparation; no game firing/shutter controller is implemented.

The canopy retains its previous three-level refinement, from 84 to 4,897 vertices. Its eighteen outside mounting vertices remain fixed. The source's closed-canopy, retracted-gear and closed-bay pose comes from frame 650. Those subsystems are frozen; the source rig and original actions are retained, with the actions archived. The new controls save in neutral at frame 1.

Twenty source meshes have deliberate coordinate/topology revisions; the other **245 source meshes** retain their geometry/UV signatures. The source starts with 265 meshes / 39,040 faces. The current model has **330 meshes**, including separate rudders, radome, wells, covers and grille strips. Extra native paint attributes are added without overwriting source UV layers. The input FBX is not changed. A uniform parent transform rotates native +X to +Y and centers the aircraft; the image pivot remains `(0.5,0.5)`.

## Model controls

There are twelve named `CTRL_*` objects: left/right leading-edge flap, flap, aileron, stabilator, rudder and TVC nozzle. Each has a separate `HINGE_*` frame storing its real hinge position and axis. Rotate `CTRL_*.rotation_euler[1]` in radians; zero is neutral. `output/articulation.json` records pivots, axes and mesh ownership. Do not rotate around the texture center.

Wing controls have inset upper-skin rims and true pockets in the fixed skin underneath. The fixed front fin stays still while its separate rudder turns. Each TVC control carries its upper/lower nozzle flaps and lips; the inner throat stays fixed. Demonstration actions use frames 1/20/40/60 for neutral/negative/positive/neutral. The twelve-degree excursions are inspection poses, not aerodynamic limits. See `output/articulation_preview.gif` and `output/tvc_review.png`.

## Regenerate

Run from the repository root with Python (Pillow/numpy) and Blender 5.1:

```powershell
python ArtSource/F22Raptor/prepare_paint.py
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background -t 6 --python-exit-code 1 --python ArtSource/F22Raptor/build.py
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background -t 6 --python-exit-code 1 --python ArtSource/F22Raptor/render_articulation.py
python ArtSource/F22Raptor/package.py
```

`articulation.py` builds physical joints and named controls from `articulation.json`; `canopy.py` refines the dome; `airframe.py` refines the radome and cuts the details. Shared `VehiclePipeline/images.py` handles premultiplied-alpha resizing, and `PropAssets/image_export.py` supplies the round outside outline. There is no painted control-seam mask. Ground-vehicle camera/TAN presets are not applied.

Gameplay draw size remains unassigned. No aircraft definition, flight implementation or in-game test is part of this art revision. Independent blueprints were not supplied.
