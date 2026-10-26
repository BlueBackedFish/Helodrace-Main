Current tile-density export: 1024×1024px, drawSize (6,6), fixed 12px outline, 64° North/East/South/West cameras. The canvas grows by 256px per longest footprint tile; density and game-space outline width are identical across buildings. Vertical drawOffset is 0, compensating headroom without moving the ground anchor. Geometry and painted colors are preserved. These current exports supersede historical camera/resolution notes below. Publish with `ArtSource/IndustrialMachines/normalize_density.py`; see `ArtSource/IndustrialMachines/PRESENTATION_CALIBRATION.md`.

# Electric arc furnace

New 4×4 model based on the existing furnace's broad round shell, three electrodes, refractory base and tapping side. The rear lifting columns, separate copper arms and tilting cradle make this a related early/mid-century machine rather than a traced copy. No simulated glow, lights, PBR or surface textures are used.

`build_furnace.py` builds `HD_SAFurnace.blend` and four transparent 512px color/mask pairs in `Textures/Buildings`. North / East / South / West use orthographic cameras at 64° downward. One model unit is one game tile; `validation.json` records the matching game drawSize and ground anchor. The 12px exterior outline is unselected in the mask.

Reference: the original game texture is preserved in `Reference/`. Historical electric-furnace form is informed by the US Bureau of Mines, *Electric Furnaces for Making Iron and Steel* (1914), [UNT digital collection](https://digital.library.unt.edu/ark:/67531/metadc12279/). This is simplified original artwork, not a particular manufacturer's reproduction. No in-game test has been performed.
