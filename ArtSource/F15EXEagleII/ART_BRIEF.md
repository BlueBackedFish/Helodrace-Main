# Aircraft art brief — F-15EX Eagle II

## Request and references

Create an unarmed F-15EX from the locally supplied model, using the established Main/Arsenal aircraft flat top-view style. Use the shared C-17/F-22 physical scale, one 12px outside outline, transparent background and centered nose-up image.

- Source archive: `.texture/ref/f-15ex-eagle-ii-all-loadouts-broken-arrow/source/F15EX_Eagle_II_Lineup.zip`; pinned absolute path and SHA256 in `style.json`.
- Selected airframe: `1_F_15EX_clean__F_15EX`; source landing gear kept hidden as `1_F_15EX_clean__open`.
- External armament / separate drop tanks / targeting pods: none; other lineup groups are omitted.
- Geometry evidence: provided game model. Independent blueprint: not provided.
- Primary nominal dimensions and appearance reference: https://www.boeing.com/defense/fighters-and-bombers/f-15ex-eagle . Boeing rear-engine photo was inspected for twin-exhaust appearance; it is not redistributed.
- Flat emission style and physical calibration: local F-22 and C-17 artwork; the C-17 stays at 19 tiles.

## Paint and geometry

| Part | Treatment |
| --- | --- |
| Airframe | Constant gray emission, original source geometry and UVs |
| Canopy | Opaque muted blue-gray; original two-seat canopy/frame geometry |
| Radome | Separate fixed gray, original shape |
| Twin exhaust | Separate inner/outer metal colors; source petal geometry |
| Intake recesses | Fixed darker gray |
| Upright fins | Mild fixed brightness gain, no lighting |
| Surface joints | Thin curves on actual upper-skin crease/boundary edges, not source-image scratches |
| Landing gear | Original deployed group archived hidden in viewport/render |
| Weapons / stores | No separate lineup store group retained |

The visible body retains all 13,886 source faces. The hidden gear retains 5,762. Source coordinates, polygon order and UVs are preserved in mesh data. One uniform parent transform centers the aircraft, rotates the original +Y-up / 35-degree heading to the exact +Z top view, and calibrates length to 19.4m. The inferred width is about 13.19m, 1.5% wider than Boeing's rounded nominal value; no independent-axis distortion is applied.

Source albedo markings, baked lighting and weathering are not used as paint. Separate curve geometry traces physical joint edges. The curve follows the same parent transform as the source body. Source triangles are not welded or removed to create the line overlay.

## Delivery and validation

- 1222px RGBA, 12px outside outline, approximately 7.163-tile square prepared display size.
- `output/model_validation.json`: archive/OBJ hashes, retained source face IDs, original coordinate/order checks, clean-group selection, hidden gear, uniform physical transform, span difference and no external armament.
- `output/selection_manifest.json`: every source group and visible/hidden/omitted status.
- `output/texture_validation.json`: transparent RGB cleanup, bounds, margins, runtime/export equality and prepared draw size.
- `output/model_review.png`: exact top and oblique inspection; no external weapons, tanks or pods visible.
- `output/scale_review.png`: 256px/512px actual display; `output/preview.png`: light/gray/dark alpha edges.
- `output/F22_F15EX_real_scale.png`: same physical scale with the existing F-22.

No gameplay aircraft definition, control-surface animation implementation or in-game validation is claimed.
