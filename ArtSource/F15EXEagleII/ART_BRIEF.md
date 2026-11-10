# Aircraft art brief — F-15EX Eagle II

Create the supplied clean F-15EX in the established flat top-view aircraft style. Preserve the C-17/F-22 calibration and 12px outside outline. Current revision: brighter engine fairings, matched nozzles, separated controls and variable exhaust apertures.

## Evidence

- Archive/hash: `style.json`; clean airframe 13,886 source faces, hidden gear 5,762. Other loadouts omitted; no external weapons, separate tanks or targeting pods.
- Geometry/hinge evidence: supplied OBJ; no independent blueprint or measured actuator limits supplied.
- Nominal dimensions: https://www.boeing.com/defense/fighters-and-bombers/f-15ex-eagle . Uniform length 19.4m; inferred span 13.19m versus rounded 13.0m specification.
- GE F110 variable-area exhaust petals: https://www.geaerospace.com/news/articles/product/ges-f110-earns-its-way-us-air-force-f-15s .
- F-15 control-system roles (stabilators, ailerons, rudders): https://ntrs.nasa.gov/api/citations/19790015808/downloads/19790015808.pdf . This does not establish EX-specific hinge dimensions or operating limits.

## Treatment

| Part | Treatment |
| --- | --- |
| Body / inspected panels | Existing fixed gray tones and source-face paint boundaries |
| Engine upper covers | Lighter RGB 149/159/168 |
| Engine crowns | Fixed smooth vertex paint, body gray toward RGB 163/173/181 |
| Canopy / radome / upright fins | Existing opaque canopy, distinct radome and mild fixed fin brightness gain |
| Flaps / ailerons / stabilators / rudders | Eight independent surfaces and physical hinge controllers |
| Open control skins | Closed from source vertices with skin UV/color transfer, dark caps and 0.020m gaps |
| Nozzles | Right barrel and 24 leaf meshes mirrored to replace the more open source left assembly |
| Nozzle motion | Independent aperture controls, tangential leaf hinges, saved drivers, continuous variable inner seals |
| Exhaust core / inner barrel | Dark recess color replaces accidental body-colored plug |
| Crease lines | Owned by each mesh; follow surface/leaf motion |
| Gear | Original group hidden in original calibration basis |

Source geometry/UVs are verified at ingestion, then explicitly edited. Unedited faces retain geometry/UVs in the physical-coordinate basis. Revised control hulls/caps, mirrored left nozzle and new seals are recorded edits; the complete final model is no longer described as untouched source geometry. No source file is modified.

No lights, shadows, AO, reflections, gloss, source markings or baked weathering. Broad crown transitions are fixed geometric vertex colors.

## Delivery and validation

- 1222px RGBA, 12px outside outline, approximately 7.163-tile square draw size.
- `output/engine_paint_review.png`: `bf3456f0` / current engine fairings.
- `output/nozzle_review.png`: contracted / matched neutral pair / open.
- `output/articulation_review.png`, `output/articulation.gif`: neutral and moving surfaces.
- `output/model_validation.json`: hashes, source/final inventories, face partition, geometry edits, scale, pivots and independent movement checks.
- `output/articulation_validation.json`: reopened saved drivers, symmetry and joint-line ownership/return.
- `output/panel_color_validation.json`: protected source paint mapping before geometry edits and current crown treatment.
- `output/texture_validation.json`: alpha, margins, scale and export/runtime equality.
- `output/visual_review.json`: inspected final images and limits.

Preview travel (±12° control surfaces, ±10° nozzle leaves) demonstrates motion only. No game aircraft definition, runtime flight controller or in-game test is included.
