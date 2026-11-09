# Aircraft art brief — F-22 Raptor

## Target and evidence

Create a directly overhead F-22 from the supplied FBX, with photo-informed operational gray camouflage and modeled external details. Preserve the established aircraft flat-emission style, 1024px delivery, transparent background, centered pivot, and one 12px black outside outline.

- Source path and pinned source/albedo hashes: `style.json`.
- Native axes: nose +X, up +Z, port +Y. Original physical units are unverified.
- Pose: source frame 650 closes canopy, gear and bays; new control rig is neutral at frame 1.
- Photos: USAF 8397555 / 8075211, and Mark Kubes' F-22 walkaround, especially 05, 08, 10–12, 40 and 43. Full links and usage notes: `analysis/photo_references.json`.
- Style: Main P-47/A-10 and Main/Arsenal aircraft comparison, recorded in `output/references.json`.
- No independent blueprint or surveyed dimensions were supplied. The camouflage is a photo-informed interpretation; palette RGBs are not measured paint-chip values.

## Parts and treatment

| Part | Treatment |
| --- | --- |
| Fixed airframe / wings | Broad two-tone gray lobes and thin light edge bands, stored in native point paint coordinates |
| Stabilators | Separate paint map to prevent main-wing bands crossing onto the tail |
| Fins / rudders | Gray pattern without main-wing edge bands; retained mild 1.12 linear brightness gain |
| Radome | Four-level local subdivision, fixed attachment loop and pointed tip, separate muted gray material |
| Canopy | Retained three-level curved gold dome and fixed mounting footprint |
| Cannon muzzle | Real starboard recess, floor, sloping walls and separate closed shutter |
| Canopy / shoulder screens | Four cut recesses with modeled grille strips below the skin |
| Engine bypass doors | Eight cut recesses with separate sawtooth-edged covers |
| APU intake / exhaust | Separate cut recesses and grille geometry |
| Refueling receptacle | Cut panel recess with separate closed cover |
| Moving controls | Existing eight wing/tail controls, two split rudders and two independent TVC hinges |

The sixteen new recesses are Boolean cuts through the fixed upper skin; each cut is verified with a ray probe before adding its well. Covers and grilles are modeled parts. No flat black detail decals or post-render boundary masks substitute for those cuts. Photo-informed dimensions and exact model-space footprints are recorded in the model report.

## Preservation and validation

Twenty original meshes have authorized geometry changes. The other 245 source meshes retain coordinate/topology/UV signatures, with native paint point attributes added as needed. There are 330 meshes overall. The original 265-mesh / 39,040-face FBX is unchanged. Original rig data and actions are retained; source actions are archived. No parts are omitted merely for being small.

- `output/model_validation.json`: source hash, per-source signatures, radome fitting/tip errors, canopy mounting anchors, sixteen successful cut probes, independent twelve-control motion/return checks.
- `output/texture_validation.json`: 1024px RGBA, twelve-pixel outline, transparent RGB cleanup, margins and exact runtime/output file equality.
- `output/silhouette_validation.json`: comparison with the pre-radome revision; changed contour limited to the nose.
- `output/paint_review.png`: previous and current full aircraft at equal scale.
- `output/airframe_review.png`: shutter closed/hidden, curved radome and dorsal details.
- `output/scale_review.png` and `output/preview.png`: actual 256/512px display and light/gray/dark alpha edges.
- `output/articulation_preview.gif` / `output/tvc_review.png`: model articulation inspection.

The default texture has the cannon shutter closed. Hiding its separate cover reveals the actual opening. No gameplay opening behavior or flight controller is added. Game draw size, aerodynamic limits, independent blueprint dimensions and in-game verification remain unassigned.
