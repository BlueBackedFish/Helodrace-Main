# Aircraft art brief — F-22 Raptor

## References and target

- Request: create a texture from the supplied F-22, looking directly down from above.
- Source: `C:/Users/human/Documents/GitHub/.texture/ref/lockheed-martin-f-22-raptor/source/Lockheed Martin F-22 «Raptor».fbx`.
- Hash and per-mesh preservation signatures: `output/model_validation.json`.
- Blueprint: not supplied; no independent dimensional/blueprint approval is claimed.
- Source axes: nose +X, up +Z, left/right span along Y. Source physical units are unverified.
- Pose: freeze source frame 650 for canopy/gear/bays, archive original actions, neutralize the flight controls, and save the new control rig at its neutral frame 1.
- Projection: orthographic, exact 90-degree top. Root rotation +90 degrees about Z makes the nose point up in the PNG; no nonuniform scale.
- Style references: Main `Textures/Effects/CAS/HD_P47_CAS.png`, Main `Textures/Effects/CAS/A10/HD_A10C.png`, and the Main/Arsenal comparison in `ArtSource/C17Globemaster/analysis/aircraft_references.png`.
- Ground-vehicle style exception: follow the established aircraft flat paint and a single aircraft silhouette, rather than ground-vehicle roof/wall shading or per-equipment black outlines.
- Delivery: 1024×1024 RGBA from a 2048px master, matching the existing fighter/support-aircraft texture canvas. Gameplay draw size is unassigned.

## Parts and paint plan

| Source part/material | Treatment | Preservation |
| --- | --- | --- |
| Main fuselage and fixed wing | Original UV camouflage/RAM layout; six true control pockets cut from the underlying fixed skin | Fixed skin outside the control footprint retained |
| Eight existing moving wing/tail panels | Connected upper skin inset with sloping joint rims; independent named hinges | Source panel shape/UV paint basis, revised rim topology |
| Upright fins and rudders | Split on the source fin hinge vertices; capped physical gaps; same camouflage with mild 1.12 linear gain | Fixed front fin and separate movable rudder |
| `Cockpit.001` glass and frame | Opaque muted gold glass, separate neutral frame | Source closed-canopy pose and geometry |
| Eight nozzle flap/lip meshes | Small modeled edge bevel; independent left/right TVC hinge | Fixed metal paint, source inner throat retained |
| Gear, internal cockpit and weapon-bay assemblies | Fixed mechanism material; normal 3D occlusion in the chosen pose | Kept in model, no automatic deletion |
| Control boundaries | Modeled upper-skin rims and rudder separation; nominal 2px rim at 1024px | No raster seam mask or painted contour |

The source starts with 265 meshes / 39,040 faces. This revision explicitly edits 19 source meshes for the requested mechanical joints and adds two rudder meshes. The remaining 246 source meshes retain their coordinate/topology/UV hashes. Control paint uses the source UVs with interpolated UVs on new boundary vertices; cut caps and rim faces have the fixed joint material. The source rig and original action data are retained, but original actions are archived and the source subsystems are frozen. The input FBX is not overwritten. `output/model_validation.json` lists the operations and before/after face counts instead of claiming that edited topology is unchanged.

## Paint and outline

Aircraft surfaces use fixed RGB/UV image to Emission. No lamps, normal shading, AO, material gloss or view-dependent color changes are present. Camouflage cleanup works in the source UV map and removes fine source wear, not aircraft geometry. The broad camouflage and RAM-edge shapes remain source-derived. Palette and reviewed control-object names are in `style.json`.

There is one 12px black aircraft silhouette, generated at double resolution and reduced with linear-light, premultiplied-alpha filtering. Internal control boundaries are real inset/bevel/cut surfaces colored with muted aircraft gray; no 2D seam image is composited. Canopy and exhaust are color/material boundaries, without separate thick black outlines. Upright fins and rudders are slightly brighter through a fixed material gain, not lighting or a global height multiplier.

## Review

| Item | Status / evidence |
| --- | --- |
| Limited geometry edits / untouched mesh preservation | 19 listed source edits, two added rudders; 246 untouched source meshes retain their geometry/UV signatures |
| Exactly top-down camera and nose-up orientation | Camera rotation `(0,0,0)`, orthographic, reviewed rendered silhouette |
| Pose selection | Source side-view samples in `analysis/pose_selection.png`; frame 650 selected |
| Flat camouflage and clear canopy/nozzles | Review final output and record in `output/visual_review.json` |
| 256px / 512px readability | `output/scale_review.png` |
| Transparent edges on light/gray/dark | `output/preview.png`, automatic transparent-RGB and margin checks |
| Independent motion and neutral return | Each of twelve controls moved by 12 degrees and returned; fixed fins and all other controls verified unchanged |
| TVC geometry motion | `output/tvc_review.png`; independent engine hinge ownership in `output/articulation.json` |
| Neutral airframe silhouette | `output/silhouette_validation.json`, compared with the initial source-derived texture |
| Independent blueprint dimensions | Not performed; blueprint not provided |
| In-game display / flight | Not performed; texture-only delivery |

## Iteration notes

The first four-color shader pass exposed isolated baked weathering as spots. The paint input was median-cleaned in UV space before four-color assignment, retaining the broad camouflage instead of adding light or shadow. Source geometry, camera, closed flight pose and the 12px outside-outline target were fixed during that initial paint iteration.

The follow-up request authorizes control-joint geometry changes. Raster boundary lines were removed. Source control panels receive real inset upper-skin rims; closed footprint prisms cut away the underlying fixed wing skin, preventing duplicate surfaces during movement. The fins were split into fixed and moving pieces along the existing rudder hinge vertices. Source nozzle flaps/lips now have independent left/right TVC controls. Camera, camouflage, opaque canopy and outside-outline thickness remain the same; control poses now start from a symmetric neutral state.
