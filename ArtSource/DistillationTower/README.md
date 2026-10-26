Current tile-density export: 768×768px, drawSize (4.5,4.5), fixed 12px outline, 64° North/East/South/West cameras. The canvas grows by 256px per longest footprint tile; density and game-space outline width are identical across buildings. Vertical drawOffset is 0.463665724, compensating headroom without moving the ground anchor. Geometry and painted colors are preserved. These current exports supersede historical camera/resolution notes below. Publish with `ArtSource/IndustrialMachines/normalize_density.py`; see `ArtSource/IndustrialMachines/PRESENTATION_CALIBRATION.md`.

# Compact fractionation tower

Reworked existing 3×3 distillation building, circa-1950 visual family. A tall narrow tray column with access covers and collars connects to an overhead vapor line, side condenser, reflux receiver/return and horizontal reboiler. Two partial service platforms, a coarse ladder, product valve and analog dial establish scale and function. Open skid members replace a broad empty foundation plate. Neutral cast/machined gray and limited brass match the recent reactors.

Period context: [Iowa State's pilot-plant fractionating column photograph, catalogued 1951](https://digitalcollections.lib.iastate.edu/universityphotographs/items/universityphotographs1311.html), and the [1951 U.S. Bureau of Mines distillation-laboratory report](https://fischer-tropsch.org/Bureau_of_Mines/ri/ri_4764/ri_4764_toc.htm). These guide the machinery family; the compact layout is invented for a game, not a literal laboratory or refinery reconstruction.

Build using Blender background mode with `--python ArtSource/DistillationTower/build_tower.py`. Scene `HD_DistillationTower.blend`; prefix `Buildings/HD_DistillationTower`. North/East/South share 64° downward orthographic cameras, scale/drawSize 6 and zero ground target/drawOffset. The canvas expands for the tall overhead line while keeping one model unit per game tile. Final 512px transparent color/mask pairs have 12px black unselected outlines. Original North texture/mask are packed and preserved in `Reference/`; preview sheets and `validation.json` are generated.

The existing 3×3 footprint, Graphic_Multi path, power/fuel use, recipes, research, cost and work speed are retained. Only display size/offset changes alongside artwork. No in-game test is claimed.

Four-direction export (2026-10-26): North / East / South / West are actual 64-degree orthographic renders of the same saved geometry and colors; West is not mirrored East. See preview_four_views.png and validation.json.
