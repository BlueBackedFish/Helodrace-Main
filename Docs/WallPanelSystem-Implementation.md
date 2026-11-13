# Wall panel system — implemented behavior

2026-11-13. RimWorld 1.7. Source/art construction milestone plus thermal, fire, ballistic and room-shielding effects.

## Construction and models

Blender-authored isolated, end, straight, corner, T and cross junctions cover all 16 NESW masks. Runtime OBJ meshes compensate for the actual native loader's X reflection and winding reversal. One atlas and shared Waylith resources are preloaded; the map view projects authored height into a short facade within the building draw layers.

Five panels attach to existing one-cell walls and doors. Doors use their front/rear faces. Each face has one insulation, shielding and reinforcement slot; mineral wool/foam and RHA/aramid compete within their respective slots. Blueprint/frame support references, material delivery, independent repair/removal, saved support identity and exposed-side work use the construction foundation. Deconstructing a support deconstructs its attachments with the normal material return; any other support loss destroys them. Blueprints and frames anchored to a removed support are cancelled and return delivered materials. Panel wear registers for ordinary repair; buried faces and construction that would bury existing panels/orders are rejected. Opening a door disables its protection and hides its first-stage panel indicator.

## Effects

- **Thermal:** additional resistance applies to the engine's individual wall-equalization paths and closed-door exchange. Mineral wool adds 3, foam adds 5, and the sandwich wall has 7 intrinsic resistance. Transfer is divided by `1 + added resistance`. An untouched path, open doorway, vent, cooler or roof pathway retains its original calculation. Consumed foam progressively loses its resistance.
- **Fire:** face-specific ignition and flame multipliers apply to fire spread and directional hits, including known bullet impact faces. Unlocated heat/ignition uses the least protected exposed face. A single lining does not make wood globally fireproof. Existing fire is not extinguished by installation. Burned-out foam no longer contributes fuel/flammability or extra ignition risk.
- **RHA:** physically described kinetic bullets use existing `HD_Armor_RHA` and KineticBulletModel for incidence, ricochet and penetration. Perforation passes reduced damage/penetration to the remaining layers and support; absorbed energy wears the plate. The baseline is 8 mm, 63 mass units and 21 rolled-steel units; these are game balance choices. Unknown rounds retain sacrificial HP behavior without invented ballistic parameters.
- **Ricochet:** a deflection launches the existing weakened `Projectile_Ricochet` from outside the struck face. Humanoid and wall impacts share its launch path. Only one generation is allowed. Aramid never enters this resolver and supplies independent sacrificial HP only.
- **Combustion:** foam panels, sandwich walls and stored EPS have finite saved unburned-core fractions. Active burning, or ignition after sustained high ambient temperature, consumes core and emits custom polymer smoke. Repair does not replenish the core. Split/merged EPS stacks conserve their remaining per-unit fuel.
- **Toxic smoke:** gas is divided among reachable neighboring air cells, not duplicated per exposed side or emitted only into a solid wall cell. Walls emit on both exposed sides; an attached panel starts on its own face. The existing gas grid handles diffusion, closed-door boundaries, dissipation, rendering and exposure ticks. Smoke causes a dedicated inhalation hediff, breathing impairment and toxic buildup; existing gas/exposure resistance remains effective.
- **Room shielding:** every inward-facing boundary must have completed EMI lining, all cells must be roofed, and doors must be lined and closed. Unadapted vents/coolers and other boundary openings invalidate the enclosure. Every occupied cell of a protected device must belong to the same protected room. Pending region rebuilds fail closed.
- **Solar flare:** protected electrical equipment can operate on its own available power. Outside generators and batteries cannot energize it during the flare. The actual network solver remains in use; individual power state, output, battery acceptance and transfer are filtered. Direct native facility/automatic-power checks receive the same local exception. The map condition is never removed, and unrelated blackouts and EMP gates remain intact.

Room results are invalidated on lining, structure, roof and room changes. Door changes invalidate only rooms whose reports depend on that door; unsuccessful close attempts do not flush every room each tick. Inspection shows actual panel resistance, per-axis wall transfer, core remaining, shielding coverage and local flare protection.

## Limits and validation scope

- This is not a multi-cell support adapter or a Combat Extended projectile adapter. Unsupported projectile physics use the ordinary HP path.
- Shielding does not grant world-radio/support-call immunity, bypass the native comms-console external communications checks, stop EMP weapons or block smoke.
- Complete roofs represent the ceiling/floor shielding abstraction; separate conductive floor/ceiling tiles are not required.
- Door indicator geometry is still hidden during opening rather than animated with the door leaf.
- Native engine fixture tests are not an interactive played save or GPU rendering validation. Full game placement/AI workflow, long-running performance, visible smoke/PPE playtesting and final balance still require live verification.

The full integration build uses the actual installed game/Harmony/Waylith assemblies. `WallPanels.Tests` validates the 16 native-loaded meshes, patch binding, real room/edifice/roof grids, per-path heat changes through the patched engine method, sacrificial HP direction, normal/grazing physical impacts, finite core and native Scribe round-trip, stack conservation, smoke air-cell routing, enclosure breaks, native network ticks, battery isolation and native automatic/facility power checks. A narrow test condition supplies map applicability so a console fixture does not bootstrap the user's DLC/ModsConfig. No replacement game classes or GPU are used.

```powershell
dotnet run --project Source/Tests/WallPanels/WallPanels.Tests.csproj -- Meshes/WallPanels .
```

For isolated builds pass `-p:HelodAssembly=<absolute DLL path>`. The optional second positional argument audits the panel/effect XML and English/Korean arguments against actual engine fields/types.

## Native boundaries

`Building_SandwichPanelWall.DrawAt` and `Building_WallPanel.DrawAt` keep the installed override's `UnityEngine.Vector3` parameter. Internal rendering uses System.Numerics and Waylith meshes, textures, materials and Graphics. The gizmo icon is assigned from the existing game `ThingDef.uiIcon` handle.

`PanelBulletScope` reads the protected game's compatibility `Projectile.origin` field and converts it once to System.Numerics. `Projectile_Ricochet` converts its final launch origin to the game's actual `Projectile.Launch` signature. No Unity resource creation or internal math is added. `DamageInfo.armorPenetrationInt` is updated through the actual struct field because the public game property is read-only.

`PanelWallHeatTransfer` preserves the installed engine's sampling, fallback, vacuum and normalization logic. Its transpiler replaces exactly one direct-air read and one outdoor fallback read with path-aware reads; a changed engine contract fails validation rather than silently scaling entire room temperatures. See `WallPanelSystem-Effects.md` for tuning and callback details.
