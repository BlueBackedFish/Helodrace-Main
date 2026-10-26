Current tile-density export: 1024×1024px, drawSize (6,6), fixed 12px outline, 64° North/East/South/West cameras. The canvas grows by 256px per longest footprint tile; density and game-space outline width are identical across buildings. Vertical drawOffset is 0, compensating headroom without moving the ground anchor. Geometry and painted colors are preserved. These current exports supersede historical camera/resolution notes below. Publish with `ArtSource/IndustrialMachines/normalize_density.py`; see `ArtSource/IndustrialMachines/PRESENTATION_CALIBRATION.md`.

# Cable-tool drilling rig

New 4×3 timber standard derrick: a crown sheave and suspended heavy bit, a hoisting drum/bull wheel and a low Samson-post walking beam. There is no rotary table or steel lattice mast, so it stays distinct from the newer rotary drill. The well deck has a real opening. Three broad braced bays keep the tall silhouette readable.

`build_rig.py` produces `HD_CableToolRig.blend` plus North / East / South / West color/mask pairs, all 512px transparent RGBA at 64° downward. Camera scale matches game drawSize; drawOffset and ground target are zero. The exterior outline is 12px. Timber uses baked vanilla wood colors, as with the wooden pumpjack; the black mask preserves both wood and iron colors. Woody construction cost and all drilling mechanics remain in the existing definition.

The previous North texture and mask are preserved in `Reference/`. Historical structure reference: Petroleum History Institute, [walking beam and wooden Samson post](https://www.petroleumhistory.org/OilHistory/pages/Cable/walking.html), and the [USGS cable-tool schematic](https://pubs.usgs.gov/pp/pp1713/03/pp1713_ch03.pdf). This is an original simplified model. No in-game test has been performed.
