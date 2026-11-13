# Wall panel system — construction milestone

2026-11-13. RimWorld 1.7. Source/art milestone; not a completed thermal, ballistic or shielding release.

## Implemented

- Blender-authored isolated, end, straight, corner, T and cross junctions: all 16 NESW masks.
- Runtime OBJ meshes and one texture atlas; actual 1.7 ObjMeshLoader coordinate/winding adaptation.
- Adjacent one-cell walls and doors select the wall connector mask. The map renderer projects source height into a facade while keeping Y within the building draw layers.
- Five buildable panels: mineral wool, expanded polystyrene, rolled armor, aramid and EMI lining.
- Existing one-cell walls and doors are valid supports. Door panels use the front/rear faces; their indicators and protection are inactive while open.
- One insulation, shielding and reinforcement slot per face. Mineral wool/foam and RHA/aramid are mutually exclusive in their slots.
- Standard blueprints, material hauling, construction, repair and removal. Target-side touch/path selection directs panel work to the exposed side.
- Saved support references persist through blueprint → frame → finished panel. Replacing a support at the same cell does not transfer its construction orders.
- Support loss destroys finished attachments; incomplete orphaned orders cannot continue and can be canceled.
- Buried faces reject panel placement. Wall construction that would bury a panel/order is rejected. Completed attachments forced into buried faces by external tools are inactive.
- Panel selection menu on the support, independent HP and directional sacrificial HP absorption for nonexplosive, nonflame damage with a known angle. Reinforcement resolves before shielding and insulation; aramid never creates ricochets.
- Lightweight sandwich wall: HP 220, construction work 33.75 versus the base wall's 135; source thickness .18 versus an assumed .24.
- Expanded polystyrene resource and a stirred-tank-reactor production recipe using existing petrochemical feedstock. Source materials and recipe references are validated; production balance remains provisional.
- English originals and Korean translations.

## Not implemented in this milestone

- Path-specific wall heat transfer and insulated closed-door heat exchange. Thermal resistance fields currently describe the intended next-stage material data; they do not yet modify temperatures.
- Directional ignition/flame reduction, burning-core fuel state and toxic smoke emission. Standard Flammability stats exist, but the smoke descriptions express the planned material behavior, not a verified gas effect.
- Ballistic penetration and physical ricochet. The initial HP layer is not an RHA penetration model and must be replaced/extended at the projectile boundary.
- Complete room shielding, roof/door/penetration checks and per-device solar-flare exemption. EMI properties do not yet confer immunity.
- Moving panel geometry on an opening door: the first-stage indicator is hidden until the door closes.
- Multi-cell support adapters, live game placement/save-load/AI work verification, and GPU/in-game image verification.

These omissions are explicit follow-up work. Do not report the full panel system or the smoke/thermal/shielding effects as complete.

## Validation

The staging integration build compiled all current Helodrace source plus the new files with zero warnings/errors. The installed game's actual assemblies were used, without stubbed game types.

`WallPanels.Tests` checks all 16 models through the native OBJ loader, triangle orientation, degeneracy, connector width and bounds; door/furniture support rules; orphan anchors; and actual Harmony patch binding/unbinding against the installed engine. A separate package audit validates new definition names, material/recipe/texture references and translation pairs. These checks do not constitute gameplay or GPU validation.

Example after building the mod:

```powershell
dotnet run --project Source/Tests/WallPanels/WallPanels.Tests.csproj -- Meshes/WallPanels
```

For an isolated output DLL pass `-p:HelodAssembly=<absolute DLL path>`.

## Native API boundaries

| File / symbol | Contract | Reason |
|---|---|---|
| `PanelGraphics.cs / Building_SandwichPanelWall.DrawAt` | `UnityEngine.Vector3` argument | Actual 1.7 protected override signature; input geometry is not calculated with that type. |
| `WallPanels.cs / Building_WallPanel.DrawAt` | `UnityEngine.Vector3` argument | Same actual game override contract. |
| `WallPanels.cs / PlaceWorker_WallPanel` | Verse buildable, cell, map and rotation types | Actual placement API. |
| `WallPanels.cs / PanelHostSelection` | `Command_Action.icon = ThingDef.uiIcon` | The game's gizmo field and def icon already use compatibility texture handles; no new Unity resource is created. |

All new mesh creation/loading, material/texture ownership, draw submission, matrices and render math use Waylith or System.Numerics APIs. Runtime meshes/materials preload after long events, then share resources for the loaded assembly lifetime. The source's 3D vertical dimension is compressed to the map draw layer and projected northward; it does not become 1.25 units of render-layer altitude.

## Next implementation order

1. Verify placement, hauling, construction, independent repair, door behavior and save/load in the actual game, including modded wall coexistence.
2. Wire per-path thermal resistance and directional fire protection into the inspected engine calculation sites.
3. Resolve RHA penetration/ricochet using the existing Ballistics model and real projectile context.
4. Add finite foam-core combustion and the custom gas-grid smoke worker.
5. Add room shielding and local solar-flare checks, then balance production/research and run live room tests.
