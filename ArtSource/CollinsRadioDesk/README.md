Current tile-density export: 256×256px, drawSize (1.5,1.5), fixed 12px outline, 64° North/East/South/West cameras. The canvas grows by 256px per longest footprint tile; density and game-space outline width are identical across buildings. Vertical drawOffset is 0.154591769, compensating headroom without moving the ground anchor. Geometry and painted colors are preserved. These current exports supersede historical camera/resolution notes below. Publish with `ArtSource/IndustrialMachines/normalize_density.py`; see `ArtSource/IndustrialMachines/PRESENTATION_CALIBRATION.md`.

# Collins radio desk

New 1×1 sprite-proxy desk with simplified Collins 32V-3 transmitter (left), 75A-3 receiver (right), a tabletop microphone, and a dual-paddle telegraph key at the front left. Rectangular cabinets have vertical front panels, with the existing meter, dial and switch detail layout preserved. Lid and rear ventilation slots are dark regions of the case surface itself, with no raised vent bars or overlay geometry. Broad dark rims, meter ticks, dial hubs/pointers, two switches, panel grips and three broad vent slots give the radios readable detail. Both radios are shifted 0.16 units toward the rear, leaving a 0.095-unit margin behind the cabinets. The microphone uses only a rounded head, three grille bars, a straight stem and an oval foot; the fork, pivots, grille spine, collar, button and base inset have been removed. The paper note has been removed. These are deliberately stylized cabinet shapes. Baked wood base RGB is `(133,97,67)`, from vanilla `WoodLog.stuffProps.color`. Case faces differ by only eight RGB levels, panel faces by eight, and microphone metal faces by sixteen; wood edges and legs share baked RGB `(113,83,57)`, using the TelegraphTable reference ratio of 217/255 against the vanilla wood top. Dark trim and actual grille/vent features retain contrast. No color mask is produced.

Active game outputs are `Textures/Buildings/HD_Radio_{north,east,south,west}.png`, transparent 256px RGBA, using the existing `HD_Radio` definition. North shows the operating faces (-Y); South shows the cabinet backs (+Y). West is a real render, not mirrored East. All cameras are orthographic at 64° downward with scale 1.47. The active drawSize remains 1.5. The scene is `collins_radio_desk.blend`.

The North/South correction exchanged the original active PNG bytes exactly; hashes are in `direction_correction.json`. `publish_active.py` adds West and synchronizes camera names without altering meshes or colors. `preview_active_four_views.png` and `active_validation.json` describe the current active exports. Legacy 512px previews below are historical; they are not the game-facing files.

Rebuild from the repository root:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' -b --python ArtSource/CollinsRadioDesk/build_radio_desk.py
```

Reference identification: [32V-3](https://www.universal-radio.com/catalog/hamhf/32v3.html), [75A-3](https://www.universal-radio.com/catalog/commrxvr/75a3.html). Cabinet proportions and controls are simplified for sprite readability; this is not an engineering reconstruction. Geometry and flat materials are newly authored.

The build reopens the saved scene and validates camera elevation, image dimensions and transparent framing; results are recorded in `validation.json`.

`revise_radio_style.py` updates the radios and microphone, removes the note, and reduces wood face shading in the existing saved scene while preserving desk geometry and cameras. `preview_128px.png` shows the three directions at 128 pixels per view for readability review. No in-game verification has been performed.

Painting reference: `ArtSource/VEHICLE_ART_STYLE.md` sections 4–6 and 14, plus `M2A3Bradley/EmissionTrial/README.md`. Furniture borrows the surface principles, not the vehicle palette: fixed Emission colors, restrained directional differences, painted rims rather than black holes, and one color around each small cylinder barrel with independently colored end caps. Dark grille slots, knob rubber and recessed panel beds are intentional material exceptions. No global height ranking or automatic minimum contrast is applied to unrelated parts. The earlier paint-only pass at `0a372ab3` changed radio rims and cylinder face assignments relative to `93679faa`; its `paint_validation.json` is a historical record, before the subsequent microphone simplification and rearward radio move. The existing 128px review and 512px renders cover sprite readability; no game verification is implied.


Desk shading reference: HD_TelegraphTable_south.png, sampled at image coordinates top (300,330), edge (200,361), and leg (160,392): RGB 255, 217, 217 respectively. recolor_desk.py changes only desk materials and checks that geometry, cameras and device colors are preserved; desk_color_validation.json records the result. No mask is needed.

The dual-paddle key uses a broad dark foot, gray base, paired metal levers/pivots and two large black finger pieces. Its controls face the working side (-Y); add_telegraph_key.py updates only this addition in the saved scene. It is a simplified paddle key, inspired by the general dual-lever layout rather than an exact product model. Form reference: https://museumkesher.org.il/en/catalog/bencher-by-1-black-base-iambic/ .

Render the current saved model without rebuilding: run Blender in background with --python ArtSource/CollinsRadioDesk/render_views.py. This writes the three PNGs, previews and validation.json while leaving the Blender scene file unchanged.

To publish after rebuilding, run Blender with `--python ArtSource/CollinsRadioDesk/publish_active.py -- --all`. This exports the active HD_Radio prefix at 256px with a 6px outline (12px at the industrial 512px reference size), and no color mask. The historical build/render scripts write the older Collins artwork prefix; publish_active.py is the current game integration step.
