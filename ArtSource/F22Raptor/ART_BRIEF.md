# Aircraft art brief — F-22 Raptor

## Target and evidence

Create a directly overhead F-22 from the supplied FBX, with the restored source-UV camouflage layout, current palette, crisp paint boundaries and modeled external details. Preserve the established aircraft flat-emission style, 1192px delivery with a prepared 6.9867-tile square display size, transparent background, centered pivot, and one 12px black outside outline.

- Source path and pinned source/albedo hashes: `style.json`.
- Native axes: nose +X, up +Z, port +Y. Original physical units are unverified.
- Pose: source frame 650 closes canopy, gear and bays; new control rig is neutral at frame 1.
- Photos: USAF 8397555 / 8075211, and Mark Kubes' F-22 walkaround, especially 05, 08, 10–12, 40 and 43. Full links and usage notes: `analysis/photo_references.json`.
- Style: Main P-47/A-10 and Main/Arsenal aircraft comparison, recorded in `output/references.json`.
- No independent blueprint or surveyed dimensions were supplied. The user selected the earlier source-UV paint pattern instead of the redrawn photo-based pattern; current palette RGBs are retained and are not measured paint-chip values.

## Parts and treatment

| Part | Treatment |
| --- | --- |
| Fixed airframe / wings | Restored source-UV camouflage; exactly four atlas colors, closest texture sampling, no blur |
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

The sixteen new recesses are Boolean cuts through the fixed upper skin; each cut is verified with a ray probe before adding its well. Covers and grilles are modeled parts. Each recess has a one-pixel-wide surrounding band outside the original cut footprint; original opening, depth and cover dimensions are unchanged. New covers inherit the underlying skin UVs. No flat black detail decals or post-render boundary masks substitute for those cuts. Photo-informed dimensions and exact model-space footprints are recorded in the model report.

## Preservation and validation

Twenty original meshes have authorized geometry changes. The other 245 source meshes retain coordinate/topology/UV signatures, with the original source UV layers preserved. There are 346 meshes overall, including sixteen additional surrounding border bands. The original 265-mesh / 39,040-face FBX is unchanged. Original rig data and actions are retained; source actions are archived. No parts are omitted merely for being small.

- `output/finish_validation.json`: earlier pattern masks match exactly, only palette values differ; all sixteen hole footprints/depths and canopy/radome are unchanged.
- `output/model_validation.json`: source hash, per-source signatures, radome fitting/tip errors, canopy mounting anchors, sixteen successful cut probes, independent twelve-control motion/return checks.
- `output/texture_validation.json`: 1192px RGBA, twelve-pixel outline, transparent RGB cleanup, margins and exact runtime/output file equality.
- `output/silhouette_validation.json`: rendered plain-alpha aspect matches native model proportions after physical framing.
- `output/physical_scale.json` / `output/comparison_validation.json`: F-22 18.9/13.6m versus C-17 53/51.75m, with the C-17 kept at 19 tiles and all resizing uniform.
- `output/F22_C17_real_scale.png`: equal meters-per-pixel comparison, excluding padding/outline from body length calibration.
- `output/paint_review.png`: previous and current full aircraft at equal scale.
- `output/airframe_review.png`: shutter closed/hidden, curved radome and dorsal details.
- `output/scale_review.png` and `output/preview.png`: actual 256/512px display and light/gray/dark alpha edges.
- `output/articulation_preview.gif` / `output/tvc_review.png`: model articulation inspection.

The default texture has the cannon shutter closed. Hiding its separate cover reveals the actual opening. No gameplay opening behavior or flight controller is added. Prepared display draw size is 6.9867×6.9867 tiles. Aerodynamic limits, independent blueprint dimensions and in-game verification remain unassigned.

The source-painted panel/insignia-removal revision was reverted at the user's request. Paint is restored exactly to `a1430830`; the subsequent changes calibrate display size and output pixel density against C-17. Interior one-pixel borders and control-joint widths are derived from the delivery resolution.

The later logo-only request removes the wing insignia in atlas bounds (276,268)–(403,344), with the source panel graphics and camouflage outside that rectangle untouched. `output/logo_validation.json` verifies the scope.
