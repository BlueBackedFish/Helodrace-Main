Current tile-density export: 1024×1024px, drawSize (6,6), fixed 12px outline, 64° North/East/South/West cameras. The canvas grows by 256px per longest footprint tile; density and game-space outline width are identical across buildings. Vertical drawOffset is 0, compensating headroom without moving the ground anchor. Geometry and painted colors are preserved. These current exports supersede historical camera/resolution notes below. Publish with `ArtSource/IndustrialMachines/normalize_density.py`; see `ArtSource/IndustrialMachines/PRESENTATION_CALIBRATION.md`.

# Vacuum induction melting furnace

New 3×4 machine with a sealed vessel, circular access door, charging lid, short vacuum manifold, separate pump/motor and broad analog control cabinet. The original white vessel / dark pump / copper cooling connections remain recognizable. Exposed coils are omitted because they belong inside the vacuum chamber.

`build_furnace.py` creates `HD_VIMFurnace.blend` and four transparent 512px color/mask pairs at 64° downward. Ground target and game drawOffset are zero; drawSize equals camera scale as recorded in `validation.json`. Exterior outline: 12px, unselected in the mask. Broad fixed Emission colors use the same neutral industrial palette as the other machines.

The original texture is in `Reference/`. Period context: Taub and Doll, *A New Design Vacuum Melting Furnace*, Los Alamos Scientific Laboratory (1950), [catalog record](https://books.google.com/books?id=iSc-qf0rHPAC). This is an original simplified game machine, not a reconstruction of that experimental furnace. No in-game test has been performed.
