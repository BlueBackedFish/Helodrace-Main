# Collins radio desk

New 1×1 sprite-proxy desk with simplified Collins 32V-3 transmitter (left), 75A-3 receiver (right), and a tabletop microphone. Shallow wedge cabinets and panels tilted 45° toward the overhead view replace the large empty lids. Broad dark rims, meter ticks, dial hubs/pointers, two switches, panel grips and three broad vent slots give the radios readable detail. The microphone has a rounded head, five grille slots, central grille spine, fork mount and oval foot. The paper note has been removed. These are deliberately stylized cabinet shapes. Baked wood base RGB is `(133,97,67)`, from vanilla `WoodLog.stuffProps.color`. Case faces differ by only eight RGB levels, panel faces by eight, and microphone metal faces by sixteen; wood side shading is also restrained. Dark trim and actual grille/vent features retain contrast. No color mask is produced.

Outputs: `Textures/Buildings/HD_CollinsRadioDesk_{north,east,south}.png`, transparent 512×512 RGBA. North, East and South share an orthographic scale of 1.47 and an elevation of 64° downward from horizontal. South shows the operating faces; North shows the cabinet backs. West can use the engine's mirrored East view. The scene is `collins_radio_desk.blend`.

For a future building definition use size `(1,1)`, `Graphic_Multi`, texPath `Buildings/HD_CollinsRadioDesk`, and white graphic color with no stuff tint. This change supplies artwork only and does not add a building definition.

Rebuild from the repository root:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' -b --python ArtSource/CollinsRadioDesk/build_radio_desk.py
```

Reference identification: [32V-3](https://www.universal-radio.com/catalog/hamhf/32v3.html), [75A-3](https://www.universal-radio.com/catalog/commrxvr/75a3.html). Cabinet proportions and controls are simplified for sprite readability; this is not an engineering reconstruction. Geometry and flat materials are newly authored.

The build reopens the saved scene and validates camera elevation, image dimensions and transparent framing; results are recorded in `validation.json`.

`revise_radio_style.py` updates the radios and microphone, removes the note, and reduces wood face shading in the existing saved scene while preserving desk geometry and cameras. `preview_128px.png` shows the three directions at 128 pixels per view for readability review. No in-game verification has been performed.

Painting reference: `ArtSource/VEHICLE_ART_STYLE.md` sections 4–6 and 14, plus `M2A3Bradley/EmissionTrial/README.md`. Furniture borrows the surface principles, not the vehicle palette: fixed Emission colors, restrained directional differences, painted rims rather than black holes, and one color around each small cylinder barrel with independently colored end caps. Dark grille slots, knob rubber and recessed panel beds are intentional material exceptions. No global height ranking or automatic minimum contrast is applied to unrelated parts. Compared with `93679faa`, this pass changes radio rims and cylinder face assignments only; camera and geometry remain identical. The existing 128px review and 512px renders cover sprite readability; no game verification is implied.
