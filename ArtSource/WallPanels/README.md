# Wall panel textures

`render_wall_textures.py` (Blender background) rebuilds the sandwich wall junction geometry from the original union-outline rules and renders every game texture. No mesh is shipped or loaded at runtime.

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' -b --python-exit-code 1 --python ArtSource/WallPanels/render_wall_textures.py
```

| Output | Use |
|---|---|
| `Textures/Buildings/WallPanels/HD_SandwichWall_Atlas.png` (896²) | Linked wall atlas, 16 NESW states |
| `Textures/Buildings/WallPanels/HD_SandwichWall_MenuIcon.png` | Architect icon |
| `Textures/Buildings/WallPanels/Overlay/HD_Panel_<type>_{Sandwich,Wall}_{north,east,south,west}.png` (252²) | Panel overlays, drawSize 1.5 |
| `Textures/Buildings/WallPanels/HD_Panel_<type>_MenuIcon.png` | Panel icons |
| `Textures/Items/HD_ExpandedPolystyrene.png` | Foam resource |
| `Review/*.png`, `texture_validation.json` | Review sheets and checks (not game files) |

## Building texture rule review

Rules: `ArtSource/IndustrialMachines/PRESENTATION_CALIBRATION.md`, `BUILDING_COLOR_THEME.md`.

| Rule | Result |
|---|---|
| Fixed Emission colours, no lighting/shadow/AO, Standard view, transparent background | Met. The previous PBR/lit menu and panel icons were replaced. |
| Material palette | Met. Metal skin uses the theme's working silver top/front (193/180), ribs its side tone (166); exposed foam the white body (239/228); RHA vanilla Steel (105). Mineral wool, aramid and EMI keep their earlier identifying colours with the theme's top→front ratio. |
| Colour mask / stuff tint | None, like the radio: the wall and panels have fixed materials and no stuff cost. |
| 12 px black external outline (0.0703 tiles) | Met: 12 px at 168 px/tile = 0.0714 tiles. Outlines continue across connected cells; a panel adds outline only outside its host's outlined silhouette. |
| 170.67 px per game unit | Deviation: 168 px/tile (−1.6%). The engine samples the inner 0.1875 of a 4×4 atlas (offset 0.03125); 168 px is the nearest density with whole-pixel cells (896² atlas, 224 px cells, 28 px bleed). Overlays use the same density so they align with the atlas. |
| 64° four-direction cameras, drawSize 1.5 framing | Not applicable to the wall: linked walls are drawn one atlas cell per tile, so the atlas uses the linked-wall projection (plan view, height sheared north by 0.2 per unit, only top and south faces visible). Overlays use drawSize 1.5. |

Linking: `Basic`, not `CornerFiller`. The corner filler paints a square from atlas UV (0.5, 0.6) between diagonal walls, which would put solid blocks between these 0.18-thick walls.

Panel faces: a south lining covers the facade; north, east and west linings show as their top edge because the wall hides the rest in this projection. Panels on other walls and doors sit on the cell edge without occlusion, since those hosts draw flat atlases.

Not verified in game: appearance next to vanilla walls and doors, overlay altitude against pawns/items, blueprint display.
