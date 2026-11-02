# Native TOW platform endpoint contract

ITowGuidanceSource.TowWireOrigin now returns System.Numerics.Vector3. Projectile_Tow consumes it directly; fixed launcher DrawPos/TurretTop and actual game callbacks remain explicit game boundaries. No old interface member/overload is retained. LauncherPlatform references and current save keys are unchanged.

```powershell
dotnet build Source/Helodrace/Helodrace.csproj -p:IntermediateOutputPath=obj/Migration17/ -v:minimal
dotnet build Source/Tests/EngineMigration/EngineMigration.csproj -v:minimal
Source/Tests/EngineMigration/Run-Migration.ps1 -Case native-tow -RuntimeRoot .codex-staging/migration17/tow-api-FRESH -NoWindow
Source/Tests/EngineMigration/Wait-Migration.ps1 -RuntimeRoot .codex-staging/migration17/tow-api-FRESH -TimeoutSeconds 45
```

Actual 1.7.4995 rev309 `native-tow-api4995`: 16 PASS/COMPLETE, observed process exit, collected exit code null. Product/Main bridge builds have zero warnings/errors. Product SHA-256 `0FB6C94AC461B8FD3E2E00D6BDFBC8B8BE4EC67E5AC4A15F78D99A8E8E0DF51D`. The only host ERROR is the known missing Workshop item 3781143823.

Existing frozen a70f624f regression passes: 256 signed angles, 1,350 steering cases, 60 actual launched flight ticks/three retargets, 96 collision cases (44 hits/52 misses), two wire obstruction cases, 1,637 draw callback pairs and 76,272 float components with maximum difference zero. Four current missiles still roundtrip exact vectors, null/empty/single/19-point wire lists, scalar state and launcher references before ticking.

The new endpoint probe adds a synthetic ThingComp implementing the real interface to a real spawned M220. Eight changing origins cover ordinary/large coordinates, signed zero, subnormal, infinity/NaN payload and height projection to zero. Both native and frozen endpoint methods preserve 96 exact component bits and perform exactly 16 provider reads. Disabled guidance, unspawned launcher and missing launcher refuse both copies without reading the origin. The frozen method marshals the new supplied native value only at its test boundary; its prior endpoint rules remain. The provider, temporary missiles and unspawned source are removed/destroyed before save probes, with no shared Def changes or test provider type saved.

This is provider-contract/CPU geometry and selected current-save evidence. The source remains a building with an injected test Comp, not an actual VF vehicle; no vehicle movement, crew/turret/ammunition behavior or VF/Lobos 1.7 compatibility is proven. Mesh/line and impact requests are captured by the existing harness. GPU/resource lifetime and performance are not measured.

Lobos' CompBradley 1.7 port must return the new native endpoint, importing only its actual VF muzzle result if that callback remains a game vector. VF-facing Bradley patches must export at their actual game return boundary; MapSmoke must compare native endpoint values and native TryGetWireEndpoints outputs. Those consumer build/runtime checks belong to the planned VF/Lobos stage, without an old Main interface shim.
