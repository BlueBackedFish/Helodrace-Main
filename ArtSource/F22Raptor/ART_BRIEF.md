# Aircraft art brief — F-22 Raptor

## Target and evidence

Create a directly overhead F-22 from the supplied FBX, with the restored broad camouflage, current palette and crisp boundaries, source-painted panels/insignia removed, and modeled external details retained. Preserve the established aircraft flat-emission style, 1024px delivery, transparent background, centered pivot, and one 12px black outside outline.

- Source path and pinned source/albedo hashes: `style.json`.
- Native axes: nose +X, up +Z, port +Y. Original physical units are unverified.
- Pose: source frame 650 closes canopy, gear and bays; new control rig is neutral at frame 1.
- Photos: USAF 8397555 / 8075211, and Mark Kubes' F-22 walkaround, especially 05, 08, 10–12, 40 and 43. Full links and usage notes: `analysis/photo_references.json`.
- Style: Main P-47/A-10 and Main/Arsenal aircraft comparison, recorded in `output/references.json`.
- No independent blueprint or surveyed dimensions were supplied. The user selected the earlier source-UV paint pattern instead of the redrawn photo-based pattern; current palette RGBs are retained and are not measured paint-chip values.

## Parts and treatment

| Part | Treatment |
| --- | --- |
| Fixed airframe / wings | Source-UV camouflage cleaned of baked panels/markings; three paint colors, closest sampling, no blur |
| Stabilators | Original source UV layout and the restored paint atlas |
| Fins / rudders | Restored source UV pattern; retained mild 1.12 linear brightness gain |
| Radome | Four-level local subdivision, fixed attachment loop and pointed tip, separate muted gray material |
| Canopy | Retained three-level curved gold dome and fixed mounting footprint |
| Cannon muzzle | Real starboard recess, floor, sloping walls and separate closed shutter |
| Canopy / shoulder screens | Four cut recesses with modeled grille strips below the skin |
| Engine bypass doors | Eight cut recesses with separate sawtooth-edged covers |
| APU intake / exhaust | Separate cut recesses and grille geometry |
| Refueling receptacle | Cut panel recess with separate closed cover |
| Moving controls | Existing eight wing/tail controls, two split rudders and two independent TVC hinges |

The sixteen new recesses are Boolean cuts through the fixed upper skin; each cut is verified with a ray probe before adding its well. Covers and grilles are modeled parts. Each recess has a one-pixel-wide surrounding band outside the original cut footprint; original opening, depth and cover dimensions are unchanged. New covers use a continuous local paint atlas sampled from the cleaned skin, preventing false stripes across source UV islands. No flat black detail decals or post-render boundary masks substitute for those cuts. Photo-informed dimensions and exact model-space footprints are recorded in the model report.

## Preservation and validation

Twenty original meshes have authorized geometry changes. The other 245 source meshes retain coordinate/topology/UV signatures, with the original source UV layers preserved. There are 346 meshes overall, including sixteen additional surrounding border bands. The original 265-mesh / 39,040-face FBX is unchanged. Original rig data and actions are retained; source actions are archived. No parts are omitted merely for being small.

- `output/paint_cleanup_validation.json` / `output/paint_cleanup_mask.png`: only selected source graphic regions are edited, with unchanged paint elsewhere.
- `output/finish_validation.json`: all 346 mesh shapes and all sixteen hole footprints/depths/borders are unchanged; no remaining vent-stencil color or wing insignia in the paint atlas.
- `output/model_validation.json`: source hash, per-source signatures, radome fitting/tip errors, canopy mounting anchors, sixteen successful cut probes, independent twelve-control motion/return checks.
- `output/texture_validation.json`: 1024px RGBA, twelve-pixel outline, transparent RGB cleanup, margins and exact runtime/output file equality.
- `output/silhouette_validation.json`: comparison with the pre-radome revision; changed contour limited to the nose.
- `output/paint_review.png`: previous and current full aircraft at equal scale.
- `output/airframe_review.png`: shutter closed/hidden, curved radome and dorsal details.
- `output/scale_review.png` and `output/preview.png`: actual 256/512px display and light/gray/dark alpha edges.
- `output/articulation_preview.gif` / `output/tvc_review.png`: model articulation inspection.

The default texture has the cannon shutter closed. Hiding its separate cover reveals the actual opening. No gameplay opening behavior or flight controller is added. Game draw size, aerodynamic limits, independent blueprint dimensions and in-game verification remain unassigned.
