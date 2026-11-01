# Native turret graphic geometry checks

Build Main and the Main engine bridge and run serially against actual RimWorld 1.7, Harmony 3 and mandatory Blancas:

```powershell
dotnet build Source/Helodrace/Helodrace.csproj -p:IntermediateOutputPath=obj/Migration17/ -v:minimal
dotnet build Source/Tests/EngineMigration/EngineMigration.csproj -v:minimal
Source/Tests/EngineMigration/Run-Migration.ps1 -Case native-turret-graphics -RuntimeRoot .codex-staging/migration17/turrets-native17 -NoWindow
Source/Tests/EngineMigration/Wait-Migration.ps1 -RuntimeRoot .codex-staging/migration17/turrets-native17 -TimeoutSeconds 45
```

Use a fresh runtime. An observation timeout leaves the game running; continue waiting on that recorded PID/runtime rather than starting another instance.

The three FrozenTurret_* files copy 31668478 properties/comp bodies into an isolated namespace. External Harmony patch classes are excluded. The frozen TOW helper uses the unchanged current TowLoadedGraphicExtension from the actual XML Defs, instead of shadowing that type and accidentally hiding the real extension. Product visibility is not widened. Existing native scalar aliases remain shared as before.

The product stores internal positions/rotations/scales in BCL values and uses Waylith quaternion math/native TRS, converting matrices at Graphics.DrawMesh. M167's current game ref drawLoc is imported, added to its already-native center offset and exported once. The authored M167 Vector2 XML field/default remains a game parse contract. Material/Shader/texture/mesh/cache ownership and the current game rendering callback signatures remain host boundaries. No ammo, installation, firing, recoil timing, RNG or save rules change.

Coverage:

- CROWS: 3,528 paired PostDraw calls, covering every actual CrowsWeaponDef and unarmed/re-arm transitions, all four base facings, six top angles, three controlled origins and seven recoil ages (-1/0/1/3/5/6/7). Actual NotifyWeaponChanged/NotifyShotFired methods execute; all declared cache/state fields are compared as well as layer order, every matrix component and exact mesh/material references. Seven weapon-change cache transitions are exercised. The fixture sets installedWeapon presentation state directly: it does not run installation, costs, remote control, ammo or gun replacement flows.
- TOW: 1,008 paired calls across loaded/unloaded/reloaded state, actual shell graphics, a cloned missing-line extension and a cloned absent extension, four facings, six top angles, three controlled origins and normal/missing base-layer paths. The actual gun loader object is used; its shell/count presentation state is constructed. Independent comp properties preserve source Defs. Missing layers still preserve the original explicit layer-cursor increments and cache state. Real reload jobs/ammo consumption are outside scope.
- Reciprocating M2HB: 6,048 pairs across three scale/offset/recoil profiles (including zero/negative scale and zero recoil), recoil durations 0/1/4/7, four facings, six angles, three controlled origins and seven ages. Actual notification and scalar state are compared. Three additional wrong-parent/no-top guard pairs must submit nothing.
- The existing NativeM167Checks.Run is rerun before installing the new fixture's hooks. It directly exercises the changed drawLoc addition and its native center-offset boundary: 84 profiles, 756 layer/casing draw callbacks, 392 sound submissions, 252 expirations and the 128-casing cap. The original frozen M167 baseline is 0b8f5ab3, as recorded by that existing check.

Actual spawned CROWS/TOW/M2HB Things, tops, current gun/loader, Defs and material caches are used. DrawPos inputs are controlled through the base Thing getter; current Building/ThingWithComps/TurretGun/CROWS do not override it (checked in the matching metadata). Mesh submissions are intercepted before rendering. These are CPU matrix/layer/cache/state comparisons, not GPU images, shader/resource lifetime, actual control/reload/fire jobs, external IAnimatedTurret implementations or performance. The tick is anchored at 10,000 and restored; fixture Things/Harmony hooks and position overrides are removed at completion. The numerical comparator uses existing relative 2e-5 criteria and reports actual maximum/nonzero differences; discrete state/resource identity/order/count uses exact comparison.

Verified 2026-11-02: actual 1.7.4995 rev309 native-turret-graphics-4995 passed ten engine checks. New fixture: 10,587 pairs, 28,728 mesh submissions, 47,944 state fields and 459,648 float components, maximum difference zero and no nonzero differences. Independent M167 regression compared 127,970 additional components with maximum error zero. COMPLETE and process termination observed; collected exit code null. The only ERROR was the known missing Workshop item. Product/Main/Blancas17 builds had zero warnings/errors. Product string literals and copied frozen bodies match the baseline under the stated namespace/scaffold changes.

Product DLL SHA-256: 59C935748539E46E330DE050A486CCDC0FC172DE4198FBB946B43091786EA4CF. Fresh matching-PDB IL audit finds no remaining geometry calls in the three converted components; M167 has only the current XML Vector2.zero default. The broader unfinished queue is tracked in Docs/1.7 remaining native calculations.md. Other math/color/source boundaries, Blancas B2 and R7 remain ongoing.
