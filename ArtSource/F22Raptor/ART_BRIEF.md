# Aircraft art brief — F-22 Raptor

## References and target

- Request: create a texture from the supplied F-22, looking directly down from above.
- Source: `C:/Users/human/Documents/GitHub/.texture/ref/lockheed-martin-f-22-raptor/source/Lockheed Martin F-22 «Raptor».fbx`.
- Hash and per-mesh preservation signatures: `output/model_validation.json`.
- Blueprint: not supplied; no independent dimensional/blueprint approval is claimed.
- Source axes: nose +X, up +Z, left/right span along Y. Source physical units are unverified.
- Pose: source frame 650; side-view samples confirm closed canopy, retracted gear and closed bays.
- Projection: orthographic, exact 90-degree top. Root rotation +90 degrees about Z makes the nose point up in the PNG; no nonuniform scale.
- Style references: Main `Textures/Effects/CAS/HD_P47_CAS.png`, Main `Textures/Effects/CAS/A10/HD_A10C.png`, and the Main/Arsenal comparison in `ArtSource/C17Globemaster/analysis/aircraft_references.png`.
- Ground-vehicle style exception: follow the established aircraft flat paint and a single aircraft silhouette, rather than ground-vehicle roof/wall shading or per-equipment black outlines.
- Delivery: 1024×1024 RGBA from a 2048px master, matching the existing fighter/support-aircraft texture canvas. Gameplay draw size is unassigned.

## Parts and paint plan

| Source part/material | Treatment | Preservation |
| --- | --- | --- |
| Main fuselage, wings, stabilators and fins | Original UV camouflage/RAM layout reduced to four coherent gray colors | All source faces and UVs retained |
| `Cockpit.001` glass and frame | Opaque muted gold glass, separate neutral frame | Source closed-canopy pose and geometry |
| `Nozzle*` | Fixed metal, lip and interior roles | All original meshes |
| Gear, internal cockpit and weapon-bay assemblies | Fixed mechanism material; normal 3D occlusion in the chosen pose | Kept in model, no automatic deletion |
| Reviewed control surfaces | 2px muted gray boundary where actually visible | Mask from the original posed scene |

The source 265 meshes / 39,040 faces are preserved. Source mesh topology, vertex coordinates and UV loops are hashed before/after painting. Shared source mesh data may be copied to keep material assignment independent; geometry is identical. The original rig and its animation are retained. The input FBX is not overwritten.

## Paint and outline

Aircraft surfaces use fixed RGB/UV image to Emission. No lamps, normal shading, AO, material gloss or view-dependent color changes are present. Camouflage cleanup works in the source UV map and removes fine source wear, not aircraft geometry. The broad camouflage and RAM-edge shapes remain source-derived. Palette and reviewed control-object names are in `style.json`.

There is one 12px black aircraft silhouette, generated at double resolution and reduced with linear-light, premultiplied-alpha filtering. Internal control boundaries use the existing muted aircraft-gray approach. Canopy and exhaust are color/material boundaries, without separate thick black outlines.

## Review

| Item | Status / evidence |
| --- | --- |
| Source mesh/UV preservation | Automated comparison in `output/model_validation.json` |
| Exactly top-down camera and nose-up orientation | Camera rotation `(0,0,0)`, orthographic, reviewed rendered silhouette |
| Pose selection | Source side-view samples in `analysis/pose_selection.png`; frame 650 selected |
| Flat camouflage and clear canopy/nozzles | Review final output and record in `output/visual_review.json` |
| 256px / 512px readability | `output/scale_review.png` |
| Transparent edges on light/gray/dark | `output/preview.png`, automatic transparent-RGB and margin checks |
| Independent blueprint dimensions | Not performed; blueprint not provided |
| In-game display / flight | Not performed; texture-only delivery |

## Iteration notes

The first four-color shader pass exposed isolated baked weathering as spots. The paint input is now median-cleaned in UV space before four-color assignment, retaining the broad camouflage instead of adding light or shadow. The source geometry, camera, closed flight pose and 12px outside-outline target remain fixed.
