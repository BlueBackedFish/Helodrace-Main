Current tile-density export: 512×512px, drawSize (3,3), fixed 12px outline, 64° North/East/South/West cameras. Tabletop-anchored drawOffset Z is -0.182008177. Publish with `ArtSource/IndustrialMachines/normalize_density.py -- HD_ProductionPlanningDesk`; see `ArtSource/IndustrialMachines/PRESENTATION_CALIBRATION.md`.

# Production planning desk

New 2×1 sprite-proxy desk for `HD_ProductionPlanningDesk`, replacing the recolored basic-workbench picture. The working side faces -Y (North view). A three-drawer pedestal with brass pulls sits on the left, two legs and a foot rail on the right, and a modesty panel at the rear. The desktop carries an unrolled blueprint with drawn shaft and plate outlines in the center, a clipboard production schedule on the right, a red ledger, a pencil cup and a green-shaded desk lamp. A cork planning board on two posts stands at the rear (+Y) with a four-column kanban grid: red, yellow, green and blue column headers over paper cards with colored tabs.

This is furniture, so it follows the radio/tailoring/gun-bench furniture rules rather than the vehicle or industrial-machine palette: fixed Emission colors, no lights or shadows, restrained face shading (top / ±Y front / ±X side differ by about eight RGB levels), and painted surfaces rather than black holes. The top uses vanilla `WoodLog.stuffProps.color` (133,97,67); the pedestal, legs and rails use the darker 217/255 edge ratio (113,83,57), as on the radio desk. Like the other furniture (`FIXED` in `normalize_density.py`, `EXCLUDED` in `building_theme.py`), it has no tint selection: every mask pixel is black, so construction stuff never recolors the wood or the paper. The former `graphicData/color` tint was removed.

The center anchor is the desktop mesh `01_Desktop` (`ArtSource/BuildingAlignment` rule), so the tabletop sits on the footprint center in all four directions. One model unit is one game tile.

Rebuild and publish from the repository root:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' -b --python-exit-code 1 --python ArtSource/ProductionPlanningDesk/build_planning_desk.py
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' -b --python-exit-code 1 --python ArtSource/IndustrialMachines/normalize_density.py -- HD_ProductionPlanningDesk
```

Outputs: `Textures/Buildings/HD_ProductionPlanningDesk_{north,east,south,west}.png` with all-black `…m.png` masks, `preview_four_views.png`, `preview_north_east_south.png`, `preview_256px.png` and `validation.json`. The export checks 64° cameras, a minimum 12px margin and matching color/mask alpha. All four masks were measured at RGB ≤ 1/255.

Verification (2026-11-06): actual 1.7 run `.codex-staging/ProductionPlanning/desk-art-01` (`Start-Checks.ps1 -Perf -Visible`) loaded the definition, spawned four rotated desks with loaded textures and captured `desk-rotations.png`. `IndustrialMachines/verify_rotation.py` currently stops on the gameplay-field hash of every older asset (their definitions changed after art integration); the new desk's own hash matches. No hand-played session has been done.
