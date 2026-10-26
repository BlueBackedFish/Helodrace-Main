Current tile-density export: 1536×1536px, drawSize (9,9), fixed 12px outline, 64° North/East/South/West cameras. The canvas grows by 256px per longest footprint tile; density and game-space outline width are identical across buildings. Vertical drawOffset is 0, compensating headroom without moving the ground anchor. Geometry and painted colors are preserved. These current exports supersede historical camera/resolution notes below. Publish with `ArtSource/IndustrialMachines/normalize_density.py`; see `ArtSource/IndustrialMachines/PRESENTATION_CALIBRATION.md`.

# Conventional steel pumpjack

New original circa-1950 pumping-unit artwork, proposed footprint 6×3. The curved horsehead and bridle, Sampson A-frame, I-section beam, twin pitmans, crank counterweights, cast reducer and belt-driven motor distinguish it from the timber basic pump. A stuffing box, polished rod, wellhead outlet and broad handwheel complete the oil-pumping identity. Neutral gray cast/machined metal with limited brass matches the industrial family. No dense gears or decorative panel clutter.

Period-family guidance includes [The History Center's circa-1944 Lufkin pumping-unit gear-assembly collection](https://www.thehistorycenteronline.com/collections/1944-pumping-unit-gear-assembly-floor-photographic-department-lufkin-foundry-machine-co-lufkin-texas-ca) and the [Engineering and Technology History Wiki pumpjack history](https://ethw.org/Pump_Jacks). It is a new game design, not a specific catalog unit reconstruction.

Build through Blender background mode with `--python ArtSource/MetalPumpJack/build_pumpjack.py`. Scene `HD_MetalPumpJack.blend`; prefix `Buildings/HD_MetalPumpJack`. The 512px North/East/South color/mask pairs use 64° orthographic cameras, scale 9, zero ground target and a 12px black unselected outline. Use drawSize (9,9), drawOffset (0,0,0) for one model unit per tile. Preview sheets and `validation.json` are generated.

There is no existing gameplay definition for a metal pumpjack in this repository. This asset supplies independent source and texture files only; no research, cost, output, power consumption or mechanical balance is invented. A future definition should use the proposed 6×3 footprint and Graphic_Multi texture prefix above. No in-game test is claimed.

Four-direction export (2026-10-26): North / East / South / West are actual 64-degree orthographic renders of the same saved geometry and colors; West is not mirrored East. See preview_four_views.png and validation.json.
